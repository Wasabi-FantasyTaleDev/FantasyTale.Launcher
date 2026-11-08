#include "fthook/engine.h"
#include "fthook/log.h"
#include "fthook/memory.h"
#include "fthook/semver.h"
#include "fthook/native_plugin.h"

#include <windows.h>

#include <algorithm>
#include <set>

namespace fthook {

bool LocatorRegistry::add(const std::string& name, LocatorFn fn) {
    if (name.empty() || !fn || locators_.contains(name)) return false;
    locators_.emplace(name, std::move(fn));
    return true;
}

const LocatorFn* LocatorRegistry::find(const std::string& name) const {
    const auto it = locators_.find(name);
    return it == locators_.end() ? nullptr : &it->second;
}

void LocatorRegistry::remove(const std::string& name) { locators_.erase(name); }

const char* to_string(Outcome outcome) noexcept {
    switch (outcome) {
    case Outcome::Installed: return "installed";
    case Outcome::AlreadyPresent: return "already-present";
    case Outcome::SkippedOption: return "skipped-option";
    case Outcome::UnavailableBuild: return "unavailable-build";
    case Outcome::SignatureMismatch: return "signature-mismatch";
    case Outcome::Timeout: return "timeout";
    case Outcome::Failed: return "failed";
    case Outcome::RollbackFailed: return "rollback-failed";
    }
    return "failed";
}

const char* activation_error(const std::vector<PluginReport>& reports) noexcept {
    bool required_failed = false;
    for (const auto& report : reports) {
        if (report.outcome == Outcome::RollbackFailed) return "patch-restoration";
        const bool ok = report.outcome == Outcome::Installed || report.outcome == Outcome::AlreadyPresent
            || report.outcome == Outcome::SkippedOption;
        if (report.required && !ok) required_failed = true;
    }
    return required_failed ? "required-plugin" : "";
}

namespace {
bool fail(PluginReport& report, Outcome outcome, std::string detail) {
    report.outcome = outcome;
    report.detail = std::move(detail);
    return false;
}

bool check_option(const OptionSpec& spec, const OptionValue& value, std::string& detail) {
    switch (spec.type) {
    case OptionSpec::Type::Bool:
        if (!std::holds_alternative<bool>(value)) return detail = "option '" + spec.key + "' must be a boolean", false;
        return true;
    case OptionSpec::Type::Int: {
        if (!std::holds_alternative<int64_t>(value)) return detail = "option '" + spec.key + "' must be an integer", false;
        const int64_t v = std::get<int64_t>(value);
        if (v < spec.min || v > spec.max) return detail = "option '" + spec.key + "' is out of range", false;
        return true;
    }
    case OptionSpec::Type::String:
    case OptionSpec::Type::Enum: {
        if (!std::holds_alternative<std::string>(value)) return detail = "option '" + spec.key + "' must be a string", false;
        const std::string& v = std::get<std::string>(value);
        if (v.size() > spec.max_length) return detail = "option '" + spec.key + "' is too long", false;
        if (spec.type == OptionSpec::Type::Enum && std::find(spec.values.begin(), spec.values.end(), v) == spec.values.end())
            return detail = "option '" + spec.key + "' is not one of its values", false;
        return true;
    }
    }
    return false;
}

std::optional<uintptr_t> code_address(const ImageInfo& image, uintptr_t address, size_t size) {
    for (const Region& r : image.code)
        if (address >= r.address && size <= r.size && address - r.address <= r.size - size) return address;
    return std::nullopt;
}
}

std::optional<PreparedPlugin> prepare(PluginManifest manifest, const SessionPlugin& entry, const std::string& build,
    const std::string& branch, PluginReport& report, const std::string& client_language) {
    report.id = entry.id;
    report.required = entry.required;
    if (manifest.id != entry.id) return fail(report, Outcome::Failed, "plugin.toml id is '" + manifest.id + "'"), std::nullopt;
    if (manifest.version != entry.version)
        return fail(report, Outcome::Failed, "plugin.toml version is " + manifest.version + ", session expects " + entry.version), std::nullopt;
    if (!manifest.runtime_range.empty()) {
        const auto ok = satisfies(*parse_semver(FTHOOK_VERSION_STRING), manifest.runtime_range);
        if (!ok) return fail(report, Outcome::Failed, "invalid runtime range"), std::nullopt;
        if (!*ok) return fail(report, Outcome::Failed, "needs runtime " + manifest.runtime_range), std::nullopt;
    }
    if (!any_selector_matches(manifest.builds, build, branch) || any_selector_matches(manifest.exclude, build, branch))
        return fail(report, Outcome::UnavailableBuild, "not available on " + build), std::nullopt;

    PreparedPlugin prepared;
    prepared.required = entry.required;
    report.required = prepared.required;
    for (const OptionSpec& spec : manifest.options) prepared.options[spec.key] = spec.default_value;
    for (const auto& [key, value] : entry.options) {
        const auto spec = std::find_if(manifest.options.begin(), manifest.options.end(), [&](const OptionSpec& o) { return o.key == key; });
        if (spec == manifest.options.end()) return fail(report, Outcome::Failed, "unknown option '" + key + "'"), std::nullopt;
        std::string detail;
        if (!check_option(*spec, value, detail)) return fail(report, Outcome::Failed, detail), std::nullopt;
        prepared.options[key] = value;
    }
    prepared.manifest = std::move(manifest);

    std::set<std::string> ids;
    bool gated = false;
    for (const PatchSpec& patch : prepared.manifest.patches) {
        if (!patch.builds.empty() && !any_selector_matches(patch.builds, build, branch)) continue;
        const auto matches_language = [&](const std::string& language) {
            const auto lower = [](unsigned char c) { return c >= 'A' && c <= 'Z' ? c + ('a' - 'A') : c; };
            return language.size() == client_language.size() && std::equal(language.begin(), language.end(), client_language.begin(),
                [&](unsigned char a, unsigned char b) { return lower(a) == lower(b); });
        };
        if ((!patch.client_languages.empty() && !std::any_of(patch.client_languages.begin(), patch.client_languages.end(), matches_language))
            || std::any_of(patch.exclude_client_languages.begin(), patch.exclude_client_languages.end(), matches_language))
        { gated = true; continue; }
        if (!patch.when.empty() && !std::get<bool>(prepared.options.at(patch.when))) { gated = true; continue; }
        if (!ids.insert(patch.id).second)
            return fail(report, Outcome::Failed, "patch '" + patch.id + "' has overlapping variants for " + build), std::nullopt;
        prepared.patches.push_back(&patch);
    }
    if (prepared.patches.empty() && prepared.manifest.kind != "native")
        return fail(report, gated ? Outcome::SkippedOption : Outcome::UnavailableBuild, gated ? "disabled by options" : "no patch for " + build),
               std::nullopt;
    return prepared;
}

Probe probe(const PreparedPlugin& plugin, const ImageInfo& image, const LocatorRegistry& locators, std::vector<uintptr_t>& sites,
    std::string& detail) {
    sites.assign(plugin.patches.size(), 0);
    for (size_t i = 0; i < plugin.patches.size(); ++i) {
        const PatchSpec& patch = *plugin.patches[i];
        uintptr_t address = 0;
        if (patch.locate.pattern) {
            const ScanResult found = scan(image.code, *patch.locate.pattern, patch.locate.unique ? 2 : 1);
            if (found.count == 0) return detail = "patch '" + patch.id + "': pattern not found", Probe::NotYet;
            if (found.count > 1) return detail = "patch '" + patch.id + "': pattern is not unique", Probe::Mismatch;
            address = found.first;
        } else if (patch.locate.rva) {
            address = image.base + *patch.locate.rva;
        } else {
            const LocatorFn* locator = locators.find(patch.locate.locator);
            if (!locator) return detail = "patch '" + patch.id + "': unknown locator '" + patch.locate.locator + "'", Probe::Mismatch;
            std::string why;
            const auto located = (*locator)(image, why);
            if (!located) return detail = "patch '" + patch.id + "': " + why, Probe::NotYet;
            address = *located;
        }
        address += static_cast<uintptr_t>(static_cast<intptr_t>(patch.locate.offset));
        if (patch.import_slot ? !is_import_slot(address) : !code_address(image, address, patch.write.size()))
            return detail = "patch '" + patch.id + "': site is not executable yet", Probe::NotYet;
        Pattern already{patch.write, std::vector<uint8_t>(patch.write.size(), 0xFF)};
        if (!matches_at(address, patch.expect) && !matches_at(address, already)) {
            detail = "patch '" + patch.id + "': unexpected bytes at site";
            // A fixed address in a packed image holds ciphertext until the unpacker is done.
            return patch.locate.rva && patch.wait_unpacked ? Probe::NotYet : Probe::Mismatch;
        }
        sites[i] = address;
        for (size_t j = 0; j < i; ++j) {
            if (address < sites[j] + plugin.patches[j]->write.size() && sites[j] < address + patch.write.size())
                return detail = "patch '" + patch.id + "': overlaps another patch", Probe::Mismatch;
        }
    }
    return Probe::Ready;
}

Outcome apply(const PreparedPlugin& plugin, const std::vector<uintptr_t>& sites, std::string& detail, bool* rollback_failed) {
    struct Write { uintptr_t address; Pattern original; Pattern installed; bool changed = false; bool import_slot = false; };
    std::vector<Write> writes;
    writes.reserve(plugin.patches.size());
    if (rollback_failed) *rollback_failed = false;
    if (sites.size() != plugin.patches.size()) { detail = "patch site count mismatch"; return Outcome::Failed; }
    // Allocate the complete undo journal before the first instruction is changed.
    for (size_t i = 0; i < plugin.patches.size(); ++i) {
        const PatchSpec& patch = *plugin.patches[i];
        std::vector<uint8_t> original(patch.write.size());
        if (!read_memory(sites[i], original.data(), original.size())) {
            detail = "patch '" + patch.id + "': cannot read original bytes";
            return Outcome::Failed;
        }
        if (original != patch.write && !patch.expect.matches(original.data())) {
            detail = "patch '" + patch.id + "': unexpected bytes while recording originals";
            return Outcome::SignatureMismatch;
        }
        writes.push_back({sites[i], Pattern{std::move(original), std::vector<uint8_t>(patch.write.size(), 0xFF)},
            Pattern{patch.write, std::vector<uint8_t>(patch.write.size(), 0xFF)}, false, patch.import_slot});
    }
    bool wrote = false;
    for (size_t i = 0; i < plugin.patches.size(); ++i) {
        const PatchSpec& patch = *plugin.patches[i];
        // Recheck the exact journal snapshot, including manifest wildcard bytes, so
        // rollback never replaces a newer value with an older captured original.
        const WriteResult result = patch.import_slot ? write_import(sites[i], writes[i].original, patch.write)
            : write_code(sites[i], writes[i].original, patch.write);
        if (result == WriteResult::Installed) {
            wrote = true;
            writes[i].changed = true;
            continue;
        }
        if (result == WriteResult::AlreadyPresent) continue;
        bool incomplete = result == WriteResult::RollbackFailed;
        for (auto it = writes.rbegin(); it != writes.rend(); ++it) {
            if (!it->changed) continue;
            const auto undone = it->import_slot ? write_import(it->address, it->installed, it->original.bytes)
                : write_code(it->address, it->installed, it->original.bytes);
            if (undone != WriteResult::Installed && undone != WriteResult::AlreadyPresent) {
                incomplete = true;
            }
        }
        detail = "patch '" + patch.id + "': " + to_string(result);
        if (incomplete) detail += "; patch bytes, cache or protection could not be restored";
        if (rollback_failed) *rollback_failed = incomplete;
        if (incomplete) return Outcome::RollbackFailed;
        return result == WriteResult::Mismatch ? Outcome::SignatureMismatch : Outcome::Failed;
    }
    return wrote ? Outcome::Installed : Outcome::AlreadyPresent;
}

std::vector<PluginReport> run_session(const Session& session, LocatorRegistry& locators,
    const std::function<std::optional<ImageInfo>()>& describe, unsigned poll_ms) {
    struct AppliedRange {
        uintptr_t begin = 0;
        uintptr_t end = 0;
        std::string plugin;
        std::string patch;
    };
    std::vector<AppliedRange> applied_ranges;
    std::vector<PluginReport> reports;
    bool restoration_failed = false;
    const auto session_deadline = GetTickCount64() + session.startup_timeout_ms;
    auto image = describe();
    std::string image_problem;
    if (!image) image_problem = "main image unreadable";
    else if (session.pe_timestamp && *session.pe_timestamp != image->pe_timestamp) image_problem = "fingerprint mismatch (pe_timestamp)";
    else if (session.image_size && *session.image_size != image->image_size) image_problem = "fingerprint mismatch (image_size)";

    for (const SessionPlugin& entry : session.plugins) {
        PluginReport report;
        report.id = entry.id;
        report.required = entry.required;
        if (restoration_failed) {
            fail(report, Outcome::Failed, "session stopped after incomplete patch restoration");
            reports.push_back(std::move(report));
            continue;
        }
        if (!session.launch_id.empty() && GetTickCount64() >= session_deadline) {
            fail(report, Outcome::Timeout, "session initialization deadline exceeded");
            reports.push_back(std::move(report));
            continue;
        }
        if (!image_problem.empty()) {
            fail(report, Outcome::Failed, image_problem);
            reports.push_back(std::move(report));
            continue;
        }
        std::string error;
        auto manifest = load_manifest(entry.path, error);
        if (!manifest) {
            fail(report, Outcome::Failed, error);
            reports.push_back(std::move(report));
            continue;
        }
        if (manifest->game != session.game) {
            fail(report, Outcome::UnavailableBuild, "plugin is for game '" + manifest->game + "'");
            reports.push_back(std::move(report));
            continue;
        }
        auto prepared = prepare(std::move(*manifest), entry, session.build, session.branch, report, session.client_language);
        reports.push_back(report);
        if (!prepared) continue;
        std::unique_ptr<NativePlugin> native;
        if (prepared->manifest.kind == "native") {
            native = NativePlugin::start(*prepared, entry, session, locators, error);
            if (!native) { fail(reports.back(), Outcome::Failed, error); continue; }
        }
        uint32_t wait = 0;
        bool retry = false;
        for (const PatchSpec* p : prepared->patches)
            if (p->wait_unpacked) { retry = true; wait = std::max(wait, p->deadline_ms); }
        const ULONGLONG deadline = GetTickCount64() + wait;
        // Finish a provider before a later plugin can use one of its locators.
        for (;;) {
            if (!session.launch_id.empty() && GetTickCount64() >= session_deadline) {
                fail(reports.back(), Outcome::Timeout, "session initialization deadline exceeded");
                break;
            }
            image = describe();
            PluginReport& result = reports.back();
            std::vector<uintptr_t> sites;
            std::string detail;
            Probe state;
            try { state = image ? probe(*prepared, *image, locators, sites, detail) : Probe::NotYet; }
            catch (const std::exception& e) {
                fail(result, Outcome::Failed, e.what());
                break;
            }
            if (state == Probe::NotYet && retry && GetTickCount64() < deadline) { Sleep(poll_ms); continue; }
            if (state == Probe::Ready) {
                // Reject shared patch ownership before a later plugin changes another byte.
                // Even identical writes conflict so cleanup cannot invalidate another owner.
                bool conflict = false;
                for (size_t i = 0; i < prepared->patches.size() && !conflict; ++i) {
                    const PatchSpec& patch = *prepared->patches[i];
                    const uintptr_t address = sites[i];
                    const size_t size = patch.write.size();
                    if (!address || !size || address > UINTPTR_MAX - size) {
                        fail(result, Outcome::Failed, "patch '" + patch.id + "': invalid patch range");
                        conflict = true;
                        break;
                    }
                    const uintptr_t end = address + size;
                    for (const auto& prior : applied_ranges) {
                        if (address < prior.end && prior.begin < end) {
                            fail(result, Outcome::Failed, "patch '" + patch.id + "': overlaps patch '"
                                + prior.patch + "' from plugin '" + prior.plugin + "'");
                            conflict = true;
                            break;
                        }
                    }
                }
                if (conflict) {
                    native.reset();
                    break;
                }
                bool rollback_failed = false;
                result.outcome = apply(*prepared, sites, result.detail, &rollback_failed);
                restoration_failed = result.outcome == Outcome::RollbackFailed;
                if (native && (result.outcome == Outcome::Installed || result.outcome == Outcome::AlreadyPresent
                    || result.outcome == Outcome::Failed || rollback_failed)) {
                    if (result.outcome == Outcome::Failed || rollback_failed)
                        result.detail += "; native DLL retained because patch restoration could not be guaranteed";
                    if (prepared->patches.empty()) result.outcome = Outcome::Installed;
                    NativePlugin::retain(std::move(native), result.outcome == Outcome::Installed || result.outcome == Outcome::AlreadyPresent);
                }
                if (result.outcome == Outcome::Installed || result.outcome == Outcome::AlreadyPresent) {
                    for (size_t i = 0; i < prepared->patches.size(); ++i) {
                        const PatchSpec& patch = *prepared->patches[i];
                        applied_ranges.push_back({sites[i], sites[i] + patch.write.size(), prepared->manifest.id, patch.id});
                    }
                }
            }
            else fail(result, state == Probe::NotYet ? Outcome::Timeout : Outcome::SignatureMismatch, detail);
            break;
        }
    }
    return reports;
}

} // namespace fthook

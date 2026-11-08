#include "fthook/manifest.h"
#include "fthook/semver.h"
#include "toml_util.h"

#include <windows.h>

#include <algorithm>
#include <set>

namespace fthook {
using detail::Errors;

std::wstring widen(const std::string& text) {
    if (text.empty()) return {};
    const int n = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
    std::wstring out(static_cast<size_t>(n), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), n);
    return out;
}

std::string narrow(const std::wstring& text) {
    if (text.empty()) return {};
    const int n = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string out(static_cast<size_t>(n), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), n, nullptr, nullptr);
    return out;
}

bool read_text_file(const std::wstring& path, size_t max_bytes, std::string& out) {
    // Readers observe one immutable snapshot without blocking an atomic file replacement.
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER size{};
    bool ok = GetFileSizeEx(file, &size) != FALSE;
    if (ok && (size.QuadPart < 0 || static_cast<unsigned long long>(size.QuadPart) > max_bytes)) {
        SetLastError(ERROR_FILE_TOO_LARGE);
        ok = false;
    }
    if (ok) {
        out.resize(static_cast<size_t>(size.QuadPart));
        DWORD done = 0;
        ok = out.empty() || ReadFile(file, out.data(), static_cast<DWORD>(out.size()), &done, nullptr);
        if (ok && !out.empty() && done != out.size()) { SetLastError(ERROR_HANDLE_EOF); ok = false; }
    }
    const DWORD error = ok ? ERROR_SUCCESS : GetLastError();
    CloseHandle(file);
    SetLastError(error);
    if (ok && out.size() >= 3 && static_cast<unsigned char>(out[0]) == 0xEF && static_cast<unsigned char>(out[1]) == 0xBB
        && static_cast<unsigned char>(out[2]) == 0xBF)
        out.erase(0, 3);
    return ok;
}

bool selector_matches(const std::string& selector, const std::string& build, const std::string& branch) {
    if (selector == "*") return true;
    if (selector.rfind("branch:", 0) == 0) return !branch.empty() && selector.substr(7) == branch;
    return selector == build;
}

bool any_selector_matches(const std::vector<std::string>& selectors, const std::string& build, const std::string& branch) {
    for (const auto& s : selectors)
        if (selector_matches(s, build, branch)) return true;
    return false;
}

namespace {
bool valid_id(const std::string& id) {
    if (id.empty() || id.size() > 96) return false;
    for (char c : id)
        if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-')) return false;
    return true;
}

bool parse_localized(const toml::table& t, std::string_view key, const std::string& path, std::map<std::string, std::string>& out,
    Errors& e, bool required) {
    const toml::table* table = detail::get_table(t, key, path, e);
    if (!table) return required ? e.fail(detail::join(path, key), "missing") : e.ok();
    for (auto&& [lang, node] : *table) {
        const auto* s = node.as_string();
        if (!s) return e.fail(detail::join(path, key) + "." + std::string(lang.str()), "must be a string");
        out[std::string(lang.str())] = s->get();
    }
    if (!out.contains("en")) return e.fail(detail::join(path, key), "needs an 'en' entry");
    return true;
}

bool parse_option(const toml::table& t, const std::string& path, OptionSpec& o, Errors& e) {
    if (!detail::only_keys(t, {"key", "type", "default", "min", "max", "max_length", "values", "label", "description"}, path, e)) return false;
    std::string type;
    if (!detail::get_string(t, "key", path, o.key, e, true) || !detail::get_string(t, "type", path, type, e, true)) return false;
    std::map<std::string, std::string> label, description;
    if (!parse_localized(t, "label", path, label, e, true) || !parse_localized(t, "description", path, description, e, false)) return false;
    const toml::node* def = t.get("default");
    if (!def) return e.fail(path + ".default", "missing");
    if (type == "bool") {
        o.type = OptionSpec::Type::Bool;
        if (!def->as_boolean()) return e.fail(path + ".default", "must be a boolean");
        o.default_value = def->as_boolean()->get();
    } else if (type == "int") {
        o.type = OptionSpec::Type::Int;
        std::optional<int64_t> min, max;
        if (!detail::get_int(t, "min", path, min, e) || !detail::get_int(t, "max", path, max, e)) return false;
        o.min = min.value_or(INT64_MIN);
        o.max = max.value_or(INT64_MAX);
        if (!def->as_integer()) return e.fail(path + ".default", "must be an integer");
        const int64_t v = def->as_integer()->get();
        if (o.min > o.max || v < o.min || v > o.max) return e.fail(path + ".default", "outside min..max");
        o.default_value = v;
    } else if (type == "string" || type == "enum") {
        o.type = type == "string" ? OptionSpec::Type::String : OptionSpec::Type::Enum;
        std::optional<int64_t> max_length;
        if (!detail::get_int(t, "max_length", path, max_length, e, 1, 65536) || !detail::get_strings(t, "values", path, o.values, e)) return false;
        o.max_length = static_cast<size_t>(max_length.value_or(256));
        if (!def->as_string()) return e.fail(path + ".default", "must be a string");
        const std::string v = def->as_string()->get();
        if (o.type == OptionSpec::Type::Enum && (o.values.empty() || std::find(o.values.begin(), o.values.end(), v) == o.values.end()))
            return e.fail(path + ".default", "must be one of values");
        if (v.size() > o.max_length) return e.fail(path + ".default", "longer than max_length");
        o.default_value = v;
    } else {
        return e.fail(path + ".type", "must be bool, int, string or enum");
    }
    return true;
}

bool read_client_languages(const toml::table& t, const std::string& path,
    std::vector<std::string>& included, std::vector<std::string>& excluded, Errors& e) {
    if (!detail::get_strings(t, "client_languages", path, included, e)
        || !detail::get_strings(t, "exclude_client_languages", path, excluded, e)) return false;
    std::set<std::string> include_tags;
    for (const bool excluding : {false, true}) {
        std::set<std::string> seen;
        const auto key = path + (excluding ? ".exclude_client_languages" : ".client_languages");
        for (const auto& language : excluding ? excluded : included) {
            auto normalized = language;
            std::transform(normalized.begin(), normalized.end(), normalized.begin(), [](unsigned char c) {
                return static_cast<char>(c >= 'A' && c <= 'Z' ? c + ('a' - 'A') : c);
            });
            if (language.empty() || language.size() > 32 || !seen.insert(normalized).second
                || !std::all_of(language.begin(), language.end(), [](unsigned char c) {
                    return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-'; }))
                return e.fail(key, "requires distinct language tags");
            if (excluding && include_tags.contains(normalized)) return e.fail(key, "must not overlap client_languages");
            if (!excluding) include_tags.insert(normalized);
        }
    }
    return true;
}

bool parse_patch(const toml::table& t, const std::string& path, PatchSpec& p, Errors& e) {
    if (!detail::only_keys(t, {"id", "builds", "when", "locate", "expect", "write", "timing", "deadline_ms", "client_languages", "exclude_client_languages"}, path, e)) return false;
    std::string expect, write, timing = "unpacked";
    std::optional<int64_t> deadline;
    if (!detail::get_string(t, "id", path, p.id, e, true) || !detail::get_strings(t, "builds", path, p.builds, e)
        || !read_client_languages(t, path, p.client_languages, p.exclude_client_languages, e)
        || !detail::get_string(t, "when", path, p.when, e) || !detail::get_string(t, "expect", path, expect, e, true)
        || !detail::get_string(t, "write", path, write, e, true) || !detail::get_string(t, "timing", path, timing, e)
        || !detail::get_int(t, "deadline_ms", path, deadline, e, 0, 600000))
        return false;
    if (timing != "unpacked" && timing != "immediate") return e.fail(path + ".timing", "must be unpacked or immediate");
    p.wait_unpacked = timing == "unpacked";
    p.deadline_ms = static_cast<uint32_t>(deadline.value_or(60000));

    std::string error;
    auto expected = parse_pattern(expect, error);
    if (!expected) return e.fail(path + ".expect", error);
    auto written = parse_pattern(write, error);
    if (!written) return e.fail(path + ".write", error);
    if (!written->is_exact()) return e.fail(path + ".write", "must not contain wildcards");
    if (written->size() != expected->size()) return e.fail(path + ".write", "must have the same length as expect");
    p.expect = std::move(*expected);
    p.write = std::move(written->bytes);

    const toml::table* locate = detail::get_table(t, "locate", path, e);
    if (!locate) return e.ok() ? e.fail(path + ".locate", "missing") : false;
    const std::string lpath = path + ".locate";
    if (!detail::only_keys(*locate, {"pattern", "match", "rva", "locator", "offset"}, lpath, e)) return false;
    std::string pattern, match = "unique";
    std::optional<int64_t> rva, offset;
    if (!detail::get_string(*locate, "pattern", lpath, pattern, e) || !detail::get_string(*locate, "match", lpath, match, e)
        || !detail::get_int(*locate, "rva", lpath, rva, e, 0, UINT32_MAX) || !detail::get_string(*locate, "locator", lpath, p.locate.locator, e)
        || !detail::get_int(*locate, "offset", lpath, offset, e, -65536, 65536))
        return false;
    const int kinds = (pattern.empty() ? 0 : 1) + (rva ? 1 : 0) + (p.locate.locator.empty() ? 0 : 1);
    if (kinds != 1) return e.fail(lpath, "needs exactly one of pattern, rva, locator");
    if (match != "unique" && match != "first") return e.fail(lpath + ".match", "must be unique or first");
    p.locate.unique = match == "unique";
    p.locate.offset = static_cast<int32_t>(offset.value_or(0));
    if (!pattern.empty()) {
        auto parsed = parse_pattern(pattern, error);
        if (!parsed) return e.fail(lpath + ".pattern", error);
        p.locate.pattern = std::move(*parsed);
    }
    if (rva) {
        if (p.builds.size() != 1 || p.builds[0] == "*" || p.builds[0].rfind("branch:", 0) == 0)
            return e.fail(lpath + ".rva", "requires builds to name exactly one build");
        p.locate.rva = static_cast<uint32_t>(*rva);
    }
    return true;
}
}

std::optional<PluginManifest> parse_manifest(const std::string& toml_text, const std::string& source, std::string& error) {
    toml::parse_result parsed = toml::parse(toml_text, source);
    if (!parsed) {
        error = source + ": " + std::string(parsed.error().description());
        return std::nullopt;
    }
    const toml::table& root = parsed.table();
    Errors e;
    PluginManifest m;
    std::optional<int64_t> schema;
    detail::only_keys(root, {"schema", "plugin", "compatibility", "options", "patches", "native", "companion", "files"}, "", e);
    detail::get_int(root, "schema", "", schema, e);
    if (e.ok() && schema != 1) e.fail("schema", "must be 1");

    const toml::table* plugin = detail::get_table(root, "plugin", "", e);
    if (e.ok() && !plugin) e.fail("plugin", "missing");
    if (e.ok()) {
        detail::only_keys(*plugin, {"id", "aliases", "version", "game", "kind", "runtime", "name", "description", "warning", "authors", "license", "homepage", "update_source", "minimum_launcher_version", "diagnostics", "restore_files"}, "plugin", e);
        std::string minimum_launcher;
        detail::get_string(*plugin, "minimum_launcher_version", "plugin", minimum_launcher, e);
        if (e.ok() && !minimum_launcher.empty() && (minimum_launcher.size() > 128 || !parse_semver(minimum_launcher)))
            e.fail("plugin.minimum_launcher_version", "must be a SemVer version");
        std::vector<std::string> authors;
        std::string ignored;
        bool diagnostics = false;
        detail::get_bool(*plugin, "diagnostics", "plugin", diagnostics, e);
        detail::get_string(*plugin, "update_source", "plugin", ignored, e);
        std::map<std::string, std::string> description;
        std::map<std::string, std::string> warning;
        bool restore_files = true;
        detail::get_bool(*plugin, "restore_files", "plugin", restore_files, e);
        parse_localized(*plugin, "warning", "plugin", warning, e, false);
        e.ok() && detail::get_string(*plugin, "id", "plugin", m.id, e, true) && detail::get_string(*plugin, "version", "plugin", m.version, e, true)
            && detail::get_string(*plugin, "game", "plugin", m.game, e, true) && detail::get_string(*plugin, "kind", "plugin", m.kind, e, true)
            && detail::get_string(*plugin, "runtime", "plugin", m.runtime_range, e) && parse_localized(*plugin, "name", "plugin", m.name, e, true)
            && parse_localized(*plugin, "description", "plugin", description, e, false) && detail::get_strings(*plugin, "authors", "plugin", authors, e)
            && detail::get_string(*plugin, "license", "plugin", ignored, e) && detail::get_string(*plugin, "homepage", "plugin", ignored, e);
        if (e.ok() && !valid_id(m.id)) e.fail("plugin.id", "must be 1-96 characters of a-z 0-9 . -");
        detail::get_strings(*plugin, "aliases", "plugin", m.aliases, e);
        std::set<std::string> former_ids;
        for (const auto& alias : m.aliases) {
            if (!e.ok()) break;
            if (!valid_id(alias) || alias == m.id || !former_ids.insert(alias).second)
                e.fail("plugin.aliases", "must contain distinct former identifiers, excluding the canonical id");
        }
        if (e.ok() && !parse_semver(m.version)) e.fail("plugin.version", "must be a SemVer version");
        if (e.ok() && m.kind != "patch" && m.kind != "native" && m.kind != "files") e.fail("plugin.kind", "must be patch, native or files");
        if (e.ok() && plugin->contains("restore_files") && m.kind != "files") e.fail("plugin.restore_files", "only allowed for file plugins");
    }

    // File deployment belongs to the launcher. Validate its metadata without opening or writing payloads here.
    if (const auto* files = root.get("files"); files && e.ok()) {
        const auto* rows = files->as_array();
        if (m.kind != "files" || !rows || rows->empty()) e.fail("files", "requires a file plugin and a nonempty array of tables");
        for (size_t i = 0; rows && e.ok() && i < rows->size(); ++i) {
            const auto path = "files[" + std::to_string(i) + "]";
            const auto* row = rows->get(i)->as_table();
            if (!row) { e.fail(path, "must be a table"); break; }
            std::string source_path, target, hash;
            std::optional<int64_t> size;
            std::vector<std::string> builds, languages, excluded_languages;
            detail::only_keys(*row, {"source", "target", "size", "sha256", "builds", "client_languages", "exclude_client_languages"}, path, e)
                && detail::get_string(*row, "source", path, source_path, e, true)
                && detail::get_string(*row, "target", path, target, e, true)
                && detail::get_string(*row, "sha256", path, hash, e, true)
                && detail::get_int(*row, "size", path, size, e, 1)
                && detail::get_strings(*row, "builds", path, builds, e)
                && read_client_languages(*row, path, languages, excluded_languages, e);
            if (e.ok() && (!size || source_path.empty() || target.empty() || hash.size() != 64
                || hash.find_first_not_of("0123456789abcdef") != std::string::npos))
                e.fail(path, "requires source, target, positive size and a lowercase SHA-256");
        }
    }
    if (e.ok() && m.kind == "files" && !root.contains("files")) e.fail("files", "file plugins require payload metadata");

    if (const toml::table* c = detail::get_table(root, "compatibility", "", e); c && e.ok()) {
        detail::only_keys(*c, {"builds", "exclude"}, "compatibility", e) && detail::get_strings(*c, "builds", "compatibility", m.builds, e)
            && detail::get_strings(*c, "exclude", "compatibility", m.exclude, e);
    }

    if (const toml::node* options = root.get("options"); options && e.ok()) {
        const auto* list = options->as_array();
        if (!list) e.fail("options", "must be an array of tables");
        for (size_t i = 0; list && e.ok() && i < list->size(); ++i) {
            const std::string path = "options[" + std::to_string(i) + "]";
            const auto* t = list->get(i)->as_table();
            OptionSpec o;
            if (!t) e.fail(path, "must be a table");
            else if (parse_option(*t, path, o, e)) {
                for (const auto& other : m.options)
                    if (other.key == o.key) e.fail(path + ".key", "duplicate option '" + o.key + "'");
                m.options.push_back(std::move(o));
            }
        }
    }

    if (const toml::node* patches = root.get("patches"); patches && e.ok()) {
        const auto* list = patches->as_array();
        if (!list) e.fail("patches", "must be an array of tables");
        if (list && list->size() > 256) e.fail("patches", "at most 256 patches are allowed per plugin");
        for (size_t i = 0; list && e.ok() && i < list->size(); ++i) {
            const std::string path = "patches[" + std::to_string(i) + "]";
            const auto* t = list->get(i)->as_table();
            PatchSpec p;
            if (!t) e.fail(path, "must be a table");
            else if (parse_patch(*t, path, p, e)) {
                if (!p.when.empty()) {
                    const auto it = std::find_if(m.options.begin(), m.options.end(), [&](const OptionSpec& o) { return o.key == p.when; });
                    if (it == m.options.end() || it->type != OptionSpec::Type::Bool) e.fail(path + ".when", "must name a bool option");
                }
                m.patches.push_back(std::move(p));
            }
        }
    }

    if (const toml::table* n = detail::get_table(root, "native", "", e); n && e.ok()) {
        detail::only_keys(*n, {"library", "entry"}, "native", e) && detail::get_string(*n, "library", "native", m.native_library, e, true)
            && detail::get_string(*n, "entry", "native", m.native_entry, e);
        if (e.ok() && m.native_library.find_first_of("/\\:") != std::string::npos) e.fail("native.library", "must be a file name inside the plugin folder");
        if (e.ok() && (m.native_library == "." || m.native_library == ".." || m.native_library.find('\0') != std::string::npos))
            e.fail("native.library", "must be a non-NUL file name inside the plugin folder");
        if (e.ok() && (m.native_entry.empty() || m.native_entry.size() > 128 || m.native_entry.find('\0') != std::string::npos))
            e.fail("native.entry", "must be a non-NUL export name of 1..128 characters");
    }
    if (e.ok() && m.kind == "native" && m.native_library.empty()) e.fail("native", "required when kind = \"native\"");
    if (e.ok() && m.kind == "patch" && !m.native_library.empty()) e.fail("native", "only allowed when kind = \"native\"");
    if (e.ok() && m.kind == "files" && !m.native_library.empty())
        e.fail("files", "file plugins cannot contain native libraries");
    if (e.ok() && m.kind == "files" && std::any_of(m.options.begin(), m.options.end(), [&](const OptionSpec& option) {
        return option.type != OptionSpec::Type::Bool || !std::any_of(m.patches.begin(), m.patches.end(),
            [&](const PatchSpec& patch) { return patch.when == option.key; });
    })) e.fail("files", "file plugin options must be boolean gates referenced by memory patches");
    if (e.ok() && m.kind == "files" && m.patches.empty())
        e.fail("patches", "file plugins without memory patches are launcher-only");

    if (const auto* companion = detail::get_table(root, "companion", "", e); companion && e.ok()) {
        if (m.kind != "native") e.fail("companion", "only allowed for native plugins");
        detail::only_keys(*companion, {"messages"}, "companion", e);
        const auto* messages = detail::get_table(*companion, "messages", "companion", e);
        if (!messages || messages->empty() || messages->size() > 128) e.fail("companion.messages", "requires 1..128 message types");
        if (messages && e.ok()) for (auto&& [key, node] : *messages) {
            const std::string type(key.str());
            const auto version = node.value_exact<int64_t>();
            if (!valid_id(type) || type[0] < 'a' || type[0] > 'z' || !version || *version < 1 || *version > 65535) {
                e.fail("companion.messages", "requires lower-case type identities and schema versions between 1 and 65535"); break;
            }
            m.companion_messages.emplace(type, static_cast<uint32_t>(*version));
        }
    }
    if (!e.ok()) {
        error = source + ": " + e.first;
        return std::nullopt;
    }
    return m;
}

std::optional<PluginManifest> load_manifest(const std::wstring& folder, std::string& error) {
    const std::wstring path = folder + L"\\plugin.toml";
    std::string text;
    if (!read_text_file(path, 256 * 1024, text)) {
        const auto code = GetLastError();
        error = narrow(path) + ": cannot be read (Windows error " + std::to_string(code) + ")";
        return std::nullopt;
    }
    return parse_manifest(text, narrow(path), error);
}

} // namespace fthook

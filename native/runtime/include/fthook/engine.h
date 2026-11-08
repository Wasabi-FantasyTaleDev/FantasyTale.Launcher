#pragma once
// Resolves, checks and applies plugins against the loaded image.
#include "image.h"
#include "manifest.h"
#include "session.h"

#include <functional>
#include <map>
#include <optional>
#include <string>
#include <vector>

namespace fthook {

/// Finds a site that a byte pattern alone cannot. Returns the address, or nullopt with
/// `error` set. Called repeatedly while a packed image is still unpacking.
using LocatorFn = std::function<std::optional<uintptr_t>(const ImageInfo& image, std::string& error)>;

/// Named locators shared by all plugins ("fthook.config-md5-verdict").
class LocatorRegistry {
public:
    /// Adds a locator. Returns false when the name is empty or already taken.
    bool add(const std::string& name, LocatorFn fn);
    /// The locator with this name, or nullptr.
    const LocatorFn* find(const std::string& name) const;
    /// Removes a locator during failed native activation, before its DLL is unloaded.
    void remove(const std::string& name);
private:
    std::map<std::string, LocatorFn> locators_;
};

/// Registers the locators built into the runtime (see locators.cpp).
void register_builtin_locators(LocatorRegistry& registry);

/// Final state of one plugin.
enum class Outcome { Installed, AlreadyPresent, SkippedOption, UnavailableBuild, SignatureMismatch, Timeout, Failed, RollbackFailed };

/// Stable lower-case name of an outcome, used in log lines.
const char* to_string(Outcome outcome) noexcept;

/// Result line for one plugin.
struct PluginReport {
    std::string id;
    Outcome outcome = Outcome::Failed;
    std::string detail;
    bool required = false;
};

/// Returns an empty string for safe activation, required-plugin for a required
/// failure, or patch-restoration for incomplete rollback regardless of required flags.
const char* activation_error(const std::vector<PluginReport>& reports) noexcept;

/// A plugin prepared for one build: manifest, effective options and the patches that apply.
struct PreparedPlugin {
    PluginManifest manifest;
    std::map<std::string, OptionValue> options;  ///< Defaults overlaid by session values.
    std::vector<const PatchSpec*> patches;       ///< Points into manifest.patches.
    bool required = false;
};

/// Checks availability, validates options and selects patches for `build`, `branch` and the game language.
/// Returns nullopt with a report when the plugin cannot run (unavailable build, bad option,
/// overlapping patch variants, rva without exactly one build).
std::optional<PreparedPlugin> prepare(PluginManifest manifest, const SessionPlugin& entry,
    const std::string& build, const std::string& branch, PluginReport& report, const std::string& client_language = "");

/// Outcome of one attempt to locate and check every patch of a plugin.
enum class Probe { Ready, NotYet, Mismatch };

/// Locates every patch of `plugin` and checks its expected bytes, filling `sites`.
/// NotYet means a site has not appeared (the image may still be unpacking).
Probe probe(const PreparedPlugin& plugin, const ImageInfo& image, const LocatorRegistry& locators,
    std::vector<uintptr_t>& sites, std::string& detail);

/// Writes every patch at `sites` (from a Ready probe). Returns Installed, AlreadyPresent
/// when every site already held its bytes, or Failed after rolling back earlier writes.
Outcome apply(const PreparedPlugin& plugin, const std::vector<uintptr_t>& sites, std::string& detail, bool* rollback_failed = nullptr);

/// Prepares and applies plugins in session order, retrying `unpacked` plugins every
/// `poll_ms` until each is resolved or its deadline passes. Blocking; call on a worker thread.
std::vector<PluginReport> run_session(const Session& session, LocatorRegistry& locators,
    const std::function<std::optional<ImageInfo>()>& describe, unsigned poll_ms = 25);

} // namespace fthook

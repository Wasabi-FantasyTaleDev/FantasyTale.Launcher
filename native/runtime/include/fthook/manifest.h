#pragma once
// In-memory form of plugin.toml (docs/hook/plugin-format.md, schema 1).
#include "pattern.h"

#include <cstdint>
#include <map>
#include <optional>
#include <string>
#include <variant>
#include <vector>

namespace fthook {

/// A typed option value.
using OptionValue = std::variant<bool, int64_t, std::string>;

/// One `[[options]]` entry.
struct OptionSpec {
    enum class Type { Bool, Int, String, Enum };
    std::string key;
    Type type = Type::Bool;
    OptionValue default_value;
    int64_t min = INT64_MIN, max = INT64_MAX;  ///< Int bounds.
    size_t max_length = 256;                    ///< String bound.
    std::vector<std::string> values;            ///< Enum members.
};

/// How a patch site is found. Exactly one member is set.
struct Locate {
    std::optional<Pattern> pattern;
    bool unique = true;                 ///< match = "unique" (true) or "first" (false).
    std::optional<uint32_t> rva;
    std::string locator;                ///< Named locator, empty when unused.
    int32_t offset = 0;
};

/// One `[[patches]]` entry.
struct PatchSpec {
    std::string id;
    std::vector<std::string> builds;    ///< Empty: every build the plugin is available on.
    std::string when;                   ///< Bool option gate, empty when unconditional.
    std::vector<std::string> client_languages; ///< Selected game language gate; empty means every language.
    std::vector<std::string> exclude_client_languages; ///< Selected game languages that suppress this patch.
    Locate locate;
    Pattern expect;
    std::vector<uint8_t> write;
    bool wait_unpacked = true;          ///< timing = "unpacked" (true) or "immediate".
    uint32_t deadline_ms = 60000;
    bool import_slot = false;          ///< Runtime-only native IAT write; never accepted from TOML.
};

/// A parsed and validated plugin.toml.
struct PluginManifest {
    std::string id, version, game, kind, runtime_range;
    std::vector<std::string> aliases;          ///< Former identifiers; activation still uses the canonical id.
    std::map<std::string, std::string> name;    ///< Language tag to text; "en" always present.
    std::vector<std::string> builds{"*"}, exclude;
    std::vector<OptionSpec> options;
    std::vector<PatchSpec> patches;
    std::string native_library, native_entry = "fthook_plugin_main";
    std::map<std::string, uint32_t> companion_messages; ///< Manifest-requested companion schemas; no credentials.
};

/// True when `selector` ("*", "branch:<b>" or a build id) matches the build.
bool selector_matches(const std::string& selector, const std::string& build, const std::string& branch);
/// True when any selector in the list matches.
bool any_selector_matches(const std::vector<std::string>& selectors, const std::string& build, const std::string& branch);

/// Parses `toml_text` as plugin.toml. On failure returns nullopt and sets `error` to a
/// message naming the key and the reason. `source` names the file in messages.
std::optional<PluginManifest> parse_manifest(const std::string& toml_text, const std::string& source, std::string& error);

/// Reads and parses `<folder>/plugin.toml`.
std::optional<PluginManifest> load_manifest(const std::wstring& folder, std::string& error);

/// Reads a whole UTF-8 file. Returns false when it cannot be read or exceeds `max_bytes`.
bool read_text_file(const std::wstring& path, size_t max_bytes, std::string& out);

/// UTF-8 / UTF-16 conversion helpers.
std::wstring widen(const std::string& text);
std::string narrow(const std::wstring& text);

} // namespace fthook

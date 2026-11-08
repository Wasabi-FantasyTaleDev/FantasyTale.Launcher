#pragma once
// In-memory form of session.toml (docs/hook/session-contract.md, schema 1).
#include "manifest.h"

#include <cstdint>
#include <map>
#include <optional>
#include <string>
#include <vector>

namespace fthook {

/// One `[[plugins]]` entry of a session.
struct SessionPlugin {
    std::string id, version;
    std::wstring path;                          ///< Folder holding plugin.toml.
    bool required = false;
    std::map<std::string, OptionValue> options; ///< Values as written by the launcher.
};

/// A parsed session.toml.
struct Session {
    std::string client_language;               ///< Selected game language, independent of launcher UI language.
    std::string launch_id;                      ///< Schema 2 launch correlation; never contains credentials.
    uint32_t startup_timeout_ms = 120000;
    uint32_t launcher_pid = 0;                  ///< Expected local named-pipe server process.
    std::string game, build, branch, instance;
    std::string messenger;                      ///< Host-specific mode, e.g. "standin" / "forward".
    std::wstring log_path;
    size_t log_max_kib = 1024;
    std::optional<uint32_t> pe_timestamp, image_size;
    bool name_login = false;                   ///< Explicit name-only room login; never inferred from a missing ticket.
    std::string account, ticket;                ///< Secrets: never logged.
    std::string credential_plugin;              ///< Explicit selected-plugin grant; empty denies access.
    std::string endpoint_host;
    uint16_t endpoint_port = 0;
    std::wstring overlay_pipe;                 ///< Optional launcher-owned native overlay broker.
    std::string messenger_transport;           ///< [messenger] transport: "http" (the default) when the section exists.
    std::string messenger_url;                 ///< Optional [messenger] base URL of the messenger service.
    std::string messenger_ticket;              ///< Optional [messenger] signed ticket; a secret, never logged.
    std::vector<SessionPlugin> plugins;
    /// Opaque game-module launch metadata; the runtime validates only its bounded transport envelope.
    struct ModuleData {
        std::string capability, payload;
    };
    std::optional<ModuleData> module_data;
    /// Optional [game_login]: the player signs in on the game's own login screen, and the stand-in sends the
    /// launcher's sign-in and launch requests for this launch. launch_key is a secret, never logged.
    struct GameLogin {
        std::string api_url, launch_id, launch_key, launcher_version, build;
        uint32_t protocol_version = 0;
        std::string release_version, patch_version, launcher_language, native_language, client_language;
    };
    std::optional<GameLogin> game_login;
};

/// Parses session.toml text; unknown keys are errors.
std::optional<Session> parse_session(const std::string& toml_text, const std::string& source, std::string& error);

/// Reads and parses a session file.
std::optional<Session> load_session(const std::wstring& path, std::string& error);

} // namespace fthook

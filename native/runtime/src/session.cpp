#include "fthook/session.h"
#include "toml_util.h"
#include <algorithm>
#include <windows.h>

namespace fthook {
using detail::Errors;

namespace {
// An absolute http(s) URL with a host and nothing a client must not send onward: no user information, query,
// fragment, whitespace or control characters. The messenger appends its own operation paths to it.
bool valid_messenger_url(const std::string& url) {
    if (url.size() > 2048) return false;
    const size_t scheme = url.rfind("https://", 0) == 0 ? 8 : url.rfind("http://", 0) == 0 ? 7 : 0;
    if (!scheme || url.size() == scheme) return false;
    for (unsigned char c : url)
        if (c <= 0x20 || c >= 0x7F || c == '?' || c == '#' || c == '\\') return false;
    const size_t authority_end = url.find('/', scheme);
    const std::string authority = url.substr(scheme, authority_end == std::string::npos ? std::string::npos : authority_end - scheme);
    return !authority.empty() && authority.find('@') == std::string::npos && authority[0] != ':';
}

// The signed ticket is "v1." followed by base64url text and dots; anything else is refused before it is sent.
bool valid_messenger_ticket(const std::string& ticket) {
    if (ticket.empty() || ticket.size() > 2048) return false;
    return std::all_of(ticket.begin(), ticket.end(), [](unsigned char c) {
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
    });
}
} // namespace

std::optional<Session> parse_session(const std::string& toml_text, const std::string& source, std::string& error) {
    toml::parse_result parsed = toml::parse(toml_text, source);
    if (!parsed) {
        error = source + ": " + std::string(parsed.error().description());
        return std::nullopt;
    }
    const toml::table& root = parsed.table();
    Errors e;
    Session s;
    std::optional<int64_t> schema;
    detail::only_keys(root, {"schema", "session", "login", "endpoint", "overlay", "messenger", "game_login", "plugins", "module_data"}, "", e);
    detail::get_int(root, "schema", "", schema, e);
    if (e.ok() && schema != 1 && schema != 2) e.fail("schema", "must be 1 or 2");

    const toml::table* session = detail::get_table(root, "session", "", e);
    if (e.ok() && !session) e.fail("session", "missing");
    if (e.ok()) {
        std::string log;
        std::optional<int64_t> log_kib;
        detail::only_keys(*session, {"game", "build", "branch", "instance", "messenger", "log", "log_max_kib", "fingerprint", "launch_id", "startup_timeout_ms", "launcher_pid", "client_language"}, "session", e)
            && detail::get_string(*session, "game", "session", s.game, e, true) && detail::get_string(*session, "build", "session", s.build, e, true)
            && detail::get_string(*session, "branch", "session", s.branch, e) && detail::get_string(*session, "instance", "session", s.instance, e)
            && detail::get_string(*session, "client_language", "session", s.client_language, e)
            && detail::get_string(*session, "messenger", "session", s.messenger, e) && detail::get_string(*session, "log", "session", log, e)
            && detail::get_int(*session, "log_max_kib", "session", log_kib, e, 16, 65536);
        s.log_path = widen(log);
        if (s.client_language.size() > 32 || !std::all_of(s.client_language.begin(), s.client_language.end(), [](unsigned char c) {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-'; }))
            e.fail("session.client_language", "invalid language tag");
        s.log_max_kib = static_cast<size_t>(log_kib.value_or(1024));
        std::optional<int64_t> timeout;
        detail::get_string(*session, "launch_id", "session", s.launch_id, e, schema == 2);
        detail::get_int(*session, "startup_timeout_ms", "session", timeout, e, 1000, 300000);
        s.startup_timeout_ms = static_cast<uint32_t>(timeout.value_or(120000));
        std::optional<int64_t> launcher_pid;
        detail::get_int(*session, "launcher_pid", "session", launcher_pid, e, 1, UINT32_MAX);
        if (schema == 2 && !launcher_pid) e.fail("session.launcher_pid", "missing");
        s.launcher_pid = static_cast<uint32_t>(launcher_pid.value_or(0));
        if (schema == 1 && (!s.launch_id.empty() || timeout)) e.fail("session.launch_id", "requires schema 2");
        if (schema == 2 && (s.launch_id.size() != 32 || s.launch_id.find_first_not_of("0123456789abcdef") != std::string::npos))
            e.fail("session.launch_id", "must be 32 lower-case hexadecimal characters");
        if (const toml::table* fp = detail::get_table(*session, "fingerprint", "session", e); fp && e.ok()) {
            std::optional<int64_t> ts, size;
            detail::only_keys(*fp, {"pe_timestamp", "image_size"}, "session.fingerprint", e)
                && detail::get_int(*fp, "pe_timestamp", "session.fingerprint", ts, e, 0, UINT32_MAX)
                && detail::get_int(*fp, "image_size", "session.fingerprint", size, e, 0, UINT32_MAX);
            if (ts) s.pe_timestamp = static_cast<uint32_t>(*ts);
            if (size) s.image_size = static_cast<uint32_t>(*size);
        }
    }

    if (const auto* data = detail::get_table(root, "module_data", "", e); data && e.ok()) {
        Session::ModuleData value;
        if (schema != 2) e.fail("module_data", "requires schema 2");
        e.ok() && detail::only_keys(*data, {"capability", "payload"}, "module_data", e)
            && detail::get_string(*data, "capability", "module_data", value.capability, e, true)
            && detail::get_string(*data, "payload", "module_data", value.payload, e, true);
        if (e.ok() && (value.capability.empty() || value.capability.size() > 128
            || value.capability.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789-._") != std::string::npos))
            e.fail("module_data.capability", "must be 1..128 lower-case identifier characters");
        if (e.ok() && (value.payload.empty() || value.payload.size() > 32768 || value.payload.find('\0') != std::string::npos
            || !MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.payload.data(), static_cast<int>(value.payload.size()), nullptr, 0)))
            e.fail("module_data.payload", "must be 1..32768 bytes of NUL-free UTF-8");
        if (e.ok()) s.module_data = std::move(value);
    }

    if (const toml::table* login = detail::get_table(root, "login", "", e); login && e.ok()) {
        detail::only_keys(*login, {"account", "ticket", "credential_plugin", "mode"}, "login", e) && detail::get_string(*login, "account", "login", s.account, e)
            && detail::get_string(*login, "ticket", "login", s.ticket, e);
        if (login->contains("mode")) {
            std::string mode;
            if (detail::get_string(*login, "mode", "login", mode, e, true)) {
                if (mode != "name") e.fail("login.mode", "must be name when present");
                else s.name_login = true;
            }
        }
        if (login->contains("credential_plugin"))
            detail::get_string(*login, "credential_plugin", "login", s.credential_plugin, e, true);
    }

    if (const toml::table* endpoint = detail::get_table(root, "endpoint", "", e); endpoint && e.ok()) {
        std::optional<int64_t> port;
        detail::only_keys(*endpoint, {"host", "port"}, "endpoint", e) && detail::get_string(*endpoint, "host", "endpoint", s.endpoint_host, e, true)
            && detail::get_int(*endpoint, "port", "endpoint", port, e, 1, 65535);
        if (e.ok() && !port) e.fail("endpoint.port", "missing");
        s.endpoint_port = static_cast<uint16_t>(port.value_or(0));
    }

    if (const toml::table* overlay = detail::get_table(root, "overlay", "", e); overlay && e.ok()) {
        std::string pipe;
        detail::only_keys(*overlay, {"pipe"}, "overlay", e)
            && detail::get_string(*overlay, "pipe", "overlay", pipe, e, true);
        if (e.ok() && (pipe.size() > 256 || pipe.find_first_of("\r\n\t") != std::string::npos))
            e.fail("overlay.pipe", "contains an invalid pipe name");
        s.overlay_pipe = widen(pipe);
    }

    if (const toml::table* messenger = detail::get_table(root, "messenger", "", e); messenger && e.ok()) {
        // url and ticket are required when the section exists: a URL without a ticket cannot sign in, and a ticket
        // without a URL has nowhere to go. transport names how the client reaches url; only "http" exists so far, and
        // "gateway" (the original messenger protocol through a local translator) is refused until it is implemented.
        if (schema != 2) e.fail("messenger", "requires schema 2");
        e.ok() && detail::only_keys(*messenger, {"transport", "url", "ticket"}, "messenger", e)
            && detail::get_string(*messenger, "transport", "messenger", s.messenger_transport, e)
            && detail::get_string(*messenger, "url", "messenger", s.messenger_url, e, true)
            && detail::get_string(*messenger, "ticket", "messenger", s.messenger_ticket, e, true);
        if (e.ok() && s.messenger_transport.empty()) s.messenger_transport = "http";
        if (e.ok() && s.messenger_transport == "gateway") e.fail("messenger.transport", "\"gateway\" is not supported yet");
        else if (e.ok() && s.messenger_transport != "http") e.fail("messenger.transport", "must be \"http\"");
        if (e.ok() && !valid_messenger_url(s.messenger_url))
            e.fail("messenger.url", "must be an http(s) URL without credentials, query, fragment or whitespace");
        if (e.ok() && !valid_messenger_ticket(s.messenger_ticket))
            e.fail("messenger.ticket", "must be at most 2048 ticket characters");
    }

    if (const toml::table* game = detail::get_table(root, "game_login", "", e); game && e.ok()) {
        // The launch the launcher opened for an in-game sign-in; the stand-in binds it with the typed account.
        if (schema != 2) e.fail("game_login", "requires schema 2");
        if (e.ok() && !s.ticket.empty()) e.fail("game_login", "cannot accompany a login ticket");
        Session::GameLogin g;
        std::optional<int64_t> protocol;
        e.ok() && detail::only_keys(*game, {"api_url", "launch_id", "launch_key", "launcher_version", "build", "protocol_version",
                "release_version", "patch_version", "launcher_language", "native_language", "client_language"}, "game_login", e)
            && detail::get_string(*game, "api_url", "game_login", g.api_url, e, true)
            && detail::get_string(*game, "launch_id", "game_login", g.launch_id, e, true)
            && detail::get_string(*game, "launch_key", "game_login", g.launch_key, e, true)
            && detail::get_string(*game, "launcher_version", "game_login", g.launcher_version, e, true)
            && detail::get_string(*game, "build", "game_login", g.build, e, true)
            && detail::get_int(*game, "protocol_version", "game_login", protocol, e, 1, 65535)
            && detail::get_string(*game, "release_version", "game_login", g.release_version, e)
            && detail::get_string(*game, "patch_version", "game_login", g.patch_version, e)
            && detail::get_string(*game, "launcher_language", "game_login", g.launcher_language, e)
            && detail::get_string(*game, "native_language", "game_login", g.native_language, e)
            && detail::get_string(*game, "client_language", "game_login", g.client_language, e);
        auto hex64 = [](const std::string& v) { return v.size() == 64 && v.find_first_not_of("0123456789abcdef") == std::string::npos; };
        if (e.ok() && !protocol) e.fail("game_login.protocol_version", "is required");
        if (e.ok() && !valid_messenger_url(g.api_url)) e.fail("game_login.api_url", "must be an http(s) URL without credentials, query, fragment or whitespace");
        if (e.ok() && (!hex64(g.launch_id) || !hex64(g.launch_key))) e.fail("game_login", "launch_id and launch_key must be 64 lower-case hex digits");
        if (e.ok()) { g.protocol_version = static_cast<uint32_t>(*protocol); s.game_login = std::move(g); }
    }

    if (const toml::node* plugins = root.get("plugins"); plugins && e.ok()) {
        const auto* list = plugins->as_array();
        if (!list) e.fail("plugins", "must be an array of tables");
        if (list && list->size() > 64) e.fail("plugins", "at most 64 plugins are allowed per session");
        for (size_t i = 0; list && e.ok() && i < list->size(); ++i) {
            const std::string path = "plugins[" + std::to_string(i) + "]";
            const auto* t = list->get(i)->as_table();
            if (!t) { e.fail(path, "must be a table"); break; }
            SessionPlugin p;
            std::string folder;
            detail::only_keys(*t, {"id", "version", "path", "required", "options"}, path, e) && detail::get_string(*t, "id", path, p.id, e, true)
                && detail::get_string(*t, "version", path, p.version, e, true) && detail::get_string(*t, "path", path, folder, e, true)
                && detail::get_bool(*t, "required", path, p.required, e);
            p.path = widen(folder);
            if (const toml::table* options = detail::get_table(*t, "options", path, e); options && e.ok()) {
                for (auto&& [key, node] : *options) {
                    const std::string k(key.str());
                    if (const auto* b = node.as_boolean()) p.options[k] = b->get();
                    else if (const auto* n = node.as_integer()) p.options[k] = n->get();
                    else if (const auto* str = node.as_string()) p.options[k] = str->get();
                    else { e.fail(path + ".options." + k, "must be a boolean, integer or string"); break; }
                }
            }
            for (const auto& other : s.plugins)
                if (e.ok() && other.id == p.id) e.fail(path + ".id", "duplicate plugin '" + p.id + "'");
            s.plugins.push_back(std::move(p));
        }
    }

    if (e.ok() && s.name_login && (schema != 2 || !s.ticket.empty() || s.account.empty() || s.account.size() > 32
        || s.account.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-.@") != std::string::npos))
        e.fail("login.mode", "name requires a plain account and no ticket");
    if (e.ok() && !s.credential_plugin.empty()) {
        const bool selected = std::any_of(s.plugins.begin(), s.plugins.end(), [&](const auto& plugin) { return plugin.id == s.credential_plugin; });
        if (schema != 2 || (!s.name_login && s.ticket.empty()) || s.endpoint_host.empty() || !s.endpoint_port || !selected)
            e.fail("login.credential_plugin", "requires schema 2, login ticket, endpoint and a selected plugin");
    }
    if (!e.ok()) {
        error = source + ": " + e.first;
        return std::nullopt;
    }
    return s;
}

std::optional<Session> load_session(const std::wstring& path, std::string& error) {
    std::string text;
    if (!read_text_file(path, 256 * 1024, text)) {
        error = narrow(path) + ": cannot be read";
        return std::nullopt;
    }
    return parse_session(text, narrow(path), error);
}

} // namespace fthook

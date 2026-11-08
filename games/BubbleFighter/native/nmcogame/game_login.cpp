#include "game_login.h"

#include "json.h"

#include <windows.h>

namespace bubblefighter::nmco {

namespace {

constexpr unsigned kRequestTimeoutMs = 9000; // two requests inside the client's own 20 s (id-400 sub_8949A0 +9536)

std::string field(const char* name, const std::string& value) { return json::quote(name) + ":" + value; }

std::string error_of(const HttpExchange& reply) {
    json::Value body;
    std::string code;
    if (json::parse(reply.body, body)) body.string("error", code);
    return code;
}

// The id-400 sub_875E20 texts: 20000 the authentication server cannot be reached, 20002 the service is suspended,
// 20008 wrong password and the login is restricted for a while, 20010 the ID is banned, 20021 the server
// authentication failed, 20025 the ID does not exist, 20026 wrong password.
uint32_t failure(const HttpExchange& reply) {
    const std::string code = error_of(reply);
    if (code == "account_not_found") return 20025;
    if (code == "wrong_password" || code == "invalid_credentials") return 20026;
    if (code == "account_banned") return 20010;
    if (reply.status == 429) return 20008;
    if (reply.status == 503 || code == "server_maintenance" || code == "server_not_ready" || code == "server_full") return 20002;
    return 20021;
}

} // namespace

GameLoginResult game_sign_in(const GameLoginRequest& request, const std::string& account, std::string& password, const HttpSend& send) {
    GameLoginResult result;
    std::string body = "{" + field("username", json::quote(account)) + "," + field("password", json::quote(password)) + ","
        + field("launcherVersion", json::quote(request.launcher_version)) + "}";
    SecureZeroMemory(password.data(), password.size());
    HttpExchange login;
    const bool reached = send("POST", "v1/auth/login", "", body, kRequestTimeoutMs, login);
    SecureZeroMemory(body.data(), body.size());
    if (!reached) { result.code = 20000; result.note = "game sign-in: server unreachable"; return result; }
    json::Value grant;
    std::string bearer;
    if (login.status != 200) { result.code = failure(login); result.note = "game sign-in: refused (HTTP " + std::to_string(login.status) + ")"; return result; }
    if (!json::parse(login.body, grant) || !grant.string("accessToken", bearer) || bearer.empty()) {
        result.code = 20021; result.note = "game sign-in: malformed reply"; return result;
    }

    // The same /v1/launch body the launcher sends, bound to the launch it opened for this game.
    std::string launch = "{" + field("build", json::quote(request.build)) + "," + field("launcherVersion", json::quote(request.launcher_version))
        + "," + field("protocol_version", std::to_string(request.protocol_version));
    auto optional = [&](const char* name, const std::string& value) { if (!value.empty()) launch += "," + field(name, json::quote(value)); };
    optional("release_version", request.release_version);
    optional("patch_version", request.patch_version);
    optional("launcher_language", request.launcher_language);
    optional("native_language", request.native_language);
    optional("client_language", request.client_language);
    optional("launchId", request.launch_id);
    optional("launchKey", request.launch_key);
    launch += "}";
    HttpExchange launched;
    if (!send("POST", "v1/launch", bearer, launch, kRequestTimeoutMs, launched)) {
        result.code = 20000; result.note = "game launch: server unreachable"; return result;
    }
    SecureZeroMemory(bearer.data(), bearer.size());
    json::Value reply;
    if (launched.status != 200) { result.code = failure(launched); result.note = "game launch: refused (HTTP " + std::to_string(launched.status) + ")"; return result; }
    if (!json::parse(launched.body, reply) || !reply.string("ticket", result.ticket) || result.ticket.empty()) {
        result.code = 20021; result.note = "game launch: malformed reply"; return result;
    }
    if (const json::Value* messenger = reply.get("messenger")) {
        messenger->string("url", result.messenger_url);
        messenger->string("ticket", result.messenger_ticket);
    }
    result.code = 0;
    result.note = "game sign-in accepted";
    return result;
}

} // namespace bubblefighter::nmco

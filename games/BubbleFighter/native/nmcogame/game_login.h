#pragma once
// In-game sign-in: a build whose own login screen signs in (id-400 CNMLoginAuthFunc kind 2, sub_8949A0, carries the
// typed id and password) sends the launcher's own requests, /v1/auth/login then /v1/launch for the launch the
// launcher opened, and answers the client with its own result codes. Transport is injected for tests.
#include "friends_client.h"

#include <cstdint>
#include <string>

namespace bubblefighter::nmco {

/// What the launcher wrote into [game_login]; launch_key is a secret.
struct GameLoginRequest {
    std::string launch_id, launch_key, launcher_version, build;
    uint32_t protocol_version = 0;
    std::string release_version, patch_version, launcher_language, native_language, client_language;
};

/// The outcome: code 0 with a ticket (and the messenger sign-in when the server runs one), or a client result code.
struct GameLoginResult {
    uint32_t code = 20021;
    std::string ticket, messenger_url, messenger_ticket, note;
};

/// Signs in with the typed account; the password is wiped before the request goes out.
GameLoginResult game_sign_in(const GameLoginRequest& request, const std::string& account, std::string& password, const HttpSend& send);

} // namespace bubblefighter::nmco

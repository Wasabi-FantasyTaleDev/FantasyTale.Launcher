#include "kr_auth.cpp"
#include "../../../../native/tests/check.h"

namespace {
struct CredentialsFixture {
    bool allowed = true;
    bool invalid = false;
    bool name = false;
    static int32_t FTHOOK_CALL copy(void* context, const char* launch, const char* game, const char* build, fthook_credentials* output) {
        auto& fixture = *static_cast<CredentialsFixture*>(context);
        if (!fixture.allowed || std::strcmp(launch, "test-launch") || std::strcmp(game, "bubblefighter") || std::strcmp(build, "kr-1256"))
            return FTHOOK_INVALID;
        if (output->ticket_capacity < 65 || output->endpoint_capacity < 10) return FTHOOK_BUFFER_TOO_SMALL;
        if (fixture.name) std::memcpy(output->ticket, "Player_1", 9);
        else { std::memset(output->ticket, fixture.invalid ? 'A' : 'a', 64); output->ticket[64] = '\0'; }
        std::memcpy(output->endpoint_host, "127.0.0.1", 10); output->endpoint_port = 7100;
        return FTHOOK_OK;
    }
    static void FTHOOK_CALL log(void*, const char*) { }
};
}

int main(int argc, char** argv) {
    std::wstring passport;
    const std::string ticket(64, 'a');
    CHECK(korean_auth::make_passport(ticket, passport));
    CHECK(passport.size() == 75 && passport.substr(0, 11) == L"bff-ticket:");
    CHECK(passport.substr(11) == std::wstring(64, L'a'));
    CHECK(!korean_auth::make_passport(std::string(63, 'a'), passport));
    CHECK(!korean_auth::make_passport(std::string(65, 'a'), passport));
    CHECK(!korean_auth::make_passport(std::string(64, 'A'), passport));
    CHECK(!korean_auth::make_passport(std::string(64, ' '), passport));
    CHECK(korean_auth::make_name_passport("Player_1", passport) && passport == L"Player_1");
    CHECK(!korean_auth::make_name_passport("bad name", passport));
    CHECK(!korean_auth::make_name_passport("", passport));
    CHECK(!korean_auth::make_name_passport(std::string(33, 'a'), passport));
    for (unsigned bits = 0; bits < 32; ++bits)
        CHECK(korean_auth::can_submit((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0) == (bits == 15));
    CHECK(fthook_plugin_main(nullptr, nullptr) == FTHOOK_ABI_UNSUPPORTED);
    CredentialsFixture credentials;
    fthook_authentication_host api{1, sizeof(fthook_authentication_host), &credentials, "test-launch", CredentialsFixture::copy};
    fthook_host credential_host{};
    credential_host.abi_version = FTHOOK_ABI_VERSION; credential_host.struct_size = sizeof(credential_host);
    credential_host.game = "bubblefighter"; credential_host.build = "kr-1256";
    credential_host.log = CredentialsFixture::log; credential_host.authentication = &api;
    {
        credentials.name = true; api.abi_version = 2; api.name_login = 1;
        State state; state.host = &credential_host;
        CHECK(load_credentials(state) && state.passport == L"Player_1");
        api.name_login = 0;
        CHECK(!load_credentials(state));
        credentials.name = false; api.abi_version = 1;
    }
    {
        State state; state.host = &credential_host;
        CHECK(load_credentials(state));
        CHECK(state.passport == L"bff-ticket:" + std::wstring(64, L'a'));
        CHECK(state.launch_id == "test-launch" && state.port == 7100);
    }
    credentials.allowed = false;
    { State state; state.host = &credential_host; CHECK(!load_credentials(state)); CHECK(state.passport.empty()); }
    credentials.allowed = true; credentials.invalid = true;
    { State state; state.host = &credential_host; CHECK(!load_credentials(state)); }
    credential_host.authentication = nullptr;
    { State state; state.host = &credential_host; CHECK(!load_credentials(state)); }
    credential_host.struct_size = offsetof(fthook_host, authentication);
    { State state; state.host = &credential_host; CHECK(!load_credentials(state)); }
    IN_ADDR address{};
    CHECK(resolve_endpoint("127.0.0.1", address));
    struct alignas(4) Endpoint { unsigned char bytes[8]; } endpoint{};
    std::memset(endpoint.bytes, 0xA5, sizeof(endpoint.bytes));
    CHECK(write_endpoint(reinterpret_cast<uintptr_t>(&endpoint), address, 7100));
    IN_ADDR written{}; uint16_t port = 0;
    std::memcpy(&written, endpoint.bytes, sizeof(written));
    std::memcpy(&port, endpoint.bytes + 4, sizeof(port));
    CHECK(written.s_addr == address.s_addr && port == 7100);
    CHECK(endpoint.bytes[6] == 0xA5 && endpoint.bytes[7] == 0xA5);
    CHECK(argc == 2);
    if (argc == 2) {
        fthook_host host{};
        host.build = "kr-1256"; host.plugin_directory = argv[1];
        Profile profile;
        CHECK(load_profile(&host, profile));
        CHECK(profile.identity == 2516 && profile.stage_vtable == 0x119D1D0 && profile.signatures.size() >= 3);
        CHECK(profile.ngs_init == 0x9ABE90 && profile.ngs_pump == 0x9AC540);
        CHECK(profile.timeout <= 120000 && profile.poll >= 10);
        host.build = "kr-812";
        Profile unsupported;
        CHECK(!load_profile(&host, unsupported));
    }
    return finish("kr_auth_tests");
}

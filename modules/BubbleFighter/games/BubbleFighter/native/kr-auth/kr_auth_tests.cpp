#include "kr_auth.cpp"
#include "../../../../native/tests/check.h"

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
    for (unsigned bits = 0; bits < 32; ++bits)
        CHECK(korean_auth::can_submit((bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0) == (bits == 15));
    CHECK(fthook_plugin_main(nullptr, nullptr) == FTHOOK_ABI_UNSUPPORTED);
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

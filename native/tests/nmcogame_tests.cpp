#include "check.h"

#include <messenger.h>

#include <windows.h>

#include <algorithm>
#include <cstring>
#include <fstream>
#include <iterator>
#include <set>
#include <string>
#include <vector>
#include "login_reentry_tests.h"

using namespace bubblefighter::nmco;

void friends_tests();

namespace {

void identity_tests() {
    Identity id;
    CHECK(id.passport() == L"guest");
    id.environment = L"env";
    CHECK(id.passport() == L"env");
    id.launcher = L"launcher";
    CHECK(id.passport() == L"launcher");
    id.typed = L"typed";
    CHECK(id.passport() == L"typed");
    id.ticket = L"ticket";
    CHECK(id.passport() == L"bff-ticket:ticket");
}

std::vector<uint8_t> v0_envelope(const std::vector<uint8_t>& object) {
    Writer w;
    w.u32(kCryptorV0Id);
    w.u32(static_cast<uint32_t>(object.size()));
    w.out.insert(w.out.end(), object.begin(), object.end());
    w.u32(kCryptorV0Tag);
    return w.out;
}

void codec_tests() {
    // Reply framing: [v0 id][object length][header][body length][body][v0 tag].
    const std::vector<uint8_t> reply = build_reply({1, 0, 0, 0});
    CHECK(reply.size() == 4 + 4 + 4 + 4 + 4 + 4);
    uint32_t word = 0;
    std::memcpy(&word, reply.data(), 4);
    CHECK(word == kCryptorV0Id);
    std::memcpy(&word, reply.data() + 8, 4);
    CHECK(word == kObjectHeader);

    // Login-auth through the dialog (kind 2): the typed id becomes the passport.
    Writer object;
    object.u32(kObjectHeader);
    object.u32(0);
    object.u32(7);     // call id
    object.u32(2);     // kind: login dialog
    object.wstr(L"");  // passport
    object.wstr(L"player1");
    object.wstr(L"secret");
    const auto envelope = v0_envelope(object.out);
    std::vector<uint8_t> opened, body;
    CHECK(open_request(envelope.data(), opened) && opened == object.out);
    Identity id;
    id.launcher = L"launcher";
    std::string note;
    CHECK(answer(kLoginAuth, opened, id, body, note));
    CHECK(id.typed == L"player1" && note == "login-auth kind=2 auth=0");
    CHECK(body.size() == 12);

    CHECK(answer(kGetNexonPassport, {}, id, body, note));
    Reader r{body.data(), static_cast<uint32_t>(body.size()), 0};
    uint32_t result = 0;
    std::wstring passport;
    CHECK(r.u32(result) && result == 1 && r.wstr(passport) && passport == L"player1");
    CHECK(note.find("secret") == std::string::npos);

    // A fresh launcher ticket must remain marked across the real reply codec.
    // The server redeems it to the account instead of using the random token as one.
    for (const wchar_t digit : {L'a', L'b'}) {
        id.ticket.assign(64, digit);
        CHECK(answer(kGetNexonPassport, {}, id, body, note));
        Reader ticket_reader{body.data(), static_cast<uint32_t>(body.size()), 0};
        CHECK(ticket_reader.u32(result) && result == 1 && ticket_reader.wstr(passport));
        CHECK(passport == L"bff-ticket:" + std::wstring(64, digit));
        CHECK(note == "ticket provided");
    }

    CHECK(!answer(0x9999, {}, id, body, note));

    // A request longer than the limit is refused.
    std::vector<uint8_t> huge(8);
    const uint32_t big = kMaxRequestBytes + 1;
    std::memcpy(huge.data(), &kCryptorV0Id, 4);
    std::memcpy(huge.data() + 4, &big, 4);
    CHECK(!open_request(huge.data(), opened));
}

std::set<std::string> read_exports(const char* path) {
    std::ifstream file(path, std::ios::binary);
    std::vector<uint8_t> d((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    std::set<std::string> names;
    if (d.size() < 0x200) return names;
    const auto u16 = [&](size_t o) { uint16_t v; std::memcpy(&v, &d[o], 2); return v; };
    const auto u32 = [&](size_t o) { uint32_t v; std::memcpy(&v, &d[o], 4); return v; };
    const size_t pe = u32(0x3C), opt = pe + 24, sections = opt + u16(pe + 20);
    const uint32_t export_rva = u32(opt + 96);
    const auto offset = [&](uint32_t rva) -> size_t {
        for (unsigned i = 0; i < u16(pe + 6); ++i) {
            const size_t s = sections + i * 40;
            const uint32_t va = u32(s + 12), vs = std::max(u32(s + 8), u32(s + 16)), raw = u32(s + 20);
            if (rva >= va && rva < va + vs) return rva - va + raw;
        }
        return 0;
    };
    const size_t e = offset(export_rva);
    const size_t table = offset(u32(e + 32));
    for (uint32_t i = 0; i < u32(e + 24); ++i) names.insert(reinterpret_cast<const char*>(&d[offset(u32(table + 4 * i))]));
    return names;
}

void export_tests(const char* dll) {
    const std::set<std::string> expected = {"NMCO_CallNMFunc", "NMCO_CallNMFunc2", "NMCO_MemoryFree", "NMCO_SetLocale",
        "NMCO_SetLocaleAndRegion", "NMCO_SetPatchOption", "NMCO_SetUseFriendModuleOption", "NMCO_SetUseNGMOption",
        "NMCO_SetVersionFileUrl", "NMCO_SetVersionFileUrlA", "NMCO_SetVersionFileUrlW",
        "FT_IGW_Initialize_v1", "FT_IGW_GetModule_v1", "FT_IGW_Terminal_v1", "FT_IGW_Diagnostic_v1",
        "FT_GetLaunchMetadata_v1", "FT_GetLaunchId_v1", "FT_ReportBootstrapComplete_v1", "FT_ReportBootstrapFailure_v1"};
    CHECK(read_exports(dll) == expected);
}

} // namespace

int main(int argc, char** argv) {
    identity_tests();
    codec_tests();
    friends_tests();
    login_reentry_tests();
    // Default: nmcogame.dll beside this executable (shared native/out/<Configuration>/ folder).
    std::string dll;
    if (argc > 1) {
        dll = argv[1];
    } else {
        char path[MAX_PATH]{};
        dll.assign(path, GetModuleFileNameA(nullptr, path, MAX_PATH));
        dll = dll.substr(0, dll.find_last_of('\\') + 1) + "nmcogame.dll";
    }
    export_tests(dll.c_str());
    return finish("nmcogame_tests");
}

#include <winsock2.h>
#include <windows.h>
#include <fstream>
#include <cstring>
#include <string>

namespace {
bool exists(const wchar_t* path) { return GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES; }
bool login_callback_ok = false;
bool login_expect_denied = false;
int __stdcall received_login(int status, const void* info, int, int) {
    if (login_expect_denied) { login_callback_ok = status != 0 && info == nullptr; return 0; }
    if (status != 0 || !info) return 0;
    const auto fields = static_cast<const char* const*>(info);
    login_callback_ok = fields[1] && fields[2]
        && std::strcmp(fields[1], "launcher-account-longer-than-fifteen") == 0
        && std::string(fields[2]) == "bff-ticket:" + std::string(64, 'a');
    return 0;
}
bool probe_login(bool denied, bool legacy) {
    const auto module = LoadLibraryW(L".\\sdologinentry.dll");
    if (!module) return false;
    const auto initialize = reinterpret_cast<int(__stdcall*)(unsigned, const void*)>(GetProcAddress(module, "igwInitialize"));
    const auto get = reinterpret_cast<int(__stdcall*)(const void*, void**)>(GetProcAddress(module, "igwGetModule"));
    const auto terminate = reinterpret_cast<int(__cdecl*)()>(GetProcAddress(module, "igwTerminal"));
    if (!initialize || !get || !terminate) return false;
    const unsigned char displayId[16] = {0xab,0x89,0xb7,0x59,0xe9,0xbe,0x68,0x4c,0xaa,0x23,0xbc,0xa9,0xab,0x1a,0x3e,0x50};
    const unsigned char loginId[16] = {0x84,0x74,0x18,0xc8,0x5f,0x1c,0xdf,0x48,0x84,0x97,0x21,0x36,0xa7,0x2a,0x57,0x33};
    const unsigned char legacyId[16] = {0x6c,0x13,0x35,0x3f,0x61,0x70,0x69,0x4e,0xbf,0x3a,0xcc,0x78,0xd0,0x26,0xf4,0x8f};
    const auto selectedId = legacy ? legacyId : loginId;
    const auto alternateId = legacy ? loginId : legacyId;
    const unsigned char unknownId[16]{};
    void *display = nullptr, *identity = nullptr, *again = nullptr;
    if (get(selectedId, &identity) == 0 || initialize(1, displayId) == 0 || initialize(512, displayId) != 0) return false;
    if (get(unknownId, &again) == 0 || again || get(selectedId, nullptr) == 0) return false;
    // Request the login interface first and repeat it, unlike the client's normal order.
    if (get(selectedId, &identity) || get(displayId, &display) || get(alternateId, &again) || identity != again || display == identity) return false;
    const auto displayMethods = *static_cast<void***>(display);
    const auto methods = *static_cast<void***>(identity);
    const auto message = reinterpret_cast<int(__stdcall*)(void*, HWND, UINT, WPARAM, LPARAM, LRESULT*)>(displayMethods[9]);
    const auto active = reinterpret_cast<int(__stdcall*)(void*)>(methods[15]);
    LRESULT ignored = 0;
    if (message(display, nullptr, 0, 0, 0, &ignored) != 1 || active(identity) != 0) return false;
    using Callback = int(__stdcall*)(int, const void*, int, int);
    const auto signIn = reinterpret_cast<int(__stdcall*)(void*, Callback, int, int)>(methods[3]);
    if (signIn(identity, nullptr, 0, 0) == 0) return false;
    login_expect_denied = denied;
    const int result = signIn(identity, &received_login, 0, 0);
    if (!login_callback_ok || (result != 0) != denied) return false;
    if (!denied) {
        login_callback_ok = false; login_expect_denied = true;
        if (signIn(identity, &received_login, 0, 0) == 0 || !login_callback_ok) return false;
    }
    if (terminate() || get(selectedId, &again) == 0 || again) return false;
    return true;
}
bool probe_passport() {
    const auto module = GetModuleHandleW(L"nmcogame.dll");
    if (!module) return false;
    const auto call = reinterpret_cast<BOOL(__cdecl*)(UINT32, BYTE*, UINT32, BYTE**, UINT32*)>(GetProcAddress(module, "NMCO_CallNMFunc2"));
    const auto release = reinterpret_cast<void(__cdecl*)(void*)>(GetProcAddress(module, "NMCO_MemoryFree"));
    if (!call || !release) return false;
    // kr-812 sub_91EC40 requests the passport; sub_902020 wraps the marked UTF-16 reply.
    UINT32 request[] = {2083380, 0};
    BYTE* reply = nullptr;
    UINT32 size = 0;
    if (!call(0x2105, reinterpret_cast<BYTE*>(request), 0, &reply, &size) || !reply) return false;
    const std::wstring expected = L"bff-ticket:" + std::wstring(64, L'a');
    UINT32 cryptor = 0, header = 0, result = 0;
    UINT16 length = 0;
    const bool bounded = size == 26 + expected.size() * sizeof(wchar_t);
    if (bounded) {
        std::memcpy(&cryptor, reply, sizeof(cryptor));
        std::memcpy(&header, reply + 8, sizeof(header));
        std::memcpy(&result, reply + 16, sizeof(result));
        std::memcpy(&length, reply + 20, sizeof(length));
    }
    const bool matched = bounded && cryptor == 2083380 && header == 0x666F0101 && result == 1
        && length == ((expected.size() << 2) | 1)
        && std::memcmp(reply + 22, expected.data(), expected.size() * sizeof(wchar_t)) == 0;
    // The older export (cn-324 sub_7BF130) is the same call without flags and must answer alike.
    const auto older = reinterpret_cast<BOOL(__cdecl*)(UINT32, BYTE*, BYTE**, UINT32*)>(GetProcAddress(module, "NMCO_CallNMFunc"));
    BYTE* again = nullptr;
    UINT32 again_size = 0;
    const bool same = older && older(0x2105, reinterpret_cast<BYTE*>(request), &again, &again_size) && again
        && again_size == size && std::memcmp(again, reply, size) == 0;
    if (again) release(again);
    release(reply);
    return matched && same;
}
void receive_probe() {
    WSADATA data{};
    if (WSAStartup(MAKEWORD(2, 2), &data)) return;
    SOCKET socket = ::socket(AF_INET, SOCK_DGRAM, 0);
    sockaddr_in address{}; address.sin_family = AF_INET; address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    if (bind(socket, reinterpret_cast<sockaddr*>(&address), sizeof(address)) == 0) {
        int size = sizeof(address); getsockname(socket, reinterpret_cast<sockaddr*>(&address), &size);
        const char payload[] = "diagnostic-loopback-payload";
        sendto(socket, payload, sizeof(payload), 0, reinterpret_cast<sockaddr*>(&address), size);
        char buffer[128]{};
        recvfrom(socket, buffer, sizeof(buffer), 0, nullptr, nullptr);
    }
    closesocket(socket); WSACleanup();
}
}
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
    if (!LoadLibraryW(L".\\nmcogame.dll")) return 90;
    std::ofstream("ready") << "native-loaded";
    bool probed = false, login_probed = false, passport_probed = false;
    const auto deadline = GetTickCount64() + 30000;
    while (!exists(L"release") && GetTickCount64() < deadline) {
        if (!probed && exists(L"probe-net")) { receive_probe(); probed = true; std::ofstream("probe-done") << "received"; }
        if (!login_probed && (exists(L"probe-login") || exists(L"probe-login-denied"))) {
            login_probed = true;
            const bool accepted = probe_login(exists(L"probe-login-denied"), exists(L"probe-login-legacy"));
            { std::ofstream("login-result.tmp") << (accepted ? "ok" : "failed"); }
            MoveFileW(L"login-result.tmp", L"login-result");
        }
        if (!passport_probed && exists(L"probe-passport")) {
            passport_probed = true;
            { std::ofstream("passport-result.tmp") << (probe_passport() ? "ok" : "failed"); }
            MoveFileW(L"passport-result.tmp", L"passport-result");
        }
        if (exists(L"crash")) {
            *reinterpret_cast<volatile int*>(static_cast<uintptr_t>(0)) = 17;
        }
        Sleep(20);
    }
    return exists(L"release") ? 23 : 24;
}

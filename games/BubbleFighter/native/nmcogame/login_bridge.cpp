#include "host.h"
#include "diagnostic_log.h"
#include "login_reentry.h"
#include <fthook/log.h>

#include <array>
#include <atomic>
#include <cstddef>
#include <cstring>
#include <exception>
#include <intrin.h>

namespace diagnostic = bubblefighter::diagnostic;

namespace {
using Callback = int (__stdcall*)(int, const void*, int, int);
using Method = void(*)();
struct Object { const Method* methods; };
struct LoginRecord { DWORD unknown0; const char* account; const char* ticket; DWORD unknown12; };
static_assert(sizeof(LoginRecord) == 16 && offsetof(LoginRecord, account) == 4 && offsetof(LoginRecord, ticket) == 8);

// cn-406 sub_7CC3E0 requests the 16-byte identifiers at C1BCC4 and C1BCD4.
constexpr std::array<unsigned char, 16> kDisplayId = {0xab,0x89,0xb7,0x59,0xe9,0xbe,0x68,0x4c,0xaa,0x23,0xbc,0xa9,0xab,0x1a,0x3e,0x50};
constexpr std::array<unsigned char, 16> kLoginId = {0x84,0x74,0x18,0xc8,0x5f,0x1c,0xdf,0x48,0x84,0x97,0x21,0x36,0xa7,0x2a,0x57,0x33};
// cn-279 sub_6D4230 at 9EA780 and cn-324 sub_786890 at B211F8 request the earlier identity.
constexpr std::array<unsigned char, 16> kLegacyLoginId = {0x6c,0x13,0x35,0x3f,0x61,0x70,0x69,0x4e,0xbf,0x3a,0xcc,0x78,0xd0,0x26,0xf4,0x8f};
std::atomic<bool> g_initialized{false};
std::atomic<bool> g_delivered{false};
bubblefighter::login::RedirectLoginDelivery g_redirect_delivery;

[[noreturn]] void unsupported() {
    fthook::runtime_log().line("login bridge: unsupported client method");
    bubblefighter::host::fail_host();
    // An unknown signature cannot be returned from safely on x86.
    RaiseFailFastException(nullptr, nullptr, 0);
    std::terminate();
}
int __stdcall idle(Object*) { return 0; }
int __stdcall display_start(Object*, void*, void*, int) { return 0; }
int __stdcall set_value(Object*, void*) { return 0; }
// cn-406 sub_7CECB0 pushes six arguments at 7CED00 and one at 7CF300; both callees clean the stack.
int __stdcall display_message(Object*, HWND, UINT, WPARAM, LPARAM, LRESULT*) { return 1; }

int __stdcall login(Object*, Callback callback, int, int) {
    if (!callback) {
        diagnostic::record(diagnostic::Login);
        return static_cast<int>(E_POINTER);
    }
    std::string account, ticket; bool name_login = false;
    const bool ready = g_initialized.load() && bubblefighter::host::managed_credentials(account, ticket, &name_login);
    // cn-406 sub_577D00 converts narrow strings; ASCII is lossless on every client code page.
    const bool valid = ready && !account.empty() && account.size() <= 128
        && account.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-.@") == std::string::npos
        && (name_login ? ticket.empty() && account.size() <= 32 : ticket.size() == 64 && ticket.find_first_not_of("0123456789abcdef") == std::string::npos);
    const auto native = valid && !name_login ? bubblefighter::login::inspect_redirect_login(
        reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)), reinterpret_cast<uintptr_t>(callback),
        reinterpret_cast<uintptr_t>(_ReturnAddress()),
        [](uintptr_t address, void* output, size_t size) {
            SIZE_T copied{};
            return ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(address), output, size, &copied) && copied == size;
        }) : std::nullopt;
    const bool first_delivery = valid && !g_delivered.exchange(true);
    if (!valid || (!first_delivery && !g_redirect_delivery.claim_redirect(native))) {
        diagnostic::record(diagnostic::Login, diagnostic::Refused);
        bubblefighter::host::fail_host();
        callback(1, nullptr, 0, 0);
        return static_cast<int>(E_FAIL);
    }
    if (first_delivery) g_redirect_delivery.remember_initial(native);
    if (name_login) ticket = account;
    else ticket.insert(0, "bff-ticket:");
    const LoginRecord record{0, account.c_str(), ticket.c_str(), 0};
    // cn-279 sub_8580A0, cn-324 sub_5852D0 and cn-406 sub_577D00 synchronously copy +8/+4 into login strings.
    callback(0, &record, 0, 0);
    SecureZeroMemory(ticket.data(), ticket.size());
    SecureZeroMemory(account.data(), account.size());
    diagnostic::record(diagnostic::Login, diagnostic::Delivered);
    return 0;
}

const auto kDisplayMethods = [] {
    std::array<Method, 20> methods{}; methods.fill(&unsupported);
    methods[2] = reinterpret_cast<Method>(&idle);
    methods[3] = reinterpret_cast<Method>(&display_start);
    methods[5] = reinterpret_cast<Method>(&idle);
    methods[6] = reinterpret_cast<Method>(&idle);
    methods[9] = reinterpret_cast<Method>(&display_message);
    return methods;
}();
const auto kLoginMethods = [] {
    std::array<Method, 20> methods{}; methods.fill(&unsupported);
    methods[2] = reinterpret_cast<Method>(&idle);
    methods[3] = reinterpret_cast<Method>(&login);
    // cn-279 sub_8580A0, cn-324 sub_5852D0 and cn-406 sub_577D00 use slots 10/14; lifecycle uses 15/16.
    methods[10] = reinterpret_cast<Method>(&set_value);
    methods[14] = reinterpret_cast<Method>(&set_value);
    methods[15] = reinterpret_cast<Method>(&idle);
    methods[16] = reinterpret_cast<Method>(&set_value);
    return methods;
}();
Object g_display{kDisplayMethods.data()}, g_login{kLoginMethods.data()};
}

extern "C" int __stdcall ft_igw_initialize(unsigned version, const void* options) {
    bubblefighter::host::ensure_initialized();
    const int result = version == 512 && options ? 0 : static_cast<int>(E_INVALIDARG);
    diagnostic::record(diagnostic::Initialize, static_cast<uint32_t>(result));
    if (result != 0) return result;
    g_initialized.store(true);
    return 0;
}

extern "C" int __stdcall ft_igw_get_module(const void* id, void** output) {
    if (!output) {
        diagnostic::record(diagnostic::GetModule, static_cast<uint32_t>(E_POINTER));
        return static_cast<int>(E_POINTER);
    }
    *output = nullptr;
    if (!g_initialized.load() || !id) {
        diagnostic::record(diagnostic::GetModule, static_cast<uint32_t>(E_INVALIDARG));
        return static_cast<int>(E_INVALIDARG);
    }
    uint32_t module = diagnostic::UnknownModule;
    if (std::memcmp(id, kDisplayId.data(), kDisplayId.size()) == 0) { *output = &g_display; module = diagnostic::DisplayModule; }
    else if (std::memcmp(id, kLoginId.data(), kLoginId.size()) == 0
        || std::memcmp(id, kLegacyLoginId.data(), kLegacyLoginId.size()) == 0) { *output = &g_login; module = diagnostic::LoginModule; }
    else {
        diagnostic::record(diagnostic::GetModule, static_cast<uint32_t>(E_NOINTERFACE));
        return static_cast<int>(E_NOINTERFACE);
    }
    diagnostic::record(diagnostic::GetModule, 0, module);
    return 0;
}

extern "C" int __cdecl ft_igw_terminal() {
    g_initialized.store(false);
    diagnostic::record(diagnostic::Terminal);
    return 0;
}

extern "C" void __cdecl ft_igw_diagnostic(uint32_t event, uint32_t value0, uint32_t value1, uint32_t value2) {
    bubblefighter::host::ensure_initialized();
    diagnostic::record(event, value0, value1, value2);
}

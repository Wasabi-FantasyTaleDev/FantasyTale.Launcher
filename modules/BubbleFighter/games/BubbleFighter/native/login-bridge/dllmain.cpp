#include <windows.h>
#include "native_adapter.h"
#include <array>
#include <atomic>
#include <cstring>
#include <exception>
#include <mutex>

#pragma comment(linker, "/export:igwInitialize=_ft_cn_initialize@8")
#pragma comment(linker, "/export:igwGetModule=_ft_cn_get_module@8")
#pragma comment(linker, "/export:igwTerminal=_ft_cn_terminal")

namespace {
using Callback = int (__stdcall*)(int, const void*, int, int);
using Method = void(*)();
struct Object { const Method* methods; };
struct Proxy { const Method* methods; Object* original; };
struct LoginRecord { DWORD unknown0; const char* account; const char* ticket; DWORD unknown12; };
static_assert(sizeof(LoginRecord) == 16);
struct Delivery { Callback callback; bool seen{}, refused{}; };
thread_local Delivery* g_delivery{};
std::atomic<bool> g_initialized{false};
std::recursive_mutex g_state;
HMODULE g_module{};
struct CachedLogin {
    std::array<char, 129> account{};
    std::array<char, 76> ticket{};
    DWORD unknown0{}, unknown12{};
    bool valid{};
    void clear() noexcept { SecureZeroMemory(this, sizeof(*this)); }
} g_cached_login;

template<size_t Size> bool copy_string(const char* source, std::array<char, Size>& target) {
    if (!source) return false;
    for (size_t i = 0; i < Size; ++i) {
        SIZE_T copied = 0;
        if (!ReadProcessMemory(GetCurrentProcess(), source + i, &target[i], 1, &copied) || copied != 1) return false;
        if (!target[i]) return i != 0;
    }
    return false;
}

void cache_login(const void* data) {
    if (g_cached_login.valid) return;
    LoginRecord record{}; SIZE_T copied = 0;
    CachedLogin candidate;
    struct Erase { CachedLogin& value; ~Erase() { value.clear(); } } erase{candidate};
    if (!ReadProcessMemory(GetCurrentProcess(), data, &record, sizeof(record), &copied) || copied != sizeof(record)
        || !copy_string(record.account, candidate.account) || !copy_string(record.ticket, candidate.ticket)
        || std::strncmp(candidate.ticket.data(), "bff-ticket:", 11) != 0 || candidate.ticket[75] != 0) return;
    for (size_t i = 11; i < 75; ++i)
        if (!((candidate.ticket[i] >= '0' && candidate.ticket[i] <= '9')
            || (candidate.ticket[i] >= 'a' && candidate.ticket[i] <= 'f'))) return;
    candidate.unknown0 = record.unknown0; candidate.unknown12 = record.unknown12; candidate.valid = true;
    g_cached_login = candidate;
}
// cn-406 sub_7CC3E0 requests the newer identity; cn-279 sub_6D4230 and cn-324 sub_786890 use the earlier one.
constexpr std::array<BYTE, 16> kLogin = {0x84,0x74,0x18,0xc8,0x5f,0x1c,0xdf,0x48,0x84,0x97,0x21,0x36,0xa7,0x2a,0x57,0x33};
constexpr std::array<BYTE, 16> kLegacyLogin = {0x6c,0x13,0x35,0x3f,0x61,0x70,0x69,0x4e,0xbf,0x3a,0xcc,0x78,0xd0,0x26,0xf4,0x8f};

template<class T> T host(const char* name) {
    const auto module = GetModuleHandleW(L"nmcogame.dll");
    return module ? reinterpret_cast<T>(GetProcAddress(module, name)) : nullptr;
}
[[noreturn]] void unsupported() {
    OutputDebugStringA("Chinese authentication: unsupported provider method\n");
    RaiseFailFastException(nullptr, nullptr, 0); std::terminate();
}
template<size_t Slot> int __stdcall idle(Proxy* object) {
    using MethodType = int (__stdcall*)(Object*);
    return reinterpret_cast<MethodType>(object->original->methods[Slot])(object->original);
}
template<size_t Slot> int __stdcall set_value(Proxy* object, void* value) {
    using MethodType = int (__stdcall*)(Object*, void*);
    return reinterpret_cast<MethodType>(object->original->methods[Slot])(object->original, value);
}
bool seed(const void* data) {
    // cn-324 sub_5852D0 and cn-406 sub_577D00 consume provider record+8 as a narrow marked passport.
    LoginRecord record{}; SIZE_T copied = 0;
    return data && ReadProcessMemory(GetCurrentProcess(), data, &record, sizeof(record), &copied)
        && copied == sizeof(record) && cn_native::seed_passport(record.ticket);
}
int __stdcall deliver(int status, const void* data, int a, int b) {
    auto* delivery = g_delivery;
    if (!delivery || delivery->seen) return static_cast<int>(E_FAIL);
    delivery->seen = true;
    bool ready = false;
    try { ready = status != 0 || seed(data); } catch (...) { ready = false; }
    if (!ready) {
        delivery->refused = true;
        OutputDebugStringA("Chinese authentication: native passport assignment failed\n");
        return delivery->callback(1, nullptr, 0, 0);
    }
    if (status == 0) cache_login(data);
    return delivery->callback(status, data, a, b);
}
int __stdcall login(Proxy* object, Callback callback, int a, int b) {
    if (!callback) return static_cast<int>(E_POINTER);
    if (g_delivery || !g_initialized.load()) return static_cast<int>(E_FAIL);
    // The host owns credential authorization and its one-delivery guard; this proxy owns only native state adaptation.
    std::lock_guard lock(g_state);
    if (!g_initialized.load()) return static_cast<int>(E_FAIL);
    Delivery delivery{callback};
    struct Guard { ~Guard() { g_delivery = nullptr; } } guard;
    g_delivery = &delivery;
    const auto continuation = g_cached_login.valid
        ? cn_native::begin_connection_resume(g_cached_login.ticket.data()) : cn_native::Continuation::None;
    if (continuation == cn_native::Continuation::AlreadySubmitted) {
        // cn-406 sub_576DD0 and cn-717 sub_6DA700 ignore this return and retire their provider request.
        OutputDebugStringA("Chinese authentication: native Entry selected its login sender path\n");
        return 0;
    }
    if (continuation == cn_native::Continuation::Deliver) {
        // cn-279 sub_8588F0 and cn-324 sub_584580 request a callback for the replacement LoginStage.
        const LoginRecord record{g_cached_login.unknown0, g_cached_login.account.data(),
            g_cached_login.ticket.data(), g_cached_login.unknown12};
        deliver(0, &record, a, b);
        OutputDebugStringA(delivery.refused
            ? "Chinese authentication: selected native connection continuation refused\n"
            : "Chinese authentication: selected native connection continuation delivered\n");
        return delivery.refused ? static_cast<int>(E_FAIL) : 0;
    }
    using Login = int (__stdcall*)(Object*, Callback, int, int);
    const auto result = reinterpret_cast<Login>(object->original->methods[3])(object->original, &deliver, a, b);
    return delivery.refused || !delivery.seen ? static_cast<int>(E_FAIL) : result;
}
const auto kMethods = [] {
    std::array<Method, 20> methods{}; methods.fill(&unsupported);
    methods[2] = reinterpret_cast<Method>(&idle<2>);
    methods[3] = reinterpret_cast<Method>(&login);
    methods[10] = reinterpret_cast<Method>(&set_value<10>);
    methods[14] = reinterpret_cast<Method>(&set_value<14>);
    methods[15] = reinterpret_cast<Method>(&idle<15>);
    methods[16] = reinterpret_cast<Method>(&set_value<16>);
    return methods;
}();
std::array<Proxy, 2> g_proxies{{{kMethods.data(), nullptr}, {kMethods.data(), nullptr}}};
}

extern "C" int __stdcall ft_cn_initialize(unsigned version, const void* options) {
    using Initialize = int (__stdcall*)(unsigned, const void*);
    const auto initialize = host<Initialize>("FT_IGW_Initialize_v1");
    if (!initialize || version != 512 || !options) return static_cast<int>(E_INVALIDARG);
    std::lock_guard lock(g_state);
    if (g_initialized.load()) return static_cast<int>(E_UNEXPECTED);
    g_cached_login.clear();
    try {
        std::array<wchar_t, 32768> path{};
        const auto length = GetModuleFileNameW(g_module, path.data(), static_cast<DWORD>(path.size()));
        if (!length || length >= path.size() || !cn_native::prepare(reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)),
            std::filesystem::path(path.data()).parent_path())) {
            cn_native::shutdown(); return static_cast<int>(E_FAIL);
        }
        const auto result = initialize(version, options);
        if (result) { cn_native::shutdown(); return result; }
        g_initialized.store(true); return 0;
    } catch (...) {
        cn_native::shutdown();
        OutputDebugStringA("Chinese authentication: native adapter configuration failed\n");
        return static_cast<int>(E_FAIL);
    }
}

extern "C" int __stdcall ft_cn_get_module(const void* id, void** output) {
    using GetModule = int (__stdcall*)(const void*, void**);
    const auto get_module = host<GetModule>("FT_IGW_GetModule_v1");
    if (!output) return static_cast<int>(E_POINTER);
    *output = nullptr;
    if (!get_module || !id || !g_initialized.load()) return static_cast<int>(E_INVALIDARG);
    std::lock_guard lock(g_state);
    if (!g_initialized.load()) return static_cast<int>(E_FAIL);
    const auto result = get_module(id, output);
    if (result) return result;
    if (!*output) return static_cast<int>(E_FAIL);
    // Images without passport adaptation retain their original provider; optional captions do not require a wrapper.
    if (!cn_native::adapts_passport()) return result;
    for (size_t i = 0; i < g_proxies.size(); ++i) {
        const auto& known = i == 0 ? kLogin : kLegacyLogin;
        if (!std::memcmp(id, known.data(), known.size())) {
            auto& proxy = g_proxies[i];
            if (proxy.original && proxy.original != *output) { *output = nullptr; return static_cast<int>(E_FAIL); }
            proxy.original = static_cast<Object*>(*output);
            *output = &proxy; break;
        }
    }
    return result;
}

extern "C" int __cdecl ft_cn_terminal() {
    if (g_delivery) return static_cast<int>(E_UNEXPECTED);
    using Terminal = int (__cdecl*)();
    const auto terminal = host<Terminal>("FT_IGW_Terminal_v1");
    std::lock_guard lock(g_state);
    g_initialized.store(false);
    g_cached_login.clear();
    if (!cn_native::shutdown() || !terminal) return static_cast<int>(E_FAIL);
    // Native module release may follow igwTerminal; lifecycle calls still need the original static host object.
    return terminal();
}

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) g_module = module;
    return TRUE;
}

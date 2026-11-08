#include "login_contract.h"
#include <fthook/plugin_abi.h>
#include <fthook/session.h>
#include <fthook/pattern.h>
#include <toml.hpp>
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <atomic>
#include <array>
#include <algorithm>
#include <cstddef>
#include <climits>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <memory>
#include <vector>

namespace {
struct Signature { uint32_t rva; fthook::Pattern bytes; };
struct Profile {
    uint32_t timestamp{}, timeout{}, poll{}, assign{}, callback{}, app{}, connection{}, stage{}, stage_vtable{}, startup{}, patch{}, ready{}, connected{}, identity{}, endpoint_builder{}, ngs_init{}, ngs_pump{};
    std::vector<uint32_t> sizes, dependencies;
    std::vector<Signature> signatures;
};
struct State {
    const fthook_host* host{};
    Profile profile;
    uintptr_t base{};
    std::wstring folder, passport;
    std::string launch_id;
    HANDLE stop{}, worker{};
    std::atomic<HHOOK> hook{};
    std::atomic<bool> submitted{}, finished{};
    HWND window{};
    UINT message{};
    IN_ADDR address{};
    uint16_t port{};
    std::atomic<bool> endpoint_reported{};
    std::array<uint8_t, 5> endpoint_patch{};
    ~State() { if (!passport.empty()) SecureZeroMemory(passport.data(), passport.size() * sizeof(wchar_t)); }
};
SRWLOCK callback_lock = SRWLOCK_INIT;
State* current = nullptr;

bool read_file(const std::wstring& path, size_t limit, std::string& text) {
    std::ifstream stream(path, std::ios::binary | std::ios::ate);
    if (!stream || stream.tellg() < 0 || static_cast<uint64_t>(stream.tellg()) > limit) return false;
    text.resize(static_cast<size_t>(stream.tellg()));
    stream.seekg(0);
    return static_cast<bool>(stream.read(text.data(), static_cast<std::streamsize>(text.size())));
}

bool load_profile(const fthook_host* host, Profile& p) {
    std::string text;
    if (!host->plugin_directory || !read_file(fthook::widen(host->plugin_directory) + L"/native-login.toml", 65536, text)) return false;
    const auto parsed = toml::parse(text);
    if (!parsed || parsed["schema"].value<int>() != 1 || parsed["client"]["build"].value<std::string>() != host->build) return false;
    const auto* table = parsed["client"].as_table();
    if (!table) return false;
    if (parsed.table().size() != 3 || table->size() != 20) return false;
    auto get = [table](const char* key, uint32_t& value, uint32_t maximum) {
        const auto n = (*table)[key].value<int64_t>();
        if (!n || *n <= 0 || static_cast<uint64_t>(*n) > maximum) return false;
        value = static_cast<uint32_t>(*n); return true;
    };
    if (!get("pe_timestamp", p.timestamp, UINT32_MAX) || !get("timeout_ms", p.timeout, 120000)
        || !get("poll_ms", p.poll, 1000) || p.poll < 10) return false;
    for (auto [key, field] : {std::pair{"assign_rva", &p.assign}, {"callback_rva", &p.callback},
        {"app_slot_rva", &p.app}, {"connection_slot_rva", &p.connection}, {"stage_vtable_rva", &p.stage_vtable}, {"endpoint_builder_rva", &p.endpoint_builder},
        {"ngs_init_rva", &p.ngs_init}, {"ngs_pump_rva", &p.ngs_pump}})
        if (!get(key, *field, 0x10000000)) return false;
    for (auto [key, field] : {std::pair{"stage_offset", &p.stage}, {"startup_offset", &p.startup},
        {"patch_ready_offset", &p.patch}, {"app_ready_offset", &p.ready}, {"connected_offset", &p.connected}, {"identity_offset", &p.identity}})
        if (!get(key, *field, 65536)) return false;
    for (auto [key, target] : {std::pair{"image_sizes", &p.sizes}, {"dependency_slot_rvas", &p.dependencies}}) {
        const auto* list = (*table)[key].as_array();
        if (!list || list->empty() || list->size() > 8) return false;
        for (const auto& item : *list) {
            const auto n = item.value<int64_t>();
            if (!n || *n <= 0 || *n > 0x10000000) return false;
            target->push_back(static_cast<uint32_t>(*n));
        }
    }
    const auto* signatures = parsed["signatures"].as_array();
    if (!signatures || signatures->size() < 3 || signatures->size() > 16) return false;
    for (const auto& row : *signatures) {
        const auto* signature = row.as_table();
        if (!signature || signature->size() != 2) return false;
        const auto rva = (*signature)["rva"].value<int64_t>();
        const auto bytes = (*signature)["bytes"].value<std::string>();
        std::string why;
        if (!rva || *rva <= 0 || *rva > 0x10000000 || !bytes) return false;
        auto pattern = fthook::parse_pattern(*bytes, why);
        if (!pattern || pattern->size() < 8 || pattern->size() > 64) return false;
        p.signatures.push_back({static_cast<uint32_t>(*rva), std::move(*pattern)});
    }
    return true;
}

template<class T> bool read(State& s, uintptr_t address, T& value) {
    return s.host->read(address, &value, sizeof(value)) == FTHOOK_OK;
}

bool signatures_match(State& s, uint32_t* failed = nullptr) {
    BYTE bytes[64]{};
    for (const auto& signature : s.profile.signatures) {
        if (failed) *failed = signature.rva;
        MEMORY_BASIC_INFORMATION region{};
        if (!VirtualQuery(reinterpret_cast<void*>(s.base + signature.rva), &region, sizeof(region)) || region.State != MEM_COMMIT
            || (region.Protect & (PAGE_GUARD | PAGE_NOACCESS))
            || !(region.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY))) return false;
        if (s.host->read(s.base + signature.rva, bytes, static_cast<uint32_t>(signature.bytes.size())) != FTHOOK_OK) return false;
        if (signature.rva == s.profile.endpoint_builder && signature.bytes.size() >= s.endpoint_patch.size()
            && std::equal(s.endpoint_patch.begin(), s.endpoint_patch.end(), bytes))
            std::copy_n(signature.bytes.bytes.begin(), s.endpoint_patch.size(), bytes);
        if (!signature.bytes.matches(bytes)) return false;
    }
    return true;
}

bool ready(State& s, uintptr_t& app, uintptr_t& connection) {
    uintptr_t stage = 0, vtable = 0, dependency = 0;
    BYTE connected = 0, patch = 0, initialized = 0;
    if (!read(s, s.base + s.profile.app, app) || !app || !read(s, s.base + s.profile.connection, connection) || !connection
        || !read(s, app + s.profile.stage, stage) || !stage || !read(s, stage, vtable)
        || vtable != s.base + s.profile.stage_vtable) return false;
    for (const auto rva : s.profile.dependencies) if (!read(s, s.base + rva, dependency) || !dependency) return false;
    return read(s, app + s.profile.patch, patch) && read(s, app + s.profile.ready, initialized)
        && read(s, connection + s.profile.connected, connected)
        && korean_auth::can_submit(connected != 0, patch != 0, initialized != 0, true, s.submitted.load());
}

bool resolve_endpoint(const std::string& host, IN_ADDR& address) {
    if (host.empty() || host.size() > 253) return false;
    WSADATA data{};
    if (WSAStartup(MAKEWORD(2, 2), &data)) return false;
    const auto name = fthook::widen(host);
    bool ok = InetPtonW(AF_INET, name.c_str(), &address) == 1;
    if (!ok) {
        ADDRINFOEXW hints{}, *result = nullptr;
        hints.ai_family = AF_INET; hints.ai_socktype = SOCK_STREAM;
        timeval timeout{3, 0};
        const auto status = GetAddrInfoExW(name.c_str(), nullptr, NS_DNS, nullptr, &hints, &result, &timeout, nullptr, nullptr, nullptr);
        if (!status && result && result->ai_addrlen >= sizeof(sockaddr_in)) {
            address = reinterpret_cast<sockaddr_in*>(result->ai_addr)->sin_addr; ok = true;
        }
        if (result) FreeAddrInfoExW(result);
    }
    WSACleanup();
    return ok;
}

bool write_endpoint(uintptr_t output, IN_ADDR address, uint16_t port) {
    __try {
        *reinterpret_cast<IN_ADDR*>(output) = address;
        *reinterpret_cast<uint16_t*>(output + 4) = port;
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

// kr-1256 sub_87EA20 builds only initial/retry login destinations; relay addresses keep their own path.
uintptr_t __stdcall build_endpoint(uintptr_t output, uintptr_t) {
    AcquireSRWLockShared(&callback_lock);
    auto* s = current;
    const bool ok = s && write_endpoint(output, s->address, s->port);
    if (s && !s->endpoint_reported.exchange(true)) {
        char address[INET_ADDRSTRLEN]{}, message[160]{};
        InetNtopA(AF_INET, &s->address, address, sizeof(address));
        sprintf_s(message, "Korean authentication: selected game endpoint %s:%u", address, s->port);
        s->host->log(s->host->context, message);
    }
    ReleaseSRWLockShared(&callback_lock);
    return ok ? output : 0;
}

// kr-1256 sub_85EDB0 sends gspReqLogin only when the connection's passport wstring (+2516, length at +2532) is not
// empty; empty, it shows the "start the client from the website" notice. LoginStage calls it as soon as it exists,
// so the ticket is placed there first with the client's own allocating assignment (sub_40C2E0) and the client
// sends its own login once.
bool place_passport(uintptr_t assign, uintptr_t identity, const wchar_t* ticket, size_t length) {
    __try {
        reinterpret_cast<void*(__thiscall*)(void*, const wchar_t*, size_t)>(assign)(reinterpret_cast<void*>(identity), ticket, length);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

bool complete_startup(uintptr_t address) {
    __try { *reinterpret_cast<BYTE*>(address) = 1; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

LRESULT CALLBACK on_message(int code, WPARAM removed, LPARAM message) {
    AcquireSRWLockShared(&callback_lock);
    State* s = current;
    if (s && code == HC_ACTION && removed == PM_REMOVE) {
        auto* msg = reinterpret_cast<MSG*>(message);
        if (msg->hwnd == s->window && msg->message == s->message) {
            msg->message = WM_NULL;
            uintptr_t app = 0, connection = 0;
            BYTE connected = 0, patch = 0, startup = 0;
            if (!s->finished.load() && read(*s, s->base + s->profile.app, app) && app
                && read(*s, s->base + s->profile.connection, connection) && connection
                && read(*s, connection + s->profile.connected, connected) && connected
                && read(*s, app + s->profile.patch, patch) && patch
                && read(*s, app + s->profile.startup, startup) && !startup
                && complete_startup(app + s->profile.startup))
                s->host->log(s->host->context, "Korean authentication: managed startup completed after native GamePatch on the UI thread");
            uint32_t failed = 0, length = 0;
            uintptr_t stage = 0;
            if (!s->finished.load() && connection && connected && patch
                && read(*s, connection + s->profile.identity + 16, length) && read(*s, app + s->profile.stage, stage)) {
                if (stage || length) {
                    s->host->log(s->host->context, "Korean authentication: the login stage or a passport already exists; the ticket was not placed");
                    s->finished.store(true);
                } else if (!signatures_match(*s, &failed)) {
                    char text[128]{};
                    sprintf_s(text, "Korean authentication: native signature at rva 0x%X changed; the ticket was not placed", failed);
                    s->host->log(s->host->context, text);
                    s->finished.store(true);
                } else if (!s->submitted.exchange(true)) {
                    s->host->log(s->host->context, place_passport(s->base + s->profile.assign, connection + s->profile.identity,
                        s->passport.data(), s->passport.size())
                        ? "Korean authentication: launcher ticket placed as the client passport before the login stage"
                        : "Korean authentication: placing the launcher ticket failed; no retry will reuse it");
                    s->finished.store(true);
                }
            }
        }
    }
    ReleaseSRWLockShared(&callback_lock);
    return CallNextHookEx(nullptr, code, removed, message);
}

BOOL CALLBACK find_window(HWND window, LPARAM parameter) {
    auto& s = *reinterpret_cast<State*>(parameter);
    DWORD pid = 0;
    GetWindowThreadProcessId(window, &pid);
    if (pid == GetCurrentProcessId() && IsWindowVisible(window) && !GetWindow(window, GW_OWNER)) { s.window = window; return FALSE; }
    return TRUE;
}

bool host_ready(State& s) {
    std::string text;
    if (!read_file(s.folder + L"/fthook-status-" + fthook::widen(s.launch_id) + L".toml", 65536, text)) return false;
    const auto document = toml::parse(text);
    return document && document["launch_id"].value<std::string>() == s.launch_id
        && document["pid"].value<DWORD>() == GetCurrentProcessId() && document["state"].value<std::string>() == "ready";
}

DWORD WINAPI worker(void* context) {
    auto& s = *static_cast<State*>(context);
    const ULONGLONG deadline = GetTickCount64() + s.profile.timeout;
    unsigned previous = UINT_MAX, reports = 0;
    while (WaitForSingleObject(s.stop, s.profile.poll) == WAIT_TIMEOUT && GetTickCount64() < deadline && !s.finished.load()) {
        uintptr_t app = 0, connection = 0;
        const bool runtime_ready = host_ready(s);
        static_cast<void>(ready(s, app, connection));
        BYTE patch = 0, initialized = 0, connected = 0, startup = 0;
        uintptr_t stage = 0, vtable = 0, dependency = 0;
        bool dependencies = true;
        for (const auto rva : s.profile.dependencies) dependencies = dependencies && read(s, s.base + rva, dependency) && dependency;
        if (app) { read(s, app + s.profile.patch, patch); read(s, app + s.profile.ready, initialized); read(s, app + s.profile.startup, startup);
            read(s, app + s.profile.stage, stage); if (stage) read(s, stage, vtable); }
        if (connection) read(s, connection + s.profile.connected, connected);
        const unsigned flags = (app ? 1u : 0u) | (connection ? 2u : 0u) | (connected ? 4u : 0u) | (patch ? 8u : 0u)
            | (startup ? 16u : 0u) | (initialized ? 32u : 0u) | (vtable == s.base + s.profile.stage_vtable ? 64u : 0u)
            | (dependencies ? 128u : 0u) | (runtime_ready ? 256u : 0u);
        if (flags != previous && reports++ < 16) {
            char message[280]{};
            sprintf_s(message, "Korean authentication: app=%u connection=%u connected=%u patch=%u startup=%u ui=%u login_stage=%u dependencies=%u runtime=%u stage=%p vtable=%p expected=%p",
                app ? 1u : 0u, connection ? 1u : 0u, connected, patch, startup, initialized,
                vtable == s.base + s.profile.stage_vtable ? 1u : 0u, dependencies ? 1u : 0u, runtime_ready ? 1u : 0u,
                reinterpret_cast<void*>(stage), reinterpret_cast<void*>(vtable), reinterpret_cast<void*>(s.base + s.profile.stage_vtable));
            s.host->log(s.host->context, message); previous = flags;
        }
        if (!runtime_ready || !app || !connection || !connected || !patch) continue;
        // The patch window closes after GamePatch and the game window replaces it, on another thread or the same one.
        if (s.window && !IsWindow(s.window)) {
            if (const auto hook = s.hook.exchange(nullptr)) UnhookWindowsHookEx(hook);
            s.window = nullptr;
            s.host->log(s.host->context, "Korean authentication: the client window was replaced; following the new window");
        }
        if (!s.window) EnumWindows(find_window, reinterpret_cast<LPARAM>(&s));
        if (!s.window) continue;
        if (!s.hook.load()) {
            HMODULE module = nullptr;
            if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(&on_message), &module)) break;
            s.hook.store(SetWindowsHookExW(WH_GETMESSAGE, on_message, module, GetWindowThreadProcessId(s.window, nullptr)));
            if (!s.hook.load()) break;
        }
        if (!PostMessageW(s.window, s.message, 0, 0) && IsWindow(s.window)) break;
    }
    if (const auto hook = s.hook.exchange(nullptr)) UnhookWindowsHookEx(hook);
    if (!s.finished.load() && WaitForSingleObject(s.stop, 0) == WAIT_TIMEOUT)
        s.host->log(s.host->context, "Korean authentication: initialization timed out or UI dispatch failed; login was not submitted");
    s.finished.store(true);
    return 0;
}

// The launcher has no game-security service. kr-1256 sub_DABE90 initializes it and reports success as eax 0 with 257
// in dx; sub_DAC540, called from the frame loop sub_87C8E0, reads the queue that initialization would have created.
// Both become the success result without the service. Byte 6..9 of sub_DABE90 is a relocated SEH address.
bool stage_ngs(State& s) {
    static constexpr uint8_t init_expected[]{0x55, 0x8B, 0xEC, 0x6A, 0xFF, 0x68, 0, 0, 0, 0};
    static constexpr uint8_t init_mask[]{255, 255, 255, 255, 255, 255, 0, 0, 0, 0};
    static constexpr uint8_t init_stub[]{0x33, 0xC0, 0xBA, 0x01, 0x01, 0x00, 0x00, 0xC3, 0x90, 0x90};
    static constexpr uint8_t pump_expected[]{0x53, 0x8B, 0xDC};
    static constexpr uint8_t pump_mask[]{255, 255, 255};
    static constexpr uint8_t pump_stub[]{0x33, 0xC0, 0xC3};
    return s.host->stage_patch(s.host->context, s.base + s.profile.ngs_init, init_expected, init_mask, init_stub, sizeof(init_stub)) == FTHOOK_OK
        && s.host->stage_patch(s.host->context, s.base + s.profile.ngs_pump, pump_expected, pump_mask, pump_stub, sizeof(pump_stub)) == FTHOOK_OK;
}

int32_t FTHOOK_CALL cleanup(void* context) {
    auto* s = static_cast<State*>(context);
    if (!s) return FTHOOK_OK;
    if (s->stop) SetEvent(s->stop);
    if (s->worker && WaitForSingleObject(s->worker, 5000) != WAIT_OBJECT_0) return FTHOOK_FAILED;
    const auto deadline = GetTickCount64() + 5000;
    while (!TryAcquireSRWLockExclusive(&callback_lock)) {
        if (GetTickCount64() >= deadline) return FTHOOK_FAILED;
        Sleep(10);
    }
    if (current == s) current = nullptr;
    ReleaseSRWLockExclusive(&callback_lock);
    if (s->worker) CloseHandle(s->worker);
    if (s->stop) CloseHandle(s->stop);
    if (!s->passport.empty()) SecureZeroMemory(s->passport.data(), s->passport.size() * sizeof(wchar_t));
    delete s;
    return FTHOOK_OK;
}
}

extern "C" int32_t FTHOOK_CALL fthook_plugin_main(const fthook_host* host, fthook_plugin* plugin) {
    if (!host || !plugin || host->abi_version != FTHOOK_ABI_VERSION || host->struct_size < offsetof(fthook_host, stage_import)
        || !host->read || !host->log || !host->build) return FTHOOK_ABI_UNSUPPORTED;
    auto state = std::make_unique<State>();
    state->host = host;
    if (!load_profile(host, state->profile)) { host->log(host->context, "Korean authentication: missing or invalid native-login.toml"); return FTHOOK_INVALID; }
    state->base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    IMAGE_DOS_HEADER dos{}; IMAGE_NT_HEADERS32 pe{};
    if (!read(*state, state->base, dos) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < 0 || dos.e_lfanew > 1048576
        || !read(*state, state->base + dos.e_lfanew, pe) || pe.Signature != IMAGE_NT_SIGNATURE
        || pe.FileHeader.TimeDateStamp != state->profile.timestamp
        || std::find(state->profile.sizes.begin(), state->profile.sizes.end(), pe.OptionalHeader.SizeOfImage) == state->profile.sizes.end()) {
        host->log(host->context, "Korean authentication: unsupported client image fingerprint"); return FTHOOK_INVALID;
    }
    const auto image_size = pe.OptionalHeader.SizeOfImage;
    for (const auto rva : {state->profile.assign, state->profile.callback, state->profile.app, state->profile.connection, state->profile.stage_vtable, state->profile.endpoint_builder,
        state->profile.ngs_init, state->profile.ngs_pump})
        if (rva >= image_size - 64) return FTHOOK_INVALID;
    for (const auto rva : state->profile.dependencies) if (rva >= image_size - 4) return FTHOOK_INVALID;
    for (const auto& signature : state->profile.signatures) if (signature.rva >= image_size - 64) return FTHOOK_INVALID;
    wchar_t path[32768]{};
    const DWORD length = GetModuleFileNameW(nullptr, path, 32768);
    if (!length || length >= 32768) return FTHOOK_FAILED;
    state->folder = std::wstring(path, length);
    state->folder.resize(state->folder.find_last_of(L"\\/"));
    std::string error;
    auto session = fthook::load_session(state->folder + L"/session.toml", error);
    if (!session || session->launch_id.empty() || session->build != host->build || session->game != host->game
        || !korean_auth::make_passport(session->ticket, state->passport)) return FTHOOK_INVALID;
    SecureZeroMemory(session->ticket.data(), session->ticket.size());
    state->launch_id = session->launch_id;
    if (!session->endpoint_port || !resolve_endpoint(session->endpoint_host, state->address)) {
        host->log(host->context, "Korean authentication: game endpoint is missing or IPv4 resolution failed"); return FTHOOK_INVALID;
    }
    state->port = session->endpoint_port;
    const auto deadline = GetTickCount64() + state->profile.timeout;
    while (!signatures_match(*state)) {
        if (GetTickCount64() >= deadline) { host->log(host->context, "Korean authentication: unsupported or still-packed client signatures"); return FTHOOK_INVALID; }
        Sleep(state->profile.poll);
    }
    state->message = RegisterWindowMessageW(L"FantasyTale.KoreanAuthentication.Submit");
    state->stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!state->message || !state->stop) { if (state->stop) CloseHandle(state->stop); return FTHOOK_FAILED; }
    AcquireSRWLockExclusive(&callback_lock);
    if (current) { ReleaseSRWLockExclusive(&callback_lock); CloseHandle(state->stop); return FTHOOK_INVALID; }
    current = state.get();
    ReleaseSRWLockExclusive(&callback_lock);
    plugin->abi_version = FTHOOK_ABI_VERSION; plugin->struct_size = sizeof(*plugin);
    plugin->context = state.release(); plugin->cleanup = cleanup;
    auto* active = static_cast<State*>(plugin->context);
    const auto site = std::find_if(active->profile.signatures.begin(), active->profile.signatures.end(),
        [active](const Signature& item) { return item.rva == active->profile.endpoint_builder; });
    if (site == active->profile.signatures.end() || site->bytes.size() < 5
        || !std::all_of(site->bytes.mask.begin(), site->bytes.mask.begin() + 5, [](uint8_t mask) { return mask == 255; })) return FTHOOK_INVALID;
    uint8_t replacement[]{0xE9, 0, 0, 0, 0};
    const auto delta = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(&build_endpoint) - (active->base + active->profile.endpoint_builder + 5));
    std::memcpy(replacement + 1, &delta, sizeof(delta));
    std::copy_n(replacement, active->endpoint_patch.size(), active->endpoint_patch.begin());
    if (!host->stage_patch || host->stage_patch(host->context, active->base + active->profile.endpoint_builder,
        site->bytes.bytes.data(), site->bytes.mask.data(), replacement, sizeof(replacement)) != FTHOOK_OK) return FTHOOK_FAILED;
    if (!stage_ngs(*active)) return FTHOOK_FAILED;
    active->worker = CreateThread(nullptr, 0, worker, active, 0, nullptr);
    if (!active->worker) return FTHOOK_FAILED;
    host->log(host->context, "Korean authentication: verified; waiting for native connection, GamePatch and login stage");
    return FTHOOK_OK;
}

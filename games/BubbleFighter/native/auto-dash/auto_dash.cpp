#include <fthook/plugin_abi.h>
#include <fthook/pattern.h>
#include <fthook/session.h>
#include <toml.hpp>
#include <windows.h>
#include <atomic>
#include <fstream>
#include <memory>
#include <vector>
#include <cstdio>

namespace {
struct Profile {
    uint32_t timestamp{}, manager{}, index{}, list{}, list_end{}, slots{}, capacity{}, action{}, timer{}, timer_vtable{}, arm{}, poll{}, timeout{};
    fthook::Pattern signature;
};
struct State {
    const fthook_host* host{};
    Profile profile;
    uintptr_t base{};
    HANDLE stop{}, worker{};
    HHOOK hook{};
    HWND window{};
    DWORD window_thread{};
    UINT message{};
    std::atomic<bool> reported{}, failed{}, dispatched{};
};
SRWLOCK lock = SRWLOCK_INIT;
State* current = nullptr;

template<class T> bool read(uintptr_t address, T& value) {
    SIZE_T done = 0;
    return ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(address), &value, sizeof(value), &done) && done == sizeof(value);
}

enum class Implementation { invalid, patch, timer };
Implementation implementation(const toml::table& config, const char* build) {
    if (!build || config["schema"].value<int>() != 1) return Implementation::invalid;
    const auto mode = config["implementations"][build].value<std::string>();
    if (mode == "patch") return Implementation::patch;
    if (mode == "timer") return Implementation::timer;
    return Implementation::invalid;
}

bool profile(const fthook_host* host, Profile& p, bool& timer) {
    std::ifstream input(fthook::widen(host->plugin_directory) + L"/movement.toml", std::ios::binary | std::ios::ate);
    if (!input || input.tellg() <= 0 || input.tellg() > 16384) return false;
    std::string text(static_cast<size_t>(input.tellg()), '\0');
    input.seekg(0);
    if (!input.read(text.data(), static_cast<std::streamsize>(text.size()))) return false;
    const auto parsed = toml::parse(text);
    if (!parsed) return false;
    const auto mode = implementation(parsed.table(), host->build);
    if (mode == Implementation::invalid) return false;
    timer = mode == Implementation::timer;
    if (!timer) return true;
    if (parsed["client"]["build"].value<std::string>() != host->build) return false;
    auto field = [&](const char* key, uint32_t& out, uint32_t maximum) {
        const auto value = parsed["client"][key].value<int64_t>();
        if (!value || *value < 1 || static_cast<uint64_t>(*value) > maximum) return false;
        out = static_cast<uint32_t>(*value); return true;
    };
    if (!field("pe_timestamp", p.timestamp, UINT32_MAX) || !field("manager_rva", p.manager, 0x10000000)
        || !field("arm_rva", p.arm, 0x10000000) || !field("index_offset", p.index, 65536)
        || !field("list_offset", p.list, 65536) || !field("list_end_offset", p.list_end, 65536)
        || !field("slots_offset", p.slots, 65536) || !field("slot_capacity", p.capacity, 64)
        || !field("action_offset", p.action, 65536) || !field("timer_offset", p.timer, 65536)
        || !field("timer_vtable_rva", p.timer_vtable, 0x10000000)
        || !field("poll_ms", p.poll, 1000) || p.poll < 8 || !field("startup_timeout_ms", p.timeout, 120000)) return false;
    std::string error;
    const auto signature = fthook::parse_pattern(parsed["client"]["arm_signature"].value_or(std::string{}), error);
    if (!signature || !signature->is_exact() || signature->size() < 16 || signature->size() > 64) return false;
    p.signature = *signature;
    return true;
}

bool verified(const State& s) {
    IMAGE_DOS_HEADER dos{}; IMAGE_NT_HEADERS32 nt{};
    if (!read(s.base, dos) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < 0 || dos.e_lfanew > 4096
        || !read(s.base + static_cast<uint32_t>(dos.e_lfanew), nt) || nt.Signature != IMAGE_NT_SIGNATURE
        || nt.FileHeader.Machine != IMAGE_FILE_MACHINE_I386 || nt.FileHeader.TimeDateStamp != s.profile.timestamp
        || s.profile.arm >= nt.OptionalHeader.SizeOfImage || s.profile.manager >= nt.OptionalHeader.SizeOfImage - 4) return false;
    uint8_t bytes[64]{}; SIZE_T done = 0;
    return ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(s.base + s.profile.arm), bytes, s.profile.signature.size(), &done)
        && done == s.profile.signature.size() && s.profile.signature.matches(bytes);
}

// cn-279 sub_6224B0 resolves the locally controlled actor through this indexed list.
uintptr_t actor(const State& s) {
    uint32_t manager = 0, begin = 0, end = 0, selected = 0, player = 0, action = 0, timer_vtable = 0;
    int32_t index = -1;
    const auto& p = s.profile;
    if (!read(s.base + p.manager, manager) || !manager || !read(manager + p.index, index) || index < 0
        || !read(manager + p.list, begin) || !read(manager + p.list_end, end) || !begin || end < begin
        || (end - begin) % 4 || (end - begin) / 4 > p.capacity || static_cast<uint32_t>(index) >= (end - begin) / 4
        || !read(begin + static_cast<uint32_t>(index) * 4, selected) || selected >= p.capacity
        || !read(manager + p.slots + selected * 4, player) || !player || !read(player + p.action, action)
        || (action != 101 && action != 103) || !read(player + p.timer, timer_vtable)
        || timer_vtable != s.base + p.timer_vtable) return 0;
    return player;
}

bool arm(State& s, uintptr_t player) noexcept {
    // cn-279 sub_4ED110 arms this timer with 0x3FFFFFFF; native input clears it.
    // Run on the window thread so actor destruction cannot race the timer call.
    __try {
        using Arm = int(__thiscall*)(void*, int);
        reinterpret_cast<Arm>(s.base + s.profile.arm)(reinterpret_cast<void*>(player + s.profile.timer), 0x3FFFFFFF);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

LRESULT CALLBACK on_message(int code, WPARAM sent, LPARAM value) {
    AcquireSRWLockShared(&lock);
    auto* s = current;
    if (s && code >= 0 && !s->failed.load()) {
        const auto* message = reinterpret_cast<const CWPSTRUCT*>(value);
        if (message && message->message == s->message && message->hwnd == s->window) {
            s->dispatched.store(true);
            const auto player = actor(*s);
            if (player) {
                if (!arm(*s, player)) s->failed.store(true);
                else if (!s->reported.exchange(true)) s->host->log(s->host->context, "Auto Dash: native forward timer armed; executable instructions unchanged");
            }
        }
    }
    ReleaseSRWLockShared(&lock);
    return CallNextHookEx(nullptr, code, sent, value);
}

struct WindowSearch { HWND window{}; uint64_t area{}; };
BOOL CALLBACK find_window(HWND window, LPARAM argument) {
    auto& found = *reinterpret_cast<WindowSearch*>(argument);
    DWORD pid = 0; GetWindowThreadProcessId(window, &pid);
    RECT bounds{};
    if (pid == GetCurrentProcessId() && IsWindowVisible(window) && !GetWindow(window, GW_OWNER)
        && !(GetWindowLongPtrW(window, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) && GetClientRect(window, &bounds)) {
        const auto width = bounds.right - bounds.left, height = bounds.bottom - bounds.top;
        const auto area = width > 0 && height > 0 ? static_cast<uint64_t>(width) * static_cast<uint64_t>(height) : 0;
        if (area > found.area) { found.window = window; found.area = area; }
    }
    return TRUE;
}

bool attach_window(State& s) {
    WindowSearch found;
    EnumWindows(find_window, reinterpret_cast<LPARAM>(&found));
    if (!found.window) return false;
    const auto thread = GetWindowThreadProcessId(found.window, nullptr);
    if (s.hook && found.window == s.window && thread == s.window_thread) return true;
    if (s.hook && !UnhookWindowsHookEx(s.hook)) return false;
    s.hook = nullptr;
    AcquireSRWLockExclusive(&lock);
    s.window = found.window; s.window_thread = thread;
    ReleaseSRWLockExclusive(&lock);
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&on_message), &module)) return false;
    s.hook = SetWindowsHookExW(WH_CALLWNDPROC, on_message, module, thread);
    if (s.hook) s.host->log(s.host->context, "Auto Dash: game window dispatcher attached");
    return s.hook != nullptr;
}

DWORD WINAPI worker(void* argument) {
    auto& s = *static_cast<State*>(argument);
    const auto deadline = GetTickCount64() + s.profile.timeout;
    while (WaitForSingleObject(s.stop, s.profile.poll) == WAIT_TIMEOUT && GetTickCount64() < deadline) {
        if (!verified(s)) continue;
        if (attach_window(s)) break;
    }
    if (!s.hook) { s.host->log(s.host->context, "Auto Dash: native timer initialization failed or timed out"); return 0; }
    s.host->log(s.host->context, "Auto Dash: native timer dispatcher ready; no executable patch");
    auto refresh = GetTickCount64();
    auto report = refresh + 3000;
    unsigned reports = 0;
    while (WaitForSingleObject(s.stop, s.profile.poll) == WAIT_TIMEOUT && !s.failed.load()) {
        if (GetTickCount64() >= refresh) { attach_window(s); refresh = GetTickCount64() + 500; }
        if (s.hook && IsWindow(s.window) && !IsIconic(s.window)) {
            DWORD_PTR result = 0;
            // Direct dispatch reaches clients that filter custom messages out of PeekMessage.
            SendMessageTimeoutW(s.window, s.message, 0, 0, SMTO_ABORTIFHUNG | SMTO_BLOCK, 100, &result);
        }
        if (!s.reported.load() && GetTickCount64() >= report && reports++ < 6) {
            char message[160]{};
            sprintf_s(message, "Auto Dash: awaiting actor timer; callback=%u window=%u", s.dispatched.load() ? 1u : 0u, IsWindow(s.window) ? 1u : 0u);
            s.host->log(s.host->context, message); report = GetTickCount64() + 10000;
        }
    }
    UnhookWindowsHookEx(s.hook); s.hook = nullptr;
    if (s.failed.load()) s.host->log(s.host->context, "Auto Dash: timer dispatcher stopped after failure");
    return 0;
}

int32_t FTHOOK_CALL cleanup(void* argument) {
    auto* s = static_cast<State*>(argument);
    if (!s) return FTHOOK_OK;
    if (s->stop) SetEvent(s->stop);
    if (s->worker && WaitForSingleObject(s->worker, 5000) != WAIT_OBJECT_0) return FTHOOK_FAILED;
    AcquireSRWLockExclusive(&lock);
    if (current == s) current = nullptr;
    ReleaseSRWLockExclusive(&lock);
    if (s->worker) CloseHandle(s->worker);
    if (s->stop) CloseHandle(s->stop);
    delete s;
    return FTHOOK_OK;
}
}

extern "C" int32_t FTHOOK_CALL fthook_plugin_main(const fthook_host* host, fthook_plugin* plugin) {
    if (!host || !plugin || host->abi_version != FTHOOK_ABI_VERSION || host->struct_size < offsetof(fthook_host, stage_patch)
        || !host->option || !host->log || !host->plugin_directory) return FTHOOK_ABI_UNSUPPORTED;
    fthook_option option{};
    if (host->option(host->context, "auto_dash", &option) != FTHOOK_OK || option.kind != FTHOOK_OPTION_BOOL) return FTHOOK_INVALID;
    if (!option.integer) return FTHOOK_OK;
    auto s = std::make_unique<State>(); s->host = host; s->base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    bool timer = false;
    if (!profile(host, s->profile, timer)) return FTHOOK_INVALID;
    if (!timer) return FTHOOK_OK;
    s->message = RegisterWindowMessageW(L"FantasyTale.AutoDash.NativeTimer");
    s->stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!s->message || !s->stop) { if (s->stop) CloseHandle(s->stop); return FTHOOK_FAILED; }
    AcquireSRWLockExclusive(&lock);
    if (current) { ReleaseSRWLockExclusive(&lock); CloseHandle(s->stop); return FTHOOK_FAILED; }
    current = s.get(); ReleaseSRWLockExclusive(&lock);
    plugin->context = s.release(); plugin->cleanup = cleanup;
    auto* state = static_cast<State*>(plugin->context);
    state->worker = CreateThread(nullptr, 0, worker, state, 0, nullptr);
    return state->worker ? FTHOOK_OK : FTHOOK_FAILED;
}

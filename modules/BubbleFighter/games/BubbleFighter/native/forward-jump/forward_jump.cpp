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
    uint32_t timestamp{}, manager{}, index{}, list{}, list_end{}, slots{}, capacity{}, action{}, timer{}, timer_vtable{}, arm{}, resolver{}, capability{}, running_gate{}, poll{}, timeout{};
    fthook::Pattern signature, resolver_signature, running_gate_signature;
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
    uintptr_t changed_capability{};
    std::atomic<bool> running_gate_active{};
    uint32_t last_action{ UINT32_MAX };
    uint8_t last_input{};
    std::atomic<uint32_t> pending_action{ UINT32_MAX };
    std::atomic<uint8_t> pending_input{};
    std::atomic<bool> action_dirty{};
    bool overlay_panel{};
    bool dialog_panel{};
    bool dialog_shown{};
};
SRWLOCK lock = SRWLOCK_INIT;
State* current = nullptr;

template<class T> bool read(uintptr_t address, T& value) {
    SIZE_T done = 0;
    return ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(address), &value, sizeof(value), &done) && done == sizeof(value);
}

bool profile(const fthook_host* host, Profile& p) {
    std::ifstream input(fthook::widen(host->plugin_directory) + L"/movement.toml", std::ios::binary | std::ios::ate);
    if (!input || input.tellg() <= 0 || input.tellg() > 16384) return false;
    std::string text(static_cast<size_t>(input.tellg()), '\0');
    input.seekg(0);
    if (!input.read(text.data(), static_cast<std::streamsize>(text.size()))) return false;
    const auto parsed = toml::parse(text);
    if (!parsed || parsed["schema"].value<int>() != 1 || parsed["client"]["build"].value<std::string>() != host->build) return false;
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
        || !field("resolver_rva", p.resolver, 0x10000000) || !field("capability_offset", p.capability, 4096)
        || !field("running_gate_rva", p.running_gate, 0x10000000)
        || !field("poll_ms", p.poll, 1000) || p.poll < 8 || !field("startup_timeout_ms", p.timeout, 120000)) return false;
    std::string error;
    const auto signature = fthook::parse_pattern(parsed["client"]["arm_signature"].value_or(std::string{}), error);
    if (!signature || !signature->is_exact() || signature->size() < 16 || signature->size() > 64) return false;
    p.signature = *signature;
    const auto resolver = fthook::parse_pattern(parsed["client"]["resolver_signature"].value_or(std::string{}), error);
    if (!resolver || !resolver->is_exact() || resolver->size() < 16 || resolver->size() > 64) return false;
    p.resolver_signature = *resolver;
    const auto gate = fthook::parse_pattern(parsed["client"]["running_gate_signature"].value_or(std::string{}), error);
    if (!gate || !gate->is_exact() || gate->size() != 2) return false;
    p.running_gate_signature = *gate;
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
        && done == s.profile.signature.size() && s.profile.signature.matches(bytes)
        && s.profile.resolver < nt.OptionalHeader.SizeOfImage
        && ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(s.base + s.profile.resolver), bytes, s.profile.resolver_signature.size(), &done)
        && done == s.profile.resolver_signature.size() && s.profile.resolver_signature.matches(bytes)
        && s.profile.running_gate < nt.OptionalHeader.SizeOfImage
        && ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(s.base + s.profile.running_gate), bytes, s.profile.running_gate_signature.size(), &done)
        && done == s.profile.running_gate_signature.size() && s.profile.running_gate_signature.matches(bytes);
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
        || action > 1000 || !read(player + p.timer, timer_vtable)
        || timer_vtable != s.base + p.timer_vtable) return 0;
    return player;
}

// cn-279 sub_7DEF40 returns the effective local capability block in EAX from ESI.
bool resolve_capabilities(const State& s, uintptr_t player, uintptr_t& result) noexcept {
    const auto function = s.base + s.profile.resolver;
    uintptr_t resolved = 0;
    __try {
        __asm {
            mov esi, player
            call function
            mov resolved, eax
        }
        result = resolved;
        return resolved != 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}

bool enable_capability(uintptr_t address) noexcept {
    MEMORY_BASIC_INFORMATION page{};
    if (!VirtualQuery(reinterpret_cast<const void*>(address), &page, sizeof(page)) || page.State != MEM_COMMIT
        || page.Type == MEM_IMAGE || (page.Protect & (PAGE_GUARD | PAGE_NOACCESS))
        || (page.Protect & 0xff) != PAGE_READWRITE) return false;
    uint8_t before = 0;
    if (!read(address, before) || before > 1) return false;
    if (before == 1) return true;
    const uint8_t enabled = 1;
    SIZE_T written = 0;
    // cn-279 sub_651960 enables exactly this field for native effects 100171/100224.
    // sub_7CC230 and sub_731060 retain the original direction/action restrictions.
    return WriteProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(address), &enabled, 1, &written)
        && written == 1 && read(address, before) && before == enabled;
}

bool disable_capability(State& s) noexcept {
    if (!s.changed_capability) return true;
    uint8_t value = 0;
    if (!read(s.changed_capability, value)) return false;
    // Do not clear a value changed by the client or another effect after us.
    if (value == 1) {
        const uint8_t disabled = 0;
        SIZE_T written = 0;
        if (!WriteProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(s.changed_capability), &disabled, 1, &written)
            || written != 1) return false;
    }
    s.changed_capability = 0;
    return true;
}

bool set_running_gate(State& s, bool enabled) noexcept {
    if (s.running_gate_active.load() == enabled) return true;
    const auto address = s.base + s.profile.running_gate;
    uint8_t current_bytes[2]{};
    SIZE_T read_count = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(address), current_bytes, 2, &read_count)
        || read_count != 2) return false;
    const uint8_t expected[2] = { static_cast<uint8_t>(enabled ? 0x74 : 0xEB), 0x13 };
    const uint8_t replacement[2] = { static_cast<uint8_t>(enabled ? 0xEB : 0x74), 0x13 };
    if (current_bytes[0] != expected[0] || current_bytes[1] != expected[1]) return false;
    DWORD old_protection = 0;
    if (!VirtualProtect(reinterpret_cast<void*>(address), 2, PAGE_EXECUTE_READWRITE, &old_protection)) return false;
    SIZE_T written = 0;
    const auto wrote = WriteProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(address), replacement, 2, &written)
        && written == 2;
    FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<const void*>(address), 2);
    DWORD ignored = 0;
    VirtualProtect(reinterpret_cast<void*>(address), 2, old_protection, &ignored);
    if (wrote) s.running_gate_active.store(enabled);
    return wrote;
}

bool arm(State& s, uintptr_t player, bool input_requested) noexcept {
    uint32_t state = 0;
    if (!read(player + s.profile.action, state)) return false;
    if (state == 301) {
        if (!disable_capability(s)) return false;
        return set_running_gate(s, input_requested);
    }
    if (!set_running_gate(s, false)) return false;
    uintptr_t capabilities = 0;
    if (!resolve_capabilities(s, player, capabilities) || capabilities > UINTPTR_MAX - s.profile.capability) return false;
    const auto address = capabilities + s.profile.capability;
    if (state == 104 || !input_requested) return disable_capability(s);
    // cn-279 uses 101/102/103 for ordinary standing, walking and running
    // movement; 104 is the native roll state and must remain excluded.
    if (state != 101 && state != 102 && state != 103) return true;
    uint8_t before = 0;
    if (!read(address, before) || before > 1) return false;
    if (before == 1) return true;
    if (!enable_capability(address)) return false;
    s.changed_capability = address;
    return true;
}

void report_action(State& s, uintptr_t player, bool forward, bool jump) noexcept {
    uint32_t action = 0;
    if (!read(player + s.profile.action, action)) return;
    const uint8_t input = static_cast<uint8_t>((forward ? 1 : 0) | (jump ? 2 : 0));
    if (action == s.last_action && input == s.last_input) return;
    s.last_action = action;
    s.last_input = input;
    s.pending_action.store(action, std::memory_order_relaxed);
    s.pending_input.store(input, std::memory_order_relaxed);
    s.action_dirty.store(true, std::memory_order_release);
    char message[160]{};
    sprintf_s(message, "Forward Jump: action_state=%u forward=%u jump=%u", action,
        forward ? 1u : 0u, jump ? 1u : 0u);
    s.host->log(s.host->context, message);
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
                const auto forward = (GetAsyncKeyState('W') & 0x8000) != 0
                    || (GetAsyncKeyState(VK_UP) & 0x8000) != 0;
                const auto jump = (GetAsyncKeyState(VK_SPACE) & 0x8000) != 0
                    || (GetAsyncKeyState(VK_NUMPAD0) & 0x8000) != 0;
                report_action(*s, player, forward, jump);
                if (!arm(*s, player, forward && jump)) s->failed.store(true);
                else if (!s->reported.exchange(true)) s->host->log(s->host->context, "Forward Jump: native walking and running jump paths active");
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
    if (s.hook) s.host->log(s.host->context, "Forward Jump: game window dispatcher attached");
    return s.hook != nullptr;
}

DWORD WINAPI worker(void* argument) {
    auto& s = *static_cast<State*>(argument);
    const auto deadline = GetTickCount64() + s.profile.timeout;
    while (WaitForSingleObject(s.stop, s.profile.poll) == WAIT_TIMEOUT && GetTickCount64() < deadline) {
        if (!verified(s)) continue;
        if (attach_window(s)) break;
    }
    if (!s.hook) { s.host->log(s.host->context, "Forward Jump: native capability initialization failed or timed out"); return 0; }
    s.host->log(s.host->context, "Forward Jump: native walking capability and guarded running gate ready");
    static constexpr char panel_id[] = "action-state-diagnostic";
    static constexpr char dialog_id[] = "action-state-dialog";
    static constexpr char panel_html[] =
        "<style>:host{left:50%;top:18%;transform:translateX(-50%);pointer-events:none}"
        ".diag{min-width:300px;padding:16px 20px;border:2px solid #d7b56d;border-radius:8px;"
        "background:rgba(24,20,32,.94);color:#fff0c2;font:16px sans-serif;box-shadow:0 8px 28px #0008}"
        ".title{font-size:20px;font-weight:700;margin-bottom:8px}.value{white-space:pre-line}</style>"
        "<div class=diag><div class=title>动作诊断</div><div class=value data-overlay-text=text>等待角色状态…</div></div>";
    if (s.host->overlay && s.host->overlay->register_panel && s.host->overlay->show_panel
        && s.host->overlay->register_panel(s.host->overlay->context, panel_id, panel_html, nullptr) == FTHOOK_OK) {
        if (s.host->overlay->show_panel(s.host->overlay->context, panel_id) == FTHOOK_OK) s.overlay_panel = true;
        else s.host->overlay->unregister_panel(s.host->overlay->context, panel_id);
    }
    if (s.host->overlay && s.host->overlay->register_panel &&
        s.host->overlay->register_panel(s.host->overlay->context, dialog_id,
            "<div data-game-ui-root></div>", nullptr) == FTHOOK_OK) s.dialog_panel = true;
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
        if (s.overlay_panel && s.action_dirty.exchange(false, std::memory_order_acq_rel)) {
            const auto action = s.pending_action.load(std::memory_order_relaxed);
            const auto input = s.pending_input.load(std::memory_order_relaxed);
            if (s.dialog_panel && !s.dialog_shown && s.host->overlay->send_panel) {
                char dialog[768]{};
                sprintf_s(dialog, "{\"title\":\"动作诊断\",\"text\":\"当前动作状态: %u\\nW/上方向: %s\\n空格/小键盘0: %s\",\"buttons\":[{\"name\":\"ok\",\"label\":\"确定\"}]}", action,
                    (input & 1) != 0 ? "按下" : "未按下", (input & 2) != 0 ? "按下" : "未按下");
                if (s.host->overlay->send_panel(s.host->overlay->context, dialog_id, "game-ui.message-box", dialog) == FTHOOK_OK)
                    s.dialog_shown = true;
            }
            char data[512]{};
            sprintf_s(data, "{\"text\":\"动作状态: %u\\nW/上方向: %s\\n空格/小键盘0: %s\"}", action,
                (input & 1) != 0 ? "按下" : "未按下", (input & 2) != 0 ? "按下" : "未按下");
            if (s.host->overlay->send_panel(s.host->overlay->context, panel_id, "html.update", data) != FTHOOK_OK)
                s.overlay_panel = false;
        }
        if (!s.reported.load() && GetTickCount64() >= report && reports++ < 6) {
            char message[160]{};
            sprintf_s(message, "Forward Jump: awaiting actor capability; callback=%u window=%u", s.dispatched.load() ? 1u : 0u, IsWindow(s.window) ? 1u : 0u);
            s.host->log(s.host->context, message); report = GetTickCount64() + 10000;
        }
    }
    UnhookWindowsHookEx(s.hook); s.hook = nullptr;
    set_running_gate(s, false);
    if (s.overlay_panel && s.host->overlay && s.host->overlay->unregister_panel)
        s.host->overlay->unregister_panel(s.host->overlay->context, panel_id);
    if (s.dialog_panel && s.host->overlay && s.host->overlay->unregister_panel)
        s.host->overlay->unregister_panel(s.host->overlay->context, dialog_id);
    if (s.failed.load()) s.host->log(s.host->context, "Forward Jump: capability dispatcher stopped after failure");
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
    if (host->option(host->context, "forward_jump", &option) != FTHOOK_OK || option.kind != FTHOOK_OPTION_BOOL) return FTHOOK_INVALID;
    if (!option.integer) return FTHOOK_OK;
    auto s = std::make_unique<State>(); s->host = host; s->base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    if (!profile(host, s->profile)) return FTHOOK_INVALID;
    s->message = RegisterWindowMessageW(L"FantasyTale.ForwardJump.NativeCapability");
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

#include <fthook/plugin_abi.h>
#include <windows.h>
#include <tlhelp32.h>
#include <cstdint>
#include <string>
#include "dump_writer.h"
#include "window_snapshot.h"
#include "packet_trace.h"

namespace {
constexpr size_t kModuleCapacity = 128;
constexpr size_t kMapCapacity = 5;
constexpr size_t kStackBytes = 4096;
constexpr size_t kStackHits = 12;
struct Module { uintptr_t base; uint32_t size; char name[96]; };
struct ModuleMap { Module entries[kModuleCapacity]; size_t count; };
ModuleMap maps[kMapCapacity]{};
volatile LONG map_index = -1, refresh_requested = 0, armed = 0, busy = 0, active = 0;
HANDLE file = INVALID_HANDLE_VALUE, stop_event = nullptr, worker = nullptr;
PVOID observer = nullptr;
uint32_t record_limit = 32, byte_limit = 1024 * 1024, records = 0, written = 0;
uintptr_t main_base = 0;
uint32_t main_size = 0;
bool external_privileged = false, ignore_privileged = false, full = false;
bool first_fault_seen = false;
HMODULE self = nullptr;
// One slot per UI thread; a client has a handful, and the fixed table keeps the hook procedures allocation-free.
constexpr size_t kWatchCapacity = 16;
struct Watch { DWORD thread; HHOOK sent; HHOOK posted; };
Watch watches[kWatchCapacity]{};
size_t watch_count = 0;
bool watch_full_reported = false;

// A fixed buffer avoids heap allocation and CRT formatting locks on the faulting thread.
struct Line {
    char data[4096]{};
    size_t count = 0;
    void text(const char* value) { for (size_t i = 0; value[i] && count < sizeof(data) - 1; ++i) data[count++] = value[i]; }
    void hex(uintptr_t value) {
        const char digits[] = "0123456789ABCDEF";
        for (int shift = 28; shift >= 0 && count < sizeof(data) - 1; shift -= 4) data[count++] = digits[(value >> shift) & 15];
    }
    void number(uint32_t value) {
        char digits[10]; size_t n = 0;
        do { digits[n++] = static_cast<char>('0' + value % 10); value /= 10; } while (value && n < sizeof(digits));
        while (n && count < sizeof(data) - 1) data[count++] = digits[--n];
    }
};
Line line;
// The newest observation survives an exhausted record budget so a self-terminating client still reports it at exit.
Line last;
constexpr DWORD kCxxException = 0xE06D7363u;
constexpr size_t kExitStackBytes = 65536;
constexpr size_t kExitStackHits = 64;
// Held back from the exception budget so the exit record always fits in the bounded file.
constexpr uint32_t kExitReserve = 8192;

bool safe_read(uintptr_t address, void* destination, size_t size) noexcept {
    __try {
        auto* out = static_cast<unsigned char*>(destination);
        auto* in = reinterpret_cast<const volatile unsigned char*>(address);
        for (size_t i = 0; i < size; ++i) out[i] = in[i];
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
void append(const char* data, uint32_t size) noexcept {
    if (full || file == INVALID_HANDLE_VALUE) return;
    constexpr char capped[] = "recording stopped: byte limit reached\r\n";
    if (size > byte_limit - written || byte_limit - written - size < sizeof(capped)) {
        DWORD count = 0;
        if (sizeof(capped) - 1 <= byte_limit - written)
            WriteFile(file, capped, static_cast<DWORD>(sizeof(capped) - 1), &count, nullptr);
        written += count;
        full = true;
        return;
    }
    DWORD count = 0;
    if (!WriteFile(file, data, size, &count, nullptr) || count != size) {
        OutputDebugStringA("FantasyTale crash recorder: diagnostic write failed\n");
        full = true;
    }
    written += count;
}
const Module* module_at(const ModuleMap* map, uintptr_t address) noexcept {
    if (map) for (size_t i = 0; i < map->count; ++i)
        if (address >= map->entries[i].base && address - map->entries[i].base < map->entries[i].size) return &map->entries[i];
    return nullptr;
}
bool snapshot(size_t index) {
    HANDLE handle = INVALID_HANDLE_VALUE;
    // The loader can change its module list while the native runtime starts.
    for (unsigned attempt = 0; attempt < 5; ++attempt) {
        handle = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, GetCurrentProcessId());
        if (handle != INVALID_HANDLE_VALUE || GetLastError() != ERROR_BAD_LENGTH) break;
        Sleep(10);
    }
    if (handle == INVALID_HANDLE_VALUE) return false;
    MODULEENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    auto& map = maps[index];
    map.count = 0;
    BOOL more = Module32FirstW(handle, &entry);
    while (more && map.count < kModuleCapacity) {
        auto& module = map.entries[map.count++];
        module.base = reinterpret_cast<uintptr_t>(entry.modBaseAddr);
        module.size = entry.modBaseSize;
        if (!WideCharToMultiByte(CP_UTF8, 0, entry.szModule, -1, module.name, sizeof(module.name), nullptr, nullptr))
            module.name[0] = '?', module.name[1] = 0;
        more = Module32NextW(handle, &entry);
    }
    const DWORD error = map.count ? ERROR_SUCCESS : GetLastError();
    CloseHandle(handle);
    if (!map.count) { SetLastError(error); return false; }
    InterlockedExchange(&map_index, static_cast<LONG>(index));
    return true;
}
void watch_windows();
DWORD WINAPI refresh_modules(void*) {
    size_t next = 1;
    watch_windows();
    while (WaitForSingleObject(stop_event, 1000) == WAIT_TIMEOUT) {
        if (next < kMapCapacity && InterlockedExchange(&refresh_requested, 0) && snapshot(next)) ++next;
        watch_windows();
    }
    return 0;
}
void describe_address(const ModuleMap* map, uintptr_t address) {
    if (const Module* module = module_at(map, address)) {
        line.text(" module="); line.text(module->name);
        line.text(" module_base="); line.hex(module->base);
        line.text(" module_rva="); line.hex(address - module->base);
    } else {
        MEMORY_BASIC_INFORMATION region{};
        if (VirtualQuery(reinterpret_cast<const void*>(address), &region, sizeof(region))) {
            const auto base = reinterpret_cast<uintptr_t>(region.AllocationBase);
            line.text(" allocation_base="); line.hex(base);
            line.text(" allocation_offset="); line.hex(address - base);
        }
        line.text(" module=not-in-snapshot");
        InterlockedExchange(&refresh_requested, 1);
    }
}
// MSVC x86 throw: ExceptionInformation[1] is the object, [2] the ThrowInfo whose +12 catchable-type array names it.
void describe_cxx(const EXCEPTION_RECORD* record) {
    if (record->NumberParameters < 3) return;
    uintptr_t types = 0, first = 0, descriptor = 0;
    if (!safe_read(record->ExceptionInformation[2] + 12, &types, sizeof(types))
        || !safe_read(types + 4, &first, sizeof(first)) || !safe_read(first + 4, &descriptor, sizeof(descriptor))) return;
    char name[96]{};
    if (!safe_read(descriptor + 8, name, sizeof(name) - 1)) return;
    line.text(" cxx_type=");
    for (size_t i = 0; name[i] && name[i] >= 0x20 && name[i] < 0x7F; ++i) { const char c[2] = {name[i], 0}; line.text(c); }
    // std::exception keeps its message pointer right after the vtable.
    uintptr_t what = 0;
    char message[160]{};
    if (!safe_read(record->ExceptionInformation[1] + 4, &what, sizeof(what)) || !what
        || !safe_read(what, message, sizeof(message) - 1)) return;
    line.text(" what=\"");
    for (size_t i = 0; message[i] && message[i] >= 0x20 && message[i] < 0x7F; ++i) { const char c[2] = {message[i], 0}; line.text(c); }
    line.text("\"");
}
void scan_stack(const ModuleMap* map, uintptr_t start, size_t bytes, size_t limit) {
    uintptr_t top = 0;
    safe_read(reinterpret_cast<uintptr_t>(NtCurrentTeb()) + FIELD_OFFSET(NT_TIB, StackBase), &top, sizeof(top));
    start &= ~uintptr_t{3};
    size_t hits = 0;
    for (size_t offset = 0; start && offset < bytes && hits < limit; offset += sizeof(uintptr_t)) {
        if (start > UINTPTR_MAX - offset - sizeof(uintptr_t) || (top && start + offset + sizeof(uintptr_t) > top)) break;
        uintptr_t value = 0;
        if (!safe_read(start + offset, &value, sizeof(value))) break;
        const Module* module = module_at(map, value);
        if (!module) continue;
        line.text(" esp+"); line.hex(offset); line.text("="); line.hex(value);
        line.text("@"); line.text(module->name); line.text("+"); line.hex(value - module->base);
        ++hits;
    }
}
const ModuleMap* current_map() noexcept {
    const LONG index = InterlockedCompareExchange(&map_index, 0, 0);
    return index >= 0 ? &maps[index] : nullptr;
}
void describe_dispatch(const ModuleMap* map) {
    // Stack scans alone cannot distinguish return addresses from saved floating-point state or ordinary data.
    void* frames[12]{};
    const auto count = CaptureStackBackTrace(0, 12, frames, nullptr);
    line.text("dispatcher_frames");
    for (USHORT i = 0; i < count; ++i) {
        const auto address = reinterpret_cast<uintptr_t>(frames[i]);
        line.text(" frame="); line.hex(address); describe_address(map, address);
    }
    line.text("\r\nseh_chain");
    uintptr_t entry = 0;
    safe_read(reinterpret_cast<uintptr_t>(NtCurrentTeb()) + FIELD_OFFSET(NT_TIB, ExceptionList), &entry, sizeof(entry));
    uintptr_t seen[8]{};
    for (size_t i = 0; entry && entry != UINTPTR_MAX && i < 8; ++i) {
        bool repeated = false;
        for (size_t j = 0; j < i; ++j) repeated |= seen[j] == entry;
        if (repeated) { line.text(" cycle"); break; }
        seen[i] = entry;
        uintptr_t registration[2]{};
        line.text(" entry="); line.hex(entry);
        if (!safe_read(entry, registration, sizeof(registration))) { line.text(" unreadable"); break; }
        line.text(" handler="); line.hex(registration[1]); describe_address(map, registration[1]);
        entry = registration[0];
    }
    line.text("\r\n");
}
void record(EXCEPTION_POINTERS* exception) {
    if (!exception || !exception->ExceptionRecord) return;
    const auto* record = exception->ExceptionRecord;
    const auto* context = exception->ContextRecord;
    const DWORD code = record->ExceptionCode;
    // C++ throws are kept: an uncaught one reaches the client's own crash reporter, which ends the process itself.
    if ((code & 0xC0000000u) != 0xC0000000u && code != kCxxException) return;
    // A diagnostic package can reserve its bounded record budget for actual faults.
    if (ignore_privileged && code == EXCEPTION_PRIV_INSTRUCTION) return;
    const uintptr_t address = reinterpret_cast<uintptr_t>(record->ExceptionAddress);
    if (code == EXCEPTION_PRIV_INSTRUCTION && !external_privileged
        && !(address >= main_base && address - main_base < main_size)) return;
    const bool budget = records < record_limit && !full;
    const bool serious = code == EXCEPTION_ACCESS_VIOLATION || code == EXCEPTION_ILLEGAL_INSTRUCTION
        || code == EXCEPTION_STACK_OVERFLOW || code == EXCEPTION_IN_PAGE_ERROR || code == EXCEPTION_INT_DIVIDE_BY_ZERO;
    const bool first_fault = serious && !first_fault_seen;
    first_fault_seen |= serious;
    if (budget) ++records;
    const ModuleMap* map = current_map();
    line.count = 0;
    line.text("observed exception code="); line.hex(code);
    line.text(" flags="); line.hex(record->ExceptionFlags);
    line.text(" tid="); line.number(GetCurrentThreadId());
    line.text(" tick_ms_low="); line.number(GetTickCount());
    line.text(" address="); line.hex(address);
    line.text(" image_base="); line.hex(main_base);
    describe_address(map, address);
    if ((code == EXCEPTION_ACCESS_VIOLATION || code == EXCEPTION_IN_PAGE_ERROR) && record->NumberParameters >= 2) {
        line.text(" operation="); line.number(static_cast<uint32_t>(record->ExceptionInformation[0]));
        line.text(" target="); line.hex(record->ExceptionInformation[1]);
    }
    if (code == kCxxException) describe_cxx(record);
    line.text("\r\n");
    if (context) {
        line.text("context_flags="); line.hex(context->ContextFlags);
        line.text(" eflags="); line.hex(context->EFlags);
        line.text(" cs="); line.hex(context->SegCs);
        line.text(" ss="); line.hex(context->SegSs);
        line.text("\r\n");
        line.text("registers eip="); line.hex(context->Eip);
        line.text(" esp="); line.hex(context->Esp); line.text(" ebp="); line.hex(context->Ebp);
        line.text(" eax="); line.hex(context->Eax); line.text(" ebx="); line.hex(context->Ebx);
        line.text(" ecx="); line.hex(context->Ecx); line.text(" edx="); line.hex(context->Edx);
        line.text(" esi="); line.hex(context->Esi); line.text(" edi="); line.hex(context->Edi);
        line.text("\r\nstack_candidates");
        scan_stack(map, context->Esp, kStackBytes, kStackHits);
        line.text("\r\n");
    }
    if (first_fault) describe_dispatch(map);
    last = line;
    // Packed startup can exhaust the observation count before the first serious fault arrives.
    if (!budget && !first_fault) return;
    if (budget && records == record_limit) line.text("recording stopped: record limit reached; the newest exception is still kept for exit\r\n");
    append(line.data, static_cast<uint32_t>(line.count));
}
// DLL_PROCESS_DETACH for ExitProcess runs on the exiting thread, so its stack shows who ended the process.
void record_exit() noexcept {
    __try {
        byte_limit += kExitReserve;
        full = false;
        if (last.count) {
            constexpr char heading[] = "newest exception before exit:\r\n";
            append(heading, sizeof(heading) - 1);
            append(last.data, static_cast<uint32_t>(last.count));
        }
        line.count = 0;
        line.text("process exit tid="); line.number(GetCurrentThreadId());
        line.text(" tick_ms_low="); line.number(GetTickCount());
        line.text(" recorded_exceptions="); line.number(records);
        line.text("\r\nexit_stack_candidates");
        uintptr_t here = reinterpret_cast<uintptr_t>(&here);
        scan_stack(current_map(), here, kExitStackBytes, kExitStackHits);
        line.text("\r\n");
        append(line.data, static_cast<uint32_t>(line.count));
        if (file != INVALID_HANDLE_VALUE) FlushFileBuffers(file);
    } __except (EXCEPTION_EXECUTE_HANDLER) {}
}
const char* window_message_name(UINT message, WPARAM wparam) noexcept {
    switch (message) {
    case WM_CLOSE: return "WM_CLOSE";
    case WM_DESTROY: return "WM_DESTROY";
    case WM_QUIT: return "WM_QUIT";
    case WM_ENDSESSION: return "WM_ENDSESSION";
    case WM_SYSCOMMAND: return (wparam & 0xFFF0) == SC_CLOSE ? "WM_SYSCOMMAND(SC_CLOSE)" : nullptr;
    default: return nullptr;
    }
}
// A client that ends itself leaves no exception and an exit stack that only shows the runtime's exit path. A sent
// close or destroy runs this hook on the requesting call chain, so the frames name the code that decided to quit.
void record_window(const char* source, HWND window, UINT message, WPARAM wparam, LPARAM lparam) noexcept {
    const char* name = window_message_name(message, wparam);
    if (!name || (window && GetAncestor(window, GA_ROOT) != window)) return;
    if (!InterlockedCompareExchange(&armed, 0, 0)) return;
    if (InterlockedCompareExchange(&busy, 1, 0)) return;
    __try {
        const ModuleMap* map = current_map();
        line.count = 0;
        line.text("window event message="); line.text(name); line.text(" source="); line.text(source);
        line.text(" hwnd="); line.hex(reinterpret_cast<uintptr_t>(window));
        char type[64]{};
        if (window && GetClassNameA(window, type, sizeof(type))) { line.text(" class="); line.text(type); }
        line.text(" tid="); line.number(GetCurrentThreadId());
        line.text(" tick_ms_low="); line.number(GetTickCount());
        line.text(" wparam="); line.hex(static_cast<uintptr_t>(wparam)); line.text(" lparam="); line.hex(static_cast<uintptr_t>(lparam));
        line.text("\r\ncaller_frames");
        void* frames[32]{};
        const auto count = CaptureStackBackTrace(0, 32, frames, nullptr);
        for (USHORT i = 0; i < count; ++i) {
            const auto address = reinterpret_cast<uintptr_t>(frames[i]);
            line.text(" frame="); line.hex(address); describe_address(map, address);
        }
        line.text("\r\nstack_candidates");
        uintptr_t here = reinterpret_cast<uintptr_t>(&here);
        scan_stack(map, here, kExitStackBytes, kExitStackHits);
        line.text("\r\n");
        append(line.data, static_cast<uint32_t>(line.count));
        if (file != INVALID_HANDLE_VALUE) FlushFileBuffers(file);
    } __except (EXCEPTION_EXECUTE_HANDLER) {}
    InterlockedExchange(&busy, 0);
    // Window destruction during startup must not consume the later exception snapshot.
    if (requests_close_snapshot(message, wparam)) request_snapshot();
}
LRESULT CALLBACK sent_message(int code, WPARAM wparam, LPARAM lparam) {
    if (code == HC_ACTION && lparam) {
        const auto* sent = reinterpret_cast<const CWPSTRUCT*>(lparam);
        record_window("sent", sent->hwnd, sent->message, sent->wParam, sent->lParam);
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
LRESULT CALLBACK posted_message(int code, WPARAM wparam, LPARAM lparam) {
    // A peek without removal would report the same posted message again when it is finally taken.
    if (code == HC_ACTION && wparam == PM_REMOVE && lparam) {
        const auto* posted = reinterpret_cast<const MSG*>(lparam);
        record_window("posted", posted->hwnd, posted->message, posted->wParam, posted->lParam);
    }
    return CallNextHookEx(nullptr, code, wparam, lparam);
}
BOOL CALLBACK watch_window(HWND window, LPARAM) {
    DWORD process = 0;
    const DWORD thread = GetWindowThreadProcessId(window, &process);
    if (process != GetCurrentProcessId() || GetWindow(window, GW_OWNER)) return TRUE;
    for (size_t i = 0; i < watch_count; ++i) if (watches[i].thread == thread) return TRUE;
    if (watch_count == kWatchCapacity) {
        if (!watch_full_reported) {
            constexpr char note[] = "window watch: every slot is in use; later window threads are not watched\r\n";
            while (InterlockedCompareExchange(&busy, 1, 0)) Sleep(1);
            append(note, sizeof(note) - 1);
            InterlockedExchange(&busy, 0);
            watch_full_reported = true;
        }
        return TRUE;
    }
    Watch watch{thread, SetWindowsHookExW(WH_CALLWNDPROC, sent_message, self, thread),
        SetWindowsHookExW(WH_GETMESSAGE, posted_message, self, thread)};
    if (!watch.sent && !watch.posted) return TRUE;
    watches[watch_count++] = watch;
    Line note;
    note.text("window watch thread="); note.number(thread);
    note.text(watch.sent ? " sent=hooked" : " sent=unavailable"); note.text(watch.posted ? " posted=hooked" : " posted=unavailable");
    note.text("\r\n");
    while (InterlockedCompareExchange(&busy, 1, 0)) Sleep(1);
    append(note.data, static_cast<uint32_t>(note.count));
    InterlockedExchange(&busy, 0);
    return TRUE;
}
// Runs on the worker so hooks follow windows the client creates after activation.
void watch_windows() {
    if (InterlockedCompareExchange(&armed, 0, 0)) EnumWindows(watch_window, 0);
}
void unwatch_windows() {
    for (size_t i = 0; i < watch_count; ++i) {
        if (watches[i].sent) UnhookWindowsHookEx(watches[i].sent);
        if (watches[i].posted) UnhookWindowsHookEx(watches[i].posted);
    }
    watch_count = 0;
}
LONG CALLBACK observe(EXCEPTION_POINTERS* exception) {
    InterlockedIncrement(&active);
    if (InterlockedCompareExchange(&armed, 0, 0) && InterlockedCompareExchange(&busy, 1, 0) == 0) {
        __try { record(exception); request_dump(exception); }
        __except (EXCEPTION_EXECUTE_HANDLER) { OutputDebugStringA("FantasyTale crash recorder: fault state unreadable\n"); }
        InterlockedExchange(&busy, 0);
    }
    InterlockedDecrement(&active);
    return EXCEPTION_CONTINUE_SEARCH;
}
int32_t FTHOOK_CALL cleanup(void*) {
    InterlockedExchange(&armed, 0);
    if (observer) {
        if (!RemoveVectoredExceptionHandler(observer)) return FTHOOK_FAILED;
        observer = nullptr;
    }
    while (InterlockedCompareExchange(&active, 0, 0)) Sleep(1);
    stop_packet_trace();
    stop_dump_writer();
    if (stop_event) SetEvent(stop_event);
    if (worker) {
        if (WaitForSingleObject(worker, INFINITE) != WAIT_OBJECT_0) return FTHOOK_FAILED;
        CloseHandle(worker); worker = nullptr;
    }
    // The worker installs the hooks, so they are removed only after it stops; an in-flight hook call then drains.
    unwatch_windows();
    while (InterlockedCompareExchange(&busy, 0, 0)) Sleep(1);
    if (stop_event) { CloseHandle(stop_event); stop_event = nullptr; }
    if (file != INVALID_HANDLE_VALUE) { CloseHandle(file); file = INVALID_HANDLE_VALUE; }
    return FTHOOK_OK;
}
bool integer(const fthook_host* host, const char* key, uint32_t kind, int64_t& value) {
    fthook_option option{};
    if (host->option(host->context, key, &option) != FTHOOK_OK || option.kind != kind) return false;
    value = option.integer;
    return true;
}
}

extern "C" int32_t FTHOOK_CALL fthook_plugin_main(const fthook_host* host, fthook_plugin* plugin) {
    if (!host || !plugin || host->abi_version != FTHOOK_ABI_VERSION || host->struct_size < sizeof(fthook_host))
        return FTHOOK_ABI_UNSUPPORTED;
    plugin->abi_version = FTHOOK_ABI_VERSION;
    plugin->struct_size = sizeof(fthook_plugin);
    plugin->context = nullptr;
    plugin->cleanup = cleanup;
    int64_t enabled = 0, limit = 0, kib = 0, external = 0, dumps = 0, dump_mib = 0, packets = 0, packet_mib = 0;
    if (!integer(host, "enabled", FTHOOK_OPTION_BOOL, enabled)
        || !integer(host, "max_records", FTHOOK_OPTION_INT, limit) || limit < 1 || limit > 128
        || !integer(host, "max_kib", FTHOOK_OPTION_INT, kib) || kib < 16 || kib > 1024
        || !integer(host, "external_privileged", FTHOOK_OPTION_BOOL, external)
        || !integer(host, "write_dump", FTHOOK_OPTION_BOOL, dumps)
        || !integer(host, "max_dump_mib", FTHOOK_OPTION_INT, dump_mib) || dump_mib < 1 || dump_mib > 64
        || !integer(host, "receive_packets", FTHOOK_OPTION_BOOL, packets)
        || !integer(host, "max_packet_mib", FTHOOK_OPTION_INT, packet_mib) || packet_mib < 1 || packet_mib > 64) return FTHOOK_INVALID;
    if (!enabled) { host->log(host->context, "crash recorder disabled by option"); return FTHOOK_OK; }
    record_limit = static_cast<uint32_t>(limit);
    byte_limit = static_cast<uint32_t>(kib) * 1024 - kExitReserve;
    external_privileged = external != 0;
    fthook_option filter{};
    const auto filter_status = host->option(host->context, "ignore_privileged", &filter);
    if (filter_status == FTHOOK_OK) {
        if (filter.kind != FTHOOK_OPTION_BOOL) return FTHOOK_INVALID;
        ignore_privileged = filter.integer != 0;
    } else if (filter_status != FTHOOK_NOT_FOUND) return FTHOOK_INVALID;
    const std::string log(host->log_path);
    const size_t slash = log.find_last_of("/\\");
    if (slash == std::string::npos) { host->log(host->context, "crash recorder needs a runtime log directory"); return FTHOOK_INVALID; }
    const std::string path = log.substr(0, slash + 1) + "crash.log";
    const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path.c_str(), -1, nullptr, 0);
    if (!count) return FTHOOK_INVALID;
    std::wstring wide(static_cast<size_t>(count), 0);
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path.c_str(), -1, wide.data(), count);
    file = CreateFileW(wide.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) { host->log(host->context, "crash.log could not be opened"); return FTHOOK_FAILED; }
    const auto folder = wide.substr(0, wide.find_last_of(L"/\\") + 1);
    if (dumps && !start_dump_writer(folder + L"exception.dmp", static_cast<unsigned>(dump_mib))) {
        host->log(host->context, "minidump writer initialization failed"); return FTHOOK_FAILED;
    }
    if (packets && !start_packet_trace(host, folder + L"receive.ftnet", static_cast<unsigned>(packet_mib))) {
        host->log(host->context, "receive trace initialization failed"); return FTHOOK_FAILED;
    }
    main_base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    IMAGE_DOS_HEADER dos{};
    IMAGE_NT_HEADERS32 nt{};
    if (!safe_read(main_base, &dos, sizeof(dos)) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < 0
        || !safe_read(main_base + static_cast<uint32_t>(dos.e_lfanew), &nt, sizeof(nt)) || nt.Signature != IMAGE_NT_SIGNATURE)
        return FTHOOK_FAILED;
    main_size = nt.OptionalHeader.SizeOfImage;
    if (!snapshot(0)) {
        char message[160]{};
        const DWORD error = GetLastError();
        wsprintfA(message, "initial module snapshot unavailable (Windows error %lu); exception capture remains active", error);
        host->log(host->context, message);
        maps[0].count = 1;
        maps[0].entries[0] = {main_base, main_size, "main-image"};
        InterlockedExchange(&map_index, 0);
        InterlockedExchange(&refresh_requested, 1);
    }
    line.count = 0;
    line.text("FantasyTale exception observations; handled exceptions can also appear here.\r\n");
    line.text("build="); line.text(host->build); line.text(" timestamp="); line.hex(nt.FileHeader.TimeDateStamp); line.text("\r\n");
    append(line.data, static_cast<uint32_t>(line.count));
    stop_event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!stop_event) return FTHOOK_FAILED;
    worker = CreateThread(nullptr, 0, refresh_modules, nullptr, 0, nullptr);
    if (!worker) return FTHOOK_FAILED;
    InterlockedExchange(&armed, 1);
    observer = AddVectoredExceptionHandler(1, observe);
    if (!observer) return FTHOOK_FAILED;
    host->log(host->context, "crash recorder armed; bounded crash.log beside the runtime log");
    return FTHOOK_OK;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved) {
    // Window hooks name the module that holds their procedures.
    if (reason == DLL_PROCESS_ATTACH) self = instance;
    // A non-null reserved pointer marks process termination rather than FreeLibrary.
    if (reason == DLL_PROCESS_DETACH && reserved && InterlockedCompareExchange(&armed, 0, 0)) record_exit();
    return TRUE;
}

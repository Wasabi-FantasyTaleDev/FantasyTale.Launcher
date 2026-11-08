#include <fthook/plugin_abi.h>
#include <windows.h>
#include <tlhelp32.h>
#include <cstdint>
#include <string>
#include "dump_writer.h"
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
DWORD WINAPI refresh_modules(void*) {
    size_t next = 1;
    while (WaitForSingleObject(stop_event, 1000) == WAIT_TIMEOUT) {
        if (next < kMapCapacity && InterlockedExchange(&refresh_requested, 0) && snapshot(next)) ++next;
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
void record(EXCEPTION_POINTERS* exception) {
    if (!exception || !exception->ExceptionRecord) return;
    const auto* record = exception->ExceptionRecord;
    const auto* context = exception->ContextRecord;
    const DWORD code = record->ExceptionCode;
    if ((code & 0xC0000000u) != 0xC0000000u || code == 0xE06D7363u) return;
    // A diagnostic package can reserve its bounded record budget for actual faults.
    if (ignore_privileged && code == EXCEPTION_PRIV_INSTRUCTION) return;
    const uintptr_t address = reinterpret_cast<uintptr_t>(record->ExceptionAddress);
    if (code == EXCEPTION_PRIV_INSTRUCTION && !external_privileged
        && !(address >= main_base && address - main_base < main_size)) return;
    if (records >= record_limit || full) return;
    ++records;
    const LONG index = InterlockedCompareExchange(&map_index, 0, 0);
    const ModuleMap* map = index >= 0 ? &maps[index] : nullptr;
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
    line.text("\r\n");
    if (context) {
        line.text("registers eip="); line.hex(context->Eip);
        line.text(" esp="); line.hex(context->Esp); line.text(" ebp="); line.hex(context->Ebp);
        line.text(" eax="); line.hex(context->Eax); line.text(" ebx="); line.hex(context->Ebx);
        line.text(" ecx="); line.hex(context->Ecx); line.text(" edx="); line.hex(context->Edx);
        line.text(" esi="); line.hex(context->Esi); line.text(" edi="); line.hex(context->Edi);
        line.text("\r\nstack_candidates");
        uintptr_t top = 0;
        safe_read(reinterpret_cast<uintptr_t>(NtCurrentTeb()) + FIELD_OFFSET(NT_TIB, StackBase), &top, sizeof(top));
        const uintptr_t start = context->Esp & ~uintptr_t{3};
        size_t hits = 0;
        for (size_t offset = 0; start && offset < kStackBytes && hits < kStackHits; offset += sizeof(uintptr_t)) {
            if (start > UINTPTR_MAX - offset - sizeof(uintptr_t) || (top && start + offset + sizeof(uintptr_t) > top)) break;
            uintptr_t value = 0;
            if (!safe_read(start + offset, &value, sizeof(value))) break;
            const Module* module = module_at(map, value);
            if (!module) continue;
            line.text(" esp+"); line.hex(offset); line.text("="); line.hex(value);
            line.text("@"); line.text(module->name); line.text("+"); line.hex(value - module->base);
            ++hits;
        }
        line.text("\r\n");
    }
    if (records == record_limit) line.text("recording stopped: record limit reached\r\n");
    append(line.data, static_cast<uint32_t>(line.count));
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
    byte_limit = static_cast<uint32_t>(kib) * 1024;
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

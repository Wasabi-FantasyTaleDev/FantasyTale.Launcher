#include "dump_writer.h"
#include <dbghelp.h>
#include <cstdio>
#include <cstdint>
#include <array>
#include <algorithm>

namespace {
using WriteDump = BOOL(WINAPI*)(HANDLE, DWORD, HANDLE, MINIDUMP_TYPE, PMINIDUMP_EXCEPTION_INFORMATION,
    PMINIDUMP_USER_STREAM_INFORMATION, PMINIDUMP_CALLBACK_INFORMATION);
HMODULE library = nullptr;
WriteDump write_dump = nullptr;
HANDLE requested = nullptr, completed = nullptr, thread = nullptr, output = INVALID_HANDLE_VALUE;
HANDLE status_file = INVALID_HANDLE_VALUE;
uint32_t status_bytes = 0;
bool limit_hit = false;
volatile LONG claimed = 0, stopping = 0;
DWORD writer_id = 0, fault_thread = 0;
bool snapshot_only = false;
EXCEPTION_RECORD record{};
CONTEXT context{};
EXCEPTION_POINTERS pointers{&record, &context};
std::wstring final_path, temporary_path;
uint64_t maximum_bytes = 0;
uintptr_t image_begin = 0, image_end = 0, image_cursor = 0;
uint64_t image_remaining = 0, image_captured = 0;
// Indirectly referenced memory keeps the small regions that thread stacks point at, such as the packet buffer a
// dispatcher was reading when it faulted; without it a fault inside a handler leaves only the pointer. The
// io callback still enforces the size limit.
constexpr MINIDUMP_TYPE kDumpType = static_cast<MINIDUMP_TYPE>(MiniDumpNormal | MiniDumpWithThreadInfo
    | MiniDumpWithUnloadedModules | MiniDumpWithIndirectlyReferencedMemory);

void status(const char* stage, DWORD error = ERROR_SUCCESS) {
    char text[160]{};
    const int length = _snprintf_s(text, sizeof(text), _TRUNCATE, "%s error=%lu\r\n", stage, error);
    if (length <= 0 || status_file == INVALID_HANDLE_VALUE || static_cast<uint32_t>(length) > 1024 - status_bytes) return;
    DWORD written = 0;
    if (!WriteFile(status_file, text, static_cast<DWORD>(length), &written, nullptr) || written != static_cast<DWORD>(length)
        || !FlushFileBuffers(status_file)) OutputDebugStringA("FantasyTale dump status write failed\n");
    status_bytes += written;
}

void prepare_image_capture() noexcept {
    image_begin = image_end = image_cursor = 0;
    image_remaining = maximum_bytes / 2;
    image_captured = 0;
    const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    IMAGE_DOS_HEADER dos{};
    IMAGE_NT_HEADERS32 nt{};
    SIZE_T copied = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(base), &dos, sizeof(dos), &copied)
        || copied != sizeof(dos) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < 0 || dos.e_lfanew > 4096
        || !ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<const void*>(base + static_cast<uint32_t>(dos.e_lfanew)),
            &nt, sizeof(nt), &copied) || copied != sizeof(nt) || nt.Signature != IMAGE_NT_SIGNATURE
        || nt.FileHeader.Machine != IMAGE_FILE_MACHINE_I386 || nt.OptionalHeader.SizeOfImage < 4096
        || nt.OptionalHeader.SizeOfImage > UINTPTR_MAX - base) return;
    image_begin = image_cursor = base;
    image_end = base + nt.OptionalHeader.SizeOfImage;
}

bool next_image_memory(PMINIDUMP_CALLBACK_OUTPUT result) noexcept {
    while (image_cursor < image_end && image_remaining) {
        MEMORY_BASIC_INFORMATION region{};
        if (!VirtualQuery(reinterpret_cast<const void*>(image_cursor), &region, sizeof(region))) return false;
        const auto region_begin = reinterpret_cast<uintptr_t>(region.BaseAddress);
        if (region.RegionSize > UINTPTR_MAX - region_begin) return false;
        const auto region_end = (std::min)(region_begin + static_cast<uintptr_t>(region.RegionSize), image_end);
        if (region_end <= image_cursor) return false;
        const auto begin = image_cursor;
        image_cursor = region_end;
        const bool readable = (region.Protect & (PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY
            | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;
        if (region.State != MEM_COMMIT || !readable || (region.Protect & (PAGE_GUARD | PAGE_NOACCESS))
            || reinterpret_cast<uintptr_t>(region.AllocationBase) != image_begin) continue;
        const auto length = (std::min)(static_cast<uint64_t>(region_end - begin), image_remaining);
        result->MemoryBase = begin;
        result->MemorySize = static_cast<ULONG>(length);
        image_remaining -= length;
        image_captured += length;
        image_cursor = begin + static_cast<uintptr_t>(length);
        return true;
    }
    return false;
}

BOOL CALLBACK io(void*, const PMINIDUMP_CALLBACK_INPUT input, PMINIDUMP_CALLBACK_OUTPUT result) {
    if (input->CallbackType == MemoryCallback) return next_image_memory(result) ? TRUE : FALSE;
    if (input->CallbackType == IoStartCallback) result->Status = S_FALSE;
    else if (input->CallbackType == IoWriteAllCallback) {
        if (input->Io.Offset > maximum_bytes || input->Io.BufferBytes > maximum_bytes - input->Io.Offset) {
            limit_hit = true;
            result->Status = HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE); return TRUE;
        }
        LARGE_INTEGER offset{}; offset.QuadPart = static_cast<LONGLONG>(input->Io.Offset);
        DWORD written = 0;
        const bool ok = SetFilePointerEx(output, offset, nullptr, FILE_BEGIN)
            && WriteFile(output, input->Io.Buffer, input->Io.BufferBytes, &written, nullptr) && written == input->Io.BufferBytes;
        result->Status = ok ? S_OK : HRESULT_FROM_WIN32(ERROR_WRITE_FAULT);
    }
    else if (input->CallbackType == IoFinishCallback) result->Status = S_OK;
    return TRUE;
}
DWORD WINAPI run(void*) {
    WaitForSingleObject(requested, INFINITE);
    if (!InterlockedCompareExchange(&stopping, 0, 0)) {
        status("writing");
        output = CreateFileW(temporary_path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (output != INVALID_HANDLE_VALUE) {
            // Include bounded runtime code/data so a null call in protected client code can be investigated offline.
            // Missing or guarded image pages remain absent; no heap traversal or executable write is performed.
            prepare_image_capture();
            MINIDUMP_EXCEPTION_INFORMATION exception{fault_thread, &pointers, FALSE};
            MINIDUMP_CALLBACK_INFORMATION callback{io, nullptr};
            bool dumped = write_dump(GetCurrentProcess(), GetCurrentProcessId(), output,
                kDumpType, snapshot_only ? nullptr : &exception, nullptr, &callback) != FALSE;
            DWORD error = dumped ? ERROR_SUCCESS : GetLastError();
            bool without_exception = false;
            // An unusable exception context must not discard the remaining process and thread evidence.
            if (!dumped && error == ERROR_INVALID_PARAMETER && !limit_hit && !snapshot_only) {
                status("retry-without-exception", error);
                LARGE_INTEGER beginning{};
                if (SetFilePointerEx(output, beginning, nullptr, FILE_BEGIN) && SetEndOfFile(output)) {
                    prepare_image_capture();
                    dumped = write_dump(GetCurrentProcess(), GetCurrentProcessId(), output,
                        kDumpType,
                        nullptr, nullptr, &callback) != FALSE;
                    error = dumped ? ERROR_SUCCESS : GetLastError();
                    without_exception = dumped;
                } else error = GetLastError();
            }
            const bool flushed = dumped && FlushFileBuffers(output);
            if (dumped && !flushed) error = GetLastError();
            if (dumped) status(!image_captured ? "image-memory-not-captured"
                : image_cursor < image_end ? "image-memory-partial" : "image-memory-complete");
            CloseHandle(output); output = INVALID_HANDLE_VALUE;
            if (dumped && flushed) {
                if (MoveFileExW(temporary_path.c_str(), final_path.c_str(), MOVEFILE_WRITE_THROUGH))
                    status(snapshot_only ? "written-window-close" : without_exception ? "written-without-exception" : "written");
                else status("failed-publish", GetLastError());
            } else {
                status(limit_hit ? "failed-size-limit" : dumped ? "failed-flush" : "failed-write",
                    limit_hit ? ERROR_FILE_TOO_LARGE : error);
                if (!DeleteFileW(temporary_path.c_str())) status("failed-remove-partial", GetLastError());
            }
        }
        else status("failed-create", GetLastError());
    }
    SetEvent(completed);
    return 0;
}
}

bool start_dump_writer(const std::wstring& path, unsigned maximum_mib) {
    final_path = path; temporary_path = path + L".tmp";
    maximum_bytes = static_cast<uint64_t>(maximum_mib) * 1024 * 1024;
    status_file = CreateFileW((path + L".status.txt").c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (status_file == INVALID_HANDLE_VALUE) return false;
    status("initializing");
    std::array<wchar_t, 32768> system_directory{};
    const auto length = GetSystemDirectoryW(system_directory.data(), static_cast<UINT>(system_directory.size()));
    if (!length || length >= system_directory.size()) {
        status("failed-system-directory", length ? ERROR_INSUFFICIENT_BUFFER : GetLastError()); return false;
    }
    // A bare name reuses an already loaded game-local DLL before the loader applies search flags.
    const auto system_library = std::wstring(system_directory.data(), length) + L"\\dbghelp.dll";
    library = LoadLibraryExW(system_library.c_str(), nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!library) { status("failed-library", GetLastError()); return false; }
    status("system-library-loaded");
    write_dump = reinterpret_cast<WriteDump>(GetProcAddress(library, "MiniDumpWriteDump"));
    if (!write_dump) { status("failed-export", GetLastError()); return false; }
    requested = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!requested) { status("failed-event", GetLastError()); return false; }
    completed = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!completed) { status("failed-event", GetLastError()); return false; }
    thread = CreateThread(nullptr, 0, run, nullptr, 0, &writer_id);
    if (thread) status("armed");
    else status("failed-thread", GetLastError());
    return thread != nullptr;
}

void request_dump(EXCEPTION_POINTERS* exception) noexcept {
    if (!thread || GetCurrentThreadId() == writer_id || !exception || !exception->ExceptionRecord || !exception->ContextRecord) return;
    const auto code = exception->ExceptionRecord->ExceptionCode;
    if (code != EXCEPTION_ACCESS_VIOLATION && code != EXCEPTION_ILLEGAL_INSTRUCTION && code != EXCEPTION_STACK_OVERFLOW
        && code != EXCEPTION_IN_PAGE_ERROR && code != EXCEPTION_INT_DIVIDE_BY_ZERO) return;
    if (InterlockedCompareExchange(&claimed, 1, 0)) return;
    record = *exception->ExceptionRecord; record.ExceptionRecord = nullptr;
    context = *exception->ContextRecord; fault_thread = GetCurrentThreadId();
    SetEvent(requested);
    // An exception snapshot can precede application handling; it is not proof of an unhandled crash.
    WaitForSingleObject(completed, 10000);
}

void request_snapshot() noexcept {
    if (!thread || GetCurrentThreadId() == writer_id || InterlockedCompareExchange(&claimed, 1, 0)) return;
    snapshot_only = true; fault_thread = GetCurrentThreadId();
    SetEvent(requested);
    // This is called from the window-message hook. The UI cannot wait for all-thread capture.
}

void stop_dump_writer() {
    InterlockedExchange(&stopping, 1);
    if (requested) SetEvent(requested);
    if (thread) { WaitForSingleObject(thread, INFINITE); CloseHandle(thread); thread = nullptr; }
    if (requested) { CloseHandle(requested); requested = nullptr; }
    if (completed) { CloseHandle(completed); completed = nullptr; }
    if (library) { FreeLibrary(library); library = nullptr; }
    if (status_file != INVALID_HANDLE_VALUE) { CloseHandle(status_file); status_file = INVALID_HANDLE_VALUE; }
}

#include "dump_writer.h"
#include <dbghelp.h>
#include <cstdio>
#include <cstdint>

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
EXCEPTION_RECORD record{};
CONTEXT context{};
EXCEPTION_POINTERS pointers{&record, &context};
std::wstring final_path, temporary_path;
uint64_t maximum_bytes = 0;

void status(const char* stage, DWORD error = ERROR_SUCCESS) {
    char text[160]{};
    const int length = _snprintf_s(text, sizeof(text), _TRUNCATE, "%s error=%lu\r\n", stage, error);
    if (length <= 0 || status_file == INVALID_HANDLE_VALUE || static_cast<uint32_t>(length) > 1024 - status_bytes) return;
    DWORD written = 0;
    if (!WriteFile(status_file, text, static_cast<DWORD>(length), &written, nullptr) || written != static_cast<DWORD>(length)
        || !FlushFileBuffers(status_file)) OutputDebugStringA("FantasyTale dump status write failed\n");
    status_bytes += written;
}

BOOL CALLBACK io(void*, const PMINIDUMP_CALLBACK_INPUT input, PMINIDUMP_CALLBACK_OUTPUT result) {
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
            MINIDUMP_EXCEPTION_INFORMATION exception{fault_thread, &pointers, FALSE};
            MINIDUMP_CALLBACK_INFORMATION callback{io, nullptr};
            const bool dumped = write_dump(GetCurrentProcess(), GetCurrentProcessId(), output,
                static_cast<MINIDUMP_TYPE>(MiniDumpNormal | MiniDumpWithThreadInfo | MiniDumpWithUnloadedModules), &exception, nullptr, &callback) != FALSE;
            DWORD error = dumped ? ERROR_SUCCESS : GetLastError();
            const bool flushed = dumped && FlushFileBuffers(output);
            if (dumped && !flushed) error = GetLastError();
            CloseHandle(output); output = INVALID_HANDLE_VALUE;
            if (dumped && flushed) {
                if (MoveFileExW(temporary_path.c_str(), final_path.c_str(), MOVEFILE_WRITE_THROUGH)) status("written");
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
    library = LoadLibraryExW(L"dbghelp.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!library) { status("failed-library", GetLastError()); return false; }
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

void stop_dump_writer() {
    InterlockedExchange(&stopping, 1);
    if (requested) SetEvent(requested);
    if (thread) { WaitForSingleObject(thread, INFINITE); CloseHandle(thread); thread = nullptr; }
    if (requested) { CloseHandle(requested); requested = nullptr; }
    if (completed) { CloseHandle(completed); completed = nullptr; }
    if (library) { FreeLibrary(library); library = nullptr; }
    if (status_file != INVALID_HANDLE_VALUE) { CloseHandle(status_file); status_file = INVALID_HANDLE_VALUE; }
}

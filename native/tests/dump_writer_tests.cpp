#include "../../games/BubbleFighter/native/crash-recorder/dump_writer.cpp"
#include "../../games/BubbleFighter/native/crash-recorder/window_snapshot.h"
#include "check.h"
#include <filesystem>
#include <fstream>
#include <iterator>
#include <cstring>

namespace {
int fixture_mode = 0;
int fixture_calls = 0;
WriteDump system_writer = nullptr;
HANDLE fixture_entered = nullptr, fixture_release = nullptr, snapshot_returned = nullptr;

DWORD WINAPI queue_snapshot(void*) {
    request_snapshot();
    SetEvent(snapshot_returned);
    return 0;
}

BOOL WINAPI reject_context(HANDLE process, DWORD process_id, HANDLE file_handle, MINIDUMP_TYPE type,
    PMINIDUMP_EXCEPTION_INFORMATION exception, PMINIDUMP_USER_STREAM_INFORMATION streams,
    PMINIDUMP_CALLBACK_INFORMATION callback) {
    if (exception) { SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }
    return system_writer(process, process_id, file_handle, type, exception, streams, callback);
}

BOOL WINAPI write_fixture(HANDLE, DWORD, HANDLE, MINIDUMP_TYPE, PMINIDUMP_EXCEPTION_INFORMATION exception,
    PMINIDUMP_USER_STREAM_INFORMATION, PMINIDUMP_CALLBACK_INFORMATION callback) {
    ++fixture_calls;
    if (fixture_mode == 9) {
        SetEvent(fixture_entered);
        WaitForSingleObject(fixture_release, 10000);
    }
    if (fixture_mode == 1) { SetLastError(ERROR_WRITE_FAULT); return FALSE; }
    if ((fixture_mode == 5 || fixture_mode == 8) && exception) { SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }
    if (fixture_mode == 6) { SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }
    char bytes[64]{};
    MINIDUMP_CALLBACK_INPUT input{};
    MINIDUMP_CALLBACK_OUTPUT result{};
    input.CallbackType = IoWriteAllCallback;
    input.Io.Offset = fixture_mode == 2 || fixture_mode == 8 ? maximum_bytes - 2 : 0;
    input.Io.Buffer = bytes;
    input.Io.BufferBytes = fixture_mode == 7 && !exception ? 8 : sizeof(bytes);
    CHECK(callback->CallbackRoutine(callback->CallbackParam, &input, &result));
    if (FAILED(result.Status)) { SetLastError(ERROR_CANCELLED); return FALSE; }
    if (fixture_mode == 7 && exception) { SetLastError(ERROR_INVALID_PARAMETER); return FALSE; }
    return TRUE;
}

std::string read_text(const std::filesystem::path& path) {
    std::ifstream input(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>()};
}

bool contains_memory(const std::string& bytes, uintptr_t address) {
    MINIDUMP_HEADER header{};
    if (bytes.size() < sizeof(header)) return false;
    std::memcpy(&header, bytes.data(), sizeof(header));
    if (header.StreamDirectoryRva > bytes.size()
        || header.NumberOfStreams > (bytes.size() - header.StreamDirectoryRva) / sizeof(MINIDUMP_DIRECTORY)) return false;
    for (ULONG32 i = 0; i < header.NumberOfStreams; ++i) {
        MINIDUMP_DIRECTORY entry{};
        std::memcpy(&entry, bytes.data() + header.StreamDirectoryRva + i * sizeof(entry), sizeof(entry));
        if (entry.StreamType != MemoryListStream) continue;
        const auto offset = entry.Location.Rva;
        if (offset > bytes.size() || sizeof(ULONG32) > bytes.size() - offset) return false;
        ULONG32 count = 0;
        std::memcpy(&count, bytes.data() + offset, sizeof(count));
        if (count > (bytes.size() - offset - sizeof(count)) / sizeof(MINIDUMP_MEMORY_DESCRIPTOR)) return false;
        for (ULONG32 j = 0; j < count; ++j) {
            MINIDUMP_MEMORY_DESCRIPTOR memory{};
            std::memcpy(&memory, bytes.data() + offset + sizeof(count) + j * sizeof(memory), sizeof(memory));
            if (address >= memory.StartOfMemoryRange && address - memory.StartOfMemoryRange < memory.Memory.DataSize
                && memory.Memory.Rva <= bytes.size() && memory.Memory.DataSize <= bytes.size() - memory.Memory.Rva) return true;
        }
    }
    return false;
}
}

int main() {
    // Explicit image ranges skip unreadable pages and use at most half of the configured total dump cap.
    {
        auto* pages = static_cast<unsigned char*>(VirtualAlloc(nullptr, 3 * 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
        CHECK(pages != nullptr);
        if (pages) {
            DWORD old = 0;
            CHECK(VirtualProtect(pages + 4096, 4096, PAGE_NOACCESS, &old));
            image_begin = image_cursor = reinterpret_cast<uintptr_t>(pages);
            image_end = image_begin + 3 * 4096;
            image_remaining = 3 * 4096; image_captured = 0;
            MINIDUMP_CALLBACK_OUTPUT range{};
            CHECK(next_image_memory(&range));
            CHECK(range.MemoryBase == image_begin && range.MemorySize == 4096);
            CHECK(next_image_memory(&range));
            CHECK(range.MemoryBase == image_begin + 2 * 4096 && range.MemorySize == 4096);
            CHECK(!next_image_memory(&range));
            CHECK(image_captured == 2 * 4096);
            image_cursor = image_begin; image_remaining = 1024; image_captured = 0;
            CHECK(next_image_memory(&range));
            CHECK(range.MemoryBase == image_begin && range.MemorySize == 1024);
            CHECK(!next_image_memory(&range));
            CHECK(image_captured == 1024 && image_cursor < image_end);
            CHECK(VirtualFree(pages, 0, MEM_RELEASE));
        }
        maximum_bytes = 16 * 1024 * 1024;
        prepare_image_capture();
        CHECK(image_begin == reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)));
        CHECK(image_end > image_begin && image_remaining == maximum_bytes / 2);
    }
    CHECK(!requests_close_snapshot(WM_DESTROY, 0));
    CHECK(!requests_close_snapshot(WM_NCDESTROY, 0));
    CHECK(!requests_close_snapshot(WM_SYSCOMMAND, SC_CLOSE));
    CHECK(!requests_close_snapshot(WM_ENDSESSION, FALSE));
    CHECK(requests_close_snapshot(WM_CLOSE, 0));
    CHECK(requests_close_snapshot(WM_QUIT, 0));
    CHECK(requests_close_snapshot(WM_ENDSESSION, TRUE));
    namespace fs = std::filesystem;
    const auto root = fs::temp_directory_path() / ("ft-dump-writer-" + std::to_string(GetCurrentProcessId()) + "-" + std::to_string(GetTickCount64()));
    if (!fs::create_directory(root)) return 1;
    const char* outcomes[]{"written error=0", "failed-write error=29", "failed-size-limit error=223", "failed-publish", "failed-create",
        "written-without-exception error=0", "failed-write error=87", "written-without-exception error=0", "failed-size-limit error=223"};
    for (int mode = 0; mode < 9; ++mode) {
        const auto folder = root / std::to_string(mode); fs::create_directory(folder);
        final_path = (folder / "exception.dmp").wstring(); temporary_path = final_path + L".tmp";
        if (mode == 3) std::ofstream(fs::path(final_path)) << "preserve-final";
        if (mode == 4) std::ofstream(fs::path(temporary_path)) << "preserve-partial";
        status_file = CreateFileW((final_path + L".status.txt").c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        CHECK(status_file != INVALID_HANDLE_VALUE);
        status_bytes = 0; limit_hit = false; maximum_bytes = 64; stopping = 0;
        requested = CreateEventW(nullptr, TRUE, TRUE, nullptr);
        completed = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        fixture_mode = mode; fixture_calls = 0; write_dump = write_fixture;
        status("armed");
        run(nullptr);
        CHECK(WaitForSingleObject(completed, 0) == WAIT_OBJECT_0);
        const auto report = read_text(final_path + L".status.txt");
        CHECK(report.find(outcomes[mode]) != std::string::npos);
        CHECK(report.size() <= 1024);
        CHECK(fixture_calls == (mode == 4 ? 0 : mode >= 5 ? 2 : 1));
        if (mode == 0 || mode == 5 || mode == 7) {
            CHECK(fs::file_size(fs::path(final_path)) == (mode == 7 ? 8 : 64));
            CHECK(!fs::exists(fs::path(temporary_path)));
        } else if (mode == 1 || mode == 2 || mode == 6 || mode == 8) {
            CHECK(!fs::exists(fs::path(final_path)) && !fs::exists(fs::path(temporary_path)));
        } else if (mode == 3) {
            CHECK(read_text(fs::path(final_path)) == "preserve-final");
            CHECK(fs::file_size(fs::path(temporary_path)) == 64);
        } else CHECK(read_text(fs::path(temporary_path)) == "preserve-partial");
        for (int i = 0; i < 100; ++i) status("bounded-status");
        CHECK(fs::file_size(fs::path(final_path + L".status.txt")) <= 1024);
        stop_dump_writer();
    }
    wchar_t executable_path[32768]{};
    CHECK(GetModuleFileNameW(nullptr, executable_path, 32768) > 0);
    const auto legacy_path = fs::path(executable_path).parent_path() / "legacy-dbghelp/dbghelp.dll";
    const auto legacy = LoadLibraryW(legacy_path.c_str());
    CHECK(legacy != nullptr);
    const auto bare = LoadLibraryExW(L"dbghelp.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    CHECK(bare == legacy);
    if (bare) FreeLibrary(bare);
    // Verify actual system dump streams while an incompatible same-named library remains loaded.
    for (int mode = 0; mode < 2; ++mode) {
        const auto path = root / ("system-" + std::to_string(mode) + ".dmp");
        status_bytes = 0; limit_hit = false; claimed = 0; stopping = 0;
        CHECK(start_dump_writer(path.wstring(), 16));
        CHECK(library != legacy);
        system_writer = write_dump;
        if (mode == 1) write_dump = reject_context;
        RtlCaptureContext(&context);
        record = {}; record.ExceptionCode = EXCEPTION_ACCESS_VIOLATION;
        record.ExceptionAddress = reinterpret_cast<void*>(context.Eip);
        fault_thread = GetCurrentThreadId();
        CHECK(SetEvent(requested));
        CHECK(WaitForSingleObject(completed, 10000) == WAIT_OBJECT_0);
        stop_dump_writer();
        const auto report = read_text(path.wstring() + L".status.txt");
        CHECK(report.find(mode == 0 ? "written error=0" : "written-without-exception error=0") != std::string::npos);
        const auto bytes = read_text(path);
        CHECK(bytes.size() >= sizeof(MINIDUMP_HEADER) && bytes.size() <= 16 * 1024 * 1024);
        MINIDUMP_HEADER header{};
        if (bytes.size() >= sizeof(header)) std::memcpy(&header, bytes.data(), sizeof(header));
        CHECK(header.Signature == MINIDUMP_SIGNATURE);
        CHECK(header.StreamDirectoryRva <= bytes.size()
            && header.NumberOfStreams <= (bytes.size() - header.StreamDirectoryRva) / sizeof(MINIDUMP_DIRECTORY));
        bool exception_stream = false, thread_stream = false;
        for (ULONG32 i = 0; header.StreamDirectoryRva <= bytes.size()
            && header.NumberOfStreams <= (bytes.size() - header.StreamDirectoryRva) / sizeof(MINIDUMP_DIRECTORY)
            && i < header.NumberOfStreams; ++i) {
            MINIDUMP_DIRECTORY entry{};
            std::memcpy(&entry, bytes.data() + header.StreamDirectoryRva + i * sizeof(entry), sizeof(entry));
            exception_stream |= entry.StreamType == ExceptionStream;
            thread_stream |= entry.StreamType == ThreadListStream;
        }
        CHECK(exception_stream == (mode == 0));
        CHECK(thread_stream);
        CHECK(contains_memory(bytes, reinterpret_cast<uintptr_t>(&prepare_image_capture)));
    }
    // A self-ending client has no exception: the snapshot keeps every thread, and only the first request writes.
    {
        const auto path = root / "window-close.dmp";
        status_bytes = 0; limit_hit = false; claimed = 0; stopping = 0;
        CHECK(start_dump_writer(path.wstring(), 16));
        request_snapshot();
        CHECK(WaitForSingleObject(completed, 10000) == WAIT_OBJECT_0);
        request_snapshot();
        stop_dump_writer();
        const auto report = read_text(path.wstring() + L".status.txt");
        CHECK(report.find("written-window-close error=0") != std::string::npos);
        CHECK(report.find("writing") == report.rfind("writing"));
        const auto bytes = read_text(path);
        MINIDUMP_HEADER header{};
        CHECK(bytes.size() >= sizeof(header));
        if (bytes.size() >= sizeof(header)) std::memcpy(&header, bytes.data(), sizeof(header));
        CHECK(header.Signature == MINIDUMP_SIGNATURE);
        bool exception_stream = false, thread_stream = false;
        for (ULONG32 i = 0; header.StreamDirectoryRva <= bytes.size()
            && header.NumberOfStreams <= (bytes.size() - header.StreamDirectoryRva) / sizeof(MINIDUMP_DIRECTORY)
            && i < header.NumberOfStreams; ++i) {
            MINIDUMP_DIRECTORY entry{};
            std::memcpy(&entry, bytes.data() + header.StreamDirectoryRva + i * sizeof(entry), sizeof(entry));
            exception_stream |= entry.StreamType == ExceptionStream;
            thread_stream |= entry.StreamType == ThreadListStream;
        }
        CHECK(!exception_stream);
        CHECK(thread_stream);
        CHECK(contains_memory(bytes, reinterpret_cast<uintptr_t>(&prepare_image_capture)));
    }
    // A blocked dump writer must not hold the UI caller in request_snapshot.
    {
        status_bytes = 0; limit_hit = false; claimed = 0; stopping = 0;
        CHECK(start_dump_writer((root / "asynchronous-close.dmp").wstring(), 16));
        fixture_mode = 9; write_dump = write_fixture;
        fixture_entered = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        fixture_release = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        snapshot_returned = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        const auto caller = CreateThread(nullptr, 0, queue_snapshot, nullptr, 0, nullptr);
        CHECK(caller != nullptr);
        CHECK(WaitForSingleObject(fixture_entered, 10000) == WAIT_OBJECT_0);
        CHECK(WaitForSingleObject(snapshot_returned, 1000) == WAIT_OBJECT_0);
        CHECK(WaitForSingleObject(completed, 0) == WAIT_TIMEOUT);
        SetEvent(fixture_release);
        CHECK(WaitForSingleObject(completed, 10000) == WAIT_OBJECT_0);
        CHECK(WaitForSingleObject(caller, 10000) == WAIT_OBJECT_0);
        CloseHandle(caller);
        stop_dump_writer();
        CloseHandle(fixture_entered); CloseHandle(fixture_release); CloseHandle(snapshot_returned);
    }
    if (legacy) CHECK(FreeLibrary(legacy));
    fs::remove_all(root);
    return finish("dump writer outcomes");
}

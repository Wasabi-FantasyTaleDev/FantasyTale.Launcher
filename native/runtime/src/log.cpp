#include "fthook/log.h"

#include <windows.h>

#include <cstdarg>
#include <cstdio>
#include <cstring>

namespace fthook {
namespace {
constexpr size_t kMaxLine = 4096;
constexpr char kFullMarker[] = "log limit reached; further lines omitted\r\n";

void create_parents(const std::wstring& path) {
    for (size_t i = 3; i < path.size(); ++i)
        if (path[i] == L'\\' || path[i] == L'/') CreateDirectoryW(path.substr(0, i).c_str(), nullptr);
}
}

Log::~Log() {
    if (handle_) CloseHandle(handle_);
}

bool Log::open(const std::wstring& path, size_t max_bytes) {
    std::lock_guard guard(lock_);
    if (handle_) return true;
    create_parents(path);
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    handle_ = file;
    max_bytes_ = max_bytes < 16 * 1024 ? 16 * 1024 : (max_bytes > 64u * 1024 * 1024 ? 64u * 1024 * 1024 : max_bytes);
    return true;
}

void Log::line(const char* text) noexcept {
    if (!text) return;
    std::lock_guard guard(lock_);
    if (!handle_ || full_) return;
    char buffer[kMaxLine + 2];
    size_t length = strnlen(text, kMaxLine);
    std::memcpy(buffer, text, length);
    buffer[length++] = '\r';
    buffer[length++] = '\n';
    DWORD done = 0;
    if (written_ + length + sizeof(kFullMarker) > max_bytes_) {
        WriteFile(handle_, kFullMarker, sizeof(kFullMarker) - 1, &done, nullptr);
        full_ = true;
        return;
    }
    if (WriteFile(handle_, buffer, static_cast<DWORD>(length), &done, nullptr)) written_ += done;
}

void Log::linef(const char* format, ...) noexcept {
    char buffer[kMaxLine];
    va_list args;
    va_start(args, format);
    const int count = std::vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);
    if (count >= 0) line(buffer);
}

Log& runtime_log() {
    static Log log;
    return log;
}

} // namespace fthook

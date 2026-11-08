#pragma once
// Bounded, thread-safe line log.
#include <cstddef>
#include <mutex>
#include <string>

namespace fthook {

/// Appends lines to one file up to a byte cap. The file is truncated when opened, so
/// repeated runs never grow it without bound. Writing never throws; once the cap is
/// reached one final marker line is written and later lines are dropped.
class Log {
public:
    Log() = default;
    Log(const Log&) = delete;
    Log& operator=(const Log&) = delete;
    ~Log();

    /// Opens (and truncates) `path`, creating missing parent folders. `max_bytes` is
    /// clamped to 16 KiB .. 64 MiB. Returns false when the file cannot be opened.
    bool open(const std::wstring& path, size_t max_bytes);
    /// True after a successful open().
    bool is_open() const noexcept { return handle_ != nullptr; }
    /// Writes one line (a line break is appended). Lines over 4 KiB are cut.
    void line(const char* text) noexcept;
    /// printf-style convenience wrapper around line().
    void linef(const char* format, ...) noexcept;

private:
    void* handle_ = nullptr;
    size_t written_ = 0;
    size_t max_bytes_ = 0;
    bool full_ = false;
    std::mutex lock_;
};

/// The process-wide runtime log.
Log& runtime_log();

} // namespace fthook

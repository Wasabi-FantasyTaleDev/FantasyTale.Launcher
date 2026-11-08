#include "fthook/companion.h"
#include <windows.h>
#define TOML_EXCEPTIONS 0
#define TOML_ENABLE_FORMATTERS 0
#include <toml.hpp>
#include <algorithm>
#include <cstring>
#include <string_view>

namespace fthook {
namespace {
constexpr uint32_t kFrameBytes = 256u * 1024u;
constexpr uint32_t kOutputBytes = 128u * 1024u + 1u;
bool bounded(const char* value, size_t size) { return value && strnlen_s(value, size + 1) <= size; }
std::string quote(std::string_view value) {
    std::string text = "\"";
    for (const unsigned char c : value) {
        if (c == '"' || c == '\\') { text.push_back('\\'); text.push_back(static_cast<char>(c)); }
        else if (c == '\n') text += "\\n";
        else if (c == '\r') text += "\\r";
        else if (c == '\t') text += "\\t";
        else if (c < 32) return {};
        else text.push_back(static_cast<char>(c));
    }
    text.push_back('"'); return text;
}

bool transfer(HANDLE pipe, void* buffer, DWORD length, bool write, ULONGLONG deadline) {
    auto* bytes = static_cast<unsigned char*>(buffer);
    HANDLE event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!event) return false;
    bool success = true;
    while (length && success) {
        OVERLAPPED operation{}; operation.hEvent = event;
        ResetEvent(event);
        DWORD done = 0;
        const BOOL immediate = write ? WriteFile(pipe, bytes, length, &done, &operation) : ReadFile(pipe, bytes, length, &done, &operation);
        if (!immediate) {
            if (GetLastError() != ERROR_IO_PENDING) { success = false; break; }
            const auto now = GetTickCount64();
            const auto wait = now < deadline ? static_cast<DWORD>(std::min<ULONGLONG>(deadline - now, 301000u)) : 0u;
            if (WaitForSingleObject(event, wait) != WAIT_OBJECT_0) {
                CancelIoEx(pipe, &operation);
                GetOverlappedResult(pipe, &operation, &done, TRUE);
                success = false; break;
            }
            if (!GetOverlappedResult(pipe, &operation, &done, FALSE)) { success = false; break; }
        }
        if (!done || GetTickCount64() > deadline) { success = false; break; }
        bytes += done; length -= done;
    }
    CloseHandle(event); return success;
}
}

CompanionClient::CompanionClient(std::wstring pipe_name, uint32_t launcher_pid, std::string owner, std::map<std::string, uint32_t> grants)
    : pipe_name_(std::move(pipe_name)), launcher_pid_(launcher_pid), owner_(std::move(owner)), grants_(std::move(grants)) {}
CompanionClient::~CompanionClient() { std::lock_guard lock(mutex_); close_locked(); }
void CompanionClient::close_locked() { if (pipe_) { CloseHandle(static_cast<HANDLE>(pipe_)); pipe_ = nullptr; } }
bool CompanionClient::connect_locked() {
    if (pipe_) return true;
    if (pipe_name_.empty() || !launcher_pid_) return false;
    const auto path = L"\\\\.\\pipe\\" + pipe_name_;
    if (!WaitNamedPipeW(path.c_str(), 1000)) return false;
    HANDLE pipe = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
        FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return false;
    ULONG server = 0;
    if (!GetNamedPipeServerProcessId(pipe, &server) || server != launcher_pid_) { CloseHandle(pipe); return false; }
    pipe_ = pipe; return true;
}

int32_t CompanionClient::invoke(const char* method, const char* type, uint32_t version, const char* data,
    uint32_t timeout_ms, const char* operation_id, char* output, uint32_t capacity) {
    if (!bounded(method, 16) || !output || capacity < kOutputBytes) return FTHOOK_INVALID;
    output[0] = '\0';
    const std::string_view action(method);
    if (action != "state" && action != "subscribe" && action != "unsubscribe" && action != "send" && action != "request" && action != "poll") return FTHOOK_INVALID;
    if (action != "state" && action != "poll") {
        if (!bounded(type, 96)) return FTHOOK_INVALID;
        const auto grant = grants_.find(type);
        if (grant == grants_.end() || grant->second != version) return FTHOOK_INVALID;
    }
    if ((data && !bounded(data, 65536)) || (operation_id && !bounded(operation_id, 128))
        || timeout_ms > 300000 || (timeout_ms && timeout_ms < 100)) return FTHOOK_INVALID;
    const auto quoted_data = data ? quote(data) : std::string{};
    const auto quoted_operation = operation_id ? quote(operation_id) : std::string{};
    if ((data && quoted_data.empty()) || (operation_id && quoted_operation.empty())) return FTHOOK_INVALID;
    std::lock_guard lock(mutex_);
    if (!connect_locked()) return FTHOOK_NOT_FOUND;
    sequence_ = sequence_ >= INT32_MAX ? 1u : sequence_ + 1u;
    std::string frame = "protocol = 1\nrequest_id = " + std::to_string(sequence_) + "\nmethod = " + quote(action)
        + "\nowner = " + quote(owner_) + "\n";
    if (action != "state" && action != "poll") frame += "type = " + quote(type) + "\nversion = " + std::to_string(version) + "\n";
    if (data) frame += "data = " + quoted_data + "\n";
    if (operation_id) frame += "operation_id = " + quoted_operation + "\n";
    frame += "timeout_ms = " + std::to_string(timeout_ms) + "\n";
    if (frame.size() > kFrameBytes) return FTHOOK_INVALID;
    uint32_t length = static_cast<uint32_t>(frame.size());
    // Request timeouts are channel semantics; the local transport also has a finite delivery budget.
    const auto deadline = GetTickCount64() + (action == "request" ? (timeout_ms ? timeout_ms : 300000u) : 5000u) + 1000u;
    const auto pipe = static_cast<HANDLE>(pipe_);
    if (!transfer(pipe, &length, sizeof(length), true, deadline) || !transfer(pipe, frame.data(), length, true, deadline)
        || !transfer(pipe, &length, sizeof(length), false, deadline) || !length || length > kFrameBytes) {
        close_locked(); return FTHOOK_FAILED;
    }
    std::string raw(length, '\0');
    if (!transfer(pipe, raw.data(), length, false, deadline)) { close_locked(); return FTHOOK_FAILED; }
    auto reply = toml::parse(std::string_view(raw), std::string_view("companion-response"));
    if (!reply || reply.table()["protocol"].value<int64_t>() != 1 || reply.table()["request_id"].value<int64_t>() != sequence_) {
        close_locked(); return FTHOOK_FAILED;
    }
    const auto result = reply.table()["result"].value<std::string>();
    if (!result) { close_locked(); return FTHOOK_FAILED; }
    if (*result == "not-found") return FTHOOK_NOT_FOUND;
    if (*result == "invalid") return FTHOOK_INVALID;
    if (*result != "ok") return FTHOOK_FAILED;
    if (const auto value = reply.table()["data"].value<std::string>()) {
        if (value->size() >= capacity) return FTHOOK_INVALID;
        std::memcpy(output, value->c_str(), value->size() + 1);
    }
    return FTHOOK_OK;
}
}

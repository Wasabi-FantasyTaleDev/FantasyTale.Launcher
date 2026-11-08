#include "fthook/overlay.h"
#include "fthook/overlay_ui.h"

#include <windows.h>
#define TOML_EXCEPTIONS 0
#define TOML_ENABLE_FORMATTERS 0
#include <toml.hpp>

#include <algorithm>
#include <cstring>
#include <string_view>

namespace fthook {
namespace {
constexpr uint32_t kProtocol = 1;
constexpr uint32_t kMaximumFrame = 256u * 1024u;
constexpr DWORD kConnectTimeoutMs = 1000;

bool bounded(const char* value, size_t limit) {
    return value && strnlen_s(value, limit + 1) <= limit;
}

std::string quote(std::string_view value) {
    std::string result;
    result.reserve(value.size() + 2);
    result.push_back('"');
    for (const unsigned char c : value) {
        if (c == '"' || c == '\\') { result.push_back('\\'); result.push_back(static_cast<char>(c)); }
        else if (c == '\n') result += "\\n";
        else if (c == '\r') result += "\\r";
        else if (c == '\t') result += "\\t";
        else if (c < 0x20) return {};
        else result.push_back(static_cast<char>(c));
    }
    result.push_back('"');
    return result;
}

bool write_all(HANDLE pipe, const void* data, DWORD size) {
    const auto* bytes = static_cast<const uint8_t*>(data);
    while (size) {
        DWORD written = 0;
        if (!WriteFile(pipe, bytes, size, &written, nullptr) || !written) return false;
        bytes += written; size -= written;
    }
    return true;
}

bool read_all(HANDLE pipe, void* data, DWORD size) {
    auto* bytes = static_cast<uint8_t*>(data);
    while (size) {
        DWORD read = 0;
        if (!ReadFile(pipe, bytes, size, &read, nullptr) || !read) return false;
        bytes += read; size -= read;
    }
    return true;
}

bool valid_id(const char* value, size_t maximum) {
    if (!bounded(value, maximum)) return false;
    for (const unsigned char c : std::string_view(value))
        if (!(c == '-' || c == '_' || c == '.' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9')) return false;
    return value[0] != '\0' && value[0] != '.' && value[0] != '-';
}

int32_t status(std::string_view value) {
    if (value == "ok") return FTHOOK_OK;
    if (value == "not-found") return FTHOOK_NOT_FOUND;
    if (value == "invalid") return FTHOOK_INVALID;
    return FTHOOK_FAILED;
}
} // namespace

OverlayClient::OverlayClient(std::wstring pipe_name, uint32_t launcher_pid, std::string owner)
    : pipe_name_(std::move(pipe_name)), launcher_pid_(launcher_pid), owner_(std::move(owner)) {}

OverlayClient::~OverlayClient() {
    std::lock_guard lock(mutex_);
    close_locked();
}

void OverlayClient::close_locked() {
    if (pipe_) { CloseHandle(static_cast<HANDLE>(pipe_)); pipe_ = nullptr; }
}

bool OverlayClient::connect_locked() {
    if (pipe_) return true;
    if (pipe_name_.empty() || !launcher_pid_) return false;
    const std::wstring path = L"\\\\.\\pipe\\" + pipe_name_;
    if (!WaitNamedPipeW(path.c_str(), kConnectTimeoutMs)) return false;
    HANDLE pipe = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
        SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return false;
    ULONG server_pid = 0;
    if (!GetNamedPipeServerProcessId(pipe, &server_pid) || server_pid != launcher_pid_) {
        CloseHandle(pipe); return false;
    }
    DWORD mode = PIPE_READMODE_BYTE;
    if (!SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr)) { CloseHandle(pipe); return false; }
    pipe_ = pipe;
    return true;
}

int32_t OverlayClient::request(const char* method, const char* panel_id, const char* html,
    const char* file_name, const char* event_name, const char* data, char* output, uint32_t capacity) {
    if (!valid_id(panel_id, 96) || !valid_id(method, 16) || !valid_id(owner_.c_str(), 128)) return FTHOOK_INVALID;
    if (html && file_name) return FTHOOK_INVALID;
    if ((html && !bounded(html, kMaximumFrame)) || (file_name && !bounded(file_name, 256))
        || (event_name && !valid_id(event_name, 96)) || (data && !bounded(data, kMaximumFrame))) return FTHOOK_INVALID;
    if ((html && html[0] == '\0') || (file_name && file_name[0] == '\0')) return FTHOOK_INVALID;
    std::lock_guard lock(mutex_);
    if (!connect_locked()) return FTHOOK_NOT_FOUND;
    const uint32_t id = next_request_id_;
    next_request_id_ = id == UINT32_MAX ? 1u : id + 1u;
    std::string frame = "protocol = 1\nrequest_id = " + std::to_string(id) + "\nmethod = " + quote(method)
        + "\nowner = " + quote(owner_) + "\npanel_id = " + quote(panel_id) + "\n";
    const auto append = [&frame](const char* key, const char* value) {
        if (value) frame += std::string(key) + " = " + quote(value) + "\n";
    };
    append("html", html); append("file_name", file_name); append("event", event_name); append("data", data);
    if (frame.size() > kMaximumFrame) return FTHOOK_INVALID;
    const uint32_t length = static_cast<uint32_t>(frame.size());
    if (!write_all(static_cast<HANDLE>(pipe_), &length, sizeof(length))
        || !write_all(static_cast<HANDLE>(pipe_), frame.data(), length)) { close_locked(); return FTHOOK_FAILED; }
    uint32_t response_length = 0;
    if (!read_all(static_cast<HANDLE>(pipe_), &response_length, sizeof(response_length))
        || response_length == 0 || response_length > kMaximumFrame) { close_locked(); return FTHOOK_FAILED; }
    std::string response(response_length, '\0');
    if (!read_all(static_cast<HANDLE>(pipe_), response.data(), response_length)) { close_locked(); return FTHOOK_FAILED; }
    auto parsed = toml::parse(std::string_view(response), std::string_view("overlay-response"));
    if (!parsed) return FTHOOK_FAILED;
    const auto protocol = parsed.table()["protocol"].value<int64_t>();
    const auto result = parsed.table()["result"].value<std::string>();
    const auto response_id = parsed.table()["request_id"].value<int64_t>();
    if (!protocol || *protocol != kProtocol || !result || !response_id || *response_id != id) return FTHOOK_FAILED;
    const auto code = status(*result);
    if (output && code == FTHOOK_OK) {
        const auto value = parsed.table()["data"].value<std::string>();
        if (!value || value->size() >= capacity) return FTHOOK_INVALID;
        std::memcpy(output, value->c_str(), value->size() + 1);
    }
    return code;
}

int32_t OverlayClient::register_panel(const char* panel_id, const char* inline_html, const char* html_file_name) {
    if ((!inline_html) == (!html_file_name)) return FTHOOK_INVALID;
    return request("register", panel_id, inline_html, html_file_name, nullptr, nullptr);
}
int32_t OverlayClient::unregister_panel(const char* panel_id) { return request("unregister", panel_id, nullptr, nullptr, nullptr, nullptr); }
int32_t OverlayClient::show_panel(const char* panel_id) { return request("show", panel_id, nullptr, nullptr, nullptr, nullptr); }
int32_t OverlayClient::hide_panel(const char* panel_id) { return request("hide", panel_id, nullptr, nullptr, nullptr, nullptr); }
int32_t OverlayClient::send_panel(const char* panel_id, const char* event_name, const char* json_data) {
    if (!json_data) return FTHOOK_INVALID;
    return request("send", panel_id, nullptr, nullptr, event_name, json_data);
}
int32_t OverlayClient::poll_event(const char* panel_id, char* json_event, uint32_t capacity) {
    if (!json_event || capacity < 4097) return FTHOOK_INVALID;
    json_event[0] = '\0';
    return request("poll", panel_id, nullptr, nullptr, nullptr, nullptr, json_event, capacity);
}
} // namespace fthook

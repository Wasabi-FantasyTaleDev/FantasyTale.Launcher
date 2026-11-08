#pragma once

#include <cstdint>
#include <mutex>
#include <string>

namespace fthook {

/// Synchronous client for the launch-bound launcher overlay broker.
class OverlayClient {
public:
    /// Creates a client. The pipe is connected lazily on the first request.
    OverlayClient(std::wstring pipe_name, uint32_t launcher_pid, std::string owner);
    ~OverlayClient();
    OverlayClient(const OverlayClient&) = delete;
    OverlayClient& operator=(const OverlayClient&) = delete;

    /// Registers an inline HTML panel or a safe package-relative HTML file.
    int32_t register_panel(const char* panel_id, const char* inline_html, const char* html_file_name);
    /// Removes a panel owned by this plugin.
    int32_t unregister_panel(const char* panel_id);
    /// Makes a panel visible.
    int32_t show_panel(const char* panel_id);
    /// Hides a panel.
    int32_t hide_panel(const char* panel_id);
    /// Sends an event and JSON data to a panel.
    int32_t send_panel(const char* panel_id, const char* event_name, const char* json_data);
    /// Consumes one bounded JSON event into a buffer of at least 4097 bytes; NOT_FOUND means no event.
    int32_t poll_event(const char* panel_id, char* json_event, uint32_t capacity);

private:
    int32_t request(const char* method, const char* panel_id, const char* html,
        const char* file_name, const char* event_name, const char* data, char* output = nullptr, uint32_t capacity = 0);
    bool connect_locked();
    void close_locked();

    std::wstring pipe_name_;
    uint32_t launcher_pid_ = 0;
    std::string owner_;
    void* pipe_ = nullptr;
    uint32_t next_request_id_ = 1;
    std::mutex mutex_;
};

} // namespace fthook

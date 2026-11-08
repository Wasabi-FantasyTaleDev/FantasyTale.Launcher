#pragma once
#include "plugin_abi.h"
#include <stddef.h>
#include <map>
#include <mutex>
#include <string>

/* Returns an optional API without reading beyond an older runtime's host table.
   Methods: state, subscribe, unsubscribe, send, request, poll. Output is UTF-8 JSON;
   poll returns NOT_FOUND when empty. Allocate the 131073-byte buffer on the heap. */
inline const fthook_companion_host* fthook_get_companion(const fthook_host* host) {
    if (!host || host->abi_version != FTHOOK_ABI_VERSION || host->struct_size < offsetof(fthook_host, companion) + sizeof(host->companion)) return nullptr;
    const auto* api = host->companion;
    return api && api->abi_version == 1 && api->struct_size >= sizeof(fthook_companion_host) ? api : nullptr;
}

namespace fthook {
// Runtime-owned local client. Network authorization and sockets stay in the Launcher.
class CompanionClient {
public:
    /// Creates an optional, lazily connected client for one manifest-declared owner.
    CompanionClient(std::wstring pipe_name, uint32_t launcher_pid, std::string owner, std::map<std::string, uint32_t> grants);
    /// Closes the local IPC stream after the plugin has drained its own workers.
    ~CompanionClient();
    // One owner retains one request stream; copying a connected client is unsupported.
    CompanionClient(const CompanionClient&) = delete;
    CompanionClient& operator=(const CompanionClient&) = delete;
    /// Performs a bounded synchronous broker operation; use only from a plugin worker.
    int32_t invoke(const char* method, const char* type, uint32_t version, const char* data,
        uint32_t timeout_ms, const char* operation_id, char* output, uint32_t capacity);
private:
    bool connect_locked();
    void close_locked();
    std::wstring pipe_name_;
    uint32_t launcher_pid_;
    std::string owner_;
    std::map<std::string, uint32_t> grants_;
    std::mutex mutex_;
    void* pipe_ = nullptr;
    uint32_t sequence_ = 0;
};
}

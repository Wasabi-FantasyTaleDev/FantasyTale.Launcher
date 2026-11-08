#include "fthook/control.h"
#include "fthook/log.h"
#include "fthook/services.h"
#include "toml_util.h"
#include <windows.h>

namespace fthook {
namespace {
bool transfer(HANDLE pipe, void* data, DWORD bytes, bool write) {
    auto* cursor = static_cast<unsigned char*>(data);
    while (bytes) {
        DWORD done = 0;
        const BOOL ok = write ? WriteFile(pipe, cursor, bytes, &done, nullptr) : ReadFile(pipe, cursor, bytes, &done, nullptr);
        if (!ok || !done) return false;
        bytes -= done; cursor += done;
    }
    return true;
}
std::string quote(const std::string& value) {
    std::string result = "\"";
    for (char c : value) {
        if (c == '\n') result += "\\n";
        else if (c == '\r') result += "\\r";
        else if (c == '\t') result += "\\t";
        else if (c == '"' || c == '\\') { result += '\\'; result += c; }
        else if (static_cast<unsigned char>(c) < 32) return "\"\"";
        else result += c;
    }
    return result + '"';
}
}
void serve_control(const Session& session, const std::wstring& folder) {
    if (session.launch_id.empty() || !session.launcher_pid) return;
    const auto path = L"\\\\.\\pipe\\FantasyTale.Hook." + widen(session.launch_id);
    HANDLE pipe = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
        SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) { runtime_log().line("control: launcher channel unavailable"); return; }
    ULONG server = 0;
    if (!GetNamedPipeServerProcessId(pipe, &server) || server != session.launcher_pid) { CloseHandle(pipe); return; }
    for (;;) {
        uint32_t bytes = 0;
        if (!transfer(pipe, &bytes, sizeof(bytes), false) || !bytes || bytes > 65536) break;
        std::string request(bytes, '\0');
        if (!transfer(pipe, request.data(), bytes, false)) break;
        auto parsed = toml::parse(request);
        if (!parsed) break;
        detail::Errors error;
        std::optional<int64_t> protocol, id;
        std::string method, service, payload;
        const auto& root = parsed.table();
        detail::only_keys(root, {"protocol", "request_id", "method", "service", "payload"}, "", error);
        detail::get_int(root, "protocol", "", protocol, error, 1, 1);
        detail::get_int(root, "request_id", "", id, error, 1, INT32_MAX);
        detail::get_string(root, "method", "", method, error, true);
        if (method == "service") {
            detail::get_string(root, "service", "", service, error, true);
            detail::get_string(root, "payload", "", payload, error, true);
        } else if (root.contains("service") || root.contains("payload")) break;
        if (!error.ok() || !protocol || !id) break;
        std::string body, result = "ok";
        if (method == "ping") body = "pong";
        else if (method == "stop") body = "stopping";
        else if (method == "status") {
            if (!read_text_file(folder + L"\\fthook-status-" + widen(session.launch_id) + L".toml", 48 * 1024, body)) result = "not-ready";
        }
        else if (method == "service") {
            std::vector<char> response(48 * 1024);
            const auto status = invoke_service("launcher.developer", service.c_str(), payload.c_str(), response.data(), static_cast<uint32_t>(response.size()));
            if (status == FTHOOK_OK) body = response.data();
            else if (status == FTHOOK_NOT_FOUND) result = "service-unavailable";
            else result = "service-failed";
        }
        else result = "unsupported-method";
        std::string reply = "protocol = 1\nrequest_id = " + std::to_string(*id) + "\nresult = " + quote(result) + "\nbody = " + quote(body) + "\n";
        if (reply.size() > 64 * 1024) break;
        bytes = static_cast<uint32_t>(reply.size());
        if (!transfer(pipe, &bytes, sizeof(bytes), true) || !transfer(pipe, reply.data(), bytes, true)) break;
        if (method == "stop") { TerminateProcess(GetCurrentProcess(), 0); break; }
    }
    CloseHandle(pipe);
}
}

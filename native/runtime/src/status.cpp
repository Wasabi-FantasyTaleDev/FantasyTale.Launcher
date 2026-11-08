#include "fthook/status.h"
#include "fthook/log.h"
#include <windows.h>

namespace fthook {
namespace {
std::string quote(const std::string& value) {
    static constexpr char hex[] = "0123456789abcdef";
    std::string result = "\"";
    for (unsigned char c : value) {
        if (c == '\\' || c == '"') { result += '\\'; result += static_cast<char>(c); }
        else if (c < 32 || c == 127) { result += "\\u00"; result += hex[c >> 4]; result += hex[c & 15]; }
        else result += static_cast<char>(c);
    }
    return result + '"';
}
}

bool publish_status(const std::wstring& folder, const Session& session, const char* state,
    const char* error, const std::vector<PluginReport>& reports, bool bootstrap_complete) {
    if (session.launch_id.empty()) return true;
    if (session.launch_id.size() != 32 || session.launch_id.find_first_not_of("0123456789abcdef") != std::string::npos
        || reports.size() > 64 || reports.size() > session.plugins.size()) return false;
    FILETIME created{}, exited{}, kernel{}, user{};
    if (!GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user)) return false;
    const uint64_t started = (static_cast<uint64_t>(created.dwHighDateTime) << 32) | created.dwLowDateTime;
    std::string text = "schema = 1\nlaunch_id = " + quote(session.launch_id) + "\ngame = " + quote(session.game)
        + "\nbuild = " + quote(session.build) + "\ninstance = " + quote(session.instance)
        + "\npid = " + std::to_string(GetCurrentProcessId()) + "\nprocess_started = " + std::to_string(started)
        + "\nruntime = " + quote(FTHOOK_VERSION_STRING) + "\nstate = " + quote(state) + "\nerror = " + quote(error)
        + "\nbootstrap_complete = " + (bootstrap_complete ? "true\n" : "false\n");
    for (size_t i = 0; i < reports.size(); ++i) {
        const auto& r = reports[i];
        if (r.id != session.plugins[i].id) return false;
        text += "\n[[plugins]]\nid = " + quote(r.id) + "\nversion = " + quote(session.plugins[i].version)
            + "\noutcome = " + quote(to_string(r.outcome)) + "\nrequired = " + (r.required ? "true\n" : "false\n");
    }
    if (text.size() > 64 * 1024) return false;
    const std::wstring path = folder + L"\\fthook-status-" + widen(session.launch_id) + L".toml";
    const std::wstring temporary = path + L".tmp";
    HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        runtime_log().linef("status: temporary report open failed (Win32 %lu)", GetLastError());
        return false;
    }
    DWORD written = 0;
    const bool ok = WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr)
        && written == text.size() && FlushFileBuffers(file);
    CloseHandle(file);
    bool moved = false;
    DWORD move_error = ERROR_SUCCESS;
    if (ok) for (unsigned attempt = 0; attempt < 21; ++attempt) {
        if (MoveFileExW(temporary.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) { moved = true; break; }
        move_error = GetLastError();
        // A brief reader or filesystem scanner may retain a non-delete-sharing handle.
        if ((move_error != ERROR_SHARING_VIOLATION && move_error != ERROR_ACCESS_DENIED) || attempt == 20) break;
        Sleep(10);
    }
    if (!moved) {
        if (ok) runtime_log().linef("status: report replacement failed (Win32 %lu)", move_error);
        DeleteFileW(temporary.c_str());
    }
    return moved;
}
} // namespace fthook

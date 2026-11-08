#include "host.h"

#include <fthook/engine.h>
#include <fthook/log.h>
#include <fthook/session.h>
#include <fthook/status.h>
#include <fthook/control.h>

#include <atomic>
#include <mutex>
#include <optional>

namespace bubblefighter::host {

const char* const kExportNames[ExportCount] = {
    "NMCO_CallNMFunc", "NMCO_CallNMFunc2", "NMCO_MemoryFree", "NMCO_SetLocale", "NMCO_SetLocaleAndRegion",
    "NMCO_SetPatchOption", "NMCO_SetUseFriendModuleOption", "NMCO_SetUseNGMOption", "NMCO_SetVersionFileUrl",
    "NMCO_SetVersionFileUrlA", "NMCO_SetVersionFileUrlW"};

namespace {
HMODULE g_module = nullptr;
std::once_flag g_init;
Mode g_mode = Mode::StandIn;
nmco::Identity g_identity;
std::optional<fthook::Session> g_session;
std::string g_session_error;
std::atomic<int> g_ready{0}; // 0: preparing, 1: ready, -1: failed.
std::mutex g_status_mutex;
HANDLE g_session_lock = INVALID_HANDLE_VALUE;
ULONGLONG g_deadline = 0;
std::wstring module_folder();

void status(const char* state, const char* error, const std::vector<fthook::PluginReport>& reports = {}) {
    std::lock_guard lock(g_status_mutex);
    if (!g_session || (g_ready.load() < 0 && std::string(state) != "failed")) return;
    if (std::string(state) == "failed") g_ready.store(-1);
    if (!fthook::publish_status(module_folder(), *g_session, state, error, reports)) {
        g_ready.store(-1);
        fthook::runtime_log().line("host: readiness report unavailable; authentication refused");
    }
    else if (std::string(state) == "ready") g_ready.store(1);
}

std::wstring module_folder() {
    wchar_t path[MAX_PATH]{};
    const DWORD n = GetModuleFileNameW(g_module, path, MAX_PATH);
    std::wstring folder(path, n);
    const size_t slash = folder.find_last_of(L"\\/");
    return slash == std::wstring::npos ? std::wstring() : folder.substr(0, slash);
}

bool file_exists(const std::wstring& path) {
    const DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && !(attributes & FILE_ATTRIBUTE_DIRECTORY);
}

std::wstring default_log_path() {
    wchar_t base[MAX_PATH]{};
    const DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", base, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return {};
    return std::wstring(base) + L"\\FantasyTale\\BubbleFighter\\logs\\fthook.log";
}

void initialize() {
    const std::wstring folder = module_folder();
    const std::wstring session_path = folder + L"\\session.toml";
    if (file_exists(session_path)) {
        // Hold the session against replacement until exit, even if the launcher crashes.
        g_session_lock = CreateFileW(session_path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (g_session_lock == INVALID_HANDLE_VALUE) g_session_error = "session file cannot be locked";
        else g_session = fthook::load_session(session_path, g_session_error);
    }
    g_deadline = GetTickCount64() + (g_session ? g_session->startup_timeout_ms : 120000);

    const std::wstring log_path = g_session && !g_session->log_path.empty() ? g_session->log_path : default_log_path();
    if (g_session) g_session->log_path = log_path;
    if (!log_path.empty()) fthook::runtime_log().open(log_path, (g_session ? g_session->log_max_kib : 1024) * 1024);
    auto& log = fthook::runtime_log();
    log.linef("fthook %s nmcogame host, pid %lu", FTHOOK_VERSION_STRING, GetCurrentProcessId());

    const bool has_original = file_exists(folder + L"\\nmcogame.nexon.dll");
    if (g_session && g_session->messenger == "standin") g_mode = Mode::StandIn;
    else if (g_session && g_session->messenger == "forward") g_mode = has_original ? Mode::Forward : Mode::Failed;
    else g_mode = has_original ? Mode::Forward : Mode::StandIn;
    if (g_session && g_session->messenger == "forward" && !has_original)
        log.line("messenger: forward requested but nmcogame.nexon.dll is missing; authentication refused");
    else if (g_session && !g_session->messenger.empty() && g_session->messenger != "standin" && g_session->messenger != "forward")
        g_mode = Mode::Failed;
    if (!g_session_error.empty()) g_mode = Mode::Failed;

    if (g_session) {
        g_identity.ticket = fthook::widen(g_session->ticket);
        g_identity.launcher = fthook::widen(g_session->account);
        log.linef("session: game=%s build=%s instance=%s plugins=%u", g_session->game.c_str(), g_session->build.c_str(),
            g_session->instance.c_str(), static_cast<unsigned>(g_session->plugins.size()));
    } else if (!g_session_error.empty()) {
        log.linef("session: invalid: %s", g_session_error.c_str());
    } else {
        log.line("session: none");
    }
    wchar_t account[64]{};
    const DWORD n = GetEnvironmentVariableW(L"BFF_LAUNCHER_ACCOUNT", account, 64);
    if (n > 0 && n < 64) g_identity.environment.assign(account, n);
    log.linef("messenger: %s", g_mode == Mode::Forward ? "forward" : g_mode == Mode::Failed ? "failed" : "standin");
}

DWORD WINAPI control_worker(void*) { fthook::serve_control(*g_session, module_folder()); return 0; }

DWORD WINAPI worker(void*) {
    ensure_initialized();
    auto& log = fthook::runtime_log();
    if (!g_session_error.empty()) {
        MessageBoxW(nullptr, fthook::widen("The launcher session file is invalid; no client plugins were loaded.\n\n" + g_session_error).c_str(),
            L"FantasyTale hook", MB_OK | MB_ICONWARNING);
        return 0;
    }
    if (!g_session) { g_ready.store(g_mode == Mode::Failed ? -1 : 1); return 0; }
    status("starting", "");
    HMODULE pinned = nullptr;
    // Native observers retain API function pointers after this worker returns.
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(g_module), &pinned)) {
        log.linef("native host pin failed (Win32 %lu); no plugins loaded", GetLastError());
        fail_host();
        return 0;
    }
    if (!g_session->launch_id.empty()) {
        if (HANDLE thread = CreateThread(nullptr, 0, control_worker, nullptr, 0, nullptr)) CloseHandle(thread);
        else { fail_host(); return 0; }
    }
    // A failed host must still accept an authenticated launcher stop request.
    if (g_mode == Mode::Failed || (g_mode == Mode::Forward && !initialize_forward())) { fail_host(); return 0; }
    const auto image = fthook::describe_main_image();
    if (!image || (g_session->pe_timestamp && *g_session->pe_timestamp != image->pe_timestamp)
        || (g_session->image_size && *g_session->image_size != image->image_size)) { status("failed", "fingerprint"); return 0; }
    fthook::LocatorRegistry locators;
    fthook::register_builtin_locators(locators);
    const auto reports = fthook::run_session(*g_session, locators, [] { return fthook::describe_main_image(); });
    std::string failed_required;
    for (const auto& r : reports) {
        log.linef("plugin id=%s result=%s%s%s", r.id.c_str(), fthook::to_string(r.outcome), r.detail.empty() ? "" : " detail=", r.detail.c_str());
        const bool ok = r.outcome == fthook::Outcome::Installed || r.outcome == fthook::Outcome::AlreadyPresent
                        || r.outcome == fthook::Outcome::SkippedOption;
        if (r.required && !ok) failed_required += "\n" + r.id + ": " + fthook::to_string(r.outcome);
    }
    const char* activation_error = fthook::activation_error(reports);
    status(*activation_error ? "failed" : "ready", activation_error, reports);
    if (!failed_required.empty() && g_session->launch_id.empty())
        MessageBoxW(nullptr, fthook::widen("Required client plugins could not be applied; the game may not work correctly." + failed_required).c_str(),
            L"FantasyTale hook", MB_OK | MB_ICONWARNING);
    return 0;
}
}

void set_module(HMODULE module) { g_module = module; }
void ensure_initialized() { std::call_once(g_init, initialize); }
Mode mode() { return g_mode; }
nmco::Identity& identity() { return g_identity; }

void start_worker() {
    // The thread waits for the loader lock and runs once DllMain has returned.
    if (HANDLE thread = CreateThread(nullptr, 0, worker, nullptr, 0, nullptr)) CloseHandle(thread);
    else g_ready.store(-1);
}

void fail_host() { status("failed", "host-initialization"); g_ready.store(-1); }

bool await_authentication_ready() {
    ensure_initialized();
    if (g_mode == Mode::Failed || !g_session_error.empty()) return false;
    if (!g_session || g_session->launch_id.empty()) return g_ready.load() >= 0;
    while (g_ready.load() == 0 && GetTickCount64() < g_deadline) Sleep(10);
    if (g_ready.load() == 0) status("failed", "startup-timeout");
    return g_ready.load() == 1;
}

bool managed_credentials(std::string& account, std::string& ticket) {
    if (!await_authentication_ready() || !g_session || g_session->launch_id.empty() || g_session->game != "bubblefighter") return false;
    account = g_session->account;
    ticket = g_session->ticket;
    return true;
}

} // namespace bubblefighter::host

#include "host.h"

#include <fthook/engine.h>
#include <fthook/log.h>
#include <fthook/session.h>
#include <fthook/status.h>
#include <fthook/control.h>
#include <fthook/launch_metadata.h>

#include "game_login.h"
#include "http.h"
#include "nm_codes.h"

#include <atomic>
#include <cstring>
#include <memory>
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
std::vector<fthook::PluginReport> g_reports;
std::atomic<int> g_bootstrap_complete{0}; // 0: not reported, 1: queued, 2: complete.
HANDLE g_session_lock = INVALID_HANDLE_VALUE;
ULONGLONG g_deadline = 0;
std::unique_ptr<nmco::FriendsClient> g_friends;
std::wstring module_folder();

void status(const char* state, const char* error, const std::vector<fthook::PluginReport>& reports = {}) {
    std::lock_guard lock(g_status_mutex);
    if (!g_session || (g_ready.load() < 0 && std::string(state) != "failed")) return;
    if (std::string(state) == "failed") g_ready.store(-1);
    if (std::string(state) == "ready" && !reports.empty()) g_reports = reports;
    const auto& current_reports = std::string(state) == "ready" ? g_reports : reports;
    if (!fthook::publish_status(module_folder(), *g_session, state, error, current_reports, g_bootstrap_complete.load() == 2)) {
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

// Hands an event buffer to the client window. The client frees it with NMCO_MemoryFree after
// reading it (kr-812 sub_91DFC0), so it comes from the heap standin_MemoryFree releases.
bool post_event(uint32_t window, uint32_t message, uint32_t event, std::vector<uint8_t> buffer) {
    auto* owned = static_cast<BYTE*>(HeapAlloc(GetProcessHeap(), 0, buffer.size()));
    if (owned == nullptr) return false;
    std::memcpy(owned, buffer.data(), buffer.size());
    if (!PostMessageW(reinterpret_cast<HWND>(static_cast<uintptr_t>(window)), message, event, reinterpret_cast<LPARAM>(owned))) {
        HeapFree(GetProcessHeap(), 0, owned);
        fthook::runtime_log().linef("messenger: event %u not delivered (Win32 %lu)", event, GetLastError());
        return false;
    }
    const char* name = nmco::event_name(event);
    fthook::runtime_log().linef("messenger: event %u (%s) posted", event, name ? name : "unknown id");
    return true;
}

// Runs the calls the game thread queued; the game never waits on the network itself.
DWORD WINAPI work_pump(void*) {
    for (;;) g_friends->work_once(1000);
}

// Chat runs on its own lane so a line never waits behind friend-list work.
DWORD WINAPI chat_pump(void*) {
    for (;;) g_friends->work_chat_once(1000);
}

DWORD WINAPI event_pump(void*) {
    // Long-polls the service while a session and a registered window exist; a failed poll backs off.
    for (;;) {
        if (g_friends->can_poll()) { if (!g_friends->poll_once(25)) Sleep(2000); }
        else Sleep(250);
    }
}

void start_friends() {
    auto& log = fthook::runtime_log();
    const bool deferred = g_session && g_session->game_login && g_session->messenger_url.empty();
    if (g_mode != Mode::StandIn || !g_session || (g_session->messenger_url.empty() && !deferred)) return;
    std::string error;
    // An in-game sign-in learns its messenger only after the player logs in; until then nothing is sent.
    nmco::HttpSend send = deferred ? nmco::HttpSend([](const char*, const std::string&, const std::string&, const std::string&, unsigned,
        nmco::HttpExchange&) { return false; }) : nmco::make_http_send(g_session->messenger_url, error);
    if (!send) { log.linef("messenger: friend list unavailable: %s", error.c_str()); return; }
    g_friends = std::make_unique<nmco::FriendsClient>(g_session->messenger_ticket, std::move(send), post_event,
        [](const std::string& line) { fthook::runtime_log().linef("messenger: %s", line.c_str()); });
    for (auto* pump : {&work_pump, &chat_pump, &event_pump}) {
        if (HANDLE thread = CreateThread(nullptr, 0, pump, nullptr, 0, nullptr)) CloseHandle(thread);
        else { log.line("messenger: worker unavailable; friend list disabled"); g_friends->stop(); return; }
    }
    log.linef("messenger: friend list over %s", deferred ? "http after the in-game sign-in" : g_session->messenger_transport.c_str());
}

// The in-game sign-in: the launcher's own sign-in and launch, sent with the account typed into the game.
uint32_t game_sign_in(const std::wstring& id, std::wstring& password) {
    auto& log = fthook::runtime_log();
    const auto& config = *g_session->game_login;
    std::string error;
    auto send = nmco::make_http_send(config.api_url, error);
    std::string secret = nmco::to_utf8(password);
    SecureZeroMemory(password.data(), password.size() * sizeof(wchar_t));
    if (!send) { SecureZeroMemory(secret.data(), secret.size()); log.linef("game sign-in: %s", error.c_str()); return 20000; }
    const nmco::GameLoginRequest request{config.launch_id, config.launch_key, config.launcher_version, config.build, config.protocol_version,
        config.release_version, config.patch_version, config.launcher_language, config.native_language, config.client_language};
    auto result = nmco::game_sign_in(request, nmco::to_utf8(id), secret, send);
    SecureZeroMemory(secret.data(), secret.size());
    log.linef("%s", result.note.c_str());
    if (result.code != 0) return result.code;
    g_identity.ticket = fthook::widen(result.ticket);
    if (g_friends && !result.messenger_url.empty()) {
        auto messenger = nmco::make_http_send(result.messenger_url, error);
        if (messenger) g_friends->set_credentials(result.messenger_ticket, std::move(messenger));
        else log.linef("messenger: friend list unavailable: %s", error.c_str());
    }
    SecureZeroMemory(result.ticket.data(), result.ticket.size());
    return 0;
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
    if (g_session && g_session->game_login && g_mode == Mode::StandIn) g_identity.sign_in = game_sign_in;
    start_friends();
}

DWORD WINAPI control_worker(void*) { fthook::serve_control(*g_session, module_folder()); return 0; }

DWORD WINAPI bootstrap_report_worker(void*) {
    g_bootstrap_complete.store(2);
    status("ready", "");
    return 0;
}

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
nmco::FriendsClient* friends() { return g_friends.get(); }

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

bool managed_credentials(std::string& account, std::string& ticket, bool* name_login) {
    if (!await_authentication_ready() || !g_session || g_session->launch_id.empty() || g_session->game != "bubblefighter") return false;
    account = g_session->account;
    ticket = g_session->ticket;
    if (name_login) *name_login = g_session->name_login;
    return true;
}

int32_t launch_metadata(const char* capability, char* buffer, uint32_t capacity) {
    ensure_initialized();
    if (!capability || g_ready.load() < 0 || !g_session || g_session->launch_id.empty()) return -1;
    size_t length = 0;
    while (length <= 128 && capability[length]) ++length;
    if (length > 128) return -1;
    return fthook::copy_launch_metadata(*g_session, std::string_view(capability, length), buffer, capacity);
}

int32_t launch_id(char* buffer, uint32_t capacity) {
    ensure_initialized();
    if (g_ready.load() < 0 || !g_session || g_session->launch_id.empty()) return -1;
    return fthook::copy_launch_value(g_session->launch_id, buffer, capacity);
}

int32_t report_bootstrap_complete() {
    if (g_ready.load() != 1 || !g_session || !g_session->module_data || g_session->launch_id.empty()) return -1;
    int expected = 0;
    if (!g_bootstrap_complete.compare_exchange_strong(expected, 1)) return 0;
    const auto thread = CreateThread(nullptr, 0, bootstrap_report_worker, nullptr, 0, nullptr);
    if (!thread) { g_bootstrap_complete.store(0); return -1; }
    CloseHandle(thread);
    return 0;
}

} // namespace bubblefighter::host

extern "C" int32_t __cdecl ft_get_launch_metadata(const char* capability, char* buffer, uint32_t capacity) {
    try { return bubblefighter::host::launch_metadata(capability, buffer, capacity); }
    catch (...) { return -1; }
}
extern "C" int32_t __cdecl ft_get_launch_id(char* buffer, uint32_t capacity) {
    try { return bubblefighter::host::launch_id(buffer, capacity); }
    catch (...) { return -1; }
}
extern "C" int32_t __cdecl ft_report_bootstrap_complete() {
    return bubblefighter::host::report_bootstrap_complete();
}
extern "C" void __cdecl ft_report_bootstrap_failure() {
    try { bubblefighter::host::fail_host(); }
    catch (...) { }
}

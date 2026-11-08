#include <fthook/plugin_abi.h>
#include <toml.hpp>
#include <windows.h>
#include <bcrypt.h>
#include "exit_flow.h"
#include "early_input.h"
#include <algorithm>
#include <array>
#include <atomic>
#include <charconv>
#include <filesystem>
#include <fstream>
#include <memory>
#include <sstream>
#include <vector>

namespace {
using namespace mid_match_exit;
struct Profile {
    bool native_route = false;
    uint32_t timestamp{}, stage_vtable{}, key_slot{}, handler{}, context{}, room{}, dialog_flag{};
    std::vector<uint32_t> image_sizes;
    std::vector<uint8_t> signature;
    std::vector<int32_t> native_exit_modes;
};
struct Scene { uintptr_t context{}, room{}; HWND window{}; };
struct State {
    const fthook_host* host{};
    Profile profile;
    uintptr_t base{};
    HANDLE stop{}, worker{};
    DWORD worker_id{};
    std::atomic<int> readiness{-1};
};

template<class T> bool read(const State& s, uintptr_t address, T& value) {
    return s.host->read(address, &value, sizeof(value)) == FTHOOK_OK;
}
void log(const State& s, const char* text) { s.host->log(s.host->context, text); }
std::wstring wide(const char* text) {
    if (!text) return {};
    const auto length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text, -1, nullptr, 0);
    if (length <= 1) return {};
    std::wstring result(static_cast<size_t>(length), L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text, -1, result.data(), length) != length) return {};
    result.resize(static_cast<size_t>(length - 1)); return result;
}
bool load_profile(const fthook_host* host, Profile& p) {
    const auto reject = [&](const char* reason) {
        const std::string message = std::string("Mid-match exit: exit.toml ") + reason;
        host->log(host->context, message.c_str());
        return false;
    };
    const auto folder = wide(host->plugin_directory);
    if (folder.empty()) return reject("cannot be located: invalid plugin directory");
    std::ifstream input(std::filesystem::path(folder) / L"exit.toml", std::ios::binary | std::ios::ate);
    if (!input) return reject("cannot be opened beside the plugin library");
    if (input.tellg() <= 0 || input.tellg() > 16384) return reject("has an invalid size");
    std::string text(static_cast<size_t>(input.tellg()), '\0');
    input.seekg(0);
    if (!input.read(text.data(), static_cast<std::streamsize>(text.size()))) return reject("could not be read completely");
    const auto parsed = toml::parse(text);
    if (!parsed) return reject("is not valid TOML");
    if (parsed["schema"].value<int>() != 1) return reject("has an unsupported schema");
    const auto row = parsed["clients"][host->build];
    if (!row.is_table()) return reject("does not declare the active client build");
    const auto route = row["route"].value_or(std::string{});
    if (route == "native") { p.native_route = true; return true; }
    if (route != "companion") return reject("has an unsupported route for the active client build");
    const auto* image_sizes = row["image_sizes"].as_array();
    if (!image_sizes || image_sizes->empty()) return reject("is missing companion image_sizes");
    for (const auto& node : *image_sizes) {
        const auto size = node.value<int64_t>();
        if (!size || *size < 4096 || static_cast<uint64_t>(*size) > UINT32_MAX
            || std::find(p.image_sizes.begin(), p.image_sizes.end(), *size) != p.image_sizes.end())
            return reject("contains an invalid or duplicate image size");
        p.image_sizes.push_back(static_cast<uint32_t>(*size));
    }
    const auto image_size = *std::min_element(p.image_sizes.begin(), p.image_sizes.end());
    const auto* native_modes = row["native_exit_modes"].as_array();
    if (!native_modes) return reject("is missing native_exit_modes for the companion route");
    for (const auto& node : *native_modes) {
        const auto mode = node.value<int64_t>();
        if (!mode || *mode < 0 || *mode > INT32_MAX
            || std::find(p.native_exit_modes.begin(), p.native_exit_modes.end(), *mode) != p.native_exit_modes.end())
            return reject("contains an invalid or duplicate native exit mode");
        p.native_exit_modes.push_back(static_cast<int32_t>(*mode));
    }
    auto field = [&](const char* name, uint32_t& value) {
        const auto number = row[name].value<int64_t>();
        if (!number || *number < 1 || static_cast<uint64_t>(*number) > UINT32_MAX) return false;
        value = static_cast<uint32_t>(*number); return true;
    };
    if (!field("pe_timestamp", p.timestamp)
        || !field("stage_vtable_rva", p.stage_vtable) || !field("key_slot_rva", p.key_slot) || !field("handler_rva", p.handler)
        || !field("context_rva", p.context) || !field("room_rva", p.room)
        || !field("dialog_flag_offset", p.dialog_flag) || p.dialog_flag > 65535
        || p.stage_vtable > p.key_slot || p.stage_vtable % 4 || p.key_slot > image_size - 4 || p.key_slot % 4
        || p.handler > image_size - 64
        || p.context > image_size - 4 || p.room > image_size - 4)
        return reject("contains invalid companion image identity, offsets or addresses");
    std::istringstream bytes(row["handler_signature"].value_or(std::string{}));
    std::string token;
    while (bytes >> token) {
        unsigned value = 0;
        const auto converted = std::from_chars(token.data(), token.data() + token.size(), value, 16);
        if (token.size() != 2 || converted.ec != std::errc{} || converted.ptr != token.data() + token.size() || value > 255)
            return reject("contains an invalid handler signature byte");
        p.signature.push_back(static_cast<uint8_t>(value));
    }
    if (p.signature.size() < 16 || p.signature.size() > 64) return reject("contains an invalid handler signature length");
    return true;
}
bool verified(State& s) {
    const auto status = [&](int category, const char* detail) {
        if (s.readiness.exchange(category) != category) log(s, detail);
        return category == 0;
    };
    IMAGE_DOS_HEADER dos{}; IMAGE_NT_HEADERS32 nt{}; uintptr_t handler = 0;
    if (!read(s, s.base, dos) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < 0 || dos.e_lfanew > 4096
        || !read(s, s.base + static_cast<uint32_t>(dos.e_lfanew), nt) || nt.Signature != IMAGE_NT_SIGNATURE
        || nt.FileHeader.Machine != IMAGE_FILE_MACHINE_I386 || nt.FileHeader.TimeDateStamp != s.profile.timestamp)
        return status(1, "Mid-match exit: readiness waiting for the declared x86 image identity");
    if (std::find(s.profile.image_sizes.begin(), s.profile.image_sizes.end(), nt.OptionalHeader.SizeOfImage) == s.profile.image_sizes.end())
        return status(2, "Mid-match exit: readiness image size is not declared in exit.toml");
    if (!read(s, s.base + s.profile.key_slot, handler) || handler != s.base + s.profile.handler)
        return status(3, "Mid-match exit: readiness waiting for the original keyboard pointer");
    std::array<uint8_t, 64> bytes{};
    if (s.host->read(handler, bytes.data(), static_cast<uint32_t>(s.profile.signature.size())) != FTHOOK_OK
        || !std::equal(s.profile.signature.begin(), s.profile.signature.end(), bytes.begin()))
        return status(4, "Mid-match exit: readiness waiting for the original keyboard signature");
    return status(0, "Mid-match exit: client image, keyboard pointer and handler signature verified");
}
bool same_scene(const State& s, const Scene& scene) {
    uintptr_t context = 0, room = 0;
    return WaitForSingleObject(s.stop, 0) == WAIT_TIMEOUT && IsWindow(scene.window)
        && read(s, s.base + s.profile.context, context) && context && context == scene.context
        && read(s, s.base + s.profile.room, room) && room && room == scene.room;
}
std::string operation_id() {
    std::array<uint8_t, 16> bytes{};
    if (BCryptGenRandom(nullptr, bytes.data(), static_cast<ULONG>(bytes.size()), BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0) return {};
    constexpr char digits[] = "0123456789abcdef";
    std::string value = "exit-";
    for (const auto byte : bytes) { value += digits[byte >> 4]; value += digits[byte & 15]; }
    return value;
}
Reply response(int32_t result, const std::vector<char>& output) {
    if (result == FTHOOK_NOT_FOUND) return {Delivery::unavailable, {}};
    const auto end = std::find(output.begin(), output.end(), '\0');
    if (result != FTHOOK_OK || end == output.end()) return {Delivery::uncertain, {}};
    std::string text(output.begin(), end);
    if (text.empty() || !MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0))
        return {Delivery::uncertain, {}};
    return {Delivery::response, std::move(text)};
}
class Bridge final : public Transport {
    State& state_;
    Scene scene_;
    ULONGLONG started_ = GetTickCount64();
public:
    Bridge(State& state, Scene scene) : state_(state), scene_(scene) {}
    bubblefighter::session_state::Result session_state() override {
        return bubblefighter::session_state::query(state_.host);
    }
    bool has_native_exit(int32_t mode) const override {
        const auto& modes = state_.profile.native_exit_modes;
        return std::find(modes.begin(), modes.end(), mode) != modes.end();
    }
    Reply native_ui(std::string_view request) override {
        const auto* services = fthook_get_services(state_.host);
        if (!services) return {Delivery::unavailable, {}};
        std::vector<char> output(65536);
        const std::string text(request);
        const auto code = services->invoke(services->context, "game.native-ui", text.c_str(), output.data(), static_cast<uint32_t>(output.size()));
        return response(code, output);
    }
    Choice windows_confirmation() override {
        if (canceled()) return Choice::no;
        const auto selected = MessageBoxW(scene_.window, L"要立即返回广场吗？", L"中途离场",
            MB_OKCANCEL | MB_ICONQUESTION | MB_DEFBUTTON2);
        return selected == IDOK ? Choice::yes : selected == IDCANCEL ? Choice::no : Choice::failed;
    }
    Reply execute(std::string_view operation, std::string_view payload) override {
        const auto* host = state_.host;
        if (host->struct_size < offsetof(fthook_host, companion) + sizeof(host->companion)) return {Delivery::unavailable, {}};
        const auto* companion = host->companion;
        if (!companion || companion->abi_version != 1 || companion->struct_size < sizeof(*companion) || !companion->invoke)
            return {Delivery::unavailable, {}};
        std::vector<char> output(131073);
        const std::string id(operation), text(payload);
        const auto code = companion->invoke(companion->context, "request", "game.command.execute", 1,
            text.c_str(), 10000, id.c_str(), output.data(), static_cast<uint32_t>(output.size()));
        return response(code, output);
    }
    bool canceled() override { return !same_scene(state_, scene_); }
    bool wait_for_result() override {
        // A native modal may remain open indefinitely; the worker gives up without presenting another.
        return GetTickCount64() - started_ < 300000 && WaitForSingleObject(state_.stop, 50) == WAIT_TIMEOUT && !canceled();
    }
};
DWORD WINAPI worker(void* argument) {
    auto& s = *static_cast<State*>(argument);
    EscapeEdge input;
    bool ready = false;
    while (WaitForSingleObject(s.stop, ready ? 16 : 100) == WAIT_TIMEOUT) {
        if (!ready) {
            ready = verified(s);
            input.sample((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0, false);
            continue;
        }
        Scene scene;
        scene.window = GetForegroundWindow();
        DWORD process = 0;
        if (scene.window) GetWindowThreadProcessId(scene.window, &process);
        if (!input.sample((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0,
            scene.window && process == GetCurrentProcessId())) continue;
        // Context identity cancels stale work; only the authenticated server decides match eligibility.
        if (!read(s, s.base + s.profile.context, scene.context) || !scene.context
            || !read(s, s.base + s.profile.room, scene.room) || !scene.room) continue;
        try {
            const auto id = operation_id();
            if (id.empty()) { log(s, "Mid-match exit: could not create operation identity"); continue; }
            Bridge bridge(s, scene);
            const auto outcome = confirm_and_execute(bridge, id);
            switch (outcome) {
            case Outcome::completed: log(s, "Mid-match exit: plaza command completed"); break;
            case Outcome::canceled: break;
            case Outcome::unavailable: log(s, "Mid-match exit: command channel unavailable"); break;
            case Outcome::rejected: log(s, "Mid-match exit: command rejected"); break;
            case Outcome::uncertain: log(s, "Mid-match exit: outcome uncertain; automatic replay suppressed"); break;
            }
            // Do not reopen an uncertain native modal or replay a completed command within the same scene.
            if (outcome == Outcome::uncertain || outcome == Outcome::completed)
                while (same_scene(s, scene)) if (WaitForSingleObject(s.stop, 100) != WAIT_TIMEOUT) break;
        } catch (...) { log(s, "Mid-match exit: worker failed; automatic replay suppressed"); break; }
    }
    return 0;
}
BOOL CALLBACK close_confirmation(HWND window, LPARAM) {
    wchar_t type[32]{};
    if (GetClassNameW(window, type, 32) && std::wstring_view(type) == L"#32770") PostMessageW(window, WM_CLOSE, 0, 0);
    return TRUE;
}
int32_t FTHOOK_CALL cleanup(void* argument) {
    auto* s = static_cast<State*>(argument);
    if (!s) return FTHOOK_OK;
    SetEvent(s->stop);
    if (s->worker) {
        EnumThreadWindows(s->worker_id, close_confirmation, 0);
        if (WaitForSingleObject(s->worker, 12000) != WAIT_OBJECT_0) return FTHOOK_FAILED;
        CloseHandle(s->worker);
    }
    CloseHandle(s->stop); delete s;
    return FTHOOK_OK;
}
}

int32_t activate(const fthook_host* host, fthook_plugin* plugin) {
    if (!host || !plugin || host->abi_version != FTHOOK_ABI_VERSION
        || host->struct_size < offsetof(fthook_host, read) + sizeof(host->read)
        || !host->read || !host->log || !host->build || !host->plugin_directory)
        return FTHOOK_ABI_UNSUPPORTED;
    const auto activation = std::string("Mid-match exit: version=0.2.5 build=") + host->build + " modes=all";
    host->log(host->context, activation.c_str());
    auto state = std::make_unique<State>(); state->host = host;
    state->base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    if (!load_profile(host, state->profile)) return FTHOOK_INVALID;
    if (state->profile.native_route) {
        log(*state, "Mid-match exit: native profile loaded; runtime must verify and apply the declared mode patches");
        return FTHOOK_OK;
    }
    log(*state, "Mid-match exit: passive Escape polling; no executable or vtable writes; ordinary match exit requires game.session.state");
    state->stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!state->stop) return FTHOOK_FAILED;
    plugin->abi_version = FTHOOK_ABI_VERSION; plugin->struct_size = sizeof(fthook_plugin);
    plugin->context = state.release(); plugin->cleanup = cleanup;
    auto& s = *static_cast<State*>(plugin->context);
    s.worker = CreateThread(nullptr, 0, worker, &s, 0, &s.worker_id);
    if (!s.worker) return FTHOOK_FAILED;
    return FTHOOK_OK;
}

extern "C" int32_t FTHOOK_CALL fthook_plugin_main(const fthook_host* host, fthook_plugin* plugin) {
    try { return activate(host, plugin); }
    catch (...) {
        if (host && host->struct_size >= offsetof(fthook_host, log) + sizeof(host->log) && host->log)
            host->log(host->context, "Mid-match exit: initialization failed");
        return FTHOOK_FAILED;
    }
}

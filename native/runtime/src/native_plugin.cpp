#include "fthook/native_plugin.h"
#include "fthook/overlay.h"
#include "fthook/companion.h"
#include "fthook/plugin_abi.h"
#include "fthook/log.h"
#include "fthook/memory.h"
#include "fthook/services.h"
#include <windows.h>
#include <algorithm>
#include <cstring>
#include <mutex>
#include <set>
#include <stdexcept>

namespace fthook {
namespace {
struct Slots {
    std::mutex mutex;
    size_t count = 0;
    std::set<HMODULE> modules;
};
Slots& slots() {
    // Successful plugins and their host callbacks have process lifetime, including exit.
    static auto* value = new Slots;
    return *value;
}
int32_t invoke_entry(fthook_plugin_entry entry, const fthook_host* host, fthook_plugin* plugin) {
    __try { return entry(host, plugin); }
    __except (GetExceptionCode() == 0xE06D7363u ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER) {
        return FTHOOK_FAILED;
    }
}
bool invoke_cleanup(fthook_cleanup cleanup, void* context) {
    __try { return cleanup(context) == FTHOOK_OK; }
    __except (GetExceptionCode() == 0xE06D7363u ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}
int32_t invoke_locator(fthook_locator locate, void* context, const fthook_image* image, uintptr_t* address) {
    __try { return locate(context, image, address); }
    __except (GetExceptionCode() == 0xE06D7363u ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER) {
        return FTHOOK_FAILED;
    }
}
bool bounded(const char* value, size_t limit) { return value && strnlen_s(value, limit + 1) <= limit; }
}

struct NativePlugin::State {
    HMODULE module = nullptr;
    bool reserved = false, registered = false, initializing = false;
    LocatorRegistry* registry = nullptr;
    std::string id, game, build, branch, directory, log_path;
    std::map<std::string, OptionValue> options;
    std::vector<std::string> locators;
    size_t registered_locators = 0;
    std::vector<PatchSpec> patches;
    fthook_host api{};
    fthook_overlay_host overlay_api{};
    std::unique_ptr<OverlayClient> overlay;
    fthook_companion_host companion_api{};
    std::unique_ptr<CompanionClient> companion;
    fthook_authentication_host authentication_api{};
    fthook_services_host services_api{};
    std::string launch_id, credential_ticket, endpoint_host;
    std::mutex credential_mutex;
    bool credential_active = false;
    uint16_t endpoint_port = 0;
    fthook_plugin plugin{};

    static int32_t FTHOOK_CALL service_register(void* context, const char* name,
        fthook_service_handler handler, void* handler_context) {
        try {
            const auto& self = *static_cast<State*>(context);
            return self.initializing ? register_service(&self, name, handler, handler_context) : FTHOOK_INVALID;
        } catch (...) { return FTHOOK_FAILED; }
    }
    static int32_t FTHOOK_CALL service_invoke(void* context, const char* name, const char* request,
        char* response, uint32_t capacity) {
        try { return invoke_service(static_cast<State*>(context)->id.c_str(), name, request, response, capacity); }
        catch (...) { return FTHOOK_FAILED; }
    }

    static int32_t FTHOOK_CALL copy_credentials(void* context, const char* launch,
        const char* game, const char* build, fthook_credentials* output) {
        try {
            auto& self = *static_cast<State*>(context);
            std::lock_guard guard(self.credential_mutex);
            return protected_credentials(self, launch, game, build, output);
        } catch (...) { return FTHOOK_FAILED; }
    }

    static int32_t protected_credentials(State& self, const char* launch,
        const char* game, const char* build, fthook_credentials* output) {
        // Catch invalid plugin pointers inside the lock so revocation cannot deadlock.
        __try { return copy_credentials_value(self, launch, game, build, output); }
        __except (GetExceptionCode() == 0xE06D7363u ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER) {
            return FTHOOK_INVALID;
        }
    }

    static int32_t copy_credentials_value(const State& self, const char* launch,
        const char* game, const char* build, fthook_credentials* output) {
        if (!output || output->struct_size < sizeof(fthook_credentials)) return FTHOOK_INVALID;
        output->ticket_required = output->endpoint_required = output->endpoint_port = 0;
        if (!self.credential_active || self.credential_ticket.empty() || !launch || !game || !build
            || std::strcmp(launch, self.launch_id.c_str()) || std::strcmp(game, self.game.c_str())
            || std::strcmp(build, self.build.c_str())) return FTHOOK_INVALID;
        if ((!output->ticket && output->ticket_capacity) || (!output->endpoint_host && output->endpoint_capacity))
            return FTHOOK_INVALID;
        if (self.credential_ticket.size() >= UINT32_MAX || self.endpoint_host.size() >= UINT32_MAX) return FTHOOK_FAILED;
        output->ticket_required = static_cast<uint32_t>(self.credential_ticket.size() + 1);
        output->endpoint_required = static_cast<uint32_t>(self.endpoint_host.size() + 1);
        if (output->ticket_capacity < output->ticket_required || output->endpoint_capacity < output->endpoint_required)
            return FTHOOK_BUFFER_TOO_SMALL;
        std::memcpy(output->ticket, self.credential_ticket.c_str(), output->ticket_required);
        std::memcpy(output->endpoint_host, self.endpoint_host.c_str(), output->endpoint_required);
        output->endpoint_port = self.endpoint_port;
        return FTHOOK_OK;
    }

    void clear_credentials() {
        std::lock_guard guard(credential_mutex);
        credential_active = false;
        if (!credential_ticket.empty()) SecureZeroMemory(credential_ticket.data(), credential_ticket.size());
        credential_ticket.clear();
    }

    static int32_t FTHOOK_CALL companion_invoke(void* context, const char* method, const char* type, uint32_t version,
        const char* data, uint32_t timeout_ms, const char* operation_id, char* output, uint32_t capacity) {
        try {
            auto& self = *static_cast<State*>(context);
            return self.companion ? self.companion->invoke(method, type, version, data, timeout_ms, operation_id, output, capacity) : FTHOOK_NOT_FOUND;
        } catch (...) { return FTHOOK_FAILED; }
    }

    static void FTHOOK_CALL log(void* context, const char* message) {
        const auto& self = *static_cast<State*>(context);
        if (message) runtime_log().linef("native id=%s %.*s", self.id.c_str(), 3072, message);
    }
    static int32_t FTHOOK_CALL option(void* context, const char* key, fthook_option* value) {
        try {
            if (!bounded(key, 128) || !value) return FTHOOK_INVALID;
            const auto& options = static_cast<State*>(context)->options;
            const auto it = options.find(key);
            if (it == options.end()) return FTHOOK_NOT_FOUND;
            *value = {};
            if (const auto* boolean = std::get_if<bool>(&it->second)) {
                value->kind = FTHOOK_OPTION_BOOL;
                value->integer = *boolean ? 1 : 0;
            } else if (const auto* integer = std::get_if<int64_t>(&it->second)) {
                value->kind = FTHOOK_OPTION_INT;
                value->integer = *integer;
            } else {
                value->kind = FTHOOK_OPTION_STRING;
                value->text = std::get<std::string>(it->second).c_str();
            }
            return FTHOOK_OK;
        } catch (...) { return FTHOOK_FAILED; }
    }
    static int32_t FTHOOK_CALL scan_image(const fthook_image* image, const char* text, uintptr_t* address) {
        try {
            if (!image || !address || !bounded(text, 1024) || image->region_count > 65536
                || (image->region_count && !image->regions)) return FTHOOK_INVALID;
            *address = 0;
            std::string error;
            const auto pattern = parse_pattern(text, error);
            if (!pattern) return FTHOOK_INVALID;
            std::vector<Region> regions;
            for (uint32_t i = 0; i < image->region_count; ++i)
                regions.push_back({image->regions[i].address, image->regions[i].size});
            const auto found = fthook::scan(regions, *pattern, 2);
            if (!found.count) return FTHOOK_NOT_FOUND;
            if (found.count != 1) return FTHOOK_INVALID;
            *address = found.first;
            return FTHOOK_OK;
        } catch (...) { return FTHOOK_FAILED; }
    }
    static int32_t FTHOOK_CALL read(uintptr_t address, void* destination, uint32_t size) {
        if (!destination || !size || size > 4096) return FTHOOK_INVALID;
        return read_memory(address, destination, size) ? FTHOOK_OK : FTHOOK_FAILED;
    }
    static int32_t FTHOOK_CALL add_locator(void* context, const char* name, fthook_locator locate, void* locator_context) {
        try {
            auto& self = *static_cast<State*>(context);
            if (!self.initializing || !bounded(name, 192) || !locate || self.registered_locators >= 32
                || std::string(name).rfind(self.id + ".", 0) != 0) return FTHOOK_INVALID;
            if (self.registry->find(name)) return FTHOOK_INVALID;
            self.locators.emplace_back(name);
            if (!self.registry->add(name, [locate, locator_context](const ImageInfo& image, std::string& error) -> std::optional<uintptr_t> {
                std::vector<fthook_region> ranges;
                for (const auto& r : image.code) ranges.push_back({r.address, static_cast<uint32_t>(r.size)});
                const fthook_image view{image.base, image.pe_timestamp, image.image_size,
                    ranges.data(), static_cast<uint32_t>(ranges.size())};
                uintptr_t address = 0;
                const int32_t status = invoke_locator(locate, locator_context, &view, &address);
                if (status == FTHOOK_NOT_FOUND) { error = "native locator is not ready"; return std::nullopt; }
                if (status != FTHOOK_OK || !address) throw std::runtime_error("native locator failed (status " + std::to_string(status) + ")");
                return address;
            })) return FTHOOK_INVALID;
            ++self.registered_locators;
            return FTHOOK_OK;
        } catch (...) { return FTHOOK_FAILED; }
    }
    static int32_t FTHOOK_CALL stage_patch(void* context, uintptr_t address, const uint8_t* expected,
        const uint8_t* mask, const uint8_t* replacement, uint32_t size) {
        try {
            auto& self = *static_cast<State*>(context);
            if (!self.initializing || !address || !expected || !mask || !replacement || !size || size > kMaxPatchBytes
                || self.patches.size() >= 64 || address > UINTPTR_MAX - size) return FTHOOK_INVALID;
            for (uint32_t i = 0; i < size; ++i) if (mask[i] != 0 && mask[i] != 255) return FTHOOK_INVALID;
            PatchSpec patch;
            patch.id = "native-write-" + std::to_string(self.patches.size());
            patch.locate.locator = self.id + "." + patch.id;
            patch.wait_unpacked = false;
            patch.expect = Pattern{{expected, expected + size}, {mask, mask + size}};
            patch.write.assign(replacement, replacement + size);
            if (self.registry->find(patch.locate.locator)) return FTHOOK_INVALID;
            self.locators.push_back(patch.locate.locator);
            if (!self.registry->add(patch.locate.locator, [address](const ImageInfo&, std::string&) { return address; }))
                return FTHOOK_INVALID;
            self.patches.push_back(std::move(patch));
            return FTHOOK_OK;
        } catch (...) { return FTHOOK_FAILED; }
    }
    static int32_t FTHOOK_CALL stage_import(void* context, uintptr_t slot, uintptr_t expected, uintptr_t replacement) {
        if (!is_import_slot(slot) || !expected || !replacement) return FTHOOK_INVALID;
        const uint8_t mask[4] = {255, 255, 255, 255};
        const auto result = stage_patch(context, slot, reinterpret_cast<const uint8_t*>(&expected), mask,
            reinterpret_cast<const uint8_t*>(&replacement), 4);
        if (result == FTHOOK_OK) static_cast<State*>(context)->patches.back().import_slot = true;
        return result;
    }
    static int32_t FTHOOK_CALL overlay_register(void* context, const char* panel_id,
        const char* inline_html, const char* html_file_name) {
        try {
            auto& self = *static_cast<State*>(context);
            return self.overlay ? self.overlay->register_panel(panel_id, inline_html, html_file_name) : FTHOOK_NOT_FOUND;
        } catch (...) { return FTHOOK_FAILED; }
    }
    static int32_t FTHOOK_CALL overlay_unregister(void* context, const char* panel_id) {
        try {
            auto& self = *static_cast<State*>(context);
            return self.overlay ? self.overlay->unregister_panel(panel_id) : FTHOOK_NOT_FOUND;
        } catch (...) { return FTHOOK_FAILED; }
    }
    static int32_t FTHOOK_CALL overlay_show(void* context, const char* panel_id) {
        try {
            auto& self = *static_cast<State*>(context);
            return self.overlay ? self.overlay->show_panel(panel_id) : FTHOOK_NOT_FOUND;
        } catch (...) { return FTHOOK_FAILED; }
    }
    static int32_t FTHOOK_CALL overlay_hide(void* context, const char* panel_id) {
        try {
            auto& self = *static_cast<State*>(context);
            return self.overlay ? self.overlay->hide_panel(panel_id) : FTHOOK_NOT_FOUND;
        } catch (...) { return FTHOOK_FAILED; }
    }
    static int32_t FTHOOK_CALL overlay_send(void* context, const char* panel_id,
        const char* event_name, const char* json_data) {
        try {
            auto& self = *static_cast<State*>(context);
            return self.overlay ? self.overlay->send_panel(panel_id, event_name, json_data) : FTHOOK_NOT_FOUND;
        } catch (...) { return FTHOOK_FAILED; }
    }
    static int32_t FTHOOK_CALL overlay_poll(void* context, const char* panel_id, char* output, uint32_t capacity) {
        try {
            auto& self = *static_cast<State*>(context);
            return self.overlay ? self.overlay->poll_event(panel_id, output, capacity) : FTHOOK_NOT_FOUND;
        } catch (...) { return FTHOOK_FAILED; }
    }
};

NativePlugin::NativePlugin(std::unique_ptr<State> state) : state_(std::move(state)) {}

NativePlugin::~NativePlugin() {
    if (!state_) return;
    auto& self = *state_;
    self.initializing = false;
    self.clear_credentials();
    remove_services(&self);
    if (self.registry) for (auto it = self.locators.rbegin(); it != self.locators.rend(); ++it) self.registry->remove(*it);
    bool cleaned = true;
    try { if (self.plugin.cleanup) cleaned = invoke_cleanup(self.plugin.cleanup, self.plugin.context); }
    catch (...) { cleaned = false; }
    if (!cleaned) {
        runtime_log().linef("native id=%s cleanup failed; retaining DLL and API storage until process exit", self.id.c_str());
        // A broken cleanup might leave a callback running. Keep its code and context alive.
        self.registry = nullptr;
        state_.release();
        return;
    }
    if (self.reserved) {
        auto& budget = slots();
        std::lock_guard guard(budget.mutex);
        if (self.registered) budget.modules.erase(self.module);
        --budget.count;
    }
    if (self.module) FreeLibrary(self.module);
}

std::unique_ptr<NativePlugin> NativePlugin::start(PreparedPlugin& prepared, const SessionPlugin& entry,
    const Session& session, LocatorRegistry& locators, std::string& error) {
    auto instance = std::unique_ptr<NativePlugin>(new NativePlugin(std::make_unique<State>()));
    auto& self = *instance->state_;
    self.registry = &locators;
    self.id = prepared.manifest.id;
    {
        auto& budget = slots();
        std::lock_guard guard(budget.mutex);
        if (budget.count >= 64) { error = "native plugin process limit (64) reached"; return nullptr; }
        ++budget.count;
        self.reserved = true;
    }
    const std::wstring library = entry.path + L"\\" + widen(prepared.manifest.native_library);
    wchar_t absolute[32768]{};
    const DWORD length = GetFullPathNameW(library.c_str(), 32768, absolute, nullptr);
    if (!length || length >= 32768) { error = "native library path cannot be resolved"; return nullptr; }
    self.module = LoadLibraryExW(absolute, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!self.module) { error = "cannot load native library (Win32 " + std::to_string(GetLastError()) + ")"; return nullptr; }
    {
        auto& budget = slots();
        std::lock_guard guard(budget.mutex);
        self.registered = budget.modules.insert(self.module).second;
    }
    if (!self.registered) { error = "native DLL already has an activation in this process"; return nullptr; }
    const FARPROC exported = GetProcAddress(self.module, prepared.manifest.native_entry.c_str());
    if (!exported) { error = "native entry '" + prepared.manifest.native_entry + "' is missing"; return nullptr; }
    fthook_plugin_entry main = nullptr;
    static_assert(sizeof(main) == sizeof(exported));
    std::memcpy(&main, &exported, sizeof(main));
    self.game = session.game;
    self.build = session.build;
    self.branch = session.branch;
    self.log_path = narrow(session.log_path);
    std::wstring folder(absolute);
    self.directory = narrow(folder.substr(0, folder.find_last_of(L"\\/")));
    self.options = prepared.options;
    if (!session.overlay_pipe.empty() && session.launcher_pid)
        self.overlay = std::make_unique<OverlayClient>(session.overlay_pipe, session.launcher_pid, self.id);
    self.overlay_api = {1u, sizeof(fthook_overlay_host), &self, State::overlay_register,
        State::overlay_unregister, State::overlay_show, State::overlay_hide, State::overlay_send, State::overlay_poll};
    if (!prepared.manifest.companion_messages.empty() && !session.launch_id.empty() && session.launcher_pid)
        self.companion = std::make_unique<CompanionClient>(L"FantasyTale.CompanionBroker." + widen(session.launch_id),
            session.launcher_pid, self.id, prepared.manifest.companion_messages);
    self.companion_api = {1u, sizeof(fthook_companion_host), &self, State::companion_invoke};
    const bool authentication_granted = !session.credential_plugin.empty() && session.credential_plugin == self.id;
    if (authentication_granted) {
        if (session.launch_id.empty() || (!session.name_login && session.ticket.empty()) || session.endpoint_host.empty() || !session.endpoint_port) {
            error = "authentication grant has incomplete launch material";
            return nullptr;
        }
        self.launch_id = session.launch_id;
        self.credential_ticket = session.name_login ? session.account : session.ticket;
        self.endpoint_host = session.endpoint_host;
        self.endpoint_port = session.endpoint_port;
        self.credential_active = true;
        self.authentication_api = {session.name_login ? 2u : 1u, sizeof(fthook_authentication_host), &self, self.launch_id.c_str(), State::copy_credentials, session.name_login ? 1u : 0u};
    }
    self.api = {FTHOOK_ABI_VERSION, sizeof(fthook_host), &self, self.game.c_str(), self.build.c_str(),
        self.branch.c_str(), self.directory.c_str(), self.log_path.c_str(), State::log, State::option,
        State::scan_image, State::read, State::add_locator, State::stage_patch, State::stage_import,
        self.overlay ? &self.overlay_api : nullptr, self.companion ? &self.companion_api : nullptr,
        authentication_granted ? &self.authentication_api : nullptr, &self.services_api};
    self.services_api = {1u, sizeof(fthook_services_host), &self, State::service_register, State::service_invoke};
    self.plugin = {FTHOOK_ABI_VERSION, sizeof(fthook_plugin), nullptr, nullptr};
    self.initializing = true;
    int32_t result = FTHOOK_FAILED;
    try { result = invoke_entry(main, &self.api, &self.plugin); }
    catch (...) { error = "native entry threw a C++ exception"; }
    self.initializing = false;
    self.clear_credentials();
    if (result != FTHOOK_OK) {
        if (error.empty()) error = "native entry failed (status " + std::to_string(result) + ")";
        return nullptr;
    }
    if (self.plugin.abi_version != FTHOOK_ABI_VERSION || self.plugin.struct_size != sizeof(fthook_plugin)) {
        error = "native entry returned an incompatible ABI descriptor";
        return nullptr;
    }
    for (const auto& patch : self.patches) prepared.patches.push_back(&patch);
    return instance;
}

void NativePlugin::retain(std::unique_ptr<NativePlugin> plugin, bool expose_locators) {
    if (expose_locators) activate_services(plugin->state_.get());
    else remove_services(plugin->state_.get());
    if (!expose_locators && plugin->state_->registry)
        for (const auto& name : plugin->state_->locators) plugin->state_->registry->remove(name);
    plugin->state_->registry = nullptr;
    plugin.release();
}
}

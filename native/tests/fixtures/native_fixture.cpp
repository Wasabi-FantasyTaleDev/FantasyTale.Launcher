#include <fthook/plugin_abi.h>
#include <fthook/companion.h>
#include <windows.h>
#include <cstring>
#include <stdexcept>

namespace {
const fthook_host* api = nullptr;
HANDLE cleanup_event = nullptr;
const char* behavior = "";
int32_t FTHOOK_CALL locate(void*, const fthook_image* image, uintptr_t* address) {
    if (std::strcmp(behavior, "locator-fails") == 0) return FTHOOK_FAILED;
    if (std::strcmp(behavior, "locator-seh") == 0) RaiseException(EXCEPTION_ILLEGAL_INSTRUCTION, 0, 0, nullptr);
    return api->scan(image, "91 23 45 67 AA BB", address);
}
int32_t FTHOOK_CALL cleanup(void*) {
    if (cleanup_event) { SetEvent(cleanup_event); CloseHandle(cleanup_event); cleanup_event = nullptr; }
    if (std::strcmp(behavior, "cleanup-fails") == 0) return FTHOOK_FAILED;
    api = nullptr;
    return FTHOOK_OK;
}
}

extern "C" int32_t FTHOOK_CALL fthook_plugin_main(const fthook_host* host, fthook_plugin* plugin) noexcept(false) {
    if (!host || host->abi_version != FTHOOK_ABI_VERSION || host->struct_size < sizeof(fthook_host)) return FTHOOK_ABI_UNSUPPORTED;
    api = host;
    plugin->context = nullptr;
    plugin->cleanup = cleanup;
    fthook_option option{};
    if (api->option(api->context, "behavior", &option) != FTHOOK_OK || option.kind != FTHOOK_OPTION_STRING) return FTHOOK_INVALID;
    behavior = option.text;
    if (api->option(api->context, "cleanup_event", &option) != FTHOOK_OK) return FTHOOK_INVALID;
    cleanup_event = OpenEventA(EVENT_MODIFY_STATE, FALSE, option.text);
    if (!cleanup_event) return FTHOOK_FAILED;
    if (api->register_locator(api->context, "test.native.site", locate, nullptr) != FTHOOK_OK) return FTHOOK_FAILED;
    if (std::strncmp(behavior, "authentication-", 15) == 0) {
        const auto* auth = fthook_get_authentication(host);
        if (std::strcmp(behavior, "authentication-denied") == 0 || std::strcmp(behavior, "authentication-other-owner") == 0)
            return auth == nullptr ? FTHOOK_OK : FTHOOK_FAILED;
        if (!auth || auth->abi_version != 1 || auth->struct_size < sizeof(fthook_authentication_host)
            || !auth->copy_credentials || !auth->launch_id) return FTHOOK_FAILED;
        fthook_host older = *host;
        older.struct_size = static_cast<uint32_t>(offsetof(fthook_host, authentication));
        if (fthook_get_authentication(&older)) return FTHOOK_FAILED;
        auto invalid = *auth;
        older = *host;
        older.authentication = &invalid;
        invalid.abi_version = 99;
        if (fthook_get_authentication(&older)) return FTHOOK_FAILED;
        char ticket[64] = "unchanged", endpoint[64] = "unchanged";
        fthook_credentials buffers{sizeof(fthook_credentials), ticket, sizeof(ticket), 0, endpoint, sizeof(endpoint), 0, 0};
        if (auth->copy_credentials(auth->context, auth->launch_id, host->game, host->build, nullptr) != FTHOOK_INVALID
            || auth->copy_credentials(auth->context, auth->launch_id, host->game, host->build,
                reinterpret_cast<fthook_credentials*>(1)) != FTHOOK_INVALID
            || auth->copy_credentials(auth->context, reinterpret_cast<const char*>(1), host->game, host->build,
                &buffers) != FTHOOK_INVALID) return FTHOOK_FAILED;
        for (const auto* mismatch : {"launch", "game", "build"}) {
            if (auth->copy_credentials(auth->context, std::strcmp(mismatch, "launch") == 0 ? "wrong" : auth->launch_id,
                std::strcmp(mismatch, "game") == 0 ? "wrong" : host->game,
                std::strcmp(mismatch, "build") == 0 ? "wrong" : host->build, &buffers) != FTHOOK_INVALID
                || buffers.ticket_required || buffers.endpoint_required || buffers.endpoint_port
                || std::strcmp(ticket, "unchanged") || std::strcmp(endpoint, "unchanged")) return FTHOOK_FAILED;
        }
        buffers.struct_size = 0;
        if (auth->copy_credentials(auth->context, auth->launch_id, host->game, host->build, &buffers) != FTHOOK_INVALID)
            return FTHOOK_FAILED;
        buffers.struct_size = sizeof(buffers);
        buffers.ticket = buffers.endpoint_host = nullptr;
        buffers.ticket_capacity = buffers.endpoint_capacity = 0;
        if (auth->copy_credentials(auth->context, auth->launch_id, host->game, host->build, &buffers) != FTHOOK_BUFFER_TOO_SMALL
            || buffers.ticket_required != sizeof("fixture-private-ticket") || buffers.endpoint_required != sizeof("127.0.0.1")
            || buffers.endpoint_port) return FTHOOK_FAILED;
        buffers.ticket = ticket; buffers.endpoint_host = endpoint;
        buffers.ticket_capacity = sizeof(ticket); buffers.endpoint_capacity = 1;
        if (auth->copy_credentials(auth->context, auth->launch_id, host->game, host->build, &buffers) != FTHOOK_BUFFER_TOO_SMALL
            || std::strcmp(ticket, "unchanged") || std::strcmp(endpoint, "unchanged")) return FTHOOK_FAILED;
        buffers.endpoint_capacity = sizeof(endpoint); buffers.ticket = nullptr;
        if (auth->copy_credentials(auth->context, auth->launch_id, host->game, host->build, &buffers) != FTHOOK_INVALID)
            return FTHOOK_FAILED;
        buffers.ticket = ticket;
        buffers.ticket_capacity = sizeof("fixture-private-ticket");
        buffers.endpoint_capacity = sizeof("127.0.0.1");
        if (auth->copy_credentials(auth->context, auth->launch_id, host->game, host->build, &buffers) != FTHOOK_OK
            || std::strcmp(ticket, "fixture-private-ticket") || std::strcmp(endpoint, "127.0.0.1")
            || buffers.endpoint_port != 7100) return FTHOOK_FAILED;
        SecureZeroMemory(ticket, sizeof(ticket));
        return FTHOOK_OK;
    }
    if (std::strcmp(behavior, "companion-api") == 0) {
        const auto* channel = fthook_get_companion(host);
        if (!channel || !channel->invoke) return FTHOOK_FAILED;
        std::string output(131073, '\0');
        if (channel->invoke(channel->context, "send", "forbidden.type", 1, "{}", 0, nullptr,
            output.data(), static_cast<uint32_t>(output.size())) != FTHOOK_INVALID) return FTHOOK_FAILED;
        fthook_host older = *host;
        older.struct_size = static_cast<uint32_t>(offsetof(fthook_host, companion));
        return fthook_get_companion(&older) == nullptr ? FTHOOK_OK : FTHOOK_FAILED;
    }
    if (std::strcmp(behavior, "entry-fails") == 0) return FTHOOK_FAILED;
    if (std::strcmp(behavior, "cleanup-fails") == 0) return FTHOOK_FAILED;
    if (std::strcmp(behavior, "entry-seh") == 0) RaiseException(EXCEPTION_ILLEGAL_INSTRUCTION, 0, 0, nullptr);
    if (std::strcmp(behavior, "abi-mismatch") == 0) { plugin->abi_version = 99; return FTHOOK_OK; }
    if (std::strcmp(behavior, "throws") == 0) throw std::runtime_error("fixture exception");
    if (std::strcmp(behavior, "stage") == 0) {
        if (api->option(api->context, "site", &option) != FTHOOK_OK) return FTHOOK_INVALID;
        const uint8_t expected[] = {0xAA, 0xBB}, mask[] = {255, 255}, replacement[] = {0x90, 0x90};
        return api->stage_patch(api->context, static_cast<uintptr_t>(option.integer), expected, mask, replacement, 2);
    }
    return FTHOOK_OK;
}

extern "C" int32_t FTHOOK_CALL fixture_api_alive() {
    fthook_option value{};
    if (!api || api->option(api->context, "behavior", &value) != FTHOOK_OK) return 0;
    if (api->authentication) {
        fthook_credentials output{sizeof(fthook_credentials)};
        const auto* auth = api->authentication;
        if (auth->copy_credentials(auth->context, auth->launch_id, api->game, api->build, &output) != FTHOOK_INVALID
            || output.ticket_required || output.endpoint_required || output.endpoint_port) return 0;
    }
    return value.kind == FTHOOK_OPTION_STRING && std::strcmp(value.text, behavior) == 0
        && api->register_locator(api->context, "test.native.late", locate, nullptr) == FTHOOK_INVALID;
}

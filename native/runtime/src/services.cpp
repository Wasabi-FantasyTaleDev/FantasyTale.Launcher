#include "fthook/services.h"
#include <windows.h>
#include <condition_variable>
#include <cstring>
#include <map>
#include <memory>
#include <mutex>
#include <string_view>

namespace fthook {
namespace {
struct Service {
    const void* owner = nullptr;
    fthook_service_handler handler = nullptr;
    void* context = nullptr;
    bool active = false;
    size_t calls = 0;
};
struct Registry {
    std::mutex mutex;
    std::condition_variable drained;
    std::map<std::string, std::shared_ptr<Service>> entries;
};
Registry& registry() {
    // Native plugins and successful service callbacks have process lifetime.
    static auto* value = new Registry;
    return *value;
}
int32_t call(fthook_service_handler handler, void* context, const char* caller, const char* request,
    char* response, uint32_t capacity) {
    __try { return handler(context, caller, request, response, capacity); }
    __except (GetExceptionCode() == 0xE06D7363u ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER) {
        return FTHOOK_FAILED;
    }
}
}
int32_t register_service(const void* owner, const char* name,
    fthook_service_handler handler, void* context) {
    if (!owner || !name || !handler || !name[0] || strnlen_s(name, 193) > 192)
        return FTHOOK_INVALID;
    for (const unsigned char c : std::string_view(name))
        if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '.' && c != '_' && c != '-') return FTHOOK_INVALID;
    auto& state = registry();
    std::lock_guard guard(state.mutex);
    if (state.entries.size() >= 64 || state.entries.contains(name)) return FTHOOK_INVALID;
    state.entries.emplace(name, std::make_shared<Service>(owner, handler, context));
    return FTHOOK_OK;
}
void activate_services(const void* owner) {
    auto& state = registry();
    std::lock_guard guard(state.mutex);
    for (auto& [name, service] : state.entries) {
        (void)name;
        if (service->owner == owner) service->active = true;
    }
}
void remove_services(const void* owner) {
    auto& state = registry();
    std::unique_lock guard(state.mutex);
    for (auto& [name, service] : state.entries) {
        (void)name;
        if (service->owner == owner) service->active = false;
    }
    state.drained.wait(guard, [&] {
        for (const auto& [name, service] : state.entries) {
            (void)name;
            if (service->owner == owner && service->calls) return false;
        }
        return true;
    });
    std::erase_if(state.entries, [&](const auto& pair) { return pair.second->owner == owner; });
}
int32_t invoke_service(const char* caller, const char* name, const char* request, char* response, uint32_t capacity) {
    if (!caller || !caller[0] || strnlen_s(caller, 193) > 192 || !name || !request || !response || capacity < 2 || capacity > 65536
        || strnlen_s(name, 193) > 192 || strnlen_s(request, 65536) >= 65536) return FTHOOK_INVALID;
    response[0] = '\0';
    auto& state = registry();
    std::shared_ptr<Service> service;
    {
        std::lock_guard guard(state.mutex);
        const auto it = state.entries.find(name);
        if (it == state.entries.end() || !it->second->active) return FTHOOK_NOT_FOUND;
        service = it->second;
        ++service->calls;
    }
    int32_t result = FTHOOK_FAILED;
    try { result = call(service->handler, service->context, caller, request, response, capacity); }
    catch (...) { result = FTHOOK_FAILED; }
    {
        std::lock_guard guard(state.mutex);
        --service->calls;
        state.drained.notify_all();
    }
    if (strnlen_s(response, capacity) >= capacity) { response[0] = '\0'; return FTHOOK_BUFFER_TOO_SMALL; }
    return result;
}
}

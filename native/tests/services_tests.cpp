#include "check.h"
#include <fthook/services.h>
#include <chrono>
#include <cstring>
#include <future>
#include <stdexcept>

namespace {
int32_t FTHOOK_CALL echo(void*, const char*, const char* request, char* output, uint32_t capacity) {
    return strcpy_s(output, capacity, request) == 0 ? FTHOOK_OK : FTHOOK_BUFFER_TOO_SMALL;
}
int32_t FTHOOK_CALL caller_identity(void*, const char* caller, const char*, char* output, uint32_t capacity) {
    return strcpy_s(output, capacity, caller) == 0 ? FTHOOK_OK : FTHOOK_BUFFER_TOO_SMALL;
}
int32_t FTHOOK_CALL fail(void*, const char*, const char*, char*, uint32_t) { throw std::runtime_error("fixture"); }
int32_t FTHOOK_CALL unterminated(void*, const char*, const char*, char* output, uint32_t capacity) {
    std::memset(output, 'x', capacity);
    return FTHOOK_OK;
}
struct Blocking {
    std::promise<void> entered, release;
};
int32_t FTHOOK_CALL blocking(void* context, const char*, const char*, char*, uint32_t) {
    auto& state = *static_cast<Blocking*>(context);
    state.entered.set_value();
    state.release.get_future().wait();
    return FTHOOK_OK;
}
}
void services_tests() {
    int first = 0, second = 0;
    char output[128]{};
    CHECK(fthook::register_service(&first, "test.echo", echo, nullptr) == FTHOOK_OK);
    CHECK(fthook::invoke_service("test.caller", "test.echo", "text", output, sizeof(output)) == FTHOOK_NOT_FOUND);
    CHECK(fthook::register_service(&second, "test.echo", echo, nullptr) == FTHOOK_INVALID);
    fthook::activate_services(&first);
    fthook::remove_services(&second);
    CHECK(fthook::invoke_service("test.caller", "test.echo", "text", output, sizeof(output)) == FTHOOK_OK);
    CHECK(std::strcmp(output, "text") == 0);
    fthook::remove_services(&first);
    CHECK(fthook::invoke_service("test.caller", "test.echo", "text", output, sizeof(output)) == FTHOOK_NOT_FOUND);
    CHECK(fthook::register_service(&second, "test.echo", echo, nullptr) == FTHOOK_OK);
    CHECK(fthook::register_service(&second, "test.fail", fail, nullptr) == FTHOOK_OK);
    CHECK(fthook::register_service(&second, "test.unterminated", unterminated, nullptr) == FTHOOK_OK);
    CHECK(fthook::register_service(&second, "test.caller", caller_identity, nullptr) == FTHOOK_OK);
    fthook::activate_services(&second);
    CHECK(fthook::invoke_service("test.caller", "test.fail", "", output, sizeof(output)) == FTHOOK_FAILED);
    CHECK(fthook::invoke_service("test.caller", "test.unterminated", "", output, sizeof(output)) == FTHOOK_BUFFER_TOO_SMALL);
    CHECK(output[0] == '\0');
    CHECK(fthook::invoke_service("test.first", "test.caller", "", output, sizeof(output)) == FTHOOK_OK);
    CHECK(std::strcmp(output, "test.first") == 0);
    CHECK(fthook::invoke_service("test.second", "test.caller", "", output, sizeof(output)) == FTHOOK_OK);
    CHECK(std::strcmp(output, "test.second") == 0);
    fthook::remove_services(&second);

    Blocking state;
    CHECK(fthook::register_service(&first, "test.blocking", blocking, &state) == FTHOOK_OK);
    fthook::activate_services(&first);
    auto invoke = std::async(std::launch::async, [&] { return fthook::invoke_service("test.caller", "test.blocking", "", output, sizeof(output)); });
    CHECK(state.entered.get_future().wait_for(std::chrono::seconds(5)) == std::future_status::ready);
    auto remove = std::async(std::launch::async, [&] { fthook::remove_services(&first); });
    CHECK(remove.wait_for(std::chrono::milliseconds(20)) == std::future_status::timeout);
    state.release.set_value();
    CHECK(invoke.get() == FTHOOK_OK);
    CHECK(remove.wait_for(std::chrono::seconds(5)) == std::future_status::ready);
    remove.get();
    CHECK(fthook::invoke_service("test.caller", "test.blocking", "", output, sizeof(output)) == FTHOOK_NOT_FOUND);
}

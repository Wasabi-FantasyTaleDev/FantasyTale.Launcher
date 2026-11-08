#include "../mid-match-exit/exit_flow.h"
#include "../mid-match-exit/early_input.h"
#include "../common/json.h"
#include "check.h"
#include <cstring>
#include <functional>
#include <vector>

namespace {
using namespace mid_match_exit;
void early_input_tests() {
    EscapeEdge input;
    CHECK(!input.sample(false, true));
    CHECK(input.sample(true, true));
    for (unsigned held = 0; held != 100; ++held) CHECK(!input.sample(true, true));
    CHECK(!input.sample(false, true));
    CHECK(input.sample(true, true));
    CHECK(!input.sample(false, false));
    CHECK(!input.sample(true, false));
    CHECK(!input.sample(true, true));
    CHECK(!input.sample(false, true));
    CHECK(input.sample(true, true));
    EscapeEdge startup;
    CHECK(!startup.sample(true, false));
    CHECK(!startup.sample(true, true));
    CHECK(!startup.sample(false, true));
    CHECK(startup.sample(true, true));
}
struct Fake : Transport {
    bubblefighter::session_state::Result state{bubblefighter::session_state::Status::available,
        bubblefighter::session_state::Phase::match, true, true, false, 4};
    bubblefighter::session_state::Result session_state() override { return state; }
    bool native_mode = false;
    bool has_native_exit(int32_t) const override { return native_mode; }
    std::vector<Reply> replies{{Delivery::unavailable, {}}};
    std::vector<std::string> requests;
    std::string operation, payload;
    Reply command_reply{Delivery::response, "{\"status\":\"completed\",\"response\":{\"operation_id\":\"exit-abc\",\"status\":\"success\",\"code\":\"success\",\"message\":\"\"}}"};
    Choice choice = Choice::yes;
    unsigned boxes = 0, sends = 0;
    bool cancel = false, wait = true;
    std::function<void()> before_confirmation;
    Reply native_ui(std::string_view request) override {
        requests.emplace_back(request);
        if (replies.empty()) return {Delivery::uncertain, {}};
        auto result = replies.front(); replies.erase(replies.begin()); return result;
    }
    Choice windows_confirmation() override {
        ++boxes;
        if (before_confirmation) before_confirmation();
        return choice;
    }
    Reply execute(std::string_view id, std::string_view text) override {
        ++sends; operation = id; payload = text; return command_reply;
    }
    bool canceled() override { return cancel; }
    bool wait_for_result() override { return wait; }
};
Reply result(std::string_view fields) {
    return {Delivery::response, "{\"operationId\":\"exit-abc\"," + std::string(fields) + "}"};
}
void native_tests() {
    Fake f;
    f.replies = {result("\"status\":\"queued\""), result("\"status\":\"shown\""),
        result("\"status\":\"selected\",\"buttonId\":\"yes\"")};
    CHECK(confirm_and_execute(f, "exit-abc") == Outcome::completed);
    CHECK(f.boxes == 0 && f.sends == 1 && f.requests.size() == 3);
    CHECK(f.requests.front().find("icon = 'question'") != std::string::npos);
    CHECK(f.requests.front().find("title = '中途离场'") != std::string::npos);
    CHECK(f.requests.front().find("label='  确认'") != std::string::npos);
    CHECK(f.requests.front().find("label='  取消'") != std::string::npos);
    CHECK(f.requests.front().find("要立即返回广场吗？") != std::string::npos);
    CHECK(f.operation == "exit-abc");
    CHECK(f.payload == "{\"operation_id\":\"exit-abc\",\"command\":\"plaza\"}");
    for (const auto& reply : {result("\"status\":\"selected\",\"buttonId\":\"no\""), result("\"status\":\"dismissed\"")}) {
        Fake no; no.replies = {reply};
        CHECK(confirm_and_execute(no, "exit-abc") == Outcome::canceled);
        CHECK(no.boxes == 0 && no.sends == 0);
    }
}
void fallback_tests() {
    for (const auto& reply : {Reply{Delivery::unavailable, {}}, result("\"status\":\"unsupported\""),
        result("\"status\":\"notReady\""), result("\"status\":\"busy\""),
        result("\"status\":\"failed\",\"reason\":\"allocationFailed\"")}) {
        Fake f; f.replies = {reply};
        CHECK(confirm_and_execute(f, "exit-abc") == Outcome::completed);
        CHECK(f.boxes == 1 && f.sends == 1);
    }
    Fake no; no.choice = Choice::no;
    CHECK(confirm_and_execute(no, "exit-abc") == Outcome::canceled);
    CHECK(no.sends == 0);
    Fake failed; failed.choice = Choice::failed;
    CHECK(confirm_and_execute(failed, "exit-abc") == Outcome::unavailable);
    CHECK(failed.sends == 0);
    Fake changed; changed.before_confirmation = [&] { changed.cancel = true; };
    CHECK(confirm_and_execute(changed, "exit-abc") == Outcome::canceled);
    CHECK(changed.sends == 0);
    Fake queued;
    queued.replies = {result("\"status\":\"queued\""), result("\"status\":\"failed\",\"reason\":\"allocationFailed\"")};
    CHECK(confirm_and_execute(queued, "exit-abc") == Outcome::completed);
    CHECK(queued.boxes == 1 && queued.sends == 1);
}
void uncertainty_tests() {
    for (const auto& reply : {Reply{Delivery::uncertain, {}}, Reply{Delivery::response, "garbage"},
        Reply{Delivery::response, "{\"operationId\":\"wrong\",\"status\":\"selected\",\"buttonId\":\"yes\"}"},
        result("\"status\":\"outcomeUnknown\""), result("\"status\":\"selected\",\"buttonId\":\"forged\""),
        result("\"status\":\"failed\",\"reason\":\"adapterInterrupted\""),
        result("\"status\":\"selected\",\"status\":\"dismissed\",\"buttonId\":\"yes\"")}) {
        Fake f; f.replies = {reply};
        CHECK(confirm_and_execute(f, "exit-abc") == Outcome::uncertain);
        CHECK(f.boxes == 0 && f.sends == 0);
    }
    Fake lost; lost.replies = {result("\"status\":\"queued\""), {Delivery::unavailable, {}}};
    CHECK(confirm_and_execute(lost, "exit-abc") == Outcome::uncertain);
    CHECK(lost.boxes == 0 && lost.sends == 0);
    Fake shown;
    shown.replies = {result("\"status\":\"shown\""), result("\"status\":\"notReady\"")};
    CHECK(confirm_and_execute(shown, "exit-abc") == Outcome::uncertain);
    CHECK(shown.boxes == 0 && shown.sends == 0);
    Fake timeout; timeout.wait = false; timeout.replies = {result("\"status\":\"shown\"")};
    CHECK(confirm_and_execute(timeout, "exit-abc") == Outcome::uncertain);
    CHECK(timeout.boxes == 0 && timeout.sends == 0);
    Fake canceled; canceled.cancel = true;
    CHECK(confirm_and_execute(canceled, "exit-abc") == Outcome::canceled);
    CHECK(canceled.requests.empty() && canceled.sends == 0);
    Fake invalid;
    CHECK(confirm_and_execute(invalid, "exit-'\nkind='chat'") == Outcome::rejected);
    CHECK(invalid.requests.empty() && invalid.sends == 0);
    for (const auto& response : {std::string("not_bound"), std::string("busy"), std::string("unavailable")}) {
        Fake f; f.command_reply = {Delivery::response, "{\"status\":\"" + response + "\"}"};
        CHECK(confirm_and_execute(f, "exit-abc") == Outcome::unavailable);
        CHECK(f.sends == 1);
    }
}
void command_tests() {
    const auto reply = [](std::string_view status, std::string_view code) {
        return Reply{Delivery::response, "{\"status\":\"completed\",\"response\":{\"operation_id\":\"exit-abc\",\"status\":"
            + bubblefighter::json::quote(status) + ",\"code\":" + bubblefighter::json::quote(code) + ",\"message\":\"\"}}"};
    };
    for (const auto* code : {"not_found", "invalid_arguments", "denied", "busy", "cancelled", "invalid_state", "operation_conflict"}) {
        Fake f; f.command_reply = reply("rejected", code);
        CHECK(confirm_and_execute(f, "exit-abc") == Outcome::rejected);
        CHECK(f.sends == 1);
    }
    for (const auto& outcome : {reply("success", "failed"), reply("rejected", "success"), reply("error", "failed"),
        reply("success", "unknown"), reply("rejected", "future_code")}) {
        Fake f; f.command_reply = outcome;
        CHECK(confirm_and_execute(f, "exit-abc") == Outcome::uncertain);
        CHECK(f.sends == 1);
    }
    for (const auto* raw : {
        "{\"status\":\"completed\"}",
        "{\"status\":\"completed\",\"response\":null}",
        "{\"status\":\"success\",\"operation_id\":\"exit-abc\",\"code\":\"success\",\"message\":\"\"}",
        "{\"status\":\"completed\",\"response\":{\"operation_id\":\"wrong\",\"status\":\"success\",\"code\":\"success\",\"message\":\"\"}}",
        "{\"status\":\"completed\",\"response\":{\"operation_id\":\"exit-abc\",\"status\":\"success\",\"code\":\"success\"}}",
        "{\"status\":\"completed\",\"response\":{\"operation_id\":\"exit-abc\",\"status\":\"success\",\"status\":\"rejected\",\"code\":\"success\",\"message\":\"\"}}",
        "{\"status\":\"outcome_unknown\"}", "{\"status\":\"canceled\"}"}) {
        Fake f; f.command_reply = {Delivery::response, raw};
        CHECK(confirm_and_execute(f, "exit-abc") == Outcome::uncertain);
        CHECK(f.sends == 1);
    }
    Fake refused; refused.command_reply = {Delivery::response, "{\"status\":\"rejected\",\"response\":{\"code\":\"invalid_request\"}}"};
    CHECK(confirm_and_execute(refused, "exit-abc") == Outcome::rejected);
    CHECK(refused.sends == 1);
}
struct StateReply {
    std::string body;
    int32_t code = FTHOOK_OK;
    unsigned calls = 0;
    bool unterminated = false;
};
int32_t FTHOOK_CALL query_reply(void* context, const char* method, const char* type, uint32_t version,
    const char* payload, uint32_t timeout, const char* operation, char* output, uint32_t capacity) {
    auto& reply = *static_cast<StateReply*>(context);
    ++reply.calls;
    CHECK(std::string_view(method) == "request" && std::string_view(type) == "game.session.state" && version == 1);
    CHECK(std::string_view(payload) == "{}" && operation == nullptr && timeout == 2000);
    if (reply.unterminated) std::memset(output, 'x', capacity);
    else if (reply.body.size() + 1 <= capacity) std::memcpy(output, reply.body.c_str(), reply.body.size() + 1);
    return reply.code;
}
void state_query_tests() {
    using namespace bubblefighter::session_state;
    StateReply reply;
    fthook_companion_host api{1, sizeof(fthook_companion_host), &reply, query_reply};
    fthook_host host{}; host.abi_version = FTHOOK_ABI_VERSION; host.struct_size = sizeof(host); host.companion = &api;
    const auto payload = [](std::string_view fields) { return "{\"status\":\"completed\",\"response\":{" + std::string(fields) + "}}"; };
    reply.body = payload("\"state\":\"match\",\"in_match\":true,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":4");
    const auto live = query(&host);
    CHECK(live.status == Status::available && live.is_in_match() && live.allows_match_exit() && live.mode_id == 4);
    for (const auto* phase : {"unknown", "authenticated", "main_menu", "plaza", "room_waiting", "room_loading",
        "room_countdown", "result", "tutorial", "training", "migrating"}) {
        reply.body = payload("\"state\":" + bubblefighter::json::quote(phase)
            + ",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":false,\"mode_id\":null");
        const auto idle = query(&host);
        CHECK(idle.status == Status::available && !idle.is_in_match() && !idle.allows_match_exit());
    }
    reply.body = payload("\"state\":\"tutorial\",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":true,\"mode_id\":103");
    CHECK(query(&host).is_tutorial && !query(&host).allows_match_exit());
    reply.body = payload("\"state\":\"match\",\"in_match\":true,\"can_leave_match\":false,\"is_tutorial\":false,\"mode_id\":4");
    CHECK(query(&host).is_in_match() && !query(&host).allows_match_exit());
    for (const auto* invalid : {
        "\"state\":\"future_state\",\"in_match\":true,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":4",
        "\"state\":\"plaza\",\"in_match\":true,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":4",
        "\"state\":\"match\",\"in_match\":true,\"can_leave_match\":true,\"is_tutorial\":true,\"mode_id\":4",
        "\"state\":\"match\",\"in_match\":false,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":4",
        "\"state\":\"match\",\"in_match\":true,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":null",
        "\"state\":\"match\",\"in_match\":1,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":4",
        "\"state\":\"match\",\"in_match\":true,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":-1",
        "\"state\":\"match\",\"in_match\":true,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":2147483648",
        "\"state\":\"match\",\"in_match\":true,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":4.5",
        "\"state\":\"match\",\"in_match\":true,\"can_leave_match\":true,\"is_tutorial\":false",
        "\"state\":\"match\",\"state\":\"plaza\",\"in_match\":true,\"can_leave_match\":true,\"is_tutorial\":false,\"mode_id\":4",
        "\"state\":\"unknown\",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":false,\"mode_id\":4"}) {
        reply.body = payload(invalid);
        const auto malformed = query(&host);
        CHECK(malformed.status == Status::invalid_response && !malformed.is_in_match() && !malformed.allows_match_exit());
    }
    for (const auto* status : {"unavailable", "not_bound", "busy", "canceled", "rejected", "outcome_unknown", "accepted"}) {
        reply.body = "{\"status\":" + bubblefighter::json::quote(status) + "}";
        const auto interrupted = query(&host);
        CHECK(interrupted.status != Status::available && !interrupted.is_in_match() && !interrupted.allows_match_exit());
    }
    reply.unterminated = true;
    CHECK(query(&host).status == Status::invalid_response);
    reply.code = FTHOOK_NOT_FOUND;
    CHECK(query(&host).status == Status::unavailable);
    reply.code = FTHOOK_FAILED;
    CHECK(query(&host).status == Status::uncertain);
    const auto calls = reply.calls;
    CHECK(query(&host, 0).status == Status::invalid_request);
    CHECK(query(nullptr).status == Status::unavailable);
    host.struct_size = offsetof(fthook_host, companion);
    CHECK(query(&host).status == Status::unavailable && reply.calls == calls);
}
void state_guard_tests() {
    using namespace bubblefighter::session_state;
    for (const auto phase : {Phase::unknown, Phase::authenticated, Phase::main_menu, Phase::plaza, Phase::room_waiting,
        Phase::room_loading, Phase::room_countdown, Phase::result, Phase::tutorial, Phase::training, Phase::migrating}) {
        Fake f; f.state = {Status::available, phase, false, false, phase == Phase::tutorial, {}};
        CHECK(confirm_and_execute(f, "exit-abc") == Outcome::canceled);
        CHECK(f.requests.empty() && f.boxes == 0 && f.sends == 0);
    }
    for (const auto status : {Status::unavailable, Status::not_bound, Status::busy, Status::canceled, Status::rejected,
        Status::uncertain, Status::invalid_response, Status::invalid_request}) {
        Fake f; f.state.status = status;
        CHECK(confirm_and_execute(f, "exit-abc") == Outcome::unavailable);
        CHECK(f.requests.empty() && f.boxes == 0 && f.sends == 0);
    }
    Fake stale; stale.state.can_leave_match = false;
    CHECK(confirm_and_execute(stale, "exit-abc") == Outcome::canceled && stale.requests.empty() && stale.sends == 0);
    Fake original; original.native_mode = true;
    CHECK(confirm_and_execute(original, "exit-abc") == Outcome::canceled);
    CHECK(original.requests.empty() && original.boxes == 0 && original.sends == 0);
}
}
int main() {
    early_input_tests(); native_tests(); fallback_tests(); uncertainty_tests(); command_tests(); state_query_tests(); state_guard_tests();
    return finish("Mid-match exit confirmation");
}

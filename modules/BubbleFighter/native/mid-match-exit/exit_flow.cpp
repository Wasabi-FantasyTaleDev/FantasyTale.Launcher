#include "exit_flow.h"
#include "../common/json.h"
#include <algorithm>

namespace mid_match_exit {
namespace {
bool valid_operation(std::string_view value) {
    return !value.empty() && value.size() <= 64 && std::all_of(value.begin(), value.end(), [](char c) {
        return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
    });
}
enum class Native { yes, no, unavailable, failed_before_show, queued, shown, uncertain };
Native native_result(const Reply& reply, std::string_view operation) {
    if (reply.delivery == Delivery::unavailable) return Native::unavailable;
    if (reply.delivery != Delivery::response) return Native::uncertain;
    bubblefighter::json::Value value;
    std::string identity, status, button, reason;
    if (!bubblefighter::json::parse(reply.json, value) || !value.string("operationId", identity)
        || identity != operation || !value.string("status", status)) return Native::uncertain;
    if (status == "selected") {
        if (!value.string("buttonId", button)) return Native::uncertain;
        return button == "yes" ? Native::yes : button == "no" ? Native::no : Native::uncertain;
    }
    if (status == "dismissed") return Native::no;
    if (status == "queued") return Native::queued;
    if (status == "shown") return Native::shown;
    if (status == "unsupported" || status == "notReady" || status == "busy") return Native::failed_before_show;
    // These failures occur before the modal loop. Other failures can have displayed UI.
    if (status == "failed" && value.string("reason", reason)
        && (reason == "allocationFailed" || reason == "preparationFailed")) return Native::failed_before_show;
    return Native::uncertain;
}
}

Outcome confirm_and_execute(Transport& transport, std::string_view operation) {
    if (!valid_operation(operation)) return Outcome::rejected;
    if (transport.canceled()) return Outcome::canceled;
    const auto state = transport.session_state();
    if (transport.canceled()) return Outcome::canceled;
    if (state.status != bubblefighter::session_state::Status::available) return Outcome::unavailable;
    if (!state.allows_match_exit()) return Outcome::canceled;
    if (state.mode_id && transport.has_native_exit(*state.mode_id)) return Outcome::canceled;
    const auto identity = std::string(operation);
    const auto request = "action = 'invoke'\noperation_id = '" + identity
        + "'\nkind = 'messageBox'\ntitle = '中途离场'\ntext = '要立即返回广场吗？'\nicon = 'question'\n"
          "buttons = [{id='yes', label='  确认'}, {id='no', label='  取消'}]\n";
    auto reply = transport.native_ui(request);
    bool native_accepted = false;
    bool native_shown = false;
    for (;;) {
        if (transport.canceled()) return Outcome::canceled;
        const auto result = native_result(reply, operation);
        if (result == Native::no) return Outcome::canceled;
        if (result == Native::yes) break;
        if (result == Native::uncertain) return Outcome::uncertain;
        if (result == Native::unavailable || result == Native::failed_before_show) {
            // A correlated pre-presentation failure is safe even after queuing. A lost
            // service or an inconsistent response after presentation is inconclusive.
            if (native_shown || (native_accepted && result == Native::unavailable)) return Outcome::uncertain;
            switch (transport.windows_confirmation()) {
            case Choice::yes: break;
            case Choice::no: return Outcome::canceled;
            case Choice::failed: return Outcome::unavailable;
            }
            break;
        }
        native_accepted = true;
        native_shown |= result == Native::shown;
        if (!transport.wait_for_result()) return transport.canceled() ? Outcome::canceled : Outcome::uncertain;
        reply = transport.native_ui("action = 'result'\noperation_id = '" + identity + "'\n");
    }
    if (transport.canceled()) return Outcome::canceled;
    const auto payload = "{\"operation_id\":" + bubblefighter::json::quote(operation) + ",\"command\":\"plaza\"}";
    const auto response = transport.execute(operation, payload);
    if (response.delivery == Delivery::unavailable) return Outcome::unavailable;
    if (response.delivery != Delivery::response) return Outcome::uncertain;
    bubblefighter::json::Value value;
    std::string status;
    if (!bubblefighter::json::parse(response.json, value) || !value.string("status", status)) return Outcome::uncertain;
    if (status == "unavailable" || status == "not_bound" || status == "busy" || status == "reconnecting") return Outcome::unavailable;
    if (status == "rejected" || status == "unsupported") return Outcome::rejected;
    // NativeCompanionRouter wraps the authenticated response payload in a transport outcome.
    // A completed request alone does not prove that the command itself succeeded.
    if (status != "completed") return Outcome::uncertain;
    const auto* command = value.get("response");
    std::string identity_reply, command_status, code, message;
    if (!command || !command->string("operation_id", identity_reply) || identity_reply != operation
        || !command->string("status", command_status) || !command->string("code", code)
        || !command->string("message", message)) return Outcome::uncertain;
    if (command_status == "success" && code == "success") return Outcome::completed;
    if (command_status == "rejected" && (code == "not_found" || code == "invalid_arguments"
        || code == "denied" || code == "busy" || code == "cancelled" || code == "invalid_state"
        || code == "operation_conflict")) return Outcome::rejected;
    // A failed executor can have applied part of a command before reporting an error.
    return Outcome::uncertain;
}
}

#include "session_state.h"
#include "json.h"
#include <algorithm>
#include <cstddef>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace bubblefighter::session_state {
namespace {
Result failed(Status status) { Result result; result.status = status; return result; }

Result decode(std::string_view text) {
    json::Value envelope;
    std::string status;
    if (!json::parse(text, envelope) || !envelope.string("status", status)) return failed(Status::invalid_response);
    for (const auto& [name, outcome] : {
        std::pair{"unavailable", Status::unavailable}, {"not_bound", Status::not_bound}, {"busy", Status::busy},
        {"canceled", Status::canceled}, {"rejected", Status::rejected}, {"outcome_unknown", Status::uncertain}})
        if (status == name) return failed(outcome);
    if (status != "completed") return failed(Status::invalid_response);
    const auto* snapshot = envelope.get("response");
    std::string phase;
    Result result;
    if (!snapshot || !snapshot->string("state", phase) || !snapshot->flag("in_match", result.in_match)
        || !snapshot->flag("can_leave_match", result.can_leave_match) || !snapshot->flag("is_tutorial", result.is_tutorial))
        return failed(Status::invalid_response);
    bool recognized = false;
    for (const auto& [name, value] : {
        std::pair{"unknown", Phase::unknown}, {"authenticated", Phase::authenticated}, {"main_menu", Phase::main_menu},
        {"plaza", Phase::plaza}, {"room_waiting", Phase::room_waiting}, {"room_loading", Phase::room_loading},
        {"room_countdown", Phase::room_countdown}, {"match", Phase::match}, {"result", Phase::result},
        {"tutorial", Phase::tutorial}, {"training", Phase::training}, {"migrating", Phase::migrating}})
        if (phase == name) { recognized = true; result.phase = value; break; }
    if (!recognized) return failed(Status::invalid_response);
    const auto* mode = snapshot->get("mode_id");
    if (!mode) return failed(Status::invalid_response);
    if (mode->kind != json::Value::Kind::Null) {
        int64_t value = 0;
        if (!snapshot->integer("mode_id", 0, INT32_MAX, value)) return failed(Status::invalid_response);
        result.mode_id = static_cast<int32_t>(value);
    }
    if ((result.in_match && (result.phase != Phase::match || result.is_tutorial))
        || (result.can_leave_match && (!result.in_match || !result.mode_id))
        || (result.phase == Phase::unknown && (result.in_match || result.can_leave_match || result.is_tutorial || result.mode_id)))
        return failed(Status::invalid_response);
    result.status = Status::available;
    return result;
}
}

Result query(const fthook_host* host, uint32_t timeout_ms) {
    if (timeout_ms < 100 || timeout_ms > 300000) return failed(Status::invalid_request);
    if (!host || host->abi_version != FTHOOK_ABI_VERSION
        || host->struct_size < offsetof(fthook_host, companion) + sizeof(host->companion)) return failed(Status::unavailable);
    const auto* companion = host->companion;
    if (!companion || companion->abi_version != 1 || companion->struct_size < sizeof(*companion) || !companion->invoke)
        return failed(Status::unavailable);
    std::vector<char> output(json::kMaxBytes + 1);
    const auto code = companion->invoke(companion->context, "request", "game.session.state", 1,
        "{}", timeout_ms, nullptr, output.data(), static_cast<uint32_t>(output.size()));
    if (code == FTHOOK_NOT_FOUND) return failed(Status::unavailable);
    if (code != FTHOOK_OK) return failed(Status::uncertain);
    const auto end = std::find(output.begin(), output.end(), '\0');
    if (end == output.end()) return failed(Status::invalid_response);
    return decode(std::string_view(output.data(), static_cast<size_t>(end - output.begin())));
}
}

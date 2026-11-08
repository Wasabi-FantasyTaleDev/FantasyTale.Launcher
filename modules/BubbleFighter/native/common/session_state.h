#pragma once
#include <fthook/plugin_abi.h>
#include <cstdint>
#include <optional>

namespace bubblefighter::session_state {

/// Server-owned phase of the authenticated game session, independent of client memory layouts.
enum class Phase {
    unknown, authenticated, main_menu, plaza, room_waiting, room_loading,
    room_countdown, match, result, tutorial, training, migrating
};

/// Transport or validation outcome; a failed query never implies a playable match.
enum class Status { available, unavailable, not_bound, busy, canceled, rejected, uncertain, invalid_response, invalid_request };

/// A momentary snapshot, not permission to run a later command or an indefinitely valid cache.
struct Result {
    Status status = Status::unavailable;
    Phase phase = Phase::unknown;
    bool in_match = false;
    bool can_leave_match = false;
    bool is_tutorial = false;
    std::optional<int32_t> mode_id;

    /// True only for a recognized, server-confirmed ordinary match; excludes tutorials and training.
    bool is_in_match() const { return status == Status::available && phase == Phase::match && in_match && !is_tutorial; }
    /// Confirms that a leave prompt is applicable now; the server must recheck when executing plaza.
    bool allows_match_exit() const { return is_in_match() && can_leave_match; }
};

/// Queries game.session.state v1 with an empty object through the calling plugin's Nirinsou grant.
/// Call on a worker, never a game input/render callback. The manifest must declare that message type.
/// No account, session identity, client memory access, hook, automatic retry or retained cache is used.
/// The finite timeout must be within the companion ABI's 100..300000 ms range.
Result query(const fthook_host* host, uint32_t timeout_ms = 2000);

}

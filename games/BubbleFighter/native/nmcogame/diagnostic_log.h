#pragma once
#include "diagnostic.h"
#include <fthook/log.h>
#include <atomic>

namespace bubblefighter::diagnostic {
// Bound diagnostic work separately from the runtime file cap. Unknown events
// cannot consume this budget or inject text into the log.
inline std::atomic<unsigned> recorded{};
inline void record(uint32_t event, uint32_t value0 = 0, uint32_t value1 = 0, uint32_t value2 = 0) noexcept {
    if (event < Initialize || event > NativeSelector) return;
    unsigned count = recorded.load();
    do {
        if (count >= 128) return;
    } while (!recorded.compare_exchange_weak(count, count + 1));
    auto& log = fthook::runtime_log();
    switch (event) {
    case Initialize:
        log.linef("login-diagnostic initialize result=%d", static_cast<int32_t>(value0));
        break;
    case Signature:
        log.linef("login-diagnostic signature install=%u installed=%u", value0 != 0, value1 != 0);
        break;
    case Active:
        log.linef("login-diagnostic active=%u result=%d", value0 != 0, static_cast<int32_t>(value1));
        break;
    case GetModule:
        log.linef("login-diagnostic get-module result=%d module=%s", static_cast<int32_t>(value0),
            value1 == DisplayModule ? "display" : value1 == LoginModule ? "login" : "unknown");
        break;
    case Login:
        log.linef("login-diagnostic login callback=%s", value0 == Delivered ? "delivered" : value0 == Refused ? "refused" : "absent");
        break;
    case CenterResume:
        log.linef("login-diagnostic center-resume active=%u pending269=%u selected270=%u account-valid=%u ticket-valid=%u ready=%u action=%s",
            (value0 & ActiveFlag) != 0, (value0 & PendingFlag) != 0, (value0 & SelectedFlag) != 0,
            (value0 & AccountValidFlag) != 0, (value0 & TicketValidFlag) != 0, value1 != 0,
            value2 == Inspect ? "inspect" : value2 == Delivered ? "delivered" : value2 == NativeResume ? "native-resume" : "refused");
        break;
    case NmcoCall:
        log.linef("login-diagnostic nmco-call code=%u length=%u stage=%s", value0, value1,
            value2 == Decoded ? "decoded" : value2 == Unreadable ? "unreadable" : value2 == Unhandled ? "unhandled" : "unknown");
        break;
    case Terminal:
        log.line("login-diagnostic terminal");
        break;
    case NativeSelector:
        log.linef("login-diagnostic native-selector phase=%u checks=%u result=%d",
            value0 <= 7 ? value0 : 0, value1 & 15u, value2 <= 1 ? static_cast<int>(value2) : -1);
        break;
    case LoginReplyResult:
        log.linef("login-diagnostic reply result=%d known=%u", value0 <= 7 ? static_cast<int>(value0) : -1, value1 != 0);
        break;
    case LoginConnectionState:
        log.linef("login-diagnostic connection phase=%u known=%u connected=%u authenticated=%u current-known=%u current-connected=%u current-authenticated=%u is-current=%u",
            value0 <= 2 ? value0 : 0, (value1 & 1) != 0, (value1 & 2) != 0, (value1 & 4) != 0,
            (value1 & 8) != 0, (value1 & 16) != 0, (value1 & 32) != 0, (value1 & 64) != 0);
        break;
    default:
        break;
    }
}
} // namespace bubblefighter::diagnostic

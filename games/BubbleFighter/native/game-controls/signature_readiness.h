#pragma once
#include <algorithm>
#include <cstdint>
#include <stdexcept>

namespace game_controls {
// Readiness is checked again at the deadline; no retry sleeps beyond the remaining budget.
template<class Ready, class Clock, class Pause>
bool await_signatures(uint32_t timeout_ms, uint32_t poll_ms, Ready ready, Clock clock, Pause pause) {
    if (!timeout_ms || !poll_ms) throw std::invalid_argument("Native signature wait limits must be positive");
    const uint64_t started = clock();
    for (;;) {
        if (clock() - started > timeout_ms) return false;
        if (ready()) return clock() - started <= timeout_ms;
        const uint64_t elapsed = clock() - started;
        if (elapsed >= timeout_ms) return false;
        pause(static_cast<uint32_t>(std::min<uint64_t>(poll_ms, timeout_ms - elapsed)));
    }
}
}

#pragma once

namespace mid_match_exit {

// Observe only a fresh Escape press while this process owns the foreground window.
// Tracking holds while unfocused prevents Alt-Tab or startup from synthesizing an action.
class EscapeEdge {
    bool held_ = false;
public:
    bool sample(bool down, bool foreground) {
        const bool pressed = down && !held_ && foreground;
        held_ = down;
        return pressed;
    }
};

}

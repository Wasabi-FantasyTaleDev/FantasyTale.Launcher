#pragma once
#include "ui_service.h"
#include <cstdint>
#include <optional>
#include <string>

namespace game_controls {
struct UniformChat { std::string text; uint32_t color; };

// Native chat consumes ARGB words; six-digit RGB inputs are opaque.
inline uint32_t native_color(const std::string& rgba) {
    const auto value = static_cast<uint32_t>(std::stoul(rgba.substr(1), nullptr, 16));
    if (rgba.size() == 7) return 0xFF000000u | value;
    return (value >> 8) | (value << 24);
}

// kr-812 sub_7CD8D0 gives one foreground color to the entire native history row.
inline std::optional<UniformChat> uniform_chat(const std::vector<Run>& runs) {
    if (runs.empty()) return std::nullopt;
    UniformChat result{{}, native_color(runs.front().color)};
    for (const auto& run : runs) {
        if (native_color(run.color) != result.color) return std::nullopt;
        result.text += run.text;
    }
    return result;
}
}

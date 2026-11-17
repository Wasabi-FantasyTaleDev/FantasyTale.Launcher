#pragma once
#include <cstdint>
#include <map>
#include <optional>
#include <string>
#include <string_view>
#include "ui_service.h"

namespace game_controls {
/// Where the application object keeps its stage state, and the stage ids the switch rules compare against.
struct StageLayout {
    uint32_t current_offset = 0, next_offset = 0;
    uint32_t main_menu = 0, custom = 0, idle = 0;
};

/// The prefix of a descriptor address naming one developer tool stage's factory wrapper.
inline constexpr std::string_view stage_tool_prefix = "stage_tool_";

/// The request value that returns from a developer tool stage to the main menu.
inline constexpr std::string_view stage_main_menu = "mainMenu";

/// Returns the refusal reason for a stage move, or an empty view when the move may run.
///
/// kr-812 sub_688390 opens every tool stage from MainMenuStage, so a tool opens only from the main menu (current id
/// `main_menu`). kr-812 sub_A8B050 installs a factory-built stage with the current id `custom` (-2), so the main menu
/// is offered back only from such a stage. A next id other than `idle` (-1) is a switch the game has not applied yet.
template<class Reader>
std::string_view stage_refusal(uintptr_t application, const StageLayout& layout, bool to_main_menu, Reader read) {
    if (!application) return "gameUiNotReady";
    uint32_t current = 0, next = 0;
    if (!read(application + layout.current_offset, &current, static_cast<uint32_t>(sizeof(current)))
        || !read(application + layout.next_offset, &next, static_cast<uint32_t>(sizeof(next)))) return "gameUiNotReady";
    if (next != layout.idle) return "stageSwitchPending";
    if (to_main_menu) return current == layout.custom ? std::string_view{} : "notInToolStage";
    return current == layout.main_menu ? std::string_view{} : "notMainMenu";
}

/// The stage the manager is in, named by the descriptor's build-independent stage names: a factory-built stage is
/// "tool" and an id without a name is "unknown". Nullopt when the manager cannot be read.
template<class Reader>
std::optional<StageState> read_stage(uintptr_t application, const StageLayout& layout, const std::map<int32_t, std::string>& names, Reader read) {
    uint32_t current = 0, next = 0;
    if (!application || !read(application + layout.current_offset, &current, static_cast<uint32_t>(sizeof(current)))
        || !read(application + layout.next_offset, &next, static_cast<uint32_t>(sizeof(next)))) return std::nullopt;
    const auto id = static_cast<int32_t>(current);
    const auto named = names.find(id);
    return StageState{id, current == layout.custom ? "tool" : named != names.end() ? named->second : "unknown", next != layout.idle};
}
}

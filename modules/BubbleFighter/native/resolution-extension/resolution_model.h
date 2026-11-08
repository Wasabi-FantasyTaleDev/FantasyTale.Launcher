#pragma once
#include <cstdint>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace resolution_extension {
struct Mode {
    uint32_t width = 0;
    uint32_t height = 0;
    auto operator<=>(const Mode&) const = default;
};
static_assert(sizeof(Mode) == 8);

enum class Target { Begin, End, HeightBegin, HeightEnd, RemoveCountGuard };
struct Site {
    uint32_t rva = 0;
    std::vector<uint8_t> expected;
    Target target = Target::Begin;
    uint32_t operand_offset = 0;
};
struct CodeGuard {
    uint32_t rva = 0;
    std::vector<uint8_t> expected;
    uint32_t verify_offset = 0;
};
struct StageLayout {
    CodeGuard loader;
    CodeGuard set_width;
    CodeGuard set_height;
    std::vector<CodeGuard> calls;
    uint32_t screen_pointer_rva = 0;
    uint32_t screen_size_offset = 0;
    uint32_t control_size_offset = 0;
    Mode original_size;
};
struct Descriptor {
    std::string build;
    uint32_t timestamp = 0;
    std::vector<uint32_t> image_sizes;
    uint32_t preferred_base = 0;
    uint32_t table_rva = 0;
    uint32_t guard_rva = 0;
    std::vector<uint8_t> guard;
    uint32_t minimum_width = 0;
    uint32_t minimum_height = 0;
    uint32_t maximum_aspect_milli = 0;
    std::vector<Mode> original_modes;
    std::vector<Site> sites;
    std::optional<StageLayout> main_stage;
    std::optional<CodeGuard> menu_filter_call;
};
struct Write {
    uint32_t rva = 0;
    std::vector<uint8_t> expected;
    std::vector<uint8_t> replacement;
};

// Parse one build's complete, non-overlapping pointer-rebinding recipe, or throw.
Descriptor parse_descriptor(std::string_view text);
// Keep original choices and add sorted, distinct hardware modes matching the UI policy.
std::vector<Mode> make_modes(const Descriptor& descriptor, std::span<const Mode> hardware,
    std::span<const Mode> custom = {});
// Parse up to 256 width x height entries in 4096 bytes, or throw on malformed input.
std::vector<Mode> parse_custom_modes(std::string_view text);
// Allow only explicit, non-stock choices inside the active monitor's usable size range.
bool custom_menu_candidate(const Descriptor& descriptor, std::span<const Mode> custom,
    Mode candidate, Mode monitor_size);
// Rebase verified instructions and bind both menu and saved-option validation to one table.
std::vector<Write> make_writes(const Descriptor& descriptor, uint32_t image_base,
    uint32_t table_address, size_t mode_count);
// Redirect only the audited stage-local loader calls, preserving their five-byte call ABI.
std::vector<Write> make_stage_writes(const Descriptor& descriptor, uint32_t image_base, uint32_t callback);
// Redirect only the settings menu's call to the guarded display predicate.
Write make_menu_filter_write(const Descriptor& descriptor, uint32_t image_base, uint32_t callback);
// Expand the stock canvas only for non-original modes; never shrink either dimension.
Mode stage_size(const Descriptor& descriptor, Mode physical, Mode logical, Mode current);
}

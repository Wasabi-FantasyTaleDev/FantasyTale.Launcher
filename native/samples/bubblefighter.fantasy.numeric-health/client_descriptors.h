#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace numeric_health {

/// How the render wrapper obtains the selected player object for a client build.
enum class PlayerCapture {
    /// The preserved EDI value at the intercepted number-wrapper call is the player.
    CallerEdi,
    /// The preserved HUD object's first pointer is the player; ignore caller EDI.
    HudOwner
};

/// Calling convention of the selected floating HP accessor.
enum class HealthAccessor { Thiscall, EdiField };

/// How the selected player's maximum HP is obtained.
enum class MaximumHealthSource { Vtable, PlayerField };

/// Exact bytes required at one optional resource entry point.
struct CodeSignature {
    uint32_t rva = 0;
    std::vector<uint8_t> bytes;
};

/// Maximum word count in a plugin-owned character map and sprite table.
constexpr uint32_t number_table_words = 4096;

/// Runtime addresses and validation bytes for one measured client image.
/// A descriptor marked as research may contain only the evidence collected so far.
struct ClientDescriptor {
    std::string id;
    std::string status;
    uint32_t timestamp = 0;
    uintptr_t preferred_base = 0;
    std::vector<uint32_t> image_sizes;
    std::vector<uint8_t> render_call_bytes;
    uint32_t render_call_rva = 0;
    uint32_t hud_entry_va = 0;
    uint32_t number_wrapper_va = 0;
    uint32_t wrapper_call_va = 0;
    uint32_t health_accessor_call_va = 0;
    std::vector<uint8_t> health_accessor_call_bytes;
    uint32_t health_argument_setup_va = 0;
    std::vector<uint8_t> health_argument_setup_bytes;
    std::vector<uint8_t> wrapper_call_bytes;
    uint32_t health_accessor_va = 0;
    std::string health_accessor_abi;
    HealthAccessor health_accessor = HealthAccessor::Thiscall;
    MaximumHealthSource maximum_health_source = MaximumHealthSource::Vtable;
    uint32_t health_field_offset = 0;
    uint32_t maximum_health_field_offset = 0;
    uint32_t life_accessor_va = 0;
    uint32_t life_accessor_call_va = 0;
    std::vector<uint8_t> life_accessor_call_bytes;
    uint32_t life_field_offset = 0;
    uint32_t life_divisor = 0;
    uint32_t maximum_vtable_slot = 0;
    std::string hud_register;
    uint32_t original_draw_rva = 0;
    uint32_t current_health_rva = 0;
    uint32_t maximum_entry_rva = 0;
    uint32_t maximum_health_rva = 0;
    uint32_t hud_anchor_offset = 0;
    PlayerCapture player_capture = PlayerCapture::CallerEdi;
    uint32_t resource_manager_rva = 0;
    /// Inline capacity of the client string object, measured in UTF-16 code units.
    uint32_t resource_string_inline_capacity = 0;
    /// Size of the per-load resource options object in bytes.
    uint32_t resource_options_size = 0;
    uint32_t construct_options_rva = 0;
    uint32_t destroy_options_rva = 0;
    uint32_t load_sprite_rva = 0;
    uint32_t measure_number_rva = 0;
    uint32_t native_style_slot = 0;
    uint32_t native_style_sprite_index = 0;
    uint32_t sprite_frames_offset = 0;
    uint32_t sprite_texture_offset = 0;
    uint32_t texture_resident_offset = 0;
    std::vector<CodeSignature> resource_signatures;
    bool complete = false;
};

/// Parse one external descriptor file and select the requested build.
/// Returns false with a bounded human-readable error when the file is missing,
/// malformed, or does not contain the build.
bool load_client_descriptor(const std::string& text, const std::string& source,
    const std::string& build, ClientDescriptor& descriptor, std::string& error);

} // namespace numeric_health

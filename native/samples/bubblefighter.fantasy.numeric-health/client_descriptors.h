#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace numeric_health {

/// How the render wrapper obtains the selected player object for a client build.
enum class PlayerCapture {
    /// The preserved EDI value at the intercepted number-wrapper call is the player.
    CallerEdi,
    /// The number-wrapper caller retains the selected player in EBX.
    CallerEbx,
    /// The number-wrapper caller retains the selected player in ESI.
    CallerEsi,
    /// A saved caller stack slot contains the selected player after registers are reused.
    CallerStack,
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
    /// Exact instructions between the HP call return and its floating-point store.
    std::vector<uint8_t> health_result_bytes;
    /// Complete native getter bytes when the HUD inlines the same access instead of calling it.
    std::vector<uint8_t> health_accessor_bytes;
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
    /// Byte offset from the first original draw argument to the saved player pointer.
    uint32_t player_stack_offset = 0;
    /// Existing client number style used when the optional health sprite is unavailable.
    uint32_t white_style_slot = 5;
    uint32_t resource_manager_rva = 0;
    /// String representation consumed by the sprite loader; early COW strings are not MSVC strings.
    std::string resource_string_abi;
    /// Inline capacity of the client string object, measured in UTF-16 code units.
    uint32_t resource_string_inline_capacity = 0;
    /// Size of the per-load resource options object in bytes.
    uint32_t resource_options_size = 0;
    uint32_t construct_options_rva = 0;
    uint32_t destroy_options_rva = 0;
    /// Offset of the member passed to the options destructor; zero destroys the whole record.
    uint32_t destroy_options_offset = 0;
    /// Native constructor and release function for the four-byte COW path object.
    uint32_t construct_string_rva = 0;
    uint32_t release_string_rva = 0;
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

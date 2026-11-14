#include "client_descriptors.h"

#include <toml.hpp>

#include <algorithm>
#include <cctype>
#include <initializer_list>
#include <sstream>
#include <utility>

namespace numeric_health {
namespace {

bool fail(std::string& error, const std::string& path, const std::string& reason) {
    if (error.empty()) error = path + ": " + reason;
    return false;
}

bool only_keys(const toml::table& table, const std::string& path,
    std::initializer_list<std::string_view> allowed, std::string& error) {
    for (auto&& [key, node] : table) {
        (void)node;
        if (std::find(allowed.begin(), allowed.end(), key.str()) == allowed.end())
            return fail(error, path + "." + std::string(key.str()), "unknown key");
    }
    return true;
}

const toml::table* table(const toml::array* builds, size_t index, std::string& error) {
    const toml::node* node = builds->get(index);
    const auto* value = node ? node->as_table() : nullptr;
    if (!value) fail(error, "builds[" + std::to_string(index) + "]", "must be a table");
    return value;
}

bool string_value(const toml::table& t, std::string_view key, const std::string& path,
    std::string& out, std::string& error, bool required = false) {
    const toml::node* node = t.get(key);
    if (!node) return !required || fail(error, path + "." + std::string(key), "missing");
    const auto* value = node->as_string();
    if (!value) return fail(error, path + "." + std::string(key), "must be a string");
    out = value->get();
    return true;
}

bool integer_value(const toml::table& t, std::string_view key, const std::string& path,
    uint32_t& out, std::string& error, bool required = false) {
    const toml::node* node = t.get(key);
    if (!node) return !required || fail(error, path + "." + std::string(key), "missing");
    const auto* value = node->as_integer();
    if (!value || value->get() < 0 || value->get() > UINT32_MAX)
        return fail(error, path + "." + std::string(key), "must be an unsigned 32-bit integer");
    out = static_cast<uint32_t>(value->get());
    return true;
}

bool integer_array(const toml::table& t, std::string_view key, const std::string& path,
    std::vector<uint32_t>& out, std::string& error, bool required = false) {
    const toml::node* node = t.get(key);
    if (!node) return !required || fail(error, path + "." + std::string(key), "missing");
    const auto* array = node->as_array();
    if (!array) return fail(error, path + "." + std::string(key), "must be an array");
    out.clear();
    for (size_t i = 0; i < array->size(); ++i) {
        const auto* value = array->get(i)->as_integer();
        if (!value || value->get() < 0 || value->get() > UINT32_MAX)
            return fail(error, path + "." + std::string(key) + "[" + std::to_string(i) + "]", "must be an unsigned 32-bit integer");
        out.push_back(static_cast<uint32_t>(value->get()));
    }
    return !out.empty() || fail(error, path + "." + std::string(key), "must not be empty");
}

bool hex_bytes(const toml::table& t, std::string_view key, const std::string& path,
    std::vector<uint8_t>& out, std::string& error, bool required = false) {
    std::string text;
    if (!string_value(t, key, path, text, error, required)) return false;
    if (text.empty()) return !required || fail(error, path + "." + std::string(key), "must not be empty");
    std::istringstream stream(text);
    std::string token;
    out.clear();
    while (stream >> token) {
        if (token.size() != 2 || !std::isxdigit(static_cast<unsigned char>(token[0])) ||
            !std::isxdigit(static_cast<unsigned char>(token[1])))
            return fail(error, path + "." + std::string(key), "must contain two-digit hexadecimal bytes");
        const auto digit = [](char c) -> uint8_t {
            c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
            return static_cast<uint8_t>(c <= '9' ? c - '0' : c - 'a' + 10);
        };
        out.push_back(static_cast<uint8_t>((digit(token[0]) << 4) | digit(token[1])));
    }
    return !out.empty() || fail(error, path + "." + std::string(key), "must not be empty");
}

bool resource_signatures(const toml::table& t, const std::string& path,
    std::vector<CodeSignature>& out, std::string& error) {
    const toml::node* node = t.get("resource_signatures");
    if (!node) return fail(error, path + ".resource_signatures", "missing");
    const auto* array = node->as_array();
    if (!array || array->empty() || array->size() > 32)
        return fail(error, path + ".resource_signatures", "must contain 1..32 tables");
    out.clear();
    for (size_t i = 0; i < array->size(); ++i) {
        const auto* item = array->get(i)->as_table();
        const std::string item_path = path + ".resource_signatures[" + std::to_string(i) + "]";
        if (!item) return fail(error, item_path, "must be a table");
        CodeSignature signature;
        if (!integer_value(*item, "rva", item_path, signature.rva, error, true) ||
            !hex_bytes(*item, "bytes", item_path, signature.bytes, error, true)) return false;
        if (signature.bytes.size() != 8)
            return fail(error, item_path + ".bytes", "must contain exactly eight bytes");
        out.push_back(std::move(signature));
    }
    return true;
}

bool resource_layout(const toml::table& t, const std::string& path, ClientDescriptor& descriptor, std::string& error) {
    if (!integer_value(t, "native_style_slot", path, descriptor.native_style_slot, error, true) ||
        !integer_value(t, "native_style_sprite_index", path, descriptor.native_style_sprite_index, error, true) ||
        !integer_value(t, "sprite_frames_offset", path, descriptor.sprite_frames_offset, error, true) ||
        !integer_value(t, "sprite_texture_offset", path, descriptor.sprite_texture_offset, error, true) ||
        !integer_value(t, "texture_resident_offset", path, descriptor.texture_resident_offset, error, true)) return false;
    if (descriptor.native_style_slot < 18 || descriptor.native_style_slot > 30)
        return fail(error, path + ".native_style_slot", "must be in 18..30");
    const auto map_begin = descriptor.native_style_slot * 128 + 2;
    if (descriptor.native_style_sprite_index >= number_table_words || descriptor.native_style_sprite_index < map_begin + 128)
        return fail(error, path + ".native_style_sprite_index", "must be within the private table after its character map");
    for (const auto& field : {std::pair{"sprite_frames_offset", descriptor.sprite_frames_offset},
        std::pair{"sprite_texture_offset", descriptor.sprite_texture_offset}, std::pair{"texture_resident_offset", descriptor.texture_resident_offset}})
        if (field.second == 0 || field.second > 4096 || field.second % 4)
            return fail(error, path + "." + field.first, "must be a four-byte-aligned offset in 4..4096");
    if (descriptor.sprite_texture_offset >= descriptor.sprite_frames_offset &&
        descriptor.sprite_texture_offset < descriptor.sprite_frames_offset + 8)
        return fail(error, path + ".sprite_texture_offset", "must not overlap the frame begin/end pair");
    return true;
}

bool capture_layout(const toml::table& t, const std::string& path, ClientDescriptor& descriptor,
    std::string& error, bool required) {
    std::string capture;
    if (!string_value(t, "hud_register", path, descriptor.hud_register, error, required) ||
        !string_value(t, "player_capture", path, capture, error, required) ||
        !integer_value(t, "white_style_slot", path, descriptor.white_style_slot, error, required)) return false;
    if ((required || !descriptor.hud_register.empty()) && descriptor.hud_register != "ebp" &&
        descriptor.hud_register != "ebx" && descriptor.hud_register != "edi")
        return fail(error, path + ".hud_register", "must be ebp, ebx or edi");
    if (capture == "hud_owner") descriptor.player_capture = PlayerCapture::HudOwner;
    else if (capture == "caller_ebx") descriptor.player_capture = PlayerCapture::CallerEbx;
    else if (capture == "caller_esi") descriptor.player_capture = PlayerCapture::CallerEsi;
    else if (capture == "caller_stack") descriptor.player_capture = PlayerCapture::CallerStack;
    else if ((required || !capture.empty()) && capture != "caller_edi")
        return fail(error, path + ".player_capture", "must be caller_edi, caller_ebx, caller_esi, caller_stack or hud_owner");
    if (descriptor.player_capture == PlayerCapture::CallerStack) {
        if (!integer_value(t, "player_stack_offset", path, descriptor.player_stack_offset, error, true)) return false;
        if (descriptor.player_stack_offset < 28 || descriptor.player_stack_offset > 4096 || descriptor.player_stack_offset % 4)
            return fail(error, path + ".player_stack_offset", "must be a four-byte-aligned offset in 28..4096 after the original arguments");
    } else if (t.contains("player_stack_offset"))
        return fail(error, path + ".player_stack_offset", "requires caller_stack capture");
    if (descriptor.white_style_slot > 30)
        return fail(error, path + ".white_style_slot", "must be in 0..30");
    return true;
}

bool resource_string_layout(const toml::table& t, const std::string& path, ClientDescriptor& descriptor,
    std::string& error, bool supported) {
    if (!string_value(t, "resource_string_abi", path, descriptor.resource_string_abi, error, supported)) return false;
    if (descriptor.resource_string_abi.empty()) {
        if (t.contains("resource_string_inline_capacity"))
            return fail(error, path + ".resource_string_abi", "required when a string capacity is declared");
        return true;
    }
    if (descriptor.resource_string_abi == "cow_wstring4") {
        if (t.contains("resource_string_inline_capacity"))
            return fail(error, path + ".resource_string_inline_capacity", "COW strings have no inline storage");
        return integer_value(t, "construct_string_rva", path, descriptor.construct_string_rva, error, supported) &&
            integer_value(t, "release_string_rva", path, descriptor.release_string_rva, error, supported) &&
            (!supported || (descriptor.construct_string_rva && descriptor.release_string_rva) ||
                fail(error, path + ".construct_string_rva", "COW construction and release must have nonzero addresses"));
    }
    if (descriptor.resource_string_abi != "msvc_wstring24")
        return fail(error, path + ".resource_string_abi", "must be msvc_wstring24 or cow_wstring4");
    if (t.contains("construct_string_rva") || t.contains("release_string_rva"))
        return fail(error, path + ".resource_string_abi", "native string functions require cow_wstring4");
    return true;
}

} // namespace

bool load_client_descriptor(const std::string& text, const std::string& source,
    const std::string& build, ClientDescriptor& descriptor, std::string& error) {
    error.clear();
    descriptor = {};
    try {
        toml::parse_result parsed = toml::parse(text, source);
        if (!parsed) return fail(error, source, std::string(parsed.error().description()));
        const toml::table& root = parsed.table();
        const auto* schema = root.get("schema");
        if (!schema || !schema->as_integer() || schema->as_integer()->get() != 1)
            return fail(error, source + ".schema", "must be 1");
        const auto* builds_node = root.get("builds");
        const auto* builds = builds_node ? builds_node->as_array() : nullptr;
        if (!builds) return fail(error, source + ".builds", "must be an array");
        for (size_t i = 0; i < builds->size(); ++i) {
            const auto* item = table(builds, i, error);
            if (!item || !error.empty()) return false;
            std::string id, path = source + ".builds[" + std::to_string(i) + "]";
            if (!string_value(*item, "id", path, id, error, true)) return false;
            if (id != build) continue;
            descriptor = {};
            descriptor.id = id;
            if (!only_keys(*item, path,
                {"id", "status", "pe_timestamp", "image_sizes", "preferred_base", "render_call_rva",
                 "render_call_bytes", "original_draw_rva", "current_health_rva", "maximum_entry_rva",
                 "maximum_health_rva", "hud_anchor_offset", "player_capture", "resource_manager_rva",
                 "construct_options_rva", "destroy_options_rva", "load_sprite_rva", "measure_number_rva",
                 "destroy_options_offset", "construct_string_rva", "release_string_rva",
                 "resource_signatures", "hud_entry_va", "number_wrapper_va", "wrapper_call_va",
                 "health_accessor_call_va", "health_accessor_call_bytes", "health_argument_setup_va", "health_argument_setup_bytes", "wrapper_call_bytes",
                 "health_result_bytes", "health_accessor_bytes", "player_stack_offset", "white_style_slot", "resource_string_abi",
                 "health_accessor_va", "health_accessor_abi", "health_field_offset", "maximum_health_field_offset", "maximum_health_source",
                 "life_accessor_va", "life_accessor_call_va", "life_accessor_call_bytes", "life_field_offset", "life_divisor", "maximum_vtable_slot",
                 "hud_register", "resource_string_inline_capacity", "resource_options_size", "native_style_slot", "native_style_sprite_index", "sprite_frames_offset", "sprite_texture_offset", "texture_resident_offset"}, error)) return false;
            if (!string_value(*item, "status", path, descriptor.status, error, true) ||
                !integer_value(*item, "pe_timestamp", path, descriptor.timestamp, error, true) ||
                !integer_array(*item, "image_sizes", path, descriptor.image_sizes, error, true)) return false;
            if (descriptor.status != "supported" && descriptor.status != "research")
                return fail(error, path + ".status", "must be supported or research");
            if (!capture_layout(*item, path, descriptor, error, descriptor.status == "supported") ||
                !resource_string_layout(*item, path, descriptor, error, descriptor.status == "supported")) return false;
            if (descriptor.status == "research") {
                integer_value(*item, "resource_string_inline_capacity", path, descriptor.resource_string_inline_capacity, error);
                integer_value(*item, "resource_options_size", path, descriptor.resource_options_size, error);
                integer_value(*item, "destroy_options_offset", path, descriptor.destroy_options_offset, error);
                if (!error.empty()) return false;
                if (descriptor.resource_string_inline_capacity > 64)
                    return fail(error, path + ".resource_string_inline_capacity", "must be in 1..64 when present");
                if (descriptor.resource_string_inline_capacity == 0 && item->contains("resource_string_inline_capacity"))
                    return fail(error, path + ".resource_string_inline_capacity", "must be in 1..64 when present");
                if (descriptor.resource_options_size > 4096 ||
                    (item->contains("resource_options_size") && descriptor.resource_options_size < 4))
                    return fail(error, path + ".resource_options_size", "must be in 4..4096 when present");
                if (descriptor.destroy_options_offset && (descriptor.resource_options_size < 4 ||
                    descriptor.destroy_options_offset % 4 || descriptor.destroy_options_offset > descriptor.resource_options_size - 4))
                    return fail(error, path + ".destroy_options_offset", "must select an aligned member within the options record");
                if ((item->contains("native_style_slot") || item->contains("native_style_sprite_index") ||
                    item->contains("sprite_frames_offset") || item->contains("sprite_texture_offset") || item->contains("texture_resident_offset")) &&
                    !resource_layout(*item, path, descriptor, error)) return false;
                integer_value(*item, "hud_entry_va", path, descriptor.hud_entry_va, error);
                integer_value(*item, "hud_anchor_offset", path, descriptor.hud_anchor_offset, error);
                integer_value(*item, "number_wrapper_va", path, descriptor.number_wrapper_va, error);
                integer_value(*item, "wrapper_call_va", path, descriptor.wrapper_call_va, error);
                integer_value(*item, "health_accessor_call_va", path, descriptor.health_accessor_call_va, error);
                hex_bytes(*item, "health_accessor_call_bytes", path, descriptor.health_accessor_call_bytes, error);
                hex_bytes(*item, "health_result_bytes", path, descriptor.health_result_bytes, error);
                hex_bytes(*item, "health_accessor_bytes", path, descriptor.health_accessor_bytes, error);
                integer_value(*item, "health_argument_setup_va", path, descriptor.health_argument_setup_va, error);
                hex_bytes(*item, "health_argument_setup_bytes", path, descriptor.health_argument_setup_bytes, error);
                hex_bytes(*item, "wrapper_call_bytes", path, descriptor.wrapper_call_bytes, error);
                integer_value(*item, "health_accessor_va", path, descriptor.health_accessor_va, error);
                string_value(*item, "health_accessor_abi", path, descriptor.health_accessor_abi, error);
                integer_value(*item, "health_field_offset", path, descriptor.health_field_offset, error);
                integer_value(*item, "maximum_health_field_offset", path, descriptor.maximum_health_field_offset, error);
                integer_value(*item, "life_accessor_va", path, descriptor.life_accessor_va, error);
                integer_value(*item, "life_accessor_call_va", path, descriptor.life_accessor_call_va, error);
                hex_bytes(*item, "life_accessor_call_bytes", path, descriptor.life_accessor_call_bytes, error);
                integer_value(*item, "life_field_offset", path, descriptor.life_field_offset, error);
                integer_value(*item, "life_divisor", path, descriptor.life_divisor, error);
                integer_value(*item, "maximum_vtable_slot", path, descriptor.maximum_vtable_slot, error);
                if (!error.empty()) return false;
                if (descriptor.health_result_bytes.size() > 32 ||
                    (item->contains("health_result_bytes") && (!descriptor.health_accessor_call_va || descriptor.health_result_bytes.empty())))
                    return fail(error, path + ".health_result_bytes", "requires an HP call and 1..32 exact return-consumer bytes");
                if (descriptor.health_accessor_bytes.size() > 128 ||
                    (item->contains("health_accessor_bytes") && (!descriptor.health_accessor_va || descriptor.health_accessor_bytes.empty())))
                    return fail(error, path + ".health_accessor_bytes", "requires an HP accessor and 1..128 exact getter bytes");
                if (item->contains("health_argument_setup_va") || item->contains("health_argument_setup_bytes")) {
                    if (!descriptor.health_argument_setup_va || descriptor.health_argument_setup_bytes.empty() ||
                        descriptor.health_argument_setup_bytes.size() > 64 ||
                        descriptor.health_argument_setup_va >= descriptor.health_accessor_call_va ||
                        descriptor.health_accessor_call_va - descriptor.health_argument_setup_va != descriptor.health_argument_setup_bytes.size())
                        return fail(error, path + ".health_argument_setup_bytes", "must contain 1..64 bytes ending at the HP call");
                }
                if (!descriptor.health_accessor_abi.empty() && descriptor.health_accessor_abi != "thiscall_f64" &&
                    descriptor.health_accessor_abi != "edi_field_f64")
                    return fail(error, path + ".health_accessor_abi", "must be thiscall_f64 or edi_field_f64");
                if (descriptor.health_accessor_abi == "edi_field_f64" && descriptor.health_field_offset == 0)
                    return fail(error, path + ".health_field_offset", "must be positive for edi_field_f64");
                return error.empty();
            }
            uint32_t preferred_base = 0;
            if (!integer_value(*item, "preferred_base", path, preferred_base, error, true)) return false;
            descriptor.preferred_base = preferred_base;
            if (!string_value(*item, "health_accessor_abi", path, descriptor.health_accessor_abi, error, true)) return false;
            if (descriptor.health_accessor_abi == "thiscall_f64") descriptor.health_accessor = HealthAccessor::Thiscall;
            else if (descriptor.health_accessor_abi == "edi_field_f64") {
                descriptor.health_accessor = HealthAccessor::EdiField;
                if (!integer_value(*item, "health_field_offset", path, descriptor.health_field_offset, error, true)) return false;
                if (descriptor.health_field_offset == 0 || descriptor.health_field_offset > 65536)
                    return fail(error, path + ".health_field_offset", "must be in 1..65536 for edi_field_f64");
            } else return fail(error, path + ".health_accessor_abi", "must be thiscall_f64 or edi_field_f64");
            std::string maximum_source;
            if (!string_value(*item, "maximum_health_source", path, maximum_source, error, true)) return false;
            if (maximum_source == "vtable_f64") {
                descriptor.maximum_health_source = MaximumHealthSource::Vtable;
                if (!integer_value(*item, "maximum_entry_rva", path, descriptor.maximum_entry_rva, error, true) ||
                    !integer_value(*item, "maximum_health_rva", path, descriptor.maximum_health_rva, error, true) ||
                    !integer_value(*item, "maximum_vtable_slot", path, descriptor.maximum_vtable_slot, error, true)) return false;
                if (descriptor.maximum_vtable_slot == 0 || descriptor.maximum_vtable_slot > 4096 ||
                    descriptor.maximum_vtable_slot % sizeof(uint32_t) != 0)
                    return fail(error, path + ".maximum_vtable_slot", "must be a four-byte-aligned offset in 4..4096");
            } else if (maximum_source == "player_field_f32") {
                descriptor.maximum_health_source = MaximumHealthSource::PlayerField;
                if (!integer_value(*item, "maximum_health_field_offset", path, descriptor.maximum_health_field_offset, error, true)) return false;
                if (descriptor.maximum_health_field_offset == 0 || descriptor.maximum_health_field_offset > 65536)
                    return fail(error, path + ".maximum_health_field_offset", "must be in 1..65536 for player_field_f32");
                if (item->contains("maximum_entry_rva") || item->contains("maximum_health_rva") || item->contains("maximum_vtable_slot"))
                    return fail(error, path + ".maximum_health_source", "player_field_f32 cannot also declare vtable access");
            } else return fail(error, path + ".maximum_health_source", "must be vtable_f64 or player_field_f32");
            if (!hex_bytes(*item, "render_call_bytes", path, descriptor.render_call_bytes, error, true) ||
                !integer_value(*item, "render_call_rva", path, descriptor.render_call_rva, error, true) ||
                !integer_value(*item, "original_draw_rva", path, descriptor.original_draw_rva, error, true) ||
                !integer_value(*item, "current_health_rva", path, descriptor.current_health_rva, error, true) ||
                !integer_value(*item, "hud_anchor_offset", path, descriptor.hud_anchor_offset, error, true) ||
                !integer_value(*item, "resource_manager_rva", path, descriptor.resource_manager_rva, error, true) ||
                !integer_value(*item, "resource_string_inline_capacity", path, descriptor.resource_string_inline_capacity, error,
                    descriptor.resource_string_abi == "msvc_wstring24") ||
                !integer_value(*item, "resource_options_size", path, descriptor.resource_options_size, error, true) ||
                !integer_value(*item, "destroy_options_offset", path, descriptor.destroy_options_offset, error) ||
                !integer_value(*item, "construct_options_rva", path, descriptor.construct_options_rva, error, true) ||
                !integer_value(*item, "destroy_options_rva", path, descriptor.destroy_options_rva, error, true) ||
                !integer_value(*item, "load_sprite_rva", path, descriptor.load_sprite_rva, error, true) ||
                !integer_value(*item, "measure_number_rva", path, descriptor.measure_number_rva, error, true) ||
                !resource_signatures(*item, path, descriptor.resource_signatures, error) ||
                !resource_layout(*item, path, descriptor, error)) return false;
            if (descriptor.resource_string_abi == "msvc_wstring24" &&
                (descriptor.resource_string_inline_capacity < 1 || descriptor.resource_string_inline_capacity > 64))
                return fail(error, path + ".resource_string_inline_capacity", "must be in 1..64");
            if (descriptor.resource_options_size < 4 || descriptor.resource_options_size > 4096)
                return fail(error, path + ".resource_options_size", "must be in 4..4096");
            if (descriptor.destroy_options_offset % 4 || descriptor.destroy_options_offset > descriptor.resource_options_size - 4)
                return fail(error, path + ".destroy_options_offset", "must select an aligned member within the options record");
            for (const auto address : {descriptor.construct_options_rva, descriptor.destroy_options_rva,
                descriptor.load_sprite_rva, descriptor.measure_number_rva, descriptor.construct_string_rva, descriptor.release_string_rva}) {
                if (address && std::none_of(descriptor.resource_signatures.begin(), descriptor.resource_signatures.end(),
                    [address](const auto& signature) { return signature.rva == address; }))
                    return fail(error, path + ".resource_signatures", "every native resource entry point requires a signature");
            }
            descriptor.complete = true;
            return true;
        }
        return fail(error, source + ".builds", "requested build is not listed");
    } catch (const std::exception& exception) {
        return fail(error, source, exception.what());
    }
}

} // namespace numeric_health

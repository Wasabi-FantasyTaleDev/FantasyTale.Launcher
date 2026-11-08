#include "numeric_health.cpp"
#include "client_descriptors.cpp"
#include "../../tests/check.h"
#include <array>
#include <string>
#include <vector>
#include <limits>
#include <filesystem>
#include <fstream>

namespace {
std::array<uint8_t, 400> fake_hud{}, fake_table{};
std::array<uint8_t, 16> fake_player{};
std::array<uint8_t, 140> fake_sprite{};
std::array<uint8_t, 148> fake_texture{};
std::array<uint32_t, 8> fake_frame{};
std::array<uint8_t, 140> fake_health_sprite{};
std::array<uint8_t, 148> fake_health_texture{};
std::array<uintptr_t, 10> fake_health_entries{};
std::array<std::array<uint32_t, 8>, 10> fake_health_frames{};
uintptr_t fake_entry = 0;
bool percent_fixture = false;
bool health_fixture = false;
int fixture_style_slot = 22;
uint32_t fixture_sprite_index = 3079;
uint32_t fixture_resource_manager_rva = 0xA7F7A4;
uintptr_t fixture_resource_manager = 1;
uint32_t sprite_load_calls = 0;
double test_current = 125, test_maximum = 250;
uintptr_t last_health_player = 0;
std::vector<std::wstring> drawings;
std::vector<DrawArguments> forwarded;
float last_x = 0, last_y = 0;
int reports = 0;
bool image_fixture = false;
uint32_t fixture_maximum = 0x552060;
std::array<uint8_t, 8> draw_signature{0xE8, 0x56, 0x71, 0x30, 0x00, 0x83, 0xC4, 0x1C};

double __fastcall get_current(void* player, void*) { last_health_player = reinterpret_cast<uintptr_t>(player); return test_current; }
__declspec(naked) double __cdecl get_current_edi() {
    __asm {
        mov last_health_player, edi
        fld test_current
        mov edi, 1
        ret
    }
}
double __fastcall get_maximum(void* player, void*) {
    CHECK(reinterpret_cast<uintptr_t>(player) == last_health_player);
    return test_maximum;
}
void __cdecl draw(void* numbers, float x, float y, int style, float depth, float scale, const wchar_t* text) {
    CHECK(style == 5 || style == fixture_style_slot);
    if (style == fixture_style_slot) {
        const auto* table = static_cast<uint32_t*>(numbers);
        if (std::wstring(text) == L"%%") {
            CHECK(table[fixture_style_slot * 128 + 2 + L'%'] == 0 && table[fixture_sprite_index] == reinterpret_cast<uintptr_t>(fake_sprite.data()));
        } else {
            CHECK(table[fixture_sprite_index] == reinterpret_cast<uintptr_t>(fake_health_sprite.data()));
            for (uint32_t digit = 0; digit < 10; ++digit) CHECK(table[fixture_style_slot * 128 + 2 + L'0' + digit] == digit);
        }
    }
    forwarded.push_back({numbers, x, y, style, depth, scale, text});
    drawings.emplace_back(std::wstring(text) == L"%%" ? L"%" : text);
    if (!text || text[0] != L'%') { last_x = x; last_y = y; }
}
void FTHOOK_CALL log(void*, const char*) { ++reports; }
int32_t FTHOOK_CALL read_fixture(uintptr_t address, void* destination, uint32_t size) {
    if (percent_fixture && address == image_base + fixture_resource_manager_rva && size == 4) {
        std::memcpy(destination, &fixture_resource_manager, size);
        return FTHOOK_OK;
    }
    if (image_fixture && address == 0x7DFB55 && size == draw_signature.size()) {
        std::memcpy(destination, draw_signature.data(), size);
        return FTHOOK_OK;
    }
    if (image_fixture && address == 0xD0C808 && size == 4) {
        std::memcpy(destination, &fixture_maximum, size);
        return FTHOOK_OK;
    }
    for (const auto& range : {std::pair{fake_hud.data(), fake_hud.size()},
        std::pair{fake_table.data(), fake_table.size()}, std::pair{fake_player.data(), fake_player.size()},
        std::pair{fake_sprite.data(), fake_sprite.size()}, std::pair{fake_texture.data(), fake_texture.size()},
        std::pair{fake_health_sprite.data(), fake_health_sprite.size()}, std::pair{fake_health_texture.data(), fake_health_texture.size()},
        std::pair{reinterpret_cast<uint8_t*>(fake_health_entries.data()), sizeof(fake_health_entries)},
        std::pair{reinterpret_cast<uint8_t*>(fake_health_frames.data()), sizeof(fake_health_frames)},
        std::pair{reinterpret_cast<uint8_t*>(fake_frame.data()), sizeof(fake_frame)},
        std::pair{reinterpret_cast<uint8_t*>(&fake_entry), sizeof(fake_entry)}}) {
        const auto first = reinterpret_cast<uintptr_t>(range.first);
        if (address >= first && size <= range.second && address - first <= range.second - size) {
            std::memcpy(destination, reinterpret_cast<const void*>(address), size);
            return FTHOOK_OK;
        }
    }
    return FTHOOK_INVALID;
}
void* __fastcall construct_options(void* options, void*) { return options; }
void __fastcall destroy_options(void*, void*) {}
uintptr_t __fastcall load_percent(void* manager, void*, const void* name, void*) {
    ++sprite_load_calls;
    CHECK(manager == reinterpret_cast<void*>(1));
    const auto* path = static_cast<const native_style::BorrowedString*>(name);
    if (health_fixture && std::wstring(path->data) == L"Images/GameStage/myHP/myHPtext00.smf")
        return reinterpret_cast<uintptr_t>(fake_health_sprite.data());
    CHECK(std::wstring(path->data) == native_style::percent_path && path->size == wcslen(path->data));
    return reinterpret_cast<uintptr_t>(fake_sprite.data());
}
void __fastcall measure_percent(void*, void*, float, float, int style, const wchar_t* text, float, float scale_x, float scale_y, int flags, float* size) {
    CHECK((style == 5 || style == fixture_style_slot) && flags == 0x400 && scale_x == scale_y);
    size[0] = static_cast<float>(wcslen(text)) * 8.0f * scale_x;
    size[1] = 18.0f * scale_y;
}
template<typename T> void put(uint8_t* storage, size_t offset, T value) {
    std::memcpy(storage + offset, &value, sizeof(value));
}
__declspec(naked) void __cdecl invoke_intercept(const DrawArguments*, uintptr_t, uintptr_t) {
    __asm {
        push ebp
        push ebx
        push esi
        push edi
        mov esi, [esp + 20]
        mov ebp, [esp + 24]
        mov edi, [esp + 28]
        push dword ptr [esi + 24]
        push dword ptr [esi + 20]
        push dword ptr [esi + 16]
        push dword ptr [esi + 12]
        push dword ptr [esi + 8]
        push dword ptr [esi + 4]
        push dword ptr [esi]
        call draw_intercept
        add esp, 28
        pop edi
        pop esi
        pop ebx
        pop ebp
        ret
    }
}

__declspec(naked) uint32_t __cdecl invoke_intercept_ebx(const DrawArguments*, uintptr_t, uintptr_t) {
    __asm {
        push ebp
        push ebx
        push esi
        push edi
        mov esi, [esp + 20]
        mov ebx, [esp + 24]
        mov edi, [esp + 28]
        mov ebp, 1
        push dword ptr [esi + 24]
        push dword ptr [esi + 20]
        push dword ptr [esi + 16]
        push dword ptr [esi + 12]
        push dword ptr [esi + 8]
        push dword ptr [esi + 4]
        push dword ptr [esi]
        call draw_intercept_ebx
        add esp, 28
        xor eax, eax
        cmp ebp, 1
        je ebp_preserved
        or eax, 1
    ebp_preserved:
        cmp ebx, [esp + 24]
        je ebx_preserved
        or eax, 2
    ebx_preserved:
        cmp esi, [esp + 20]
        je esi_preserved
        or eax, 4
    esi_preserved:
        cmp edi, [esp + 28]
        je edi_preserved
        or eax, 8
    edi_preserved:
        pop edi
        pop esi
        pop ebx
        pop ebp
        ret
    }
}
}

int main() {
    const auto source = std::filesystem::path(__FILE__).parent_path();
    std::ifstream descriptor_file(source / "client-versions.toml", std::ios::binary);
    const std::string descriptor_text((std::istreambuf_iterator<char>(descriptor_file)), std::istreambuf_iterator<char>());
    numeric_health::ClientDescriptor parsed_descriptor;
    std::string descriptor_error;
    CHECK(numeric_health::load_client_descriptor(descriptor_text, "client-versions.toml", "kr-812", parsed_descriptor, descriptor_error));
    CHECK(parsed_descriptor.complete && parsed_descriptor.preferred_base == 0x400000 &&
        parsed_descriptor.render_call_rva == 0x3DFB55 && parsed_descriptor.render_call_bytes.size() == 8 &&
        parsed_descriptor.resource_signatures.size() == 4 && parsed_descriptor.resource_signatures[0].rva == 0x6C4D60 &&
        parsed_descriptor.maximum_vtable_slot == 356 && parsed_descriptor.hud_register == "ebp" &&
        parsed_descriptor.resource_string_inline_capacity == 7 && parsed_descriptor.resource_options_size == 88);
    const auto rejects_register = [&](const std::string& value) {
        auto changed = descriptor_text;
        const auto at = changed.find("hud_register = \"ebp\"");
        CHECK(at != std::string::npos);
        changed.replace(at, std::strlen("hud_register = \"ebp\""), value);
        numeric_health::ClientDescriptor candidate;
        std::string error;
        return !numeric_health::load_client_descriptor(changed, "client-versions.toml", "kr-812", candidate, error) &&
            error.find("hud_register") != std::string::npos;
    };
    CHECK(rejects_register(""));
    CHECK(rejects_register("hud_register = \"eax\""));
    CHECK(rejects_register("hud_register = 0"));
    {
        auto changed = descriptor_text;
        changed.replace(changed.find("hud_register = \"ebp\""), std::strlen("hud_register = \"ebp\""), "hud_register = \"ebx\"");
        numeric_health::ClientDescriptor candidate;
        std::string error;
        CHECK(numeric_health::load_client_descriptor(changed, "client-versions.toml", "kr-812", candidate, error));
        CHECK(candidate.complete && candidate.hud_register == "ebx");
    }
    const auto rejects_slot = [&](const std::string& value) {
        auto changed = descriptor_text;
        const auto at = changed.find("maximum_vtable_slot = 356");
        CHECK(at != std::string::npos);
        changed.replace(at, std::strlen("maximum_vtable_slot = 356"), value);
        numeric_health::ClientDescriptor candidate;
        std::string error;
        return !numeric_health::load_client_descriptor(changed, "client-versions.toml", "kr-812", candidate, error) &&
            error.find("maximum_vtable_slot") != std::string::npos;
    };
    CHECK(rejects_slot(""));
    CHECK(rejects_slot("maximum_vtable_slot = 0"));
    CHECK(rejects_slot("maximum_vtable_slot = 355"));
    CHECK(rejects_slot("maximum_vtable_slot = 4100"));
    const auto rejects_runtime_contract = [&](const std::string& old_value, const std::string& new_value, const char* key) {
        auto changed = descriptor_text;
        changed.replace(changed.find(old_value), old_value.size(), new_value);
        numeric_health::ClientDescriptor candidate;
        std::string error;
        return !numeric_health::load_client_descriptor(changed, "client-versions.toml", "kr-812", candidate, error) &&
            error.find(key) != std::string::npos;
    };
    CHECK(rejects_runtime_contract("health_accessor_abi = \"thiscall_f64\"", "", "health_accessor_abi"));
    CHECK(rejects_runtime_contract("health_accessor_abi = \"thiscall_f64\"", "health_accessor_abi = \"thiscall_i32\"", "health_accessor_abi"));
    CHECK(rejects_runtime_contract("health_accessor_abi = \"thiscall_f64\"", "health_accessor_abi = \"edi_field_f64\"", "health_field_offset"));
    CHECK(rejects_runtime_contract("maximum_health_source = \"vtable_f64\"", "", "maximum_health_source"));
    CHECK(rejects_runtime_contract("maximum_health_source = \"vtable_f64\"", "maximum_health_source = \"invalid\"", "maximum_health_source"));
    CHECK(rejects_runtime_contract("maximum_health_source = \"vtable_f64\"", "maximum_health_source = \"player_field_f32\"\nmaximum_health_field_offset = 12", "maximum_health_source"));
    CHECK(rejects_runtime_contract("native_style_slot = 22", "", "native_style_slot"));
    CHECK(rejects_runtime_contract("native_style_slot = 22", "native_style_slot = 17", "native_style_slot"));
    CHECK(rejects_runtime_contract("native_style_slot = 22", "native_style_slot = 31", "native_style_slot"));
    CHECK(rejects_runtime_contract("native_style_sprite_index = 3079", "native_style_sprite_index = 2818", "native_style_sprite_index"));
    CHECK(rejects_runtime_contract("native_style_sprite_index = 3079", "native_style_sprite_index = 4096", "native_style_sprite_index"));
    CHECK(rejects_runtime_contract("sprite_frames_offset = 120", "sprite_frames_offset = 121", "sprite_frames_offset"));
    CHECK(rejects_runtime_contract("sprite_texture_offset = 136", "sprite_texture_offset = 124", "sprite_texture_offset"));
    CHECK(rejects_runtime_contract("texture_resident_offset = 144", "texture_resident_offset = 0", "texture_resident_offset"));
    CHECK(rejects_runtime_contract("resource_string_inline_capacity = 7", "resource_string_inline_capacity = 0", "resource_string_inline_capacity"));
    CHECK(rejects_runtime_contract("resource_options_size = 88", "resource_options_size = 3", "resource_options_size"));
    CHECK(rejects_runtime_contract("resource_options_size = 88", "resource_options_size = 4097", "resource_options_size"));
    {
        auto changed = descriptor_text;
        const auto replace = [&](const std::string& before, const std::string& after) { changed.replace(changed.find(before), before.size(), after); };
        replace("health_accessor_abi = \"thiscall_f64\"", "health_accessor_abi = \"edi_field_f64\"\nhealth_field_offset = 8");
        replace("maximum_health_source = \"vtable_f64\"", "maximum_health_source = \"player_field_f32\"\nmaximum_health_field_offset = 12");
        replace("maximum_entry_rva = 0x90C808", "");
        replace("maximum_health_rva = 0x152060", "");
        replace("maximum_vtable_slot = 356", "");
        CHECK(numeric_health::load_client_descriptor(changed, "client-versions.toml", "kr-812", parsed_descriptor, descriptor_error));
        CHECK(parsed_descriptor.complete && parsed_descriptor.health_accessor == numeric_health::HealthAccessor::EdiField &&
            parsed_descriptor.maximum_health_source == numeric_health::MaximumHealthSource::PlayerField &&
            parsed_descriptor.health_field_offset == 8 && parsed_descriptor.maximum_health_field_offset == 12);
        for (const char* invalid : {"0", "65537"}) {
            auto bad = changed;
            const std::string before = "health_field_offset = 8";
            bad.replace(bad.find(before), before.size(), std::string("health_field_offset = ") + invalid);
            CHECK(!numeric_health::load_client_descriptor(bad, "client-versions.toml", "kr-812", parsed_descriptor, descriptor_error));
        }
    }
    CHECK(numeric_health::load_client_descriptor(descriptor_text, "client-versions.toml", "cn-279", parsed_descriptor, descriptor_error));
    const std::vector<uint8_t> cn2011_life_call{0xE8, 0xFA, 0xF1, 0xCE, 0xFF};
    const std::vector<uint8_t> cn2011_wrapper_call{0xE8, 0x5B, 0x37, 0xE2, 0xFF};
    CHECK(!parsed_descriptor.complete && parsed_descriptor.status == "research" && parsed_descriptor.hud_entry_va == 0x716880 &&
        parsed_descriptor.number_wrapper_va == 0x53AD30 && parsed_descriptor.wrapper_call_va == 0x7175D0 &&
        parsed_descriptor.life_accessor_call_va == 0x7174D1 &&
        parsed_descriptor.life_accessor_call_bytes == cn2011_life_call &&
        parsed_descriptor.wrapper_call_bytes == cn2011_wrapper_call &&
        parsed_descriptor.life_accessor_va == 0x4066D0 && parsed_descriptor.maximum_vtable_slot == 0 && parsed_descriptor.maximum_health_field_offset == 5184 &&
        parsed_descriptor.life_field_offset == 4732 && parsed_descriptor.life_divisor == 20 && parsed_descriptor.hud_register == "ebx" &&
        parsed_descriptor.resource_string_inline_capacity == 7 && parsed_descriptor.resource_options_size == 32);
        CHECK(numeric_health::load_client_descriptor(descriptor_text, "client-versions.toml", "cn-408", parsed_descriptor, descriptor_error));
    CHECK(!parsed_descriptor.complete && parsed_descriptor.status == "research" &&
        parsed_descriptor.timestamp == 0x5383216F && parsed_descriptor.image_sizes == std::vector<uint32_t>{0xCDC000} &&
        parsed_descriptor.hud_entry_va == 0x795250 && parsed_descriptor.number_wrapper_va == 0xA9EEF0);
    CHECK(numeric_health::load_client_descriptor(descriptor_text, "client-versions.toml", "cn-406", parsed_descriptor, descriptor_error));
    const std::vector<uint8_t> cn2013_life_call{0xE8, 0xAA, 0x2D, 0xF7, 0xFF};
    const std::vector<uint8_t> cn2013_wrapper_call{0xE8, 0x07, 0x61, 0x2E, 0x00};
    CHECK(!parsed_descriptor.complete && parsed_descriptor.status == "research" &&
        parsed_descriptor.hud_entry_va == 0x6E5BD0 && parsed_descriptor.number_wrapper_va == 0x9CC640 &&
        parsed_descriptor.wrapper_call_va == 0x6E6534 && parsed_descriptor.life_accessor_call_va == 0x6E6361 &&
        parsed_descriptor.life_accessor_call_bytes == cn2013_life_call &&
        parsed_descriptor.wrapper_call_bytes == cn2013_wrapper_call &&
        parsed_descriptor.life_accessor_va == 0x659110 && parsed_descriptor.maximum_vtable_slot == 340 &&
        parsed_descriptor.resource_string_inline_capacity == 7 && parsed_descriptor.resource_options_size == 88);
    CHECK(numeric_health::load_client_descriptor(descriptor_text, "client-versions.toml", "cn-706", parsed_descriptor, descriptor_error));
    CHECK(!parsed_descriptor.complete && parsed_descriptor.status == "research" &&
        parsed_descriptor.timestamp == 0x5420DCC3 && parsed_descriptor.image_sizes == std::vector<uint32_t>{0xC79000} &&
        parsed_descriptor.life_accessor_call_va == 0x76B894 && parsed_descriptor.wrapper_call_va == 0x76BA5B &&
        parsed_descriptor.life_accessor_va == 0x4B8800);
        CHECK(numeric_health::load_client_descriptor(descriptor_text, "client-versions.toml", "cn-705", parsed_descriptor, descriptor_error));
    CHECK(!parsed_descriptor.complete && parsed_descriptor.status == "research" &&
        parsed_descriptor.timestamp == 0x53DA0A65 && parsed_descriptor.image_sizes == std::vector<uint32_t>{0xC79000} &&
        parsed_descriptor.life_accessor_call_va == 0x76A354 && parsed_descriptor.wrapper_call_va == 0x76A51B &&
        parsed_descriptor.life_accessor_va == 0x4B86D0);
    struct HealthEvidence { const char* id; uint32_t target; uint32_t site; const char* abi; uint32_t anchor; uint32_t setup; };
    const HealthEvidence health_evidence[] = {
        {"cn-279", 0x4063E0, 0x717457, "edi_field_f64", 184, 0x71744F},
        {"cn-324", 0x40FD50, 0x6D36B2, "thiscall_f64", 224, 0x6D36A1},
        {"cn-406", 0x41C9F0, 0x6E5F23, "thiscall_f64", 248, 0x6E5F12},
        {"cn-408", 0x41EFB0, 0x795591, "thiscall_f64", 248, 0x79557F},
        {"cn-705", 0x41FF40, 0x769F22, "thiscall_f64", 248, 0x769F0F},
        {"cn-706", 0x4205E0, 0x76B462, "thiscall_f64", 276, 0x76B44F}
    };
    for (const auto& evidence : health_evidence) {
        CHECK(numeric_health::load_client_descriptor(descriptor_text, "client-versions.toml", evidence.id, parsed_descriptor, descriptor_error));
        CHECK(!parsed_descriptor.complete && parsed_descriptor.health_accessor_va == evidence.target &&
            parsed_descriptor.health_accessor_call_va == evidence.site && parsed_descriptor.health_accessor_abi == evidence.abi &&
            parsed_descriptor.health_accessor_call_bytes.size() == 5 && parsed_descriptor.life_divisor == 20 &&
            parsed_descriptor.life_accessor_va != parsed_descriptor.health_accessor_va);
        CHECK(parsed_descriptor.player_capture == numeric_health::PlayerCapture::HudOwner &&
            parsed_descriptor.hud_anchor_offset == evidence.anchor && parsed_descriptor.health_argument_setup_va == evidence.setup &&
            parsed_descriptor.health_argument_setup_bytes.size() == evidence.site - evidence.setup);
    }
    const auto rejects_health_contract = [&](const std::string& old_value, const std::string& new_value, const char* key) {
        auto changed = descriptor_text;
        changed.replace(changed.find(old_value), old_value.size(), new_value);
        numeric_health::ClientDescriptor candidate;
        std::string error;
        return !numeric_health::load_client_descriptor(changed, "client-versions.toml", "cn-279", candidate, error) &&
            error.find(key) != std::string::npos;
    };
    CHECK(rejects_health_contract("health_accessor_abi = \"edi_field_f64\"", "health_accessor_abi = \"thiscall_i32\"", "health_accessor_abi"));
    CHECK(rejects_health_contract("health_field_offset = 372", "health_field_offset = 0", "health_field_offset"));
    CHECK(rejects_health_contract("health_field_offset = 372", "", "health_field_offset"));
    CHECK(rejects_health_contract("health_argument_setup_va = 0x71744F", "health_argument_setup_va = 0x717450", "health_argument_setup_bytes"));
    CHECK(rejects_health_contract("health_argument_setup_bytes = \"8B 03 8D B8 74 01 00 00\"", "", "health_argument_setup_bytes"));
    CHECK(rejects_health_contract("player_capture = \"hud_owner\"", "player_capture = \"eax\"", "player_capture"));
    CHECK(!numeric_health::load_client_descriptor(descriptor_text, "client-versions.toml", "unknown", parsed_descriptor, descriptor_error));
    CHECK(!descriptor_error.empty());
    const auto temporary = std::filesystem::temp_directory_path() / (L"ft-percent-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()));
    CHECK(std::filesystem::create_directory(temporary));
    CHECK(!native_style::validate_percent_assets(temporary.wstring()));
    std::filesystem::create_directory(temporary / "assets");
    // The validator checks only descriptor values and the PNG header, not rendered pixels.
    const std::array<uint8_t, 24> fixture_png{137, 80, 78, 71, 13, 10, 26, 10,
        0, 0, 0, 13, 'I', 'H', 'D', 'R', 0, 0, 0, 20, 0, 0, 0, 18};
    const std::array<uint32_t, 10> fixture_smf{2, 1, 0, 0, 19, 17, UINT32_MAX, UINT32_MAX, UINT32_MAX, UINT32_MAX};
    {
        std::ofstream bitmap(temporary / "assets/percent_indonesia.png", std::ios::binary);
        bitmap.write(reinterpret_cast<const char*>(fixture_png.data()), sizeof(fixture_png));
        CHECK(bitmap.good());
    }
    const auto write_descriptor = [&] {
        std::ofstream descriptor(temporary / "assets/percent_indonesia.smf", std::ios::binary);
        descriptor.write(reinterpret_cast<const char*>(fixture_smf.data()), sizeof(fixture_smf));
        CHECK(descriptor.good());
    };
    write_descriptor();
    CHECK(native_style::validate_percent_assets(temporary.wstring()));
    {
        std::fstream descriptor(temporary / "assets/percent_indonesia.smf", std::ios::binary | std::ios::in | std::ios::out);
        descriptor.seekp(4); descriptor.put(2);
    }
    CHECK(!native_style::validate_percent_assets(temporary.wstring()));
    write_descriptor();
    {
        std::fstream bitmap(temporary / "assets/percent_indonesia.png", std::ios::binary | std::ios::in | std::ios::out);
        bitmap.seekp(23); bitmap.put(19);
    }
    CHECK(!native_style::validate_percent_assets(temporary.wstring()));
    std::filesystem::remove_all(temporary);
    fthook_host host{};
    host.read = read_fixture; host.log = log;
    runtime = &host;
    client_descriptor.id = "kr-812";
    client_descriptor.status = "supported";
    client_descriptor.timestamp = 0x55376C47;
    client_descriptor.image_sizes = {0xF0B000, 0xF0E000};
    client_descriptor.preferred_base = 0x400000;
    client_descriptor.render_call_rva = 0x3DFB55;
    client_descriptor.render_call_bytes.assign(draw_signature.begin(), draw_signature.end());
    client_descriptor.original_draw_rva = 0x6E6CB0;
    client_descriptor.current_health_rva = 0x28200;
    client_descriptor.maximum_entry_rva = 0x90C808;
    client_descriptor.maximum_health_rva = 0x152060;
    client_descriptor.maximum_vtable_slot = 356;
    client_descriptor.hud_anchor_offset = 304;
    client_descriptor.player_capture = numeric_health::PlayerCapture::CallerEdi;
    client_descriptor.hud_register = "ebp";
    client_descriptor.resource_manager_rva = 0xA7F7A4;
    client_descriptor.construct_options_rva = 0x26D70;
    client_descriptor.destroy_options_rva = 0x26D10;
    client_descriptor.load_sprite_rva = 0x6C4D60;
    client_descriptor.measure_number_rva = 0x6E6A40;
    client_descriptor.native_style_slot = 22;
    client_descriptor.native_style_sprite_index = 3079;
    client_descriptor.sprite_frames_offset = 120;
    client_descriptor.sprite_texture_offset = 136;
    client_descriptor.texture_resident_offset = 144;
    client_descriptor.resource_signatures = {
        {0x6C4D60, {0x6A, 0xFF, 0x68, 0xCF, 0x27, 0xBD, 0x00, 0x64}},
        {0x26D70, {0x53, 0x56, 0x8B, 0xF1, 0x33, 0xDB, 0x8D, 0x4E}},
        {0x26D10, {0x56, 0x8B, 0xF1, 0x83, 0x7E, 0x4C, 0x08, 0x72}},
        {0x6E6A40, {0x83, 0xEC, 0x3C, 0xD9, 0x44, 0x24, 0x40, 0x53}},
    };
    client_descriptor.complete = true;
    show_percent = false;
    original_draw = draw;
    // Fastcall's ignored EDX parameter models the x86 thiscall ECX argument.
    current_health = reinterpret_cast<GetHealth>(&get_current);
    const auto maximum = reinterpret_cast<uintptr_t>(&get_maximum);
    image_base = maximum - 0x152060;
    put(fake_hud.data(), 0, reinterpret_cast<uintptr_t>(fake_player.data()));
    put(fake_player.data(), 0, reinterpret_cast<uintptr_t>(fake_table.data()));
    put(fake_table.data(), 356, maximum);
    put(fake_hud.data(), 304, 20.0f); put(fake_hud.data(), 308, 70.0f);
    DrawArguments args{fake_player.data(), 10, 20, 5, 0.25f, 1.75f, L"3"};
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 2 && drawings[0] == L"3" && drawings[1] == L"50");
    CHECK(last_x == 131 && last_y == -31);
    drawings.clear(); forwarded.clear();
    invoke_intercept(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 2 && drawings[0] == L"3" && drawings[1] == L"50");
    CHECK(forwarded.size() == 2);
    CHECK(forwarded[0].numbers == args.numbers && forwarded[0].text == args.text);
    CHECK(forwarded[0].x == args.x && forwarded[0].y == args.y && forwarded[0].style == args.style);
    CHECK(forwarded[0].depth == args.depth && forwarded[0].scale == args.scale);
    CHECK(forwarded[1].numbers == args.numbers && forwarded[1].depth == args.depth && forwarded[1].scale == args.scale);
    CHECK(intercept_address() == reinterpret_cast<uintptr_t>(&draw_intercept));
    client_descriptor.hud_register = "ebx";
    CHECK(intercept_address() == reinterpret_cast<uintptr_t>(&draw_intercept_ebx));
    drawings.clear(); forwarded.clear();
    CHECK(invoke_intercept_ebx(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data())) == 0);
    CHECK(drawings.size() == 2 && drawings[0] == L"3" && drawings[1] == L"50");
    CHECK(forwarded[0].numbers == args.numbers && forwarded[0].text == args.text && forwarded[0].style == args.style);
    CHECK(forwarded[0].x == args.x && forwarded[0].y == args.y && forwarded[0].depth == args.depth && forwarded[0].scale == args.scale);
    put(fake_hud.data(), 0, uintptr_t{1}); drawings.clear();
    CHECK(invoke_intercept_ebx(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data())) == 0);
    CHECK(drawings.size() == 2 && drawings[1] == L"50");
    put(fake_hud.data(), 0, reinterpret_cast<uintptr_t>(fake_player.data()));
    client_descriptor.player_capture = numeric_health::PlayerCapture::HudOwner;
    drawings.clear();
    CHECK(invoke_intercept_ebx(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), uintptr_t{1}) == 0);
    CHECK(drawings.size() == 2 && drawings[1] == L"50");
    CHECK(last_health_player == reinterpret_cast<uintptr_t>(fake_player.data()));
    drawings.clear(); last_health_player = 0;
    CHECK(invoke_intercept_ebx(&args, uintptr_t{1}, reinterpret_cast<uintptr_t>(fake_player.data())) == 0);
    CHECK(drawings.size() == 1 && last_health_player == 0);
    client_descriptor.hud_register = "ebp";
    failure_reported = false;
    // A non-baseline slot and deliberately invalid EDI distinguish the configured owner path.
    client_descriptor.player_capture = numeric_health::PlayerCapture::HudOwner;
    client_descriptor.maximum_vtable_slot = 320;
    put(fake_table.data(), 356, uintptr_t{0});
    put(fake_table.data(), 320, maximum);
    drawings.clear(); last_health_player = 0;
    invoke_intercept(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), uintptr_t{1});
    CHECK(drawings.size() == 2 && drawings[1] == L"50");
    CHECK(last_health_player == reinterpret_cast<uintptr_t>(fake_player.data()));
    put(fake_hud.data(), 0, uintptr_t{0}); drawings.clear(); last_health_player = 0;
    invoke_intercept(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 1 && last_health_player == 0);
    drawings.clear();
    invoke_intercept(&args, uintptr_t{1}, reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 1 && last_health_player == 0);
    put(fake_hud.data(), 0, reinterpret_cast<uintptr_t>(fake_player.data()));
    client_descriptor.player_capture = numeric_health::PlayerCapture::CallerEdi;
    drawings.clear();
    invoke_intercept(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), uintptr_t{1});
    CHECK(drawings.size() == 1 && last_health_player == 0);
    put(fake_table.data(), 320, uintptr_t{0});
    put(fake_table.data(), 356, maximum);
    client_descriptor.maximum_vtable_slot = 356;
    failure_reported = false;
    client_descriptor.health_accessor = numeric_health::HealthAccessor::EdiField;
    client_descriptor.maximum_health_source = numeric_health::MaximumHealthSource::PlayerField;
    client_descriptor.health_field_offset = 8;
    client_descriptor.maximum_health_field_offset = 12;
    current_health = reinterpret_cast<GetHealth>(&get_current_edi);
    put(fake_player.data(), 12, 251.5f);
    test_current = 125.75;
    double measured_current = 0, measured_maximum = 0;
    CHECK(read_health_values(reinterpret_cast<uintptr_t>(fake_player.data()), measured_current, measured_maximum));
    CHECK(measured_current == 125.75 && measured_maximum == 251.5 &&
        last_health_player == reinterpret_cast<uintptr_t>(fake_player.data()) + 8);
    double repeated_current = 0, repeated_maximum = 0;
    for (int i = 0; i < 32; ++i) {
        if (!read_health_values(reinterpret_cast<uintptr_t>(fake_player.data()), measured_current, measured_maximum)) break;
        repeated_current += measured_current;
        repeated_maximum += measured_maximum;
    }
    CHECK(repeated_current == 4024 && repeated_maximum == 8048);
    drawings.clear();
    CHECK(invoke_intercept_ebx(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data())) == 0);
    CHECK(drawings.size() == 2 && drawings[0] == L"3" && drawings[1] == L"50");
    raw_health = true; drawings.clear();
    invoke_intercept(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 2 && drawings[1] == L"125");
    raw_health = false;
    for (float invalid : {0.0f, -1.0f, std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::infinity()}) {
        put(fake_player.data(), 12, invalid); drawings.clear(); last_health_player = 0;
        invoke_intercept(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
        CHECK(drawings.size() == 1 && drawings[0] == L"3" && last_health_player == 0);
    }
    put(fake_player.data(), 12, 251.5f);
    client_descriptor.health_field_offset = 65536;
    drawings.clear(); last_health_player = 0;
    invoke_intercept(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 1 && last_health_player == 0);
    client_descriptor.health_field_offset = 8;
    client_descriptor.maximum_health_field_offset = 65536;
    drawings.clear(); last_health_player = 0;
    invoke_intercept(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 1 && last_health_player == 0);
    client_descriptor.health_accessor = numeric_health::HealthAccessor::Thiscall;
    client_descriptor.maximum_health_source = numeric_health::MaximumHealthSource::Vtable;
    current_health = reinterpret_cast<GetHealth>(&get_current);
    test_current = 125;
    failure_reported = false;
    yellow_font = true; drawings.clear();
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 2 && drawings[1] == L"50");
    CHECK(native_style::reported);
    CHECK(last_x == 131 && last_y == -31);
    yellow_font = false;
    raw_health = true; drawings.clear();
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 2 && drawings[1] == L"125");
    CHECK(last_x == 124 && last_y == -31);
    raw_health = false; test_current = 0; drawings.clear();
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 2 && drawings[1] == L"0");
    CHECK(last_x == 138 && last_y == -31);
    native_placement = false; drawings.clear();
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 2 && drawings[1] == L"0");
    CHECK(last_x == 100 && last_y == 40);
    native_placement = true;
    percent_fixture = true; show_percent = true;
    native_style::percent_path = L"D:/plugin/assets/percent_indonesia.smf";
    native_style::construct_options = reinterpret_cast<native_style::ConstructOptions>(&construct_options);
    native_style::destroy_options = reinterpret_cast<native_style::DestroyOptions>(&destroy_options);
    native_style::load_sprite = reinterpret_cast<native_style::LoadSprite>(&load_percent);
    native_style::measure_number = reinterpret_cast<native_style::MeasureNumber>(&measure_percent);
    put(fake_sprite.data(), 120, reinterpret_cast<uintptr_t>(&fake_entry));
    put(fake_sprite.data(), 124, reinterpret_cast<uintptr_t>(&fake_entry) + sizeof(fake_entry));
    put(fake_sprite.data(), 136, reinterpret_cast<uintptr_t>(fake_texture.data()));
    put(fake_texture.data(), 144, uintptr_t{1});
    fake_entry = reinterpret_cast<uintptr_t>(fake_frame.data());
    fake_frame = {static_cast<uint32_t>(reinterpret_cast<uintptr_t>(fake_sprite.data())), 0, 0, 0, 20, 18, 0, 0};
    CHECK(native_style::load(native_style::percent_path.c_str()) == reinterpret_cast<uintptr_t>(fake_sprite.data()));
    fixture_resource_manager_rva = 0x10000;
    const auto before_resource_switch = sprite_load_calls;
    CHECK(native_style::load(native_style::percent_path.c_str()) == 0);
    CHECK(sprite_load_calls == before_resource_switch);
    client_descriptor.resource_manager_rva = 0x10000;
    CHECK(native_style::load(native_style::percent_path.c_str()) == reinterpret_cast<uintptr_t>(fake_sprite.data()));
    CHECK(sprite_load_calls == before_resource_switch + 1);
    fixture_resource_manager = 0;
    CHECK(native_style::load(native_style::percent_path.c_str()) == 0);
    CHECK(sprite_load_calls == before_resource_switch + 1);
    fixture_resource_manager = 1;
    client_descriptor.resource_manager_rva = 0;
    CHECK(native_style::load(native_style::percent_path.c_str()) == 0);
    CHECK(sprite_load_calls == before_resource_switch + 1);
    fixture_resource_manager_rva = 0xA7F7A4;
    client_descriptor.resource_manager_rva = 0xA7F7A4;
    drawings.clear(); forwarded.clear(); test_current = 125;
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 3 && drawings[1] == L"50" && drawings[2] == L"%");
    CHECK(forwarded[2].x == 164.25f && forwarded[2].y == -31 && forwarded[2].style == 22);
    CHECK(forwarded[2].depth == args.depth && forwarded[2].scale == args.scale);
    CHECK(native_style::percent_ready_reported);
    raw_health = true; drawings.clear();
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 2 && drawings[1] == L"125");
    raw_health = false;
    struct ResourceCase { const char* id; int slot; uint32_t index, frames, texture, resident; };
    const ResourceCase resource_cases[] = {
        {"kr-812", 22, 3079, 120, 136, 144},
        {"cn-279", 21, 2822, 48, 60, 72},
        {"cn-324", 21, 2822, 48, 60, 72},
        {"cn-406", 21, 2822, 120, 136, 144},
        {"cn-408", 22, 3079, 120, 136, 144},
        {"cn-705", 22, 3079, 120, 136, 144},
        {"cn-706", 22, 3079, 120, 136, 144}
    };
    health_fixture = true;
    for (const auto& resource : resource_cases) {
        CHECK(numeric_health::load_client_descriptor(descriptor_text, "client-versions.toml", resource.id, parsed_descriptor, descriptor_error));
        CHECK(parsed_descriptor.native_style_slot == static_cast<uint32_t>(resource.slot) &&
            parsed_descriptor.native_style_sprite_index == resource.index && parsed_descriptor.sprite_frames_offset == resource.frames &&
            parsed_descriptor.sprite_texture_offset == resource.texture && parsed_descriptor.texture_resident_offset == resource.resident);
        client_descriptor.native_style_slot = parsed_descriptor.native_style_slot;
        client_descriptor.native_style_sprite_index = parsed_descriptor.native_style_sprite_index;
        client_descriptor.sprite_frames_offset = parsed_descriptor.sprite_frames_offset;
        client_descriptor.sprite_texture_offset = parsed_descriptor.sprite_texture_offset;
        client_descriptor.texture_resident_offset = parsed_descriptor.texture_resident_offset;
        fixture_style_slot = resource.slot; fixture_sprite_index = resource.index;
        fake_sprite.fill(0); fake_texture.fill(0); fake_health_sprite.fill(0); fake_health_texture.fill(0);
        put(fake_sprite.data(), resource.frames, reinterpret_cast<uintptr_t>(&fake_entry));
        put(fake_sprite.data(), resource.frames + 4, reinterpret_cast<uintptr_t>(&fake_entry) + sizeof(fake_entry));
        put(fake_sprite.data(), resource.texture, reinterpret_cast<uintptr_t>(fake_texture.data()));
        put(fake_texture.data(), resource.resident, uintptr_t{1});
        put(fake_health_sprite.data(), resource.frames, reinterpret_cast<uintptr_t>(fake_health_entries.data()));
        put(fake_health_sprite.data(), resource.frames + 4, reinterpret_cast<uintptr_t>(fake_health_entries.data()) + sizeof(fake_health_entries));
        put(fake_health_sprite.data(), resource.texture, reinterpret_cast<uintptr_t>(fake_health_texture.data()));
        put(fake_health_texture.data(), resource.resident, uintptr_t{1});
        for (size_t digit = 0; digit < fake_health_entries.size(); ++digit) {
            fake_health_entries[digit] = reinterpret_cast<uintptr_t>(fake_health_frames[digit].data());
            fake_health_frames[digit] = {static_cast<uint32_t>(reinterpret_cast<uintptr_t>(fake_health_sprite.data())),
                static_cast<uint32_t>(digit), 0, 0, 8, 18, 0, 0};
        }
        yellow_font = true; drawings.clear(); forwarded.clear();
        draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
        CHECK(drawings.size() == 3 && drawings[0] == L"3" && drawings[1] == L"50" && drawings[2] == L"%");
        CHECK(forwarded[1].style == resource.slot && forwarded[2].style == resource.slot &&
            forwarded[1].numbers == native_style::digits.data() && forwarded[2].numbers == native_style::percent_digits.data());
        put(fake_health_texture.data(), resource.resident, uintptr_t{0});
        CHECK(!native_style::texture_resident(reinterpret_cast<uintptr_t>(fake_health_sprite.data())));
        put(fake_health_texture.data(), resource.resident, uintptr_t{1});
        fake_health_frames[0][0] = 0;
        drawings.clear(); forwarded.clear();
        draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
        CHECK(drawings.size() == 3 && forwarded[1].style == 5 && drawings[1] == L"50");
        fake_health_frames[0][0] = static_cast<uint32_t>(reinterpret_cast<uintptr_t>(fake_health_sprite.data()));
        client_descriptor.native_style_sprite_index = numeric_health::number_table_words;
        CHECK(!native_style::prepare_map(native_style::digits, reinterpret_cast<uintptr_t>(fake_health_sprite.data())));
        client_descriptor.native_style_sprite_index = resource.index;
    }
    health_fixture = false; yellow_font = false;
    fixture_style_slot = 22; fixture_sprite_index = 3079;
    client_descriptor.native_style_slot = 22; client_descriptor.native_style_sprite_index = 3079;
    client_descriptor.sprite_frames_offset = 120; client_descriptor.sprite_texture_offset = 136; client_descriptor.texture_resident_offset = 144;
    fake_sprite.fill(0); fake_texture.fill(0);
    put(fake_sprite.data(), 120, reinterpret_cast<uintptr_t>(&fake_entry));
    put(fake_sprite.data(), 136, reinterpret_cast<uintptr_t>(fake_texture.data()));
    put(fake_texture.data(), 144, uintptr_t{1});
    for (int scenario = 0; scenario < 3; ++scenario) {
        fake_frame[0] = scenario == 0 ? 0u : static_cast<uint32_t>(reinterpret_cast<uintptr_t>(fake_sprite.data()));
        fake_frame[4] = scenario == 1 ? 0u : 20u;
        put(fake_sprite.data(), 124, reinterpret_cast<uintptr_t>(&fake_entry) + (scenario == 2 ? 2 : 1) * sizeof(fake_entry));
        drawings.clear();
        draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
        CHECK(drawings.size() == 2 && drawings[1] == L"50");
    }
    percent_fixture = false; show_percent = false;
    reports = 0; test_maximum = 0; drawings.clear();
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 1 && reports == 1);
    test_maximum = std::numeric_limits<double>::quiet_NaN();
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(reports == 1);
    test_maximum = 250;
    put(fake_table.data(), 356, uintptr_t{0}); drawings.clear();
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), reinterpret_cast<uintptr_t>(fake_player.data()));
    CHECK(drawings.size() == 1);
    put(fake_table.data(), 356, maximum);
    drawings.clear();
    draw_health(&args, reinterpret_cast<uintptr_t>(fake_hud.data()), 0);
    CHECK(drawings.size() == 1);
    image_fixture = true;
    uintptr_t located = 0;
    fthook_image packed{0x400000, 0x55376C47, 0xF0B000, nullptr, 0};
    CHECK(locate(nullptr, &packed, &located) == FTHOOK_OK && located == 0xAE6CB0);
    yellow_font = true;
    CHECK(locate(nullptr, &packed, &located) == FTHOOK_NOT_FOUND);
    yellow_font = false;
    auto unpacked = packed; unpacked.size = 0xF0E000;
    CHECK(locate(nullptr, &unpacked, &located) == FTHOOK_OK);
    fixture_maximum = 0;
    CHECK(locate(nullptr, &packed, &located) == FTHOOK_NOT_FOUND);
    client_descriptor.maximum_health_source = numeric_health::MaximumHealthSource::PlayerField;
    CHECK(locate(nullptr, &packed, &located) == FTHOOK_OK);
    client_descriptor.maximum_health_source = numeric_health::MaximumHealthSource::Vtable;
    fixture_maximum = 0x552060;
    CHECK(locate(nullptr, &packed, &located) == FTHOOK_OK);
    auto unknown = packed; unknown.size = 0xF0C000;
    CHECK(locate(nullptr, &unknown, &located) == FTHOOK_INVALID);
    unknown = packed; unknown.timestamp++;
    CHECK(locate(nullptr, &unknown, &located) == FTHOOK_INVALID);
    draw_signature[0] = 0;
    CHECK(locate(nullptr, &packed, &located) == FTHOOK_NOT_FOUND);
    timing = {}; trace_timing = false;
    CHECK(!sample_timing(100) && timing.calls == 0 && !timing.started);
    trace_timing = true; timing.loaded_at = 100;
    CHECK(sample_timing(30000) && timing.first_at == 30000 && timing.calls == 1);
    CHECK(!sample_timing(30999) && timing.calls == 2);
    CHECK(sample_timing(31000) && timing.checkpoint == 2);
    CHECK(!sample_timing(31001));
    CHECK(sample_timing(90000) && timing.checkpoint == 6);
    CHECK(!sample_timing(90001));
    CHECK(sample_timing(150000) && timing.checkpoint == 7);
    const auto capped_calls = timing.calls;
    CHECK(!sample_timing(999999) && timing.calls == capped_calls);
    trace_timing = false;
    return finish("numeric health synthetic rendering");
}

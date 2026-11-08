#pragma once

#include <vector>

// cn-279 sub_53AA50 and kr-812 sub_AE6A40 select different private font slots.
// Separate tables leave the game's renderer and its existing slots untouched.
namespace native_style {
using ConstructOptions = void*(__thiscall*)(void*);
using DestroyOptions = void(__thiscall*)(void*);
using LoadSprite = uintptr_t(__thiscall*)(void*, const void*, void*);
using MeasureNumber = void(__thiscall*)(void*, float, float, int, const wchar_t*, float, float, float, int, float*);
ConstructOptions construct_options = nullptr;
DestroyOptions destroy_options = nullptr;
LoadSprite load_sprite = nullptr;
MeasureNumber measure_number = nullptr;
struct BorrowedString {
    const wchar_t* data;
    uint32_t unused[3]{};
    uint32_t size;
    uint32_t capacity;
};
static_assert(sizeof(BorrowedString) == 24);
using NumberTable = std::array<uint32_t, numeric_health::number_table_words>;
NumberTable digits{};
NumberTable percent_digits{};
std::wstring percent_path;
bool reported = false;
bool pending_reported = false;
bool ready_reported = false;
bool percent_reported = false;
bool percent_pending_reported = false;
bool percent_ready_reported = false;
bool percent_probe_reported = false;
bool percent_draw_reported = false;

bool read_member(uintptr_t object, uint32_t offset, void* output, uint32_t size) {
    return object && offset && object + offset >= object && read(object + offset, output, size);
}

bool sprite_texture(uintptr_t sprite, uintptr_t& texture) {
    return read_member(sprite, client_descriptor.sprite_texture_offset, &texture, sizeof(texture)) && texture;
}

bool texture_resident(uintptr_t sprite) {
    uintptr_t texture = 0, resident = 0;
    return sprite_texture(sprite, texture) &&
        read_member(texture, client_descriptor.texture_resident_offset, &resident, sizeof(resident)) && resident;
}

bool prepare_map(NumberTable& table, uintptr_t sprite) {
    const auto slot = client_descriptor.native_style_slot;
    const auto index = client_descriptor.native_style_sprite_index;
    const auto map_begin = slot * 128 + 2;
    if (slot < 18 || slot > 30 || index >= table.size() || index < map_begin + 128 || !sprite) return false;
    // All measured number renderers use 128 character entries starting at word 2.
    std::fill(table.begin() + map_begin, table.begin() + map_begin + 128, UINT32_MAX);
    table[index] = static_cast<uint32_t>(sprite);
    return true;
}

bool validate_percent_assets(const std::wstring& folder) {
    const auto read_asset = [](const std::wstring& path, void* data, DWORD bytes, LONGLONG exact, LONGLONG limit) {
        const DWORD attributes = GetFileAttributesW(path.c_str());
        if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT))) return false;
        const HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return false;
        LARGE_INTEGER size{};
        DWORD done = 0;
        const bool ok = GetFileSizeEx(file, &size) && size.QuadPart >= bytes && size.QuadPart <= limit &&
            (exact < 0 || size.QuadPart == exact) && ReadFile(file, data, bytes, &done, nullptr) && done == bytes;
        CloseHandle(file);
        return ok;
    };
    std::array<uint32_t, 10> descriptor{};
    // kr-812 sub_AF2E40 reads version/count and an inclusive rectangle followed by four optional offsets.
    constexpr std::array<uint32_t, 10> expected{2, 1, 0, 0, 19, 17, UINT32_MAX, UINT32_MAX, UINT32_MAX, UINT32_MAX};
    if (!read_asset(folder + L"/assets/percent_indonesia.smf", descriptor.data(), sizeof(descriptor), sizeof(descriptor), sizeof(descriptor)) ||
        descriptor != expected) return false;
    std::array<uint8_t, 24> header{};
    constexpr std::array<uint8_t, 24> png_header{137, 80, 78, 71, 13, 10, 26, 10,
        0, 0, 0, 13, 'I', 'H', 'D', 'R', 0, 0, 0, 20, 0, 0, 0, 18};
    return read_asset(folder + L"/assets/percent_indonesia.png", header.data(), sizeof(header), -1, 65536) && header == png_header;
}

uintptr_t load(const wchar_t* path) {
    if (!construct_options || !destroy_options || !load_sprite) return 0;
    uintptr_t manager = 0;
    if (!client_descriptor.resource_manager_rva ||
        !read(image_base + client_descriptor.resource_manager_rva, &manager, sizeof(manager)) || !manager) return 0;
    const auto length = static_cast<uint32_t>(wcslen(path));
    BorrowedString name{path, {}, length,
        std::max(length, client_descriptor.resource_string_inline_capacity)};
    std::vector<uint8_t> options(client_descriptor.resource_options_size);
    construct_options(options.data());
    const auto result = load_sprite(
        reinterpret_cast<void*>(manager), &name, options.data());
    destroy_options(options.data());
    return result;
}

bool prepare() {
    const auto health = load(L"Images/GameStage/myHP/myHPtext00.smf");
    if (!health) return false;
    uintptr_t texture = 0;
    if (!sprite_texture(health, texture)) return false;
    uintptr_t frames[2]{};
    if (!read_member(health, client_descriptor.sprite_frames_offset, frames, sizeof(frames)) || !frames[0] || frames[1] < frames[0] ||
        frames[1] - frames[0] != 10 * sizeof(uintptr_t)) return false;
    std::array<uintptr_t, 10> entries{};
    if (!read(frames[0], entries.data(), sizeof(entries))) return false;
    for (const auto entry : entries) {
        std::array<uint32_t, 8> frame{};
        if (!entry || !read(entry, frame.data(), sizeof(frame)) || frame[0] != health ||
            frame[4] <= frame[2] || frame[5] <= frame[3]) return false;
    }
    if (!prepare_map(digits, health)) return false;
    for (uint32_t i = 0; i < 10; ++i) digits[client_descriptor.native_style_slot * 128 + 2 + L'0' + i] = i;
    return true;
}

bool prepare_percent() {
    if (percent_path.empty()) return false;
    // kr-812 sub_AC4D60 derives the PNG filename from the SMF; sub_A4B340 also probes loose files.
    // Reacquire cached resources on every draw, as for the native digits.
    const auto sprite = load(percent_path.c_str());
    if (!sprite) {
        if (!percent_reported) {
            percent_reported = true;
            runtime->log(runtime->context, "numeric health: percent sprite unavailable; symbol suppressed");
        }
        return false;
    }
    uintptr_t texture = 0;
    const bool texture_read = sprite_texture(sprite, texture);
    uintptr_t frames[2]{};
    const bool frames_read = read_member(sprite, client_descriptor.sprite_frames_offset, frames, sizeof(frames));
    if (!percent_probe_reported) {
        percent_probe_reported = true;
        char message[192]{};
        uintptr_t table = 0;
        read(sprite, &table, sizeof(table));
        sprintf_s(message, "numeric health: percent resource sprite=%08X vtable=%08X texture=%08X frames=(%08X,%08X) read=%d/%d",
            static_cast<uint32_t>(sprite), static_cast<uint32_t>(table), texture,
            static_cast<uint32_t>(frames[0]), static_cast<uint32_t>(frames[1]), texture_read ? 1 : 0, frames_read ? 1 : 0);
        runtime->log(runtime->context, message);
    }
    if (!texture_read || !texture || !frames_read || !frames[0] || frames[1] < frames[0] ||
        frames[1] - frames[0] != sizeof(uintptr_t)) return false;
    uintptr_t entry = 0;
    if (!read(frames[0], &entry, sizeof(entry)) || !entry) return false;
    std::array<uint32_t, 8> frame{};
    if (!read(entry, frame.data(), sizeof(frame)) || frame[0] != sprite ||
        frame[2] != 0 || frame[3] != 0 || frame[4] != 20 || frame[5] != 18) return false;
    if (!prepare_map(percent_digits, sprite)) return false;
    percent_digits[client_descriptor.native_style_slot * 128 + 2 + L'%'] = 0;
    return true;
}

bool draw(const float* anchor, const wchar_t* text) {
    if (!prepare()) {
        if (!reported) { reported = true; runtime->log(runtime->context, "numeric health: original health sprites unavailable"); }
        return false;
    }
    // kr-812 AE6A40 renders the ten-frame native health font through a private slot.
    const float x = anchor[0];
    const float y = anchor[1];
    original_draw(digits.data(), x, y, static_cast<int>(client_descriptor.native_style_slot), 0.9f, 0.9f, text);
    // kr-812 ABDA20 binds the native texture at +144 after its lazy-load call.
    // Inspect after drawing so the load still runs; keep the white font while pending.
    if (!texture_resident(digits[client_descriptor.native_style_sprite_index])) {
        if (!pending_reported) {
            pending_reported = true;
            runtime->log(runtime->context, "numeric health: yellow texture pending; using native white digits");
        }
        return false;
    }
    if (!ready_reported) {
        ready_reported = true;
        runtime->log(runtime->context, "numeric health: yellow texture resident");
    }
    return true;
}

bool draw_percent(void* numbers, int style, const wchar_t* text, const float* anchor, int digit_count, float depth, float scale) {
    if (!prepare_percent()) {
        if (!percent_pending_reported) {
            percent_pending_reported = true;
            runtime->log(runtime->context, "numeric health: percent sprite validation pending; symbol suppressed");
        }
        return false;
    }
    if (!measure_number) return false;
    float size[2]{};
    // kr-812 sub_AE6A40's 0x400 flag measures without drawing; its cursor advances (width - 1) per character.
    measure_number(numbers, anchor[0], anchor[1], style, text, depth, scale, scale, 0x400, size);
    if (!std::isfinite(size[0]) || !std::isfinite(size[1]) || size[0] <= 0 || size[0] > 2048 || size[1] <= 0 || size[1] > 2048)
        return false;
    const float x = anchor[0] + size[0] * 0.5f - scale * static_cast<float>(digit_count - 1) + 12.0f * scale;
    if (!percent_draw_reported) {
        percent_draw_reported = true;
        char message[224]{};
        sprintf_s(message, "numeric health: percent draw position=(%.6g,%.6g) number_size=(%.6g,%.6g) symbol_size=(%.6g,%.6g) opacity=%.6g scale=%.6g",
            x, anchor[1], size[0], size[1], 20.0f * scale, 18.0f * scale, depth, scale);
        runtime->log(runtime->context, message);
    }
    // kr-812 sub_AE6CB0 formats its last argument, so a literal percent must be escaped.
    original_draw(percent_digits.data(), x, anchor[1],
        static_cast<int>(client_descriptor.native_style_slot), depth, scale, L"%%");
    if (!texture_resident(percent_digits[client_descriptor.native_style_sprite_index])) return false;
    if (!percent_ready_reported) {
        percent_ready_reported = true;
        runtime->log(runtime->context, "numeric health: percent texture resident");
    }
    return true;
}
}

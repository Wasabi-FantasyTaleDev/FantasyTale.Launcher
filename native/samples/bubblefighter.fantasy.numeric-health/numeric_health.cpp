#include <fthook/plugin_abi.h>
#include <windows.h>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <array>
#include <algorithm>
#include <string>
#include "client_descriptors.h"

namespace {
const fthook_host* runtime = nullptr;
uintptr_t image_base = 0;
numeric_health::ClientDescriptor client_descriptor{};
using DrawNumber = void(__cdecl*)(void*, float, float, int, float, float, const wchar_t*);
using GetHealth = double(__thiscall*)(void*);
DrawNumber original_draw = nullptr;
GetHealth current_health = nullptr;
int offset_x = 80, offset_y = -30;
bool native_placement = true;
bool show_percent = true;

std::array<float, 2> health_position(const float* anchor, int digit_count) {
    // id-1809 sub_789AB0 right-aligns health digits above the gauge's life anchor.
    return native_placement ? std::array<float, 2>{anchor[0] + 125.0f - 7.0f * static_cast<float>(digit_count), anchor[1] - 101.0f}
        : std::array<float, 2>{anchor[0] + static_cast<float>(offset_x), anchor[1] + static_cast<float>(offset_y)};
}
bool raw_health = false;
bool failure_reported = false;
bool locator_failure_reported = false;
bool draw_reported = false;
bool trace_timing = false;
bool yellow_font = false;
using ReadClock = ULONGLONG(WINAPI*)();
ReadClock read_clock = GetTickCount64;
struct TimingTrace {
    ULONGLONG loaded_at = 0, first_at = 0;
    uint64_t calls = 0;
    size_t checkpoint = 0;
    bool started = false;
};
TimingTrace timing{};
constexpr ULONGLONG trace_checkpoints[]{0, 1000, 5000, 15000, 30000, 60000, 120000};

bool sample_timing(ULONGLONG now) {
    if (!trace_timing || timing.checkpoint == _countof(trace_checkpoints)) return false;
    ++timing.calls;
    if (!timing.started) { timing.started = true; timing.first_at = now; }
    if (now - timing.first_at < trace_checkpoints[timing.checkpoint]) return false;
    // Emit one current snapshot after a stall, never a burst of obsolete checkpoints.
    do { ++timing.checkpoint; }
    while (timing.checkpoint < _countof(trace_checkpoints) &&
        now - timing.first_at >= trace_checkpoints[timing.checkpoint]);
    return true;
}

void report_timing(ULONGLONG now, bool valid, const float* anchor,
    float opacity, float scale, double current, double maximum) {
    char message[256]{};
    sprintf_s(message,
        "numeric health: timing loaded_ms=%llu draw_ms=%llu calls=%llu valid=%d position=(%.6g,%.6g) opacity=%.6g scale=%.6g hp=%.6g max=%.6g",
        now - timing.loaded_at, now - timing.first_at, timing.calls, valid ? 1 : 0,
        anchor[0], anchor[1],
        opacity, scale, current, maximum);
    runtime->log(runtime->context, message);
}

bool read(uintptr_t address, void* target, uint32_t size) {
    return runtime && runtime->read(address, target, size) == FTHOOK_OK;
}

void report_failure() {
    if (!failure_reported) {
        failure_reported = true;
        runtime->log(runtime->context, "numeric health: HUD/player validation failed; additional drawing suppressed");
    }
}

#include "native_style.h"

struct DrawArguments {
    void* numbers;
    float x, y;
    int style;
    float depth, scale;
    const wchar_t* text;
};
static_assert(sizeof(DrawArguments) == 28);

// cn-279 sub_4063E0 consumes its field in EDI and returns HP through ST(0).
__declspec(naked) double __cdecl call_edi_health(uintptr_t, uintptr_t) {
    __asm {
        push edi
        mov eax, [esp + 8]
        mov edi, [esp + 12]
        call eax
        pop edi
        ret
    }
}

bool read_health_values(uintptr_t player, double& current, double& maximum) {
    uintptr_t table = 0, maximum_address = 0;
    if (!player || !current_health || !read(player, &table, sizeof(table))) return false;
    if (client_descriptor.maximum_health_source == numeric_health::MaximumHealthSource::PlayerField) {
        const auto offset = client_descriptor.maximum_health_field_offset;
        float field = 0;
        if (!offset || player + offset < player || !read(player + offset, &field, sizeof(field))) return false;
        maximum = field;
        if (!std::isfinite(maximum) || maximum <= 0) return false;
    } else if (!read(table + client_descriptor.maximum_vtable_slot, &maximum_address, sizeof(maximum_address)) ||
        maximum_address != image_base + client_descriptor.maximum_health_rva) return false;
    if (client_descriptor.health_accessor == numeric_health::HealthAccessor::EdiField) {
        const auto offset = client_descriptor.health_field_offset;
        uint8_t probe = 0;
        if (!offset || player + offset < player || !read(player + offset, &probe, sizeof(probe))) return false;
        current = call_edi_health(reinterpret_cast<uintptr_t>(current_health), player + offset);
    } else current = current_health(reinterpret_cast<void*>(player));
    if (client_descriptor.maximum_health_source == numeric_health::MaximumHealthSource::Vtable)
        maximum = reinterpret_cast<GetHealth>(maximum_address)(reinterpret_cast<void*>(player));
    return std::isfinite(current) && std::isfinite(maximum) && maximum > 0 && current >= 0 && current <= 1000000;
}

// kr-812 7DFB55 has exactly seven stack slots and caller cleanup of 28 bytes.
void __cdecl draw_health(const DrawArguments* arguments, uintptr_t hud, uintptr_t player) {
    original_draw(arguments->numbers, arguments->x, arguments->y, arguments->style,
        arguments->depth, arguments->scale, arguments->text);

    const auto now = trace_timing ? read_clock() : 0;
    const bool trace = sample_timing(now);
    float anchor[2]{};
    // Owner capture uses the HUD's first pointer; EDI is only valid for caller_edi.
    if (client_descriptor.player_capture == numeric_health::PlayerCapture::HudOwner &&
        !read(hud, &player, sizeof(player))) {
        if (trace) report_timing(now, false, anchor, arguments->depth, arguments->scale, 0, 0);
        report_failure();
        return;
    }
    double current = 0, maximum = 0;
    if (!read(hud + client_descriptor.hud_anchor_offset, anchor, sizeof(anchor)) ||
        !std::isfinite(anchor[0]) || !std::isfinite(anchor[1]) || !read_health_values(player, current, maximum)) {
        if (trace) report_timing(now, false, anchor, arguments->depth, arguments->scale, 0, 0);
        report_failure();
        return;
    }
    const double value = raw_health ? current : current / maximum * 100.0;
    if (!std::isfinite(value) || value < 0 || value > 1000000) {
        if (trace) report_timing(now, false, anchor, arguments->depth, arguments->scale, 0, 0);
        report_failure();
        return;
    }
    wchar_t text[16]{};
    const int formatted = swprintf_s(text, L"%d", static_cast<int>(value));
    if (formatted <= 0) return;
    const auto position = health_position(anchor, formatted);
    if (trace) report_timing(now, true, position.data(), arguments->depth, arguments->scale, current, maximum);
    if (!draw_reported) {
        draw_reported = true;
        char message[160]{};
        sprintf_s(message, "numeric health: native white draw at (%.1f,%.1f), opacity=%.3f scale=%.3f",
            position[0], position[1],
            arguments->depth, arguments->scale);
        runtime->log(runtime->context, message);
    }
    const bool native_digits = yellow_font && native_style::draw(position.data(), text);
    if (!native_digits)
        original_draw(arguments->numbers, position[0], position[1], 5, arguments->depth, arguments->scale, text);
    if (show_percent && !raw_health)
        native_style::draw_percent(native_digits ? native_style::digits.data() : arguments->numbers,
            native_digits ? static_cast<int>(client_descriptor.native_style_slot) : 5,
            text, position.data(), formatted, native_digits ? 0.9f : arguments->depth, native_digits ? 0.9f : arguments->scale);
}

// kr-812 7DFADF restores the selected player into EDI before 7DFB55; EBP is HUD.
// Forward the untouched seven original argument slots; caller cleanup and callee-saved registers remain.
__declspec(naked) void draw_intercept() {
    __asm {
        push ebp
        mov ebp, esp
        push edi
        push dword ptr [ebp]
        lea eax, [ebp + 8]
        push eax
        call draw_health
        add esp, 12
        pop ebp
        ret
    }
}

// cn-279 7175D0 retains its HUD in EBX; the seven stack slots are unchanged.
__declspec(naked) void draw_intercept_ebx() {
    __asm {
        push ebp
        mov ebp, esp
        push edi
        push ebx
        lea eax, [ebp + 8]
        push eax
        call draw_health
        add esp, 12
        pop ebp
        ret
    }
}

uintptr_t intercept_address() {
    return client_descriptor.hud_register == "ebx" ? reinterpret_cast<uintptr_t>(&draw_intercept_ebx)
        : reinterpret_cast<uintptr_t>(&draw_intercept);
}

int32_t FTHOOK_CALL cleanup(void*) {
    runtime = nullptr;
    image_base = 0;
    original_draw = nullptr;
    current_health = nullptr;
    client_descriptor = {};
    return FTHOOK_OK;
}

int32_t FTHOOK_CALL locate(void*, const fthook_image* image, uintptr_t* address) {
    if (!image || !address || !client_descriptor.complete || image->timestamp != client_descriptor.timestamp ||
        std::find(client_descriptor.image_sizes.begin(), client_descriptor.image_sizes.end(), image->size) == client_descriptor.image_sizes.end())
        return FTHOOK_INVALID;
    uint8_t bytes[8]{};
    if (client_descriptor.render_call_bytes.size() > sizeof(bytes) ||
        !read(image->base + client_descriptor.render_call_rva, bytes, static_cast<uint32_t>(client_descriptor.render_call_bytes.size())) ||
        std::memcmp(bytes, client_descriptor.render_call_bytes.data(), client_descriptor.render_call_bytes.size()) != 0) return FTHOOK_NOT_FOUND;
    if (yellow_font || show_percent) {
        for (const auto& signature : client_descriptor.resource_signatures)
            if (!read(image->base + signature.rva, bytes, static_cast<uint32_t>(signature.bytes.size())) ||
                std::memcmp(bytes, signature.bytes.data(), signature.bytes.size()) != 0) return FTHOOK_NOT_FOUND;
    }
    uint32_t maximum = 0;
    if (client_descriptor.maximum_health_source == numeric_health::MaximumHealthSource::Vtable &&
        (!read(image->base + client_descriptor.maximum_entry_rva, &maximum, sizeof(maximum)) ||
         maximum != image->base + client_descriptor.maximum_health_rva)) {
        if (!locator_failure_reported) {
            locator_failure_reported = true;
            runtime->log(runtime->context, "numeric health: maximum-health entry not ready; waiting for unpacking");
        }
        // kr-812 code can unpack before the maximum-health vtable is populated.
        return FTHOOK_NOT_FOUND;
    }
    image_base = image->base;
    original_draw = reinterpret_cast<DrawNumber>(image->base + client_descriptor.original_draw_rva);
    current_health = reinterpret_cast<GetHealth>(image->base + client_descriptor.current_health_rva);
    *address = image->base + client_descriptor.original_draw_rva;
    return FTHOOK_OK;
}

bool read_descriptor_file(const char* directory, std::string& text) {
    if (!directory || strnlen_s(directory, 2049) > 2048) return false;
    const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, directory, -1, nullptr, 0);
    if (count <= 1) return false;
    std::wstring folder(static_cast<size_t>(count), L'\0');
    if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, directory, -1, folder.data(), count)) return false;
    folder.resize(static_cast<size_t>(count - 1));
    const std::wstring path = folder + L"\\client-versions.toml";
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER size{};
    bool ok = GetFileSizeEx(file, &size) && size.QuadPart >= 0 && size.QuadPart <= 256 * 1024;
    if (ok) {
        text.resize(static_cast<size_t>(size.QuadPart));
        DWORD read_count = 0;
        ok = text.empty() || (ReadFile(file, text.data(), static_cast<DWORD>(text.size()), &read_count, nullptr) && read_count == text.size());
    }
    CloseHandle(file);
    return ok;
}
}

extern "C" int32_t FTHOOK_CALL fthook_plugin_main(const fthook_host* host, fthook_plugin* plugin) {
    if (!host || !plugin || host->abi_version != FTHOOK_ABI_VERSION || host->struct_size < offsetof(fthook_host, overlay))
        return FTHOOK_ABI_UNSUPPORTED;
    if (!host->build) return FTHOOK_INVALID;
    runtime = host;
    plugin->abi_version = FTHOOK_ABI_VERSION;
    plugin->struct_size = sizeof(fthook_plugin);
    plugin->cleanup = cleanup;
    plugin->context = nullptr;
    fthook_option option{};
    if (host->option(host->context, "placement", &option) != FTHOOK_OK ||
        option.kind != FTHOOK_OPTION_STRING || !option.text) return FTHOOK_INVALID;
    if (std::strcmp(option.text, "native") != 0 && std::strcmp(option.text, "custom") != 0) return FTHOOK_INVALID;
    native_placement = std::strcmp(option.text, "native") == 0;
    if (host->option(host->context, "offset_x", &option) != FTHOOK_OK || option.kind != FTHOOK_OPTION_INT ||
        option.integer < -500 || option.integer > 500) return FTHOOK_INVALID;
    offset_x = static_cast<int>(option.integer);
    if (host->option(host->context, "offset_y", &option) != FTHOOK_OK || option.kind != FTHOOK_OPTION_INT ||
        option.integer < -500 || option.integer > 500) return FTHOOK_INVALID;
    offset_y = static_cast<int>(option.integer);
    if (host->option(host->context, "raw_health", &option) != FTHOOK_OK || option.kind != FTHOOK_OPTION_BOOL)
        return FTHOOK_INVALID;
    raw_health = option.integer != 0;
    if (host->option(host->context, "show_percent", &option) != FTHOOK_OK || option.kind != FTHOOK_OPTION_BOOL)
        return FTHOOK_INVALID;
    show_percent = option.integer != 0;
    if (host->option(host->context, "trace_timing", &option) != FTHOOK_OK || option.kind != FTHOOK_OPTION_BOOL)
        return FTHOOK_INVALID;
    trace_timing = option.integer != 0;
    if (host->option(host->context, "font_color", &option) != FTHOOK_OK ||
        option.kind != FTHOOK_OPTION_STRING || !option.text) return FTHOOK_INVALID;
    if (std::strcmp(option.text, "white") != 0 && std::strcmp(option.text, "yellow") != 0) return FTHOOK_INVALID;
    yellow_font = std::strcmp(option.text, "yellow") == 0;
    native_style::digits = {};
    native_style::percent_digits = {};
    native_style::reported = native_style::pending_reported = native_style::ready_reported = false;
    native_style::percent_reported = native_style::percent_pending_reported = native_style::percent_ready_reported = false;
    native_style::percent_probe_reported = false;
    native_style::percent_draw_reported = false;
    native_style::percent_path.clear();
    if (show_percent) {
        if (!host->plugin_directory || strnlen_s(host->plugin_directory, 2049) > 2048) return FTHOOK_INVALID;
        const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, host->plugin_directory, -1, nullptr, 0);
        if (count <= 1) return FTHOOK_INVALID;
        std::wstring folder(static_cast<size_t>(count), L'\0');
        if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, host->plugin_directory, -1, folder.data(), count)) return FTHOOK_INVALID;
        folder.resize(static_cast<size_t>(count - 1));
        if (!native_style::validate_percent_assets(folder)) {
            runtime->log(runtime->context, "numeric health: missing or invalid percent assets; activation rejected");
            return FTHOOK_INVALID;
        }
        native_style::percent_path = folder + L"/assets/percent_indonesia.smf";
    }
    timing = {};
    timing.loaded_at = read_clock();
    std::string descriptor_text, descriptor_error;
    if (!read_descriptor_file(host->plugin_directory, descriptor_text) ||
        !numeric_health::load_client_descriptor(descriptor_text, "client-versions.toml", host->build, client_descriptor, descriptor_error)) {
        runtime->log(runtime->context, descriptor_error.empty() ? "numeric health: client-versions.toml is missing" : descriptor_error.c_str());
        return FTHOOK_INVALID;
    }
    image_base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    if (!client_descriptor.complete || client_descriptor.preferred_base != image_base) {
        runtime->log(runtime->context, "numeric health: client descriptor is research-only or has an incompatible image base");
        return FTHOOK_INVALID;
    }
    native_style::construct_options = reinterpret_cast<native_style::ConstructOptions>(image_base + client_descriptor.construct_options_rva);
    native_style::destroy_options = reinterpret_cast<native_style::DestroyOptions>(image_base + client_descriptor.destroy_options_rva);
    native_style::load_sprite = reinterpret_cast<native_style::LoadSprite>(image_base + client_descriptor.load_sprite_rva);
    native_style::measure_number = reinterpret_cast<native_style::MeasureNumber>(image_base + client_descriptor.measure_number_rva);
    const int32_t registered = host->register_locator(host->context,
        "bubblefighter.fantasy.numeric-health.render", locate, nullptr);
    if (registered != FTHOOK_OK) return registered;
    if (client_descriptor.render_call_bytes.size() < 5) return FTHOOK_INVALID;
    const uint8_t expected[5]{client_descriptor.render_call_bytes[0], client_descriptor.render_call_bytes[1],
        client_descriptor.render_call_bytes[2], client_descriptor.render_call_bytes[3], client_descriptor.render_call_bytes[4]};
    const uint8_t mask[5]{255, 255, 255, 255, 255};
    uint8_t replacement[5]{0xE8};
    const uint32_t relative = static_cast<uint32_t>(intercept_address()
        - (image_base + client_descriptor.render_call_rva + 5));
    std::memcpy(replacement + 1, &relative, sizeof(relative));
    return host->stage_patch(host->context, image_base + client_descriptor.render_call_rva,
        expected, mask, replacement, sizeof(replacement));
}

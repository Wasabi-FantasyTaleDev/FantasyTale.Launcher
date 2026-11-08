#include "resolution_model.h"
#include <fthook/plugin_abi.h>
#include <windows.h>
#include <d3d9.h>
#include <algorithm>
#include <array>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <memory>
#include <sstream>
#include <stdexcept>

namespace {
using namespace resolution_extension;
struct State {
    const fthook_host* host;
    uintptr_t base;
    Descriptor descriptor;
    std::vector<Mode> modes;
    std::vector<Mode> custom;
    std::vector<Write> writes;
};
State* stage_state = nullptr;
State* menu_state = nullptr;

template<class T> bool read(const State& state, uintptr_t address, T& value) {
    return address <= UINT32_MAX - sizeof(T)
        && state.host->read(address, &value, static_cast<uint32_t>(sizeof(T))) == FTHOOK_OK;
}

void resize_main_stage(const State& state, void* stage, Mode logical) {
    const auto& layout = *state.descriptor.main_stage;
    uint32_t screen = 0;
    Mode physical, current;
    if (!read(state, state.base + layout.screen_pointer_rva, screen) || !screen
        || screen > UINT32_MAX - layout.screen_size_offset - sizeof(Mode)
        || reinterpret_cast<uintptr_t>(stage) > UINT32_MAX - layout.control_size_offset - sizeof(Mode)
        || !read(state, static_cast<uintptr_t>(screen) + layout.screen_size_offset, physical)
        || !read(state, reinterpret_cast<uintptr_t>(stage) + layout.control_size_offset, current)
        || physical.width == 0 || physical.height == 0 || physical.width > INT32_MAX || physical.height > INT32_MAX
        || logical.width == 0 || logical.height == 0 || logical.width > INT32_MAX || logical.height > INT32_MAX) {
        state.host->log(state.host->context, "resolution extension: main-stage dimensions are unavailable; native layout retained");
        return;
    }
    if (std::find(state.descriptor.original_modes.begin(), state.descriptor.original_modes.end(), physical)
        != state.descriptor.original_modes.end()) return;
    if (current != layout.original_size) {
        state.host->log(state.host->context, "resolution extension: main-stage canvas differs from the audited layout; native layout retained");
        return;
    }
    const auto target = stage_size(state.descriptor, physical, logical, current);
    // cn-406 sub_96BAC0 uses these setters for a screen-sized root; cn-406 sub_964FA0 keeps native layout callbacks.
    using Setter = int(__thiscall*)(void*, int);
    if (target.width != current.width)
        reinterpret_cast<Setter>(state.base + layout.set_width.rva)(stage, static_cast<int>(target.width));
    if (target.height != current.height)
        reinterpret_cast<Setter>(state.base + layout.set_height.rva)(stage, static_cast<int>(target.height));
    if (target != current) {
        char message[128]{};
        std::snprintf(message, sizeof(message), "resolution extension: main-stage canvas expanded to %u x %u", target.width, target.height);
        state.host->log(state.host->context, message);
    }
}

int __fastcall load_main_stage(void* stage, void*, int* resource) {
    // cn-406 sub_578B90 calls cn-406 sub_96C170 on two exclusive construction branches; both keep thiscall/ret 4.
    const auto& state = *stage_state;
    Mode logical;
    // cn-406 sub_9748A0 already divided the physical window size by its native UI scale before resource loading.
    if (reinterpret_cast<uintptr_t>(stage) <= UINT32_MAX - state.descriptor.main_stage->control_size_offset - sizeof(Mode))
        read(state, reinterpret_cast<uintptr_t>(stage) + state.descriptor.main_stage->control_size_offset, logical);
    using Loader = int(__thiscall*)(void*, int*);
    const auto result = reinterpret_cast<Loader>(state.base + state.descriptor.main_stage->loader.rva)(stage, resource);
    resize_main_stage(state, stage, logical);
    return result;
}
Mode active_monitor_size() {
    const auto window = GetActiveWindow();
    if (!window) return {};
    const auto monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONULL);
    MONITORINFO info{};
    info.cbSize = sizeof(info);
    if (!monitor || !GetMonitorInfoW(monitor, &info)) return {};
    const auto width = static_cast<int64_t>(info.rcMonitor.right) - info.rcMonitor.left;
    const auto height = static_cast<int64_t>(info.rcMonitor.bottom) - info.rcMonitor.top;
    if (width <= 0 || height <= 0 || width > INT32_MAX || height > INT32_MAX) return {};
    return {static_cast<uint32_t>(width), static_cast<uint32_t>(height)};
}
uint8_t __fastcall filter_menu_mode(void* graphics, void*, uint32_t width, uint32_t height) {
    const auto& state = *menu_state;
    // The verified menu calls test AL after thiscall/ret 8. Do not change the device's native mode vector.
    using Predicate = uint8_t(__thiscall*)(void*, uint32_t, uint32_t);
    const auto native = reinterpret_cast<Predicate>(state.base + state.descriptor.guard_rva)(graphics, width, height);
    if (native) return native;
    return custom_menu_candidate(state.descriptor, state.custom, {width, height}, active_monitor_size()) ? 1 : 0;
}
std::vector<Mode> hardware_modes() {
    const auto release = [](IDirect3D9* object) { if (object) object->Release(); };
    std::unique_ptr<IDirect3D9, decltype(release)> d3d(Direct3DCreate9(D3D_SDK_VERSION), release);
    if (!d3d) throw std::runtime_error("Direct3D9 mode enumeration is unavailable");
    std::vector<Mode> modes;
    // kr-812 sub_ABBFE0 selects X8R8G8B8 or R5G6B5 and enumerates the selected adapter.
    // The union supplies candidates; sub_ABA920 still filters them against that exact adapter and format.
    for (UINT adapter = 0; adapter < d3d->GetAdapterCount(); ++adapter) {
        for (const auto format : {D3DFMT_X8R8G8B8, D3DFMT_R5G6B5}) {
            const auto count = d3d->GetAdapterModeCount(adapter, format);
            for (UINT index = 0; index < count; ++index) {
                D3DDISPLAYMODE mode{};
                if (FAILED(d3d->EnumAdapterModes(adapter, format, index, &mode)))
                    throw std::runtime_error("Direct3D9 display-mode enumeration failed");
                modes.push_back({mode.Width, mode.Height});
            }
        }
    }
    if (modes.empty()) throw std::runtime_error("Direct3D9 returned no supported display modes");
    return modes;
}
bool equal(const State& state, uintptr_t address, const void* expected, size_t size) {
    const auto* bytes = static_cast<const uint8_t*>(expected);
    std::array<uint8_t, 4096> buffer{};
    for (size_t offset = 0; offset < size;) {
        const auto count = static_cast<uint32_t>(std::min(buffer.size(), size - offset));
        if (state.host->read(address + offset, buffer.data(), count) != FTHOOK_OK
            || std::memcmp(buffer.data(), bytes + offset, count) != 0) return false;
        offset += count;
    }
    return true;
}
int32_t FTHOOK_CALL locate(void* context, const fthook_image* image, uintptr_t* address) {
    const auto& state = *static_cast<const State*>(context);
    if (!image || !address || image->base != state.base || image->timestamp != state.descriptor.timestamp
        || std::find(state.descriptor.image_sizes.begin(), state.descriptor.image_sizes.end(), image->size)
            == state.descriptor.image_sizes.end()) return FTHOOK_INVALID;
    const auto& descriptor = state.descriptor;
    if (!equal(state, state.base + descriptor.guard_rva, descriptor.guard.data(), descriptor.guard.size())
        || !equal(state, state.base + descriptor.table_rva, descriptor.original_modes.data(),
            descriptor.original_modes.size() * sizeof(Mode))) return FTHOOK_NOT_FOUND;
    for (const auto& write : state.writes)
        if (!equal(state, state.base + write.rva, write.expected.data(), write.expected.size())) return FTHOOK_NOT_FOUND;
    if (descriptor.main_stage) {
        const auto& layout = *descriptor.main_stage;
        for (const auto* guard : {&layout.loader, &layout.set_width, &layout.set_height})
            if (!equal(state, state.base + guard->rva + guard->verify_offset, guard->expected.data(), guard->expected.size()))
                return FTHOOK_NOT_FOUND;
    }
    *address = state.base + descriptor.guard_rva;
    return FTHOOK_OK;
}
int32_t FTHOOK_CALL cleanup(void* context) {
    // Runtime rollback removes the calls before their state and replacement mode table are released.
    if (stage_state == context) stage_state = nullptr;
    if (menu_state == context) menu_state = nullptr;
    delete static_cast<State*>(context);
    return FTHOOK_OK;
}
bool valid_build(std::string_view build) {
    return !build.empty() && std::all_of(build.begin(), build.end(), [](unsigned char c) {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
    });
}
int32_t activate(const fthook_host* host, fthook_plugin* plugin, Descriptor descriptor,
    std::span<const Mode> hardware, uintptr_t base) {
    std::vector<Mode> custom;
    if (host->option) {
        fthook_option option{};
        const auto status = host->option(host->context, "custom_resolutions", &option);
        if (status == FTHOOK_OK) {
            if (option.kind != FTHOOK_OPTION_STRING || !option.text)
                throw std::runtime_error("custom_resolutions must be a string option");
            custom = parse_custom_modes(option.text);
        } else if (status != FTHOOK_NOT_FOUND)
            throw std::runtime_error("could not read custom_resolutions");
    }
    auto modes = make_modes(descriptor, hardware, custom);
    auto state = std::make_unique<State>(host, base, std::move(descriptor), std::move(modes), std::move(custom), std::vector<Write>{});
    state->writes = make_writes(state->descriptor, static_cast<uint32_t>(base),
        static_cast<uint32_t>(reinterpret_cast<uintptr_t>(state->modes.data())), state->modes.size());
    const auto stage_writes = make_stage_writes(state->descriptor, static_cast<uint32_t>(base),
        static_cast<uint32_t>(reinterpret_cast<uintptr_t>(&load_main_stage)));
    state->writes.insert(state->writes.end(), stage_writes.begin(), stage_writes.end());
    const bool filter_menu = state->descriptor.menu_filter_call && std::any_of(state->custom.begin(), state->custom.end(),
        [&](const auto& mode) {
            return custom_menu_candidate(state->descriptor, state->custom, mode, {INT32_MAX, INT32_MAX});
        });
    if (filter_menu) state->writes.push_back(make_menu_filter_write(state->descriptor, static_cast<uint32_t>(base),
        static_cast<uint32_t>(reinterpret_cast<uintptr_t>(&filter_menu_mode))));
    if ((state->descriptor.main_stage && stage_state) || (filter_menu && menu_state))
        throw std::runtime_error("resolution callback already has an active owner");
    if (state->descriptor.main_stage) {
        stage_state = state.get();
    }
    if (filter_menu) menu_state = state.get();
    plugin->abi_version = FTHOOK_ABI_VERSION;
    plugin->struct_size = sizeof(fthook_plugin);
    plugin->context = state.release();
    plugin->cleanup = cleanup;
    auto& active = *static_cast<State*>(plugin->context);
    const auto registered = host->register_locator(host->context,
        "bubblefighter.fantasy.resolution-extension.ready", locate, &active);
    if (registered != FTHOOK_OK) return registered;
    const std::array<uint8_t, 64> mask = [] { std::array<uint8_t, 64> result{}; result.fill(255); return result; }();
    for (const auto& write : active.writes) {
        const auto staged = host->stage_patch(host->context, base + write.rva, write.expected.data(),
            mask.data(), write.replacement.data(), static_cast<uint32_t>(write.replacement.size()));
        if (staged != FTHOOK_OK) return staged;
    }
    const auto message = "resolution extension: staged " + std::to_string(active.modes.size())
        + " display choices; native option persistence remains active";
    host->log(host->context, message.c_str());
    return FTHOOK_OK;
}
}

extern "C" int32_t FTHOOK_CALL fthook_plugin_main(const fthook_host* host, fthook_plugin* plugin) {
    if (!host || !plugin || host->abi_version != FTHOOK_ABI_VERSION
        || host->struct_size < offsetof(fthook_host, stage_patch) + sizeof(host->stage_patch)) return FTHOOK_ABI_UNSUPPORTED;
    if (!host->game || std::strcmp(host->game, "bubblefighter") != 0 || !host->build || !valid_build(host->build)
        || !host->plugin_directory || !host->read || !host->log || !host->stage_patch || !host->register_locator) return FTHOOK_INVALID;
    try {
        const std::u8string_view directory(reinterpret_cast<const char8_t*>(host->plugin_directory));
        const auto path = std::filesystem::path(directory) / "builds" / (std::string(host->build) + ".toml");
        std::ifstream stream(path, std::ios::binary);
        if (!stream) throw std::runtime_error("no resolution descriptor for the selected client build");
        std::ostringstream content;
        content << stream.rdbuf();
        if (stream.bad()) throw std::runtime_error("could not read the resolution descriptor");
        auto descriptor = parse_descriptor(content.str());
        if (descriptor.build != host->build) throw std::runtime_error("resolution descriptor build does not match the launch");
        const auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
        return activate(host, plugin, std::move(descriptor), hardware_modes(), base);
    } catch (const std::exception& exception) {
        host->log(host->context, "resolution extension: initialization failed");
        host->log(host->context, exception.what());
        return FTHOOK_FAILED;
    } catch (...) {
        host->log(host->context, "resolution extension: initialization failed");
        return FTHOOK_FAILED;
    }
}

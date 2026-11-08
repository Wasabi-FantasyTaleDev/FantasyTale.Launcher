#include "../resolution-extension/resolution_extension.cpp"
#include "check.h"
#include <functional>

namespace {
std::string read_descriptor() {
    std::ifstream file("games/BubbleFighter/plugins/bubblefighter.fantasy.resolution-extension/builds/kr-812.toml");
    if (!file) throw std::runtime_error("run tests from the repository root");
    return {std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
}
bool throws(const std::function<void()>& action) {
    try { action(); return false; } catch (const std::exception&) { return true; }
}
std::string replaced(std::string source, std::string_view from, std::string_view to) {
    const auto index = source.find(from);
    if (index == std::string::npos) throw std::runtime_error("invalid test replacement");
    source.replace(index, from.size(), to);
    return source;
}
uint32_t pointer(const std::vector<uint8_t>& code, size_t offset) {
    uint32_t value = 0;
    std::memcpy(&value, code.data() + offset, 4);
    return value;
}
const std::array<Mode, 12> hardware{{{1920,1080}, {2560,1440}, {1920,1080}, {2196,1464},
    {1024,768}, {800,600}, {640,480}, {3440,1440}, {1920,0}, {UINT32_MAX,1080}, {768,1024}, {2560,1600}}};

void model_tests(const std::string& source, const Descriptor& descriptor) {
    const auto modes = make_modes(descriptor, hardware);
    CHECK(modes.size() == 11);
    CHECK(std::is_sorted(modes.begin(), modes.end()));
    CHECK(std::count(modes.begin(), modes.end(), Mode{1920,1080}) == 1);
    CHECK(std::find(modes.begin(), modes.end(), Mode{2196,1464}) != modes.end());
    CHECK(std::find(modes.begin(), modes.end(), Mode{3440,1440}) == modes.end());
    for (const auto& original : descriptor.original_modes)
        CHECK(std::find(modes.begin(), modes.end(), original) != modes.end());
    CHECK(make_modes(descriptor, {}).size() == 7);
    const auto custom = parse_custom_modes(" 1920 x 1080; 2560X1440, 3440 x 1440\r\n");
    CHECK(custom.size() == 3);
    CHECK((custom[0] == Mode{1920, 1080}));
    CHECK((custom[1] == Mode{2560, 1440}));
    CHECK((custom[2] == Mode{3440, 1440}));
    const auto merged = make_modes(descriptor, {}, custom);
    CHECK(merged.size() == 10);
    CHECK(std::count(merged.begin(), merged.end(), Mode{1920, 1080}) == 1);
    CHECK(parse_custom_modes("1920 x 1080;1920 x 1080").size() == 2);
    CHECK(parse_custom_modes(" \t;,,\r\n").empty());
    CHECK(parse_custom_modes("\t1920\tx\t1080\t\n2560X1440").size() == 2);
    CHECK(parse_custom_modes("2147483647 x 2147483647").size() == 1);
    for (const auto input : {"1 x 2147483648", "2147483648 x 1", "0 x 600", "800 x 0",
        "bad", "1920", "1920x", "x1080", "-1920x1080", "1920x1080junk",
        "1920x1080x60", "4294967296x1080", "1920 x 1080;bad"})
        CHECK(throws([&] { parse_custom_modes(input); }));
    CHECK(throws([&] { parse_custom_modes(std::string(4097, ' ')); }));
    std::string many;
    for (int index = 0; index < 256; ++index) many += "800x600;";
    CHECK(parse_custom_modes(many).size() == 256);
    CHECK(throws([&] { parse_custom_modes(many + "800x600"); }));
    const auto duplicate_modes = make_modes(descriptor, hardware, parse_custom_modes("1920x1080;1920x1080;800x600"));
    CHECK(duplicate_modes == modes);
    const auto writes = make_writes(descriptor, 0x10000000, 0x20000000, modes.size());
    CHECK(writes.size() == 5);
    CHECK(pointer(writes[0].expected, 1) == 0x10A68518);
    CHECK(pointer(writes[0].replacement, 1) == 0x20000000);
    CHECK(pointer(writes[2].replacement, 2) == 0x20000000 + 11 * 8);
    CHECK(pointer(writes[3].replacement, 1) == 0x20000004);
    CHECK(pointer(writes[4].replacement, 1) == 0x20000000 + 11 * 8 + 4);
    CHECK(std::all_of(writes[1].replacement.begin(), writes[1].replacement.end(), [](auto b) { return b == 0x90; }));
    CHECK(throws([&] { make_writes(descriptor, 0x10000000, 0x7FFFFFF0, modes.size()); }));
    CHECK(throws([&] { make_writes(descriptor, 0x10000000, 0xFFFFFFF0, modes.size()); }));
    CHECK(throws([&] { make_writes(descriptor, 0x10000000, 0x20000000, 0); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "schema = 1", "schema = 2")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "BD 18 85 E6 00", "BD ?? 85 E6 00")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "BD 18 85 E6 00", "BD 18 85 E7 00")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "operand_offset = 2", "operand_offset = 99")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "0x2961D0", "0x296109")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "minimum_height = 768", "minimum_height = -1")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "maximum_aspect_milli", "maximum_aspect_ratio")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "target = \"height_end\"", "target = \"end\"")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "guard_rva = 0x6BA920", "guard_rva = 0x296109")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "83 7C 24 10 06 0F 87 AF 00 00 00", "83 7C 24 10 05 0F 87 AF 00 00 00")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "83 7C 24 10 06 0F 87 AF 00 00 00", "83 7C 24 10 06 0F 87 AF 00 00 00 85 ED")); }));
    const auto extra = parse_custom_modes("1584x896;800x600;640x480;1920x1081;1921x1080");
    CHECK(custom_menu_candidate(descriptor, extra, {1584,896}, {1920,1080}));
    CHECK(!custom_menu_candidate(descriptor, extra, {1584,896}, {}));
    CHECK(!custom_menu_candidate(descriptor, extra, {1920,1080}, {1920,1080}));
    CHECK(!custom_menu_candidate(descriptor, extra, {800,600}, {1920,1080}));
    CHECK(!custom_menu_candidate(descriptor, extra, {640,480}, {1920,1080}));
    CHECK(!custom_menu_candidate(descriptor, extra, {1920,1081}, {1920,1080}));
    CHECK(!custom_menu_candidate(descriptor, extra, {1921,1080}, {1920,1080}));
    CHECK(throws([&] { parse_descriptor(replaced(source, "E8 EA 47 42 00", "E8 EB 47 42 00")); }));
    CHECK(throws([&] { parse_descriptor(replaced(source, "E8 EA 47 42 00", "90 EA 47 42 00")); }));
    const auto menu_call = make_menu_filter_write(descriptor, 0x10000000, 0xF0000000);
    CHECK(menu_call.rva == 0x296131);
    CHECK(menu_call.expected[0] == 0xE8 && menu_call.replacement[0] == 0xE8);
    CHECK(uint32_t{0x10000000} + menu_call.rva + 5 + pointer(menu_call.replacement, 1) == 0xF0000000);
    CHECK(valid_build("kr-812"));
    CHECK(!valid_build("../kr-812"));
    CHECK(!valid_build("kr/812"));
}

struct Host {
    uintptr_t base = 0x10000000;
    std::vector<uint8_t> image;
    uint8_t* execution = nullptr;
    std::vector<Write> staged;
    fthook_locator locator = nullptr;
    void* locator_context = nullptr;
    int stage_failure = -1;
    int32_t registration = FTHOOK_OK;
    std::string custom;
    int32_t option_status = FTHOOK_NOT_FOUND;
    uint32_t option_kind = FTHOOK_OPTION_STRING;
    static inline Host* current = nullptr;
    explicit Host(const Descriptor& descriptor) : image(descriptor.image_sizes.front()) { current = this; }
    static int32_t FTHOOK_CALL read(uintptr_t address, void* destination, uint32_t count) {
        auto& h = *current;
        if (address < h.base || address - h.base > h.image.size() || count > h.image.size() - (address - h.base)) return FTHOOK_FAILED;
        std::memcpy(destination, (h.execution ? h.execution : h.image.data()) + address - h.base, count);
        return FTHOOK_OK;
    }
    static void FTHOOK_CALL log(void*, const char*) {}
    static int32_t FTHOOK_CALL option(void* context, const char* key, fthook_option* value) {
        auto& h = *static_cast<Host*>(context);
        CHECK(std::strcmp(key, "custom_resolutions") == 0);
        value->kind = h.option_kind;
        value->text = h.custom.c_str();
        return h.option_status;
    }
    static int32_t FTHOOK_CALL register_locator(void* context, const char* name, fthook_locator locator, void* state) {
        auto& h = *static_cast<Host*>(context);
        CHECK(std::strcmp(name, "bubblefighter.fantasy.resolution-extension.ready") == 0);
        h.locator = locator; h.locator_context = state;
        return h.registration;
    }
    static int32_t FTHOOK_CALL stage(void* context, uintptr_t address, const uint8_t* expected,
        const uint8_t* mask, const uint8_t* replacement, uint32_t size) {
        auto& h = *static_cast<Host*>(context);
        CHECK(size > 0 && size <= 64);
        CHECK(std::all_of(mask, mask + size, [](auto b) { return b == 255; }));
        if (h.stage_failure >= 0 && h.staged.size() == static_cast<size_t>(h.stage_failure)) return FTHOOK_FAILED;
        h.staged.push_back({static_cast<uint32_t>(address - h.base), {expected, expected + size}, {replacement, replacement + size}});
        return FTHOOK_OK;
    }
    fthook_host api() {
        fthook_host result{};
        result.abi_version = FTHOOK_ABI_VERSION;
        result.struct_size = sizeof(result);
        result.context = this;
        result.game = "bubblefighter";
        result.build = "kr-812";
        result.plugin_directory = ".";
        result.log = log; result.read = read;
        result.option = option;
        result.register_locator = register_locator; result.stage_patch = stage;
        return result;
    }
    void fill(const Descriptor& descriptor) {
        std::memcpy(image.data() + descriptor.table_rva, descriptor.original_modes.data(), descriptor.original_modes.size() * sizeof(Mode));
        std::copy(descriptor.guard.begin(), descriptor.guard.end(), image.begin() + descriptor.guard_rva);
        for (const auto& write : staged) std::copy(write.expected.begin(), write.expected.end(), image.begin() + write.rva);
        if (descriptor.main_stage) {
            const auto& layout = *descriptor.main_stage;
            for (const auto* guard : {&layout.loader, &layout.set_width, &layout.set_height})
                std::copy(guard->expected.begin(), guard->expected.end(), image.begin() + guard->rva + guard->verify_offset);
        }
    }
};

void activation_tests(const Descriptor& descriptor) {
    Host host(descriptor);
    const auto api = host.api();
    fthook_plugin plugin{};
    CHECK(activate(&api, &plugin, descriptor, hardware, host.base) == FTHOOK_OK);
    CHECK(plugin.cleanup && plugin.context);
    const auto write_count = descriptor.sites.size() + (descriptor.main_stage ? descriptor.main_stage->calls.size() : 0);
    CHECK(host.staged.size() == write_count);
    fthook_image image{host.base, descriptor.timestamp, descriptor.image_sizes.front(), nullptr, 0};
    uintptr_t address = 0;
    CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_NOT_FOUND);
    host.fill(descriptor);
    CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_OK);
    CHECK(address == host.base + descriptor.guard_rva);
    const auto untouched = host.image;
    for (const auto& write : host.staged) {
        host.image[write.rva] ^= 1;
        CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_NOT_FOUND);
        host.image[write.rva] ^= 1;
    }
    host.image[descriptor.table_rva] ^= 1;
    CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_NOT_FOUND);
    host.image[descriptor.table_rva] ^= 1;
    if (descriptor.main_stage) {
        const auto& layout = *descriptor.main_stage;
        for (const auto* guard : {&layout.loader, &layout.set_width, &layout.set_height}) {
            host.image[guard->rva + guard->verify_offset] ^= 1;
            CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_NOT_FOUND);
            host.image[guard->rva + guard->verify_offset] ^= 1;
        }
    }
    CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_OK);
    ++image.timestamp;
    CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_INVALID);
    --image.timestamp;
    image.size = descriptor.image_sizes.back();
    CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_OK);
    image.size = 0x1000;
    CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_INVALID);
    CHECK(host.image == untouched);
    // Model the client's two loops after commit, including the height pointer's end+4 sentinel.
    const auto& state = *static_cast<State*>(plugin.context);
    const auto* begin = reinterpret_cast<const Mode*>(pointer(host.staged[0].replacement, 1));
    const auto* end = reinterpret_cast<const Mode*>(pointer(host.staged[2].replacement, 2));
    CHECK(begin == state.modes.data() && end == begin + state.modes.size());
    auto height = static_cast<uintptr_t>(pointer(host.staged[3].replacement, 1));
    const auto height_end = static_cast<uintptr_t>(pointer(host.staged[4].replacement, 1));
    bool saved_mode_found = false;
    for (; height < height_end; height += sizeof(Mode)) {
        Mode mode{};
        std::memcpy(&mode, reinterpret_cast<const void*>(height - sizeof(uint32_t)), sizeof(mode));
        if (mode == Mode{2560,1440}) saved_mode_found = true;
    }
    CHECK(saved_mode_found);
    CHECK(plugin.cleanup(plugin.context) == FTHOOK_OK);
    for (size_t failure = 0; failure < write_count; ++failure) {
        Host failed(descriptor); failed.stage_failure = static_cast<int>(failure);
        const auto failed_api = failed.api();
        fthook_plugin result{};
        CHECK(activate(&failed_api, &result, descriptor, hardware, failed.base) == FTHOOK_FAILED);
        CHECK(result.cleanup && result.cleanup(result.context) == FTHOOK_OK);
        CHECK(std::all_of(failed.image.begin(), failed.image.end(), [](auto b) { return b == 0; }));
    }
    Host failed(descriptor); failed.registration = FTHOOK_INVALID;
    const auto failed_api = failed.api();
    fthook_plugin result{};
    CHECK(activate(&failed_api, &result, descriptor, hardware, failed.base) == FTHOOK_INVALID);
    CHECK(failed.staged.empty());
    CHECK(result.cleanup(result.context) == FTHOOK_OK);
}

struct MenuProbe {
    void* graphics = nullptr;
    Mode mode;
    unsigned calls = 0;
};
MenuProbe menu_probe;
uint8_t __fastcall fixture_filter(void* graphics, void*, uint32_t width, uint32_t height) {
    menu_probe = {graphics, {width, height}, menu_probe.calls + 1};
    return width == 1600 ? 0x7f : 0;
}
void run_menu_case(uint8_t(__thiscall* call)(void*, uint32_t, uint32_t), void* graphics, Mode mode, uint8_t expected) {
    uintptr_t before = 0, after = 0;
    menu_probe = {};
    __asm mov before, esp
    const auto result = call(graphics, mode.width, mode.height);
    __asm mov after, esp
    CHECK(before == after);
    CHECK(result == expected && menu_probe.calls == 1);
    CHECK(menu_probe.graphics == graphics && menu_probe.mode == mode);
}

void menu_abi_test(const Descriptor& descriptor) {
    Host host(descriptor);
    host.option_status = FTHOOK_OK;
    host.custom = "1584x896";
    auto* memory = static_cast<uint8_t*>(VirtualAlloc(nullptr, host.image.size(),
        MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    CHECK(memory != nullptr);
    if (!memory) return;
    host.execution = memory;
    host.base = reinterpret_cast<uintptr_t>(memory);
    auto api = host.api();
    fthook_plugin plugin{};
    CHECK(activate(&api, &plugin, descriptor, hardware, host.base) == FTHOOK_OK);
    const auto predicate = reinterpret_cast<uintptr_t>(&fixture_filter);
    auto* thunk = memory + descriptor.guard_rva;
    thunk[0] = 0xB8;
    std::memcpy(thunk + 1, &predicate, 4);
    thunk[5] = 0xFF;
    thunk[6] = 0xE0;
    const auto& write = host.staged.back();
    CHECK(write.rva == descriptor.menu_filter_call->rva);
    const std::array<uint8_t, 8> push_arguments{0xFF,0x74,0x24,0x08,0xFF,0x74,0x24,0x08};
    const std::array<uint8_t, 3> return_two{0xC2,0x08,0x00};
    auto* entry = memory + write.rva - push_arguments.size();
    std::copy(push_arguments.begin(), push_arguments.end(), entry);
    std::copy(write.replacement.begin(), write.replacement.end(), memory + write.rva);
    std::copy(return_two.begin(), return_two.end(), memory + write.rva + 5);
    CHECK(FlushInstructionCache(GetCurrentProcess(), memory, host.image.size()));
    using Predicate = uint8_t(__thiscall*)(void*, uint32_t, uint32_t);
    const auto call = reinterpret_cast<Predicate>(entry);
    auto* graphics = memory + 0x1000;
    const auto run = [&](Mode mode, uint8_t expected) {
        run_menu_case(call, graphics, mode, expected);
    };
    run({1600,900}, 0x7f);
    run({800,600}, 0);
    run({1920,1080}, 0);
    const auto previous = GetActiveWindow();
    const auto window = CreateWindowExW(0, L"STATIC", L"Resolution menu fixture", WS_POPUP,
        0, 0, 1, 1, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    CHECK(window != nullptr);
    if (window) {
        SetActiveWindow(window);
        CHECK(GetActiveWindow() == window);
        const auto monitor = active_monitor_size();
        CHECK(monitor.width > 0 && monitor.height > 0);
        const std::array<Mode, 1> custom_mode{Mode{1584,896}};
        run({1584,896}, custom_menu_candidate(descriptor, custom_mode, {1584,896}, monitor) ? 1 : 0);
        run({800,600}, 0);
        run({1920,1080}, 0);
        CHECK(DestroyWindow(window));
        SetActiveWindow(previous);
    }
    CHECK(plugin.cleanup(plugin.context) == FTHOOK_OK);
    CHECK(menu_state == nullptr);
    CHECK(VirtualFree(memory, 0, MEM_RELEASE));
}

void option_tests(const Descriptor& descriptor) {
    Host host(descriptor);
    host.option_status = FTHOOK_OK;
    host.custom = "3440 x 1440;3840x2160;800x600;3440x1440";
    auto api = host.api();
    fthook_plugin plugin{};
    CHECK(activate(&api, &plugin, descriptor, hardware, host.base) == FTHOOK_OK);
    const auto& state = *static_cast<State*>(plugin.context);
    CHECK(std::count(state.modes.begin(), state.modes.end(), Mode{3440,1440}) == 1);
    CHECK(std::count(state.modes.begin(), state.modes.end(), Mode{3840,2160}) == 1);
    for (const auto& mode : descriptor.original_modes)
        CHECK(std::count(state.modes.begin(), state.modes.end(), mode) == 1);
    CHECK(menu_state == plugin.context);
    CHECK(host.staged.back().rva == descriptor.menu_filter_call->rva);
    fthook_image image{host.base, descriptor.timestamp, descriptor.image_sizes.front(), nullptr, 0};
    uintptr_t address = 0;
    host.fill(descriptor);
    CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_OK);
    host.image[descriptor.menu_filter_call->rva + 1] ^= 1;
    CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_NOT_FOUND);
    CHECK(plugin.cleanup(plugin.context) == FTHOOK_OK);
    CHECK(menu_state == nullptr);
    host.staged.clear();
    for (const auto kind : {FTHOOK_OPTION_BOOL, FTHOOK_OPTION_INT}) {
        host.option_kind = kind;
        fthook_plugin rejected{};
        CHECK(throws([&] { activate(&api, &rejected, descriptor, hardware, host.base); }));
        CHECK(!rejected.context && host.staged.empty());
    }
    host.option_kind = FTHOOK_OPTION_STRING;
    host.custom = "0x1080";
    fthook_plugin rejected{};
    CHECK(throws([&] { activate(&api, &rejected, descriptor, hardware, host.base); }));
    CHECK(!rejected.context && host.staged.empty());
    host.custom.clear(); host.option_status = FTHOOK_FAILED;
    CHECK(throws([&] { activate(&api, &rejected, descriptor, hardware, host.base); }));
    CHECK(!rejected.context && host.staged.empty());
    api.option = nullptr;
    CHECK(activate(&api, &rejected, descriptor, hardware, host.base) == FTHOOK_OK);
    CHECK(static_cast<State*>(rejected.context)->modes == make_modes(descriptor, hardware));
    CHECK(rejected.cleanup(rejected.context) == FTHOOK_OK);
}

struct StageProbe {
    int loads = 0;
    int widths = 0;
    int heights = 0;
    void* object = nullptr;
    int* resource = nullptr;
} stage_probe;

int __fastcall fixture_load(void* object, void*, int* resource) {
    ++stage_probe.loads;
    stage_probe.object = object;
    stage_probe.resource = resource;
    const auto& layout = *stage_state->descriptor.main_stage;
    std::memcpy(static_cast<uint8_t*>(object) + layout.control_size_offset, &layout.original_size, sizeof(Mode));
    return 0x123456;
}
int __fastcall fixture_width(void* object, void*, int value) {
    ++stage_probe.widths;
    CHECK(object == stage_probe.object);
    std::memcpy(static_cast<uint8_t*>(object) + stage_state->descriptor.main_stage->control_size_offset, &value, sizeof(value));
    return 11;
}
int __fastcall fixture_height(void* object, void*, int value) {
    ++stage_probe.heights;
    CHECK(object == stage_probe.object);
    std::memcpy(static_cast<uint8_t*>(object) + stage_state->descriptor.main_stage->control_size_offset + 4, &value, sizeof(value));
    return 12;
}

Descriptor stage_descriptor() {
    std::ifstream file("games/BubbleFighter/plugins/bubblefighter.fantasy.resolution-extension/builds/cn-406.toml");
    const std::string source{std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
    for (const auto& [from, to] : std::array<std::pair<const char*, const char*>, 7>{{
        {"rva = 0x178CEF", "rva = 0x18A7E0"},
        {"E8 7C 34 3F 00", "90 7C 34 3F 00"},
        {"E8 7C 34 3F 00", "E8 7D 34 3F 00"},
        {"verify_offset = 7", "verify_offset = 0xFFFFFFFE"},
        {"screen_pointer_rva = 0x933C20", "screen_pointer_rva = 0xFFFFFFFC"},
        {"control_size_offset = 316", "control_size_offset = 315"},
        {"original_height = 900", "original_height = 0"}}})
        CHECK(throws([&] { parse_descriptor(replaced(source, from, to)); }));
    return parse_descriptor(source);
}

using StageLoader = int(__thiscall*)(void*, int*);
__declspec(noinline) int call_stage_fixture(StageLoader loader, void* object, int* resource) {
    uintptr_t before = 0, after = 0;
    __asm mov before, esp
    const auto result = loader(object, resource);
    __asm mov after, esp
    CHECK(before == after);
    return result;
}

void stage_tests(const Descriptor& descriptor) {
    const auto& layout = *descriptor.main_stage;
    CHECK((layout.original_size == Mode{1600, 900}));
    const std::array<Mode,4> visible_native{{{800,600},{1024,768},{1366,768},{1600,900}}};
    for (const auto& mode : visible_native) {
        CHECK(std::find(descriptor.original_modes.begin(), descriptor.original_modes.end(), mode) != descriptor.original_modes.end());
        CHECK(stage_size(descriptor, mode, {3840,2160}, layout.original_size) == layout.original_size);
    }
    auto disabled = descriptor;
    disabled.main_stage.reset();
    CHECK(make_stage_writes(disabled, 0x400000, 0).empty());
    CHECK(stage_size(disabled, {3840,2160}, {3840,2160}, layout.original_size) == layout.original_size);
    for (const auto& mode : descriptor.original_modes)
        CHECK(stage_size(descriptor, mode, {3840,2160}, layout.original_size) == layout.original_size);
    CHECK((stage_size(descriptor, {1920,1080}, {1920,1080}, layout.original_size) == Mode{1920,1080}));
    CHECK((stage_size(descriptor, {2560,1440}, {2048,1152}, layout.original_size) == Mode{2048,1152}));
    CHECK((stage_size(descriptor, {1280,1024}, {1280,1024}, layout.original_size) == Mode{1600,1024}));
    CHECK(stage_size(descriptor, {1280,720}, {1280,720}, layout.original_size) == layout.original_size);
    CHECK((stage_size(descriptor, {1920,1080}, {1920,1080}, {1800,1000}) == Mode{1800,1000}));
    CHECK(stage_size(descriptor, {0,1080}, {1920,1080}, layout.original_size) == layout.original_size);
    CHECK(stage_size(descriptor, {1920,1080}, {UINT32_MAX,1080}, layout.original_size) == layout.original_size);
    CHECK(throws([&] { make_stage_writes(descriptor, 0x400000, 0); }));
    const auto wrapping = make_stage_writes(descriptor, 0x10000000, 0xF0000000);
    for (const auto& call : wrapping)
        CHECK(0x10000000u + call.rva + 5 + pointer(call.replacement, 1) == 0xF0000000);

    Host host(descriptor);
    auto* memory = static_cast<uint8_t*>(VirtualAlloc(nullptr, host.image.size(), MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    CHECK(memory != nullptr);
    if (!memory) return;
    host.execution = memory;
    host.base = reinterpret_cast<uintptr_t>(memory);
    const auto api = host.api();
    fthook_plugin plugin{};
    CHECK(activate(&api, &plugin, descriptor, hardware, host.base) == FTHOOK_OK);
    host.fill(descriptor);
    std::copy(host.image.begin(), host.image.end(), memory);
    fthook_image image{host.base, descriptor.timestamp, descriptor.image_sizes.front(), nullptr, 0};
    uintptr_t address = 0;
    CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_OK);
    const auto thunk = [&](uint32_t rva, uintptr_t function) {
        memory[rva] = 0xB8;
        const auto target = static_cast<uint32_t>(function);
        std::memcpy(memory + rva + 1, &target, 4);
        memory[rva + 5] = 0xFF;
        memory[rva + 6] = 0xE0;
    };
    thunk(layout.loader.rva, reinterpret_cast<uintptr_t>(&fixture_load));
    thunk(layout.set_width.rva, reinterpret_cast<uintptr_t>(&fixture_width));
    thunk(layout.set_height.rva, reinterpret_cast<uintptr_t>(&fixture_height));
    for (const auto& write : host.staged) {
        if (write.replacement[0] != 0xE8) continue;
        const std::array<uint8_t, 4> argument{0xFF,0x74,0x24,0x04};
        const std::array<uint8_t, 3> return_one{0xC2,0x04,0x00};
        std::copy(argument.begin(), argument.end(), memory + write.rva - 4);
        std::copy(write.replacement.begin(), write.replacement.end(), memory + write.rva);
        std::copy(return_one.begin(), return_one.end(), memory + write.rva + 5);
    }
    CHECK(FlushInstructionCache(GetCurrentProcess(), memory, host.image.size()));
    const auto screen_address = static_cast<uint32_t>(host.base + 0x1000);
    std::memcpy(memory + layout.screen_pointer_rva, &screen_address, 4);
    auto* object = memory + 0x2000;
    int resource = 7;
    const auto run = [&](Mode physical, Mode logical, Mode expected) {
        std::memcpy(memory + 0x1000 + layout.screen_size_offset, &physical, sizeof(physical));
        for (const auto& call : layout.calls) {
            std::memcpy(object + layout.control_size_offset, &logical, sizeof(logical));
            stage_probe = {};
            const auto result = call_stage_fixture(reinterpret_cast<StageLoader>(host.base + call.rva - 4), object, &resource);
            CHECK(result == 0x123456 && stage_probe.loads == 1);
            CHECK(stage_probe.object == object && stage_probe.resource == &resource);
            Mode actual;
            std::memcpy(&actual, object + layout.control_size_offset, sizeof(actual));
            CHECK(actual == expected);
            CHECK(stage_probe.widths == (expected.width != layout.original_size.width ? 1 : 0));
            CHECK(stage_probe.heights == (expected.height != layout.original_size.height ? 1 : 0));
        }
    };
    // cn-406 sub_9B71F0 filters native table candidates; both construction branches retain those modes with scaled UI.
    for (const auto& mode : descriptor.original_modes) {
        run(mode, mode, layout.original_size);
        run(mode, {3840,2160}, layout.original_size);
    }
    run({1920,1080}, {1920,1080}, {1920,1080});
    run({2560,1440}, {2560,1440}, {2560,1440});
    run({3440,1440}, {3440,1440}, {3440,1440});
    run({2560,1440}, {2048,1152}, {2048,1152});
    run({1280,1024}, {1280,1024}, {1600,1024});
    run({1280,720}, {1280,720}, layout.original_size);
    run({1920,1080}, {0,1080}, layout.original_size);
    run({1920,1080}, {1920,UINT32_MAX}, layout.original_size);
    run({1920,1080}, {2147483648u,1080}, layout.original_size);
    CHECK(plugin.cleanup(plugin.context) == FTHOOK_OK);
    CHECK(stage_state == nullptr);
    CHECK(VirtualFree(memory, 0, MEM_RELEASE));
}

void all_descriptor_tests() {
    const auto folder = std::filesystem::path("games/BubbleFighter/plugins/bubblefighter.fantasy.resolution-extension/builds");
    size_t count = 0;
    for (const auto& entry : std::filesystem::directory_iterator(folder)) {
        if (entry.path().extension() != ".toml") continue;
        std::ifstream file(entry.path());
        const std::string source{std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>()};
        const auto descriptor = parse_descriptor(source);
        CHECK(descriptor.build == entry.path().stem().string());
        CHECK(descriptor.build != "cn-279" && descriptor.build != "cn-324");
        CHECK(descriptor.main_stage.has_value() == (descriptor.build == "cn-406"));
        const auto modes = make_modes(descriptor, hardware);
        const auto writes = make_writes(descriptor, 0x10000000, 0x20000000, modes.size());
        for (size_t index = 0; index < writes.size(); ++index) {
            const auto& site = descriptor.sites[index];
            const auto& write = writes[index];
            if (site.target == Target::RemoveCountGuard) {
                CHECK(std::all_of(write.replacement.begin(), write.replacement.end(), [](auto b) { return b == 0x90; }));
                continue;
            }
            const auto offset = site.target == Target::Begin ? 0u : site.target == Target::End
                ? modes.size() * sizeof(Mode) : site.target == Target::HeightBegin ? 4u : modes.size() * sizeof(Mode) + 4u;
            CHECK(pointer(write.replacement, site.operand_offset) == 0x20000000 + offset);
        }
        activation_tests(descriptor);
        option_tests(descriptor);
        ++count;
    }
    CHECK(count >= 8);
    std::printf("Validated %zu build descriptors.\n", count);
}

void dll_tests(const Descriptor& descriptor) {
    const auto dll_path = std::filesystem::absolute("artifacts/resolution-extension/resolution_extension.dll");
    const auto dll = LoadLibraryW(dll_path.c_str());
    if (!dll) std::fprintf(stderr, "Could not load plugin DLL: Windows error %lu\n", GetLastError());
    CHECK(dll != nullptr);
    if (!dll) return;
    const auto entry = reinterpret_cast<fthook_plugin_entry>(GetProcAddress(dll, "fthook_plugin_main"));
    CHECK(entry != nullptr);
    if (entry) {
        CHECK(entry(nullptr, nullptr) == FTHOOK_ABI_UNSUPPORTED);
        Host host(descriptor);
        host.base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
        auto api = host.api();
        api.build = descriptor.build.c_str();
        api.plugin_directory = "games/BubbleFighter/plugins/bubblefighter.fantasy.resolution-extension";
        fthook_plugin plugin{};
        CHECK(entry(&api, &plugin) == FTHOOK_OK);
        if (plugin.cleanup) {
            CHECK(host.staged.size() == descriptor.sites.size() + (descriptor.main_stage ? descriptor.main_stage->calls.size() : 0));
            host.fill(descriptor);
            fthook_image image{host.base, descriptor.timestamp, descriptor.image_sizes.front(), nullptr, 0};
            uintptr_t address = 0;
            CHECK(host.locator(host.locator_context, &image, &address) == FTHOOK_OK);
            CHECK(address == host.base + descriptor.guard_rva);
            const auto table = pointer(host.staged[0].replacement, 1);
            const auto end = pointer(host.staged[2].replacement, 2);
            CHECK(end > table && (end - table) % sizeof(Mode) == 0);
            CHECK((end - table) / sizeof(Mode) >= descriptor.original_modes.size());
            CHECK(plugin.cleanup(plugin.context) == FTHOOK_OK);
        }
        api.build = "kr-1256";
        fthook_plugin unsupported{};
        CHECK(entry(&api, &unsupported) == FTHOOK_FAILED);
        CHECK(!unsupported.cleanup && !unsupported.context);
    }
    CHECK(FreeLibrary(dll));
}
}

int main(int argc, char** argv) {
    if (argc == 2 && std::strcmp(argv[1], "--display-modes") == 0) {
        try {
            const auto descriptor = parse_descriptor(read_descriptor());
            for (const auto& mode : make_modes(descriptor, hardware_modes()))
                std::printf("%u x %u\n", mode.width, mode.height);
            return 0;
        } catch (const std::exception& error) { std::fprintf(stderr, "%s\n", error.what()); return 1; }
    }
    try {
        const auto source = read_descriptor();
        const auto descriptor = parse_descriptor(source);
        model_tests(source, descriptor);
        menu_abi_test(descriptor);
        all_descriptor_tests();
        dll_tests(descriptor);
        const auto main_stage = stage_descriptor();
        stage_tests(main_stage);
        dll_tests(main_stage);
        CHECK(fthook_plugin_main(nullptr, nullptr) == FTHOOK_ABI_UNSUPPORTED);
        fthook_plugin plugin{};
        fthook_host host{};
        CHECK(fthook_plugin_main(&host, &plugin) == FTHOOK_ABI_UNSUPPORTED);
        host.abi_version = 1; host.struct_size = sizeof(host);
        CHECK(fthook_plugin_main(&host, &plugin) == FTHOOK_INVALID);
        return finish("resolution extension");
    } catch (const std::exception& error) { std::fprintf(stderr, "%s\n", error.what()); return 1; }
}

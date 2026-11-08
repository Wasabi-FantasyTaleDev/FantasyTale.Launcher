#include "resolution_model.h"
#include <toml.hpp>
#include <algorithm>
#include <charconv>
#include <cctype>
#include <cstring>
#include <limits>
#include <set>
#include <sstream>
#include <stdexcept>

namespace resolution_extension {
namespace {
[[noreturn]] void invalid(const std::string& message) {
    throw std::runtime_error("resolution descriptor: " + message);
}
void keys(const toml::table& table, std::initializer_list<std::string_view> allowed) {
    for (const auto& [key, value] : table) {
        (void)value;
        if (std::find(allowed.begin(), allowed.end(), key.str()) == allowed.end())
            invalid("unknown key '" + std::string(key.str()) + "'");
    }
}
uint32_t number(const toml::node_view<const toml::node>& node, const char* name, bool zero = false) {
    const auto value = node.value<int64_t>();
    if (!node.is_integer() || !value || *value < (zero ? 0 : 1) || *value > UINT32_MAX)
        invalid(std::string(name) + " must be a " + (zero ? "nonnegative" : "positive") + " 32-bit integer");
    return static_cast<uint32_t>(*value);
}
std::string text(const toml::node_view<const toml::node>& node, const char* name) {
    const auto value = node.value<std::string>();
    if (!value || value->empty()) invalid(std::string(name) + " must be a nonempty string");
    return *value;
}
std::vector<uint8_t> bytes(const toml::node_view<const toml::node>& node, const char* name) {
    std::istringstream stream(text(node, name));
    std::vector<uint8_t> result;
    std::string token;
    while (stream >> token) {
        unsigned int value = 0;
        const auto parsed = std::from_chars(token.data(), token.data() + token.size(), value, 16);
        if (token.size() != 2 || parsed.ec != std::errc{} || parsed.ptr != token.data() + token.size())
            invalid(std::string(name) + " must contain exact two-digit hex bytes");
        result.push_back(static_cast<uint8_t>(value));
    }
    if (result.empty() || result.size() > 64) invalid(std::string(name) + " exceeds the ABI write size");
    return result;
}
uint32_t dword(std::span<const uint8_t> data, size_t offset) {
    uint32_t value = 0;
    std::memcpy(&value, data.data() + offset, sizeof(value));
    return value;
}
void dword(std::vector<uint8_t>& data, size_t offset, uint32_t value) {
    std::memcpy(data.data() + offset, &value, sizeof(value));
}
uint32_t add(uint32_t a, uint64_t b) {
    if (b > UINT32_MAX || a > UINT32_MAX - b) invalid("address range overflows x86");
    return a + static_cast<uint32_t>(b);
}
uint32_t table_offset(Target target, uint32_t table_size) {
    switch (target) {
    case Target::Begin: return 0;
    case Target::End: return table_size;
    case Target::HeightBegin: return sizeof(uint32_t);
    case Target::HeightEnd: return add(table_size, sizeof(uint32_t));
    default: invalid("count guard has no pointer operand");
    }
}
void count_guard(std::span<const uint8_t> code, size_t original_count) {
    // The menu bounds remain pointer-based; only the redundant cmp/ja pair may be removed.
    size_t compare_size = 0;
    if (code.size() >= 7 && code[0] == 0x83 && code[1] == 0x7c && code[2] == 0x24)
        compare_size = 5;
    else if (code.size() >= 5 && code[0] == 0x83 && code[1] >= 0xf8)
        compare_size = 3;
    if (compare_size == 0 || original_count > 128 || code[compare_size - 1] != original_count - 1)
        invalid("count guard must compare the original last index");
    if ((code.size() == compare_size + 2 && code[compare_size] == 0x77)
        || (code.size() == compare_size + 6 && code[compare_size] == 0x0f && code[compare_size + 1] == 0x87))
        return;
    invalid("count guard must contain only cmp and ja instructions");
}
}

Descriptor parse_descriptor(std::string_view source) {
    const auto parsed = toml::parse(source);
    if (!parsed) invalid(std::string(parsed.error().description()));
    const auto& root = parsed.table();
    keys(root, {"schema", "build", "timestamp", "image_sizes", "preferred_base", "table_rva",
        "guard_rva", "guard", "minimum_width", "minimum_height", "maximum_aspect_milli", "original_modes", "sites",
        "main_stage", "menu_filter_call"});
    if (number(root["schema"], "schema") != 1) invalid("unsupported schema");
    Descriptor descriptor;
    descriptor.build = text(root["build"], "build");
    descriptor.timestamp = number(root["timestamp"], "timestamp");
    descriptor.preferred_base = number(root["preferred_base"], "preferred_base");
    descriptor.table_rva = number(root["table_rva"], "table_rva");
    descriptor.guard_rva = number(root["guard_rva"], "guard_rva");
    descriptor.guard = bytes(root["guard"], "guard");
    descriptor.minimum_width = number(root["minimum_width"], "minimum_width");
    descriptor.minimum_height = number(root["minimum_height"], "minimum_height");
    descriptor.maximum_aspect_milli = number(root["maximum_aspect_milli"], "maximum_aspect_milli");
    const auto* sizes = root["image_sizes"].as_array();
    if (!sizes || sizes->empty()) invalid("image_sizes must be a nonempty array");
    for (const auto& size : *sizes) descriptor.image_sizes.push_back(number(toml::node_view{size}, "image_sizes"));
    const auto* modes = root["original_modes"].as_array();
    if (!modes || modes->empty()) invalid("original_modes must be a nonempty array");
    if (modes->size() > UINT32_MAX / sizeof(Mode)) invalid("original table exceeds x86 address space");
    for (const auto& mode : *modes) {
        const auto* pair = mode.as_array();
        if (!pair || pair->size() != 2) invalid("original_modes entries must be width/height pairs");
        descriptor.original_modes.push_back({number(toml::node_view{(*pair)[0]}, "width"),
            number(toml::node_view{(*pair)[1]}, "height")});
        if (descriptor.original_modes.back().width > INT32_MAX || descriptor.original_modes.back().height > INT32_MAX)
            invalid("original dimensions must fit the game's signed fields");
    }
    const auto* sites = root["sites"].as_array();
    if (!sites || sites->empty() || sites->size() > 64) invalid("sites must fit the ABI staged write limit");
    std::set<Target> targets;
    for (const auto& node : *sites) {
        const auto* row = node.as_table();
        if (!row) invalid("sites entries must be tables");
        keys(*row, {"rva", "expect", "target", "operand_offset"});
        Site site;
        site.rva = number((*row)["rva"], "site.rva");
        site.expected = bytes((*row)["expect"], "site.expect");
        const auto target = text((*row)["target"], "site.target");
        if (target == "begin") site.target = Target::Begin;
        else if (target == "end") site.target = Target::End;
        else if (target == "height_begin") site.target = Target::HeightBegin;
        else if (target == "height_end") site.target = Target::HeightEnd;
        else if (target == "remove_count_guard") site.target = Target::RemoveCountGuard;
        else invalid("unknown site.target '" + target + "'");
        if (site.target == Target::RemoveCountGuard) {
            if (row->contains("operand_offset")) invalid("count guard cannot have an operand_offset");
            count_guard(site.expected, descriptor.original_modes.size());
        } else {
            site.operand_offset = number((*row)["operand_offset"], "site.operand_offset", true);
            if (site.operand_offset > site.expected.size() || site.expected.size() - site.operand_offset < 4)
                invalid("pointer operand exceeds site.expect");
            const auto offset = table_offset(site.target, static_cast<uint32_t>(descriptor.original_modes.size() * sizeof(Mode)));
            if (dword(site.expected, site.operand_offset) != add(add(descriptor.preferred_base, descriptor.table_rva), offset))
                invalid("expected pointer does not reference the original table");
        }
        targets.insert(site.target);
        descriptor.sites.push_back(std::move(site));
    }
    if (targets.size() != 5) invalid("menu, validator and count guard must be adapted together");
    const auto smallest_image = *std::min_element(descriptor.image_sizes.begin(), descriptor.image_sizes.end());
    if (add(descriptor.table_rva, descriptor.original_modes.size() * sizeof(Mode)) > smallest_image
        || add(descriptor.guard_rva, descriptor.guard.size()) > smallest_image)
        invalid("table or guard is outside the image");
    for (size_t i = 0; i < descriptor.sites.size(); ++i) {
        const auto& site = descriptor.sites[i];
        const auto end = add(site.rva, site.expected.size());
        if (end > smallest_image) invalid("site is outside the image");
        if (site.rva < descriptor.guard_rva + descriptor.guard.size() && descriptor.guard_rva < end)
            invalid("site overlaps the preflight guard");
        for (size_t j = 0; j < i; ++j) {
            const auto& prior = descriptor.sites[j];
            if (site.rva < prior.rva + prior.expected.size() && prior.rva < end) invalid("sites overlap");
        }
    }
    if (root.contains("main_stage")) {
        const auto* stage = root["main_stage"].as_table();
        if (!stage) invalid("main_stage must be a table");
        keys(*stage, {"loader", "set_width", "set_height", "calls", "screen_pointer_rva",
            "screen_size_offset", "control_size_offset", "original_width", "original_height"});
        StageLayout layout;
        const auto guard = [&](const toml::node_view<const toml::node>& node) {
            const auto* row = node.as_table();
            if (!row) invalid("stage code guards must be tables");
            keys(*row, {"rva", "expect", "verify_offset"});
            CodeGuard code{number((*row)["rva"], "stage.rva"), bytes((*row)["expect"], "stage.expect")};
            if (row->contains("verify_offset")) code.verify_offset = number((*row)["verify_offset"], "stage.verify_offset", true);
            if (add(add(code.rva, code.verify_offset), code.expected.size()) > smallest_image) invalid("stage code is outside the image");
            return code;
        };
        layout.loader = guard((*stage)["loader"]);
        layout.set_width = guard((*stage)["set_width"]);
        layout.set_height = guard((*stage)["set_height"]);
        layout.screen_pointer_rva = number((*stage)["screen_pointer_rva"], "screen_pointer_rva");
        layout.screen_size_offset = number((*stage)["screen_size_offset"], "screen_size_offset");
        layout.control_size_offset = number((*stage)["control_size_offset"], "control_size_offset");
        layout.original_size = {number((*stage)["original_width"], "original_width"),
            number((*stage)["original_height"], "original_height")};
        if (layout.original_size.width > INT32_MAX || layout.original_size.height > INT32_MAX
            || add(layout.screen_pointer_rva, 4) > smallest_image || layout.screen_pointer_rva % 4
            || layout.screen_size_offset % 4 || layout.control_size_offset % 4
            || layout.screen_size_offset > INT32_MAX - 8 || layout.control_size_offset > INT32_MAX - 8)
            invalid("invalid stage dimension fields");
        const auto* calls = (*stage)["calls"].as_array();
        if (!calls || calls->empty() || calls->size() > 64 - descriptor.sites.size()) invalid("stage calls exceed the staged write limit");
        for (const auto& node : *calls) {
            auto call = guard(toml::node_view{node});
            if (call.verify_offset || call.expected.size() != 5 || call.expected[0] != 0xe8
                || add(call.rva, 5) + dword(call.expected, 1) != layout.loader.rva)
                invalid("stage call must target the verified loader");
            const auto overlaps = [&](uint32_t rva, size_t size) {
                return call.rva < rva + size && rva < call.rva + call.expected.size();
            };
            for (const auto& site : descriptor.sites)
                if (overlaps(site.rva, site.expected.size())) invalid("stage call overlaps a mode-table site");
            for (const auto& prior : layout.calls)
                if (overlaps(prior.rva, prior.expected.size())) invalid("stage calls overlap");
            for (const auto* code : {&layout.loader, &layout.set_width, &layout.set_height})
                if (overlaps(code->rva + code->verify_offset, code->expected.size())) invalid("stage call overlaps a function guard");
            if (overlaps(descriptor.guard_rva, descriptor.guard.size())
                || overlaps(descriptor.table_rva, descriptor.original_modes.size() * sizeof(Mode)))
                invalid("stage call overlaps the original table or predicate");
            layout.calls.push_back(std::move(call));
        }
        descriptor.main_stage = std::move(layout);
    }
    if (root.contains("menu_filter_call")) {
        const auto* row = root["menu_filter_call"].as_table();
        if (!row) invalid("menu_filter_call must be a table");
        keys(*row, {"rva", "expect"});
        CodeGuard call{number((*row)["rva"], "menu_filter_call.rva"), bytes((*row)["expect"], "menu_filter_call.expect")};
        const auto end = add(call.rva, 5);
        if (end > smallest_image || call.expected.size() != 5 || call.expected[0] != 0xe8
            || end + dword(call.expected, 1) != descriptor.guard_rva)
            invalid("menu filter call must target the verified display predicate");
        const auto overlaps = [&](uint32_t rva, size_t size) {
            return call.rva < rva + size && rva < end;
        };
        if (overlaps(descriptor.guard_rva, descriptor.guard.size())
            || overlaps(descriptor.table_rva, descriptor.original_modes.size() * sizeof(Mode)))
            invalid("menu filter call overlaps the original table or predicate");
        for (const auto& site : descriptor.sites)
            if (overlaps(site.rva, site.expected.size())) invalid("menu filter call overlaps a mode-table site");
        size_t writes = descriptor.sites.size();
        if (descriptor.main_stage) {
            const auto& stage = *descriptor.main_stage;
            writes += stage.calls.size();
            for (const auto& prior : stage.calls)
                if (overlaps(prior.rva, prior.expected.size())) invalid("menu filter call overlaps a stage call");
            for (const auto* guard : {&stage.loader, &stage.set_width, &stage.set_height})
                if (overlaps(guard->rva + guard->verify_offset, guard->expected.size()))
                    invalid("menu filter call overlaps a stage function guard");
        }
        if (writes >= 64) invalid("menu filter call exceeds the staged write limit");
        descriptor.menu_filter_call = std::move(call);
    }
    return descriptor;
}

namespace {
Write redirect_call(const CodeGuard& call, uint32_t image_base, uint32_t callback) {
    const auto next = add(image_base, static_cast<uint64_t>(call.rva) + 5);
    Write write{call.rva, call.expected, call.expected};
    // An x86 relative call wraps in the 32-bit address space, including across 2 GiB.
    dword(write.replacement, 1, callback - next);
    return write;
}
}

std::vector<Write> make_stage_writes(const Descriptor& descriptor, uint32_t image_base, uint32_t callback) {
    std::vector<Write> writes;
    if (!descriptor.main_stage) return writes;
    if (!callback) invalid("stage callback is null");
    for (const auto& call : descriptor.main_stage->calls) writes.push_back(redirect_call(call, image_base, callback));
    return writes;
}

Write make_menu_filter_write(const Descriptor& descriptor, uint32_t image_base, uint32_t callback) {
    if (!descriptor.menu_filter_call || !callback) invalid("menu filter call or callback is missing");
    return redirect_call(*descriptor.menu_filter_call, image_base, callback);
}

bool custom_menu_candidate(const Descriptor& descriptor, std::span<const Mode> custom,
    Mode candidate, Mode monitor_size) {
    if (candidate.width > monitor_size.width || candidate.height > monitor_size.height
        || std::find(custom.begin(), custom.end(), candidate) == custom.end()
        || std::find(descriptor.original_modes.begin(), descriptor.original_modes.end(), candidate) != descriptor.original_modes.end())
        return false;
    return std::any_of(descriptor.original_modes.begin(), descriptor.original_modes.end(), [&](const auto& original) {
        return candidate.width >= original.width && candidate.height >= original.height;
    });
}

Mode stage_size(const Descriptor& descriptor, Mode physical, Mode logical, Mode current) {
    if (!descriptor.main_stage || current != descriptor.main_stage->original_size
        || physical.width == 0 || physical.height == 0 || logical.width == 0 || logical.height == 0
        || physical.width > INT32_MAX || physical.height > INT32_MAX
        || logical.width > INT32_MAX || logical.height > INT32_MAX
        || std::find(descriptor.original_modes.begin(), descriptor.original_modes.end(), physical) != descriptor.original_modes.end())
        return current;
    return {std::max(current.width, logical.width), std::max(current.height, logical.height)};
}

std::vector<Mode> make_modes(const Descriptor& descriptor, std::span<const Mode> hardware,
    std::span<const Mode> custom) {
    std::set<Mode> modes(descriptor.original_modes.begin(), descriptor.original_modes.end());
    for (const auto& mode : custom) {
        if (mode.width == 0 || mode.height == 0 || mode.width > INT32_MAX || mode.height > INT32_MAX)
            continue;
        modes.insert(mode);
    }
    for (const auto& mode : hardware) {
        if (mode.width > INT32_MAX || mode.height > INT32_MAX || mode.height == 0) continue;
        // kr-1256 sub_102CD90 limits aspect ratio; sub_5BCE20 preserves 800x600 and the 1024x768 minimum.
        if ((mode == Mode{800, 600} || (mode.width >= descriptor.minimum_width && mode.height >= descriptor.minimum_height))
            && static_cast<uint64_t>(mode.width) * 1000 < static_cast<uint64_t>(mode.height) * descriptor.maximum_aspect_milli)
            modes.insert(mode);
    }
    return {modes.begin(), modes.end()};
}

std::vector<Mode> parse_custom_modes(std::string_view source) {
    if (source.size() > 4096) throw std::runtime_error("custom_resolutions exceeds 4096 bytes");
    std::vector<Mode> result;
    constexpr size_t max_custom_modes = 256;
    size_t cursor = 0;
    while (cursor < source.size()) {
        const auto end = source.find_first_of(";,\r\n", cursor);
        const auto token = source.substr(cursor, end == std::string_view::npos ? source.size() - cursor : end - cursor);
        if (token.find_first_not_of(" \t\r\n") == std::string_view::npos) {
            if (end == std::string_view::npos) break;
            cursor = end + 1;
            continue;
        }
        const auto separator = token.find_first_of("xX");
        if (separator != std::string_view::npos) {
            uint32_t width = 0;
            uint32_t height = 0;
            const auto parse = [](std::string_view value, uint32_t& output) {
                while (!value.empty() && std::isspace(static_cast<unsigned char>(value.front()))) value.remove_prefix(1);
                while (!value.empty() && std::isspace(static_cast<unsigned char>(value.back()))) value.remove_suffix(1);
                if (value.empty()) return false;
                const auto [end, error] = std::from_chars(value.data(), value.data() + value.size(), output);
                return error == std::errc{} && end == value.data() + value.size();
            };
            if (!parse(token.substr(0, separator), width) || !parse(token.substr(separator + 1), height)
                || width == 0 || height == 0 || width > INT32_MAX || height > INT32_MAX)
                throw std::runtime_error("custom_resolutions entries must be positive width x height pairs within signed 32-bit range");
            if (result.size() == max_custom_modes)
                throw std::runtime_error("custom_resolutions exceeds 256 entries");
            result.push_back({width, height});
        } else throw std::runtime_error("custom_resolutions entries must use width x height");
        if (end == std::string_view::npos) break;
        cursor = end + 1;
    }
    return result;
}

std::vector<Write> make_writes(const Descriptor& descriptor, uint32_t image_base,
    uint32_t table_address, size_t mode_count) {
    if (mode_count < descriptor.original_modes.size() || mode_count > UINT32_MAX / sizeof(Mode))
        invalid("invalid replacement table size");
    const auto old_size = static_cast<uint32_t>(descriptor.original_modes.size() * sizeof(Mode));
    const auto new_size = static_cast<uint32_t>(mode_count * sizeof(Mode));
    // Both legacy loops use signed pointer comparisons; neither range may straddle 2 GiB.
    if (!table_address || (table_address >> 31) != (add(table_address, static_cast<uint64_t>(new_size) + 4) >> 31))
        invalid("replacement table crosses the signed pointer boundary");
    std::vector<Write> writes;
    for (const auto& site : descriptor.sites) {
        add(image_base, static_cast<uint64_t>(site.rva) + site.expected.size());
        Write write{site.rva, site.expected, site.expected};
        if (site.target == Target::RemoveCountGuard) {
            // kr-812 sub_694130 already bounds the loop by its table end; the seven-item index check is redundant.
            std::fill(write.replacement.begin(), write.replacement.end(), uint8_t{0x90});
        } else {
            dword(write.expected, site.operand_offset,
                add(add(image_base, descriptor.table_rva), table_offset(site.target, old_size)));
            dword(write.replacement, site.operand_offset, add(table_address, table_offset(site.target, new_size)));
        }
        writes.push_back(std::move(write));
    }
    return writes;
}
}

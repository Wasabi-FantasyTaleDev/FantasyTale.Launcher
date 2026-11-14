#pragma once
#include <cstddef>
#include <cstdint>
#include <optional>
#include <stdexcept>

namespace game_controls {

// A selection group whose buttons do not map to values by position: the form keeps a vector of fixed-size records,
// each pairing a button ArrayIndex with the value the form stores when that button is chosen (kr-1256 sub_5C9070
// matches btnGameMode keys with record +0 and selects record +4; kr-1256 sub_5C9CA0 finds the current record by it).
struct SelectionRecords {
    uint32_t size = 0, key_offset = 0, value_offset = 0;
    bool valid() const { return size >= 8 && size % 4 == 0 && key_offset % 4 == 0 && value_offset % 4 == 0
        && key_offset <= size - 4 && value_offset <= size - 4 && key_offset != value_offset; }
};

// Returns the value of the first record whose key equals `key`, or nothing when no record has it. `read` reads one
// signed 32-bit integer and throws when the memory is unavailable. A vector that is not a whole number of records or
// that exceeds `limit` records is a corrupt form, not an empty selection.
template<class Read>
std::optional<int32_t> record_value(uintptr_t begin, uintptr_t end, const SelectionRecords& layout, int32_t key, size_t limit, Read read) {
    if (!layout.valid()) throw std::invalid_argument("Invalid native selection record layout");
    if (end < begin || (end - begin) % layout.size || (end - begin) / layout.size > limit)
        throw std::runtime_error("Invalid native selection records");
    for (auto record = begin; record < end; record += layout.size)
        if (read(record + layout.key_offset) == key) return read(record + layout.value_offset);
    return std::nullopt;
}

// A selection group whose form stores no chosen value: the form keeps an ordered map from each button's ArrayIndex to
// the button and marks the choice by disabling it. cn-406 sub_7711C0 disables the clicked btnGameMode and enables the
// others; cn-406 sub_770E80 reports the first member, in key order, whose enabled byte is clear. Every map node starts
// with its left, parent and right links (+0, +4, +8); the header node's left link is the smallest key, and the header
// and every leaf link carry a set nil byte.
struct SelectionMembers {
    uint32_t key_offset = 0, control_offset = 0, nil_offset = 0;
    bool valid() const { return key_offset >= 12 && control_offset >= 12 && nil_offset >= 12 && key_offset % 4 == 0
        && control_offset % 4 == 0 && key_offset != control_offset && nil_offset != key_offset && nil_offset != control_offset
        && (nil_offset < key_offset || nil_offset >= key_offset + 4) && (nil_offset < control_offset || nil_offset >= control_offset + 4); }
};

// Returns the key of the first member, in key order, whose control has a clear byte at `enabled_offset`, or nothing when
// every member is enabled. `pointer` reads one link or control pointer, `byte` one byte and `integer` one signed 32-bit
// key; each throws when the memory is unavailable. A walk longer than `limit` steps, a null link or a null member is a
// corrupt form, not an empty selection.
template<class Pointer, class Byte, class Integer>
std::optional<int32_t> disabled_member(uintptr_t header, const SelectionMembers& layout, uint32_t enabled_offset, size_t limit,
    Pointer pointer, Byte byte, Integer integer) {
    if (!layout.valid()) throw std::invalid_argument("Invalid native selection member layout");
    size_t steps = 0;
    const auto step = [&](uintptr_t next) {
        if (!next || ++steps > limit) throw std::runtime_error("Invalid native selection members");
        return next;
    };
    auto node = step(pointer(header));
    while (node != header) {
        if (byte(node + layout.nil_offset)) throw std::runtime_error("Invalid native selection members");
        const auto control = pointer(node + layout.control_offset);
        if (!control) throw std::runtime_error("Invalid native selection members");
        if (!byte(control + enabled_offset)) return integer(node + layout.key_offset);
        if (const auto right = step(pointer(node + 8)); !byte(right + layout.nil_offset)) {
            node = right;
            for (auto left = step(pointer(node)); !byte(left + layout.nil_offset); left = step(pointer(node))) node = left;
        } else {
            auto parent = step(pointer(node + 4));
            while (!byte(parent + layout.nil_offset) && node == pointer(parent + 8)) { node = parent; parent = step(pointer(node + 4)); }
            node = parent;
        }
    }
    return std::nullopt;
}

// The same selection kept in a tree whose header node lies inside the form instead of behind a pointer. cn-324
// sub_743F30 walks DlgCreateRoom's tree from the header's left link (+8, the smallest key) to the header itself and
// returns the first member whose control is disabled. Every node holds its parent, left and right links at +4, +8 and
// +12; a missing child is a null link, and climbing past the root reaches the header (cn-324 sub_935340).
struct InlineSelectionMembers {
    uint32_t key_offset = 0, control_offset = 0;
    bool valid() const { return key_offset >= 16 && control_offset >= 16 && key_offset % 4 == 0 && control_offset % 4 == 0
        && key_offset != control_offset; }
};

// Returns the key of the first member, in key order, whose control has a clear byte at `enabled_offset`, or nothing when
// the tree is empty or every member is enabled. `header` is the address of the header node itself. `pointer` and
// `integer` read one link and one signed key and throw when the memory is unavailable. A walk longer than `limit`
// steps, a null link where the tree needs one or a null member is a corrupt form, not an empty selection.
template<class Pointer, class Byte, class Integer>
std::optional<int32_t> disabled_inline_member(uintptr_t header, const InlineSelectionMembers& layout, uint32_t enabled_offset, size_t limit,
    Pointer pointer, Byte byte, Integer integer) {
    if (!layout.valid()) throw std::invalid_argument("Invalid native selection member layout");
    size_t steps = 0;
    const auto step = [&](uintptr_t next) {
        if (!next || ++steps > limit) throw std::runtime_error("Invalid native selection members");
        return next;
    };
    auto node = step(pointer(header + 8));
    while (node != header) {
        const auto control = pointer(node + layout.control_offset);
        if (!control) throw std::runtime_error("Invalid native selection members");
        if (!byte(control + enabled_offset)) return integer(node + layout.key_offset);
        if (auto right = pointer(node + 12)) {
            node = step(right);
            for (auto left = pointer(node + 8); left; left = pointer(node + 8)) node = step(left);
        } else {
            auto parent = step(pointer(node + 4));
            while (parent != header && node == pointer(parent + 12)) { node = parent; parent = step(pointer(node + 4)); }
            node = parent;
        }
    }
    return std::nullopt;
}

}

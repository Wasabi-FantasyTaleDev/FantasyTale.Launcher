#include "check.h"
#include "../../games/BubbleFighter/native/game-controls/ui_service.h"
#include "../../games/BubbleFighter/native/game-controls/presentation.h"
#include "../../games/BubbleFighter/native/game-controls/native_calls.h"
#include "../../games/BubbleFighter/native/game-controls/control_registry.h"
#include "../../games/BubbleFighter/native/game-controls/signature_readiness.h"
#include "../../games/BubbleFighter/native/game-controls/selection_records.h"
#include <array>
#include <stdexcept>

using namespace game_controls;
namespace {
using native_calls::BorrowedString;
std::wstring copied(const BorrowedString* value) {
    return {value->capacity < 8 ? value->storage.small : value->storage.data, value->length};
}
struct NativeArguments {
    uintptr_t instance = 0;
    std::wstring title, text;
    int buttons = 0, icon = 0, center_x = 0, center_y = 0;
    uint32_t color = 0;
} arguments;
unsigned legacy_releases = 0;
// kr-1256 DlgCreateRoom keeps 56-byte mode records: +0 the btnGameMode ArrayIndex, +4 the game mode.
void selection_record_tests() {
    struct Record { int32_t key, value; uint8_t rest[48]; };
    static_assert(sizeof(Record) == 56);
    const Record records[] = {{0, 1, {}}, {1, 3, {}}, {6, 101, {}}, {24, 40, {}}};
    const auto begin = reinterpret_cast<uintptr_t>(records), end = begin + sizeof(records);
    const game_controls::SelectionRecords layout{56, 0, 4};
    const auto read = [](uintptr_t location) { return *reinterpret_cast<const int32_t*>(location); };
    CHECK(layout.valid());
    CHECK(game_controls::record_value(begin, end, layout, 6, 64, read) == 101);
    CHECK(game_controls::record_value(begin, end, layout, 0, 64, read) == 1);
    CHECK(game_controls::record_value(begin, end, layout, 24, 64, read) == 40);
    CHECK(!game_controls::record_value(begin, end, layout, 7, 64, read).has_value());
    CHECK(!game_controls::record_value(begin, begin, layout, 0, 64, read).has_value());
    // The value and key may swap places; the layout says which field is which.
    CHECK(game_controls::record_value(begin, end, game_controls::SelectionRecords{56, 4, 0}, 101, 64, read) == 6);
    const auto fails = [&](uintptr_t first, uintptr_t last, game_controls::SelectionRecords shape, size_t limit) {
        try { (void)game_controls::record_value(first, last, shape, 0, limit, read); return false; }
        catch (const std::exception&) { return true; }
    };
    CHECK(fails(begin, end - 4, layout, 64));    // not a whole number of records
    CHECK(fails(end, begin, layout, 64));        // reversed vector
    CHECK(fails(begin, end, layout, 3));         // more records than the tree limit
    CHECK(fails(begin, end, {56, 0, 0}, 64));    // key and value cannot share a field
    CHECK(fails(begin, end, {56, 54, 4}, 64));   // a field must fit inside its record
    CHECK(fails(begin, end, {6, 0, 4}, 64));     // records hold at least two fields
    CHECK(!game_controls::SelectionRecords{}.valid());
}

// cn-406 DlgCreateRoom maps each btnGameMode ArrayIndex to its button in an ordered map (node +0x0C key, +0x10 button,
// +0x15 nil byte); the selection is the first disabled member in key order.
void selection_member_tests() {
    constexpr uint32_t base = 0x1000, node_size = 24, enabled = 4;
    std::vector<uint8_t> memory(1024);
    const auto at = [&](uintptr_t location, size_t size) -> uint8_t* {
        if (location < base || location - base + size > memory.size()) throw std::runtime_error("unmapped");
        return memory.data() + (location - base);
    };
    const auto put = [&](uintptr_t location, uint32_t value) { std::memcpy(at(location, 4), &value, 4); };
    const auto pointer = [&](uintptr_t location) { uint32_t value; std::memcpy(&value, at(location, 4), 4); return static_cast<uintptr_t>(value); };
    const auto byte = [&](uintptr_t location) { return *at(location, 1); };
    const auto integer = [&](uintptr_t location) { int32_t value; std::memcpy(&value, at(location, 4), 4); return value; };
    const uintptr_t header = base;
    const auto node = [&](int key) { return static_cast<uintptr_t>(base + node_size * (key + 1)); };
    const auto control = [&](int key) { return static_cast<uintptr_t>(base + 512 + 8 * key); };
    // Keys 0..4: root 2, its left 1 with left 0, its right 3 with right 4.
    const auto link = [&](int key, uintptr_t left, uintptr_t parent, uintptr_t right) {
        put(node(key), static_cast<uint32_t>(left)); put(node(key) + 4, static_cast<uint32_t>(parent)); put(node(key) + 8, static_cast<uint32_t>(right));
        put(node(key) + 12, static_cast<uint32_t>(key)); put(node(key) + 16, static_cast<uint32_t>(control(key)));
        memory[control(key) + enabled - base] = 1;
    };
    link(2, node(1), header, node(3));
    link(1, node(0), node(2), header);
    link(0, header, node(1), header);
    link(3, header, node(2), node(4));
    link(4, header, node(3), header);
    put(header, static_cast<uint32_t>(node(0))); put(header + 4, static_cast<uint32_t>(node(2))); put(header + 8, static_cast<uint32_t>(node(4)));
    memory[header + 21 - base] = 1;
    const game_controls::SelectionMembers layout{12, 16, 21};
    const auto chosen = [&](size_t limit = 64) { return game_controls::disabled_member(header, layout, enabled, limit, pointer, byte, integer); };
    CHECK(layout.valid());
    CHECK(!chosen().has_value());
    memory[control(3) + enabled - base] = 0;
    CHECK(chosen() == 3);
    // The client reports the lowest disabled key, so a second disabled member does not change the choice.
    memory[control(4) + enabled - base] = 0;
    CHECK(chosen() == 3);
    memory[control(0) + enabled - base] = 0;
    CHECK(chosen() == 0);
    memory[control(0) + enabled - base] = 1; memory[control(3) + enabled - base] = 1;
    CHECK(chosen() == 4);
    const auto fails = [&](auto call) {
        try { (void)call(); return false; }
        catch (const std::exception&) { return true; }
    };
    CHECK(fails([&] { return chosen(3); }));              // longer than the tree limit
    put(node(3) + 16, 0);
    CHECK(fails([&] { return chosen(); }));               // a member without a control
    put(node(3) + 16, static_cast<uint32_t>(control(3)));
    put(node(1) + 8, 0);
    CHECK(fails([&] { return chosen(); }));               // a null link
    put(node(1) + 8, static_cast<uint32_t>(header));
    put(header, static_cast<uint32_t>(header));
    CHECK(!chosen().has_value());                         // an empty map
    CHECK(fails([&] { return game_controls::disabled_member(header, {12, 12, 21}, enabled, 64, pointer, byte, integer); }));
    CHECK(fails([&] { return game_controls::disabled_member(header, {12, 16, 13}, enabled, 64, pointer, byte, integer); }));
    CHECK(!game_controls::SelectionMembers{}.valid());
}

// cn-324 DlgCreateRoom keeps the header node itself at +352 (parent/root +4, left/smallest +8, right/largest +12); each
// node holds parent +4, left +8, right +12, key +16 and button +20, and a missing child is a null link.
void inline_selection_member_tests() {
    constexpr uint32_t base = 0x1000, node_size = 24, enabled = 4;
    std::vector<uint8_t> memory(1024);
    const auto at = [&](uintptr_t location, size_t size) -> uint8_t* {
        if (location < base || location - base + size > memory.size()) throw std::runtime_error("unmapped");
        return memory.data() + (location - base);
    };
    const auto put = [&](uintptr_t location, uint32_t value) { std::memcpy(at(location, 4), &value, 4); };
    const auto pointer = [&](uintptr_t location) { uint32_t value; std::memcpy(&value, at(location, 4), 4); return static_cast<uintptr_t>(value); };
    const auto byte = [&](uintptr_t location) { return *at(location, 1); };
    const auto integer = [&](uintptr_t location) { int32_t value; std::memcpy(&value, at(location, 4), 4); return value; };
    const uintptr_t header = base;
    const auto node = [&](int key) { return static_cast<uintptr_t>(base + node_size * (key + 1)); };
    const auto control = [&](int key) { return static_cast<uintptr_t>(base + 512 + 8 * key); };
    // Keys 0..4: root 2, its left 1 with left 0, its right 3 with right 4.
    const auto link = [&](int key, uintptr_t parent, uintptr_t left, uintptr_t right) {
        put(node(key) + 4, static_cast<uint32_t>(parent)); put(node(key) + 8, static_cast<uint32_t>(left)); put(node(key) + 12, static_cast<uint32_t>(right));
        put(node(key) + 16, static_cast<uint32_t>(key)); put(node(key) + 20, static_cast<uint32_t>(control(key)));
        memory[control(key) + enabled - base] = 1;
    };
    link(2, header, node(1), node(3));
    link(1, node(2), node(0), 0);
    link(0, node(1), 0, 0);
    link(3, node(2), 0, node(4));
    link(4, node(3), 0, 0);
    put(header + 4, static_cast<uint32_t>(node(2))); put(header + 8, static_cast<uint32_t>(node(0))); put(header + 12, static_cast<uint32_t>(node(4)));
    const game_controls::InlineSelectionMembers layout{16, 20};
    const auto chosen = [&](size_t limit = 64) { return game_controls::disabled_inline_member(header, layout, enabled, limit, pointer, byte, integer); };
    const auto fails = [&](auto call) {
        try { (void)call(); return false; }
        catch (const std::exception&) { return true; }
    };
    CHECK(layout.valid());
    CHECK(!chosen().has_value());
    memory[control(4) + enabled - base] = 0;
    CHECK(chosen() == 4);                                 // the largest key is reached through the right spine
    memory[control(3) + enabled - base] = 0;
    CHECK(chosen() == 3);
    memory[control(0) + enabled - base] = 0;
    CHECK(chosen() == 0);
    memory[control(0) + enabled - base] = 1; memory[control(3) + enabled - base] = 1; memory[control(4) + enabled - base] = 1;
    memory[control(2) + enabled - base] = 0;
    CHECK(chosen() == 2);                                 // climbing from the left subtree reaches the root
    memory[control(2) + enabled - base] = 1;
    // The map walker for cn-406 cannot read this tree: it follows the header's +0 (the colour, zero) as its first link.
    CHECK(fails([&] { return game_controls::disabled_member(header, game_controls::SelectionMembers{16, 20, 24}, enabled, 64, pointer, byte, integer); }));
    CHECK(fails([&] { return chosen(3); }));              // longer than the tree limit
    put(node(3) + 20, 0);
    CHECK(fails([&] { return chosen(); }));               // a member without a control
    put(node(3) + 20, static_cast<uint32_t>(control(3)));
    put(node(4) + 4, 0);
    CHECK(fails([&] { return chosen(); }));               // a null parent while climbing
    put(node(4) + 4, static_cast<uint32_t>(node(3)));
    put(header + 8, static_cast<uint32_t>(header));
    CHECK(!chosen().has_value());                         // an empty tree points its left link at the header
    put(header + 8, 0);
    CHECK(fails([&] { return chosen(); }));               // a header without a smallest link
    CHECK(fails([&] { return game_controls::disabled_inline_member(header, {16, 16}, enabled, 64, pointer, byte, integer); }));
    CHECK(fails([&] { return game_controls::disabled_inline_member(header, {12, 20}, enabled, 64, pointer, byte, integer); }));
    CHECK(!game_controls::InlineSelectionMembers{}.valid());
}

// cn-324 sub_401B10 keeps the length at data -20 and the characters at data.
void legacy_string_tests() {
    struct Header { int32_t length, capacity, references, unknown_12, unknown_16; wchar_t data[8]; } stored{3, 7, 1, 0, 0, {L'a', L'b', L'c', 0}};
    const auto data = reinterpret_cast<uintptr_t>(stored.data);
    const auto read = [](uintptr_t location, void* output, uint32_t size) { std::memcpy(output, reinterpret_cast<const void*>(location), size); return true; };
    CHECK(native_calls::legacy_string(data, 16, read) == L"abc");
    stored.length = 0;
    CHECK(native_calls::legacy_string(data, 16, read).empty());
    const auto fails = [&](auto call) {
        try { (void)call(); return false; }
        catch (const std::exception&) { return true; }
    };
    stored.length = 17;
    CHECK(fails([&] { return native_calls::legacy_string(data, 16, read); }));   // longer than the text limit
    stored.length = -1;
    CHECK(fails([&] { return native_calls::legacy_string(data, 16, read); }));   // a corrupt header
    CHECK(fails([&] { return native_calls::legacy_string(0, 16, read); }));      // no string at all
    stored.length = 3;
    CHECK(fails([&] { return native_calls::legacy_string(data, 16, [](uintptr_t, void*, uint32_t) { return false; }); }));
}

void signature_readiness_tests() {
    uint64_t now = 100;
    std::vector<uint32_t> sleeps;
    const auto clock = [&] { return now; };
    const auto pause = [&](uint32_t milliseconds) { sleeps.push_back(milliseconds); now += milliseconds; };
    CHECK(await_signatures(25, 10, [] { return true; }, clock, pause));
    CHECK(now == 100 && sleeps.empty());
    CHECK(await_signatures(25, 10, [&] { return now >= 120; }, clock, pause));
    CHECK(now == 120 && sleeps == std::vector<uint32_t>({10, 10}));
    sleeps.clear();
    CHECK(!await_signatures(25, 10, [] { return false; }, clock, pause));
    CHECK(now == 145 && sleeps == std::vector<uint32_t>({10, 10, 5}));
    sleeps.clear();
    CHECK(await_signatures(5, 100, [&] { return now == 150; }, clock, pause));
    CHECK(now == 150 && sleeps == std::vector<uint32_t>({5}));
    sleeps.clear();
    CHECK(!await_signatures(5, 100, [&] { now += 6; return false; }, clock, pause));
    CHECK(sleeps.empty());
    CHECK(!await_signatures(5, 100, [&] { now += 6; return true; }, clock, pause));
    bool awakened = false;
    unsigned attempts = 0;
    CHECK(!await_signatures(5, 100, [&] { ++attempts; return awakened; }, clock,
        [&](uint32_t milliseconds) { now += milliseconds + 1; awakened = true; }));
    CHECK(attempts == 1);
    for (const auto limits : {std::pair{0u, 10u}, std::pair{10u, 0u}}) {
        bool rejected = false;
        try { (void)await_signatures(limits.first, limits.second, [] { return true; }, clock, pause); }
        catch (const std::invalid_argument&) { rejected = true; }
        CHECK(rejected);
    }
}
void legacy_readiness_tests() {
    std::array<uint8_t, 512> memory{};
    constexpr uintptr_t application = 0x1000, manager = 0x1100;
    const native_calls::LegacyUiLayout layout{24, 56, 64, 4};
    const auto write = [&](uintptr_t address, uintptr_t value) {
        std::memcpy(memory.data() + address - application, &value, sizeof(value));
    };
    bool deny_exit_read = false;
    const auto read = [&](uintptr_t address, void* output, uint32_t size) {
        if ((deny_exit_read && address == application + 4) || address < application
            || address - application > memory.size() || size > memory.size() - (address - application)) return false;
        std::memcpy(output, memory.data() + address - application, size);
        return true;
    };
    CHECK(!native_calls::legacy_ui_ready(application, manager, layout, read));
    write(manager + 24, 0x3000);
    CHECK(!native_calls::legacy_ui_ready(application, manager, layout, read));
    write(application + 56, 0x4000);
    CHECK(native_calls::legacy_ui_ready(application, manager, layout, read));
    write(application + 64, 0x5000);
    CHECK(!native_calls::legacy_ui_ready(application, manager, layout, read));
    write(application + 64, 0);
    memory[4] = 1;
    CHECK(!native_calls::legacy_ui_ready(application, manager, layout, read));
    memory[4] = 0;
    deny_exit_read = true;
    CHECK(!native_calls::legacy_ui_ready(application, manager, layout, read));
    deny_exit_read = false;
    CHECK(native_calls::legacy_ui_ready(application, manager, layout, read));
    CHECK(!native_calls::legacy_ui_ready(0, manager, layout, read));
    CHECK(!native_calls::legacy_ui_ready(application, UINTPTR_MAX - 8, layout, read));
    auto wrapped = layout;
    wrapped.pending_form_offset = UINT32_MAX;
    CHECK(!native_calls::legacy_ui_ready(application, manager, wrapped, read));
}

void __fastcall detach_dialog(void* dialog, void*, uintptr_t parent) {
    CHECK(dialog == &arguments && parent == 0);
    arguments.instance = 1;
}
void* __fastcall legacy_string_create(const wchar_t** value, void*, const wchar_t* text) {
    *value = text;
    return value;
}
void __fastcall legacy_string_release(const wchar_t** value, void*) {
    CHECK(*value != nullptr);
    ++legacy_releases;
}
void* __stdcall legacy_stack_construct(void* instance, const wchar_t* const* title, const wchar_t* const* body, int buttons, int icon) {
    arguments.instance = reinterpret_cast<uintptr_t>(instance);
    arguments.title = *title; arguments.text = *body;
    arguments.buttons = buttons; arguments.icon = icon;
    return instance;
}
void* __fastcall legacy_coordinate_construct(int x, int y, void* instance, const wchar_t* const* title,
    const wchar_t* const* body, int buttons, int icon) {
    CHECK(x == 0x8000 && y == 0x8000);
    return legacy_stack_construct(instance, title, body, buttons, icon);
}
void* __stdcall stack_construct(void* instance, const BorrowedString* title, const BorrowedString* body, int buttons, int icon) {
    arguments.instance = reinterpret_cast<uintptr_t>(instance);
    arguments.title = copied(title); arguments.text = copied(body);
    arguments.buttons = buttons; arguments.icon = icon;
    return instance;
}
void* __fastcall coordinate_construct(int x, int y, void* instance, const BorrowedString* title, const BorrowedString* body, int buttons, int icon) {
    arguments.center_x = x; arguments.center_y = y;
    return stack_construct(instance, title, body, buttons, icon);
}
// The unused EDX argument models a thiscall callee without a compiler-specific member-pointer cast.
void* __fastcall member_construct(void* instance, void*, const BorrowedString* title, const BorrowedString* body, int buttons, int icon) {
    return stack_construct(instance, title, body, buttons, icon);
}
int __fastcall three_flag_modal(void* instance, void*, int first, int second, int third) {
    CHECK(reinterpret_cast<uintptr_t>(instance) == arguments.instance);
    CHECK(first == 0 && second == 1 && third == 1);
    return 256;
}
int __fastcall four_flag_modal(void* instance, void* unused, int first, int second, int third, int fourth) {
    CHECK(fourth == 0);
    return three_flag_modal(instance, unused, first, second, third);
}
int __fastcall interrupted_modal(void* instance, void* unused, uint8_t* interrupted, int first, int second, int third, int fourth) {
    *interrupted = 1;
    return four_flag_modal(instance, unused, first, second, third, fourth);
}
void __cdecl notification(const BorrowedString* body) { arguments.text = copied(body); }
void __fastcall member_notification(const BorrowedString* body, void*) { notification(body); }
void __fastcall owned_notification(intptr_t duration, void*, uintptr_t instance, const BorrowedString* body,
    const BorrowedString* sound, intptr_t first, intptr_t second) {
    CHECK(duration == -1 && first == -1 && second == -1 && copied(sound).empty());
    arguments.instance = instance; notification(body);
}
void __stdcall styled_notification(uintptr_t instance, const BorrowedString* body, const BorrowedString* sound,
    intptr_t duration, intptr_t first, intptr_t second, int style) {
    CHECK(style == 0);
    owned_notification(duration, nullptr, instance, body, sound, first, second);
}
void __fastcall local_chat(uintptr_t instance, void*, const BorrowedString* title, uint32_t color, const BorrowedString* body) {
    arguments.instance = instance; arguments.title = copied(title); arguments.color = color; notification(body);
}
void __fastcall extended_chat(uintptr_t instance, void* unused, const BorrowedString* title, uint32_t color,
    const BorrowedString* body, int reserved) {
    CHECK(reserved == 0);
    local_chat(instance, unused, title, color, body);
}
void __fastcall pointer_chat(uintptr_t instance, void*, uint32_t color, const wchar_t* title, uint32_t title_size,
    const wchar_t* body, uint32_t body_size) {
    arguments.instance = instance; arguments.title.assign(title, title_size); arguments.text.assign(body, body_size); arguments.color = color;
}
void __fastcall register_chat(uintptr_t instance, const BorrowedString* body, const BorrowedString* title, uint32_t color) {
    local_chat(instance, nullptr, title, color, body);
}
__declspec(naked) void esi_chat() {
    __asm {
        push [esp + 8]
        push [esp + 8]
        mov ecx, esi
        call register_chat
        mov esi, 12345678h
        ret 8
    }
}
uint8_t __fastcall notifying_edit(uintptr_t edit, void*, const BorrowedString* text, uint8_t quiet) {
    arguments.instance = edit; arguments.text = copied(text); arguments.buttons = quiet;
    return 1;
}
uint8_t __fastcall plain_edit(uintptr_t edit, void*, const BorrowedString* text) {
    arguments.instance = edit; arguments.text = copied(text); arguments.buttons = -1;
    return 1;
}
void* __cdecl sized_allocate(uint32_t size) {
    arguments.color = size;
    return &arguments;
}
void* __cdecl contextual_allocate(uintptr_t allocator, uint32_t size, intptr_t* context) {
    CHECK(allocator == 123 && context[0] == 0 && context[1] == -1 && context[2] == 0);
    // The native allocator may mutate its borrowed stack context during the call.
    context[0] = 42;
    return sized_allocate(size);
}
struct CapturedAnnouncement {
    uintptr_t instance = 0, end = 0;
    std::wstring text, secondary;
    uint32_t color = 0, tick = 0, duration = 0, fade = 0;
    uint8_t type = 255;
} announcement;
void __fastcall legacy_announcement(uintptr_t instance, const native_calls::AnnouncementRecord* source) {
    announcement = {instance, 0, copied(&source->text), copied(&source->secondary), source->color,
        source->tick, source->duration, source->fade, source->type};
    CHECK(source->unknown_24 == 0 && source->unknown_64 == 0);
}
__declspec(naked) void register_announcement() {
    __asm {
        mov edx, ecx
        mov ecx, ebx
        call legacy_announcement
        mov ebx, 12345678h
        ret
    }
}
void __fastcall compact_announcement(uintptr_t instance, void*, uintptr_t end, const native_calls::CompactAnnouncementRecord* source) {
    announcement = {instance, end, copied(&source->text), copied(&source->secondary), source->color,
        source->tick, source->duration, source->fade, source->type};
}
uint32_t __fastcall member_tick(const uint32_t* clock, void*) { return *clock; }
uint32_t __cdecl global_tick() { return 123456789; }
void announcement_call_tests() {
    const uint32_t clock = 876543210;
    CHECK(native_calls::announcement_tick(1, reinterpret_cast<uintptr_t>(&member_tick), reinterpret_cast<uintptr_t>(&clock)) == clock);
    CHECK(native_calls::announcement_tick(2, reinterpret_cast<uintptr_t>(&global_tick), 0) == 123456789);
    CHECK(offsetof(native_calls::AnnouncementRecord, color) == 28 && offsetof(native_calls::AnnouncementRecord, secondary) == 40);
    CHECK(offsetof(native_calls::CompactAnnouncementRecord, secondary) == 36 && offsetof(native_calls::CompactAnnouncementRecord, type) == 60);
    for (const auto* text : {L"", L"short", L"first\nsecond\r\nthird with a heap-backed native string"}) {
        std::wstring source(text), secondary = L"independently copied secondary text";
        native_calls::AnnouncementRecord legacy{native_calls::BorrowedString(source)};
        legacy.secondary = native_calls::BorrowedString(secondary);
        legacy.tick = clock;
        uintptr_t before = 0, after = 0;
        __asm mov before, ebx
        native_calls::append_announcement(1, reinterpret_cast<uintptr_t>(&register_announcement), 321, 0, &legacy);
        __asm mov after, ebx
        CHECK(before == after);
        CHECK(announcement.instance == 321 && announcement.text == source && announcement.secondary == secondary);
        CHECK(announcement.color == 0xFFFFFF00 && announcement.tick == clock && announcement.duration == 0
            && announcement.type == 1 && announcement.fade == 2000);
        native_calls::CompactAnnouncementRecord compact{native_calls::BorrowedString(source)};
        compact.secondary = native_calls::BorrowedString(secondary);
        compact.tick = 123456789;
        compact.duration = 13000;
        native_calls::append_announcement(2, reinterpret_cast<uintptr_t>(&compact_announcement), 654, 0x100044, &compact);
        CHECK(announcement.instance == 654 && announcement.end == 0x100044);
        CHECK(announcement.text == source && announcement.secondary == secondary);
        CHECK(announcement.color == 0xFFFFFF00 && announcement.tick == 123456789 && announcement.duration == 13000
            && announcement.type == 0 && announcement.fade == 2000);
        source.assign(L"changed"); secondary.clear();
        CHECK(announcement.text == text && announcement.secondary == L"independently copied secondary text");
    }
    for (const uint32_t abi : {0u, 3u}) {
        bool rejected = false;
        try { (void)native_calls::announcement_tick(abi, 0, 0); }
        catch (const std::invalid_argument&) { rejected = true; }
        CHECK(rejected);
        rejected = false;
        try { native_calls::append_announcement(abi, 0, 0, 0, nullptr); }
        catch (const std::invalid_argument&) { rejected = true; }
        CHECK(rejected);
    }
}
void native_call_tests() {
    {
        const std::wstring title = L" ", message = L"\u8981\u7ACB\u5373\u8FD4\u56DE\u5E7F\u573A\u5417\uFF1F";
        const auto create = reinterpret_cast<uintptr_t>(&legacy_string_create);
        const auto release = reinterpret_cast<uintptr_t>(&legacy_string_release);
        const native_calls::LegacyString first(create, release, title), second(create, release, message);
        int box = 0;
        CHECK(native_calls::construct_legacy_message(4, reinterpret_cast<uintptr_t>(&legacy_stack_construct), &box, first, second, 2, 0) == &box);
        CHECK(arguments.title == title && arguments.text == message && arguments.buttons == 2 && arguments.icon == 2);
        CHECK(native_calls::construct_legacy_message(5, reinterpret_cast<uintptr_t>(&legacy_coordinate_construct), &box, first, second, 2, 0x8000) == &box);
        CHECK(arguments.title == title && arguments.text == message && arguments.buttons == 2 && arguments.icon == 2);
    }
    CHECK(legacy_releases == 2);
    const std::wstring title = L"Title", body = L"first\nsecond\r\nthird";
    const BorrowedString heading(title), text(body);
    int dialog = 0;
    const uintptr_t constructors[]{reinterpret_cast<uintptr_t>(&coordinate_construct), reinterpret_cast<uintptr_t>(&member_construct), reinterpret_cast<uintptr_t>(&stack_construct)};
    const uintptr_t modals[]{reinterpret_cast<uintptr_t>(&four_flag_modal), reinterpret_cast<uintptr_t>(&interrupted_modal), reinterpret_cast<uintptr_t>(&three_flag_modal)};
    const uintptr_t notifications[]{reinterpret_cast<uintptr_t>(&notification), reinterpret_cast<uintptr_t>(&member_notification), reinterpret_cast<uintptr_t>(&owned_notification)};
    const uintptr_t chats[]{reinterpret_cast<uintptr_t>(&local_chat), reinterpret_cast<uintptr_t>(&pointer_chat), reinterpret_cast<uintptr_t>(&register_chat), reinterpret_cast<uintptr_t>(&esi_chat)};
    CHECK(native_calls::allocate(1, reinterpret_cast<uintptr_t>(&sized_allocate), 0, 768) == &arguments && arguments.color == 768);
    CHECK(native_calls::allocate(2, reinterpret_cast<uintptr_t>(&contextual_allocate), 123, 848) == &arguments && arguments.color == 848);
    for (const auto* caption : {L"", L"Long custom caption"}) {
        uintptr_t before = 0, after = 0;
        __asm mov before, esi
        native_calls::chat(4, chats[3], 987, caption, body, 0xFFAABBCC);
        __asm mov after, esi
        CHECK(before == after);
        CHECK(arguments.instance == 987 && arguments.title == caption && arguments.text == body && arguments.color == 0xFFAABBCC);
        native_calls::chat(5, reinterpret_cast<uintptr_t>(&extended_chat), 654, caption, body, 0xFF123456);
        CHECK(arguments.instance == 654 && arguments.title == caption && arguments.text == body && arguments.color == 0xFF123456);
    }
    for (uint32_t abi = 1; abi <= 3; ++abi) {
        CHECK(native_calls::construct(abi, constructors[abi - 1], &dialog, heading, text, 2, 3, 32768) == &dialog);
        CHECK(arguments.instance == reinterpret_cast<uintptr_t>(&dialog) && arguments.title == title && arguments.text == body && arguments.buttons == 2 && arguments.icon == 3);
        if (abi == 1) CHECK(arguments.center_x == 32768 && arguments.center_y == 32768);
        uint8_t interrupted = 255;
        CHECK(native_calls::modal(abi, modals[abi - 1], &dialog, interrupted) == 256);
        CHECK(interrupted == (abi == 2 ? 1 : 0));
        native_calls::notify(abi, notifications[abi - 1], 123, text);
        CHECK(arguments.text == body);
        if (abi == 3) CHECK(arguments.instance == 123);
        native_calls::chat(abi, chats[abi - 1], 456, L"", body, 0xFFAABBCC);
        CHECK(arguments.instance == 456 && arguments.title.empty() && arguments.text == body && arguments.color == 0xFFAABBCC);
        native_calls::chat(abi, chats[abi - 1], 789, L"Long custom caption", L"short", 0xFF123456);
        CHECK(arguments.instance == 789 && arguments.title == L"Long custom caption" && arguments.text == L"short" && arguments.color == 0xFF123456);
    }
    native_calls::notify(4, reinterpret_cast<uintptr_t>(&styled_notification), 321, text);
    CHECK(arguments.instance == 321 && arguments.text == body);
    // id-1809 combines the stack constructor with the four-flag modal, unlike id-400.
    CHECK(native_calls::construct(3, constructors[2], &dialog, heading, text, 1, 0, 0) == &dialog);
    uint8_t interrupted = 255;
    CHECK(native_calls::modal(1, modals[0], &dialog, interrupted) == 256 && !interrupted);
    bool rejected = false;
    try { (void)native_calls::construct(0, 0, &dialog, heading, text, 2, 0, 0); }
    catch (const std::invalid_argument&) { rejected = true; }
    CHECK(rejected);
    // Each edit setter pops its own arguments; a mismatched count leaves the stack moved.
    const uintptr_t edits[]{reinterpret_cast<uintptr_t>(&notifying_edit), reinterpret_cast<uintptr_t>(&plain_edit)};
    for (uint32_t abi = 1; abi <= 2; ++abi) {
        uintptr_t before = 0, after = 0;
        __asm mov before, esp
        native_calls::set_edit_text(abi, edits[abi - 1], 555, text);
        __asm mov after, esp
        CHECK(before == after);
        CHECK(arguments.instance == 555 && arguments.text == body && arguments.buttons == (abi == 1 ? 0 : -1));
    }
    rejected = false;
    try { native_calls::set_edit_text(3, edits[1], 555, text); }
    catch (const std::invalid_argument&) { rejected = true; }
    CHECK(rejected);
}
struct Fixture final : Adapter {
    std::vector<Completion> callbacks;
    std::vector<Request> requests;
    std::vector<std::string> callers;
    bool supported = true, ready = true, throws = false;
    std::function<void()> synchronous;
    std::vector<Capability> capabilities() override { return {{"messageBox", supported, ready, {}, {}}, {"chat", supported, ready, {}, {}}, {"notification", supported, ready, {}, {}},
        {"control.inspect", supported, ready, {}, {}}, {"control.setText", supported, ready, {}, {}}, {"control.addButton", supported, ready, {}, {}}, {"control.remove", supported, ready, {}, {}}, {"control.activate", supported, ready, {}, {}}, {"control.setEnabled", supported, ready, {}, {}}, {"control.events", supported, ready, {}, {}}}; }
    void submit(const std::string& caller, Request request, Completion complete) override {
        if (throws) throw std::runtime_error("fixture");
        callers.push_back(caller);
        requests.push_back(std::move(request));
        callbacks.push_back(std::move(complete));
        const auto task = synchronous;
        if (task) task();
    }
};
std::string message(const std::string& id, const std::string& text = "body") {
    return "action='invoke'\noperation_id='" + id + "'\nkind='messageBox'\ntext='" + text + "'\nicon='none'\n[[buttons]]\nid='ok'\nlabel='OK'\n";
}
std::string poll(const std::string& id) { return "action='result'\noperation_id='" + id + "'"; }
bool contains(const std::string& result, const char* value) { return result.find(value) != std::string::npos; }
void rejects(Service& service, const std::string& payload) {
    bool rejected = false;
    try { (void)service.handle("test.a", payload); } catch (const std::invalid_argument&) { rejected = true; }
    CHECK(rejected);
}
void control_registry_tests() {
    ControlRegistry registry(8);
    std::vector<ControlSnapshot> tree{
        {1, 1, {{}, "Ui/DlgCreateRoom.ui", "", "form", "Create room", true}},
        {2, 1, {{}, "Ui/DlgCreateRoom.ui", "edtRoomTitle", "edit", "Existing title", true}},
        {3, 1, {{}, "Ui/DlgCreateRoom.ui", "btnGameMode", "button", "Normal", true}},
        {4, 1, {{}, "Ui/DlgCreateRoom.ui", "btnGameMode", "button", "Normal", true}},
        {5, 5, {{}, "Ui/DlgOther.ui", "edtRoomTitle", "edit", "Other title", true}}};
    Request set;
    set.kind = "control.setText";
    set.target.form = "ui\\dlgcreateroom.ui";
    set.target.name = "edtRoomTitle";
    // An exact form/name selector never needs the old text to be supplied.
    auto selected = registry.select("panel.a", set, tree);
    CHECK(selected.size() == 1 && selected[0].object == 2);
    auto description = registry.describe("panel.a", selected);
    CHECK(!description[0].id.empty());
    const auto first_id = description[0].id;
    set.target = {};
    set.target.id = first_id;
    CHECK(registry.select("panel.a", set, tree).size() == 1);
    CHECK(registry.select("panel.b", set, tree).empty());
    set.target = {};
    set.target.name = "edtRoomTitle";
    CHECK(registry.select("panel.a", set, tree).size() == 2);
    set.target.text = "Other title";
    CHECK(registry.select("panel.a", set, tree).front().object == 5);
    set.target.text = "";
    CHECK(registry.select("panel.a", set, tree).empty());
    set.target = {};
    set.target.form = "Ui/DlgCreateRoom.ui";
    CHECK(registry.select("panel.a", set, tree).front().object == 1);
    set.kind = "control.inspect";
    CHECK(registry.select("panel.a", set, tree).size() == 4);
    set.kind = "control.addButton";
    CHECK(registry.select("panel.a", set, tree).size() == 1);
    set.target = {};
    set.target.name = "btnGameMode";
    set.kind = "control.setText";
    CHECK(registry.select("panel.a", set, tree).size() == 2);
    tree[2].info.array_index = 0; tree[3].info.array_index = 1;
    set.target.array_index = 0;
    CHECK(registry.select("panel.a", set, tree).size() == 1 && registry.select("panel.a", set, tree).front().object == 3);
    set.target.array_index = 1;
    CHECK(registry.select("panel.a", set, tree).front().object == 4);
    set.target.array_index = 2;
    CHECK(registry.select("panel.a", set, tree).empty());
    auto button = tree[2].info;
    CHECK(button_activation(button) == ButtonActivation::disabled);
    button.selected = true;
    CHECK(button_activation(button) == ButtonActivation::already_selected);
    button.selected = false;
    CHECK(button_activation(button) == ButtonActivation::disabled);
    button.enabled = true;
    CHECK(button_activation(button) == ButtonActivation::ready);
    button.kind = "label";
    CHECK(button_activation(button) == ButtonActivation::unsupported);
    registry.invalidate(2);
    set.target = {};
    set.target.id = first_id;
    CHECK(registry.select("panel.a", set, tree).empty());
    const auto replacement = registry.describe("panel.a", {tree[1]})[0].id;
    CHECK(replacement != first_id);
    registry.refresh({tree[0]});
    set.target.id = replacement;
    CHECK(registry.select("panel.a", set, tree).empty());
    ControlRegistry bounded(1);
    bool full = false;
    try { (void)bounded.describe("a", {tree[0], tree[1]}); } catch (const std::length_error&) { full = true; }
    CHECK(full);
    CHECK(bounded.describe("a", {tree[0]}).size() == 1);
    ControlEvents events(2, 2);
    events.reserve("a"); events.reserve("b");
    Request poll_request;
    poll_request.operation_id = "poll";
    events.click("a", "a-1");
    events.click("b", "b-1");
    const auto first = events.poll("a", poll_request);
    CHECK(first.events.size() == 1 && first.events[0].control_id == "a-1" && first.cursor == 1);
    CHECK(events.poll("a", poll_request).events.size() == 1);
    events.click("a", "a-2");
    CHECK(events.poll("a", poll_request).reason == "eventHistoryLost");
    CHECK(events.poll("b", poll_request).status == "completed");
    poll_request.after_sequence = 1;
    const auto next = events.poll("a", poll_request);
    CHECK(next.events.size() == 1 && next.events[0].sequence == 2 && next.cursor == 2);
    CHECK(events.poll("b", poll_request).events.empty());
    CHECK(events.poll("unknown", poll_request).reason == "invalidEventCursor");
    bool owners_full = false;
    try { events.reserve("c"); } catch (const std::length_error&) { owners_full = true; }
    CHECK(owners_full);
}
void control_service_tests() {
    Fixture adapter;
    Service service("fixture", adapter, {16, 32});
    const auto command = [](const char* id, const char* kind, const std::string& fields = "") {
        return std::string("action='invoke'\noperation_id='") + id + "'\nkind='" + kind + "'\n" + fields;
    };
    const auto set = command("set", "control.setText", "text=''\nmaximum_length=128\n[target]\nform='Ui/DlgCreateRoom.ui'\nname='edtRoomTitle'\n");
    CHECK(contains(service.handle("host", "owner_scope='package.a'\n" + set), "queued"));
    CHECK(adapter.callers[0] == "host:package.a");
    CHECK(adapter.requests[0].maximum_length == 128 && !adapter.requests[0].target.text);
    CHECK(contains(service.handle("host", "owner_scope='package.b'\n" + poll("set")), "operationUnavailable"));
    CHECK(contains(service.handle("another", "owner_scope='package.a'\n" + poll("set")), "operationUnavailable"));
    Result result{"set", "completed", {}, {}};
    result.controls.push_back({"control-1", "Ui/DlgCreateRoom.ui", "edtRoomTitle", "edit", "", true, 82, 6, 344, 23, 128});
    adapter.callbacks[0](result);
    const auto completed = service.handle("host", "owner_scope='package.a'\n" + poll("set"));
    CHECK(contains(completed, "maximumLength") && contains(completed, "edtRoomTitle") && contains(completed, "completed"));
    const auto auto_set = command("search", "control.setText", "text='Replacement'\n[target]\ntext='Current'\n");
    CHECK(contains(service.handle("host", auto_set), "queued"));
    CHECK(adapter.requests.back().target.text == "Current");
    adapter.callbacks.back()({"search", "ambiguous", {}, "multipleControls"});
    CHECK(contains(service.handle("host", poll("search")), "ambiguous"));
    CHECK(contains(service.handle("host", command("events", "control.events", "after_sequence=12\n")), "queued"));
    Result events{"events", "completed", {}, {}};
    events.events.push_back({13, "control-1", "click"}); events.cursor = 13;
    adapter.callbacks.back()(events);
    const auto polled = service.handle("host", poll("events"));
    CHECK(contains(polled, "controlId") && contains(polled, "cursor") && contains(polled, "click"));
    CHECK(contains(service.handle("host", command("add", "control.addButton", "text='Modes'\n[target]\nform='Ui/DlgCreateRoom.ui'\n[definition]\nname='modeExtension'\nx=300\ny=623\nwidth=100\nheight=25\n")), "queued"));
    CHECK(adapter.requests.back().definition->width == 100);
    CHECK(contains(service.handle("host", command("add-by-id", "control.addButton", "text='Modes'\n[target]\nid='control-form'\n[definition]\nname='modeExtension'\nx=300\ny=621\nwidth=100\nheight=25\n")), "queued"));
    CHECK(adapter.requests.back().target.form.empty() && adapter.requests.back().target.id == "control-form");
    CHECK(contains(service.handle("host", command("activate", "control.activate", "[target]\nform='Ui/DlgCreateRoom.ui'\nname='btnGameMode'\narray_index=0\n")), "queued"));
    CHECK(adapter.requests.back().target.array_index == 0);
    // The service accepts the enabled flag it parses: Mode Extension's look of an unselected mode depends on it.
    CHECK(contains(service.handle("host", command("enable", "control.setEnabled", "enabled=true\n[target]\nid='control-mode'\n")), "queued"));
    CHECK(adapter.requests.back().kind == "control.setEnabled" && adapter.requests.back().enabled == true);
    CHECK(contains(service.handle("host", command("disable", "control.setEnabled", "enabled=false\n[target]\nid='control-mode'\n")), "queued"));
    CHECK(adapter.requests.back().enabled == false);
    // A re-enabled mode button is still the selection: its value and selected state travel together, and a reply with the
    // value alone is refused instead of reaching the launcher as a snapshot it rejects.
    ControlInfo toggled{"control-mode", "Ui/DlgCreateRoom.ui", "btnGameMode", "button", "", true};
    toggled.array_index = 0; toggled.selection_value = 1; toggled.selected = true;
    Result disabled{"disable", "completed", {}, {}}; disabled.controls.push_back(toggled);
    adapter.callbacks.back()(disabled); adapter.callbacks.pop_back();
    toggled.enabled = true; toggled.selected.reset();
    Result torn{"enable", "completed", {}, {}}; torn.controls.push_back(toggled);
    adapter.callbacks.back()(torn); adapter.callbacks.pop_back();
    const auto disabled_reply = service.handle("host", poll("disable"));
    CHECK(contains(disabled_reply, "completed") && contains(disabled_reply, "\"selected\""));
    CHECK(contains(service.handle("host", poll("enable")), "invalidAdapterResult"));
    Result activation{"activate", "completed", {}, "alreadySelected"};
    ControlInfo mode{"control-mode", "Ui/DlgCreateRoom.ui", "btnGameMode", "button", "", true};
    mode.enabled = false; mode.array_index = 0; mode.selection_value = 1; mode.selected = true;
    activation.controls.push_back(mode);
    adapter.callbacks.back()(activation);
    const auto activated = service.handle("host", poll("activate"));
    CHECK(contains(activated, "arrayIndex") && contains(activated, "selectionValue") && contains(activated, "selected") && contains(activated, "enabled") && contains(activated, "alreadySelected"));
    CHECK(contains(service.handle("host", command("inspect-mode", "control.inspect", "[target]\nname='btnGameMode'\narray_index=0\n")), "queued"));
    mode.selected = false;
    Result inspected_mode{"inspect-mode", "completed", {}, {}};
    inspected_mode.controls.push_back(mode);
    adapter.callbacks.back()(inspected_mode);
    const auto unselected = service.handle("host", poll("inspect-mode"));
    const auto selected_key = unselected.find("\"selected\"");
    CHECK(selected_key != std::string::npos && unselected.compare(unselected.find_first_not_of(" :\t\r\n", selected_key + 10), 5, "false") == 0);
    rejects(service, command("bad", "control.setText"));
    rejects(service, command("bad", "control.setText", "text='unfiltered'"));
    rejects(service, command("bad", "control.remove"));
    rejects(service, command("bad", "control.activate"));
    rejects(service, command("bad", "control.activate", "text='unexpected'\n[target]\nname='btnGameMode'\narray_index=0"));
    rejects(service, command("bad", "control.activate", "[target]\narray_index=-1"));
    rejects(service, command("bad", "control.activate", "[target]\narray_index=0"));
    rejects(service, command("bad", "control.inspect", "text='unexpected'"));
    rejects(service, command("bad", "control.inspect", "maximum_length=50"));
    rejects(service, command("bad", "control.setEnabled", "[target]\nid='control-mode'"));
    rejects(service, command("bad", "control.setEnabled", "enabled='yes'\n[target]\nid='control-mode'"));
    rejects(service, command("bad", "control.setEnabled", "enabled=true\ntext='x'\n[target]\nid='control-mode'"));
    rejects(service, command("bad", "control.inspect", "enabled=true"));
    rejects(service, command("bad", "control.setText", "text='x'\nmaximum_length=0"));
    rejects(service, command("bad", "control.inspect", "[target]\nform='../Dlg.ui'"));
    rejects(service, command("bad", "control.events", "after_sequence=-1"));
    rejects(service, command("bad", "control.events", "[target]\nid='control-1'"));
    rejects(service, "owner_scope='bad:scope'\n" + command("bad", "control.inspect"));
    rejects(service, command("bad", "control.addButton", "text='x'"));
    rejects(service, command("bad", "notification", "[target]\nname='native'"));
}
}
int main() {
    selection_record_tests();
    selection_member_tests();
    inline_selection_member_tests();
    legacy_string_tests();
    signature_readiness_tests();
    control_registry_tests();
    control_service_tests();
    legacy_readiness_tests();
    native_calls::detach_legacy_message(reinterpret_cast<uintptr_t>(&detach_dialog), &arguments);
    CHECK(arguments.instance == 1);
    native_call_tests();
    announcement_call_tests();
    CHECK(native_color("#123456AB") == 0xAB123456);
    CHECK(native_color("#4DA6FFFF") == 0xFF4DA6FF);
    CHECK(native_color("#FFFFFF") == 0xFFFFFFFF);
    CHECK(native_color("#00aaff") == 0xFF00AAFF);
    const auto joined = uniform_chat({{"First ", "#00aaFFFF"}, {"second", "#00AAFFFF"}});
    CHECK(joined && joined->text == "First second" && joined->color == 0xFF00AAFF);
    CHECK(!uniform_chat({{"First", "#00AAFFFF"}, {"second", "#FFFFFFFF"}}));
    CHECK(!uniform_chat({}));
    {
        Fixture presentation;
        Service inputs("fixture", presentation, {8, 8});
        const std::string blank = "action='invoke'\noperation_id='blank'\nkind='messageBox'\n[[buttons]]\nid='ok'\nlabel=''\n[[buttons]]\nid='cancel'\nlabel='   '\n";
        CHECK(contains(inputs.handle("test.a", blank), "queued"));
        CHECK(presentation.requests.back().buttons[0].label.empty());
        CHECK(presentation.requests.back().buttons[1].label == "   ");
        const std::string chat = "action='invoke'\noperation_id='chat'\nkind='chat'\nchat_caption='Custom source'\n[[runs]]\ntext='body'\ncolor='#FFFFFF'\n";
        CHECK(contains(inputs.handle("test.a", chat), "queued"));
        CHECK(presentation.requests.back().chat_caption == "Custom source");
        CHECK(contains(inputs.handle("test.a", "action='invoke'\noperation_id='notice'\nkind='notification'\ntext=\"first\\nsecond\\r\\nthird\""), "queued"));
        CHECK(presentation.requests.back().text == "first\nsecond\r\nthird");
        rejects(inputs, "action='invoke'\noperation_id='bad'\nkind='notification'\nchat_caption='invalid'");
        rejects(inputs, "action='invoke'\noperation_id='bad'\nkind='messageBox'\n[[buttons]]\nid='ok'");
        auto changed = chat;
        changed.replace(changed.find("Custom source"), 13, "Changed source");
        CHECK(contains(inputs.handle("test.a", changed), "operationConflict"));
    }
    Fixture adapter;
    Service service("kr-812", adapter, {2, 3});
    CHECK(contains(service.handle("test.a", "action='capabilities'"), "kr-812"));
    rejects(service, "not toml");
    rejects(service, "action='capabilities'\nextra=1");
    rejects(service, message("a") + "extra=true\n");
    rejects(service, message("bad id"));
    rejects(service, message("a") + "[[buttons]]\nid='ok'\nlabel='Duplicate'");
    rejects(service, "action='invoke'\noperation_id='a'\nkind='chat'\n[[runs]]\ntext='hi'\ncolor='#GG0000FF'");
    CHECK(adapter.callbacks.empty());
    CHECK(contains(service.handle("test.a", message("a")), "queued"));
    CHECK(contains(service.handle("test.a", message("a")), "queued"));
    CHECK(adapter.callbacks.size() == 1);
    CHECK(contains(service.handle("test.a", message("a", "changed")), "operationConflict"));
    CHECK(contains(service.handle("test.b", poll("a")), "operationUnavailable"));
    CHECK(contains(service.handle("test.b", message("a")), "queued"));
    CHECK(adapter.callbacks.size() == 2);
    CHECK(contains(service.handle("test.a", message("b")), "queueFull"));
    adapter.callbacks[0]({"a", "selected", "ok", {}});
    CHECK(contains(service.handle("test.a", poll("a")), "selected"));
    CHECK(contains(service.handle("test.b", poll("a")), "queued"));
    adapter.callbacks[0]({"a", "failed", {}, {}});
    CHECK(contains(service.handle("test.a", poll("a")), "selected"));
    adapter.callbacks[1]({"a", "selected", "not-a-button", {}});
    CHECK(contains(service.handle("test.b", poll("a")), "invalidAdapterResult"));
    CHECK(contains(service.handle("test.a", message("b")), "queued"));
    adapter.callbacks[2]({"wrong-id", "dismissed", {}, {}});
    CHECK(contains(service.handle("test.a", poll("b")), "invalidAdapterResult"));
    CHECK(contains(service.handle("test.a", message("c")), "queued"));
    CHECK(contains(service.handle("test.a", poll("a")), "operationUnavailable"));
    adapter.callbacks[3]({"c", "shown", {}, {}});
    CHECK(contains(service.handle("test.a", poll("c")), "shown"));
    adapter.callbacks[3]({"c", "selected", "ok", {}});
    CHECK(contains(service.handle("test.a", poll("c")), "selected"));
    adapter.supported = false;
    CHECK(contains(service.handle("test.a", message("d")), "unsupported"));
    adapter.supported = true; adapter.ready = false;
    CHECK(contains(service.handle("test.a", message("d")), "notReady"));
    adapter.ready = true; adapter.throws = true;
    CHECK(contains(service.handle("test.a", message("d")), "adapterInterrupted"));
    adapter.throws = false;
    {
        Service transient("kr-812", adapter, {1, 1});
        CHECK(contains(transient.handle("test.a", message("late")), "queued"));
    }
    adapter.callbacks.back()({"late", "selected", "ok", {}});
    CHECK(true);
    // A synchronous completion can be evicted by another submission before handle returns.
    Fixture reentrant;
    Service bounded("kr-812", reentrant, {1, 1});
    reentrant.synchronous = [&] {
        reentrant.callbacks[0]({"first", "selected", "ok", {}});
        reentrant.synchronous = {};
        CHECK(contains(bounded.handle("test.a", message("second")), "queued"));
    };
    CHECK(contains(bounded.handle("test.a", message("first")), "selected"));
    CHECK(contains(bounded.handle("test.a", poll("first")), "operationUnavailable"));
    reentrant.callbacks[1]({"second", "dismissed", {}, {}});
    CHECK(contains(bounded.handle("test.a", message("first")), "queued"));
    reentrant.callbacks[0]({"first", "selected", "ok", {}});
    CHECK(contains(bounded.handle("test.a", poll("first")), "queued"));
    reentrant.callbacks[2]({"first", "inserted", {}, {}});
    CHECK(contains(bounded.handle("test.a", poll("first")), "invalidAdapterResult"));
    return finish("game-controls-service");
}

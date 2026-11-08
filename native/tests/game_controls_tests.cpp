#include "check.h"
#include "../../games/BubbleFighter/native/game-controls/ui_service.h"
#include "../../games/BubbleFighter/native/game-controls/presentation.h"
#include "../../games/BubbleFighter/native/game-controls/native_calls.h"
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
}
struct Fixture final : Adapter {
    std::vector<Completion> callbacks;
    std::vector<Request> requests;
    bool supported = true, ready = true, throws = false;
    std::function<void()> synchronous;
    std::vector<Capability> capabilities() override { return {{"messageBox", supported, ready, {}, {}}, {"chat", supported, ready, {}, {}}, {"notification", supported, ready, {}, {}}}; }
    void submit(const std::string&, Request request, Completion complete) override {
        if (throws) throw std::runtime_error("fixture");
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
}
int main() {
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

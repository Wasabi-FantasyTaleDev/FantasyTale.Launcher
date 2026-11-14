#pragma once
#include <cstdint>
#include <cstddef>
#include <cstring>
#include <stdexcept>
#include <string>

namespace game_controls::native_calls {
struct LegacyUiLayout {
    uint32_t root_offset, active_form_offset, pending_form_offset, exit_flag_offset;
};

// cn-279 sub_4A0A50 and cn-324 sub_86A110 center against the root without a null check.
// Their modal loops also require an active application with no pending form replacement.
template<class Reader>
bool legacy_ui_ready(uintptr_t application, uintptr_t manager, const LegacyUiLayout& layout, Reader read) {
    if (!application || !manager) return false;
    const auto field = [&](uintptr_t owner, uint32_t offset, auto& value) {
        return offset <= UINTPTR_MAX - owner && sizeof(value) - 1 <= UINTPTR_MAX - owner - offset
            && read(owner + offset, &value, static_cast<uint32_t>(sizeof(value)));
    };
    uintptr_t root = 0, active = 0, pending = 0;
    uint8_t exiting = 0;
    return field(manager, layout.root_offset, root) && root
        && field(application, layout.active_form_offset, active) && active
        && field(application, layout.pending_form_offset, pending) && !pending
        && field(application, layout.exit_flag_offset, exiting) && !exiting;
}

// cn-279 sub_4A4B80 and cn-324 sub_86E2B0 remove the parent's child-list reference.
inline void detach_legacy_message(uintptr_t function, void* dialog) {
    reinterpret_cast<void(__thiscall*)(void*, uintptr_t)>(function)(dialog, 0);
}

// kr-812 sub_401300 and id-400 sub_403D20 copy length/capacity at +16/+20, never adopting the input.
struct BorrowedString {
    union { wchar_t small[8]; const wchar_t* data; } storage{};
    uint32_t length = 0, capacity = 7;
    explicit BorrowedString(const std::wstring& text) : length(static_cast<uint32_t>(text.size())) {
        if (text.size() < 8) std::memcpy(storage.small, text.c_str(), (text.size() + 1) * sizeof(wchar_t));
        else { storage.data = text.c_str(); capacity = length; }
    }
};
static_assert(sizeof(BorrowedString) == 24);

// cn-279 sub_401570 and cn-324 sub_401C00 construct a single pointer to
// game-owned UTF-16 storage. The matching release uses the game's allocator.
class LegacyString {
    const wchar_t* value_ = nullptr;
    uintptr_t release_;
public:
    LegacyString(uintptr_t create, uintptr_t release, const std::wstring& value) : release_(release) {
        reinterpret_cast<void*(__thiscall*)(const wchar_t**, const wchar_t*)>(create)(&value_, value.c_str());
        if (!value_) throw std::runtime_error("Native legacy string construction failed");
    }
    ~LegacyString() { reinterpret_cast<void(__thiscall*)(const wchar_t**)>(release_)(&value_); }
    LegacyString(const LegacyString&) = delete;
    LegacyString& operator=(const LegacyString&) = delete;
    const wchar_t* const* address() const { return &value_; }
};

// Copies the text of the game string whose characters start at `data`: cn-324 sub_401B10 and cn-324 sub_880310 keep
// the length at data -20. `read` copies native memory and reports false when it is unavailable. A missing string, a
// negative length or one above `limit` is corrupt memory, not empty text.
template<class Reader>
std::wstring legacy_string(uintptr_t data, uint32_t limit, Reader read) {
    int32_t length = 0;
    if (data < 20 || !read(data - 20, &length, static_cast<uint32_t>(sizeof(length)))) throw std::runtime_error("Native control text is unavailable");
    if (length < 0 || static_cast<uint32_t>(length) > limit) throw std::length_error("controlTextLimit");
    std::wstring result(static_cast<size_t>(length), L'\0');
    if (length && !read(data, result.data(), static_cast<uint32_t>(length) * static_cast<uint32_t>(sizeof(wchar_t))))
        throw std::runtime_error("Native control text is unavailable");
    return result;
}

inline void* construct_legacy_message(uint32_t abi, uintptr_t function, void* storage, const LegacyString& title,
    const LegacyString& body, int icon, int center) {
    using String = const wchar_t* const*;
    // cn-279 sub_7AA7D0 pushes five parameters to cn-279 sub_8A2F10.
    if (abi == 4) return reinterpret_cast<void*(__stdcall*)(void*, String, String, int, int)>(function)(
        storage, title.address(), body.address(), 2, icon);
    // cn-324 sub_576670 passes 0x8000 in ECX and EDX before the same five stack parameters.
    if (abi == 5) return reinterpret_cast<void*(__fastcall*)(int, int, void*, String, String, int, int)>(function)(
        center, center, storage, title.address(), body.address(), 2, icon);
    throw std::invalid_argument("Unknown legacy native message-box ABI");
}

// kr-812 sub_8F2F70 deep-copies both strings; kr-812 sub_8E23D0 owns expiry and destruction.
struct AnnouncementRecord {
    BorrowedString text;
    uint32_t unknown_24 = 0;
    uint32_t color = 0xFFFFFF00, tick = 0, duration = 0;
    BorrowedString secondary{std::wstring{}};
    uint32_t unknown_64 = 0;
    uint8_t type = 1;
    uint8_t padding[3]{};
    uint32_t fade = 2000;
};
static_assert(sizeof(AnnouncementRecord) == 76);

// tw-454 sub_B5F4D0 copies 68 bytes; tw-454 sub_B4FA10 consumes duration +32 and fade +64.
struct CompactAnnouncementRecord {
    BorrowedString text;
    uint32_t color = 0xFFFFFF00, tick = 0, duration = 0;
    BorrowedString secondary{std::wstring{}};
    uint8_t type = 0;
    uint8_t padding[3]{};
    uint32_t fade = 2000;
};
static_assert(sizeof(CompactAnnouncementRecord) == 68);
static_assert(offsetof(CompactAnnouncementRecord, color) == 24 && offsetof(CompactAnnouncementRecord, fade) == 64);

inline uint32_t announcement_tick(uint32_t abi, uintptr_t function, uintptr_t clock) {
    switch (abi) {
    case 1: return reinterpret_cast<uint32_t(__thiscall*)(void*)>(function)(reinterpret_cast<void*>(clock));
    // tw-454 sub_EDB9A0 and cn-774 sub_F40160 return the paused or running game clock without arguments.
    case 2: return reinterpret_cast<uint32_t(__cdecl*)()>(function)();
    default: throw std::invalid_argument("Unknown native announcement clock ABI");
    }
}

inline void append_announcement(uint32_t abi, uintptr_t function, uintptr_t vector, uintptr_t end, const void* record) {
    switch (abi) {
    case 1:
        // kr-812 sub_8EFF90 takes the source in ECX and the native vector in EBX.
        __asm {
            push ebx
            mov ebx, vector
            mov ecx, record
            call function
            pop ebx
        }
        return;
    case 2:
        // tw-454 sub_B5F550 inserts at the supplied end iterator and deep-copies the record before moving storage.
        reinterpret_cast<void(__thiscall*)(void*, uintptr_t, const void*)>(function)(reinterpret_cast<void*>(vector), end, record);
        return;
    default: throw std::invalid_argument("Unknown native announcement insertion ABI");
    }
}

inline void* allocate(uint32_t abi, uintptr_t function, uintptr_t allocator, uint32_t size) {
    switch (abi) {
    case 1: return reinterpret_cast<void*(__cdecl*)(uint32_t)>(function)(size);
    case 2: {
        // cn-735 sub_98C450 passes the allocator address, size and a {0,-1,0} stack context to cn-735 sub_CD8E40.
        intptr_t context[]{0, -1, 0};
        return reinterpret_cast<void*(__cdecl*)(uintptr_t, uint32_t, intptr_t*)>(function)(allocator, size, context);
    }
    default: throw std::invalid_argument("Unknown native allocation ABI");
    }
}

inline void* construct(uint32_t abi, uintptr_t function, void* storage, const BorrowedString& title,
    const BorrowedString& body, int buttons, int icon, int center) {
    switch (abi) {
    case 1:
        // kr-812 sub_896B00 supplies coordinates in ECX/EDX before five stack arguments.
        return reinterpret_cast<void*(__fastcall*)(int, int, void*, const BorrowedString*, const BorrowedString*, int, int)>(function)(
            center, center, storage, &title, &body, buttons, icon);
    case 2:
        // kr-1256 sub_899620 supplies the allocated dialog in ECX.
        return reinterpret_cast<void*(__thiscall*)(void*, const BorrowedString*, const BorrowedString*, int, int)>(function)(
            storage, &title, &body, buttons, icon);
    case 3:
        // id-400 sub_739FF0 pushes storage/title/body/mode/icon; id-400 sub_8153E0 returns with RET 20.
        return reinterpret_cast<void*(__stdcall*)(void*, const BorrowedString*, const BorrowedString*, int, int)>(function)(
            storage, &title, &body, buttons, icon);
    default: throw std::invalid_argument("Unknown native message-box ABI");
    }
}

inline int modal(uint32_t abi, uintptr_t function, void* dialog, uint8_t& interrupted) {
    interrupted = 0;
    switch (abi) {
    case 1:
        return reinterpret_cast<int(__thiscall*)(void*, int, int, int, int)>(function)(dialog, 0, 1, 1, 0);
    case 2:
        return reinterpret_cast<int(__thiscall*)(void*, uint8_t*, int, int, int, int)>(function)(dialog, &interrupted, 0, 1, 1, 0);
    case 3:
        // id-400 sub_A03370 consumes three flags, as called by id-400 sub_739FF0.
        return reinterpret_cast<int(__thiscall*)(void*, int, int, int)>(function)(dialog, 0, 1, 1);
    default: throw std::invalid_argument("Unknown native modal ABI");
    }
}

inline void notify(uint32_t abi, uintptr_t function, uintptr_t application, const BorrowedString& body) {
    switch (abi) {
    case 1: reinterpret_cast<void(__cdecl*)(const BorrowedString*)>(function)(&body); return;
    case 2: reinterpret_cast<void(__thiscall*)(const BorrowedString*)>(function)(&body); return;
    case 3: {
        // id-400 sub_93E8A0 calls id-400 sub_85A640 with ECX=-1 and five stack arguments.
        const BorrowedString empty(std::wstring{});
        reinterpret_cast<void(__thiscall*)(intptr_t, uintptr_t, const BorrowedString*, const BorrowedString*, intptr_t, intptr_t)>(function)(
            -1, application, &body, &empty, -1, -1);
        return;
    }
    case 4: {
        // id-1809 sub_95AB50 supplies seven stack arguments to id-1809 sub_880F70, including its final style byte.
        const BorrowedString empty(std::wstring{});
        reinterpret_cast<void(__stdcall*)(uintptr_t, const BorrowedString*, const BorrowedString*, intptr_t, intptr_t, intptr_t, int)>(function)(
            application, &body, &empty, -1, -1, -1, 0);
        return;
    }
    default: throw std::invalid_argument("Unknown native notification ABI");
    }
}

inline void chat(uint32_t abi, uintptr_t function, uintptr_t module, const std::wstring& caption,
    const std::wstring& body, uint32_t color) {
    const BorrowedString heading(caption), text(body);
    switch (abi) {
    case 1:
        reinterpret_cast<void(__thiscall*)(void*, const BorrowedString*, uint32_t, const BorrowedString*)>(function)(
            reinterpret_cast<void*>(module), &heading, color, &text);
        return;
    case 2:
        reinterpret_cast<void(__thiscall*)(void*, uint32_t, const wchar_t*, uint32_t, const wchar_t*, uint32_t)>(function)(
            reinterpret_cast<void*>(module), color, caption.c_str(), heading.length, body.c_str(), text.length);
        return;
    case 3:
        // id-400 sub_754ED0 passes module/body in ECX/EDX, then caption/color on the stack.
        reinterpret_cast<void(__fastcall*)(uintptr_t, const BorrowedString*, const BorrowedString*, uint32_t)>(function)(
            module, &text, &heading, color);
        return;
    case 4: {
        // cn-735 sub_A344A0 supplies module in ESI, body in EDX and caption/color on the stack.
        const auto* text_pointer = &text;
        const auto* heading_pointer = &heading;
        __asm {
            push esi
            mov esi, module
            mov edx, text_pointer
            push color
            push heading_pointer
            call function
            pop esi
        }
        return;
    }
    case 5:
        // tw-454 sub_A99620 passes caption/color/body/0 to tw-454 sub_A0CCF0, which returns with RET 16.
        reinterpret_cast<void(__thiscall*)(void*, const BorrowedString*, uint32_t, const BorrowedString*, int)>(function)(
            reinterpret_cast<void*>(module), &heading, color, &text, 0);
        return;
    default: throw std::invalid_argument("Unknown native chat ABI");
    }
}

// Both setters copy the text, truncate it to the edit limit and clamp the caret.
inline void set_edit_text(uint32_t abi, uintptr_t function, uintptr_t edit, const BorrowedString& text) {
    switch (abi) {
    // kr-1256 sub_FC91C0 takes a byte after the text and notifies the owner only when it is clear; it returns with RET 8.
    case 1: reinterpret_cast<uint8_t(__thiscall*)(void*, const BorrowedString*, uint8_t)>(function)(reinterpret_cast<void*>(edit), &text, 0); return;
    // cn-774 sub_F68290 takes only the text, always notifies, and returns with RET 4.
    case 2: reinterpret_cast<uint8_t(__thiscall*)(void*, const BorrowedString*)>(function)(reinterpret_cast<void*>(edit), &text); return;
    default: throw std::invalid_argument("Unknown native edit text ABI");
    }
}
}

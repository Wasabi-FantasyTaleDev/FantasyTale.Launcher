#include "ui_service.h"
#include "presentation.h"
#include "native_calls.h"
#include "control_registry.h"
#include "signature_readiness.h"
#include "selection_records.h"
#include <fthook/plugin_abi.h>
#include <windows.h>
#include <toml.hpp>
#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <cstdio>
#include <deque>
#include <fstream>
#include <iterator>
#include <map>
#include <mutex>
#include <optional>
#include <sstream>
#include <stdexcept>

namespace game_controls {
namespace {
const fthook_host* runtime = nullptr;

std::wstring wide(const std::string& value) {
    if (value.empty()) return {};
    const auto size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0);
    if (!size) throw std::invalid_argument("Native UI text must be valid UTF-8");
    std::wstring result(static_cast<size_t>(size), L'\0');
    if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), size)) throw std::invalid_argument("Cannot convert native UI text");
    return result;
}
bool read(uintptr_t address, void* output, uint32_t size) { return runtime->read(address, output, size) == FTHOOK_OK; }
uintptr_t pointer(uintptr_t address) { uintptr_t result = 0; return read(address, &result, sizeof(result)) ? result : 0; }

using native_calls::BorrowedString;

struct Descriptor {
    struct SelectionGroup {
        std::string form, name;
        uintptr_t form_vtable = 0;
        uint32_t value_offset = 0;
        std::vector<int32_t> values;
        // Set when the form maps ArrayIndex to value through its own record vector instead of `values`.
        uint32_t records_offset = 0;
        SelectionRecords records;
        // Set when the form stores no chosen value and marks the choice as the first disabled member of its own map;
        // `value_offset` is then unused.
        uint32_t members_offset = 0;
        SelectionMembers members;
        // Set when the member tree's header node lies inside the form at `members_offset` (member_layout 2).
        bool inline_members = false;
        InlineSelectionMembers inline_layout;
    };
    uintptr_t base = 0;
    std::map<std::string, uintptr_t> addresses;
    std::map<std::string, uint32_t> values;
    std::vector<std::pair<uintptr_t, std::vector<uint8_t>>> signatures;
    std::vector<SelectionGroup> selection_groups;
    uintptr_t address(const char* name) const { return addresses.at(name); }
    uint32_t value(const char* name) const { return values.at(name); }
    uint32_t convention(const char* name) const { const auto it = values.find(name); return it == values.end() ? 1 : it->second; }
    bool legacy_message_box() const { return value("message_abi") >= 4; }
    bool ui_ready() const {
        const auto manager = pointer(address("ui_manager"));
        const auto application = pointer(address("application"));
        if (!manager || !application || !pointer(address("ui_context"))) return false;
        return !legacy_message_box() || native_calls::legacy_ui_ready(application, manager,
            {value("ui_root_offset"), value("active_form_offset"), value("pending_form_offset"), value("exit_flag_offset")}, read);
    }
    bool matches() const {
        for (const auto& [address, expected] : signatures) {
            std::vector<uint8_t> actual(expected.size());
            if (!read(address, actual.data(), static_cast<uint32_t>(actual.size())) || actual != expected) return false;
        }
        return true;
    }
};
std::optional<Descriptor> load_descriptor() {
    const auto path = wide(runtime->plugin_directory) + L"\\native-ui\\" + wide(runtime->build) + L".toml";
    std::ifstream input(path, std::ios::binary);
    if (!input) return std::nullopt;
    const std::string contents{std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>()};
    auto parsed = toml::parse(contents);
    if (!parsed) throw std::invalid_argument("Malformed native UI descriptor");
    const auto& table = parsed.table();
    for (const auto& [key, node] : table) {
        (void)node;
        if (key != "schema" && key != "build" && key != "timestamp" && key != "addresses" && key != "values" && key != "signatures" && key != "selection_groups") throw std::invalid_argument("Unknown native UI descriptor field");
    }
    if (table["schema"].value<int64_t>() != 1 || table["build"].value<std::string>() != runtime->build) throw std::invalid_argument("Native UI descriptor identity mismatch");
    Descriptor result;
    result.base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    IMAGE_DOS_HEADER dos{};
    IMAGE_NT_HEADERS32 pe{};
    if (!read(result.base, &dos, sizeof(dos)) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew <= 0
        || !read(result.base + static_cast<uint32_t>(dos.e_lfanew), &pe, sizeof(pe)) || pe.Signature != IMAGE_NT_SIGNATURE
        || pe.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC || table["timestamp"].value<int64_t>() != pe.FileHeader.TimeDateStamp)
        throw std::invalid_argument("Native UI image fingerprint mismatch");
    const auto* addresses = table["addresses"].as_table();
    const auto* values = table["values"].as_table();
    const auto* signatures = table["signatures"].as_array();
    if (!addresses || !values || !signatures || signatures->empty()) throw std::invalid_argument("Incomplete native UI descriptor");
    for (const auto& [key, node] : *addresses) {
        const auto value = node.value<int64_t>();
        if (!value || *value <= 0 || *value >= pe.OptionalHeader.SizeOfImage) throw std::invalid_argument("Invalid native UI address");
        result.addresses.emplace(std::string(key.str()), result.base + static_cast<uintptr_t>(*value));
    }
    for (const auto& [key, node] : *values) {
        const auto value = node.value<int64_t>();
        if (!value || *value < 0 || *value > UINT32_MAX) throw std::invalid_argument("Invalid native UI layout value");
        result.values.emplace(std::string(key.str()), static_cast<uint32_t>(*value));
    }
    for (const auto& node : *signatures) {
        const auto* row = node.as_table();
        if (!row) throw std::invalid_argument("A native UI signature must be a table");
        const auto name = (*row)["address"].value<std::string>();
        const auto text = (*row)["bytes"].value<std::string>();
        if (!name || !text || !result.addresses.contains(*name)) throw std::invalid_argument("Invalid native UI signature address");
        std::istringstream bytes(*text);
        std::vector<uint8_t> expected;
        std::string token;
        while (bytes >> token) {
            if (token.size() != 2 || token.find_first_not_of("0123456789abcdefABCDEF") != std::string::npos) throw std::invalid_argument("Invalid native UI signature byte");
            expected.push_back(static_cast<uint8_t>(std::stoul(token, nullptr, 16)));
        }
        if (expected.empty() || expected.size() > 4096) throw std::invalid_argument("Empty or oversized native UI code signature");
        if (result.addresses.at(*name) - result.base + expected.size() > pe.OptionalHeader.SizeOfImage)
            throw std::invalid_argument("Native UI signature extends beyond the main image");
        result.signatures.emplace_back(result.addresses.at(*name), std::move(expected));
    }
    if (const auto* node = table.get("selection_groups")) {
        const auto* groups = node->as_array();
        if (!groups) throw std::invalid_argument("Native selection groups must be an array");
        for (const auto& item : *groups) {
            const auto* group = item.as_table();
            if (!group) throw std::invalid_argument("A native selection group must be a table");
            for (const auto& [key, value] : *group) {
                (void)value;
                if (key != "form" && key != "name" && key != "form_vtable" && key != "form_size" && key != "value_offset" && key != "values"
                    && key != "records_offset" && key != "record_size" && key != "record_key_offset" && key != "record_value_offset"
                    && key != "members_offset" && key != "member_key_offset" && key != "member_control_offset" && key != "member_nil_offset"
                    && key != "member_layout")
                    throw std::invalid_argument("Unknown native selection group field");
            }
            const auto form = (*group)["form"].value<std::string>(), name = (*group)["name"].value<std::string>(), vtable = (*group)["form_vtable"].value<std::string>();
            const auto size = (*group)["form_size"].value<int64_t>(), offset = (*group)["value_offset"].value<int64_t>();
            const auto* choices = (*group)["values"].as_array();
            const auto records = (*group)["records_offset"].value<int64_t>(), record_size = (*group)["record_size"].value<int64_t>();
            const auto record_key = (*group)["record_key_offset"].value<int64_t>(), record_value = (*group)["record_value_offset"].value<int64_t>();
            const bool keyed = records || record_size || record_key || record_value;
            const auto members = (*group)["members_offset"].value<int64_t>(), member_key = (*group)["member_key_offset"].value<int64_t>();
            const auto member_control = (*group)["member_control_offset"].value<int64_t>(), member_nil = (*group)["member_nil_offset"].value<int64_t>();
            const auto member_layout = (*group)["member_layout"].value<int64_t>();
            const bool marked = members || member_key || member_control || member_nil || member_layout;
            if (!form || form->empty() || !form->ends_with(".ui") || form->find('\0') != std::string::npos
                || !name || name->empty() || name->find('\0') != std::string::npos || !vtable || !result.addresses.contains(*vtable)
                || !size || *size < 4 || *size > UINT32_MAX || (marked && (offset || keyed))
                || (!marked && (!offset || *offset < 0 || *offset > *size - 4 || *offset % 4))
                || (keyed ? choices != nullptr : !choices || choices->empty()))
                throw std::invalid_argument("Invalid native selection group layout");
            Descriptor::SelectionGroup selected{*form, *name, result.addresses.at(*vtable), marked ? 0 : static_cast<uint32_t>(*offset), {}};
            // 1 (default): the form holds a pointer to the map's header and every link carries a nil byte (cn-406 +712).
            // 2: the form holds the header node itself and missing children are null links (cn-324 DlgCreateRoom +352).
            if ((*group)["member_layout"] && (!member_layout || (*member_layout != 1 && *member_layout != 2)))
                throw std::invalid_argument("Unknown native selection member layout");
            if (marked && member_layout == 2) {
                // The header node (colour, parent, left, right) lies inside the form; node fields are read only at the stated offsets.
                if (!members || !member_key || !member_control || member_nil || *members < 4 || *members % 4 || *members > *size - 16
                    || *member_key < 0 || *member_control < 0 || *member_key > 4096 || *member_control > 4096)
                    throw std::invalid_argument("Invalid native selection member layout");
                selected.members_offset = static_cast<uint32_t>(*members);
                selected.inline_members = true;
                selected.inline_layout = {static_cast<uint32_t>(*member_key), static_cast<uint32_t>(*member_control)};
                if (!selected.inline_layout.valid()) throw std::invalid_argument("Invalid native selection member layout");
            } else if (marked) {
                // The member map's header pointer lies inside the form; node fields are read only at the stated offsets.
                if (!members || !member_key || !member_control || !member_nil || *members < 4 || *members % 4 || *members > *size - 4
                    || *member_key < 0 || *member_control < 0 || *member_nil < 0 || *member_key > 4096 || *member_control > 4096 || *member_nil > 4096)
                    throw std::invalid_argument("Invalid native selection member layout");
                selected.members_offset = static_cast<uint32_t>(*members);
                selected.members = {static_cast<uint32_t>(*member_key), static_cast<uint32_t>(*member_control), static_cast<uint32_t>(*member_nil)};
                if (!selected.members.valid()) throw std::invalid_argument("Invalid native selection member layout");
            }
            if (keyed) {
                // The record vector is a begin/end pointer pair inside the form; each record is read only within its size.
                if (!records || !record_size || !record_key || !record_value || *records < 0 || *records % 4 || *records > *size - 8
                    || *record_size < 0 || *record_size > 4096 || *record_key < 0 || *record_value < 0 || *record_key > UINT32_MAX || *record_value > UINT32_MAX)
                    throw std::invalid_argument("Invalid native selection record layout");
                selected.records_offset = static_cast<uint32_t>(*records);
                selected.records = {static_cast<uint32_t>(*record_size), static_cast<uint32_t>(*record_key), static_cast<uint32_t>(*record_value)};
                if (!selected.records.valid()) throw std::invalid_argument("Invalid native selection record layout");
            }
            if (choices) for (const auto& choice : *choices) {
                const auto value = choice.value<int64_t>();
                if (!value || *value < INT32_MIN || *value > INT32_MAX) throw std::invalid_argument("Native selection values must be signed 32-bit integers");
                selected.values.push_back(static_cast<int32_t>(*value));
            }
            if (std::any_of(result.selection_groups.begin(), result.selection_groups.end(), [&](const auto& prior) { return prior.form == selected.form && prior.name == selected.name && prior.form_vtable == selected.form_vtable; }))
                throw std::invalid_argument("Duplicate native selection group");
            result.selection_groups.push_back(std::move(selected));
        }
    }
    // Required keys fail during activation rather than after a control is allocated.
    for (const auto* name : {"construct_message", "modal_message", "message_vtable", "application", "ui_context", "ui_manager"}) (void)result.address(name);
    for (const auto* name : {"message_abi", "modal_abi", "message_size", "reference_offset", "secondary_offset", "destructor_slot", "release_previous", "ok_offset", "cancel_offset", "command_offset", "icon_question"}) (void)result.value(name);
    if (result.value("message_abi") < 1 || result.value("message_abi") > 5) throw std::invalid_argument("Unknown native message-box ABI");
    if (result.value("message_abi") == 1 || result.value("message_abi") == 5) (void)result.value("center");
    if (result.legacy_message_box()) {
        for (const auto* name : {"allocate", "position_message", "detach_message", "string_create", "string_release", "set_text"}) (void)result.address(name);
        (void)result.value("position");
        (void)result.value("modal_slot");
        (void)result.value("text_slot");
        for (const auto* name : {"ui_root_offset", "active_form_offset", "pending_form_offset"})
            if (result.value(name) % sizeof(uintptr_t)) throw std::invalid_argument("Unaligned native UI readiness offset");
        (void)result.value("exit_flag_offset");
        if (result.value("modal_abi") != 3) throw std::invalid_argument("Invalid legacy native modal ABI");
    } else {
        for (const auto* name : {"layout_message", "notify", "chat"}) (void)result.address(name);
        for (const auto* name : {"notification_offset", "chat_view_offset", "icon_notice", "icon_warning", "icon_error"}) (void)result.value(name);
        if (!result.addresses.contains("chat_module")) (void)result.value("chat_offset");
        if (result.value("notify_abi") < 1 || result.value("notify_abi") > 4 || result.value("chat_abi") < 1 || result.value("chat_abi") > 5)
            throw std::invalid_argument("Unknown native text-operation ABI");
    }
    if (!result.addresses.contains("set_text")) (void)result.value("text_slot");
    if (result.addresses.contains("allocate")) {
        const auto abi = result.values.contains("allocate_abi") ? result.value("allocate_abi") : 1;
        if (abi < 1 || abi > 2) throw std::invalid_argument("Unknown native allocation ABI");
        if (abi == 2) (void)result.address("allocator");
    } else (void)result.address("allocator");
    if (result.value("modal_abi") < 1 || result.value("modal_abi") > 3) throw std::invalid_argument("Unknown native modal ABI");
    for (const auto* name : {"reference_offset", "secondary_offset", "ok_offset", "cancel_offset"}) {
        const auto offset = result.value(name), size = result.value("message_size");
        if (size < sizeof(uintptr_t) || offset > size - sizeof(uintptr_t) || offset % sizeof(uintptr_t))
            throw std::invalid_argument(std::string("Invalid native dialog offset: ") + name);
    }
    if (result.values.contains("without_icon_offset") && result.value("without_icon_offset") >= result.value("message_size"))
        throw std::invalid_argument("Icon-free flag extends beyond the native dialog");
    for (const auto* name : {"text_slot", "modal_slot", "destructor_slot"})
        if (result.values.contains(name) && result.value(name) % sizeof(uintptr_t))
            throw std::invalid_argument(std::string("Unaligned native vtable slot: ") + name);
    if (result.addresses.contains("append_announcement")) {
        (void)result.address("clock_now");
        for (const auto* name : {"announcement_abi", "announcement_layout", "clock_abi"})
            if (result.convention(name) < 1 || result.convention(name) > 2) throw std::invalid_argument("Unknown native announcement convention");
        if (result.convention("clock_abi") == 1) (void)result.address("clock");
        (void)result.value("announcement_offset"); (void)result.value("announcement_limit");
    }
    for (const auto* name : {"allocate", "construct_message", "layout_message", "modal_message", "position_message", "detach_message", "string_create", "string_release", "notify", "chat", "append_announcement", "clock_now", "set_text"})
        if (result.addresses.contains(name) && std::none_of(result.signatures.begin(), result.signatures.end(), [&](const auto& signature) { return signature.first == result.address(name); }))
            throw std::invalid_argument("Missing native UI function signature");
    if (result.values.contains("control_abi")) {
        const auto abi = result.value("control_abi");
        if (abi != 1 && abi != 2) throw std::invalid_argument("Unknown native control ABI");
        const auto signed_functions = [&](std::initializer_list<const char*> names) {
            for (const auto* name : names) {
                const auto address = result.address(name);
                if (std::none_of(result.signatures.begin(), result.signatures.end(), [&](const auto& signature) { return signature.first == address; }))
                    throw std::invalid_argument("Missing native control function signature");
            }
        };
        signed_functions({"control_load_form", "control_initialize", "control_destroy", "control_click", "control_set_text", "edit_set_text"});
        if (abi == 1) {
            // kr-812: text goes through vtable getters and game-allocated strings; buttons can be created.
            signed_functions({"control_get_text", "edit_get_text", "edit_set_maximum", "control_string_assign", "control_button_create", "control_parent", "control_geometry"});
            if (result.convention("control_text_abi") == 1) signed_functions({"control_string_release"});
            (void)result.address("ime_manager");
            for (const auto* name : {"get_text_slot", "ime_composition_offset"}) (void)result.value(name);
        } else {
            // kr-1256: text is read in place, the edit limit is a field, and focus is cleared before a control is disabled.
            signed_functions({"control_focus"});
            for (const auto* name : {"text_offset", "focus_offset"}) (void)result.value(name);
        }
        for (const auto* name : {"button_vtable", "edit_vtable", "label_vtable"}) (void)result.address(name);
        for (const auto* name : {"name_offset", "visible_offset", "enabled_offset", "array_index_offset", "children_offset", "x_offset", "y_offset", "width_offset", "height_offset",
            "edit_maximum_offset", "control_root_offset", "control_layer_offset", "control_modal_offset", "owner_form_offset"}) (void)result.value(name);
        // Observer prologues are copied whole into trampolines: each length must end on an instruction boundary of
        // position-independent code in that build; kr-812's defaults are 7, 7, 7 and 5 bytes.
        for (const auto* name : {"load_hook_bytes", "initialize_hook_bytes", "destroy_hook_bytes", "click_hook_bytes"})
            if (result.values.contains(name) && (result.value(name) < 5 || result.value(name) > 16)) throw std::invalid_argument("Invalid native control hook length");
        // 1 (default): the button handler takes a "clicked" byte and fires only when it is set (kr-812 sub_A91840).
        // 2: the handler takes no argument and is the activation itself (cn-406 sub_9812D0, UiButton vtable +0xB4).
        if (result.values.contains("click_abi") && result.value("click_abi") != 1 && result.value("click_abi") != 2)
            throw std::invalid_argument("Unknown native control click ABI");
        // 1 (default): the edit setter takes the text and a notification byte (kr-1256 sub_FC91C0).
        // 2: it takes the text alone (cn-774 sub_F68290, UiEdit vtable +244).
        if (result.values.contains("edit_text_abi") && (abi != 2 || (result.value("edit_text_abi") != 1 && result.value("edit_text_abi") != 2)))
            throw std::invalid_argument("Unknown native edit text ABI");
        // 1 (default): control strings are 24-byte wide strings with length/capacity at +16/+20 (kr-812 sub_401300).
        // 2: they are one pointer to UTF-16 data behind a 20-byte header with the length at -20 (cn-324 sub_401B10),
        // built and released by string_create/string_release.
        if (result.values.contains("control_text_abi")) {
            const auto text = result.value("control_text_abi");
            if (abi != 1 || (text != 1 && text != 2)) throw std::invalid_argument("Unknown native control text ABI");
            if (text == 2) signed_functions({"string_create", "string_release"});
        }
        // Optional: builds without a verified enable setter report control.setEnabled as unsupported.
        if (result.addresses.contains("control_set_enabled") && std::none_of(result.signatures.begin(), result.signatures.end(),
                [&](const auto& signature) { return signature.first == result.address("control_set_enabled"); }))
            throw std::invalid_argument("Missing native control function signature");
    }
    if (!result.selection_groups.empty() && !result.values.contains("control_abi")) throw std::invalid_argument("Native selections require a control adapter");
    return result;
}

#include "native_controls.h"

class NativeAdapter;
std::atomic<NativeAdapter*> active_adapter = nullptr;

class NativeAdapter final : public Adapter {
    struct Work { std::string caller; Request request; Completion complete; };
    std::optional<Descriptor> descriptor_;
    std::unique_ptr<NativeControls> controls_;
    std::mutex queue_mutex_, setup_mutex_;
    std::deque<Work> work_;
    HANDLE stop_ = nullptr, wake_ = nullptr, dispatcher_ = nullptr;
    HHOOK hook_ = nullptr;
    std::atomic<HWND> window_ = nullptr;
    DWORD thread_ = 0;
    UINT message_ = 0;
    size_t queue_limit_;
    bool processing_ = false;
    const char* phase_ = "idle";
    std::atomic<bool> failed_ = false;

    struct WindowSearch { HWND window = nullptr; uint64_t area = 0; };
    static BOOL CALLBACK find_window(HWND window, LPARAM context) {
        auto& found = *reinterpret_cast<WindowSearch*>(context);
        DWORD process = 0;
        GetWindowThreadProcessId(window, &process);
        if (process != GetCurrentProcessId() || GetWindow(window, GW_OWNER) || !IsWindowVisible(window)
            || (GetWindowLongPtrW(window, GWL_EXSTYLE) & WS_EX_TOOLWINDOW)) return TRUE;
        RECT bounds{};
        if (!GetClientRect(window, &bounds) || bounds.right <= 0 || bounds.bottom <= 0) return TRUE;
        const auto area = static_cast<uint64_t>(bounds.right) * static_cast<uint64_t>(bounds.bottom);
        if (area > found.area) { found.window = window; found.area = area; }
        return TRUE;
    }
    bool connect() {
        std::lock_guard guard(setup_mutex_);
        if (hook_ && window_ && IsWindow(window_)) return true;
        if (hook_) {
            if (!UnhookWindowsHookEx(hook_)) return false;
            hook_ = nullptr; window_ = nullptr;
        }
        WindowSearch found;
        EnumWindows(find_window, reinterpret_cast<LPARAM>(&found));
        const auto window = found.window;
        if (!window) return false;
        DWORD process = 0;
        const auto thread = GetWindowThreadProcessId(window, &process);
        if (!thread || process != GetCurrentProcessId()) return false;
        if (thread_ && thread_ != thread) return false;
        if (!message_) message_ = RegisterWindowMessageW(L"FantasyTale.GameControls.Dispatch.v1");
        if (!message_) return false;
        HMODULE module = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&message_hook), &module)) return false;
        window_ = window;
        active_adapter.store(this);
        hook_ = SetWindowsHookExW(WH_CALLWNDPROC, message_hook, module, thread);
        if (!hook_) { active_adapter.store(nullptr); window_ = nullptr; return false; }
        thread_ = thread;
        return true;
    }
    static LRESULT CALLBACK message_hook(int code, WPARAM removed, LPARAM data) {
        auto* adapter = active_adapter.load();
        if (code >= 0 && adapter) {
            const auto* message = reinterpret_cast<const CWPSTRUCT*>(data);
            if (message && message->message == adapter->message_ && message->hwnd == adapter->window_) {
                adapter->drain();
            }
        }
        return CallNextHookEx(nullptr, code, removed, data);
    }
    static DWORD WINAPI dispatch(void* context) {
        auto& self = *static_cast<NativeAdapter*>(context);
        const HANDLE events[]{self.stop_, self.wake_};
        while (WaitForMultipleObjects(2, events, FALSE, INFINITE) == WAIT_OBJECT_0 + 1) {
            // cn-279 sub_4A9100 filters queued messages. Sent notifications reach the
            // owning UI thread without blocking the service callback on a modal loop.
            if (!SendNotifyMessageW(self.window_, self.message_, 0, 0)) {
                std::deque<Work> rejected;
                { std::lock_guard guard(self.queue_mutex_); rejected.swap(self.work_); }
                for (auto& work : rejected) work.complete({work.request.operation_id, "notReady", {}, "gameUiNotReady"});
            }
        }
        return 0;
    }
    static LONG fault(NativeAdapter* self, EXCEPTION_POINTERS* exception) {
        if (exception->ExceptionRecord->ExceptionCode == 0xE06D7363u) return EXCEPTION_CONTINUE_SEARCH;
        char message[192]{};
        const auto address = reinterpret_cast<uintptr_t>(exception->ExceptionRecord->ExceptionAddress);
        const auto base = self->descriptor_ ? self->descriptor_->base : 0;
        sprintf_s(message, "native UI: fault phase=%s code=%08lX image_offset=%08lX",
            self->phase_, exception->ExceptionRecord->ExceptionCode, static_cast<unsigned long>(address - base));
        runtime->log(runtime->context, message);
        return EXCEPTION_EXECUTE_HANDLER;
    }
    static bool guarded(NativeAdapter* self, Work* work) {
        __try { self->execute(*work); return true; }
        __except (fault(self, GetExceptionInformation())) { return false; }
    }
    void drain() {
        if (processing_) return;
        processing_ = true;
        for (;;) {
            std::optional<Work> work;
            {
                std::lock_guard guard(queue_mutex_);
                if (work_.empty()) break;
                work = std::move(work_.front()); work_.pop_front();
            }
            bool success = false;
            phase_ = "prepare";
            try { success = guarded(this, &*work); }
            catch (const std::exception& error) { runtime->log(runtime->context, error.what()); }
            catch (...) { success = false; }
            if (!success) {
                failed_.store(true);
                runtime->log(runtime->context, "native UI: adapter fault; additional presentation disabled");
                work->complete({work->request.operation_id, "outcomeUnknown", {}, "adapterInterrupted"});
            }
        }
        phase_ = "idle";
        processing_ = false;
    }
    void set_text(uintptr_t control, const void* value) const {
        const auto& descriptor = *descriptor_;
        if (!control) throw std::runtime_error("The native button is unavailable");
        const auto function = descriptor.addresses.contains("set_text") ? descriptor.address("set_text") : pointer(pointer(control) + descriptor.value("text_slot"));
        if (descriptor.legacy_message_box() && pointer(pointer(control) + descriptor.value("text_slot")) != function)
            throw std::runtime_error("Unexpected legacy native button text method");
        MEMORY_BASIC_INFORMATION region{};
        if (!control || !function || !VirtualQuery(reinterpret_cast<void*>(function), &region, sizeof(region))
            || region.AllocationBase != reinterpret_cast<void*>(descriptor.base) || !(region.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)))
            throw std::runtime_error("The native button text method is unavailable");
        reinterpret_cast<void(__thiscall*)(void*, const void*)>(function)(reinterpret_cast<void*>(control), value);
    }
    uintptr_t chat_module(uintptr_t application) const {
        return descriptor_->addresses.contains("chat_module") ? pointer(descriptor_->address("chat_module"))
            : pointer(application + descriptor_->value("chat_offset"));
    }
    void* allocate_message() const {
        const auto& descriptor = *descriptor_;
        auto size = descriptor.value("message_size");
        if (descriptor.addresses.contains("allocate"))
            return native_calls::allocate(descriptor.values.contains("allocate_abi") ? descriptor.value("allocate_abi") : 1,
                descriptor.address("allocate"), descriptor.addresses.contains("allocator") ? descriptor.address("allocator") : 0, size);
        // kr-1256 sub_899620 uses the UI allocator with its stack-owned four-word allocation context.
        const auto allocator = pointer(pointer(descriptor.address("allocator")));
        const auto method = allocator ? pointer(pointer(allocator) + sizeof(uintptr_t)) : 0;
        MEMORY_BASIC_INFORMATION region{};
        if (!method || !VirtualQuery(reinterpret_cast<void*>(method), &region, sizeof(region))
            || region.AllocationBase != reinterpret_cast<void*>(descriptor.base)
            || !(region.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)))
            throw std::runtime_error("The native UI allocator is unavailable");
        uintptr_t context[4]{};
        context[2] = reinterpret_cast<uintptr_t>(context);
        context[3] = reinterpret_cast<uintptr_t>(&size);
        return reinterpret_cast<void*(__thiscall*)(void*, int, uint32_t, void*)>(method)(reinterpret_cast<void*>(allocator), 0, size, context);
    }
    void execute(Work& work) {
        const auto& value = work.request;
        const auto& descriptor = *descriptor_;
        if (failed_.load() || !descriptor.matches() || !descriptor.ui_ready()) {
            work.complete({value.operation_id, "notReady", {}, "gameUiNotReady"}); return;
        }
        if (value.kind.starts_with("control.")) {
            phase_ = "controls";
            work.complete(controls_ ? controls_->execute(work.caller, value) : Result{value.operation_id, "unsupported", {}, "adapterUnavailable"});
            return;
        }
        const auto legacy = descriptor.legacy_message_box();
        if (legacy && value.kind != "messageBox") {
            work.complete({value.operation_id, "unsupported", {}, "adapterUnavailable"}); return;
        }
        const auto text = wide(value.text);
        const BorrowedString body(text);
        const auto application = pointer(descriptor.address("application"));
        if (!application) { work.complete({value.operation_id, "notReady", {}, "gameUiNotReady"}); return; }
        if (value.kind == "announcement.show") {
            if (!descriptor.addresses.contains("append_announcement")) { work.complete({value.operation_id, "unsupported", {}, "adapterUnavailable"}); return; }
            if (text.empty()) { work.complete({value.operation_id, "failed", {}, "textRequired"}); return; }
            const auto vector = application + descriptor.value("announcement_offset");
            const auto record_size = descriptor.convention("announcement_layout") == 1 ? sizeof(native_calls::AnnouncementRecord) : sizeof(native_calls::CompactAnnouncementRecord);
            uintptr_t pointers[3]{};
            if (!read(vector, pointers, sizeof(pointers)) || pointers[1] < pointers[0] || pointers[2] < pointers[1]
                || (pointers[1] - pointers[0]) % record_size) throw std::runtime_error("Invalid native announcement vector");
            // kr-812 sub_8E3350 retains at most seven notices; do not evict a game's notice for a test.
            if ((pointers[1] - pointers[0]) / record_size >= descriptor.value("announcement_limit")) {
                work.complete({value.operation_id, "busy", {}, "announcementFull"}); return;
            }
            const auto clock_abi = descriptor.convention("clock_abi");
            const auto clock = clock_abi == 1 ? pointer(descriptor.address("clock")) : 0;
            if (clock_abi == 1 && !clock) { work.complete({value.operation_id, "notReady", {}, "gameUiNotReady"}); return; }
            const auto present = [&](auto record) {
                record.tick = native_calls::announcement_tick(clock_abi, descriptor.address("clock_now"), clock);
                // Zero preserves kr-812's native default; tw-454 sub_A842D0 explicitly supplies 13000.
                if (descriptor.values.contains("announcement_duration")) record.duration = descriptor.value("announcement_duration");
                native_calls::append_announcement(descriptor.convention("announcement_abi"), descriptor.address("append_announcement"), vector, pointers[1], &record);
            };
            if (descriptor.convention("announcement_layout") == 1) present(native_calls::AnnouncementRecord{body});
            else present(native_calls::CompactAnnouncementRecord{body});
            work.complete({value.operation_id, "shown", {}, {}});
            return;
        }
        if (value.kind == "chat") {
            const auto chat = uniform_chat(value.runs);
            if (!chat) { work.complete({value.operation_id, "unsupported", {}, "multipleColorsUnsupported"}); return; }
            const auto module = chat_module(application);
            if (!module || !pointer(module + descriptor.value("chat_view_offset"))) {
                work.complete({value.operation_id, "notReady", {}, "gameUiNotReady"}); return;
            }
            const auto contents = wide(chat->text);
            const auto caption = wide(value.chat_caption);
            native_calls::chat(descriptor.value("chat_abi"), descriptor.address("chat"), module, caption, contents, chat->color);
            work.complete({value.operation_id, "inserted", {}, {}});
            return;
        }
        if (value.kind == "notification") {
            native_calls::notify(descriptor.value("notify_abi"), descriptor.address("notify"), application, body);
            const bool presented = application && pointer(application + descriptor.value("notification_offset"));
            work.complete({value.operation_id, presented ? "shown" : "failed", {}, presented ? "" : "gameUiNotReady"});
            return;
        }
        const auto icon = value.icon == "none" ? descriptor.values.find("icon_notice") : descriptor.values.find("icon_" + value.icon);
        if (value.kind != "messageBox" || value.buttons.empty() || value.buttons.size() > 2 || icon == descriptor.values.end()
            || (legacy && value.buttons.size() != 2)
            || (value.icon == "none" && !descriptor.values.contains("without_icon_offset"))) {
            work.complete({value.operation_id, "unsupported", {}, "parametersUnsupported"}); return;
        }
        const auto title = wide(value.title);
        const auto first_label = wide(value.buttons[0].label);
        const auto second_label = value.buttons.size() > 1 ? wide(value.buttons[1].label) : std::wstring{};
        const BorrowedString heading(title), first(first_label), second(second_label);
        std::optional<native_calls::LegacyString> legacy_title, legacy_body, legacy_first, legacy_second;
        if (legacy) {
            const auto create = descriptor.address("string_create"), release = descriptor.address("string_release");
            phase_ = "strings";
            legacy_title.emplace(create, release, title);
            legacy_body.emplace(create, release, text);
            legacy_first.emplace(create, release, first_label);
            legacy_second.emplace(create, release, second_label);
        }
        phase_ = "allocate";
        auto* storage = allocate_message();
        if (!storage) { work.complete({value.operation_id, "failed", {}, "allocationFailed"}); return; }
        const auto message_abi = descriptor.value("message_abi");
        const auto constructor = descriptor.address("construct_message");
        const auto center = descriptor.values.contains("center") ? static_cast<int>(descriptor.value("center")) : 0;
        phase_ = "construct";
        auto* dialog = legacy ? native_calls::construct_legacy_message(message_abi, constructor, storage,
            *legacy_title, *legacy_body, static_cast<int>(icon->second), center)
            : native_calls::construct(message_abi, constructor, storage, heading, body,
                static_cast<int>(value.buttons.size()), static_cast<int>(icon->second), center);
        const auto address = reinterpret_cast<uintptr_t>(dialog);
        phase_ = "verify-object";
        if (dialog != storage || pointer(address) != descriptor.address("message_vtable")) throw std::runtime_error("Unexpected native message-box object");
        auto* reference = reinterpret_cast<volatile LONG*>(address + descriptor.value("reference_offset"));
        InterlockedIncrement(reference);
        const auto release = [&] {
            if (static_cast<uint32_t>(InterlockedExchangeAdd(reference, -1)) == descriptor.value("release_previous")) {
                const auto secondary = address + descriptor.value("secondary_offset");
                reinterpret_cast<void(__thiscall*)(void*, int)>(pointer(pointer(secondary) + descriptor.value("destructor_slot")))(reinterpret_cast<void*>(secondary), 1);
            }
        };
        int selected_index = -1;
        bool entered_modal = false;
        const auto cleanup = [&] {
            if (legacy && !entered_modal) native_calls::detach_legacy_message(descriptor.address("detach_message"), dialog);
            release();
        };
        try {
            phase_ = "buttons";
            const auto first_button = pointer(address + descriptor.value("ok_offset"));
            const auto second_button = pointer(address + descriptor.value("cancel_offset"));
            if (pointer(first_button + descriptor.value("command_offset")) != 257 || pointer(second_button + descriptor.value("command_offset")) != 256)
                throw std::runtime_error("Unexpected native message-box button commands");
            set_text(first_button, legacy ? static_cast<const void*>(legacy_first->address()) : &first);
            if (value.buttons.size() == 2) set_text(second_button, legacy ? static_cast<const void*>(legacy_second->address()) : &second);
            // kr-812 sub_8935A0 hides native icons when this owned dialog's flag is set.
            if (descriptor.values.contains("without_icon_offset"))
                *reinterpret_cast<uint8_t*>(address + descriptor.value("without_icon_offset")) = value.icon == "none" ? 1 : 0;
            if (legacy) {
                phase_ = "position";
                const auto position = static_cast<int>(descriptor.value("position"));
                reinterpret_cast<void(__thiscall*)(void*, int, int)>(descriptor.address("position_message"))(dialog, position, position);
            } else reinterpret_cast<void(__thiscall*)(void*)>(descriptor.address("layout_message"))(dialog);
            const auto modal = descriptor.values.contains("modal_slot") ? pointer(pointer(address) + descriptor.value("modal_slot")) : descriptor.address("modal_message");
            if (modal != descriptor.address("modal_message")) throw std::runtime_error("Unexpected native message-box modal method");
            // kr-812 sub_7B1D80 retains the object around this modal call and reads command 257.
            uint8_t interrupted = 0;
            phase_ = "modal";
            work.complete({value.operation_id, "shown", {}, {}});
            entered_modal = true;
            const auto command = native_calls::modal(descriptor.value("modal_abi"), modal, dialog, interrupted);
            selected_index = !interrupted && command == 257 ? 0 : !interrupted && command == 256 && value.buttons.size() == 2 ? 1 : -1;
        } catch (const std::runtime_error&) {
            cleanup();
            if (!legacy || entered_modal) throw;
            runtime->log(runtime->context, "native UI: legacy dialog could not be prepared");
            work.complete({value.operation_id, "failed", {}, "preparationFailed"});
            return;
        } catch (...) { cleanup(); throw; }
        phase_ = "release";
        release();
        work.complete({value.operation_id, selected_index >= 0 ? "selected" : "dismissed", selected_index >= 0 ? value.buttons[static_cast<size_t>(selected_index)].id : "", {}});
    }
public:
    explicit NativeAdapter(size_t queue_limit, ControlLimits limits, uint32_t signature_timeout_ms, uint32_t signature_poll_ms)
        : descriptor_(load_descriptor()), queue_limit_(queue_limit) {
        if (descriptor_ && descriptor_->values.contains("control_abi")) {
            // The host worker can run before the client has unpacked its code.
            // Stage observers only after every descriptor signature is readable and matches.
            if (!await_signatures(signature_timeout_ms, signature_poll_ms, [&] { return descriptor_->matches(); }, GetTickCount64, Sleep))
                throw std::runtime_error("Native control signatures did not become ready before the activation deadline");
            controls_ = std::make_unique<NativeControls>(*descriptor_, limits);
        }
        stop_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        wake_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (stop_ && wake_) dispatcher_ = CreateThread(nullptr, 0, dispatch, this, 0, nullptr);
        if (!dispatcher_) {
            if (stop_) CloseHandle(stop_);
            if (wake_) CloseHandle(wake_);
            throw std::runtime_error("Cannot start native UI dispatcher");
        }
    }
    bool cleanup_available() const { return !controls_ || !controls_->hooks_installed(); }
    ~NativeAdapter() override {
        SetEvent(stop_);
        WaitForSingleObject(dispatcher_, INFINITE);
        CloseHandle(dispatcher_); CloseHandle(wake_); CloseHandle(stop_);
        if (hook_) UnhookWindowsHookEx(hook_);
        active_adapter.store(nullptr);
    }
    std::vector<Capability> capabilities() override {
        const bool supported = descriptor_.has_value() && !failed_.load();
        const bool legacy = supported && descriptor_->legacy_message_box();
        const bool ready = supported && descriptor_->matches() && descriptor_->ui_ready() && connect();
        const auto application = supported && !legacy ? pointer(descriptor_->address("application")) : 0;
        const auto chat = application ? chat_module(application) : 0;
        const bool chat_ready = ready && chat && pointer(chat + descriptor_->value("chat_view_offset"));
        const bool banner_supported = supported && descriptor_->addresses.contains("append_announcement");
        std::vector<std::string> message_features{"title", "text", "buttons:1..2", "buttonLabels", "buttonResult", "icon:notice", "icon:warning", "icon:question", "icon:error"};
        if (legacy) message_features = {"title", "text", "buttons:2", "buttonLabels", "buttonResult", "icon:question"};
        if (supported && descriptor_->values.contains("without_icon_offset")) message_features.push_back("icon:none");
        std::vector<Capability> result{{"messageBox", supported, ready, message_features, supported ? "" : "adapterUnavailable"},
            {"notification", supported && !legacy, ready && !legacy, {"text"}, supported && !legacy ? "" : "adapterUnavailable"},
            {"announcement.show", banner_supported, banner_supported && ready, {"text", "autoExpire"}, banner_supported ? "" : "adapterUnavailable"},
            {"announcement.close", false, false, {}, "automaticExpiryOnly"},
            {"chat", supported && !legacy, chat_ready, {"text", "caption", "color:uniform", "style:normal", "style:system", "localOnly"}, supported && !legacy ? "" : "adapterUnavailable"}};
        const auto control_supported = supported && controls_ && controls_->available();
        // Control ABI 2 (kr-1256) has no verified button factory, so owned buttons are not offered there.
        const auto in_place = supported && descriptor_->values.contains("control_abi") && descriptor_->value("control_abi") == 2;
        for (const auto* kind : {"control.inspect", "control.setText", "control.addButton", "control.remove", "control.activate"}) {
            std::vector<std::string> features{"form", "name", "id", "currentText", "visibleOnly", "callerScoped"};
            if (std::string_view(kind) == "control.setText") { features.push_back("maximumLength"); features.push_back("readback"); }
            if (std::string_view(kind) == "control.addButton") { features.push_back("nativeDefaultSkin"); features.push_back("clickEvents"); }
            if (std::string_view(kind) == "control.remove") features.push_back("ownedOnly");
            if (std::string_view(kind) == "control.activate") { features.push_back("enabled"); features.push_back("selected"); }
            const auto creation = std::string_view(kind) == "control.addButton" || std::string_view(kind) == "control.remove";
            const auto offered = control_supported && !(creation && in_place);
            result.push_back({kind, offered, offered && ready, std::move(features),
                offered ? "" : control_supported ? "controlCreationUnsupported" : "adapterUnavailable"});
        }
        const auto enable_supported = control_supported && (in_place || descriptor_->addresses.contains("control_set_enabled"));
        result.push_back({"control.setEnabled", enable_supported, enable_supported && ready, {"form", "name", "id", "visibleOnly", "callerScoped", "cosmetic"},
            enable_supported ? "" : "adapterUnavailable"});
        result.push_back({"control.events", control_supported, control_supported && ready, {"callerScoped", "cursor", "retained", "lossBoundary"}, control_supported ? "" : "adapterUnavailable"});
        return result;
    }
    std::optional<std::vector<FormState>> forms(const std::string& resource) override {
        if (failed_.load() || !controls_ || !controls_->available() || !descriptor_->matches()) return std::nullopt;
        try { return controls_->forms(resource); }
        catch (const std::exception&) { return std::vector<FormState>{}; }
    }
    void submit(const std::string& caller, Request request, Completion complete) override {
        if (!connect()) { complete({request.operation_id, "notReady", {}, "gameUiNotReady"}); return; }
        std::lock_guard guard(queue_mutex_);
        if (work_.size() >= queue_limit_) { complete({request.operation_id, "busy", {}, "queueFull"}); return; }
        work_.push_back({caller, std::move(request), std::move(complete)});
        if (!SetEvent(wake_)) {
            auto rejected = std::move(work_.back()); work_.pop_back();
            rejected.complete({rejected.request.operation_id, "notReady", {}, "gameUiNotReady"});
        }
    }
};
std::unique_ptr<NativeAdapter> adapter;
std::unique_ptr<Service> service;
int32_t FTHOOK_CALL handle(void*, const char* caller, const char* request, char* response, uint32_t capacity) {
    if (!caller || !request || !response || !capacity) return FTHOOK_INVALID;
    try {
        const auto result = service->handle(caller, request);
        if (result.size() >= capacity) return FTHOOK_BUFFER_TOO_SMALL;
        std::memcpy(response, result.c_str(), result.size() + 1);
        return FTHOOK_OK;
    } catch (const std::invalid_argument&) { return FTHOOK_INVALID; }
    catch (...) { return FTHOOK_FAILED; }
}
int32_t FTHOOK_CALL cleanup(void*) {
    if (adapter && !adapter->cleanup_available()) return FTHOOK_FAILED;
    service.reset(); adapter.reset(); runtime = nullptr;
    return FTHOOK_OK;
}
}
}

extern "C" int32_t FTHOOK_CALL fthook_plugin_main(const fthook_host* host, fthook_plugin* plugin) {
    const auto* services = fthook_get_services(host);
    if (!services || !plugin) return FTHOOK_ABI_UNSUPPORTED;
    using namespace game_controls;
    runtime = host;
    plugin->cleanup = cleanup;
    fthook_option pending{}, results{};
    if (host->option(host->context, "max_pending", &pending) != FTHOOK_OK || pending.kind != FTHOOK_OPTION_INT
        || host->option(host->context, "retained_results", &results) != FTHOOK_OK || results.kind != FTHOOK_OPTION_INT
        || pending.integer <= 0 || results.integer < pending.integer) return FTHOOK_INVALID;
    try {
        const auto option = [&](const char* key) {
            fthook_option value{};
            if (host->option(host->context, key, &value) != FTHOOK_OK || value.kind != FTHOOK_OPTION_INT || value.integer <= 0 || value.integer > INT32_MAX)
                throw std::invalid_argument("Invalid native control resource limit");
            return static_cast<uint32_t>(value.integer);
        };
        const ControlLimits control_limits{option("max_controls"), option("max_tracked_forms"), option("max_tree_nodes"), option("retained_control_events"), option("max_control_text")};
        adapter = std::make_unique<NativeAdapter>(static_cast<size_t>(pending.integer), control_limits,
            option("signature_timeout_ms"), option("signature_poll_ms"));
        service = std::make_unique<Service>(host->build, *adapter, Limits{static_cast<size_t>(pending.integer), static_cast<size_t>(results.integer)});
        return services->register_service(services->context, "game.native-ui", handle, nullptr);
    } catch (const std::exception& error) {
        char message[256]{};
        sprintf_s(message, "native UI: plugin activation failed: %.192s", error.what());
        host->log(host->context, message);
        return FTHOOK_FAILED;
    } catch (...) { host->log(host->context, "native UI: plugin activation failed"); return FTHOOK_FAILED; }
}

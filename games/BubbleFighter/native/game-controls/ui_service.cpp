#include "ui_service.h"
#include <toml.hpp>
#include <algorithm>
#include <array>
#include <cstdint>
#include <map>
#include <mutex>
#include <sstream>
#include <stdexcept>

namespace game_controls {
namespace {
constexpr std::array kinds{"messageBox", "notification", "announcement.show", "announcement.close", "chat",
    "control.inspect", "control.setText", "control.addButton", "control.remove", "control.activate", "control.setEnabled", "control.events"};
bool identity(const std::string& value) {
    return !value.empty() && std::all_of(value.begin(), value.end(), [](unsigned char c) {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
    });
}
std::string form_path(std::string path) {
    std::replace(path.begin(), path.end(), '\\', '/');
    std::transform(path.begin(), path.end(), path.begin(), [](unsigned char c) { return c >= 'A' && c <= 'Z' ? static_cast<char>(c + ('a' - 'A')) : static_cast<char>(c); });
    if (path.starts_with('/') || path.find(':') != std::string::npos || path.find("..") != std::string::npos
        || path.size() < 4 || path.substr(path.size() - 3) != ".ui") throw std::invalid_argument("form must identify a relative .ui resource");
    return path;
}
std::string field(const toml::table& table, const char* key, const char* fallback = "") {
    const auto* node = table.get(key);
    if (!node) return fallback;
    const auto* text = node->as_string();
    if (!text || text->get().find('\0') != std::string::npos) throw std::invalid_argument(std::string(key) + " must be a non-NUL string");
    return text->get();
}
void keys(const toml::table& table, std::initializer_list<std::string_view> allowed) {
    for (const auto& [key, value] : table) {
        (void)value;
        if (std::find(allowed.begin(), allowed.end(), key.str()) == allowed.end()) throw std::invalid_argument("Unknown native UI field: " + std::string(key.str()));
    }
}
Request request(const toml::table& table) {
    keys(table, {"action", "owner_scope", "operation_id", "kind", "text", "title", "icon", "announcement_id", "chat_style", "chat_caption", "buttons", "runs", "target", "definition", "maximum_length", "after_sequence", "enabled"});
    Request result;
    result.operation_id = field(table, "operation_id"); result.kind = field(table, "kind");
    result.text = field(table, "text"); result.title = field(table, "title"); result.icon = field(table, "icon", "notice");
    result.announcement_id = field(table, "announcement_id"); result.chat_style = field(table, "chat_style", "system");
    result.chat_caption = field(table, "chat_caption", result.kind == "chat" && result.chat_style == "system" ? "Fantasy Tale" : "");
    if (table.contains("chat_caption") && result.kind != "chat") throw std::invalid_argument("Only chat accepts a caption");
    if (!identity(result.operation_id) || std::find(kinds.begin(), kinds.end(), result.kind) == kinds.end()) throw std::invalid_argument("Invalid native UI operation identity or kind");
    if (result.kind.starts_with("control.")) {
        for (const auto* key : {"title", "icon", "announcement_id", "chat_style", "chat_caption", "buttons", "runs"})
            if (table.contains(key)) throw std::invalid_argument("Control requests cannot contain presentation fields");
        if (const auto* node = table.get("target")) {
            const auto* target = node->as_table();
            if (!target) throw std::invalid_argument("target must be a table");
            keys(*target, {"form", "name", "id", "text", "array_index"});
            result.target.form = field(*target, "form"); result.target.name = field(*target, "name"); result.target.id = field(*target, "id");
            if (target->contains("text")) result.target.text = field(*target, "text");
            if (const auto* index = target->get("array_index")) {
                const auto value = index->value<int64_t>();
                if (!value || *value < 0 || *value > INT32_MAX) throw std::invalid_argument("array_index must be a nonnegative native array index");
                result.target.array_index = static_cast<int32_t>(*value);
            }
            if (result.target.array_index && result.target.name.empty()) throw std::invalid_argument("array_index requires a native control name");
            auto path = result.target.form;
            std::replace(path.begin(), path.end(), '\\', '/');
            std::transform(path.begin(), path.end(), path.begin(), [](unsigned char c) { return c >= 'A' && c <= 'Z' ? static_cast<char>(c + ('a' - 'A')) : static_cast<char>(c); });
            if (!path.empty() && (path.starts_with('/') || path.find(':') != std::string::npos || path.find("..") != std::string::npos
                || path.size() < 4 || path.substr(path.size() - 3) != ".ui")) throw std::invalid_argument("form must identify a relative .ui resource");
            if (!result.target.id.empty() && !identity(result.target.id)) throw std::invalid_argument("Invalid native control identity");
        }
        if (const auto* node = table.get("definition")) {
            const auto* definition = node->as_table();
            if (result.kind != "control.addButton" || !definition) throw std::invalid_argument("Only control.addButton accepts a definition table");
            keys(*definition, {"name", "template", "x", "y", "width", "height"});
            const auto number = [&](const char* name) {
                const auto value = (*definition)[name].value<int64_t>();
                if (!value || *value < 0 || *value > INT32_MAX) throw std::invalid_argument("Control geometry must be nonnegative integers");
                return static_cast<int32_t>(*value);
            };
            result.definition = ControlDefinition{field(*definition, "name"), field(*definition, "template"), number("x"), number("y"), number("width"), number("height")};
            if (!identity(result.definition->name) || !result.definition->width || !result.definition->height)
                throw std::invalid_argument("An added button needs a stable name and positive dimensions");
        }
        if (const auto* node = table.get("maximum_length")) {
            const auto value = node->value<int64_t>();
            if (result.kind != "control.setText" || !value || *value < 1 || *value > INT32_MAX)
                throw std::invalid_argument("Only control.setText accepts a positive maximum_length");
            result.maximum_length = static_cast<uint32_t>(*value);
        }
        if (const auto* node = table.get("after_sequence")) {
            const auto value = node->value<int64_t>();
            if (result.kind != "control.events" || !value || *value < 0) throw std::invalid_argument("Only control.events accepts a nonnegative after_sequence");
            result.after_sequence = *value;
        }
        if (const auto* node = table.get("enabled")) {
            const auto value = node->value<bool>();
            if (result.kind != "control.setEnabled" || !value) throw std::invalid_argument("Only control.setEnabled accepts a boolean enabled");
            result.enabled = *value;
        }
        if (result.kind == "control.setEnabled" && !result.enabled) throw std::invalid_argument("control.setEnabled needs enabled");
        if (result.kind == "control.events" && table.contains("target")) throw std::invalid_argument("Event polling is caller scoped and accepts no target");
        if (result.kind != "control.inspect" && result.kind != "control.events" && result.target.form.empty()
            && result.target.name.empty() && result.target.id.empty() && !result.target.text && !result.target.array_index)
            throw std::invalid_argument("A native control mutation needs a form, name, text filter or handle");
        if (result.kind == "control.addButton" && !result.definition) throw std::invalid_argument("An added button needs a definition");
        if (result.kind == "control.setText" || result.kind == "control.addButton") {
            if (!table.contains("text")) throw std::invalid_argument("A native text mutation needs text, including an explicit empty string");
        } else if (table.contains("text")) throw std::invalid_argument("This control request accepts no replacement text");
        return result;
    }
    for (const auto* key : {"target", "definition", "maximum_length", "after_sequence", "enabled"})
        if (table.contains(key)) throw std::invalid_argument("Only control requests accept control fields");
    if (const auto* node = table.get("buttons")) {
        const auto* list = node->as_array();
        if (!list) throw std::invalid_argument("buttons must be an array");
        for (const auto& item : *list) {
            const auto* row = item.as_table();
            if (!row) throw std::invalid_argument("A button must be a table");
            keys(*row, {"id", "label"});
            Button button{field(*row, "id"), field(*row, "label")};
            if (!row->contains("label") || !identity(button.id) || std::any_of(result.buttons.begin(), result.buttons.end(), [&](const Button& existing) { return existing.id == button.id; }))
                throw std::invalid_argument("Buttons need unique identities and string labels");
            result.buttons.push_back(std::move(button));
        }
    }
    if (const auto* node = table.get("runs")) {
        const auto* list = node->as_array();
        if (!list) throw std::invalid_argument("runs must be an array");
        for (const auto& item : *list) {
            const auto* row = item.as_table();
            if (!row) throw std::invalid_argument("A text run must be a table");
            keys(*row, {"text", "color"});
            Run run{field(*row, "text"), field(*row, "color")};
            if (run.text.empty() || (run.color.size() != 7 && run.color.size() != 9) || run.color[0] != '#' || !std::all_of(run.color.begin() + 1, run.color.end(), [](unsigned char c) {
                return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            })) throw std::invalid_argument("Text runs require text and an #RRGGBB or #RRGGBBAA color");
            result.runs.push_back(std::move(run));
        }
    }
    if (result.kind == "messageBox") {
        if (result.buttons.empty() || (result.icon != "none" && result.icon != "notice" && result.icon != "warning" && result.icon != "question" && result.icon != "error"))
            throw std::invalid_argument("Invalid message-box buttons or icon");
    } else if (!result.buttons.empty() || !result.title.empty()) throw std::invalid_argument("Only message boxes accept titles and buttons");
    const bool announcement = result.kind.starts_with("announcement.");
    if ((!announcement && !result.announcement_id.empty()) || (announcement && !result.announcement_id.empty() && !identity(result.announcement_id))
        || (result.kind == "announcement.close" && result.announcement_id.empty())) throw std::invalid_argument("Invalid announcement ownership identity");
    if (result.kind == "announcement.close" && !result.text.empty()) throw std::invalid_argument("Closing an announcement accepts no text");
    if (result.kind == "chat") {
        if (!result.text.empty() || result.runs.empty() || (result.chat_style != "normal" && result.chat_style != "system")) throw std::invalid_argument("Invalid local chat entry");
    } else if (!result.runs.empty()) throw std::invalid_argument("Only chat accepts text runs");
    return result;
}
std::string json(const toml::table& table) { std::ostringstream output; output << toml::json_formatter{table}; return output.str(); }
std::string json(const Result& result) {
    toml::table table{{"operationId", result.operation_id}, {"status", result.status}};
    if (!result.button_id.empty()) table.insert("buttonId", result.button_id);
    if (!result.reason.empty()) table.insert("reason", result.reason);
    if (!result.controls.empty()) {
        toml::array controls;
        for (const auto& control : result.controls) {
            toml::table row{{"id", control.id}, {"form", control.form}, {"name", control.name}, {"kind", control.kind},
                {"text", control.text}, {"writable", control.writable}, {"enabled", control.enabled}, {"x", control.x}, {"y", control.y}, {"width", control.width}, {"height", control.height}};
            if (control.maximum_length) row.insert("maximumLength", static_cast<int64_t>(*control.maximum_length));
            if (control.array_index) row.insert("arrayIndex", *control.array_index);
            if (control.selection_value) row.insert("selectionValue", *control.selection_value);
            if (control.selected) row.insert("selected", *control.selected);
            controls.push_back(std::move(row));
        }
        table.insert("controls", std::move(controls));
    }
    if (result.cursor) {
        toml::array events;
        for (const auto& event : result.events) events.push_back(toml::table{{"sequence", event.sequence}, {"controlId", event.control_id}, {"event", event.event}});
        table.insert("events", std::move(events));
        table.insert("cursor", *result.cursor);
    }
    return json(table);
}
bool pending(const Result& result, const Request& value) { return result.status == "queued" || (value.kind == "messageBox" && result.status == "shown"); }
bool valid_result(const Result& result, const Request& value) {
    if (result.operation_id != value.operation_id) return false;
    if ((!result.controls.empty() || !result.events.empty() || result.cursor) && !value.kind.starts_with("control.")) return false;
    if ((!result.events.empty() || result.cursor) && value.kind != "control.events") return false;
    // A selection button reports its value and selected state together; the launcher rejects a snapshot with only one.
    if (std::any_of(result.controls.begin(), result.controls.end(), [](const ControlInfo& control) {
        return control.selection_value.has_value() != control.selected.has_value(); })) return false;
    if (result.status == "selected") return value.kind == "messageBox" && std::any_of(value.buttons.begin(), value.buttons.end(), [&](const Button& button) { return button.id == result.button_id; });
    if (!result.button_id.empty()) return false;
    if (result.status == "completed" || result.status == "ambiguous") return value.kind.starts_with("control.");
    if (result.status == "shown") return value.kind == "messageBox" || value.kind == "notification" || value.kind == "announcement.show";
    if (result.status == "dismissed") return value.kind == "messageBox";
    if (result.status == "closed") return value.kind == "announcement.close";
    if (result.status == "inserted") return value.kind == "chat";
    return result.status == "queued" || result.status == "unsupported" || result.status == "notReady" || result.status == "busy" || result.status == "failed" || result.status == "outcomeUnknown";
}
}
struct Service::State {
    struct Entry { Request request; Result result; uint64_t sequence; };
    std::mutex mutex;
    std::map<std::pair<std::string, std::string>, std::shared_ptr<Entry>> entries;
    Limits limits;
    uint64_t sequence = 0;
};
Service::Service(std::string build, Adapter& adapter, Limits limits) : state_(std::make_shared<State>()), build_(std::move(build)), adapter_(adapter) {
    if (!limits.pending || limits.results < limits.pending) throw std::invalid_argument("Native UI limits must retain every pending operation");
    state_->limits = limits;
}
Service::~Service() = default;
std::string Service::handle(const std::string& caller, const std::string& payload) {
    if (!identity(caller)) throw std::invalid_argument("Invalid native UI caller identity");
    auto parsed = toml::parse(payload);
    if (!parsed) throw std::invalid_argument("Malformed native UI TOML");
    const auto& table = parsed.table();
    const auto owner_scope = field(table, "owner_scope");
    if (!owner_scope.empty() && !identity(owner_scope)) throw std::invalid_argument("Invalid native UI owner scope");
    // The runtime caller remains authoritative; a host scope only subdivides that caller's state.
    const auto owner = owner_scope.empty() ? caller : caller + ":" + owner_scope;
    const auto action = field(table, "action");
    if (action == "capabilities") {
        keys(table, {"action", "owner_scope"});
        toml::array operations;
        for (const auto& operation : adapter_.capabilities()) {
            toml::array features;
            for (const auto& feature : operation.features) features.push_back(feature);
            operations.push_back(toml::table{{"kind", operation.kind}, {"supported", operation.supported}, {"ready", operation.ready}, {"features", std::move(features)}, {"reason", operation.reason}});
        }
        return json(toml::table{{"build", build_}, {"operations", std::move(operations)}});
    }
    if (action == "forms") {
        // A read of the load/destroy tracker: answered here, never queued behind game-thread UI work.
        keys(table, {"action", "owner_scope", "resource"});
        const auto states = adapter_.forms(form_path(field(table, "resource")));
        if (!states) return json(toml::table{{"status", "unsupported"}, {"reason", "adapterUnavailable"}});
        toml::array forms;
        for (const auto& state : *states) {
            toml::table form{{"resource", state.resource}, {"sequence", static_cast<int64_t>(state.sequence)}, {"visible", state.visible},
                {"x", state.x}, {"y", state.y}, {"width", state.width}, {"height", state.height},
                {"rootWidth", state.root_width}, {"rootHeight", state.root_height}};
            if (state.selection) form.insert("selection", *state.selection);
            form.insert("clicks", static_cast<int64_t>(state.clicks));
            forms.push_back(std::move(form));
        }
        return json(toml::table{{"status", "completed"}, {"forms", std::move(forms)}});
    }
    const auto operation_id = field(table, "operation_id");
    if (!identity(operation_id)) throw std::invalid_argument("Invalid native UI operation identity");
    const auto key = std::make_pair(owner, operation_id);
    if (action == "result") {
        keys(table, {"action", "owner_scope", "operation_id"});
        std::lock_guard guard(state_->mutex);
        const auto found = state_->entries.find(key);
        return json(found == state_->entries.end() ? Result{operation_id, "outcomeUnknown", {}, "operationUnavailable"} : found->second->result);
    }
    if (action != "invoke") throw std::invalid_argument("Unknown native UI action");
    auto value = request(table);
    const auto capabilities = adapter_.capabilities();
    std::shared_ptr<State::Entry> entry;
    {
        std::lock_guard guard(state_->mutex);
        if (const auto found = state_->entries.find(key); found != state_->entries.end())
            return json(found->second->request == value ? found->second->result : Result{operation_id, "failed", {}, "operationConflict"});
        const auto capability = std::find_if(capabilities.begin(), capabilities.end(), [&](const Capability& item) { return item.kind == value.kind; });
        if (capability == capabilities.end() || !capability->supported) return json(Result{operation_id, "unsupported", {}, "adapterUnavailable"});
        if (!capability->ready) return json(Result{operation_id, "notReady", {}, "gameUiNotReady"});
        const auto active = std::count_if(state_->entries.begin(), state_->entries.end(), [](const auto& item) { return pending(item.second->result, item.second->request); });
        if (static_cast<size_t>(active) >= state_->limits.pending) return json(Result{operation_id, "busy", {}, "queueFull"});
        if (state_->entries.size() >= state_->limits.results) {
            auto oldest = state_->entries.end();
            for (auto it = state_->entries.begin(); it != state_->entries.end(); ++it)
                if (!pending(it->second->result, it->second->request) && (oldest == state_->entries.end() || it->second->sequence < oldest->second->sequence)) oldest = it;
            if (oldest == state_->entries.end()) return json(Result{operation_id, "busy", {}, "queueFull"});
            state_->entries.erase(oldest);
        }
        entry = std::make_shared<State::Entry>(State::Entry{value, Result{operation_id, "queued", {}, {}}, ++state_->sequence});
        state_->entries.emplace(key, entry);
    }
    std::weak_ptr<State> weak = state_;
    auto complete = [weak, key, entry](Result result) {
        const auto state = weak.lock();
        if (!state) return;
        std::lock_guard guard(state->mutex);
        const auto found = state->entries.find(key);
        if (found == state->entries.end() || found->second != entry || !pending(entry->result, entry->request)) return;
        entry->result = valid_result(result, entry->request) ? std::move(result) : Result{entry->request.operation_id, "outcomeUnknown", {}, "invalidAdapterResult"};
    };
    try { adapter_.submit(owner, std::move(value), complete); }
    catch (...) { complete(Result{operation_id, "outcomeUnknown", {}, "adapterInterrupted"}); }
    std::lock_guard guard(state_->mutex);
    return json(entry->result);
}
}

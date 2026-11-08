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
constexpr std::array kinds{"messageBox", "notification", "announcement.show", "announcement.close", "chat"};
bool identity(const std::string& value) {
    return !value.empty() && std::all_of(value.begin(), value.end(), [](unsigned char c) {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
    });
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
    keys(table, {"action", "operation_id", "kind", "text", "title", "icon", "announcement_id", "chat_style", "chat_caption", "buttons", "runs"});
    Request result;
    result.operation_id = field(table, "operation_id"); result.kind = field(table, "kind");
    result.text = field(table, "text"); result.title = field(table, "title"); result.icon = field(table, "icon", "notice");
    result.announcement_id = field(table, "announcement_id"); result.chat_style = field(table, "chat_style", "system");
    result.chat_caption = field(table, "chat_caption", result.kind == "chat" && result.chat_style == "system" ? "Fantasy Tale" : "");
    if (table.contains("chat_caption") && result.kind != "chat") throw std::invalid_argument("Only chat accepts a caption");
    if (!identity(result.operation_id) || std::find(kinds.begin(), kinds.end(), result.kind) == kinds.end()) throw std::invalid_argument("Invalid native UI operation identity or kind");
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
    return json(table);
}
bool pending(const Result& result, const Request& value) { return result.status == "queued" || (value.kind == "messageBox" && result.status == "shown"); }
bool valid_result(const Result& result, const Request& value) {
    if (result.operation_id != value.operation_id) return false;
    if (result.status == "selected") return value.kind == "messageBox" && std::any_of(value.buttons.begin(), value.buttons.end(), [&](const Button& button) { return button.id == result.button_id; });
    if (!result.button_id.empty()) return false;
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
    const auto action = field(table, "action");
    if (action == "capabilities") {
        keys(table, {"action"});
        toml::array operations;
        for (const auto& operation : adapter_.capabilities()) {
            toml::array features;
            for (const auto& feature : operation.features) features.push_back(feature);
            operations.push_back(toml::table{{"kind", operation.kind}, {"supported", operation.supported}, {"ready", operation.ready}, {"features", std::move(features)}, {"reason", operation.reason}});
        }
        return json(toml::table{{"build", build_}, {"operations", std::move(operations)}});
    }
    const auto operation_id = field(table, "operation_id");
    if (!identity(operation_id)) throw std::invalid_argument("Invalid native UI operation identity");
    const auto key = std::make_pair(caller, operation_id);
    if (action == "result") {
        keys(table, {"action", "operation_id"});
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
    try { adapter_.submit(caller, std::move(value), complete); }
    catch (...) { complete(Result{operation_id, "outcomeUnknown", {}, "adapterInterrupted"}); }
    std::lock_guard guard(state_->mutex);
    return json(entry->result);
}
}

#pragma once
#include "ui_service.h"
#include <algorithm>
#include <map>
#include <deque>
#include <set>
#include <stdexcept>

namespace game_controls {
struct ControlSnapshot { uintptr_t object = 0, form_object = 0; ControlInfo info; };
enum class ButtonActivation { unsupported, disabled, already_selected, ready };
inline ButtonActivation button_activation(const ControlInfo& control) {
    if (control.kind != "button") return ButtonActivation::unsupported;
    if (control.selected == true) return ButtonActivation::already_selected;
    return control.enabled ? ButtonActivation::ready : ButtonActivation::disabled;
}

/// Retains click events without consuming them, including an explicit loss boundary per owner.
class ControlEvents {
    struct Cursor { int64_t latest = 0, lost = 0; };
    struct Entry { std::string caller; ControlEvent event; };
    std::map<std::string, Cursor> callers_;
    std::deque<Entry> events_;
    size_t owner_limit_, event_limit_;
public:
    ControlEvents(size_t owners, size_t events) : owner_limit_(owners), event_limit_(events) {
        if (!owners || !events) throw std::invalid_argument("Native control events must be bounded");
    }
    void reserve(const std::string& caller) {
        if (!callers_.contains(caller) && callers_.size() >= owner_limit_) throw std::length_error("controlOwnerLimit");
        callers_.try_emplace(caller);
    }
    void click(const std::string& caller, const std::string& control) {
        auto& cursor = callers_.at(caller);
        if (events_.size() >= event_limit_) {
            auto& oldest = events_.front();
            callers_.at(oldest.caller).lost = oldest.event.sequence;
            events_.pop_front();
        }
        events_.push_back({caller, {++cursor.latest, control, "click"}});
    }
    Result poll(const std::string& caller, const Request& request) const {
        Result result{request.operation_id, "completed", {}, {}};
        const auto found = callers_.find(caller);
        const auto cursor = found == callers_.end() ? Cursor{} : found->second;
        result.cursor = cursor.latest;
        if (request.after_sequence < cursor.lost) { result.status = "failed"; result.reason = "eventHistoryLost"; return result; }
        if (request.after_sequence > cursor.latest) { result.status = "failed"; result.reason = "invalidEventCursor"; return result; }
        for (const auto& entry : events_) if (entry.caller == caller && entry.event.sequence > request.after_sequence) result.events.push_back(entry.event);
        return result;
    }
};

/// Issues opaque caller-scoped identities; every use is resolved against a fresh visible tree.
class ControlRegistry {
    struct Handle { std::string caller; uintptr_t object; };
    std::map<std::string, Handle> handles_;
    uint64_t sequence_ = 0;
    size_t limit_;
    static std::string path(std::string value) {
        for (auto& c : value) {
            if (c == '\\') c = '/';
            else if (c >= 'A' && c <= 'Z') c = static_cast<char>(c + ('a' - 'A'));
        }
        return value;
    }
public:
    explicit ControlRegistry(size_t limit) : limit_(limit) { if (!limit) throw std::invalid_argument("A native control registry must be bounded"); }
    void invalidate(uintptr_t object) {
        std::erase_if(handles_, [&](const auto& item) { return item.second.object == object; });
    }
    void refresh(const std::vector<ControlSnapshot>& tree) {
        std::set<uintptr_t> visible;
        for (const auto& control : tree) visible.insert(control.object);
        std::erase_if(handles_, [&](const auto& item) { return !visible.contains(item.second.object); });
    }
    std::vector<ControlSnapshot> select(const std::string& caller, const Request& request, const std::vector<ControlSnapshot>& tree) const {
        uintptr_t object = 0;
        if (!request.target.id.empty()) {
            const auto found = handles_.find(request.target.id);
            if (found == handles_.end() || found->second.caller != caller) return {};
            object = found->second.object;
        }
        std::vector<ControlSnapshot> selected;
        for (const auto& control : tree) {
            if (object && object != control.object) continue;
            if (!request.target.form.empty() && path(request.target.form) != path(control.info.form)) continue;
            if (!request.target.name.empty() && request.target.name != control.info.name) continue;
            if (request.target.array_index && request.target.array_index != control.info.array_index) continue;
            if (request.target.text && *request.target.text != control.info.text) continue;
            const bool form_only = !request.target.form.empty() && request.target.name.empty() && !request.target.text && !request.target.array_index && !object;
            if (request.kind != "control.inspect" && form_only && control.object != control.form_object) continue;
            if (request.kind == "control.addButton" && control.object != control.form_object) continue;
            selected.push_back(control);
        }
        return selected;
    }
    std::vector<ControlInfo> describe(const std::string& caller, const std::vector<ControlSnapshot>& controls) {
        size_t needed = 0;
        for (const auto& control : controls)
            if (std::none_of(handles_.begin(), handles_.end(), [&](const auto& item) { return item.second.caller == caller && item.second.object == control.object; })) ++needed;
        if (needed > limit_ - handles_.size()) throw std::length_error("controlHandleLimit");
        std::vector<ControlInfo> result;
        for (const auto& control : controls) {
            auto existing = std::find_if(handles_.begin(), handles_.end(), [&](const auto& item) { return item.second.caller == caller && item.second.object == control.object; });
            if (existing == handles_.end()) existing = handles_.emplace("control-" + std::to_string(++sequence_), Handle{caller, control.object}).first;
            auto info = control.info;
            info.id = existing->first;
            result.push_back(std::move(info));
        }
        return result;
    }
};
}

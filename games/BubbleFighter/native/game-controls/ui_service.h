#pragma once
#include <functional>
#include <memory>
#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace game_controls {
struct Button { std::string id, label; bool operator==(const Button&) const = default; };
struct Run { std::string text, color; bool operator==(const Run&) const = default; };
struct ControlTarget {
    std::string form, name, id;
    std::optional<std::string> text;
    std::optional<int32_t> array_index;
    bool operator==(const ControlTarget&) const = default;
};
struct ControlDefinition {
    std::string name, template_name;
    int32_t x = 0, y = 0, width = 0, height = 0;
    bool operator==(const ControlDefinition&) const = default;
};
struct ControlInfo {
    std::string id, form, name, kind, text;
    bool writable = false;
    int32_t x = 0, y = 0, width = 0, height = 0;
    std::optional<uint32_t> maximum_length;
    bool enabled = false;
    std::optional<int32_t> array_index, selection_value;
    std::optional<bool> selected;
};
struct ControlEvent { int64_t sequence; std::string control_id, event; };
struct Request {
    std::string operation_id, kind, text, title, icon = "notice", announcement_id, chat_style = "system", chat_caption;
    std::vector<Button> buttons;
    std::vector<Run> runs;
    ControlTarget target;
    std::optional<ControlDefinition> definition;
    std::optional<uint32_t> maximum_length;
    int64_t after_sequence = 0;
    std::optional<bool> enabled;
    bool operator==(const Request&) const = default;
};
struct Result {
    std::string operation_id, status, button_id, reason;
    std::vector<ControlInfo> controls;
    std::vector<ControlEvent> events;
    std::optional<int64_t> cursor;
};
struct Capability { std::string kind; bool supported = false, ready = false; std::vector<std::string> features; std::string reason; };
/// One loaded native form: its resource, a load sequence that changes on every load, and its current geometry.
struct FormState {
    std::string resource;
    uint64_t sequence = 0;
    bool visible = false;
    int32_t x = 0, y = 0, width = 0, height = 0, root_width = 0, root_height = 0;
    /// The form's selection-group value (the Create Room dialog's chosen mode), when the descriptor names one.
    std::optional<int32_t> selection;
    /// Native button clicks so far in the whole client; a change means the player pressed something.
    uint64_t clicks = 0;
};
struct Limits { size_t pending, results; };

/// The adapter schedules UI work and completes operations without holding a service lock.
class Adapter {
public:
    using Completion = std::function<void(Result)>;
    virtual ~Adapter() = default;
    virtual std::vector<Capability> capabilities() = 0;
    virtual void submit(const std::string& caller, Request request, Completion complete) = 0;
    /// Reads the loaded forms of one resource without queuing UI work; nullopt when form tracking is unavailable.
    virtual std::optional<std::vector<FormState>> forms(const std::string& resource) { (void)resource; return std::nullopt; }
};

/// Owns caller-scoped operations, bounded results and duplicate suppression for one launch.
class Service {
public:
    Service(std::string build, Adapter& adapter, Limits limits);
    ~Service();
    Service(const Service&) = delete;
    Service& operator=(const Service&) = delete;
    /// Accepts strict TOML and returns JSON; malformed requests throw before presentation.
    std::string handle(const std::string& caller, const std::string& payload);
private:
    struct State;
    std::shared_ptr<State> state_;
    std::string build_;
    Adapter& adapter_;
};
}

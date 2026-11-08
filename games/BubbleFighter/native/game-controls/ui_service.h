#pragma once
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace game_controls {
struct Button { std::string id, label; bool operator==(const Button&) const = default; };
struct Run { std::string text, color; bool operator==(const Run&) const = default; };
struct Request {
    std::string operation_id, kind, text, title, icon = "notice", announcement_id, chat_style = "system", chat_caption;
    std::vector<Button> buttons;
    std::vector<Run> runs;
    bool operator==(const Request&) const = default;
};
struct Result { std::string operation_id, status, button_id, reason; };
struct Capability { std::string kind; bool supported = false, ready = false; std::vector<std::string> features; std::string reason; };
struct Limits { size_t pending, results; };

/// The adapter schedules UI work and completes operations without holding a service lock.
class Adapter {
public:
    using Completion = std::function<void(Result)>;
    virtual ~Adapter() = default;
    virtual std::vector<Capability> capabilities() = 0;
    virtual void submit(const std::string& caller, Request request, Completion complete) = 0;
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

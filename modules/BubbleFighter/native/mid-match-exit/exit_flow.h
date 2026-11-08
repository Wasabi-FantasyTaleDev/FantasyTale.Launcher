#pragma once
#include "../common/session_state.h"
#include <string>
#include <string_view>

namespace mid_match_exit {
enum class Delivery { response, unavailable, uncertain };
struct Reply { Delivery delivery; std::string json; };
enum class Choice { yes, no, failed };
enum class Outcome { canceled, completed, unavailable, rejected, uncertain };

// All potentially blocking calls run on one worker. A canceled scene never sends a command.
struct Transport {
    virtual ~Transport() = default;
    virtual bubblefighter::session_state::Result session_state() = 0;
    virtual bool has_native_exit(int32_t mode) const = 0;
    virtual Reply native_ui(std::string_view request) = 0;
    virtual Choice windows_confirmation() = 0;
    virtual Reply execute(std::string_view operation, std::string_view payload) = 0;
    virtual bool canceled() = 0;
    virtual bool wait_for_result() = 0;
};

// Prefer native UI; only a definite failure before presentation permits a second modal.
Outcome confirm_and_execute(Transport& transport, std::string_view operation);
}

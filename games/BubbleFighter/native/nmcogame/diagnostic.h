#pragma once

#include <cstdint>

namespace bubblefighter::diagnostic {

// The bridge and host exchange only bounded numeric events. Strings and buffers
// from the client are deliberately not part of this ABI.
enum Event : uint32_t {
    Initialize = 1,
    Signature = 2,
    Active = 3,
    GetModule = 4,
    Login = 5,
    CenterResume = 6,
    NmcoCall = 7,
    Terminal = 8,
    LoginReplyResult = 9,
    LoginConnectionState = 10,
    NativeSelector = 11,
};

enum Module : uint32_t {
    UnknownModule = 0,
    DisplayModule = 1,
    LoginModule = 2,
};

enum Stage : uint32_t {
    UnknownStage = 0,
    Decoded = 1,
    Unreadable = 2,
    Unhandled = 3,
};

enum CenterAction : uint32_t {
    Inspect = 1,
    Refused = 2,
    Delivered = 3,
    NativeResume = 4,
};

enum CenterFlag : uint32_t {
    ActiveFlag = 1u,
    PendingFlag = 2u,
    SelectedFlag = 4u,
    AccountValidFlag = 8u,
    TicketValidFlag = 16u,
};

using Sink = void (__cdecl*)(uint32_t event, uint32_t value0, uint32_t value1, uint32_t value2);

} // namespace bubblefighter::diagnostic

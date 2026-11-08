#pragma once
#include "session.h"
#include <cstring>
#include <string_view>

namespace fthook {
/// Copies a NUL-terminated immutable launch value; returns required bytes including NUL.
/// A null buffer with zero capacity queries size; -1 rejects arguments, -2 rejects a short buffer without writing.
inline int32_t copy_launch_value(std::string_view value, char* buffer, uint32_t capacity) {
    if ((!buffer && capacity) || (buffer && !capacity)) return -1;
    const auto required = static_cast<uint32_t>(value.size() + 1);
    if (!buffer) return static_cast<int32_t>(required);
    if (capacity < required) return -2;
    std::memcpy(buffer, value.data(), value.size());
    buffer[value.size()] = '\0';
    return static_cast<int32_t>(required);
}
/// Returns zero for an absent capability, otherwise copies the session's opaque module payload.
/// Only exact capability matches are accepted; this function never interprets game-specific JSON.
inline int32_t copy_launch_metadata(const Session& session, std::string_view capability, char* buffer, uint32_t capacity) {
    if (capability.empty() || capability.size() > 128 || (!buffer && capacity) || (buffer && !capacity)) return -1;
    if (!session.module_data || session.module_data->capability != capability) return 0;
    return copy_launch_value(session.module_data->payload, buffer, capacity);
}
}

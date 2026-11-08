#pragma once
// Masked byte patterns written as hex text ("83 3D ?? ?? 02 75 03").
#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace fthook {

/// A byte sequence where each position is either a fixed byte or a wildcard.
struct Pattern {
    std::vector<uint8_t> bytes; ///< Expected byte at each position (0 where wildcard).
    std::vector<uint8_t> mask;  ///< 0xFF for a fixed byte, 0x00 for a wildcard.

    /// Number of bytes the pattern spans.
    size_t size() const noexcept { return bytes.size(); }
    /// True when every fixed byte equals the byte at the same position of `data`,
    /// which must hold at least size() bytes.
    bool matches(const uint8_t* data) const noexcept;
    /// True when the pattern has no wildcard.
    bool is_exact() const noexcept;
};

/// Parses whitespace-separated two-digit hex tokens; "??" or "?" is a wildcard.
/// Returns nullopt and sets `error` for empty input, odd tokens or non-hex digits.
std::optional<Pattern> parse_pattern(std::string_view text, std::string& error);

} // namespace fthook

#pragma once
// SemVer 2.0 versions and simple comparator ranges.
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>

namespace fthook {

/// A parsed semantic version. Build metadata is ignored for ordering.
struct SemVer {
    uint32_t major = 0, minor = 0, patch = 0;
    std::string prerelease;   ///< Empty for a release.

    /// Three-way comparison following SemVer 2.0 precedence rules.
    int compare(const SemVer& other) const noexcept;
};

/// Parses "MAJOR.MINOR.PATCH[-pre][+build]". Returns nullopt on malformed input.
std::optional<SemVer> parse_semver(std::string_view text);

/// True when `version` satisfies every space-separated comparator in `range`
/// (">=3.0.0 <4.0.0"; operators >=, >, <=, <, =, or a bare version meaning =).
/// Returns nullopt when the range is malformed.
std::optional<bool> satisfies(const SemVer& version, std::string_view range);

} // namespace fthook

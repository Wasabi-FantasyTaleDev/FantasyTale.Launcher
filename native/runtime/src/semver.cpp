#include "fthook/semver.h"

#include <charconv>

namespace fthook {
namespace {
bool parse_number(std::string_view& text, uint32_t& out) {
    if (text.empty() || (text.size() > 1 && text[0] == '0' && text[1] >= '0' && text[1] <= '9')) return false;
    const auto [end, ec] = std::from_chars(text.data(), text.data() + text.size(), out);
    if (ec != std::errc() || end == text.data()) return false;
    text.remove_prefix(static_cast<size_t>(end - text.data()));
    return true;
}

bool is_numeric(std::string_view s) {
    if (s.empty()) return false;
    for (char c : s)
        if (c < '0' || c > '9') return false;
    return true;
}

// SemVer 2.0 section 11: dot-separated identifiers, numeric ones compare numerically and
// rank below alphanumeric ones; a shorter list ranks lower when all shared ones are equal.
int compare_prerelease(std::string_view a, std::string_view b) {
    while (!a.empty() && !b.empty()) {
        const size_t da = a.find('.'), db = b.find('.');
        const std::string_view ia = a.substr(0, da), ib = b.substr(0, db);
        const bool na = is_numeric(ia), nb = is_numeric(ib);
        int c = 0;
        if (na && nb) c = ia.size() != ib.size() ? (ia.size() < ib.size() ? -1 : 1) : ia.compare(ib);
        else if (na != nb) c = na ? -1 : 1;
        else c = ia.compare(ib);
        if (c != 0) return c < 0 ? -1 : 1;
        a = da == std::string_view::npos ? std::string_view() : a.substr(da + 1);
        b = db == std::string_view::npos ? std::string_view() : b.substr(db + 1);
    }
    return a.empty() == b.empty() ? 0 : (a.empty() ? -1 : 1);
}
}

int SemVer::compare(const SemVer& other) const noexcept {
    if (major != other.major) return major < other.major ? -1 : 1;
    if (minor != other.minor) return minor < other.minor ? -1 : 1;
    if (patch != other.patch) return patch < other.patch ? -1 : 1;
    if (prerelease.empty() != other.prerelease.empty()) return prerelease.empty() ? 1 : -1;
    return compare_prerelease(prerelease, other.prerelease);
}

std::optional<SemVer> parse_semver(std::string_view text) {
    if (const size_t plus = text.find('+'); plus != std::string_view::npos) text = text.substr(0, plus);
    SemVer v;
    if (!parse_number(text, v.major) || text.empty() || text[0] != '.') return std::nullopt;
    text.remove_prefix(1);
    if (!parse_number(text, v.minor) || text.empty() || text[0] != '.') return std::nullopt;
    text.remove_prefix(1);
    if (!parse_number(text, v.patch)) return std::nullopt;
    if (!text.empty()) {
        if (text[0] != '-' || text.size() == 1) return std::nullopt;
        v.prerelease = std::string(text.substr(1));
    }
    return v;
}

std::optional<bool> satisfies(const SemVer& version, std::string_view range) {
    bool any = false;
    while (!range.empty()) {
        while (!range.empty() && range[0] == ' ') range.remove_prefix(1);
        if (range.empty()) break;
        size_t end = range.find(' ');
        std::string_view token = range.substr(0, end);
        range = end == std::string_view::npos ? std::string_view() : range.substr(end);
        std::string_view op = "=";
        for (std::string_view candidate : {">=", "<=", ">", "<", "="})
            if (token.substr(0, candidate.size()) == candidate) { op = candidate; token.remove_prefix(candidate.size()); break; }
        const auto bound = parse_semver(token);
        if (!bound) return std::nullopt;
        const int c = version.compare(*bound);
        const bool ok = op == ">=" ? c >= 0 : op == "<=" ? c <= 0 : op == ">" ? c > 0 : op == "<" ? c < 0 : c == 0;
        if (!ok) return false;
        any = true;
    }
    if (!any) return std::nullopt;
    return true;
}

} // namespace fthook

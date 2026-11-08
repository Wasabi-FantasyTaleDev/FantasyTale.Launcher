#include "fthook/pattern.h"

#include <algorithm>

namespace fthook {
namespace {
int hex_value(char c) noexcept {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;
}
bool is_space(char c) noexcept { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }
}

bool Pattern::matches(const uint8_t* data) const noexcept {
    for (size_t i = 0; i < bytes.size(); ++i)
        if ((data[i] & mask[i]) != bytes[i]) return false;
    return true;
}

bool Pattern::is_exact() const noexcept {
    return std::all_of(mask.begin(), mask.end(), [](uint8_t m) { return m == 0xFF; });
}

std::optional<Pattern> parse_pattern(std::string_view text, std::string& error) {
    Pattern pattern;
    size_t i = 0;
    while (i < text.size()) {
        if (is_space(text[i])) { ++i; continue; }
        size_t end = i;
        while (end < text.size() && !is_space(text[end])) ++end;
        const std::string_view token = text.substr(i, end - i);
        if (token == "?" || token == "??") {
            pattern.bytes.push_back(0);
            pattern.mask.push_back(0);
        } else if (token.size() == 2 && hex_value(token[0]) >= 0 && hex_value(token[1]) >= 0) {
            pattern.bytes.push_back(static_cast<uint8_t>(hex_value(token[0]) << 4 | hex_value(token[1])));
            pattern.mask.push_back(0xFF);
        } else {
            error = "invalid byte token '" + std::string(token) + "'";
            return std::nullopt;
        }
        i = end;
    }
    if (pattern.bytes.empty()) {
        error = "empty byte pattern";
        return std::nullopt;
    }
    return pattern;
}

} // namespace fthook

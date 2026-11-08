#pragma once
// A strict, bounded JSON reader and a string quoter for the messenger HTTP API. Replies are small
// server documents; anything malformed, deeper than kMaxDepth or larger than kMaxBytes is refused.
#include <cstdint>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace bubblefighter::json {

constexpr size_t kMaxDepth = 16;
constexpr size_t kMaxBytes = 1u << 20;

/// One parsed value. Numbers keep their integer value when they have no fraction or exponent.
struct Value {
    enum class Kind { Null, Bool, Number, String, Array, Object };
    Kind kind = Kind::Null;
    bool boolean = false;
    bool integral = false;
    int64_t number = 0;
    std::string text;                                  ///< UTF-8 string value.
    std::vector<Value> items;                          ///< Array elements.
    std::vector<std::pair<std::string, Value>> members; ///< Object members in document order.

    /// The member named `key`, or null.
    const Value* get(std::string_view key) const;
    /// The integer member `key` within [minimum, maximum].
    bool integer(std::string_view key, int64_t minimum, int64_t maximum, int64_t& out) const;
    /// The string member `key`.
    bool string(std::string_view key, std::string& out) const;
    /// The boolean member `key`.
    bool flag(std::string_view key, bool& out) const;
};

/// Parses a complete document; false on any syntax error, trailing text or limit.
bool parse(std::string_view text, Value& out);
/// A JSON string literal for UTF-8 text, with control characters escaped.
std::string quote(std::string_view utf8);

} // namespace bubblefighter::json

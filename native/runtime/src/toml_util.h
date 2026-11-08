#pragma once
// Strict helpers over toml++ shared by the manifest and session parsers.
#include <toml.hpp>

#include <initializer_list>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace fthook::detail {

/// Accumulates the first error with its key path.
struct Errors {
    std::string first;
    bool fail(const std::string& path, const std::string& reason) {
        if (first.empty()) first = path + ": " + reason;
        return false;
    }
    bool ok() const noexcept { return first.empty(); }
};

/// Rejects keys of `table` not listed in `allowed`.
inline bool only_keys(const toml::table& table, std::initializer_list<std::string_view> allowed, const std::string& path, Errors& errors) {
    for (auto&& [key, node] : table) {
        (void)node;
        bool known = false;
        for (std::string_view a : allowed) known = known || key.str() == a;
        if (!known) return errors.fail(path.empty() ? std::string(key.str()) : path + "." + std::string(key.str()), "unknown key");
    }
    return true;
}

inline std::string join(const std::string& path, std::string_view key) {
    return path.empty() ? std::string(key) : path + "." + std::string(key);
}

/// Reads an optional string; a present non-string value is an error.
inline bool get_string(const toml::table& t, std::string_view key, const std::string& path, std::string& out, Errors& e, bool required = false) {
    const toml::node* node = t.get(key);
    if (!node) return required ? e.fail(join(path, key), "missing") : true;
    const auto* s = node->as_string();
    if (!s) return e.fail(join(path, key), "must be a string");
    out = s->get();
    return true;
}

/// Reads an optional integer within [min, max].
inline bool get_int(const toml::table& t, std::string_view key, const std::string& path, std::optional<int64_t>& out, Errors& e,
    int64_t min = INT64_MIN, int64_t max = INT64_MAX) {
    const toml::node* node = t.get(key);
    if (!node) return true;
    const auto* i = node->as_integer();
    if (!i) return e.fail(join(path, key), "must be an integer");
    if (i->get() < min || i->get() > max) return e.fail(join(path, key), "out of range");
    out = i->get();
    return true;
}

/// Reads an optional boolean.
inline bool get_bool(const toml::table& t, std::string_view key, const std::string& path, bool& out, Errors& e) {
    const toml::node* node = t.get(key);
    if (!node) return true;
    const auto* b = node->as_boolean();
    if (!b) return e.fail(join(path, key), "must be a boolean");
    out = b->get();
    return true;
}

/// Reads an optional array of strings.
inline bool get_strings(const toml::table& t, std::string_view key, const std::string& path, std::vector<std::string>& out, Errors& e) {
    const toml::node* node = t.get(key);
    if (!node) return true;
    const auto* a = node->as_array();
    if (!a) return e.fail(join(path, key), "must be an array of strings");
    out.clear();
    for (auto&& item : *a) {
        const auto* s = item.as_string();
        if (!s) return e.fail(join(path, key), "must be an array of strings");
        out.push_back(s->get());
    }
    return true;
}

/// Optional sub-table; a present non-table value is an error.
inline const toml::table* get_table(const toml::table& t, std::string_view key, const std::string& path, Errors& e) {
    const toml::node* node = t.get(key);
    if (!node) return nullptr;
    if (!node->as_table()) e.fail(join(path, key), "must be a table");
    return node->as_table();
}

} // namespace fthook::detail

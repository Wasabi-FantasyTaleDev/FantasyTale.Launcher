#include "json.h"

#include <cstdio>
#include <charconv>
#include <unordered_set>

namespace bubblefighter::json {

namespace {

struct Parser {
    std::string_view text;
    size_t pos = 0;

    void space() {
        while (pos < text.size() && (text[pos] == ' ' || text[pos] == '\t' || text[pos] == '\n' || text[pos] == '\r')) ++pos;
    }

    bool literal(std::string_view word) {
        if (text.substr(pos, word.size()) != word) return false;
        pos += word.size();
        return true;
    }

    static void append_utf8(std::string& out, uint32_t cp) {
        if (cp < 0x80) out.push_back(static_cast<char>(cp));
        else if (cp < 0x800) {
            out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else if (cp < 0x10000) {
            out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else {
            out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }

    bool hex4(uint32_t& out) {
        if (pos + 4 > text.size()) return false;
        out = 0;
        for (int i = 0; i < 4; ++i) {
            const char c = text[pos++];
            out <<= 4;
            if (c >= '0' && c <= '9') out |= static_cast<uint32_t>(c - '0');
            else if (c >= 'a' && c <= 'f') out |= static_cast<uint32_t>(c - 'a' + 10);
            else if (c >= 'A' && c <= 'F') out |= static_cast<uint32_t>(c - 'A' + 10);
            else return false;
        }
        return true;
    }

    bool string(std::string& out) {
        if (pos >= text.size() || text[pos] != '"') return false;
        ++pos;
        while (pos < text.size()) {
            const char c = text[pos++];
            if (c == '"') return true;
            if (static_cast<unsigned char>(c) < 0x20) return false;
            if (c != '\\') { out.push_back(c); continue; }
            if (pos >= text.size()) return false;
            const char e = text[pos++];
            switch (e) {
            case '"': out.push_back('"'); break;
            case '\\': out.push_back('\\'); break;
            case '/': out.push_back('/'); break;
            case 'b': out.push_back('\b'); break;
            case 'f': out.push_back('\f'); break;
            case 'n': out.push_back('\n'); break;
            case 'r': out.push_back('\r'); break;
            case 't': out.push_back('\t'); break;
            case 'u': {
                uint32_t cp = 0;
                if (!hex4(cp)) return false;
                if (cp >= 0xD800 && cp < 0xDC00) {
                    uint32_t low = 0;
                    if (!literal("\\u") || !hex4(low) || low < 0xDC00 || low >= 0xE000) return false;
                    cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                } else if (cp >= 0xDC00 && cp < 0xE000) return false;
                append_utf8(out, cp);
                break;
            }
            default: return false;
            }
        }
        return false;
    }

    bool number(Value& out) {
        const size_t start = pos;
        if (pos < text.size() && text[pos] == '-') ++pos;
        if (pos >= text.size() || text[pos] < '0' || text[pos] > '9') return false;
        if (text[pos] == '0') ++pos;
        else while (pos < text.size() && text[pos] >= '0' && text[pos] <= '9') ++pos;
        bool integral = true;
        if (pos < text.size() && text[pos] == '.') {
            integral = false;
            ++pos;
            if (pos >= text.size() || text[pos] < '0' || text[pos] > '9') return false;
            while (pos < text.size() && text[pos] >= '0' && text[pos] <= '9') ++pos;
        }
        if (pos < text.size() && (text[pos] == 'e' || text[pos] == 'E')) {
            integral = false;
            ++pos;
            if (pos < text.size() && (text[pos] == '+' || text[pos] == '-')) ++pos;
            if (pos >= text.size() || text[pos] < '0' || text[pos] > '9') return false;
            while (pos < text.size() && text[pos] >= '0' && text[pos] <= '9') ++pos;
        }
        out.kind = Value::Kind::Number;
        out.integral = false;
        if (!integral || pos - start > 19) return true; // kept as a non-integral number; never read as an integer
        const auto converted = std::from_chars(text.data() + start, text.data() + pos, out.number);
        if (converted.ec != std::errc{}) return true;
        out.integral = true;
        return true;
    }

    bool value(Value& out, size_t depth) {
        if (depth > kMaxDepth) return false;
        space();
        if (pos >= text.size()) return false;
        const char c = text[pos];
        if (c == '{') {
            ++pos;
            out.kind = Value::Kind::Object;
            std::unordered_set<std::string> keys;
            space();
            if (pos < text.size() && text[pos] == '}') { ++pos; return true; }
            for (;;) {
                space();
                std::string key;
                if (!string(key) || !keys.insert(key).second) return false;
                space();
                if (pos >= text.size() || text[pos++] != ':') return false;
                Value member;
                if (!value(member, depth + 1)) return false;
                out.members.emplace_back(std::move(key), std::move(member));
                space();
                if (pos >= text.size()) return false;
                if (text[pos] == ',') { ++pos; continue; }
                if (text[pos] == '}') { ++pos; return true; }
                return false;
            }
        }
        if (c == '[') {
            ++pos;
            out.kind = Value::Kind::Array;
            space();
            if (pos < text.size() && text[pos] == ']') { ++pos; return true; }
            for (;;) {
                Value item;
                if (!value(item, depth + 1)) return false;
                out.items.push_back(std::move(item));
                space();
                if (pos >= text.size()) return false;
                if (text[pos] == ',') { ++pos; continue; }
                if (text[pos] == ']') { ++pos; return true; }
                return false;
            }
        }
        if (c == '"') { out.kind = Value::Kind::String; return string(out.text); }
        if (literal("true")) { out.kind = Value::Kind::Bool; out.boolean = true; return true; }
        if (literal("false")) { out.kind = Value::Kind::Bool; return true; }
        if (literal("null")) { out.kind = Value::Kind::Null; return true; }
        return number(out);
    }
};

} // namespace

const Value* Value::get(std::string_view key) const {
    if (kind != Kind::Object) return nullptr;
    for (const auto& [name, member] : members)
        if (name == key) return &member;
    return nullptr;
}

bool Value::integer(std::string_view key, int64_t minimum, int64_t maximum, int64_t& out) const {
    const Value* v = get(key);
    if (!v || v->kind != Kind::Number || !v->integral || v->number < minimum || v->number > maximum) return false;
    out = v->number;
    return true;
}

bool Value::string(std::string_view key, std::string& out) const {
    const Value* v = get(key);
    if (!v || v->kind != Kind::String) return false;
    out = v->text;
    return true;
}

bool Value::flag(std::string_view key, bool& out) const {
    const Value* v = get(key);
    if (!v || v->kind != Kind::Bool) return false;
    out = v->boolean;
    return true;
}

bool parse(std::string_view text, Value& out) {
    if (text.size() > kMaxBytes) return false;
    Parser parser{text};
    out = Value{};
    if (!parser.value(out, 0)) return false;
    parser.space();
    return parser.pos == text.size();
}

std::string quote(std::string_view utf8) {
    std::string out = "\"";
    for (const char c : utf8) {
        switch (c) {
        case '"': out += "\\\""; break;
        case '\\': out += "\\\\"; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default:
            if (static_cast<unsigned char>(c) < 0x20) {
                char escape[8];
                std::snprintf(escape, sizeof escape, "\\u%04X", static_cast<unsigned>(static_cast<unsigned char>(c)));
                out += escape;
            } else out.push_back(c);
        }
    }
    out.push_back('"');
    return out;
}

} // namespace bubblefighter::json

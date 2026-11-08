#include "friends.h"

#include <cstring>

namespace bubblefighter::nmco {

namespace {

constexpr uint32_t kEventTag = 0x656F0101;          // event blob, owner word 0x656F
constexpr uint32_t kRequestFriendDataTag = 0x72660101; // CNMRequestNewFriendData (kr-812 sub_90AC30)
constexpr uint32_t kCategoryTag = 0x63690201;       // CNMCateFriendInfo, object byte+6 = 2
constexpr uint32_t kFriendTag = 0x75690301;         // CNMFriendInfo, object byte+6 = 3
constexpr uint32_t kAvatarTag = 0x61690101;         // CNMAvatarItemInfo (kr-812 sub_903450)
constexpr uint32_t kGameCodeTag = 0x67630101;       // CNMGameCode
constexpr uint32_t kSessionInfoTag = 0x73690101;    // CNMSessionInfo (kr-812 sub_909170)
constexpr uint32_t kMemberTag = 0x736D0101;         // CNMSessionMemberInfo (kr-812 sub_909220)
constexpr uint32_t kChatMessageTag = 0x636D0101;    // CNMChatMessage (kr-812 sub_906E70)

bool u8(Reader& r, uint8_t& value) {
    if (r.pos + 1 > r.size) return false;
    value = r.data[r.pos++];
    return true;
}

bool u64(Reader& r, uint64_t& value) {
    if (r.pos + 8 > r.size) return false;
    std::memcpy(&value, r.data + r.pos, 8);
    r.pos += 8;
    return true;
}

bool skip_u32(Reader& r, int count) {
    uint32_t ignored = 0;
    for (int i = 0; i < count; ++i)
        if (!r.u32(ignored)) return false;
    return true;
}

void put_u8(Writer& w, uint8_t value) { w.out.push_back(value); }
void put_u16(Writer& w, uint16_t value) {
    w.out.push_back(static_cast<uint8_t>(value));
    w.out.push_back(static_cast<uint8_t>(value >> 8));
}
void put_u64(Writer& w, uint64_t value) {
    w.u32(static_cast<uint32_t>(value));
    w.u32(static_cast<uint32_t>(value >> 32));
}

/// Appends [tag][len][fields]: every decoder bounds its reads by len (kr-812 sub_902BF0).
void put_blob(Writer& w, uint32_t tag, const Writer& fields) {
    w.u32(tag);
    w.u32(static_cast<uint32_t>(fields.out.size()));
    w.out.insert(w.out.end(), fields.out.begin(), fields.out.end());
}

/// [v0 id][N][payload][v0 tag]: the client attaches the buffer and reads it raw (kr-812 sub_902020).
std::vector<uint8_t> envelope(const Writer& payload) {
    Writer w;
    w.u32(kCryptorV0Id);
    w.u32(static_cast<uint32_t>(payload.out.size()));
    w.out.insert(w.out.end(), payload.out.begin(), payload.out.end());
    w.u32(kCryptorV0Tag);
    return w.out;
}

// CNMFriendInfo in the order of kr-812 sub_904980 -> sub_903F70 -> sub_903B80.
void put_friend(Writer& w, const NativeFriend& f) {
    Writer o;
    o.wstr(L"");                       // +12, unknown
    put_u64(o, f.key);                 // +144 user key: 40961 sends it for the clicked friend (kr-812 sub_89CC90)
    o.u32(f.blocked ? 1u : 0u);        // +152 flags, bit0 = blocked
    o.wstr(L"");                       // +156, unknown
    o.u32(f.online ? 1u : 0u);         // +284 presence: 0/13/14 offline, other values online
    o.u32(0);                          // +288, unknown
    o.u32(0);                          // +292, unknown
    put_u16(o, 0);                     // +296, unknown
    o.wstr(L"");                       // discarded string
    Writer avatar;                     // CNMAvatarItemInfo, both values discarded
    put_u8(avatar, 0);
    avatar.u32(0);
    put_blob(o, kAvatarTag, avatar);
    put_u64(o, f.key);                 // +336 friend key
    o.u32(f.game_code);                // +344 code of the foreign-game header
    o.u32(0);                          // +348, unknown
    o.u32(f.game_code);                // +352 friend's game code
    o.u32(0);                          // +356, unknown
    o.wstr(f.nickname);                // +360 displayed nickname
    o.wstr(L"");                       // +488, unknown
    // +3064: an online friend with 0 here is labelled "Nexon Plug" rather than online (kr-812 sub_89A9F0).
    put_u8(o, f.online ? 1u : 0u);
    o.u32(f.level);                    // +3068 level icon
    o.u32(0);                          // +316, unknown
    put_u8(o, 0);                      // +318, unknown
    put_u8(o, 0);                      // +322, unknown
    put_u8(o, 0);                      // +332, unknown
    o.wstr(L"");                       // +1000, unknown
    o.wstr(L"");                       // +3000, unknown
    put_blob(w, kFriendTag, o);
}

// Version 1 of CNMAvatarFullInfo: [u8][u32][u8][u16][u16] then 38 u32 pairs, all discarded (kr-812 sub_9037B0).
void put_avatar_full(Writer& w) {
    Writer o;
    put_u8(o, 0);
    o.u32(0);
    put_u8(o, 0);
    put_u16(o, 0);
    put_u16(o, 0);
    for (int i = 0; i < 76; ++i) o.u32(0);
    put_blob(w, kAvatarTag, o);
}

// CNMSessionInfo in kr-812 sub_909170 order; only the type is consumed (event+52 == 1 opens a chat, kr-812 sub_8FE930).
void put_session_info(Writer& w) {
    Writer o;
    o.u32(1);      // +12 session type, 1 = chat
    o.u32(0);      // +16, unknown
    o.u32(0);      // +20, unknown
    put_u8(o, 0);  // +24, unknown
    o.u32(0);      // +28, unknown
    o.u32(0);      // +32, unknown
    put_u64(o, 0); // +40, unknown
    put_u64(o, 0); // +48, unknown
    for (int i = 0; i < 4; ++i) o.u32(0); // +56..+68, unknown
    put_blob(w, kSessionInfoTag, o);
}

// CNMSessionMemberInfo in kr-812 sub_9095D0 order. The peer re-invite takes a name from +24 or +152 (kr-812 sub_89CC90).
void put_member(Writer& w, const ChatMember& m) {
    Writer o;
    put_u64(o, m.key);  // +16 user key
    o.wstr(m.nickname); // +24 name
    o.wstr(m.nickname); // +152 name
    o.u32(0);           // +280, unknown
    o.u32(0);           // +284, unknown
    o.wstr(m.nickname); // +288 name in the window's "offline" notice (kr-812 sub_86B650)
    o.u32(m.online ? 1u : 0u); // +416 presence, coded like FriendInfo +284
    put_avatar_full(o); // +420
    put_blob(w, kMemberTag, o);
}

void put_members(Writer& w, const std::vector<ChatMember>& members) {
    w.u32(static_cast<uint32_t>(members.size()));
    for (const auto& m : members) put_member(w, m);
}

} // namespace

Reader request_fields(const std::vector<uint8_t>& object) {
    Reader r{object.data(), static_cast<uint32_t>(object.size()), 8};
    uint32_t base = 0;
    if (!r.u32(base)) r.pos = r.size + 1; // every later read fails
    return r;
}

bool read_register_callback(const std::vector<uint8_t>& object, uint32_t& window, uint32_t& message) {
    Reader r = request_fields(object);
    return r.u32(window) && r.u32(message);
}

bool read_change_level(const std::vector<uint8_t>& object, uint32_t& level) {
    Reader r = request_fields(object);
    return skip_u32(r, 2) && r.u32(level);
}

bool read_request_new_friend(const std::vector<uint8_t>& object, std::wstring& nickname, std::wstring& message) {
    Reader r = request_fields(object);
    uint32_t tag = 0, length = 0;
    if (!r.u32(tag) || (tag >> 16) != (kRequestFriendDataTag >> 16) || !r.u32(length) || length > r.size - r.pos) return false;
    Reader d{r.data, r.pos + length, r.pos};
    std::wstring ignored;
    uint8_t last = 0;
    return skip_u32(d, 1) && d.wstr(nickname) && skip_u32(d, 5) && d.wstr(ignored) && d.wstr(ignored) && skip_u32(d, 1)
        && d.wstr(message) && u8(d, last);
}

bool read_confirm_new_friend(const std::vector<uint8_t>& object, uint32_t& handle, uint8_t& code) {
    Reader r = request_fields(object);
    return r.u32(handle) && u8(r, code);
}

bool read_block_friend(const std::vector<uint8_t>& object, uint64_t& key, uint8_t& variant) {
    Reader r = request_fields(object);
    return u64(r, key) && skip_u32(r, 4) && u8(r, variant);
}

bool read_friend_category(const std::vector<uint8_t>& object, uint64_t& key, uint32_t& category) {
    Reader r = request_fields(object);
    return u64(r, key) && skip_u32(r, 4) && r.u32(category);
}

bool read_move_friend(const std::vector<uint8_t>& object, uint64_t& key, uint32_t& from, uint32_t& to) {
    Reader r = request_fields(object);
    return u64(r, key) && skip_u32(r, 4) && r.u32(from) && r.u32(to);
}

bool read_add_category(const std::vector<uint8_t>& object, std::wstring& name) {
    Reader r = request_fields(object);
    return skip_u32(r, 2) && r.wstr(name);
}

bool read_delete_category(const std::vector<uint8_t>& object, uint32_t& category) {
    Reader r = request_fields(object);
    return r.u32(category);
}

bool read_rename_category(const std::vector<uint8_t>& object, uint32_t& category, std::wstring& name) {
    Reader r = request_fields(object);
    return r.u32(category) && r.wstr(name);
}

bool read_game_code(const std::vector<uint8_t>& object, uint32_t& code) {
    Reader r = request_fields(object);
    return r.u32(code);
}

std::vector<uint8_t> friend_list_reply(const std::vector<NativeCategory>& categories, const std::vector<NativeFriend>& root) {
    Writer w;
    w.u32(1);
    w.u32(static_cast<uint32_t>(categories.size()));
    for (const auto& c : categories) {
        // CNMCateFriendInfo (kr-812 sub_905C30 -> sub_905850): the two trailing bytes are read last.
        Writer o;
        o.u32(c.id);
        o.u32(c.game_code);
        o.u32(0); // +20, unknown
        o.wstr(c.name);
        o.u32(static_cast<uint32_t>(c.members.size()));
        for (const auto& member : c.members) put_friend(o, member);
        put_u8(o, 0); // +88, unknown
        put_u8(o, 0); // +89, unknown
        put_blob(w, kCategoryTag, o);
    }
    w.u32(static_cast<uint32_t>(root.size()));
    for (const auto& f : root) put_friend(w, f);
    return w.out;
}

std::vector<uint8_t> support_game_list_reply(const std::vector<uint32_t>& codes) {
    Writer w;
    w.u32(1);
    w.u32(static_cast<uint32_t>(codes.size()));
    for (const uint32_t code : codes) {
        Writer o;
        o.u32(code);
        put_blob(w, kGameCodeTag, o);
    }
    return w.out;
}

std::vector<uint8_t> game_name_reply(const std::wstring* name) {
    Writer w;
    w.u32(name ? 1u : 0u);
    if (name) w.wstr(*name);
    return w.out;
}

std::vector<uint8_t> result_reply(uint32_t result) {
    Writer w;
    w.u32(result);
    return w.out;
}

bool read_request_chat_session(const std::vector<uint8_t>& object, uint64_t& key) {
    Reader r = request_fields(object);
    return u64(r, key) && skip_u32(r, 3);
}

bool read_session_id(const std::vector<uint8_t>& object, uint32_t& session) {
    Reader r{object.data(), static_cast<uint32_t>(object.size()), 8};
    return r.u32(session);
}

bool read_send_chat_message(const std::vector<uint8_t>& object, uint32_t& session, std::wstring& text) {
    Reader r{object.data(), static_cast<uint32_t>(object.size()), 8};
    uint32_t tag = 0, size = 0, avatar_tag = 0, avatar_size = 0;
    std::wstring sender, unknown_160, unknown_672;
    if (!r.u32(session) || !r.u32(tag) || !r.u32(size) || (tag >> 16) != (kChatMessageTag >> 16) || size > r.size - r.pos)
        return false;
    // kr-812 sub_9071D0 bounds CNMChatMessage by its blob length, independently of the outer functor.
    Reader message{r.data, r.pos + size, r.pos};
    if (!skip_u32(message, 2) || !message.u32(avatar_tag) || (avatar_tag >> 16) != (kAvatarTag >> 16)
        || !message.u32(avatar_size) || avatar_size > message.size - message.pos) return false;
    message.pos += avatar_size;
    // +32 is the sender label the client builds for itself (kr-812 sub_9201E0); the server names the sender instead.
    return message.wstr(sender) && message.wstr(unknown_160) && message.wstr(unknown_672) && message.wstr(text)
        && skip_u32(message, 1);
}

std::vector<uint8_t> chat_session_reply() {
    Writer w;
    w.u32(1);
    w.u32(0); // functor +64, unknown
    w.u32(0); // functor +68, unknown
    return w.out;
}

std::vector<uint8_t> session_info_reply(const std::vector<ChatMember>& members) {
    Writer w;
    w.u32(1);
    put_session_info(w);
    put_members(w, members);
    return w.out;
}

std::vector<uint8_t> member_list_reply(const std::vector<ChatMember>& members) {
    Writer w;
    w.u32(1);
    put_members(w, members);
    return w.out;
}

std::vector<uint8_t> want_close_reply() {
    Writer w;
    w.u32(1);
    put_u8(w, 0); // functor +44, unknown
    return w.out;
}

std::vector<uint8_t> session_created_event(uint32_t session, const std::vector<ChatMember>& members) {
    Writer event;
    event.u32(session);     // +36 session id, the key of the chat window
    put_session_info(event); // +40
    put_members(event, members);
    put_u8(event, 0);        // +128, unknown
    put_u8(event, 0);        // +132, unknown
    Writer payload;
    put_blob(payload, kEventTag, event);
    return envelope(payload);
}

std::vector<uint8_t> chat_message_event(uint32_t session, const std::wstring& sender, const std::wstring& text) {
    Writer message;
    message.u32(0);     // +12, unknown
    message.u32(0);     // +16, unknown
    Writer avatar;      // +20 CNMAvatarItemInfo, discarded
    put_u8(avatar, 0);
    avatar.u32(0);
    put_blob(message, kAvatarTag, avatar);
    message.wstr(sender); // +32, shown as the sender; "_NOTICE_" is reserved for notices (kr-812 sub_8FF1D0)
    message.wstr(L"");    // +160, unknown
    message.wstr(L"");    // +672, unknown
    message.wstr(text);   // +1184 the line
    message.u32(0);       // +3232, unknown
    Writer event;
    event.u32(session);   // +36 session id
    put_blob(event, kChatMessageTag, message);
    put_u8(event, 0);     // +3276, unknown
    Writer payload;
    put_blob(payload, kEventTag, event);
    return envelope(payload);
}

std::vector<uint8_t> bare_event(uint32_t code) {
    Writer fields;
    fields.u32(code);
    Writer payload;
    put_blob(payload, kEventTag, fields);
    return envelope(payload);
}

std::vector<uint8_t> request_new_friend_event(uint32_t handle, uint32_t requester_game_code, const std::wstring& requester,
    const std::wstring& message) {
    // The prompt reads data+284 (short-name lookup), data+292 (name), data+424 (message), and the
    // answer 16388 echoes data+12 (kr-812 sub_8FF840); the other fields are not consumed.
    Writer data;
    data.u32(handle);              // +12 request handle
    data.wstr(L"");                // +16
    data.u32(0);                   // +144
    data.u32(0);                   // +148
    data.u32(0);                   // +152
    data.u32(requester_game_code); // +284
    data.u32(0);                   // +288
    data.wstr(L"");                // +156
    data.wstr(requester);          // +292
    data.u32(0);                   // +420
    data.wstr(message);            // +424
    put_u8(data, 0);               // +936
    Writer event;
    put_blob(event, kRequestFriendDataTag, data);
    Writer payload;
    put_blob(payload, kEventTag, event);
    return envelope(payload);
}

} // namespace bubblefighter::nmco

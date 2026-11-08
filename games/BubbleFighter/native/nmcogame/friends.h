#pragma once
// Native friend-list codecs of the messenger stand-in: request readers, reply bodies and the event
// buffers posted to the client window. Layouts are kr-812; each piece names the client function it
// follows. Fields whose purpose is not established are written as zero or empty.
#include "messenger.h"
#include "nm_codes.h"

#include <cstdint>
#include <string>
#include <vector>

namespace bubblefighter::nmco {

// Function codes of the friend list (build-qualified functor constructors; codes are the functor's +12 word).
constexpr uint32_t kResetCallback = 0x1003;          // 4099
constexpr uint32_t kLoginVirtual = 0x1103;           // 4355, kr-812 sub_915870
constexpr uint32_t kLoginPassport = 0x110A;          // cn-324 sub_7BD880, called through cn-324 sub_79FDC0
constexpr uint32_t kChangeMyLevel = 0x1108;          // 4360, kr-812 sub_91F130
constexpr uint32_t kLogoutMessenger = 0x110E;        // 4366
constexpr uint32_t kLogoutAuth = 0x1110;             // 4368
constexpr uint32_t kGetFriendList = 0x4001;          // 16385, kr-812 sub_91F5E0
constexpr uint32_t kRequestNewFriend = 0x4003;       // 16387, kr-812 sub_91F3D0
constexpr uint32_t kConfirmNewFriend = 0x4004;       // 16388, kr-812 sub_91F4C0
constexpr uint32_t kBlockFriend = 0x4005;            // 16389
constexpr uint32_t kAddFriendToCategory = 0x4007;    // 16391
constexpr uint32_t kDeleteFriendFromCategory = 0x4008; // 16392
constexpr uint32_t kMoveFriendCategory = 0x4009;     // 16393
constexpr uint32_t kAddCategory = 0x4101;            // 16641
constexpr uint32_t kDeleteCategory = 0x4102;         // 16642
constexpr uint32_t kChangeCategoryName = 0x4103;     // 16643
constexpr uint32_t kGetSupportGameList = 0x800C;     // 32780, kr-812 sub_91FE60
constexpr uint32_t kGetGameFullName = 0x800E;        // 32782, kr-812 sub_91FF40
constexpr uint32_t kGetGameShortName = 0x800F;       // 32783, kr-812 sub_920010
// Friend chat: the double-click request, then the calls on the session it opens (kr-812 sub_8FEBB0, sub_86BC00).
constexpr uint32_t kRequestChatSession = 0xA001;     // 40961, kr-812 sub_91A680, sent by kr-812 sub_89CC90
constexpr uint32_t kGSRegisterCallback = 0xC001;     // 49153, kr-812 sub_920660; its failure keeps the window shut
constexpr uint32_t kGSWantClose = 0xC002;            // 49154, kr-812 sub_920700
constexpr uint32_t kGSGetSessionInfo = 0xC003;       // 49155, kr-812 sub_920790
constexpr uint32_t kGSGetMemberList = 0xC005;        // 49157, kr-812 sub_9208E0
constexpr uint32_t kCSSendChatMessage = 0xC101;      // 49409, kr-812 sub_9209C0

// Events the stand-in posts (wParam of the registered message; kr-812 sub_91DFC0 -> sub_9147E0).
constexpr uint32_t kMessengerReplyEvent = 4097; // code 1 after LoginMessenger opens the friend window (kr-812 sub_8FE6C0)
constexpr uint32_t kRefreshEvent = 4099;        // code 2 makes an open friend window re-fetch (kr-812 sub_8FEFF0)
constexpr uint32_t kRequestNewFriendEvent = 4101; // incoming friend request prompt (kr-812 sub_8FF840)
// Both window messages reach the same event parser (kr-812 sub_8E2040: 9131 or 9133 -> sub_91DFC0), so chat events
// go to the window registered with 4098.
constexpr uint32_t kSessionCreatedEvent = 40961;      // opens the chat window when the session type is 1 (kr-812 sub_8FE930)
constexpr uint32_t kChatMessageReceivedEvent = 41217; // adds a line to the session's window (kr-812 sub_8FF1D0)

// The codes this stand-in answers must be ids the clients define.
static_assert(function_name(kRegisterCallback) != nullptr);
static_assert(function_name(kLoginMessenger) != nullptr);
static_assert(function_name(kLoginAuth) != nullptr);
static_assert(function_name(kInitialize) != nullptr);
static_assert(function_name(kSetLocale) != nullptr);
static_assert(function_name(kGetNexonPassport) != nullptr);
static_assert(function_name(kGetNxHwid) != nullptr);
static_assert(function_name(kGameLogInitialize) != nullptr);
static_assert(function_name(kResetCallback) != nullptr);
static_assert(function_name(kLoginVirtual) != nullptr);
static_assert(function_name(kLoginPassport) != nullptr);
static_assert(function_name(kChangeMyLevel) != nullptr);
static_assert(function_name(kLogoutMessenger) != nullptr);
static_assert(function_name(kLogoutAuth) != nullptr);
static_assert(function_name(kGetFriendList) != nullptr);
static_assert(function_name(kRequestNewFriend) != nullptr);
static_assert(function_name(kConfirmNewFriend) != nullptr);
static_assert(function_name(kBlockFriend) != nullptr);
static_assert(function_name(kAddFriendToCategory) != nullptr);
static_assert(function_name(kDeleteFriendFromCategory) != nullptr);
static_assert(function_name(kMoveFriendCategory) != nullptr);
static_assert(function_name(kAddCategory) != nullptr);
static_assert(function_name(kDeleteCategory) != nullptr);
static_assert(function_name(kChangeCategoryName) != nullptr);
static_assert(function_name(kGetSupportGameList) != nullptr);
static_assert(function_name(kGetGameFullName) != nullptr);
static_assert(function_name(kGetGameShortName) != nullptr);
static_assert(function_name(kRequestChatSession) != nullptr);
static_assert(function_name(kGSRegisterCallback) != nullptr);
static_assert(function_name(kGSWantClose) != nullptr);
static_assert(function_name(kGSGetSessionInfo) != nullptr);
static_assert(function_name(kGSGetMemberList) != nullptr);
static_assert(function_name(kCSSendChatMessage) != nullptr);
static_assert(event_name(kSessionCreatedEvent) != nullptr);
static_assert(event_name(kChatMessageReceivedEvent) != nullptr);
static_assert(event_name(kMessengerReplyEvent) != nullptr);
static_assert(event_name(kRefreshEvent) != nullptr);
static_assert(event_name(kRequestNewFriendEvent) != nullptr);

/// The functor object after its 8-byte header and the base u32 (kr-812 sub_915720), ready for fields.
Reader request_fields(const std::vector<uint8_t>& object);

/// 4098 CNMRegisterCallbackFunc: window and message the events go to (kr-812 sub_91E9E0).
bool read_register_callback(const std::vector<uint8_t>& object, uint32_t& window, uint32_t& message);
/// 4360: [u32][u32][u32 level][u32] after the base (kr-812 sub_91BF30).
bool read_change_level(const std::vector<uint8_t>& object, uint32_t& level);
/// 16387: the CNMRequestNewFriendData object (tag word 0x7266), fields in kr-812 sub_90AFF0 order;
/// the target nickname is data+16 and the message data+424 (kr-812 sub_91F3D0).
bool read_request_new_friend(const std::vector<uint8_t>& object, std::wstring& nickname, std::wstring& message);
/// 16388: [u32 handle][u8 code][u32]; code 1 accept, 2 decline, 3 dismissed (kr-812 sub_91F4C0).
bool read_confirm_new_friend(const std::vector<uint8_t>& object, uint32_t& handle, uint8_t& code);
/// 16389: [u64 key][u32 x4][u8 variant]; variant 1 blocks, 0 unblocks.
bool read_block_friend(const std::vector<uint8_t>& object, uint64_t& key, uint8_t& variant);
/// 16391 / 16392: [u64 key][u32 x4][u32 category][u8].
bool read_friend_category(const std::vector<uint8_t>& object, uint64_t& key, uint32_t& category);
/// 16393: [u64 key][u32 x4][u32 from][u32 to][u8].
bool read_move_friend(const std::vector<uint8_t>& object, uint64_t& key, uint32_t& from, uint32_t& to);
/// 16641: [u32 game code][u32][str name][u32][u32].
bool read_add_category(const std::vector<uint8_t>& object, std::wstring& name);
/// 16642: [u32 category][u8].
bool read_delete_category(const std::vector<uint8_t>& object, uint32_t& category);
/// 16643: [u32 category][str name][u8].
bool read_rename_category(const std::vector<uint8_t>& object, uint32_t& category, std::wstring& name);
/// 32782 / 32783: [u32 game code].
bool read_game_code(const std::vector<uint8_t>& object, uint32_t& code);

/// 40961: [u64 user key][u32 game code][u32][u32] from the clicked friend's FriendInfo +144, +352, +356 (kr-812 sub_89CC90).
bool read_request_chat_session(const std::vector<uint8_t>& object, uint64_t& key);
/// The base word of the session calls 49153/49154/49155/49157/49409: the session id (kr-812 sub_920660).
bool read_session_id(const std::vector<uint8_t>& object, uint32_t& session);
/// 49409: the session id and the CNMChatMessage text at +1184 (kr-812 sub_86BC00 fills it, sub_9071D0 reads it).
bool read_send_chat_message(const std::vector<uint8_t>& object, uint32_t& session, std::wstring& text);

/// One member of a chat session.
struct ChatMember {
    uint64_t key = 0;
    std::wstring nickname;
    bool online = false; ///< MemberInfo +416; 0, 13 and 14 make the window announce the member offline (kr-812 sub_86B650).
};

/// 40961 reply body: [u32 1][u32][u32]; the trampoline checks only the result (kr-812 sub_91FB80).
std::vector<uint8_t> chat_session_reply();
/// 49155 reply body: [u32 1][CNMSessionInfo][u32 n][n x CNMSessionMemberInfo] (kr-812 sub_920790).
std::vector<uint8_t> session_info_reply(const std::vector<ChatMember>& members);
/// 49157 reply body: [u32 1][u32 n][n x CNMSessionMemberInfo] (kr-812 sub_9208E0); the window reads the peer from it
/// before every line (kr-812 sub_9204C0), so the caller lists the peer first.
std::vector<uint8_t> member_list_reply(const std::vector<ChatMember>& members);
/// 49154 reply body: [u32 1][u8] (kr-812 sub_91B3C0).
std::vector<uint8_t> want_close_reply();
/// A 40961 buffer: [session][CNMSessionInfo type 1][u32 n][members][u8][u8] (kr-812 sub_910B90).
std::vector<uint8_t> session_created_event(uint32_t session, const std::vector<ChatMember>& members);
/// A 41217 buffer: [session][CNMChatMessage sender +32, text +1184][u8] (kr-812 sub_9122A0, sub_9071D0).
std::vector<uint8_t> chat_message_event(uint32_t session, const std::wstring& sender, const std::wstring& text);

/// One friend in a 16385 reply.
struct NativeFriend {
    uint64_t key = 0;     ///< Echoed verbatim by 16389/16391/16392/16393 (FriendInfo +336) and 40961 (FriendInfo +144).
    uint32_t game_code = 0;
    std::wstring nickname;
    bool blocked = false; ///< FriendInfo +152 bit0 selects the blocked-friend menu.
    bool online = false;  ///< FriendInfo +284 (0 offline, 1 online) and +3064.
    uint32_t level = 0;   ///< FriendInfo +3068.
};

/// One category in a 16385 reply with its members.
struct NativeCategory {
    uint32_t id = 0;
    uint32_t game_code = 0; ///< Shown only when it equals the client's own code (kr-812 sub_8999D0).
    std::wstring name;
    std::vector<NativeFriend> members;
};

/// 16385 reply body: [u32 1][u32 nCate][CateFriendInfo...][u32 nRoot][FriendInfo...] (kr-812 sub_917070).
std::vector<uint8_t> friend_list_reply(const std::vector<NativeCategory>& categories, const std::vector<NativeFriend>& root);
/// 32780 reply body: [u32 1][u32 n][n x CNMGameCode] (kr-812 sub_91A330).
std::vector<uint8_t> support_game_list_reply(const std::vector<uint32_t>& codes);
/// 32782 / 32783 reply body: [u32 1][str name] (kr-812 sub_91A610), or [u32 0] when unknown.
std::vector<uint8_t> game_name_reply(const std::wstring* name);
/// A reply body holding only the call result.
std::vector<uint8_t> result_reply(uint32_t result);

/// A bare event buffer (4097/4098/4099): [v0 id][12][0x656F0101][4][u32 code][v0 tag] (kr-812 sub_90CE70).
std::vector<uint8_t> bare_event(uint32_t code);
/// A 4101 buffer: event blob around a CNMRequestNewFriendData blob (kr-812 sub_90DE30 -> sub_90AFF0).
std::vector<uint8_t> request_new_friend_event(uint32_t handle, uint32_t requester_game_code, const std::wstring& requester,
    const std::wstring& message);

} // namespace bubblefighter::nmco

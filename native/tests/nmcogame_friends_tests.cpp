// Friend-list codecs and the HTTP mapping of the nmcogame stand-in.
#include "check.h"

#include <friends_client.h>
#include <game_login.h>
#include <json.h>
#include <nm_codes.h>

#include <algorithm>
#include <cstring>
#include <map>
#include <set>
#include <string>
#include <vector>

using namespace bubblefighter;
using namespace bubblefighter::nmco;

namespace {

std::vector<uint8_t> functor(void (*fields)(Writer&)) {
    Writer body;
    body.u32(0); // base word (kr-812 sub_915720)
    fields(body);
    Writer object;
    object.u32(kObjectHeader);
    object.u32(static_cast<uint32_t>(body.out.size()));
    object.out.insert(object.out.end(), body.out.begin(), body.out.end());
    return object.out;
}

void json_tests() {
    json::Value v;
    CHECK(json::parse(R"({"a":1,"b":"x\u00e9\ud83d\ude00","c":[true,null,-5],"d":1.5})", v));
    int64_t n = 0;
    std::string s;
    CHECK(v.integer("a", 0, 10, n) && n == 1);
    CHECK(!v.integer("a", 2, 10, n));
    CHECK(v.string("b", s) && s == "x\xC3\xA9\xF0\x9F\x98\x80");
    CHECK(v.get("c") && v.get("c")->items.size() == 3 && v.get("c")->items[2].number == -5);
    CHECK(!v.integer("d", 0, 10, n));
    CHECK(!json::parse("{\"a\":1} x", v));
    CHECK(!json::parse("{\"a\":01}", v));
    CHECK(!json::parse("\"\\ud800\"", v));
    CHECK(!json::parse(std::string(40, '[') + std::string(40, ']'), v));
    CHECK(json::quote("a\"\\\n\x01") == "\"a\\\"\\\\\\n\\u0001\"");
}

void codec_tests() {
    // 4099 code 2: [v0 id][12][event tag][4][code][v0 tag].
    const std::vector<uint8_t> refresh = {0x34, 0xCA, 0x1F, 0x00, 0x0C, 0, 0, 0, 0x01, 0x01, 0x6F, 0x65, 0x04, 0, 0, 0,
        0x02, 0, 0, 0, 0x9E, 0x11, 0x8A, 0x00};
    CHECK(bare_event(2) == refresh);

    // 4101 with handle 1, requester "Bob" (data+292), message "hi" (data+424): 71 bytes.
    const std::vector<uint8_t> request = {0x34, 0xCA, 0x1F, 0x00, 0x3B, 0, 0, 0, 0x01, 0x01, 0x6F, 0x65, 0x33, 0, 0, 0,
        0x01, 0x01, 0x66, 0x72, 0x2B, 0, 0, 0, 0x01, 0, 0, 0, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0x00, 0x0C, 0x42, 0x00, 0x6F, 0x00, 0x62, 0x00, 0, 0, 0, 0, 0x08, 0x68, 0x00, 0x69, 0x00, 0x00, 0x9E, 0x11, 0x8A, 0x00};
    CHECK(request.size() == 71);
    CHECK(request_new_friend_event(1, 0, L"Bob", L"hi") == request);

    CHECK(friend_list_reply({}, {}) == std::vector<uint8_t>({1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}));
    // One root friend: [1][0 categories][1 friend][tag][86][fields]; the key sits at field offset 42.
    const auto one = friend_list_reply({}, {NativeFriend{0x0102030405060708ull, 74255, L"Al", true, true, 3}});
    CHECK(one.size() == 106);
    uint32_t word = 0;
    uint64_t key = 0;
    std::memcpy(&word, one.data() + 12, 4);
    CHECK(word == 0x75690301);
    std::memcpy(&word, one.data() + 16, 4);
    CHECK(word == 86);
    std::memcpy(&key, one.data() + 20 + 42, 8);
    CHECK(key == 0x0102030405060708ull);
    // The +3064 byte (field offset 72) follows presence, so an online friend is not shown as "Nexon Plug".
    CHECK(one[20 + 72] == 1);
    std::memcpy(&word, one.data() + 20 + 73, 4);
    CHECK(word == 3);
    CHECK(friend_list_reply({}, {NativeFriend{1, 74255, L"Al", false, false, 3}})[20 + 72] == 0);

    CHECK(support_game_list_reply({74255}) == std::vector<uint8_t>({1, 0, 0, 0, 1, 0, 0, 0, 0x01, 0x01, 0x63, 0x67, 4, 0, 0, 0,
        0x0F, 0x22, 0x01, 0x00}));
    CHECK(game_name_reply(nullptr) == result_reply(0));

    uint64_t friend_key = 0;
    uint8_t variant = 0;
    CHECK(read_block_friend(functor([](Writer& w) { w.u32(7); w.u32(0); for (int i = 0; i < 4; ++i) w.u32(0); w.out.push_back(1); }),
        friend_key, variant) && friend_key == 7 && variant == 1);
    CHECK(!read_block_friend(functor([](Writer& w) { w.u32(7); }), friend_key, variant));
    std::wstring nickname, message;
    const auto add = functor([](Writer& w) {
        Writer d;
        d.u32(0); d.wstr(L"Target"); for (int i = 0; i < 5; ++i) d.u32(0); d.wstr(L""); d.wstr(L""); d.u32(0); d.wstr(L"hello"); d.out.push_back(0);
        w.u32(0x72660101); w.u32(static_cast<uint32_t>(d.out.size())); w.out.insert(w.out.end(), d.out.begin(), d.out.end());
    });
    CHECK(read_request_new_friend(add, nickname, message) && nickname == L"Target" && message == L"hello");
    auto truncated = add;
    truncated.resize(truncated.size() - 3);
    CHECK(!read_request_new_friend(truncated, nickname, message));
}

struct FakeService {
    std::map<std::string, HttpExchange> replies; // by path without the query
    std::vector<std::string> paths, bodies, bearers;
    bool reachable = true;
    std::vector<std::pair<uint32_t, std::vector<uint8_t>>> posted;
    std::vector<std::string> logged;

    HttpSend send() {
        return [this](const char*, const std::string& path, const std::string& bearer, const std::string& body, unsigned, HttpExchange& reply) {
            paths.push_back(path);
            bodies.push_back(body);
            bearers.push_back(bearer);
            if (!reachable) return false;
            const auto it = replies.find(path.substr(0, path.find('?')));
            reply = it == replies.end() ? HttpExchange{404, "{}"} : it->second;
            return true;
        };
    }
    PostEvent post() {
        return [this](uint32_t window, uint32_t message, uint32_t event, std::vector<uint8_t> buffer) {
            CHECK(window == 0x1234 && message == 9131);
            posted.emplace_back(event, std::move(buffer));
            return true;
        };
    }
    LogLine log() {
        return [this](const std::string& line) { logged.push_back(line); };
    }
};

// Runs the queued service work on the test thread, as the worker and chat threads would.
void drain(FriendsClient& client) {
    while (client.work_once(0) || client.work_chat_once(0)) {}
}

void client_tests() {
    FakeService service;
    service.replies["NexonMessenger/CNMLoginAuthFunc"] = {200, R"({"session":"s1","expiresAt":"2026-10-25T00:00:00Z","user":{"id":5,"nickname":"Me","gameCode":74255,"tenant":"local"}})"};
    service.replies["NexonMessenger/CNMLoginMessengerFunc"] = {200, R"({"result":"ok"})"};
    service.replies["NexonMessenger/CNMBlockFriendFunc"] = {200, R"({"result":"ok"})"};
    service.replies["NexonMessenger/CNMGetFriendListFunc"] = {200, R"({"page":0,"totalPages":1,"totalFriends":2,
        "categories":[{"id":1,"name":"Club","gameCode":74255}],
        "friends":[{"id":7,"nickname":"Al","gameCode":74255,"categoryId":0,"blocked":false,"status":"online","level":3},
                   {"id":8,"nickname":"Bo","gameCode":74255,"categoryId":1,"blocked":true,"status":"offline","level":1}]})"};
    service.replies["NexonMessenger/events"] = {200, R"({"next":3,"reset":false,"events":[
        {"seq":2,"kind":"friend_request","request":{"handle":9,"from":{"id":8,"nickname":"Bo","gameCode":74255,"tenant":"local"},"message":"hi","createdAt":"x"}},
        {"seq":3,"kind":"friend_list_changed"}]})"};
    service.replies["NexonMessenger/CNMGetSupportGameListFunc"] = {200,
        R"({"games":[{"code":74255,"fullName":"BubbleFighter","shortName":"BF"}]})"};
    FriendsClient client("v1.ticket.sig", service.send(), service.post(), service.log());
    std::vector<uint8_t> body;
    std::string note;
    const auto list = friend_list_reply({NativeCategory{1, 74255, L"Club", {NativeFriend{8, 74255, L"Bo", true, false, 1}}}},
        {NativeFriend{7, 74255, L"Al", false, true, 3}});

    CHECK(!client.can_poll());
    CHECK(client.handle(kRegisterCallback, functor([](Writer& w) { w.u32(0x1234); w.u32(9131); }), body, note));
    // The game thread never waits on the service: the login is queued and answered at once.
    CHECK(client.handle(kLoginMessenger, functor([](Writer&) {}), body, note) && body == result_reply(1));
    CHECK(service.paths.empty() && service.posted.empty());
    CHECK(client.handle(kGetFriendList, functor([](Writer&) {}), body, note) && body == result_reply(0));
    drain(client);
    CHECK(service.paths.size() == 4 && service.bodies[0] == R"({"ticket":"v1.ticket.sig"})" && service.bearers[1] == "s1");
    CHECK(service.posted.size() == 1 && service.posted[0].first == 4097 && service.posted[0].second == bare_event(1));
    CHECK(service.logged.back() == "messenger signed in");
    for (const auto& line : service.logged) CHECK(line.find("v1.ticket") == std::string::npos);
    CHECK(client.can_poll());

    // A second messenger login reuses the session: the ticket is single use, and the game table, which
    // cannot change, is not read again.
    CHECK(client.handle(kLoginMessenger, functor([](Writer&) {}), body, note));
    drain(client);
    CHECK(service.paths.size() == 6 && service.paths[4] == "NexonMessenger/CNMLoginMessengerFunc");
    CHECK(service.posted.size() == 2 && service.posted[1].first == 4097);

    // Lists, game names and support games come from the cache without a request.
    const size_t requests = service.paths.size();
    CHECK(client.handle(kGetFriendList, functor([](Writer& w) { w.u32(0); w.u32(0); }), body, note) && body == list);
    CHECK(client.handle(kGetSupportGameList, functor([](Writer&) {}), body, note) && body == support_game_list_reply({74255}));
    const std::wstring full = L"BubbleFighter";
    CHECK(client.handle(kGetGameFullName, functor([](Writer& w) { w.u32(74255); }), body, note) && body == game_name_reply(&full));
    CHECK(client.handle(kGetGameShortName, functor([](Writer& w) { w.u32(1); }), body, note) && body == game_name_reply(nullptr));
    CHECK(service.paths.size() == requests);

    // A change is queued; its result arrives as a refreshed list and a 4099 event.
    CHECK(client.handle(kBlockFriend, functor([](Writer& w) { w.u32(7); w.u32(0); for (int i = 0; i < 4; ++i) w.u32(0); w.out.push_back(1); }), body, note));
    CHECK(body == result_reply(1) && service.paths.size() == requests);
    drain(client);
    CHECK(service.bodies[requests] == R"({"friendId":7,"blocked":true})");
    CHECK(service.posted.size() == 3 && service.posted[2].first == 4099 && service.posted[2].second == bare_event(2));

    CHECK(client.poll_once(25));
    CHECK(service.paths.back() == "NexonMessenger/CNMGetFriendListFunc");
    CHECK(service.posted.size() == 5 && service.posted[3].first == 4101
        && service.posted[3].second == request_new_friend_event(9, 74255, L"Bo", L"hi"));
    CHECK(service.posted[4].first == 4099 && service.posted[4].second == bare_event(2));
    CHECK(client.poll_once(25));
    CHECK(std::find(service.paths.begin(), service.paths.end(), "NexonMessenger/events?after=3&wait=25") != service.paths.end());

    // A change whose reply carries the new list redraws from it without a second request.
    service.replies["NexonMessenger/CNMBlockFriendFunc"] = {200, R"({"result":"ok","friendList":{"page":0,"totalPages":1,
        "categories":[],"friends":[{"id":7,"nickname":"Al","gameCode":74255,"categoryId":0,"blocked":true,"status":"online","level":3}]}})"};
    const size_t before_change = service.paths.size();
    const size_t posted_change = service.posted.size();
    CHECK(client.handle(kBlockFriend, functor([](Writer& w) { w.u32(7); w.u32(0); for (int i = 0; i < 4; ++i) w.u32(0); w.out.push_back(1); }), body, note));
    drain(client);
    CHECK(service.paths.size() == before_change + 1 && service.posted.size() == posted_change + 1 && service.posted.back().first == 4099);
    CHECK(client.handle(kGetFriendList, functor([](Writer&) {}), body, note)
        && body == friend_list_reply({}, {NativeFriend{7, 74255, L"Al", true, true, 3}}));

    // A presence event patches the cached list in place: one 4099 and no request.
    service.replies["NexonMessenger/events"] = {200, R"({"next":5,"reset":false,"events":[
        {"seq":5,"kind":"presence_changed","friendId":7,"status":"offline","level":4}]})"};
    const size_t before_presence = service.paths.size();
    CHECK(client.poll_once(25));
    CHECK(service.paths.size() == before_presence + 1 && service.posted.back().first == 4099);
    CHECK(client.handle(kGetFriendList, functor([](Writer&) {}), body, note)
        && body == friend_list_reply({}, {NativeFriend{7, 74255, L"Al", true, false, 4}}));

    // Unreachable service: the change is logged as failed and the list reads as none until a fetch succeeds.
    service.reachable = false;
    CHECK(client.handle(kBlockFriend, functor([](Writer& w) { w.u32(7); w.u32(0); for (int i = 0; i < 4; ++i) w.u32(0); w.out.push_back(0); }), body, note));
    drain(client);
    CHECK(std::find(service.logged.begin(), service.logged.end(), "messenger CNMBlockFriendFunc: service unreachable") != service.logged.end());
    CHECK(client.handle(kGetFriendList, functor([](Writer&) {}), body, note) && body == result_reply(0));
    service.reachable = true;

    // An expired session ends polling and is not re-opened with the spent ticket.
    service.replies["NexonMessenger/events"] = {200, R"({"next":4,"reset":true,"events":[]})"};
    service.replies["NexonMessenger/CNMGetFriendListFunc"] = {401, R"({"error":"unauthorized"})"};
    CHECK(client.poll_once(25));
    CHECK(client.handle(kGetFriendList, functor([](Writer&) {}), body, note) && body == result_reply(0));
    CHECK(!client.can_poll());
    const size_t posted = service.posted.size();
    CHECK(client.handle(kLoginMessenger, functor([](Writer&) {}), body, note));
    drain(client);
    CHECK(service.posted.size() == posted && service.logged.back() == "messenger sign-in: ticket already used");

    // A refused sign-in never posts the friend-window event.
    FakeService refused;
    refused.replies["NexonMessenger/CNMLoginAuthFunc"] = {401, R"({"error":"unauthorized"})"};
    FriendsClient denied("v1.ticket.sig", refused.send(), refused.post(), refused.log());
    CHECK(denied.handle(kRegisterCallback, functor([](Writer& w) { w.u32(0x1234); w.u32(9131); }), body, note));
    CHECK(denied.handle(kLoginMessenger, functor([](Writer&) {}), body, note));
    drain(denied);
    CHECK(refused.posted.empty() && refused.logged.back().find("refused") != std::string::npos);

    // A full queue refuses the call instead of growing; a stopped client takes no work.
    for (int i = 0; i < 64; ++i) CHECK(denied.handle(kLogoutMessenger, functor([](Writer&) {}), body, note));
    note.clear();
    CHECK(!denied.handle(kLogoutMessenger, functor([](Writer&) {}), body, note) && note == "messenger busy: work queue full");
    drain(denied);
    denied.stop();
    CHECK(!denied.handle(kLogoutMessenger, functor([](Writer&) {}), body, note) && !denied.work_once(0));

    // Locale-2 clients open the same friend window through CNMLoginPassportFunc.
    FakeService passport_service;
    passport_service.replies["NexonMessenger/CNMLoginAuthFunc"] = service.replies["NexonMessenger/CNMLoginAuthFunc"];
    passport_service.replies["NexonMessenger/CNMLoginMessengerFunc"] = {200, R"({"result":"ok"})"};
    FriendsClient passport_client("v1.ticket.sig", passport_service.send(), passport_service.post());
    CHECK(FriendsClient::owns(kLoginPassport));
    CHECK(passport_client.handle(kRegisterCallback, functor([](Writer& w) { w.u32(0x1234); w.u32(9131); }), body, note));
    CHECK(passport_client.handle(kLoginPassport, functor([](Writer& w) { w.wstr(L"opaque-publisher-passport"); }), body, note)
        && body == result_reply(1));
    drain(passport_client);
    CHECK(passport_service.paths.size() == 4 && passport_service.bodies[0] == R"({"ticket":"v1.ticket.sig"})");
    CHECK(passport_service.posted.size() == 1 && passport_service.posted[0].first == 4097
        && passport_service.posted[0].second == bare_event(1));
    CHECK(passport_client.can_poll());

    FakeService passport_refused;
    passport_refused.replies["NexonMessenger/CNMLoginAuthFunc"] = {401, R"({"error":"unauthorized"})"};
    FriendsClient passport_denied("v1.ticket.sig", passport_refused.send(), passport_refused.post());
    CHECK(passport_denied.handle(kRegisterCallback, functor([](Writer& w) { w.u32(0x1234); w.u32(9131); }), body, note));
    CHECK(passport_denied.handle(kLoginPassport, functor([](Writer&) {}), body, note));
    drain(passport_denied);
    CHECK(passport_refused.posted.empty());
    CHECK(!passport_denied.can_poll());
}

void game_code_tests() {
    // Records pass through unchanged: the server converts codes to the one announced at sign-in.
    FakeService service;
    service.replies["NexonMessenger/CNMLoginAuthFunc"] = {200,
        R"({"session":"local-session","user":{"id":5,"nickname":"Me","gameCode":17244687}})"};
    service.replies["NexonMessenger/CNMLoginMessengerFunc"] = {200, R"({"result":"ok"})"};
    service.replies["NexonMessenger/CNMGetFriendListFunc"] = {200, R"({"totalPages":1,"categories":[],
        "friends":[{"id":7,"nickname":"Al","gameCode":17244687,"categoryId":0,"blocked":false,"status":"online","level":3}]})"};
    FriendsClient client("test-ticket", service.send(), service.post());
    std::vector<uint8_t> body;
    std::string note;
    CHECK(FriendsClient::owns(kInitialize));
    CHECK(client.handle(kInitialize, functor([](Writer& w) { w.u32(17244687); w.u32(0x80000000); }), body, note)
        && body == result_reply(1));
    CHECK(service.paths.empty());
    CHECK(!client.handle(kInitialize, functor([](Writer& w) { w.u32(999); }), body, note));
    CHECK(client.handle(kRegisterCallback, functor([](Writer& w) { w.u32(0x1234); w.u32(9131); }), body, note));
    CHECK(client.handle(kLoginPassport, functor([](Writer&) {}), body, note));
    drain(client);
    CHECK(service.bodies[0] == R"({"ticket":"test-ticket","gameCode":17244687})");
    CHECK(client.handle(kGetFriendList, functor([](Writer&) {}), body, note));
    CHECK(body == friend_list_reply({}, {NativeFriend{7, 17244687, L"Al", false, true, 3}}));

    // A client that never initializes signs in with the ticket alone.
    FakeService plain;
    plain.replies = service.replies;
    FriendsClient quiet("test-ticket", plain.send(), plain.post());
    CHECK(quiet.handle(kRegisterCallback, functor([](Writer& w) { w.u32(0x1234); w.u32(9131); }), body, note));
    CHECK(quiet.handle(kLoginMessenger, functor([](Writer&) {}), body, note));
    drain(quiet);
    CHECK(plain.bodies[0] == R"({"ticket":"test-ticket"})");
}

void name_table_tests() {
    // Every id is named once per space, and the two spaces overlap without sharing a name.
    std::set<uint32_t> functions, events;
    for (const NmName& entry : kFunctionNames) CHECK(functions.insert(entry.code).second);
    for (const NmName& entry : kEventNames) CHECK(events.insert(entry.code).second);
    CHECK(functions.size() == 48);
    CHECK(events.size() == 61);
    CHECK(std::strcmp(function_name(0xA001), "CNMRequestChatSessionFunc") == 0);
    CHECK(std::strcmp(event_name(0xA001), "CNMGSSessionCreatedEvent") == 0);
    CHECK(std::strcmp(function_name(0x7003), "CNMGetMyGuildListExFunc") == 0);
    CHECK(std::strcmp(event_name(41217), "CNMCSChatMessageReceivedEvent") == 0);
    CHECK(function_name(0x4002) == nullptr);
}

void chat_tests() {
    // 41217 for session 3, sender "A", line "hi": [v0][56][event tag][48][3][chat tag][35][0][0][avatar][A][][][hi][0][u8][v0 tag].
    const std::vector<uint8_t> line = {0x34, 0xCA, 0x1F, 0x00, 0x38, 0, 0, 0, 0x01, 0x01, 0x6F, 0x65, 0x30, 0, 0, 0, 0x03, 0, 0, 0,
        0x01, 0x01, 0x6D, 0x63, 0x23, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x01, 0x01, 0x69, 0x61, 0x05, 0, 0, 0, 0x00, 0, 0, 0, 0,
        0x04, 0x41, 0x00, 0x00, 0x00, 0x08, 0x68, 0x00, 0x69, 0x00, 0, 0, 0, 0, 0x00, 0x9E, 0x11, 0x8A, 0x00};
    CHECK(line.size() == 68);
    CHECK(chat_message_event(3, L"A", L"hi") == line);

    // 40961 for session 4 with one member: the window key at +36 and session type 1 at +52 are what the game reads.
    const std::vector<uint8_t> created = session_created_event(4, {ChatMember{7, L"A"}});
    CHECK(created.size() == 450);
    const std::vector<uint8_t> head = {0x34, 0xCA, 0x1F, 0x00, 0xB6, 0x01, 0, 0, 0x01, 0x01, 0x6F, 0x65, 0xAE, 0x01, 0, 0, 0x04, 0, 0, 0,
        0x01, 0x01, 0x69, 0x73, 0x35, 0, 0, 0, 0x01, 0, 0, 0};
    CHECK(std::equal(head.begin(), head.end(), created.begin()));
    CHECK(session_info_reply({ChatMember{7, L"A"}}).size() == 4 + 61 + 4 + 359);
    CHECK(member_list_reply({}) == std::vector<uint8_t>({1, 0, 0, 0, 0, 0, 0, 0}));
    CHECK(want_close_reply() == std::vector<uint8_t>({1, 0, 0, 0, 0}));

    // 40961 request: [base][u64 FriendInfo +144][u32 game code][u32][u32].
    uint64_t key = 0;
    CHECK(read_request_chat_session(functor([](Writer& w) { w.u32(7); w.u32(0); w.u32(74255); w.u32(0); w.u32(0); }), key) && key == 7);
    CHECK(!read_request_chat_session(functor([](Writer& w) { w.u32(7); }), key));

    // 49409: [session][CNMChatMessage]; the line is the fourth string.
    Writer message;
    message.u32(0);
    message.u32(0);
    message.u32(0x61690101);
    message.u32(5);
    message.out.insert(message.out.end(), 5, 0);
    message.wstr(L"Me(BF)");
    message.wstr(L"");
    message.wstr(L"");
    message.wstr(L"hello");
    message.u32(0);
    Writer send;
    send.u32(kObjectHeader);
    send.u32(static_cast<uint32_t>(12 + message.out.size()));
    send.u32(9);
    send.u32(0x636D0101);
    send.u32(static_cast<uint32_t>(message.out.size()));
    send.out.insert(send.out.end(), message.out.begin(), message.out.end());
    uint32_t session = 0;
    std::wstring text;
    CHECK(read_send_chat_message(send.out, session, text) && session == 9 && text == L"hello");
    std::vector<uint8_t> cut(send.out.begin(), send.out.end() - 8);
    CHECK(!read_send_chat_message(cut, session, text));
    auto invalid_blob = send.out;
    const uint32_t short_blob = 8;
    std::memcpy(invalid_blob.data() + 16, &short_blob, sizeof short_blob);
    CHECK(!read_send_chat_message(invalid_blob, session, text));
    invalid_blob = send.out;
    const uint32_t wrong_avatar = 0x73690101;
    std::memcpy(invalid_blob.data() + 28, &wrong_avatar, sizeof wrong_avatar);
    CHECK(!read_send_chat_message(invalid_blob, session, text));
    cut.assign(send.out.begin(), send.out.end() - 1);
    CHECK(!read_send_chat_message(cut, session, text));

    FakeService service;
    service.replies["NexonMessenger/CNMLoginAuthFunc"] = {200, R"({"session":"s1","expiresAt":"x","user":{"id":5,"nickname":"Me","gameCode":74255,"tenant":"local"}})"};
    service.replies["NexonMessenger/CNMLoginMessengerFunc"] = {200, R"({"result":"ok"})"};
    service.replies["NexonMessenger/CNMRequestChatSessionFunc"] = {200, R"({"result":"ok","chat":{"id":4,"members":[
        {"id":5,"nickname":"Me","gameCode":74255,"tenant":"local","status":"online"},{"id":7,"nickname":"Al","gameCode":74255,"tenant":"local","status":"offline"}]}})"};
    service.replies["NexonMessenger/CNMGSGetSessionInfoFunc"] = {200, R"({"result":"ok","chat":{"id":4,"members":[
        {"id":5,"nickname":"Me","gameCode":74255,"tenant":"local","status":"online"},{"id":7,"nickname":"Al","gameCode":74255,"tenant":"local","status":"offline"}]}})"};
    service.replies["NexonMessenger/CNMCSSendChatMessageFunc"] = {200, R"({"result":"ok"})"};
    service.replies["NexonMessenger/CNMGSWantCloseFunc"] = {200, R"({"result":"ok"})"};
    service.replies["NexonMessenger/events"] = {200, R"({"next":2,"reset":false,"events":[
        {"seq":1,"kind":"chat_opened","chat":{"id":4,"members":[{"id":5,"nickname":"Me","gameCode":74255,"tenant":"local","status":"online"},{"id":7,"nickname":"Al","gameCode":74255,"tenant":"local","status":"offline"}]}},
        {"seq":2,"kind":"chat_message","chatId":4,"from":{"id":7,"nickname":"Al","gameCode":74255,"tenant":"local"},"text":"yo"}]})"};
    FriendsClient client("v1.ticket.sig", service.send(), service.post(), service.log());
    std::vector<uint8_t> body;
    std::string note;
    CHECK(client.handle(kRegisterCallback, functor([](Writer& w) { w.u32(0x1234); w.u32(9131); }), body, note));
    CHECK(client.handle(kLoginMessenger, functor([](Writer&) {}), body, note));
    drain(client);

    CHECK(client.handle(kRequestChatSession, functor([](Writer& w) { w.u32(7); w.u32(0); w.u32(74255); w.u32(0); w.u32(0); }), body, note));
    CHECK(body == chat_session_reply());
    drain(client);
    CHECK(service.bodies.back() == R"({"friendId":7})");
    // The window opens from the reply; the peer comes first, and keeps its own presence.
    const std::vector<ChatMember> members = {{7, L"Al", false}, {5, L"Me", true}};
    CHECK(service.posted.size() == 2 && service.posted[1].first == 40961 && service.posted[1].second == session_created_event(4, members));
    // The chat_opened event for the same chat opens nothing more.
    CHECK(client.poll_once(25));
    CHECK(service.posted.size() == 3 && service.posted[2].first == 41217 && service.posted[2].second == chat_message_event(4, L"Al", L"yo"));

    Writer base;
    base.u32(kObjectHeader);
    base.u32(4);
    base.u32(4);
    CHECK(client.handle(kGSRegisterCallback, base.out, body, note) && body == result_reply(1));
    // Session members come from the opening event, without a request.
    const size_t requests = service.paths.size();
    CHECK(client.handle(kGSGetSessionInfo, base.out, body, note) && body == session_info_reply(members));
    CHECK(client.handle(kGSGetMemberList, base.out, body, note) && body == member_list_reply(members));
    CHECK(service.paths.size() == requests);
    // The sender's own line is shown at once, before the service answers.
    CHECK(client.handle(kCSSendChatMessage, send.out, body, note) && body == result_reply(1));
    CHECK(service.posted.back().first == 41217 && service.posted.back().second == chat_message_event(9, L"Me", L"hello"));
    drain(client);
    CHECK(service.bodies.back() == R"({"chatId":9,"text":"hello"})");
    // ...and its echo from the service is not shown a second time.
    service.replies["NexonMessenger/events"] = {200, R"({"next":3,"reset":false,"events":[
        {"seq":3,"kind":"chat_message","chatId":9,"from":{"id":5,"nickname":"Me","gameCode":74255,"tenant":"local"},"text":"hello"}]})"};
    const size_t shown = service.posted.size();
    CHECK(client.poll_once(25));
    CHECK(service.posted.size() == shown);
    CHECK(client.handle(kGSWantClose, base.out, body, note) && body == want_close_reply());
    drain(client);
    CHECK(service.paths.back() == "NexonMessenger/CNMGSWantCloseFunc");
    // A closed session is no longer known.
    CHECK(!client.handle(kGSGetSessionInfo, base.out, body, note) && note == "messenger chat session unknown");

    service.replies["NexonMessenger/CNMCSSendChatMessageFunc"] = {200, R"({"result":"refused"})"};
    CHECK(client.handle(kCSSendChatMessage, send.out, body, note));
    drain(client);
    CHECK(service.logged.back() == "messenger CNMCSSendChatMessageFunc: refused");

    // A refused open is logged and opens no window: no 40961 event follows.
    service.replies["NexonMessenger/CNMRequestChatSessionFunc"] = {200, R"({"result":"refused","chat":null})"};
    const size_t posted = service.posted.size();
    CHECK(client.handle(kRequestChatSession, functor([](Writer& w) { w.u32(7); w.u32(0); w.u32(74255); w.u32(0); w.u32(0); }), body, note));
    drain(client);
    CHECK(service.logged.back() == "messenger CNMRequestChatSessionFunc: refused" && service.posted.size() == posted);
}

void game_login_tests() {
    // The in-game sign-in sends the launcher's own two requests and answers with the client's codes.
    const GameLoginRequest request{std::string(64, 'a'), std::string(64, 'b'), "0.1.33", "id-400", 400, "113", "", "en", "id", "id"};
    FakeService service;
    service.replies["v1/auth/login"] = {200, R"({"accessToken":"bearer-1"})"};
    service.replies["v1/launch"] = {200, R"({"account":"ann","ticket":"t-1","messenger":{"url":"http://bf.example:8091","ticket":"m-1"}})"};
    std::string password = "secret-pw";
    auto ok = game_sign_in(request, "ann", password, service.send());
    CHECK(ok.code == 0 && ok.ticket == "t-1" && ok.messenger_url == "http://bf.example:8091" && ok.messenger_ticket == "m-1");
    CHECK(password.find("secret") == std::string::npos);
    CHECK(service.paths.size() == 2 && service.paths[0] == "v1/auth/login" && service.paths[1] == "v1/launch");
    CHECK(service.bodies[0] == R"({"username":"ann","password":"secret-pw","launcherVersion":"0.1.33"})");
    CHECK(service.bearers[1] == "bearer-1");
    CHECK(service.bodies[1].find(R"("launchId":")" + std::string(64, 'a') + "\"") != std::string::npos);
    CHECK(service.bodies[1].find(R"("build":"id-400")") != std::string::npos && ok.note.find("secret") == std::string::npos);

    // Each refusal shows the client's own text (id-400 sub_875E20).
    auto code = [&](int status, const char* body) {
        FakeService refused;
        refused.replies["v1/auth/login"] = {status, body};
        std::string pw = "x";
        return game_sign_in(request, "ann", pw, refused.send()).code;
    };
    CHECK(code(401, R"({"error":"wrong_password"})") == 20026);
    CHECK(code(401, R"({"error":"account_not_found"})") == 20025);
    CHECK(code(401, R"({"error":"account_banned"})") == 20010);
    CHECK(code(429, R"({"error":"rate_limited"})") == 20008);
    CHECK(code(503, R"({"error":"server_not_ready"})") == 20002);
    FakeService down;
    down.reachable = false;
    std::string pw = "x";
    CHECK(game_sign_in(request, "ann", pw, down.send()).code == 20000);
}

} // namespace

void friends_tests() {
    name_table_tests();
    chat_tests();
    json_tests();
    codec_tests();
    client_tests();
    game_code_tests();
    game_login_tests();
}

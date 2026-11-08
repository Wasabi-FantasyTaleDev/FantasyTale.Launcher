#include "friends_client.h"

#include <windows.h>

#include <algorithm>
#include <chrono>
#include <cstdio>

namespace bubblefighter::nmco {

namespace {

constexpr unsigned kCallTimeoutMs = 5000;
constexpr int kMaxPages = 64;
// Bounds work queued while the service is slow; a full queue refuses the call instead of growing.
constexpr size_t kMaxJobs = 64;

std::string field(const char* name, const std::string& json_value) { return json::quote(name) + ":" + json_value; }
std::string number(int64_t value) { return std::to_string(value); }

std::string object(std::initializer_list<std::string> fields) {
    std::string out = "{";
    for (const auto& f : fields) {
        if (out.size() > 1) out += ",";
        out += f;
    }
    return out + "}";
}

const char* decision(uint8_t code) {
    switch (code) {
    case 1: return "accept";
    case 2: return "decline";
    case 3: return "dismiss";
    default: return nullptr;
    }
}

// Reads chat.members from a reply or event with the members other than `self` first; false when malformed.
bool chat_members(const json::Value& chat, uint64_t self, int64_t& id, std::vector<ChatMember>& members) {
    const json::Value* list = chat.get("members");
    if (!chat.integer("id", 1, INT32_MAX, id) || !list || list->kind != json::Value::Kind::Array) return false;
    for (const auto& m : list->items) {
        int64_t key = 0;
        std::string nickname, status;
        if (!m.integer("id", 1, INT64_MAX, key) || !m.string("nickname", nickname) || !m.string("status", status)) return false;
        members.push_back({static_cast<uint64_t>(key), from_utf8(nickname), status == "online"});
    }
    std::stable_partition(members.begin(), members.end(), [self](const ChatMember& m) { return m.key != self; });
    return true;
}

void set_note(std::string& note, const char* operation, const char* what) {
    char text[160];
    std::snprintf(text, sizeof text, "messenger %s: %s", operation, what);
    note = text;
}

} // namespace

std::string to_utf8(const std::wstring& text) {
    if (text.empty()) return {};
    const int n = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string out(static_cast<size_t>(n > 0 ? n : 0), '\0');
    if (n > 0) WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), n, nullptr, nullptr);
    return out;
}

std::wstring from_utf8(const std::string& text) {
    if (text.empty()) return {};
    const int n = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
    std::wstring out(static_cast<size_t>(n > 0 ? n : 0), L'\0');
    if (n > 0) MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), out.data(), n);
    return out;
}

FriendsClient::FriendsClient(std::string ticket, HttpSend send, PostEvent post, LogLine log)
    : ticket_(std::move(ticket)), send_(std::move(send)), post_(std::move(post)), log_(std::move(log)) {}

void FriendsClient::write_log(const std::string& line) {
    if (log_ && !line.empty()) log_(line);
}

bool FriendsClient::enqueue(std::function<void()> job, std::string& note, bool chat) {
    {
        std::lock_guard lock(mutex_);
        auto& jobs = chat ? chat_jobs_ : jobs_;
        if (stopped_.load()) { note = "messenger stopped"; return false; }
        if (jobs.size() >= kMaxJobs) { note = "messenger busy: work queue full"; return false; }
        jobs.push_back(std::move(job));
    }
    (chat ? chat_ready_ : jobs_ready_).notify_one();
    if (note.empty()) note = "queued";
    return true;
}

bool FriendsClient::work(std::deque<std::function<void()>>& jobs, std::condition_variable& ready, unsigned wait_ms) {
    std::function<void()> job;
    {
        std::unique_lock lock(mutex_);
        if (!ready.wait_for(lock, std::chrono::milliseconds(wait_ms), [&] { return !jobs.empty() || stopped_.load(); })
            || jobs.empty())
            return false;
        job = std::move(jobs.front());
        jobs.pop_front();
    }
    job();
    return true;
}

bool FriendsClient::work_once(unsigned wait_ms) { return work(jobs_, jobs_ready_, wait_ms); }
bool FriendsClient::work_chat_once(unsigned wait_ms) { return work(chat_jobs_, chat_ready_, wait_ms); }

// Refreshes the cached friend list, from a change's own reply when it carries page 0; the caller then
// lets the client re-read it. The game table never changes after sign-in.
void FriendsClient::fetch_friends(const json::Value* first) {
    std::string note;
    if (friends_reply(note, first)) return;
    write_log(note);
    std::lock_guard lock(mutex_);
    friends_cache_.reset();
}

// Applies one presence event to the cached list without a request; false when the friend is not listed.
bool FriendsClient::patch_presence(uint64_t key, bool online, uint32_t level) {
    std::lock_guard lock(mutex_);
    if (!friends_cache_) return false;
    bool found = false;
    auto apply = [&](std::vector<NativeFriend>& friends) {
        for (auto& f : friends)
            if (f.key == key) { f.online = online; f.level = level; found = true; }
    };
    apply(list_root_);
    for (auto& category : list_categories_) apply(category.members);
    if (found) friends_cache_ = friend_list_reply(list_categories_, list_root_);
    return found;
}

void FriendsClient::remote(const char* operation, std::string body, json::Value* out) {
    json::Value reply;
    std::string note, result;
    if (call(operation, body, reply, note)) {
        reply.string("result", result);
        set_note(note, operation, result.empty() ? "done" : result.c_str());
        if (out) *out = std::move(reply);
    }
    write_log(note);
}

bool FriendsClient::owns(uint32_t code) {
    switch (code) {
    case kInitialize: case kRegisterCallback: case kResetCallback: case kLoginMessenger: case kLoginPassport: case kLogoutMessenger: case kLogoutAuth:
    case kLoginVirtual: case kChangeMyLevel: case kGetFriendList: case kRequestNewFriend: case kConfirmNewFriend:
    case kBlockFriend: case kAddFriendToCategory: case kDeleteFriendFromCategory: case kMoveFriendCategory:
    case kAddCategory: case kDeleteCategory: case kChangeCategoryName: case kGetSupportGameList:
    case kGetGameFullName: case kGetGameShortName:
    case kRequestChatSession: case kGSRegisterCallback: case kGSWantClose: case kGSGetSessionInfo: case kGSGetMemberList:
    case kCSSendChatMessage:
        return true;
    default:
        return false;
    }
}

bool FriendsClient::call(const char* operation, const std::string& body, json::Value& reply, std::string& note) {
    std::string session;
    {
        std::lock_guard lock(mutex_);
        session = session_;
    }
    if (session.empty()) { set_note(note, operation, "not signed in"); return false; }
    HttpExchange exchange;
    if (!send_("POST", std::string("NexonMessenger/") + operation, session, body, kCallTimeoutMs, exchange)) {
        set_note(note, operation, "service unreachable");
        return false;
    }
    if (exchange.status == 401) {
        std::lock_guard lock(mutex_);
        if (session_ == session) session_.clear();
        set_note(note, operation, "session expired");
        return false;
    }
    if (exchange.status != 200 || !json::parse(exchange.body, reply) || reply.kind != json::Value::Kind::Object) {
        char text[48];
        std::snprintf(text, sizeof text, "unexpected reply (HTTP %d)", exchange.status);
        set_note(note, operation, text);
        return false;
    }
    return true;
}

bool FriendsClient::sign_in(std::string& note) {
    {
        std::lock_guard lock(mutex_);
        if (!session_.empty()) return true;
        // The ticket is single use on the server: a second sign-in with it can only fail.
        if (ticket_spent_) { note = "messenger sign-in: ticket already used"; return false; }
        ticket_spent_ = true;
    }
    uint32_t announced = 0;
    {
        std::lock_guard lock(mutex_);
        announced = client_code_;
    }
    // The server shows every record under the code the client initialized with, so pass it on when known.
    const std::string login = announced != 0
        ? object({field("ticket", json::quote(ticket_)), field("gameCode", number(announced))})
        : object({field("ticket", json::quote(ticket_))});
    HttpExchange exchange;
    if (!send_("POST", "NexonMessenger/CNMLoginAuthFunc", "", login, kCallTimeoutMs, exchange)) {
        note = "messenger sign-in: service unreachable";
        return false;
    }
    json::Value reply;
    std::string session;
    int64_t code = 0, key = 0;
    std::string nickname;
    const json::Value* user = nullptr;
    if (exchange.status != 200 || !json::parse(exchange.body, reply) || !reply.string("session", session) || session.empty()
        || !(user = reply.get("user")) || !user->integer("gameCode", 0, UINT32_MAX, code) || !user->string("nickname", nickname)
        || !user->integer("id", 1, INT64_MAX, key)) {
        char text[64];
        std::snprintf(text, sizeof text, "messenger sign-in: refused (HTTP %d)", exchange.status);
        note = text;
        return false;
    }
    std::lock_guard lock(mutex_);
    session_ = session;
    own_code_ = static_cast<uint32_t>(code);
    own_nickname_ = from_utf8(nickname);
    own_key_ = static_cast<uint64_t>(key);
    return true;
}

bool FriendsClient::mutate(const char* operation, std::string body, std::vector<uint8_t>& reply, std::string& note) {
    // The client ignores these results and re-fetches its list after the refresh event; a refused
    // change shows up there. The service answers a change with the list it produced (friendList).
    reply = result_reply(1);
    return enqueue([this, operation, body = std::move(body)] {
        json::Value answer;
        remote(operation, body, &answer);
        const json::Value* list = answer.get("friendList");
        fetch_friends(list && list->kind == json::Value::Kind::Object ? list : nullptr);
        refresh();
    }, note);
}

bool FriendsClient::friends_reply(std::string& note, const json::Value* first) {
    std::vector<NativeCategory> categories;
    std::vector<NativeFriend> root;
    std::map<int64_t, size_t> index;
    int64_t total = 1;
    for (int64_t page = 0; page < total && page < kMaxPages; ++page) {
        json::Value fetched;
        if (!(page == 0 && first) && !call("CNMGetFriendListFunc", object({field("page", number(page))}), fetched, note)) return false;
        const json::Value& reply = page == 0 && first ? *first : fetched;
        const json::Value* cats = reply.get("categories");
        const json::Value* list = reply.get("friends");
        if (!reply.integer("totalPages", 0, kMaxPages, total) || !cats || cats->kind != json::Value::Kind::Array
            || !list || list->kind != json::Value::Kind::Array) {
            set_note(note, "CNMGetFriendListFunc", "malformed page");
            return false;
        }
        if (page == 0) {
            for (const auto& c : cats->items) {
                int64_t id = 0, code = 0;
                std::string name;
                if (!c.integer("id", 1, INT32_MAX, id) || !c.integer("gameCode", 0, UINT32_MAX, code) || !c.string("name", name)) {
                    set_note(note, "CNMGetFriendListFunc", "malformed category");
                    return false;
                }
                index[id] = categories.size();
                categories.push_back({static_cast<uint32_t>(id), static_cast<uint32_t>(code), from_utf8(name), {}});
            }
        }
        for (const auto& f : list->items) {
            int64_t id = 0, code = 0, category = 0, level = 0;
            std::string nickname, status;
            bool blocked = false;
            if (!f.integer("id", 1, INT64_MAX, id) || !f.integer("gameCode", 0, UINT32_MAX, code) || !f.string("nickname", nickname)
                || !f.integer("categoryId", 0, INT32_MAX, category) || !f.flag("blocked", blocked) || !f.string("status", status)
                || !f.integer("level", 0, UINT32_MAX, level)) {
                set_note(note, "CNMGetFriendListFunc", "malformed friend");
                return false;
            }
            NativeFriend native{static_cast<uint64_t>(id), static_cast<uint32_t>(code), from_utf8(nickname), blocked,
                status == "online", static_cast<uint32_t>(level)};
            const auto it = category == 0 ? index.end() : index.find(category);
            if (it == index.end()) root.push_back(std::move(native));
            else categories[it->second].members.push_back(std::move(native));
        }
    }
    char text[64];
    std::snprintf(text, sizeof text, "friends=%u categories=%u", static_cast<unsigned>(root.size()), static_cast<unsigned>(categories.size()));
    note = text;
    std::lock_guard lock(mutex_);
    friends_cache_ = friend_list_reply(categories, root);
    list_categories_ = std::move(categories);
    list_root_ = std::move(root);
    return true;
}

bool FriendsClient::games_reply(std::string& note) {
    json::Value reply;
    if (!call("CNMGetSupportGameListFunc", "{}", reply, note)) return false;
    const json::Value* games = reply.get("games");
    if (!games || games->kind != json::Value::Kind::Array) { set_note(note, "CNMGetSupportGameListFunc", "malformed reply"); return false; }
    std::vector<uint32_t> codes;
    std::map<uint32_t, std::pair<std::wstring, std::wstring>> names;
    for (const auto& g : games->items) {
        int64_t code = 0;
        std::string full, short_name;
        if (!g.integer("code", 0, UINT32_MAX, code)) { set_note(note, "CNMGetSupportGameListFunc", "malformed game"); return false; }
        g.string("fullName", full);
        g.string("shortName", short_name);
        codes.push_back(static_cast<uint32_t>(code));
        names[static_cast<uint32_t>(code)] = {from_utf8(full), from_utf8(short_name)};
    }
    std::lock_guard lock(mutex_);
    games_cache_ = support_game_list_reply(codes);
    game_names_ = std::move(names);
    return true;
}

bool FriendsClient::handle(uint32_t code, const std::vector<uint8_t>& request, std::vector<uint8_t>& body, std::string& note) {
    switch (code) {
    case kInitialize: {
        // [base result][game code][unknown_44] (cn-324 sub_7BFBF0 -> sub_7BEC00, id-400 sub_8962E0, tw-454 sub_1004390);
        // it precedes the messenger sign-in that announces the code to the server.
        Reader fields = request_fields(request);
        uint32_t game = 0, unknown_44 = 0;
        if (!fields.u32(game) || !fields.u32(unknown_44)) { note = "initialize unreadable"; return false; }
        {
            std::lock_guard lock(mutex_);
            client_code_ = game;
        }
        body = result_reply(1);
        note = "messenger client game=" + std::to_string(game);
        return true;
    }
    case kRegisterCallback: {
        uint32_t window = 0, message = 0;
        if (!read_register_callback(request, window, message)) { note = "register-callback unreadable"; return false; }
        std::lock_guard lock(mutex_);
        window_ = window;
        message_ = message;
        body = result_reply(1);
        return true;
    }
    case kResetCallback: {
        std::lock_guard lock(mutex_);
        window_ = 0;
        body = result_reply(1);
        return true;
    }
    case kLoginVirtual:
        body = result_reply(1);
        return true;
    case kLoginPassport:
    case kLoginMessenger: {
        // The reply is ignored (tw-454 sub_A7FD30); 4097 code 1 is what opens the friend window.
        // cn-324 sub_79FDC0 selects passport login for locale 2; sub_7BDC70 reads only the base result on reply.
        // Authenticate with the server-issued Messenger ticket, never the client's opaque publisher passport.
        // The lists are fetched before 4097, so the window's first read is answered from the cache.
        body = result_reply(1);
        note = "messenger sign-in queued";
        enqueue([this] {
            std::string result;
            json::Value reply;
            bool fresh = false;
            {
                std::lock_guard lock(mutex_);
                fresh = session_.empty();
            }
            if (sign_in(result) && call("CNMLoginMessengerFunc", "{}", reply, result)) {
                fetch_friends();
                std::string games;
                if (fresh && !games_reply(games)) write_log(games);
                post(kMessengerReplyEvent, bare_event(1));
                result = "messenger signed in";
            }
            write_log(result);
        }, note);
        return true;
    }
    case kLogoutMessenger:
        body = result_reply(1);
        return enqueue([this] { remote("CNMLogoutMessengerFunc", "{}"); }, note);
    case kLogoutAuth:
        body = result_reply(1);
        return enqueue([this] {
            remote("CNMLogoutAuthFunc", "{}");
            std::lock_guard lock(mutex_);
            session_.clear();
            friends_cache_.reset();
            list_categories_.clear();
            list_root_.clear();
        }, note);
    case kChangeMyLevel: {
        uint32_t level = 0;
        if (!read_change_level(request, level)) { note = "change-level unreadable"; return false; }
        return mutate("CNMChangeMyLevelFunc", object({field("level", number(level))}), body, note);
    }
    case kGetFriendList: {
        std::lock_guard lock(mutex_);
        if (friends_cache_) { body = *friends_cache_; note = "cached"; }
        else body = result_reply(0); // the reader treats result 0 as "no list" (kr-812 sub_917070)
        return true;
    }
    case kGetSupportGameList: {
        std::lock_guard lock(mutex_);
        body = games_cache_.empty() ? support_game_list_reply({}) : games_cache_;
        return true;
    }
    case kGetGameFullName:
    case kGetGameShortName: {
        uint32_t game = 0;
        if (!read_game_code(request, game)) { note = "game-name unreadable"; return false; }
        std::lock_guard lock(mutex_);
        const auto it = game_names_.find(game);
        const std::wstring* name = it == game_names_.end() ? nullptr
            : code == kGetGameFullName ? &it->second.first : &it->second.second;
        body = game_name_reply(name && !name->empty() ? name : nullptr); // the 4101 prompt falls back to a built-in name on 0
        return true;
    }
    case kRequestNewFriend: {
        std::wstring nickname, message;
        if (!read_request_new_friend(request, nickname, message)) { note = "request-friend unreadable"; return false; }
        return mutate("CNMRequestNewFriendFunc", object({field("nickname", json::quote(to_utf8(nickname))),
            field("message", json::quote(to_utf8(message)))}), body, note);
    }
    case kConfirmNewFriend: {
        uint32_t handle = 0;
        uint8_t answer = 0;
        if (!read_confirm_new_friend(request, handle, answer) || !decision(answer)) { note = "confirm-friend unreadable"; return false; }
        return mutate("CNMConfirmNewFriendFunc", object({field("handle", number(handle)), field("decision", json::quote(decision(answer)))}), body, note);
    }
    case kBlockFriend: {
        uint64_t key = 0;
        uint8_t variant = 0;
        if (!read_block_friend(request, key, variant) || key > INT64_MAX) { note = "block-friend unreadable"; return false; }
        return mutate("CNMBlockFriendFunc", object({field("friendId", number(static_cast<int64_t>(key))),
            field("blocked", variant == 1 ? "true" : "false")}), body, note);
    }
    case kAddFriendToCategory:
    case kDeleteFriendFromCategory: {
        uint64_t key = 0;
        uint32_t category = 0;
        if (!read_friend_category(request, key, category) || key > INT64_MAX || category > INT32_MAX) { note = "friend-category unreadable"; return false; }
        return mutate(code == kAddFriendToCategory ? "CNMAddFriendToCategoryFunc" : "CNMDeleteFriendFromCategoryFunc",
            object({field("friendId", number(static_cast<int64_t>(key))), field("categoryId", number(category))}), body, note);
    }
    case kMoveFriendCategory: {
        uint64_t key = 0;
        uint32_t from = 0, to = 0;
        if (!read_move_friend(request, key, from, to) || key > INT64_MAX || from > INT32_MAX || to > INT32_MAX) { note = "move-friend unreadable"; return false; }
        return mutate("CNMMoveFriendCategoryFunc", object({field("friendId", number(static_cast<int64_t>(key))),
            field("fromCategoryId", number(from)), field("toCategoryId", number(to))}), body, note);
    }
    case kAddCategory: {
        std::wstring name;
        if (!read_add_category(request, name)) { note = "add-category unreadable"; return false; }
        return mutate("CNMAddCategoryFunc", object({field("name", json::quote(to_utf8(name)))}), body, note);
    }
    case kDeleteCategory: {
        uint32_t category = 0;
        if (!read_delete_category(request, category) || category > INT32_MAX) { note = "delete-category unreadable"; return false; }
        return mutate("CNMDeleteCategoryFunc", object({field("categoryId", number(category))}), body, note);
    }
    case kChangeCategoryName: {
        uint32_t category = 0;
        std::wstring name;
        if (!read_rename_category(request, category, name) || category > INT32_MAX) { note = "rename-category unreadable"; return false; }
        return mutate("CNMChangeCategoryNameFunc", object({field("categoryId", number(category)), field("name", json::quote(to_utf8(name)))}), body, note);
    }
    case kRequestChatSession: {
        // The window opens on the 40961 event the server queues for the caller, not on this reply.
        uint64_t key = 0;
        if (!read_request_chat_session(request, key) || key == 0 || key > INT64_MAX) { note = "request-chat unreadable"; return false; }
        body = chat_session_reply();
        return enqueue([this, key] {
            // The reply carries the chat, so the window opens after one exchange; the chat_opened event
            // that follows for the same chat is then skipped.
            json::Value reply;
            remote("CNMRequestChatSessionFunc", object({field("friendId", number(static_cast<int64_t>(key)))}), &reply);
            const json::Value* chat = reply.get("chat");
            int64_t id = 0;
            std::vector<ChatMember> members;
            if (chat && chat->kind == json::Value::Kind::Object && chat_members(*chat, self_key(), id, members)) open_chat(static_cast<uint32_t>(id), std::move(members));
        }, note, true);
    }
    case kGSRegisterCallback:
        // Every chat event goes to the 4098 window, which parses both window messages alike (kr-812 sub_8E2040).
        body = result_reply(1);
        return true;
    case kGSGetSessionInfo:
    case kGSGetMemberList: {
        // Members come from the 40961 event that opened the window, which precedes these reads.
        uint32_t session = 0;
        if (!read_session_id(request, session) || session == 0 || session > INT32_MAX) { note = "session-info unreadable"; return false; }
        std::lock_guard lock(mutex_);
        const auto it = chats_.find(session);
        if (it == chats_.end()) { note = "messenger chat session unknown"; return false; }
        body = code == kGSGetSessionInfo ? session_info_reply(it->second) : member_list_reply(it->second);
        return true;
    }
    case kGSWantClose: {
        uint32_t session = 0;
        if (!read_session_id(request, session) || session == 0 || session > INT32_MAX) { note = "close-chat unreadable"; return false; }
        {
            std::lock_guard lock(mutex_);
            chats_.erase(session);
        }
        body = want_close_reply();
        return enqueue([this, session] { remote("CNMGSWantCloseFunc", object({field("chatId", number(session))})); }, note, true);
    }
    case kCSSendChatMessage: {
        uint32_t session = 0;
        std::wstring text;
        if (!read_send_chat_message(request, session, text) || session == 0 || session > INT32_MAX) { note = "chat-message unreadable"; return false; }
        // The client never adds the sender's own line (kr-812 sub_86BC00); the stand-in posts it at once
        // instead of waiting for the server's echo, which the event pump then skips.
        body = result_reply(1);
        if (!enqueue([this, session, line = to_utf8(text)] {
            remote("CNMCSSendChatMessageFunc", object({field("chatId", number(session)), field("text", json::quote(line))}));
        }, note, true))
            return false;
        std::wstring self;
        {
            std::lock_guard lock(mutex_);
            self = own_nickname_;
        }
        post(kChatMessageReceivedEvent, chat_message_event(session, self, text));
        return true;
    }
    default:
        return false;
    }
}

void FriendsClient::post(uint32_t event, std::vector<uint8_t> buffer) {
    uint32_t window = 0, message = 0;
    {
        std::lock_guard lock(mutex_);
        window = window_;
        message = message_;
    }
    if (window != 0 && !stopped_.load()) post_(window, message, event, std::move(buffer));
}

uint64_t FriendsClient::self_key() const {
    std::lock_guard lock(mutex_);
    return own_key_;
}

void FriendsClient::refresh() { post(kRefreshEvent, bare_event(2)); }

// Records a chat and opens its window once, whether the opener's reply or the chat_opened event comes first.
void FriendsClient::open_chat(uint32_t id, std::vector<ChatMember> members) {
    std::vector<ChatMember> shown;
    {
        std::lock_guard lock(mutex_);
        if (chats_.count(id)) return;
        chats_[id] = members;
        shown = std::move(members);
    }
    post(kSessionCreatedEvent, session_created_event(id, shown));
}

bool FriendsClient::can_poll() const {
    std::lock_guard lock(mutex_);
    return !stopped_.load() && !session_.empty() && window_ != 0;
}

void FriendsClient::set_credentials(std::string ticket, HttpSend send) {
    std::lock_guard lock(mutex_);
    ticket_ = std::move(ticket);
    send_ = std::move(send);
    ticket_spent_ = false;
}

void FriendsClient::stop() {
    stopped_.store(true);
    jobs_ready_.notify_all();
    chat_ready_.notify_all();
}

bool FriendsClient::poll_once(unsigned wait_seconds) {
    std::string session;
    int64_t cursor = 0;
    {
        std::lock_guard lock(mutex_);
        session = session_;
        cursor = cursor_;
    }
    if (session.empty() || stopped_.load()) return false;
    HttpExchange exchange;
    const std::string path = "NexonMessenger/events?after=" + number(cursor) + "&wait=" + std::to_string(wait_seconds);
    if (!send_("GET", path, session, "", (wait_seconds + 10) * 1000, exchange)) return false;
    if (exchange.status == 401) {
        std::lock_guard lock(mutex_);
        if (session_ == session) session_.clear();
        return false;
    }
    json::Value batch;
    int64_t next = 0;
    bool reset = false;
    const json::Value* events = nullptr;
    if (exchange.status != 200 || !json::parse(exchange.body, batch) || !batch.integer("next", 0, INT64_MAX, next)
        || !batch.flag("reset", reset) || !(events = batch.get("events")) || events->kind != json::Value::Kind::Array)
        return false;
    bool changed = reset, patched = false;
    for (const auto& e : events->items) {
        std::string kind;
        if (!e.string("kind", kind)) continue;
        if (kind == "chat_opened") {
            const json::Value* chat = e.get("chat");
            int64_t id = 0;
            std::vector<ChatMember> members;
            if (chat && chat_members(*chat, self_key(), id, members)) open_chat(static_cast<uint32_t>(id), std::move(members));
            continue;
        }
        if (kind == "chat_message") {
            const json::Value* from = e.get("from");
            int64_t chat = 0, sender = 0;
            std::string nickname, text;
            // The sender's own line was already shown when it was sent.
            if (from && from->integer("id", 1, INT64_MAX, sender) && static_cast<uint64_t>(sender) == self_key()) continue;
            if (from && e.integer("chatId", 1, INT32_MAX, chat) && from->string("nickname", nickname) && e.string("text", text))
                post(kChatMessageReceivedEvent, chat_message_event(static_cast<uint32_t>(chat), from_utf8(nickname), from_utf8(text)));
            continue;
        }
        if (kind == "presence_changed") {
            int64_t key = 0, level = 0;
            std::string status;
            if (!reset && !changed && e.integer("friendId", 1, INT64_MAX, key) && e.string("status", status)
                && e.integer("level", 0, UINT32_MAX, level)
                && patch_presence(static_cast<uint64_t>(key), status == "online", static_cast<uint32_t>(level))) {
                patched = true;
                continue;
            }
            changed = true;
            continue;
        }
        if (kind != "friend_request") { changed = true; continue; }
        const json::Value* request = e.get("request");
        const json::Value* from = request ? request->get("from") : nullptr;
        int64_t handle = 0, code = 0;
        std::string nickname, message;
        if (!request || !from || !request->integer("handle", 1, UINT32_MAX, handle) || !request->string("message", message)
            || !from->integer("gameCode", 0, UINT32_MAX, code) || !from->string("nickname", nickname))
            continue;
        post(kRequestNewFriendEvent, request_new_friend_event(static_cast<uint32_t>(handle), static_cast<uint32_t>(code),
            from_utf8(nickname), from_utf8(message)));
    }
    // One re-fetch covers every list change of the batch; presence alone is patched in place.
    if (changed) fetch_friends();
    if (changed || patched) refresh();
    std::lock_guard lock(mutex_);
    if (session_ == session) cursor_ = next;
    return true;
}

} // namespace bubblefighter::nmco

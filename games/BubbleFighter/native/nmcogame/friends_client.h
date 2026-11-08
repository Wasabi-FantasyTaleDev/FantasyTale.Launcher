#pragma once
// The stand-in's friend list: answers the native friend codes from the messenger HTTP API and turns
// its change events into the window messages the client registered for. Transport and window
// posting are injected so the mapping is testable without a network or a game window.
//
// The client calls NMCO_CallNMFunc on its game thread and waits for the return, as it did with the
// local publisher service. No answer may therefore wait on the network: replies come from a cache and
// a queue, and every HTTP exchange runs on the worker, chat or event-pump thread, which report back
// through the same window events the native service posted. Work that the window waits for is kept to
// one exchange: a change's reply carries the new list, presence changes patch the cached list, a chat
// opens from its own reply and the sender's line is shown at once.
#include "friends.h"
#include "json.h"

#include <atomic>
#include <condition_variable>
#include <deque>
#include <cstdint>
#include <functional>
#include <map>
#include <mutex>
#include <optional>
#include <string>
#include <vector>

namespace bubblefighter::nmco {

/// One HTTP exchange with the messenger service. `body` is empty for GET.
struct HttpExchange {
    int status = 0;
    std::string body;
};

/// Sends one request; false on a transport failure (no reply at all).
using HttpSend = std::function<bool(const char* method, const std::string& path, const std::string& bearer,
    const std::string& body, unsigned timeout_ms, HttpExchange& reply)>;
/// Hands an event buffer to the client window; false when the window did not take it.
using PostEvent = std::function<bool(uint32_t window, uint32_t message, uint32_t event, std::vector<uint8_t> buffer)>;
/// Records the outcome of background work, which has no caller to report to.
using LogLine = std::function<void(const std::string& line)>;

/// Messenger sign-in state of one client process.
class FriendsClient {
public:
    FriendsClient(std::string ticket, HttpSend send, PostEvent post, LogLine log = {});

    /// True for the codes this class answers or observes.
    static bool owns(uint32_t code);

    /// Answers `code` without network access. Service work is queued; false with a note when the
    /// request is unreadable, the queue is full, or the cached state cannot answer it.
    bool handle(uint32_t code, const std::vector<uint8_t>& object, std::vector<uint8_t>& body, std::string& note);

    /// Fetches events once (`wait_seconds` long poll) and posts them. False when the poll failed or the session ended.
    bool poll_once(unsigned wait_seconds);
    /// True while a signed-in session and a registered window exist.
    bool can_poll() const;
    /// Runs one queued job, waiting up to `wait_ms` for one; false when none ran.
    bool work_once(unsigned wait_ms);
    /// Runs one queued chat job (open, line, close) on its own lane, so a chat never waits behind list work.
    bool work_chat_once(unsigned wait_ms);
    /// Stops event delivery and queued work; the threads exit after their current exchange.
    void stop();
    /// Supplies the messenger sign-in an in-game sign-in received; the client signs in to the messenger only
    /// after its own login, so no exchange is in flight yet.
    void set_credentials(std::string ticket, HttpSend send);

private:
    bool call(const char* operation, const std::string& body, json::Value& reply, std::string& note);
    bool sign_in(std::string& note);
    bool friends_reply(std::string& note, const json::Value* first = nullptr);
    bool games_reply(std::string& note);
    void fetch_friends(const json::Value* first = nullptr);
    bool patch_presence(uint64_t key, bool online, uint32_t level);
    bool enqueue(std::function<void()> job, std::string& note, bool chat = false);
    bool work(std::deque<std::function<void()>>& jobs, std::condition_variable& ready, unsigned wait_ms);
    bool mutate(const char* operation, std::string body, std::vector<uint8_t>& reply, std::string& note);
    void remote(const char* operation, std::string body, json::Value* reply = nullptr);
    void write_log(const std::string& line);
    void post(uint32_t event, std::vector<uint8_t> buffer);
    void refresh();
    void open_chat(uint32_t id, std::vector<ChatMember> members);
    uint64_t self_key() const;

    std::string ticket_;
    HttpSend send_;
    PostEvent post_;
    LogLine log_;
    mutable std::mutex mutex_;
    std::string session_;
    bool ticket_spent_ = false;
    uint32_t own_code_ = 0;
    uint64_t own_key_ = 0;
    uint32_t client_code_ = 0;
    std::wstring own_nickname_;
    uint32_t window_ = 0;
    uint32_t message_ = 0;
    int64_t cursor_ = 0;
    std::optional<std::vector<uint8_t>> friends_cache_; // the 16385 reply body; none until fetched
    std::vector<NativeCategory> list_categories_;        // the list the cache was written from
    std::vector<NativeFriend> list_root_;
    std::vector<uint8_t> games_cache_;
    std::map<uint32_t, std::pair<std::wstring, std::wstring>> game_names_; // code -> full, short
    std::map<uint32_t, std::vector<ChatMember>> chats_; // from chat_opened events
    std::deque<std::function<void()>> jobs_, chat_jobs_;
    std::condition_variable jobs_ready_, chat_ready_;
    std::atomic<bool> stopped_{false};
};

/// UTF-16 to UTF-8 and back for the JSON boundary.
std::string to_utf8(const std::wstring& text);
std::wstring from_utf8(const std::string& text);

} // namespace bubblefighter::nmco

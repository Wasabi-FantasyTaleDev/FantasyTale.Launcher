#include "http.h"

#include <windows.h>
#include <winhttp.h>

#include <memory>

namespace bubblefighter::nmco {

namespace {

constexpr size_t kMaxReplyBytes = 1u << 20;

struct HandleCloser {
    void operator()(void* handle) const { if (handle) WinHttpCloseHandle(handle); }
};
using Handle = std::unique_ptr<void, HandleCloser>;

struct Target {
    std::wstring host;
    INTERNET_PORT port = 0;
    bool secure = false;
    std::wstring path; // base path without a trailing slash
};

bool send(const std::shared_ptr<void>& session, const Target& target, const char* method, const std::string& path,
    const std::string& bearer, const std::string& body, unsigned timeout_ms, HttpExchange& reply) {
    const int timeout = static_cast<int>(timeout_ms);
    Handle connection(WinHttpConnect(session.get(), target.host.c_str(), target.port, 0));
    if (!connection) return false;
    const std::wstring object = target.path + L"/" + from_utf8(path);
    Handle request(WinHttpOpenRequest(connection.get(), from_utf8(method).c_str(), object.c_str(), nullptr, WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES, target.secure ? WINHTTP_FLAG_SECURE : 0));
    if (!request || !WinHttpSetTimeouts(request.get(), timeout, timeout, timeout, timeout)) return false;
    std::wstring headers = L"Accept: application/json\r\n";
    if (!body.empty()) headers += L"Content-Type: application/json\r\n";
    if (!bearer.empty()) headers += L"Authorization: Bearer " + from_utf8(bearer) + L"\r\n";
    if (!WinHttpSendRequest(request.get(), headers.c_str(), static_cast<DWORD>(headers.size()),
            body.empty() ? WINHTTP_NO_REQUEST_DATA : const_cast<char*>(body.data()), static_cast<DWORD>(body.size()),
            static_cast<DWORD>(body.size()), 0)
        || !WinHttpReceiveResponse(request.get(), nullptr))
        return false;
    DWORD status = 0, size = sizeof status;
    if (!WinHttpQueryHeaders(request.get(), WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX,
            &status, &size, WINHTTP_NO_HEADER_INDEX))
        return false;
    reply.status = static_cast<int>(status);
    reply.body.clear();
    for (;;) {
        DWORD available = 0;
        if (!WinHttpQueryDataAvailable(request.get(), &available)) return false;
        if (available == 0) return true;
        if (reply.body.size() + available > kMaxReplyBytes) return false;
        const size_t at = reply.body.size();
        reply.body.resize(at + available);
        DWORD read = 0;
        if (!WinHttpReadData(request.get(), reply.body.data() + at, available, &read)) return false;
        reply.body.resize(at + read);
    }
}

} // namespace

HttpSend make_http_send(const std::string& base_url, std::string& error) {
    const std::wstring url = from_utf8(base_url);
    URL_COMPONENTS parts{};
    parts.dwStructSize = sizeof parts;
    parts.dwHostNameLength = parts.dwUrlPathLength = static_cast<DWORD>(-1);
    if (url.empty() || !WinHttpCrackUrl(url.c_str(), static_cast<DWORD>(url.size()), 0, &parts)
        || (parts.nScheme != INTERNET_SCHEME_HTTP && parts.nScheme != INTERNET_SCHEME_HTTPS) || parts.dwHostNameLength == 0) {
        error = "messenger url is not an http(s) URL";
        return {};
    }
    Target target;
    target.host.assign(parts.lpszHostName, parts.dwHostNameLength);
    target.port = parts.nPort;
    target.secure = parts.nScheme == INTERNET_SCHEME_HTTPS;
    target.path.assign(parts.lpszUrlPath ? parts.lpszUrlPath : L"", parts.lpszUrlPath ? parts.dwUrlPathLength : 0);
    while (!target.path.empty() && target.path.back() == L'/') target.path.pop_back();
    std::shared_ptr<void> session(WinHttpOpen(L"FantasyTale-nmcogame", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_NO_PROXY_NAME,
        WINHTTP_NO_PROXY_BYPASS, 0), HandleCloser{});
    if (!session) {
        error = "messenger http session unavailable";
        return {};
    }
    return [session, target](const char* method, const std::string& path, const std::string& bearer, const std::string& body,
               unsigned timeout_ms, HttpExchange& reply) { return send(session, target, method, path, bearer, body, timeout_ms, reply); };
}

} // namespace bubblefighter::nmco

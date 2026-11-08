#pragma once
#include <string>
#include <string_view>

namespace korean_auth {
// The server redeems one complete 64-character ticket; no native short-string truncation is allowed.
inline bool make_passport(std::string_view ticket, std::wstring& passport) {
    if (ticket.size() != 64 || ticket.find_first_not_of("0123456789abcdef") != std::string_view::npos) return false;
    passport.assign(L"bff-ticket:");
    passport.append(ticket.begin(), ticket.end());
    return true;
}

// A name-only room passes a plain identity through the same native string consumer.
inline bool make_name_passport(std::string_view name, std::wstring& passport) {
    if (name.empty() || name.size() > 32 || name.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-.@") != std::string_view::npos) return false;
    passport.assign(name.begin(), name.end());
    return true;
}

// Native login runs only after connection, GamePatch and UI initialization, while still in the login stage.
inline bool can_submit(bool connected, bool patch_ready, bool app_ready, bool dependencies_ready, bool already_submitted) {
    return connected && patch_ready && app_ready && dependencies_ready && !already_submitted;
}
}

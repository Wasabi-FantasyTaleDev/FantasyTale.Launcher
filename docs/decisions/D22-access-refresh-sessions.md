# D22. Short-lived access and rotating refresh sessions (2026-10-10)

## Initial decision (superseded by the persistence amendment below)

The launcher keeps the access token and refresh token in process memory only. A
short-lived access token limits the value of a leaked request credential; a
separate refresh token avoids retaining the account password and allows the
launcher to renew without interrupting the user. The server stores only a hash
of the refresh token, rotates it on use, and revokes it on logout or account
ban. The launcher serializes renewal per account and fences a late response by
the account session identity.

The API deliberately has no password-based background relogin and no token
persistence across launcher restarts. A deployment that still returns only the
single legacy bearer field cannot silently renew; it must implement the explicit
login and refresh envelope documented in `docs/launcher-auth.md`. Reopen this
decision if a platform-protected credential store is later approved.

## Persistence amendment (2026-10-10)

The maintainer requires uninterrupted sign-in across launcher and server restarts.
This requirement replaces the process-only refresh-token restriction above.

The launcher keeps the short-lived access token in process memory. It retains the
refresh token only in a Windows-current-user DPAPI protected file, never in an
instance, browser storage, plugin package, or update package. A separate refresh
token avoids retaining the account password and allows the launcher to renew without
interrupting the user after either process or server restart. The server stores only
a hash of the refresh token, rotates it on use, and revokes it on logout or account
ban. The launcher serializes renewal per account and fences a late response by the
account session identity.

The API deliberately has no password-based background relogin. A deployment that
still returns only the single legacy bearer field cannot silently renew; it must
implement the explicit login and refresh envelope documented in
`docs/launcher-auth.md`. Reauthentication is required only when the refresh session
is expired, revoked, rejected, or no longer decryptable under the current Windows
user.

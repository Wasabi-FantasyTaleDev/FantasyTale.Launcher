# Launcher account sessions

The launcher does not store passwords by default and never persists access tokens.
It retains the refresh credential, encrypted with the current Windows user's DPAPI key in the
launcher data directory. The protected file is not exposed to the frontend,
game instances, plugins, or launcher update packages. When the launcher starts,
it restores the last validated profile and silently exchanges the refresh token
for a new access token before account data or a launch ticket is requested.

Account lists retain unselected accounts while their protected refresh sessions
remain valid, including after restarting the Launcher. The public account snapshot
includes `expiresAt` for access and optional `refreshExpiresAt` for renewal; it
contains no access or refresh credential. The UI removes a displayed account only
after both available deadlines expire. Selecting a retained account renews it
before authenticated use, and a server rejection still removes only that session.
If automatic renewal rejects the selected session, listing accounts returns the
remaining accounts with no active selection. Service outages, rejected discovery,
malformed responses and Launcher upgrade requirements still report their errors;
they do not discard retained sessions.

During the initial account and server-login requests, Home shows a connection
ring in the account area and a blue Connecting button. Version selection remains
available, while duplicate login prompts are blocked until the result is known.
A restored session switches directly to its character display; an empty or
rejected session returns to ordinary sign-in, and failures end the busy state
while preserving the error. Late responses from another server cannot end the
current server's indicator or replace its account. Refreshing the same server
retains valid account details instead of briefly presenting a signed-out state.
Manual sign-in uses the same busy-button treatment until the request finishes.
These indicators do not save credentials or submit remembered passwords.

## Remember password

The login form offers an unchecked Remember password option. Only a successful
opted-in login retains a password, in the separate Windows-user-encrypted
`remembered-logins.bin` file. Reopening the same server's login form fills its
last remembered username and password and keeps the option checked. It does not
submit the form automatically. Changing the username clears an unchanged filled
password; switching server, registering, closing or leaving the dialog clears
its secrets and rejects delayed fills. These credentials are scoped to both the
server ID and exact API address, independently of game client versions.

Signing out revokes the refresh session but retains an explicitly remembered
password for a later login. Signing in successfully with Remember password
unchecked forgets that account's stored password. Failed authentication does not
save a new password or erase the previous choice. Editing or removing the server
forgets its passwords. Password and refresh storage have no fixed account-count
or file-size ceiling. Corrupt or undecryptable storage cannot fill
the form. Neither password nor refresh storage may enter a launcher release.

Logout shares the account's rotation gate: a refresh already in progress finishes
before logout revokes its newly rotated credentials. Switching to another account
while waiting does not retarget logout, and a newer login for the same username
cannot be removed by the older pending operation. Canceling the wait leaves the
account available. Remote failure after removal still reports an error while
retaining other accounts.

## Required API contract

`POST /v1/auth/login` accepts `{ "username", "password", "launcherVersion" }` and
returns the account profile with this session envelope:

```json
{
  "accessToken": "opaque-access-token",
  "accessExpiresAt": "2026-10-10T18:30:00Z",
  "refreshToken": "opaque-refresh-token",
  "refreshExpiresAt": "2026-11-09T18:00:00Z",
  "account": { "username": "player", "displayName": "player", "stats": {} }
}
```

The access token should be short lived. The refresh token should be long lived,
stored only as a hash by the server, and scoped to the account and launcher
client. The server should rotate it on every successful refresh and revoke the
old value. Neither value may be logged or included in an API error.

`POST /v1/auth/refresh` accepts `{ "refreshToken": "...", "launcherVersion": "0.1.14" }` and returns a new
access/refresh pair using the same four fields. The launcher refreshes shortly
before the access deadline, serializes concurrent refreshes for one account, and
retries one request when a still-live access token is rejected. A rejected or
expired refresh token removes only that server/account session and asks the user
to sign in again.

`POST /v1/auth/logout` accepts `{ "refreshToken": "..." }` and an access bearer
header when available. The server must accept the refresh credential without a
live access token, so an account can be removed immediately after launcher restart.
It revokes only that refresh session and its outstanding launch tickets, returning
HTTP 200 with a JSON object. A launcher restart therefore does not require a new
login, and a server restart does not require one either when the durable refresh
session remains valid. If an access token is rejected, the launcher refreshes
silently and retries the request once.

Temporary network failures and HTTP 5xx do not erase the retained refresh session.
Routing requirements are discovered again after launcher restart, before a restored
refresh credential or logout request is transmitted. Public catalog discovery can
retry plain HTTP through a resolved IP without credentials; the validated server
policy then controls authenticated requests, overriding local IP preferences.
A rejected catalog does not establish that the refresh token was rejected and
therefore does not erase it. Explicit logout still removes the local account even
when discovery or remote revocation fails, and reports that failure to the user.
API deadlines are checked using the injected clock. Access expiry must be within
24 hours, and refresh expiry must be later than access expiry and within 365 days.

The user must sign in again only when the refresh token expires or is revoked,
the account is rejected by the server, the user explicitly logs out, or the
protected Windows-user storage cannot be opened (for example after changing
the Windows account or deleting the launcher data).

## Launcher versions and plugin policy

The launch request keeps `build` as the wire dialect identity, for example
`kr-1256`, even when the selected client profile is `kr-1256-2`. Optional
`release_version` and `patch_version` strings describe that installation in the
same `POST /v1/launch` body. Unknown labels are omitted. Servers select packet
layouts using `build`; these descriptive labels do not select a protocol. Launches
from a build definition also send its numeric `protocol_version` in that same
request. A valid access session needs only one `POST /v1/launch`; refresh and
public routing discovery occur when the session needs renewal. A launch retains
the account selected at its start. Switching accounts during renewal or ticket
issuance cancels that launch, including a switch back to the original account.

The host reuses the selected server's previously validated catalog during launch;
explicit discovery still refreshes it. This snapshot is scoped to the exact
server ID, API URL and game. Editing or removing the server invalidates it, and a
late response cannot restore a removed scope or overwrite a newer completed
discovery. Without a validated snapshot, the host first reads the public catalog.
Joined rooms also retain their source-membership check. These checks, credential
renewal and required plugin transfers can add requests to a cold or changed launch.

For Bubble Fighter, optional `version.dat` metadata overrides profile labels:
`[PATCH] VERSION` supplies the patch label and `[Common] Version` supplies the
release label. When PATCH is absent, the Common value also supplies the patch
label. Missing, unreadable, oversized or unrecognized metadata falls back to the
profile's optional `release_version` and `patch_version`. It never makes a
version file mandatory or adds a network request.

The same launch body also carries `launcher_language` (current UI language),
`native_language` (the unmodified client's language) and `client_language` (the
selected language, including an installed translation). Unknown client language
metadata is omitted. These values describe the launch and do not choose the
wire dialect. Selecting a translation does not change the native language.
There is no separate language-metadata request. Session restoration, token
refresh or a one-time authentication retry can still add requests when needed;
a fresh valid session sends one ticket request.

Every launcher API request includes `X-FantasyTale-Launcher-Version`. Login,
registration, refresh and launch JSON bodies also include `launcherVersion`.
The value is the packaged application SemVer, without informational source
metadata; the About page and compatibility checks use that same value. Compare
numeric SemVer components and prerelease precedence: `1.0.10` is newer than
`1.0.9`, and `1.0.10-rc.1` is older than `1.0.10`. There is no second release
counter to keep synchronized.

The public catalog and successful login/refresh envelopes may additionally return:

```json
{
  "minimumLauncherVersion": "0.1.33",
  "clientPolicy": {
    "version": 1,
    "sources": [{ "name": "community", "kind": "http", "baseUrl": "https://plugins.example/", "catalogPath": "source.toml" }],
    "plugins": [
      { "id": "bubblefighter.fantasy.font-weight", "rules": [{ "id": "offer", "type": "recommend", "builds": ["kr-812"], "sources": ["community"] }] },
      { "id": "bubblefighter.fantasy.cn-auth", "rules": [{ "id": "need", "type": "require", "builds": ["cn-404"] }, { "id": "floor", "type": "min_version", "builds": ["cn-404"], "version": "1.0.2" }] }
    ]
  }
}
```

Both fields are optional. The policy is described in the
[Multiplayer guide](multiplayer.md#server-plugin-policy). The policy from the
selected account's last sign-in or refresh is the one launches use; the catalog's
copy is shown before sign-in. Plugin identities resolve through the player's
stores; a policy never supplies executable paths or download addresses.

Reject a gated operation with HTTP 426 and
`{"error":"launcher_update_required","minimumLauncherVersion":"0.1.14"}`.
The launcher retains the account and opens the shared update flow. Public catalog
discovery must remain readable by older launchers, and logout must remain exempt
from the minimum-version gate, including refresh-only logout after a restart.
Malformed extension metadata fails validation. A refresh response's valid rotated
credentials are persisted before extension validation, so an invalid or newer
policy cannot discard the only live refresh token.

The server owns the built-in WebUI **Authentication** tab and validated deployment
configuration for these policies. Launcher support does not establish that the
server UI or live deployment has been updated; those require server-side acceptance.

## Deployment requirement

The launcher client validates and consumes this contract. A server that only
returns the legacy single `token`/`expiresAt` response does not satisfy the test
contract and is rejected as an invalid session response. The deployed API has now
been checked with an existing account: login and account profile, separate-process
restore without a password, rotation and old-token rejection, refresh-only logout,
and raw build-bound launch tickets. A controlled access HTTP 401 was followed by
real refresh and retry against the deployment. This verifies client recovery but
does not claim that the deployed server was restarted during the check or that a
retail game loaded its character.

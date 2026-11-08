# Authoritative session state

Native Bubble Fighter plugins can share `native/common/session_state.h` and
`session_state.cpp`, linked with the bounded reader in `json.cpp`. This helper
uses Nirinsou Link's existing companion API. It does not install a hook, inspect
client memory, require build offsets, or create another installed plugin.

Declare the capability in the consuming plugin's manifest:

```toml
[companion.messages]
"game.session.state" = 1
```

Call `bubblefighter::session_state::query(host)` from a worker. The default
timeout is two seconds. The result distinguishes transport failure from a known
non-match phase. `is_in_match()` identifies an ordinary live match;
`allows_match_exit()` additionally requires the server to permit a leave prompt.
Tutorials and training are not ordinary matches. An unknown, malformed, denied,
unbound, canceled or timed-out reply never authorizes presentation.

The wire request has type `game.session.state`, version `1`, kind `request`, and
the exact payload `{}`. The server obtains the player from the authenticated,
bound game session. Callers cannot provide an account or session selector.
The response payload contains:

```json
{"state":"match","in_match":true,"can_leave_match":true,"is_tutorial":false,"mode_id":4}
```

Recognized states are `unknown`, `authenticated`, `main_menu`, `plaza`,
`room_waiting`, `room_loading`, `room_countdown`, `match`, `result`, `tutorial`,
`training`, and `migrating`. `mode_id` is the current client's native mode value,
as a nonnegative integer or null; it is not normalized across client builds. The
native broker wraps this payload inside `response` with transport status
`completed`; a completed transport without a valid snapshot is not success.

The snapshot applies to the instant of the server's query. It must not be cached
as lasting permission. After a confirmation dialog, the server still validates
the current session and command permissions when executing `plaza`. Do not
replace the tutorial's original Escape behavior or replay an old input event
after awaiting this query. Input interception and UI-thread dispatch remain the
consumer's responsibilities, separate from this zero-hook state method.

Local parser and consumer tests cover unavailable transport, unknown phases,
malformed and contradictory fields, tutorials, room waiting, and match exit
permission. Server deployment and a bound cross-node game session must still be
validated before declaring end-to-end acceptance.

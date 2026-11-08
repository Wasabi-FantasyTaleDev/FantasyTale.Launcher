# Optional companion channel

The companion channel is shown to players as **Fantasy Tale Nirinsou Link** (short name Nirinsou); "companion"
remains the name in code, the wire and configuration. It is an optional foundation under development. It is separate from
the original game protocol, the Hook ABI and the Overlay renderer. A game can run
without it; a plugin that consumes it must handle `Unavailable`, `NotBound`,
`Busy`, `Reconnecting` and `OutcomeUnknown` explicitly.

The first transport is a long-lived WebSocket connection using UTF-8 JSON v1. It is `wss`, or plain `ws` only
for a server whose launcher API is itself plain http, so the one-time ticket is never less protected than the
bearer token that obtained it. The server
uses the `fantasytale.companion.v1` subprotocol and the Launcher uses
`ClientWebSocket`. A WebSocket message is one complete envelope; the foundation
does not add another length prefix. gRPC, SignalR and protobuf are deliberately
deferred until measured throughput or compatibility requires a new wire major.

The public SDK exposes a narrow `ICompanionChannel` rather than a socket or
JSON client:

```text
State, Capabilities
SendAsync(type, version, payload)
RequestAsync(type, version, payload, timeout)
Subscribe(type, version, handler) -> IDisposable
```

Every registration is owner- and launch-scoped and is released through the normal
plugin effect lifetime. One communication component owns the connection; plugins
never open independent sockets. Overlay consumers subscribe to a structured
notification DTO and map it to a trusted local game-style dialog or custom panel.
Server messages never carry arbitrary HTML, JavaScript, component source, process
addresses, packets or commands.

The server must issue a separate one-time companion connection ticket during the
launcher launch flow. It is sent only in the WebSocket upgrade Authorization
header, never in a URL, subprotocol, log, page or client file. The ticket is bound
to the launcher authorization session, server, build, capabilities and expiry.
The server marks the launch `PendingGame` until the original game ticket is
redeemed and game login completes; username, IP and PID are not binding signals.
Logout, revocation, expiry and game exit invalidate the binding. Reconnect obtains
a fresh ticket and a state snapshot rather than replaying old socket messages.

The proposed first implementation keeps one receive loop and one send pump, with 64 KiB
messages, JSON depth 16, 128 queued inbound/outbound messages and 1 MiB byte caps,
32 pending requests, a 10-second handshake, 10-second request
default, 20-second heartbeat and 3-second close drain. Reconnect uses bounded
exponential backoff with jitter. Requests with side effects are never retried
automatically; callers receive `OutcomeUnknown` unless they define an idempotent
operation id that the server records.

The server endpoint should be owned by its companion plugin on a separate Kestrel
listener or explicitly isolated route, because the existing short HTTP worker pool
must not be held by long WebSocket connections. A missing catalog capability keeps
the feature unavailable without blocking ordinary login. A future standalone
companion process would be a separate decision if the Launcher must be fully
closed while the game remains connected.

Required acceptance includes reconnect, expired/revoked tickets, game re-login,
multiple launches, stale epochs, duplicate ids, queue/message/UTF-8 limits, slow
readers, missing Overlay, plugin unload and complete secret/payload redaction in
logs.

Unicode acceptance must include mixed Chinese/Korean/Indonesian/English text,
supplementary-plane characters, combining marks and UTF-8 sequences split across
WebSocket fragments. Enforce the byte cap incrementally before appending each
fragment, then strictly decode UTF-8 and parse JSON only after the complete message;
never decode each fragment
with replacement. Text limits and UTF-8 byte limits are separate. The game adapter
may explicitly convert a legacy GBK/CP949 payload, but the companion channel never
uses the system ANSI code page. .NET strings, JSON and JavaScript retain Unicode,
and envelope IDs remain strings to avoid JavaScript integer precision loss. Missing
fonts are an Overlay fallback concern, not an encoding failure.

## Launcher implementation and integration boundary

`FantasyTale.Launcher.Communication` implements the experimental client transport
without a game/WPF dependency. The Host includes it as an optional built-in service
provider but opens no network connection without the account authorization factory.
`CompanionConnection` owns
one epoch, and `CompanionSession` obtains a fresh authorization grant for each attempt,
reconnects with bounded jittered backoff and rebinds plugin subscriptions. Queued sends
and requests belong to the original epoch and are never replayed. Authentication or
protocol rejection stops retries; the future account authorization provider must own
any single controlled access-token refresh. The component never reads stored passwords
or account tokens itself.

Each plugin receives a disposable restricted channel with explicit type/schema grants.
The transport uses one send pump, one receive loop and one ordered callback worker,
so an asynchronous callback may issue a request without blocking response reception.
Callbacks receive cancellation; slow consumers hit the inbound count/byte budget and
close the epoch. Disposal reports `companion.shutdown-pending` if a plugin ignores
cancellation, retaining cleanup ownership until that callback ends and preventing an
overlapping reconnect. Control frames have a separate eight-entry reserved queue.
Receive rate and fragment budgets default to 128 messages/second and 4096 fragments
per message. All message, queue, route, request and timeout budgets are validated.

The **Fantasy Tale Game Control Support** provider exposes native UI contracts to
other plugins through a shared interface. Its game-local adapter is C++: it reports
supported operations, invokes them on the game's UI thread and returns results.
It owns no Overlay fallback policy. The C#
communication component owns notification orchestration and chooses native or
Overlay delivery. An uncertain native result must not automatically show a second
dialog; fallback requires confirmation that native presentation did not occur.
The shared native-control contracts, local service transport, developer test page
and initial kr-812 message-box/text-notification adapter are implemented. Actual
game presentation remains unverified. Communication retains its existing Overlay
receiver and resolves Overlay only when a notification needs presentation.
Native-first Companion routing and a managed plugin-package loader remain pending.

`CompanionOverlayNotifications` is a separate optional consumer using only the public
channel and Overlay interfaces. It handles `ui.notification.show` schema 1 with a
plain-text title, text and one to four `{id,label}` buttons, then sends
`ui.notification.result` events with `notification_id` and `phase`:

- `queued`: registered locally and the mount command is about to be delivered.
- `shown`: the page emitted `game-ui.mounted`.
- `selected`: an allowed button id or cancel was returned by the panel.
- `busy`, `failed`, `unavailable` or `closed`: bounded delivery could not complete.

The consumer defaults to four live dialogs, 32 queued results and a ten-second page
readiness wait. Text limits are 256 UTF-16 code units for titles, 8192 for body text,
64 for button ids and 128 for labels, in addition to the wire UTF-8 byte limit.
Buttons call the trusted game-style dialog API; HTML or scripts are never mounted
from network data. Disconnect closes that epoch's dialogs and cancels its queued
action results. The default omitted button is Simplified Chinese; other locale
defaults remain TODO with the Overlay locale catalog.

**Server binding:** `/v1/launch` returns a `companion` grant (`url`, one-time `ticket`, `launchId`, `expiresAt`)
beside the game ticket, and the game ticket names the same launch. The Host keeps the grant in memory for that
client, uses it for the first connection and asks `POST /v1/companion/tickets` with the `launchId` for every
reconnect; `launch_not_found` ends the session as unavailable. The server binds the connection when the game
login redeems the game ticket and closes it when the game connection ends. A launch whose reply has no grant, or
a malformed one, keeps playing without the channel and records `server-unavailable`.

**Still pending:** agreed shared wire fixtures, server snapshots and catalog capability discovery.
The packages are not published and this is not real-server acceptance. Local
tests use real WebSocket pairs over loopback TCP and an SDK Overlay double; a live
TLS upgrade, server restart and retail Overlay delivery remain separate acceptance.

## Reserved Gateway lifecycle service

The SDK reserves `IGatewayControl.StartAsync` and `StopAsync` for an optional,
launch-scoped service that a future Nirinsou Link consumer can resolve. This is
an interface contract only: no service is registered, no companion control
message is advertised or subscribed, and receiving an unrecognized message
cannot start or close a process.

Initial Center handling stays disabled unless the server catalog explicitly
enables it. Once enabled, the omitted mode defaults to gateway; explicit native
uses the server's own Center. A future authenticated server request may separately
authorize starting Gateway during a bound game session. Presence of Nirinsou
Link alone grants no lifecycle permission.

`GatewayStartRequest` carries an operation id and an opaque authorization id.
The host must resolve that authorization against the current server-issued launch,
companion epoch, build, issuer, expiry, permitted directory and Automatic pool.
The request cannot supply an executable, command line, arbitrary endpoint or
expanded worker membership. The future service must reuse the packaged Gateway
provider and one owned lease, serialize operations and reuse a matching running
instance instead of starting duplicates. Gateway readiness does not prove that
the game changed connections; native route transitions remain a separate contract.

`GatewayStopRequest` carries an operation id and the opaque Gateway lease id
returned by start. Stop uses the owned lease's normal control handshake, cannot
target another launch or an arbitrary PID, and refuses a stale id after lease
replacement. Automatic handoff completion, game exit and explicit close must
share the same cleanup ownership. Repeated operations are idempotent; an uncertain
outcome is reported explicitly and never retried blindly. Epoch closure removes
the consumer and revokes its handles without replay on reconnect.

## Optional service packages

Launcher-only packages use `kind = "service"`. Their `[service]` table selects a
registered built-in provider, never an assembly path or endpoint. Current providers
are `companion` and `overlay-notifications`. Communication owns its optional
notification receiver and declares its exact schema-one route pair in
`[service.messages]`; other application routes remain consumer-owned:

```toml
[plugin]
id = "bubblefighter.fantasy.communication"
version = "0.2.0"
game = "bubblefighter"
kind = "service"
minimum_launcher_version = "0.1.14"
name = { en = "Fantasy Tale Nirinsou Link" }

[service]
provider = "companion"
[service.messages]
"ui.notification.show" = 1
"ui.notification.result" = 1
```

The normal library/import/options system owns these packages, and the shared native
session resolver excludes them. They accept no patches, native libraries or file
replacements. Bundled Communication is enabled by default and remains optional;
Game Control Support is a separate native package with no Companion routes. One
service provider may be selected at most once per client. The supported integer settings are
`heartbeat_interval_ms`, `request_timeout_ms` and `max_dialogs` for their respective
providers; unsupported keys/types/ranges fail before launch.

`LauncherServiceManager` starts after game process creation and owns the services
until game completion or Host shutdown. Missing transport/Overlay dependencies are
observable without auto-enabling them. The account integration must supply a
server-authorized `CompanionSession` factory; it cannot infer identity from username,
PID or the Overlay pipe. The Host factory uses only the server's launch grant. The Host diagnostic method
`instances.services` returns only plugin ids and state codes. Without a grant the manager remains dormant
and creates no socket. An integration test composes the real manager and transport,
delivers a notification, reports mounting and selection, then verifies cleanup on
the simulated game-lifetime task; it starts no Launcher UI or retail client.

Authorization provider failures remain visible as `failed` until the game exits
or the Host shuts down; exception text and response bodies are not exposed through
diagnostics. Teardown attempts each later dependency even if an earlier release
fails, and removes the client entry in all cases. Notification diagnostics follow
channel availability and report `delivery-failed` when bounded result delivery
fails. Reconnect tests verify that old dialogs close and their late button actions
cannot enter a fresh connection, while fresh notifications still render.

## Native consumers (runtime 3.3.3)

An optional native plugin requests only its own message/schema pairs in the same
manifest read by the Launcher and runtime:

```toml
[companion.messages]
"room.mode.extra" = 1
```

Only `kind = "native"` accepts this table. A launch permits at most 64 native owners
and 128 total consumer routes, including managed consumers; incompatible schema
requests fail before game start. This declaration does not enable the communication
foundation or authorize a server connection. Missing dependencies remain optional.

Schema values must be TOML integers; both readers reject boolean/float coercions.
Local owner scopes accept valid package ids, including ids beginning with digits;
wire message types retain their separate lower-case-letter prefix rule.

The sized ABI-one host appends `companion`. Include `fthook/companion.h` and use
`fthook_get_companion(host)` to check the tail safely with older runtimes. A missing
API is a normal optional-feature outcome. On a plugin worker, allocate a 131073-byte
output buffer on the heap and call the returned `invoke` callback:

```cpp
const auto* channel = fthook_get_companion(host);
if (channel) {
    std::string output(131073, '\0');
    const auto result = channel->invoke(channel->context, "subscribe", "room.mode.extra",
        1, nullptr, 0, nullptr, output.data(), static_cast<uint32_t>(output.size()));
    // Handle NOT_FOUND/FAILED without disrupting the ordinary game path.
}
```

Supported methods are `state`, `subscribe`, `unsubscribe`, `send`, `request` and
`poll`. Runtime and Launcher independently enforce installed owner/type/schema
grants. `state` returns JSON state/capabilities; `send` and `request` return JSON
`status`, optional `request_id` and `response`. Accepted means queued, not executed
by the server. Subscription acknowledgements have empty output. `poll` returns one
JSON `{type,version,id,payload}` event, or `FTHOOK_NOT_FOUND` when empty. Unsubscribe
removes that route and its queued events. Callers release subscriptions and stop
workers during their existing cleanup contract.

Each owner has at most 32 events and 128 KiB queued. Overflow produces
`{"error":"companion.events-overflow"}`; events canceled with their original
connection epoch are discarded instead of delivered after reconnect. Pending
native requests use the same connection/request budgets as managed consumers and
are canceled with the game lifetime. A request timeout is zero for the configured
default, or 100..300000 milliseconds. The synchronous local transport has a finite
deadline and is never suitable for a render callback. `FTHOOK_FAILED` after request
delivery may mean an unknown server outcome; never automatically replay it.

The independent `FantasyTale.CompanionBroker.<local launch id>` pipe uses the shared
64-connection process-authenticated server, four-byte little-endian frames and
UTF-8 TOML containing bounded JSON data. This local random id only names IPC; it
is never used as server-issued launch identity. Both sides check peer process ids.
No connection ticket, account token, endpoint or raw socket reaches native code.
No new `session.toml` field or Overlay renderer is required.

The Host composes a router for the game's lifetime and lazily creates restricted
SDK channels after authorization exists. Without a server launch grant,
calls report unavailable and open no network connection. A controlled x86 probe
tests this actual Host manager, named pipe, runtime client and bidirectional
WebSocket path over loopback, independently of any real game or Overlay. The
native DLL activation suite also checks the optional ABI tail and older-table
fallback. `tools/package-native.ps1 -RuntimeOnly` refreshes development runtime
binaries without rewriting existing plugin payloads or published hashes.
Launcher release packaging separately invokes `tools/package-native.ps1 -BundledPluginsOnly` to stage
the bundled game control and diagnostic DLLs. This does not rewrite store packages or their manifests;
the release publisher checks every bundled native library before generating an update index.

## Provisional wire examples

Every frame is a text WebSocket message with snake_case envelope fields. The
following launch/epoch values are nonsecret examples; actual connection tickets
exist only in the HTTP Authorization header.

```json
{"kind":"hello","id":"h1","type":"companion.hello","version":1,"payload":{"protocol":1,"game":"bubblefighter","build":"kr-812","launch_id":"example-launch","locale":"zh-CN","capabilities":{"ui.notification.show":1,"ui.notification.result":1}}}
{"kind":"welcome","id":"w1","type":"companion.welcome","version":1,"epoch":"example-epoch","payload":{"protocol":1,"build":"kr-812","launch_id":"example-launch","state":"pending_game","capabilities":{"ui.notification.show":1,"ui.notification.result":1}}}
{"kind":"state","id":"s1","type":"companion.state","version":1,"epoch":"example-epoch","payload":{"state":"connected"}}
{"kind":"event","id":"n1","type":"ui.notification.show","version":1,"epoch":"example-epoch","payload":{"title":"提示","text":"服务器通知","buttons":[{"id":"ok","label":"确定"}]}}
{"kind":"event","id":"r1","type":"ui.notification.result","version":1,"epoch":"example-epoch","payload":{"notification_id":"n1","phase":"selected","action":"ok"}}
```

Response/error messages echo the request's `type`/`version` and include `reply_to`;
all post-welcome messages carry the negotiated `epoch`. Heartbeats use
`companion.heartbeat` schema 1 and null payloads; `heartbeat_ack` echoes the probe id
in `reply_to`. Unexpected kinds, stale epochs, duplicate envelope fields/ids,
unnegotiated types and malformed data terminate the connection with credential-free
reason codes. The first receiver accepts server events and client request responses;
server-initiated RPC handlers are not part of this provisional SDK.

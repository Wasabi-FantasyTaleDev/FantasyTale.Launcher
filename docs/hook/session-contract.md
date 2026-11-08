# Session contract (`session.toml`, schemas 1 and 2)

The launcher writes `session.toml` into the client folder (next to the host
DLL, for Bubble Fighter `nmcogame.dll`) immediately before each start. The
hook runtime reads it once at load. A file is used instead of environment
variables because elevated (UAC) client starts do not inherit the launcher's
environment.

```toml
schema = 2

[session]
launch_id = "0123456789abcdef0123456789abcdef"
launcher_pid = 1234
startup_timeout_ms = 120000
game = "bubblefighter"
build = "kr-1256"
branch = "kr"                  # used by "branch:" selectors
client_language = "zh-CN"       # optional game language, runtime 3.7.2+
instance = "kr-1256-official"
messenger = "standin"          # standin | forward
log = "C:/Users/me/AppData/Local/FantasyTale/BubbleFighter/logs/kr-1256-official/fthook.log"
log_max_kib = 1024

[session.fingerprint]          # optional; the runtime refuses to patch on mismatch
pe_timestamp = 0x5E1C9A2B
image_size = 0x00F3A000

[login]                        # optional; consumed by the host shim
account = "player1"
ticket = "short-lived-ticket-from-the-server"

[endpoint]                     # optional; consumed by plugins that redirect the server address
host = "203.0.113.10"
port = 7100

[messenger]                    # optional, schema 2; consumed by the messenger stand-in
transport = "http"             # http (the default); gateway is reserved and refused
url = "http://203.0.113.10:8091"
ticket = "v1.signed-messenger-ticket"

[[plugins]]
id = "bubblefighter.fantasy.font-weight"
version = "1.0.0"
path = "C:/Users/me/AppData/Local/FantasyTale/plugins/bubblefighter.fantasy.font-weight/1.0.0"
required = false
[plugins.options]
enabled_weight = 400
```

## Rules

* `session.client_language` carries the selected client language, falling back to
  the build's declared native language when no explicit selection exists. It is
  independent of the launcher interface language and of the optional sign-in
  bridge's language fields. Runtime 3.7.2 uses it for patch `client_languages`
  filters. The launcher omits this key for older runtimes; manual sessions may
  omit it, in which case language-restricted patches are skipped.

* `schema` must be 1 or 2. Unknown keys are errors, so a typo never silently
  disables a hook.
* The launcher writes schema 2 for managed launches. `launch_id` is a fresh
  128-bit random value encoded as 32 lowercase hexadecimal characters.
  `launcher_pid` identifies the named-pipe server. `startup_timeout_ms` is
  bounded to 1000..300000 milliseconds. Schema 1 remains available for manual
  compatibility tests without the launch identity and readiness handshake.
* `build` is the launcher's identification. When `[session.fingerprint]` is
  present the runtime compares each key it contains with the loaded image and
  refuses every plugin on mismatch. `pe_timestamp` is identical in packed and
  unpacked images of one build; `image_size` is not (unpackers add sections),
  so build definitions normally pin only the timestamp.
* Each `[[plugins]]` entry is loaded from `path/plugin.toml`; its `id` and
  `version` must match the entry. A `required` plugin that fails makes the
  runtime log a fault and show one message box; other failures are only logged.
* Options are validated against the plugin's `[[options]]`.
* A session contains at most 64 plugins. Native plugins need FtHook 3.1 or later
  and retain their DLL and API storage through process exit after successful activation.
* `ticket` and `account` are secrets for the session's lifetime. The runtime
  never logs them, and the launcher deletes `session.toml` when the client
  exits.
* Runtime 3.4.0 accepts optional `login.credential_plugin`: exactly one selected
  native plugin explicitly authorized by `LaunchPlan.AuthenticationPlugin`.
  It requires schema 2, a nonempty ticket and an endpoint. The Launcher refuses
  unselected/non-native owners and runtime packages older than 3.4.0 before
  deployment. Omission grants no native authentication API. The runtime supplies
  only this launch's ticket, endpoint and correlation ID through caller-owned
  buffers during plugin entry; account data and the private session object are
  not exposed. The plugin must check the size-versioned API and clear ticket
  buffers after use. Returning from entry revokes access and wipes the runtime's
  temporary credential copy. Plugin manifests cannot authorize themselves.
* Runtime 3.5.0 accepts optional `[messenger]`, which requires schema 2 and both
  `url` and `ticket`. `url` is an absolute http(s) URL of at most 2048
  characters without credentials, query, fragment or whitespace. `ticket` is at
  most 2048 characters of `A-Z a-z 0-9 - _ .`; it is a secret like the login
  ticket, single-use on the server, and separate from it. `transport` says how
  the client reaches `url`: `http` (the default) means the messenger stand-in
  calls the service directly. `gateway`, which will carry a client's original
  messenger protocol through a local translator, is reserved and refused until
  it exists. The launcher writes the section only for builds whose messenger
  mode is `standin`, only when the server's launch reply carries a messenger
  sign-in, and only for runtime packages of 3.5.0 or later; parser errors never
  repeat the ticket.

* Runtime 3.6.0 accepts explicit `login.mode = "name"` in schema 2. It requires a
  nonempty ASCII account of at most 32 characters (`A-Z a-z 0-9 _ - . @`) and
  refuses a ticket in that mode. Omission retains ticket behavior. Only an
  explicitly selected name-only local/room destination enables this mode; a
  missing ticket alone never enables it. The host login bridge returns the plain
  name instead of a ticket passport. An authorized native credential owner may
  receive authentication API revision 2, whose `name_login` field is set and whose
  credential buffer contains that name. Name buffers receive the same lifetime
  and logging protections as tickets. Ordinary ticket grants retain revision 1.

## In-game sign-in (runtime 3.7.0)

For a server preset with `launcher_login: game`, the Launcher opens an anonymous
launch through `POST /v1/launch`, then writes the following optional schema 2
section before starting the client. It does not write a login ticket or enable
plain-name login for this flow.

```toml
[game_login]
api_url = "https://game.example/api/"
launch_id = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
launch_key = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"
launcher_version = "0.1.32"
build = "id-400"
protocol_version = 400
release_version = "2014"        # optional installed release label
patch_version = "1"             # optional installed patch label
launcher_language = "en"       # optional
native_language = "id"         # optional
client_language = "id"         # optional
```

`session.launch_id` remains the Launcher's 32-character local readiness identity.
`game_login.launch_id` and `launch_key` are the server-issued 64-character
lowercase hexadecimal values; they are not interchangeable with that local ID.
The launch key is a secret and receives the same file-lifetime and logging
protections as a login ticket. The section requires schema 2, no `login.ticket`
and no `login.mode = "name"`.

`api_url` uses the discovered server's `secureUrl` when supplied. Otherwise the
per-server Use HTTPS preference upgrades an entered HTTP address, preserving an
explicit nondefault port; the default HTTP port becomes the default HTTPS port.
The preference is off by default. Discovery itself uses the entered address.

The stand-in takes the account and password from the game's own login screen,
sends the existing `POST /v1/auth/login`, then sends `POST /v1/launch` with the
returned access token and this server launch's identity and key. The launch
requests report the same Launcher version, wire build, protocol, installed
release/patch labels and selected languages as a Launcher sign-in. The reply
supplies the game ticket and optional Messenger sign-in. The Launcher obtains
the same launch's Nirinsou Link grant through `POST /v1/launch/companion` after
the game binds it. No account password is written into `session.toml`.

This flow currently needs the Launcher and a runtime package of 3.7.0 or later.
It does not introduce a standalone login configuration or modify `login.ini`.

## Readiness and deployment

The host holds the session file open without write/delete sharing for its process
lifetime. It atomically publishes `fthook-status-<launch_id>.toml` beside it.
The report has its own schema 1 and contains launch identity, game, build,
instance, PID, process creation FILETIME, runtime version and ordered plugin
results. The launcher accepts `ready` only after matching every identity and
every resolved required plugin. Process creation alone does not mean hook
readiness, and hook readiness does not mean successful game-server login.

An incomplete patch journal or low-level byte/cache/protection restoration reports plugin outcome `rollback-failed`
and session state `failed` with error `patch-restoration`. This applies to optional
plugins as well as required ones. The launcher rejects any report that combines
that outcome with a ready state or another error. Later plugins are not loaded.

Native text readers permit delete sharing so status/control inspection cannot
block an atomic report replacement. A sharing/access-denied replacement is retried
for at most 200 ms to tolerate brief external readers. Persistent contention still
refuses authentication; the native log includes the Win32 failure code without
credentials. A failed write does not replace the last complete report.

The launcher validates the runtime package's sizes and SHA-256 digests before
staging at most 16 file bindings and 64 MiB. `.fantasytale-runtime` contains
staged files, original backups and a durable replacement journal. Forward mode
preserves the original messenger through its alias. Cleanup only runs after the
game process has stopped; conflicting external edits keep the recovery files
and produce an error. A residual session blocks another launch. Automatic
orphan-session recovery is not yet exposed in the UI.

See [control-protocol.md](control-protocol.md) for live coordination.

## Without a session

If `session.toml` is missing, the host still works: Bubble Fighter's
`nmcogame.dll` picks `forward` when `nmcogame.nexon.dll` exists and `standin`
otherwise, loads no plugins and logs `session: none`.

# Multiplayer directories

[Chinese](multiplayer.zh.md)

Multiplayer lists persistent servers and temporary rooms through the same directory
contract. Store sources can advertise persistent servers using the configured
AList, HTTP or GitHub transport; live directories also publish leased rooms. Neither
kind of directory implements a tunnel or hosts the game process itself.

## Launcher workflow

Launcher 0.1.38 bundles Gateway for in-game selection among independent real
IPv4 server endpoints advertised by a server directory. Authentication remains
bound to the catalog's credential endpoint while the game connects through its
local Gateway bootstrap. Automatic/manual selector aliases sharing a destination
are rejected until their native selection bridge is available. This release does
not establish six-selector or every-client graphical acceptance.

This page remembers whether the player last selected Rooms or Servers. Reopening
it restores that choice; a URL specifying a tab takes precedence. Rooms remains
browsable while public hosting is under development. The connection-scope menu
shows its unavailable Public room option in gray, with a future-release label;
private and LAN hosting remain available.

The bundled server's independent TOML profile declares its optional `icon_asset`,
relative to web assets. Other servers retain their published icons or the standard
fallback. Favorites show a yellow filled star; their text fades between states
without changing the button width.

The former standalone Servers page is deprecated and no longer maintained. Its saved-server management is now the Servers tab inside Multiplayer. The legacy `/servers` route remains as a bookmark redirect to that tab. Saved servers, directory-discovered servers and temporary rooms use the same bounded workspace; browsing a saved server does not switch the Home account or destination until Choose server is selected.

Open Multiplayer, select a source, and switch between Rooms and Servers. Store
sources supply the same persistent-server discovery surface as live directories.
Browsing, opening details and favoriting never download a server profile or add it
to saved servers. Choosing or explicitly saving a discovered server downloads its
TOML profile, when declared, through that source's transport and imports it locally.
Inline-only listings are converted to the same profile format at that point.

Select a supported client version and choose the destination on Home. Missing
clients and account login follow the normal primary-button workflow. Directory
selection never supplies an account or bypasses the server's login requirement.
Account identity includes the source URL and listing identity so that unrelated
rooms cannot share credentials.

### Saved profile identity and file exchange

New servers accept an optional Server ID. Leaving it empty generates an initial ID
from the game host and port, or the API host and effective port when no game endpoint
is set. Only ASCII English letters, digits, `-` and `_` are allowed; generated IDs
replace other address characters with `_`. For example, `127.0.0.1:8080` becomes
`127_0_0_1_8080`, stored as `servers/127_0_0_1_8080.toml`. Windows device filenames
are rejected. New-server identity collisions are rejected case-insensitively.

The stable ID remains unchanged when the connection address changes. Reading and
updating match the `server.id` inside each file, even if a user renames the file.
The built-in server is `FantasyTale`, stored in `FantasyTale.toml`.

Import TOML and Export TOML are at the bottom of the Server information tab in the
add/edit dialog. Export uses the saved configuration, without account credentials.
Import first matches the stable ID, then the same game's connection destination.
Identical settings are reused without rewriting the file. Different settings need
explicit overwrite confirmation, bound to the inspected saved contents; a concurrent
edit requires inspection again. Canceling leaves the saved profile untouched.

Local profiles remain display fallbacks for server presentation returned by
`/v1/catalog`. JSON is the network contract; the .NET backend reads and writes TOML,
so the browser need not parse TOML. A partial live presentation replaces only fields
that occur in the JSON response. For example, publishing a new online count does
not reset a locally disabled roster, whitelist flag, description or image gallery.
Descriptions and artwork merge by language tag without case-sensitive duplicates.

Explicit values remain authoritative: `rosterEnabled = false` hides the roster,
`onlinePlayers = -1` hides that counter, `playerLimit = null` means unlimited, and
`images = []` clears the plain screenshot gallery. Null counters mean unknown.
Missing fields retain the local fallback; invalid remote values reject the update
without replacing the last validated catalog. This overlay uses the existing
catalog request and does not rewrite the saved TOML automatically or change the
connection address. The Server information tab exposes Update from server for an
explicitly saved profile. That action atomically persists the latest validated
name, presentation, supported builds and plugin declarations while retaining the
stable ID and connection address. Server-directed endpoint migration still needs
a separate confirmation workflow.

Joined rooms remain transient. The backend refreshes membership while the page is
closed and rechecks it before account requests and launch. Missing rooms or changed
connection details invalidate the old account session. A previously confirmed room
is hidden when its lease expires if the source is unreachable. Approved persistent
servers are added to the player's normal server collection.

Listing joined rooms and joining another room discard expired memberships and
their account scopes immediately. Resolving an
expired membership fails before directory access; it requires a fresh join.
This cleanup does not wait for a stalled passive membership request.

Passive refresh reads each source once per round for all its joined rooms, with
up to four sources in flight. A slow source does not block the other active
sources. Invalid replies affect that source's round; later refreshes continue.
The client does not cap joined-room count, retained publication statuses, known
source count, listing rows or response bytes. Directory JSON is parsed from the
response stream, with schema, identity, duplicate and room-lease validation retained.
An independently hosted directory may still apply its own deployment policies.

Rejoining a room creates a new connection generation even when its address and
identity are unchanged. Directory replies from a previous join cannot remove the
new connection or invalidate its account sessions.

Submit server sends a name, API address, introduction and supported builds into the
selected directory's review queue. It does not immediately publish a server.

To host, configure room mode on Local hosting and start the service. Publish room
becomes available only after the owned process reports readiness. Choose a source
and an optional introduction. Heartbeats run in the backend independently of page
navigation. Stopping the server withdraws the listing; unexpected exit is detected
as well. Unpublish room leaves the local server running. Editing an in-use source
requires withdrawing its publication first.

The title, source controls and join actions stay visible; list and description
regions scroll independently. Existing dialogs, flyouts and transitions are reused
in both themes, with seven UI languages.

## Hosting the directory API

The separate `FantasyTale.Launcher.Directory` executable runs without the launcher,
any game, a store or a game server. Publish it for the deployment platform:

```sh
dotnet publish src/FantasyTale.Launcher.Directory -c Release -r linux-x64 --self-contained false -o directory-publish
```

Install the .NET 10 ASP.NET runtime. Configuration is read from environment
variables prefixed `FT_DIRECTORY_` or matching command-line options:

| Setting | Default | Purpose |
| --- | --- | --- |
| `Listen` | `http://127.0.0.1:8096` | Listener root; an operator may place a reverse proxy in front |
| `DataPath` | `directory-data` | Reviewed servers and pending submissions |
| `Name` | `Fantasy Tale` | Source display name |
| `AdminToken` | required | Private bearer token for source-owner review; 16–256 characters |
| `MaximumEntries` | 512 | Separate limits for servers, pending submissions and rooms, maximum 1024 |
| `RoomLeaseSeconds` | 90 | Heartbeat lease, configurable from 30 to 300 seconds |
| `HostPolicy` | `auto` | `ip`, `domain` or `auto`, following launcher network policy |
| `Region` | unset | Optional two-letter deployment region |
| `ReadRequestsPerMinute` | 1200 | Global read budget |
| `WriteRequestsPerMinute` | 120 | New publications, submissions and review budget |
| `LeaseRequestsPerMinute` | 8192 | Separate renewal/withdrawal budget |

For example, supply `FT_DIRECTORY_AdminToken` through deployment configuration,
then run `dotnet FantasyTale.Launcher.Directory.dll`. The token is an ordinary
private administration credential, not a release-signing or public-key workflow.
The service never contacts a submitted address. The launcher verifies the actual
target only when the player chooses it.

The API root exposes:

| Method and path | Behavior |
| --- | --- |
| `GET v1/info` | Schema, source name, room lifetime and network policy |
| `GET v1/entries` | Approved servers and unexpired rooms; no private management tokens |
| `POST v1/submissions` | Queue a persistent server for review |
| `POST v1/rooms` | Publish a temporary room and return its private lease |
| `POST v1/rooms/{id}/heartbeat` | Renew the exact room using its bearer token |
| `DELETE v1/rooms/{id}` | Withdraw the exact room using its bearer token |
| `GET v1/admin/submissions` | Read the pending queue with the administrator bearer token |
| `POST v1/admin/submissions/{id}/approve` | Approve a pending revision |
| `POST v1/admin/submissions/{id}/reject` | Reject a pending revision |
| `DELETE v1/admin/servers/{id}` | Remove a reviewed server |

An advertisement contains `game`, `name`, `apiUrl`, `builds` and `description`.
An entry adds `id`, `kind` (`server` or `room`) and `expiresAt`. The schema is
game-independent and custom directory implementations can implement the same API.
Administration endpoints require `Authorization: Bearer <administrator token>`.
Room tokens grant only renewal and withdrawal of one exact room, never approval.

## Container deployment

Publish `src/FantasyTale.Launcher.Directory` for `linux-x64` without bundling the
runtime into `deploy/directory/publish`, then copy that directory with the
Dockerfile and Compose configuration to the host. Copy `.env.example` to `.env`
and set the private administrator token before running `docker compose up -d --build`.
The default port is 8096. `FT_DIRECTORY_REGION` may be empty when unspecified;
an explicit region uses two uppercase letters. Configure `FT_DIRECTORY_HOST_POLICY`
as `auto`, `ip` or `domain` for the source's discovery response.

The container runs as an unprivileged user with a read-only root filesystem,
a persistent data volume, bounded logs and a 256 MiB memory limit. Preserve the
volume across updates. Keep `.env` outside version control and do not publish it
with client definitions. These files deploy only the directory, independently of
any running game service.

## Lifetime and bounds

Reviewed servers and submissions use an exclusive single-writer file with atomic
replacement and disk flush before confirmation. A failed write does not mutate
the visible state. Corrupt state fails startup instead of resetting the directory.
Room leases are deliberately process-local: a directory restart removes them,
after which the host may publish again. Expired room identities cannot be renewed.
Republishing creates a fresh identity; stale commands cannot withdraw the new room.

Each publication owns one in-flight backend operation. At most four background
operations use the directory transport concurrently; a stalled room request does
not block later renewals or process-exit withdrawal for other rooms. Explicit
withdrawal shares that room's gate with renewal, and shutdown cancels and drains
in-flight operations before the final withdrawal attempts.

Default heartbeats occur every 30 seconds. Network failures are visible and retried
until the confirmed lease expires. A failed withdrawal stops renewal, retries in
the background, and eventually disappears by lease expiry. If a publication succeeds
but its HTTP response is lost, the unconfirmed listing also expires without renewal.
The launcher retains at most 32 publication records and 32 joined rooms; it can
own at most two local game servers at once. Secrets remain backend-only in memory.

When the launcher exits, room withdrawal attempts share one ten-second deadline
with at most four concurrent requests. An unreachable directory therefore does
not add another full timeout for every retained publication before local server
cleanup can proceed. Unconfirmed withdrawals keep their existing lease expiry;
the launcher never reports them as successfully withdrawn.

Requests are capped at 16 KiB, advertisements at 8 KiB, stored state at 8 MiB and
connections at 64. Metadata is validated on both sides. HTTP redirects are rejected,
and credential-free discovery obtains any explicit IP/domain policy before writes.

## Server plugin policy

The catalog and account responses carry a `clientPolicy`. Its `sources` are store
catalogs the server offers for the player to add, and its `plugins` give each
plugin identity a list of typed rules. Every rule has an `id`, a `type` and a
`builds` list; an empty list applies to every build.

| Type | Extra field | Effect for launches to this server |
| --- | --- | --- |
| `require` | none | The plugin must be installed and enabled; the Launcher offers it. |
| `disable` | none | The plugin is turned off for this launch only; the saved selection is unchanged. |
| `recommend` | `sources` | Offered once per client and rule, installed from the named sources (empty: any added store). |
| `min_version` | `version` | An enabled plugin below this version is updated before launch. |
| `max_version` | `version` | An enabled plugin above this version is moved to the highest allowed version. |
| `latest` | none | An enabled plugin is updated to its newest version before launch. |

Rules of one plugin that share a build must not conflict: two rules of the same
type, `disable` with any other rule, `latest` with `max_version`, or a minimum above
the maximum. Metadata readers reject a conflicting, unknown or malformed policy
before a client is selected, and refuse a policy with a section or field this
Launcher does not know. A plugin the build itself requires cannot be disabled; the
launch is refused instead.

Installs and version changes use only stores the player has added. A server's
sources appear on its page with an Add button; a server never adds a store itself.
Saved and store-delivered profiles carry the policy as flat TOML tables:

```toml
[[client_policy_sources]]
name = "community"
kind = "http"
base_url = "https://plugins.example/"
catalog_path = "source.toml"

[[client_policy_rules]]
plugin = "bubblefighter.fantasy.cn-auth"
id = "need"
type = "require"
builds = ["cn-404"]
```

## Verification and remaining work

Tests cover approval visibility and persistence, immutable metadata, bounded state,
failed writes, exclusive storage, private room tokens, expiry, wrong/stale owners,
real HTTP client/service interaction, source-scoped destinations, malformed input,
redirect refusal, backend heartbeat and process-exit withdrawal. The complete local
CN2013 server package also verifies discovery, joining, actual account login and
account invalidation after withdrawal without starting a launcher window or game.
Headless light/dark checks cover page bounds, source and submission dialogs, join,
publication and withdrawal controls.

The bundled Bubble Fighter Fantasy directory is deployed at
`http://bf.600819.xyz:8096/`. The shared client has verified credential-free
discovery with IP fallback and subsequent use of the source's explicit IP policy;
the definition keeps the domain so later DNS migrations do not require a code edit.
Older empty bundled overrides inherit this endpoint while preserving their enabled
setting, custom label and any explicit replacement URL. The public directory starts
empty. Deployment checks cover external discovery/listing, administrator rejection
without credentials and successful restart; they do not publish a test room.

Real remote-player acceptance and a graphical source-owner review surface remain
open. Local durable storage and automatic server
package installation are separate core work. Automatic firewall/NAT/VPN setup is
outside the current direct-address implementation; directory publication alone
does not prove that a friend's machine can reach the advertised address.

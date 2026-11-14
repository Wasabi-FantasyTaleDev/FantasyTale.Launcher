# Architecture

Overlay packages may declare a game-module controller. The host starts that
controller and subscribes its panel proxy before dispatching the entry HTML,
then awaits its cleanup before closing the session transport. Native observation,
mode policy and confirmed room mutations run in the managed controller, even
while WebView is suspended. The page receives versioned display snapshots and
submits session-bound user actions; controlled panels have no raw native bridge.

Controlled packages receive a stable, owner-scoped Companion channel before
their controller starts. Connection snapshots revoke authorization locally on
disconnect. Mode Extension subscribes before obtaining its initial context and
scene snapshots, and observes native controls only while the active room-create
context is in `plaza`. Main-menu and in-room states remain dormant. Reconnects
resynchronize; there is no periodic server query or time limit in the plaza.
Native reads keep their operation identity while queued, without a controller
deadline canceling the shared Hook channel. Accepted mutations retain bounded
outcome checks and are never replayed after uncertainty.

Hook admission contention is distinct from an uncertain submitted operation.
An unsent request can be retried within a deadline; a queued operation permits
only result queries. Mode observation also waits through transient native UI
readiness changes. Controller stage/status/reason records have a separate bounded
log so routine frontend polling cannot evict startup failure evidence.

The Bubble Fighter Gateway can check native auxiliary readiness before freezing its directory.
The shared resolver retains catalog capacities separately from confirmed unavailability; the game
module supplies a bounded native probe configuration. A TCP connection alone does not establish that
the selected build's Center status handler is active. Failed protocol probes use the existing local
unavailable listener, preserving visible identities and native uniqueness retirement. Later TCP-only
Automatic refreshes cannot reactivate those endpoints during the same launch. Other modules and
explicit directories without the capacity contract retain their existing behavior.

Automatic admission and native cleanup have different lifetimes. Ticket redemption
or grant expiry withdraws candidates through a correlated refresh without STOP.
The bootstrap lease retains its control pipe and accepted connections until native
retirement or game disposal; only explicit disposal cancels that lease.

File and memory plugin conditions use the selected game language. File variants
are filtered by build and include/exclude language tags before hashing and target
collision checks. Both parsers reject duplicate or contradictory tags. Native
patch exclusions use the same case-insensitive exact-tag semantics.

Diagnostic retention is advisory during launch preparation. Only recognized launcher-owned runs are eligible; a directory is moved to a pruning name before its files are removed. A blocked run is retained where possible and revisited on later launches. Cleanup failures record their path, exception category and OS code without preventing creation of the new diagnostic run. Root path validation and creation of the current run remain mandatory.

Plugin component recovery first checks a read-only snapshot. If the selection is already current, it returns without taking the client or name-registry lease. Scheme application also validates an unchanged selection without saving it. Empty library cleanup returns before acquiring its lease; reader disposal takes it only when it must discard a pending import. Cleanup rereads its candidates under the lease before removing them. A real edit still acquires the same exclusive cross-process lease and revalidates before commit. Inventory refresh can postpone a busy scheme write, retaining its selection for retry; launch continues to await the scheme. Occupancy replies carry local lease context for the details dialog, with no credentials or speculative process identity.

The store's explicit install-and-enable choice targets all compatible builds,
including builds without a registered client and missing or disabled selections.
The frontend persists the choice in each build's selected scheme, so restoring
that scheme cannot erase host activation. Unregistered builds retain an unresolved
selection until the shared package is available. Pending choices carry their task
and previous selection; cancellation restores unchanged choices and later edits
supersede them. Running clients defer until exit.
Automatic updates preserve enablement. Batch confirmations retain selection stamps
so edits made while the dialog is open are not overwritten by an older decision.

Plugin downloads finish when their verified package reaches the shared library.
Client selection changes are independent, durable intents: a missing requirement
on another plugin cannot block saving a verified installation. Launch preflight
still checks the complete selection. Pending notices and activation failures belong
to the selected client, and game-exit deferral requires an actual running session.

The external overlay accepts WebView messages only from its exact startup document,
including the selected theme/resource query. Hash routes and local subresources remain
available; other top-level documents and popup windows are blocked before navigation.
Event metadata is checked before readiness or input state changes. This gate does not
authenticate the main Photino window, whose native source/navigation API remains under review.

The overlay clears the inherited WebView2 user-data-folder override before creating
its browser. The launcher's Photino profile can have incompatible browser options;
the overlay uses its own folder and shares that environment within its process.
Initialization failures record the stage and numeric HRESULT without exception text.

Packages may share data. A `kind = "assets"` package is data only and never reaches the native session, and any
package may declare `requires`. `PluginRequirements` (Core) resolves them for one client: launch checks every
requirement of every enabled package before touching client files, and the store install path queues missing
requirements from the same catalog. For overlay launches the Host sends the id-to-folder map of the enabled overlay
packages and the assets packages they require to the overlay process (`package-roots`), which answers
`https://packages.fantasytale.local/<id>/<path>` itself from `OverlayPackageFiles` (Sdk): read-only, links and
traversal refused, files streamed. See [shared data packages](plugin-management.md#shared-data-packages-and-requirements)
and [package files](hook/overlay.md#package-files).

Launch preparation owns a deadline, including the remaining automatic admission lifetime.
Windows shell confirmation itself cannot be canceled by that token. If cancellation or
binding fails after Windows creates a process, the normal monitor takes ownership before
startup reports failure. A denied termination keeps the running slot, client lease and
session recovery barrier until the original process handle confirms exit; runtime files
are not restored beneath a live child. The independent system confirmation and real-client
elevation paths still require interactive acceptance.

Managed game modules share the host SDK and available framework assemblies, even when a self-contained distribution places framework DLLs beside the module. This preserves exception and serialization identity across host callbacks. Additional managed and native dependencies remain package local; an optional framework-named dependency absent from the host can still resolve from its module package.

Resource edit providers bind logical targets to physical member addresses before conflict resolution. Original-value
conditions are checked as one read-only pass before writes. Whole-member and field-level decisions share an input-bound
snapshot; binding, member and archive caches retain the original resource as authority. Game-owned managed capabilities
provide target lookup, manifest builders and native control inspection; the host transports structured conflict reports
and explicit retry decisions.

Archive-member plans may gate on an existing boolean option through `when`, or
match a declared string/enum option through `when` plus `when_value`. Exact string
matching preserves independent choices and allows additional styles without new
provider code. Invalid option types, enum values and string bounds fail before
archive composition; a nonmatching choice contributes no writes.

`when_options` adds an AND of declared boolean, string or enum equality conditions,
and combines with any existing `when` condition. Every condition and selected value
is validated even when another condition does not match. Exact combinations allow
one shared archive member to represent several independent settings without
overlapping writes; scene-specific behavior remains in the game package's data.

Transient InfoBar notifications expire after six seconds by default for info, success and warning; error notifications remain until dismissed. Callers can explicitly request persistence or another duration. Hover and keyboard focus pause expiry, and replacing an operation gives its notification a fresh lifetime. State-dependent inline banners remain visible while their underlying state applies.

Plugin schemes store independent selections per client build. The selected scheme is remembered per server and build; applying it preserves installed-client inventory and validates resource settings without preparing game files. Shared TOML is parsed by the managed backend. Uninstalled selections remain in the scheme until their exact package can be applied, independently of the import dialog lifetime.

Private local hosting starts from the Home launch action. Saving settings never starts a server and running changes take effect only after restart. Owned private processes stop on game exit, destination changes and launcher shutdown. The hosting terminal displays bounded logs and accepts only the declared server lifecycle commands.

Launch grants use issuer-relative durations projected onto the receiving machine's clock and consumed through monotonic elapsed time. The issuer owns heartbeat readiness. Source observation timestamps and repeated source revisions do not exclude an online worker whose player-side TCP probe succeeds. Admission expiry, explicit offline or draining state, capacity and failed probes remain authoritative.

Native directories retain every configured manual destination, including failed DNS and TCP probes. An unavailable destination retains its original identity and capacity and receives a local status-only listener; it cannot redirect or become an active Automatic candidate during that launch. A new launch probes it again. Directory membership has no fixed entry-count cap; the native dialect's encoded frame and actual control-message budget determine representable size.

Before freezing a native endpoint, transient DNS and connection failures receive
up to three attempts within one shared per-entry deadline. Each connection retains
its bounded probe timeout. A successful retry retains the real endpoint; only
exhausted failures become status-only entries. Caller cancellation propagates.
Diagnostics contain the selector identity, attempt number and fixed result category,
without endpoints or untrusted transport details. Published native bindings remain
immutable for that launch.

The shell and store share a session-scoped catalog and complete plugin inventory. Background update discovery counts each game/plugin identity once across enabled sources. Search and source filters affect store results only. Single-item transfer views keep progress outside the information card; multiple-item views associate progress with each card.

[Chinese](architecture.zh.md)

Audited game modules may expose `IAutomaticServerSelectionCapability`. When the
server requests Gateway mode and enables Automatic, an authenticated launch
requests issuer-scoped server admission before
bootstrap or resource preparation. Shared code freezes the canonical manual
selectors, resolved physical endpoints, authority and automatic pool. Health
refreshes reuse the original account, ticket and grant; they cannot replace an
endpoint or extend admission. Online readiness and a bounded successful TCP probe
permit an admitted worker to participate without comparing heartbeat timestamps.
An offline worker remains admitted but cannot participate until the issuer reports it
online and the player-side probe succeeds. A full empty snapshot withdraws automatic
candidates without changing manual membership.

Center is disabled by default. The `/v1/catalog` response must explicitly supply
`center: { "enabled": true, "mode": "gateway" }` to activate local Gateway.
An enabled Center defaults to gateway when `mode` is omitted; explicit `native`
selects the server's own Center.
Omitting `center`, omitting `enabled`, or setting it to false keeps ordinary launch
behavior and does not request `/v1/servers/<build>`. Enabled native mode retains
the original Center endpoint and lets that server implement its own chooser and
routing, without starting local Gateway or requesting automatic admission.
Only enabled gateway mode reads the directory and prepares local protocol support;
the directory must repeat the same activation. An explicitly imported API-less
server profile also requires `enabled = true` in its directory. Online catalog
policy takes precedence over saved profile metadata. There is no per-build
deployment switch.

In gateway mode, the server enables Automatic by supplying an `automatic: true`
selector; that selector's `targets` defines its permitted pool. The authenticated
launch grant fixes the actual candidate membership, issuer, scope and endpoints.
Manual-only nodes never enter that pool, and health refreshes cannot expand it.
Without the Automatic row, the chooser contains manual destinations only. A
bootstrap-only module cannot preselect an Automatic alias instead of obtaining
admission. The embedded Gateway capability descriptor records implementation
support only, and never enables a server feature or widens its authorization.
Update that descriptor alongside reviewed Gateway artifacts and target-server
checks; metadata alone does not establish actual client acceptance.

Gateway Center login uses the game's original server chooser and native
redirect protocol. Automatic decisions, health measurements and endpoint routing
belong to the launcher, Gateway and server, without selector rendering, click or
automatic-selection Hooks. Each build needs its own audited packet consumers
before its capability is enabled. Chinese authentication repairs remain in the Chinese authentication component.

Native authentication components receive the prepared game endpoint in their
session, including a bootstrap Center endpoint when present. The issuer and its
ticket remain the original authentication authority; a connection redirect must
not cause a component to restore the original physical game destination.

The bootstrap lease owns one refresh loop and one control-pipe reader after READY,
correlates increasing parent revisions, serializes refresh and STOP writes, and
keeps an absolute grant deadline across resource preparation and native redirect.
A committed selection stops refreshes while Gateway completes its own handshake.
Gateway retires its bootstrap connections only after the native redirect and
source-release handshake complete; the selected game connection continues
independently. Necessary authentication and startup adaptations remain separate
from Center selection capabilities.

Native BootstrapComplete reports begin a separate successful-handoff observation;
they never dispose the lease or cancel its refresh task. Gateway retains its pipe
and finishes bounded auxiliary retirement before exiting naturally. Game exit
cancels that observation and disposes the lease through the normal STOP path.
Providers without handoff observation retain their lease until game exit.

Directory membership remains distinct from current worker availability. Native
automatic admission preserves the original manual mappings and supplies a real
local automatic Center; ordinary directories use distinct physical destinations.
The former CN2012 selector presentation adapter is removed. The generic
`LaunchBootstrapMetadata` contract remains available to other module capabilities;
shared launch code writes it verbatim to `[module_data]`, and the game module
validates its meaning. See the
[native metadata contract](hook/native-abi.md#launch-module-metadata).

FantasyTale.Launcher is a multi-game launcher for community servers of legacy
online games. It installs and updates game clients, keeps any number of
independent client copies side by side, connects them to the servers the user
chooses, and loads client-side hook plugins into the game process. Bubble
Fighter is the first and, for now, the only enabled game.

Launch validation remains read-only. After validation and while holding the client
lease, the shared launcher calls the module's optional `PrepareLaunch` operation
before deploying runtime files or starting the process. Game-specific persistent
settings belong there. Bubble Fighter synchronizes an existing `login.ini` with
the selected endpoint; unchanged settings are not rewritten.

The guiding rule is the one the companion server project follows: **everything
that varies is data or a plugin, never a code change.** Games, client builds,
servers, download sources, plugin stores and hook plugins are all described by
TOML files and discovered at run time.

The frontend retains a display-only snapshot of game definitions, client names,
installed flags and languages, plus the last client/build selection for each game. It restores
that snapshot before background inspection. Cached versions can be selected and
installed clients can be launched immediately, while host discovery continues
independently. Loading cannot masquerade as a
missing client or a fictitious default version. Without a snapshot, Home shows a
loading state until the saved registration list succeeds. A failed list retains
the snapshot; a successful list replaces it, including a genuinely empty list.
Running-process inspection updates independently. Actual launching still resolves the registered client in the backend; cache
fields never rewrite its settings. Changing the client language and editing/cloning
registrations wait for the first successful inventory response. Installation paths, plugin options and account/session
credentials are excluded from the snapshot; launch validation remains authoritative.

## 1. Layers

```text
+--------------------------------------------------------------+
| web/            Vue 3 frontend (Fluent / WinUI look)          |
|                 talks only to the bridge                      |
+------------------------------^-------------------------------+
                               | JSON messages (bridge contract)
+------------------------------v-------------------------------+
| FantasyTale.Launcher.Host    Photino window, bridge, Mica     |
+--------------------------------------------------------------+
| FantasyTale.Launcher.Core    instances, servers, plugin       |
|                              manager, settings, i18n catalog  |
| FantasyTale.Launcher.Downloads  aria2 / HTTP engines,         |
|                              AList and HTTP sources, manifests|
+--------------------------------------------------------------+
| FantasyTale.Launcher.Sdk     public contracts for game        |
|                              modules, sources, stores         |
+------------------^-------------------------------^-----------+
                   |                               |
   games/BubbleFighter (open)        games/TalesRunner, games/AirRider
   Games.BubbleFighter module        (private repositories, loaded
   + native nmcogame host            at run time from the games folder)
                   |
                   v  session.toml in the instance folder
+--------------------------------------------------------------+
| native/runtime   FtHook: x86 hook runtime inside the game     |
|                  process; loads hook plugins from data        |
+--------------------------------------------------------------+
```

* `web/` is a separate Vite project. Its output is copied into the host's
  `wwwroot`. On Windows, `https://fantasytale.invalid/` is intercepted locally;
  other platforms retain `app://localhost`. Only that exact origin can read
  allowlisted static/media files under the configured roots. Missing local
  paths never fall through to the network. The frontend uses the bridge for
  filesystem and service operations. The current host buffers media responses
  and does not yet support video range streaming.
* `Host` owns the window and the bridge. It contains no game logic.
* Startup uses only a centered static logo before frontend initialization, without
  text or a progress indicator. It crossfades after the workspace paints,
  without a minimum display duration. The title bar is mounted outside the fixed
  workspace's stacking context so window controls remain usable over the startup
  surface. Closing before the startup transition completes exits the application;
  later manual caption closes show a dialog for application exit,
  continuing in the tray or canceling. Cancel and Escape leave the window open. Native close requests retain the tray behavior. The host receives an explicit ready message
  after the transition ends. Compact transparent PNG and multi-resolution ICO assets supply the
  logo, executable, window and DPI-scaled tray icon. The first frontend paint
  keeps a pure-white splash background while its logo enters.
  The logo entrance completes before the splash fades out; only after it clears
  does a Web Animations entrance reveal the workspace, avoiding occluded motion. Dialog exit
  choices wait for the shared dialog leave animation before hiding or exiting the
  application; reduced-motion mode retains a brief fade without spatial motion.
* `Core` and `Downloads` are game-agnostic.
* `Sdk` is the only assembly a game module references. It is versioned with
  SemVer and is the stable surface for closed-source modules.

## 2. Open and closed parts

The launcher and everything Bubble Fighter related are open source (MIT).
TalesRunner and AirRider support is developed in **separate private
repositories** that reference only the public `FantasyTale.Launcher.Sdk`
package and the public native hook ABI:

* The open repository contains a display definition `games/<Game>/game.toml`
  marked `status = "placeholder"`, plus rank tables and icons consumed by the shared
  account display. A `home_order` value makes it browsable in the
  home carousel without enabling a game module. `preparing` controls the page's
  preparation notice independently: Air Rider has the normal library page,
  while Tales Runner shows Preparing. Launching still requires an enabled module
  and a supported client build.
* A private module is built into a *game module package* (a folder with a
  `game.toml`, its managed assembly and, if needed, native hook plugins).
  Dropping the package into the user's `games/` folder is enough; the open
  launcher has no compile-time dependency on it.
* Their game-specific patch SDK (TalesRunner) and hooks (AirRider) live in
  those private repositories and use the same runtime ABI as Bubble Fighter.

Managed modules resolve dependencies in a separate load context for each installed
assembly path. Ship the module's private DLLs beside it, with its `.deps.json` and
runtime-specific dependencies when applicable. The resolver uses that manifest
and also supports adjacent managed DLLs in a manually assembled package. Each
context shares the host's `FantasyTale.Launcher.Sdk` assembly so launch plans and
optional SDK interfaces retain their type identity, even if a package includes an
SDK copy. Framework dependencies retain normal .NET resolution. Module contexts
are reused for the process lifetime; restart the Launcher after replacing an
installed module or dependency. This provides dependency isolation, not a security
sandbox. An installed game-local module takes precedence; an absent one falls back
to the application directory. Invalid or incomplete game-local packages fail rather
than selecting the bundled module.

Native dependencies called by a managed module run inside the Launcher and must
match its process architecture. They are separate from hook DLLs loaded into the
game, which must match the game's architecture. Windows package tests exercise
assembly-scoped native loading from two directories containing the same library
name, with and without a component manifest, and reject a missing native library.
These synthetic module checks do not establish acceptance of either private game.

## 3. Definitions instead of code

| File | Describes |
| --- | --- |
| `games/<game>/game.toml` | Game id, localized names, artwork, module assembly, hook host kind |
| `games/<game>/builds/<build>.toml` | One client build: id, branch, region, executable names, fingerprint, messenger mode, default plugins |
| `games/<game>/plugins/<id>/plugin.toml` | One hook plugin (see [hook/plugin-format.md](hook/plugin-format.md)) |
| `games/<game>/servers/*.toml` | Independent bundled server profiles, including optional local branding |
| user `servers/<id>.toml` | Explicitly saved server profiles and local overrides, identified by `server.id` |
| user `sources.toml` | Download sources and plugin store sources |
| `<instance>/instance.toml` | One installed client copy: build, language, enabled plugins, options |
| `<instance>/session.toml` | Written by the launcher at each start, read by the hook runtime ([hook/session-contract.md](hook/session-contract.md)) |

A new client build, a new server or a new hook is added by writing one of these
files. Code changes are needed only for new *kinds* of behaviour.

Build definitions have reversible availability switches under `[build]`:
`enabled` defaults to `true` and `hidden` defaults to `false`. Disabled or hidden
builds are omitted from version selectors, server presets and client downloads.
`enabled = false` additionally rejects client creation, installation, game launch
and local hosting before operational work begins. Identification fingerprints,
plugin compatibility definitions and existing client files/settings are retained.
`hidden = true` alone changes discovery, not the launch contract. After adaptation,
set `enabled = true` and `hidden = false`, then reload the launcher. The currently
declared CN2016 build `cn-735` is disabled/hidden pending adaptation; the TW2019
definition is retained even though its executable signature dates from 2018.

Home presentation also comes from `game.toml`:

`game.navigation_entries` declares the supported menu route identifiers. Known
identifiers are `home`, `instances`, `multiplayer`, `hosting`, `store`, `downloads`,
`plugins`, `accounts` and `settings`. Values must be unique and include `home`;
unknown identifiers fail configuration loading. Older definitions without this
field expose only `home`, `downloads` and `accounts`. The bridge carries the
validated list to navigation and route checks; browser previews read the same
TOML declarations. Changing games or entering a saved unsupported route returns
to Home once the catalog is loaded. Home shortcuts consume the same declaration.
Server plugin restrictions disable applicable actions rather than hiding a declared
plugin library menu.

* `game.home_order` is an optional integer from 0 to 10000. Enabled games and
  definitions with this value appear in ascending order, with game id as the
  tie-breaker. Previous/next game arrows disappear at the ends of the list.
* `game.unsupported_regions` is an optional array of distinct launcher interface
  locale tags, for example `["zh-CN"]`. It hides the game from the home selector
  only while that exact locale is selected, ignoring case. Missing or empty means
  visible in every locale. Changing the interface language immediately restores
  eligible entries; installed clients and saved data remain available. If the
  selected game becomes hidden, the next idle state selects the first visible
  game. This metadata does not depend on the game's selected language or IP
  location and is not an access-control boundary. No bundled profile enables it.
* `game.preparing` defaults to true for placeholder modules and false for enabled
  modules. `game.service_notice` is optional localized text displayed below the
  cover. The English game name is the small heading; the main heading uses the
  UI language, falling back to the Korean original when it equals the English name.
* Bundled destinations live in independent `games/<game>/servers/*.toml` files,
  not in `game.toml`. The Bubble Fighter profile is `FantasyTale.toml` with
  `server.id = "FantasyTale"`. Local overrides use that same identity. A bundled
  profile can declare `icon_asset` relative to web assets; imported profiles cannot
  grant themselves access to bundled-only artwork.
* `game.media.background_videos` is an ordered array of up to 16 local MP4/WebM
  paths relative to the game folder. An empty list uses the colour backdrop.
  The legacy `background_video` key accepts one path; it cannot be combined with
  the array. Paths cannot escape the game folder, and large media stays out of Git.
* Each cover starts muted and exposes a sound toggle. Multiple clips add a next
  video button and position indicator; clips advance automatically and wrap.
  Switching clips preserves the sound choice; switching games resets it. Video
  fades in after its first frame. Leaving the page stops playback, hidden pages
  pause, and failed clips are skipped with a visible fallback if none can play.

## 4. Instances

An **instance** is one installed client copy with its own folder,
language, plugin set and options, like an isolated game directory in a
Minecraft launcher. Any number of instances may exist, including several of
the same build, because servers can require different files or hooks.

The Add client dialog selects an installation destination, not an import source.
An optional absolute `instance.installation_path` points to that directory; when
omitted, the managed `client` subdirectory is used. A detected existing client is
registered without an overwrite prompt; repeated registration of the same build
and directory returns the existing record. For other nonempty destinations,
preflight asks for overwrite consent. Registration preserves existing files;
package replacement is performed by the verified manifest installer. Overlapping client directories and metadata directories are
rejected. Client removal explicitly chooses verified game-file cleanup or
registration-only removal. Neither action recursively deletes a client tree.
Registration-only removal retires the declaration in place; the exact managed
client child can later reactivate that identity. Cleanup requires an installation
receipt and a validated file-check inventory, rejects root/protected/overlapping
locations, links, running processes and unfinished work, and verifies each file's
size and SHA-256 through the same exclusive Windows handle used for deletion.
Modified and unlisted files remain, including personal files inside a game folder.
An imported client without a reliable inventory can only remove its registration.
Cloning copies files into a new independent managed directory.
The language controls in this dialog are retained as commented markup; language
selection remains on Home.

The display name is optional when adding a client: the frontend supplies the
selected build's localized name when the field is empty. Creation reserves the
first available display name under a registry lease, using `Name (1)`, `Name (2)`
and so on on collisions. It persists that name in the instance definition and
keeps identifiers and installation paths independent of display names.

Later phases add a content-addressed object store shared by instances
(hard links where the file system allows) so duplicate client files are stored
once. The instance folder layout stays the same with or without the store.

## 5. Servers and the launcher API

A server is added by its launcher API base URL. The API is an optional plugin
of the server (for Bubble Fighter: a plugin row in BubbleFighter.Fantasy) and
provides:

* server information: name, online count, supported builds, news;
* account registration and login (passwords are verified by the server);
* a profile summary (display name, currency, experience);
* the client distribution manifest per build: file list with sizes and
  hashes, where to get each file, and typed `clientPolicy` plugin rules;
* a short-lived **login ticket** the launcher passes to the client through
  `session.toml`; the messenger stand-in returns it as the passport the client
  sends in its login request, so the game server can verify it.

A server without the API can still be added manually by address; it then
works like today's launchers (no verified login, no automatic install).

## 6. Downloads

Local server packages can also be owned by the launcher through the optional
`ILocalServerModule` SDK capability. Shared orchestration retains exact process
ownership, graceful shutdown, bounded diagnostics and transient account destinations.
The game's module generates a configuration patch without rewriting package files.
See [Local hosting](local-hosting.md) for the current adapter and remaining room work.

* The built-in HTTP engine implements ranged resume and streamed hash validation.
  Bundled aria2 over JSON-RPC remains planned.
* Sources: plain HTTP(S) and **AList** (`/api/fs/get` for a signed `raw_url`,
  `/api/fs/list` for folders, optional token). AList is supported, not
  required: a manifest entry names a source id and a path, and any source
  type that resolves a path to a URL works.
* Every file is verified by size and hash before it is moved into place. The
  installer applies manifest additions/replacements using a durable rollback
  journal. Deletion, move and rename operations remain planned.

The current frontend supplies an opt-in development preview of client and launcher
updates. It keeps total byte progress and current-file progress separate, with
speed and an estimated remaining time only while data is transferring. Paused,
verification, installation, failure and completion states share the same task
model. The preview is held in memory and never downloads, installs or restarts
anything. Real client tasks now share these views through a separate host-backed
store. Launcher updates now share those views and use an external process with
frontend readiness confirmation and rollback; see [Launcher updates](launcher-updates.md).

The Home card uses a fixed height within the desktop workspace. Expanding update
details reduces the video area instead of moving the client controls and server,
account and language row out of view. The status row explicitly offers Expand
details or Collapse details, while Open downloads navigates to the task. On the
downloads page, the task list and file details scroll independently; the title,
progress and task controls stay visible.
Task counts appear only in the filter tabs. Aggregate download speed sits beside
the tabs; the selected task retains its own speed in the progress details.
One initially collapsed details area contains the Files and Download source tabs.
Its height and opacity animate together within the remaining workspace; the
chevron rotates with the transition. Tabs remain visible while their content
scrolls. Changing the selected task collapses details, and closing details resumes
following the current file. Reduced-motion settings disable these transitions.

Store source management and the shared AList/HTTP catalog format are documented
in [Store sources](store-sources.md). Client file manifests, downloads and
transactional installation are connected. Store plugin files/ZIPs and launcher
updates reuse the transports while retaining separate installation lifecycles.

## 7. Hook plugins (client side)

CN2013 (`cn-406`, retail `0.2.35.001`) selects `launch.login = "gamewidget-ticket"`
and `launch.login_module = "sdologinentry.dll"`. The game module adds the verified
login bridge to the same transactional runtime deployment. Its exports forward
into the existing messenger host, sharing one session, readiness gate and control
channel. The bridge passes an account and the complete marked one-use server ticket
only after required plugins succeed; it has no account-file or environment fallback.
Other builds retain their own login paths. See the [native login contract](hook/cn-login-bridge.md).

After the client exits, native deployment restoration retries Windows access-denied,
sharing and lock conflicts within a two-second retry window for the whole restore.
Each attempt rechecks the target and original backup hashes before replacement.
Other errors and external byte changes are rejected immediately. Persistent contention
reports failure and retains the journal and original backups for stopped-client recovery;
it does not treat process exit as proof that all file readers have released the DLLs.

Desktop launches use an asynchronous worker for module preparation and shell startup.
Pending starts reserve a bounded slot and retain their instance lease without holding
the running-state lock, so system confirmation cannot block running-client snapshots.
Duplicate starts are refused. Explicit application exit and shutdown wait for pending
starts to resolve before stopping/draining returned processes; pending starts also
count as active clients for update/exit guards. The launcher does not cancel or bypass
a Windows permission prompt. Canceling it follows the normal startup-failure rollback.

An interrupted launch exposes Recover client on the Clients page. Explicit stopped-game
confirmation is required; recovery acquires the instance lease and refuses a matching
live executable or an uninspectable process. A leftover session must match the instance,
game and build before any restoration. Original DLL backups and the session remain if
validation or hash-based restoration fails. Successful restoration deletes the session
and retains diagnostic logs. Cloning/importing an unfinished native overlay is refused;
a session without an overlay continues to be excluded from copied clients. Recovery
does not terminate untracked games and assumes no external actor starts the executable
during the explicitly confirmed maintenance operation.

Hooks run inside the game process and are delivered as plugins:

* **FtHook runtime** (`native/runtime`): a static x86 library. It identifies
  the client build, waits until a packed executable is unpacked in memory,
  then loads the plugins listed in `session.toml`, validates their
  compatibility and applies them, writing one result line per plugin.
* **Host shim** per game. For Bubble Fighter it is `nmcogame.dll`, a DLL every
  client build imports. One binary exports the union of all messenger entry
  points seen across builds and runs in one of two modes:
  * `standin`: answers the messenger protocol itself (no Nexon services);
  * `forward`: passes every call to the original library renamed to
    `nmcogame.nexon.dll`.
  Because the client loads it after its unpacker has run, patches avoid the
  anti-tamper window that a `version.dll` proxy hits.
* **Declarative plugins** describe patches as data: how to locate a site
  (byte pattern, fixed RVA per build, or a named locator), the bytes expected
  there and the bytes to write. No recompilation is needed to add one.
* **Native plugins** add a DLL with a small C ABI for hooks that need code
  (detours, observers). They use the runtime's locator, patch and log
  services.
* Each plugin declares a SemVer version, supported and excluded builds, builds
  on which it is mandatory, and typed options the launcher renders as a
  settings form.

The Plugins page imports local folders and ZIPs through a per-user immutable
library. Staged previews are validated before commit; importing executes no code
and changes no client selections. Each client pins its plugin versions. Bundled
IDs and global mandatory declarations are protected from local imports. See
[Plugin management](plugin-management.md) for limits and activation behavior.

## 8. Frontend

The launcher stays open while a game runs. Closing its window hides it to the
tray; explicit exit confirms stopping active games. A per-user/data-root mutex
and activation event return subsequent launches to the first window. Launch
readiness and stop controls use the [local hook protocol](hook/control-protocol.md).
See [diagnostics](diagnostics.md) for opt-in crash and receive records.

The home card and its version selector retain their sizes across game changes,
including games with only one version. Tab content uses shared short fade/slide
transitions and respects reduced-motion preferences. Navigation buttons do not
expose browser link-status text.

Vue 3 + Vite + TypeScript, using the launcher's own control layer. The goal is that
the window is indistinguishable from a native WinUI 3 app: custom title bar,
Mica backdrop provided by the host, NavigationView, Fluent motion, no browser
context menu, text selection or scroll chrome, and a native-looking splash
while the web view loads. Seven UI languages: zh-CN, zh-TW, ko, id, vi, th, en,
chosen from the system language by default.
Messages live in per-locale JSON catalogs. Thai client date labels use the Buddhist
calendar; stored dates, version identities and user-entered names retain their original values.
Untranslated Thai messages retain English text.

Traditional Chinese (`zh-TW`) uses 中國, 韓國, 台灣 and 印度尼西亞 for client
regions, 模組 for project plugins/mods and 程式模組 for binary/program modules.
The official Traditional Chinese game title is 泡泡大亂鬥. Keep the server name
`Bubble Fighter Fantasy` untranslated. Simplified Chinese retains 国服, 韩服,
台服, 印尼服 and 插件. Apply these locale-specific terms to dictionaries,
game/build/plugin display definitions and browser preview data; never rename
identifiers, TOML keys, API fields or protocol terms to translate them.

The window opens at 1360 x 840 device-independent pixels, constrained to the
current monitor's work area. The caption text and window buttons occupy the
top 32 pixels; navigation starts below a 48-pixel title region. The navigation
pane starts as a 56-pixel icon rail, expands to 256 pixels, and remembers an explicit expansion. On
narrow windows it opens over the content and closes after selecting a page.
Settings stays at the bottom while the navigation list scrolls independently.
Cards retain a one-pixel border with modest contrast in both themes. Settings
cards fill the available content width with consistent page padding.

The WebView background may be transparent to show the system backdrop, but
the native window keeps normal rectangular input bounds. It must not use a
layered HWND whose transparent pixels send clicks to applications underneath.
Maximization uses the monitor work area; restore uses the native window state.

## 9. Repository map

```text
docs/                 design, decisions, roadmap, task cards
native/               FantasyTale.Native.sln (Visual Studio, x86)
native/runtime/       FtHook runtime (static library, C++20, x86)
native/tests/         runtime tests
games/BubbleFighter/  game.toml, builds, built-in hook plugins, native host
games/TalesRunner/    display definition and account rank assets; no game module
games/AirRider/       display definition and account rank assets; no game module
src/                  .NET 10 projects
tests/                .NET tests
web/                  Vue 3 frontend
```


## Connected server settings

The launcher first requests public `/v1/catalog` metadata. If a plain HTTP domain
entry fails, discovery retries its live DNS address without credentials. It never
bundles a deployment IP or region. A proxy-generated 198.18.0.0/15 DNS answer triggers
an HTTPS public-DNS lookup. No third-party service receives passwords or tokens.
Subsequent requests use explicit server `hostPolicy` first, then a nullable local
`prefer_ip` toggle, then global `ip_preference = "china" | "all" | "disabled"`.
`china` is the default; the server's region is authoritative. When absent, CN registry
allocation ranges are a best-effort fallback and do not guarantee physical location.
Plain HTTP uses the IP authority/Host. HTTPS keeps its domain and normal certificate
validation; the HTTP Host workaround does not bypass TLS identity checks.

Server settings are independent TOML files under the data root. Per-instance TOML
stores build, language and plugins. Selecting a server does not overwrite an
instance's connection configuration. Explicit server fields (including false and
empty lists) override local capabilities and login/language choices. Unknown client
builds can appear as presets but cannot install or launch without a local module.

Login and registration share one dialog. Additional registration fields are bounded
server data, rendered as text/form controls. Tokens never cross the frontend bridge;
access tokens remain in memory and refresh credentials use current-user protected
Windows storage. Profiles display unknown statistics as a dash:
Bubble Fighter and Air Rider use level/LUCCI/Cash; Tales Runner uses level/EXP/TR/Cash.
The server may provide an HTTPS rank icon, with a numeric fallback.

Optional `game.media.background_audio_tracks` aligns one audio file or empty string
with each video. Separate audio follows video time, pause, mute and playlist changes;
a failed audio track leaves the video usable with a visible notice. Game language
choices are separate from launcher UI language. Planned build languages are selectable
and persisted before their game module implementation is complete.

Account levels, experience and local icons use the TOML/source tables selected by `game.levels`; see [level data](levels.md). Special icons override appearance while preserving ordinary level and experience.

## Server account selection

Each exact local server identity and API endpoint owns an independent account
collection, without a fixed account or server count limit.
The public bridge returns an atomic selected profile and account list, never
bearer tokens. Signing in adds or replaces that account only. Passwords and access
tokens are not persisted. Refresh credentials and the last validated profile are
encrypted with Windows-current-user DPAPI and atomically retained below the data
root. After restart, selected sessions obtain a new access token before requesting
account data or launch tickets. Access expiry and unexpected HTTP 401 trigger one
serialized renewal; a transient network error leaves the refresh session intact.
The server must retain refresh-session hashes across its own restart. See
[the authentication contract](launcher-auth.md).

The Home account menu selects a session and ends with Add account. The Accounts
page groups all signed-in accounts by server and supports selecting, refreshing
the active profile and signing out of one account. Signing out never silently
selects a different identity. Login failure does not clear existing accounts;
remote rejection invalidates only the token used by that request. Server edits
fence pending logins. A launch ticket is rejected if the selection changed while
it was requested, even if the user switched back to the original account.

A delayed successful login adds its account without taking over a selection
changed during authentication. A response cannot replace the same account's
session if it was refreshed, removed or expired meanwhile; the user retries
authentication against the current state.


### Temporary feature availability

`game.rooms_enabled` and `game.local_hosting_enabled` are validated booleans,
defaulting to `true` for existing definitions. Setting them to `false` retains
the implementations and saved settings. The hosting navigation entry is disabled,
and direct hosting links show a development notice without polling. Room browsing,
joining and publishing are unavailable while the server tab, saved servers,
discovery, submissions and server connections remain available. Backend guards
reject disabled operations with `feature_in_development`; stopping existing runs
and withdrawing publications remain available for cleanup. Re-enable each feature
through its game declaration after acceptance testing.

The Chinese authentication component owns native passport adaptation. The game module may supply optional CN2011 Center titles through the existing opaque module metadata envelope, bound to native selector IDs. Missing names preserve native fixed titles; Gateway packets and routing are unchanged. The optional title callsite adapter is separate from passport assignment and must not be described as Hook-free.

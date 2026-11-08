# Decisions

Each entry records a decision, why it was made and what would reopen it.
Append new entries; do not rewrite old ones; mark superseded entries.

## D1. Hooks load through `nmcogame.dll`, not `version.dll` (2026-10-07)

Every Bubble Fighter client build imports `nmcogame.dll`. Measured on the local
client set: all CN builds (2011-2019) ship the original Nexon library with 10
exports (`NMCO_CallNMFunc` family); KR2015, ID2014, ID2015 and TW2019 already
run our 5-export messenger stand-in; KR 2018+ (kr-1256 family) ships the
original with 10 exports including `NMCO_CallNMFunc2`. Themida-packed
executables (ID2014) stall when a `version.dll` proxy loads during the
loader's import resolution, while `nmcogame.dll` is loaded after unpacking.

Decision: one x86 `nmcogame.dll` exports the union of the 11 entry points and
runs either as a stand-in or as a forwarder to the renamed original
(`nmcogame.nexon.dll`). The hook runtime is linked into it. BFClientHook V2
(`version.dll`) is deprecated once V3 is verified on real clients.

Reopen if a build is found that does not import `nmcogame.dll`.

## D2. Hook plugins are data first (2026-10-07)

Most client patches are "find bytes, check bytes, write bytes". They are
described in `plugin.toml` and need no compiled code. Code-requiring hooks
are native plugins behind a C ABI. Named locators let complex site discovery
be written once (in the runtime or a native plugin) and reused declaratively.

## D3. Closed-source games are runtime-discovered modules (2026-10-07)

TalesRunner and AirRider modules live in private repositories, reference only
the public SDK and native ABI, and are discovered from the `games/` folder at
run time. The launcher is MIT, so a module may be loaded in-process.

## D4. TOML for every definition (2026-10-07)

Matches the server project's configuration style and is pleasant to hand-edit.
Managed side: Tomlyn. Native side: toml++ (header-only, MIT).

## D5. A file, not environment variables, carries the session (2026-10-07)

Elevated client starts lose the parent environment (seen with the CN2011
GameWidget stand-in). `session.toml` next to the host DLL is read instead.

## D6. Frontend: Photino + Vue 3 with a local control layer (2026-10-07)

Web UI gives the motion quality wanted. The launcher maintains its own Vue
controls and Fluent-style design tokens. The host adds a Mica backdrop and a
custom title bar so the window reads as native WinUI.

## D7. Downloads: aria2 first, AList supported but not required (2026-10-07)

aria2 (JSON-RPC) gives multi-connection resume for large clients. A manifest
names a source id and path; AList and plain HTTP are two source kinds.

## D8. Launch identity, readiness and bounded local control (2026-10-07)

Schema-two sessions carry a random launch id and launcher PID. Native readiness
matches the process creation timestamp and exact resolved plugin list. A
current-user named pipe with peer PID checks supplies ping/status/stop; it needs
no service or network port. Opening a process is separate from hook readiness
and server authentication. Reopen for plugin-defined live commands after their
authorization, lifetime and message limits have a concrete contract.

## D9. Recoverable temporary runtime deployment (2026-10-07)

Validate package hashes, stage files, preserve originals and persist a journal
before replacement. Restore only after confirmed process exit. Conflicting
external edits preserve recovery evidence and block automatic replacement.
Automatic orphan recovery must independently establish that the process stopped.

## D10. Locally intercepted Windows asset origin (2026-10-07)

The Windows web view did not navigate to the custom app scheme. The host now
intercepts the exact reserved origin https://fantasytale.invalid/ and serves
allowlisted files from wwwroot and game asset roots. Other HTTPS requests retain
normal handling. The stable origin preserves browser storage without a local
listener. Reopen when the host offers supported custom navigation or streaming
virtual-host mapping; current media responses are fully buffered.

## D11. Source-independent verified client installation (2026-10-07)

HTTP and AList resolve immutable source paths for one file-manifest format.
Catalog identity and the digest of exact manifest bytes are pinned before any
task starts. Ranged transfers are bounded by size, and whole-file hashes are
verified before replacement. AList URLs are resolved again on each attempt.

Installation uses a same-volume staging area, a durable original-file journal
and the existing client lease. Recovery preserves external edits and refuses to
launch a client with a remaining journal. A committed receipt retains source,
package, version and manifest identity for future update discovery. The first
implementation handles uncompressed additions/replacements and user-initiated
retry; archive extraction and restart task scheduling remain separate work.
Client sources do not inherit permission to replace the launcher itself.


## D12. Folder synchronization without manual release lists (2026-10-07)

Game build versions remain original identifiers. File synchronization is the default
publication workflow: one configuration maps client folders to game/build identities,
a watcher generates content snapshots and publishes only new file objects, and HTTP
and AList serve the same output. Client update labels and revision numbers are not
required. The current snapshot digest identifies changes; it never rewrites build
metadata. Optional numbered immutable releases remain supported separately.

Both publisher and client cache successfully checked content against size and file
timestamps. Normal checks reuse unchanged entries instead of repeatedly hashing large
files. SHA-256 still verifies transferred bytes. Metadata caches are change detectors,
not proof against deliberate timestamp preservation or silent corruption. Snapshot
files are internal generated transport data, not an operator-maintained change list.

Task history is atomically persisted before scheduling, bounded to 32 tasks and two
workers, and retains manifest bytes rather than expiring URLs. Recovery binds the
source endpoint, client destination and previous receipt. User pauses survive restart;
active tasks resume and transaction journals recover before downloading. Source
retargeting needs explicit store selection. Self-update retains its separate trust.

## D13. Immutable local plugin imports and explicit activation (2026-10-07)

Folder and ZIP imports share a bounded staging library. Preview and commit verify
the same copied bytes; commit atomically moves a complete version into place.
Library identity is game/id/version, hashed only for its filesystem directory.
Importing executes no native code and does not select a new plugin. A higher
version upgrades compatible existing selections while retaining activation and
shared options. Running clients retain their configuration lease; a durable
local activation intent verifies the imported snapshot after exit or Launcher
restart, and later user choices invalidate that intent. Explicit version switches
can opt into fresh defaults. This revises the initial selection-free import rule.

Bundled IDs remain reserved across all versions. Imported manifests cannot impose
`required_on`, because catalog discovery would otherwise add global requirements
or auto-enable code in newly created instances. Reopen when source-authenticated
mandatory package distribution has its own contract. Store delivery should reuse
the installation boundary with source-bound receipts; it is not implemented by
this local-import change.

## D14. Login interface forwarding shares the native host (2026-10-08)

A client-loaded login adapter forwards its three exports to versioned entries in
the existing nmcogame host. It does not start a second runtime or read another
identity store. The client supplies its callback; the host waits for required-plugin
readiness and passes the complete marked server ticket. Data selects the DLL target;
shared launcher code does not branch on a game or build. One-use delivery refuses
missing/malformed credentials and never falls back to a guessed account. Reopen
when another proven client interface or non-ASCII account contract is required.

## D15. Source-driven launcher updates with deferred commit (2026-10-08)

Launcher publication scans a build directory into ordinary TOML file metadata and
content objects. HTTP and AList carry the same bytes. No signing-key workflow is
required. The configured source supplies the release; checksums verify downloaded
content and pin a selected task. Source settings remain separate from third-party
game/plugin catalogs and cannot be overwritten by a release.

An installed helper runs outside the destination, checks process identity, waits
for exit, and reuses the installation journal with deferred commit. A new process
must report frontend readiness before originals are discarded. Managed game starts
are blocked across handoff and health verification. Reopen for obsolete-file
deletion, power-loss repair tooling and installation directories requiring elevation.

## D16. Owned local server processes with private stdio control (2026-10-08)

Optional game modules generate server launch plans and isolated configuration
patches. Shared code owns redirected pipes, startup deadlines, per-installation
leases, bounded logs and Windows Job Objects. Only the owned child's complete
stdout readiness marker enables its transient server destination. Stop sends a
private stdin command before a bounded forced termination; stale run ids fail.
Administration remains the existing loopback web UI. Temporary persistence is
explicitly opt-in and never an automatic database-failure fallback.

This implementation does not advertise direct reachability as automatic networking.
Directory publication and room expiry/join follow D17; durable local storage remains core work.
Reopen for non-Windows process containment or an evolved server control protocol.

## D17. Separate reviewed servers and leased rooms (2026-10-08)

A standalone, game-independent HTTP directory owns reviewed persistent server
records and a bounded pending queue. Temporary room leases are distinct and are
not persisted across directory restart. Room management tokens remain in backend
memory and grant no review authority. The source's ordinary administrator bearer
credential protects approval; no public-key or release-signature infrastructure
is introduced.

The launcher publishes only ready owned room invocations and runs heartbeats
outside the frontend. Process exit, explicit withdrawal, source failure and lease
expiry have separate outcomes. Joining rechecks directory membership and the game
server's actual catalog; account scopes include source and entry identity. Room
destinations are transient, while selecting a reviewed persistent server adds it
to the normal collection. Reopen for directory federation, delegated moderation or
network traversal; this protocol makes no reachability claim.

## D18. Source-bound plugin upgrades retain explicit activation (2026-10-08)

Update discovery starts from an installed package's receipt, scoped to transport,
base URL and catalog path. Matching names or IDs in other catalogs grant no update
authority. Stable packages do not move into prereleases, and equal SemVer precedence
with different build metadata cannot arbitrarily choose a winner. A selected update
is rechecked before the existing HTTP/AList queue receives it.

Downloading installs another immutable library version; it never changes a client
selection or executes native code. Confirmation switches one client, with new option
defaults and the existing instance lease. Old versions remain available for rollback.
Bundled packages follow launcher updates, and manual imports remain manually managed.
Reopen for explicit source migration, background update policy or dependency resolution.

Amendment (2026-10-10): the confirmation now defaults to retaining shared option
overrides instead of resetting them. Removed keys are dropped; an incompatible
retained value rejects activation without saving a new selection. The user can
explicitly choose defaults. Persisted selections are resolved again at launch,
and disabling a plugin preserves its options without adding it to the session.

## D19. Explicit local saves preserve legacy storage choices (2026-10-08)

Local hosting profiles add an optional storage mode without replacing the legacy
temporary-data field or constructor. Absent mode retains the old package/temporary
choice; explicit conflicting fields fail validation. New UI profiles choose local
saves. Switching providers does not migrate accounts or progress.

The game adapter activates the complete package's SQLite provider only in local
mode and places its database in the exclusively owned stable data directory.
Missing provider/native dependencies fail before launch. Package and temporary
modes retain their existing behavior; no provider failure falls back to memory.
The adapter disables providers with replacement rows and supplies settings with
separate config updates, matching the actual server composition contract.
Reopen for storage migration, schema upgrade UX or portable backup workflows.

## D20. Obsolete launcher files require recorded ownership (2026-10-08)

Launcher updates retain a bounded file-ownership snapshot alongside their installed
receipt. Only matching source/package identities can remove absent paths, and each
removed file must still match its recorded size/hash. The same durable transaction
backs up removals and replacements; startup health gates both. Unrecorded files stay
untouched, and older receipts acquire ownership without guessing existing files.

External changes before removal refuse installation; recreation during startup
refuses commit and retains recovery evidence. A committed journal durably publishes
the new receipt and ownership snapshot before its removal, so recovery can repeat
those writes. Client installers keep their prior additive behavior unless explicitly
using this capability. Reopen for file/directory shape migration or independent
power-loss repair UX.

## D21. Offline recovery belongs to the retained updater (2026-10-08)

An interrupted launcher installation is repaired by the updater kept outside the installation. Bootstrap hands off the exact journal hash and parent identity, then waits for exit; the helper rechecks the journal before and after the wait and uses the same leases. It restores committed and uncommitted journals without committing a new release or accessing the network. Direct `--repair` handles a launcher that cannot start. Changed files and missing recovery evidence remain untouched and produce `recovery-required`. No signature requirement is introduced.

Amendment to D18 (2026-10-10): selecting an installed version in the library is
an explicit selection operation. It replaces the single client-scoped version
while retaining enabled state and shared overrides; it does not execute code.
An expected previous version rejects stale selection changes. Ordinary disabling
still targets only the selected release. Downloading alone remains selection-free.

## D22. Older readers retain a stable bootstrap feed (2026-10-10)

Superseded (2026-10-23): this product is unreleased. Older test readers are not a
compatibility target or release gate. Human-readable TOML, complete inventories
and exact-byte identity take priority; no reader size ceiling determines the
published format. The following records the former decision only.

Release labels, immutable object folders and client-index schema versions are
independent. Canonical discovery remains schema one so every shipped older
reader can fetch a complete newer launcher. Previously distributed feed aliases
remain published with identical index bytes. A future format requires an initial
release supporting both readers and a retained schema-one bootstrap, not replacing
the old entry point with an unreadable index.

Packaging emits canonical/legacy indexes and a manual-download ZIP; publication
verifies their content before replacing discovery entries. Manual fallback uses
its own local definition so a failed index does not remove the download-page
option. Unknown latest versions remain unknown. A retained acceptance tool loads
unchanged older distribution assemblies and verifies real discovery, automatic
staging and offline restoration; current-reader tests are not a substitute.

## D23. Recommendations belong to the client build (2026-10-11)

Optional plugin recommendations are declared by each build in its `[plugins]` `recommended` array. An empty array has no user-visible effect. The launcher filters enabled selections, records one acknowledgement beside the client, and stores any confirmed store download as a durable activation intent. Verification and the instance selection lease complete before that intent can enable a plugin; changed selections therefore cannot be overwritten by a late worker. Bundled plugin identities remain reserved unless the game explicitly lists the identity as store-managed, and a same-version store payload must have identical bytes. Reopen for server-mandated recommendations or a migration that changes the per-client acknowledgement semantics.

## D24. Launcher-side file plugins use the shared recovery transaction (2026-10-11)

Hashed resource/DLL replacements are `kind = "files"` packages, resolved by build selectors and excluded from hook sessions. They share runtime staging, collision checks and restoration after confirmed exit. Existing byte limits remain bounded and external edits retain recovery evidence. Trusted bundled `warn_on` components are selected once and may be disabled only with per-launch confirmation, unlike hard `required_on` locks. Imported packages cannot impose either policy. Chinese authentication keeps one identity with widget/SDO file branches; future hooks retain that identity but require an explicit contract extension. Reopen for archive-entry patching, permanent replacement or combined file/native packages.

## D25. Source-bound latest components and selection-safe upgrades (2026-10-11)

Mandatory recommendations are checked and locked in the installation prompt; later warning overrides remain distinct from hard runtime requirements. Bundled latest-only components name one configured source, whose releases must preserve the bundled source and requirement policy. A pre-launch catalog/manifest/payload check queues verified upgrades and blocks execution while pending or unverifiable. Ordinary newer store downloads switch only the target client's unchanged enabled selection, preserving compatible options; separate bound activation sidecars retain schema-one task-reader compatibility. Installed file lists also pin extracted ZIP contents. No keys, new trust infrastructure or game build-number changes are introduced.

Reopen if source federation, offline latest authorization or native hot reload obtains a concrete requirement.

## D26. Update confirmation authorizes one automatic restart (2026-10-13)

Confirmation timing superseded by D27; native readiness and lifecycle rules remain.

The circular navigation-footer entry uses the shared WinUI dialog, progress bar,
ring and tooltip controls. A confirmed update keeps download/restart consent for
one manifest and task in the current frontend lifetime, independent of whether
progress is visible. Background staging alone gives no restart consent. Cancellation,
selection changes and failure revoke it; failed handoff requires another user action.
The host rechecks the selected hash and readiness before explicitly permitted owned
process cleanup, while retaining the lifecycle gate until handoff completes or fails.
No feed schema change or persisted automatic-restart preference is introduced.

Reopen for background operation after complete frontend teardown or cross-restart
restart consent, which would need a separately defined durable user intent.

## D27. Download and installation require separate confirmations (2026-10-13)

Available updates first show installed/target versions and request download permission.
Verified ready staging then requests installation permission with the interruption warning.
Downloads can be hidden through X, background action, Escape or backdrop; installation
handoff cannot be hidden. Failed handoff releases that lock and requires explicit retry.
Every healthy startup checks regardless of the saved periodic preference, which now
controls subsequent thirty-minute checks. Manual discovery retains the same UI and can
reopen a canceled prompt. Sidebar byte progress becomes a spinner only after transfer.
No feed migration or persisted installation consent is needed.

Amendment (2026-10-29): a verified ready task automatically opens the installation
confirmation once per selected manifest during this frontend session, including
background downloads and restored staging. Later preserves staged files without
repeated polling prompts. The sidebar reopens it; installation still requires a click.

Reopen for a concrete requirement to install automatically without the ready prompt.

## D28. Application SemVer gates authenticated use and plugin activation (2026-10-14)

The packaged application SemVer supplies About, API reporting and minimum-version
comparisons; there is no separate integer version ledger. Optional plugin minimums
are enforced by the launcher and syntax-checked by the native manifest parser.
Catalog and login/refresh policies merge exact-build plugin identities, resolve
them through configured stores and retain the strongest requirement. Public catalog
discovery and logout remain exempt so outdated restored clients can route and
revoke sessions. Valid rotated refresh credentials are persisted before policy
extensions are validated.

Remote requirements remain scoped to the selected destination and account. Launch
rechecks enabled packages under the client lease and promotes required runtime
failure only in the transient session, never in persisted client selections.
Deferred remote activation has a separate bound sidecar; schema-one task JSON
and existing recommendation sidecars keep their exact shapes for older readers.
Older launchers cannot activate remote-only recommendations without local data.

Reopen for version ranges, minimums per individual server build, or a concrete
need to federate plugin sources through authentication responses.

## D29. Companion communication is an optional WSS JSON foundation (2026-10-16)

The next independent foundation plugin owns one WSS connection per game launch,
using `fantasytale.companion.v1` and UTF-8 JSON envelopes. Other plugins consume an
owner-scoped SDK, with no sockets or network credentials exposed to game code or
HTML. Overlay is a separate consumer of structured text and trusted local templates.
The client prototype and separate notification consumer are implemented, while
optional manifests and Host lifecycle activation are implemented. Production
server authorization/account factory integration remains pending. Runtime 3.3.3
adds a separate owner-restricted native broker, sharing process authentication and
framing with Overlay without depending on its renderer. Native message schemas
are declared in manifests and merged before launch. The current
boundary and provisional wire are recorded in `../hook/companion.md`.

Server-issued launch authorization and game-session binding are prerequisites.
Companion connection tickets are distinct from game-login tickets, supplied through
the upgrade Authorization header and renewed for each reconnect. Queue/message
limits, cancellation, heartbeat, epoch checks and explicit uncertain request results
belong to the transport foundation. The original game protocol remains independent.

Strict UTF-8 preserves mixed languages, supplementary characters and combining marks.
Fragment byte limits are checked before buffering; decode and JSON parse wait for
the complete message. Game-specific legacy encoding conversion is confined to build
adapters, and string IDs avoid JavaScript numeric precision loss.

Reopen for measured throughput requiring a binary encoding or a requirement to keep
the channel active after the Launcher has fully exited. Those requirements do not
justify gRPC/protobuf or a separate companion process in the first implementation.

## D30. Game implementations ship from separate repositories (2026-10-21)

Keep the launcher, managed SDK and generic native runtime in the launcher
repository. Maintain Bubble Fighter's game module, `nmcogame` host and game
plugins in an independent repository with separate versioning and packaging.
Store profiles remain the source of required/recommended plugin policy. The
launcher repository owns a narrow, standalone C ABI SDK export; game code must
not reach into runtime implementation projects. Existing authentication and
movement plugins still depend on internal helpers, and no source has moved yet.
The migration guide lists the build and behavior gates. This decision does not
change repository visibility or authorize publication of unresolved assets.

Reopen if separate repositories force duplicated launcher contracts or prevent
independent builds without creating a versioned game implementation dependency.


## D31. Stable server profiles are shared across local files and stores (2026-10-23)

Each persistent server has a standalone TOML profile. The internal `server.id` is
stable across endpoint changes; new saved filenames are `<id>.toml`. IDs contain
ASCII letters, digits, hyphens and underscores only, excluding device filenames.
The add form may omit the ID and derive an initial address-and-port identity.
Identity comparisons are case-insensitive to prevent Windows filename collisions;
renaming a file does not change its internal identity.

A store listing carries a relative profile path, but browsing or favoriting never
fetches that file or saves a server. Explicit selection/import applies the common
parser. Identical settings reuse the destination; different saved settings need
confirmation tied to the inspected contents. Local/store TOML and network JSON
remain separate representations. Live presentation already overlays local fallback
information. An explicit Update from server action writes validated presentation
and declarations back to disk while preserving the profile ID and endpoint, and
refuses to overwrite concurrent local edits. Address migration remains future work
requiring a matching server contract and explicit confirmation.

## D32. Managed game packages own their dependency resolution (2026-10-23)

Load each installed managed module path in its own assembly load context. Resolve
private dependencies through the component manifest and adjacent managed DLLs;
share the host SDK assembly so public interfaces and launch data retain type
identity. Keep ordinary framework resolution and the existing application-folder
fallback when the game-local main assembly is absent. Reuse each path's context
for the process lifetime, constructing a module instance for each load request as
before. Replacing a loaded package requires a Launcher restart.

A separate SDK-only package reproduced a missing adjacent dependency under the
former default-context loader. Package-local resolution also allows two installed
paths to carry identically named dependencies independently. This is a runtime
integration boundary, not an isolation or trust boundary for untrusted code.

Reopen for explicit module unload/reload requirements, a versioned SDK ABI change,
or a packaging layout that the component dependency resolver cannot represent.

## D33. Game/build profiles use clientPolicy (2026-10-27)

Use the same format-one typed policy for game definitions, bundled/store build
profiles and server destinations. Remove package activation selectors and the
separate build required/recommended lists; rewrite existing rules manually. Do
not retain aliases, automatic conversion or old bridge methods. This supersedes
D30's field representation while preserving store-distributed build profiles.

Local declarations compose with game rules. Server constraints intersect local
requirements, version bounds and named sources instead of replacing them. Fail
on contradictions. The installation prompt, update preparation and launch checks
consume that result. Native code receives the managed session's required flag;
a package cannot impose policy. Source ownership, package compatibility, warning
text, file retention and initial default selection remain separate concerns.

See [client policy](../client-policy.md) for TOML, rule types and the manually
rewritten 23-row inventory. Reopen when the policy format adds a new rule family.

## D34. Resource overrides compose from the user's archives (2026-10-29)

Settings-dependent resource plugins contribute member edits rather than shipping foreign complete BFZ or PKN files. The game module reads the selected client's current archive, merges all claims for one physical archive, preserves untouched stored records, and stages the result through the existing recoverable runtime transaction. A content-addressed cache is keyed by the input archive digest and all effective edits; a changed input or damaged metadata rebuilds the cache. The transaction records original hashes before replacement and restores them after exit or a later crash recovery. Resource preparation reports measured archive and member progress so a launch can be cancelled safely. Reopen if a game requires a format whose reader and writer cannot preserve its own metadata.

Packages declare incompatible predecessors through `plugin.exclusive_with` data.
Explicit activation disables optional conflicting selections in the same save,
retains their settings, protects client-policy requirements and never uninstalls
their library packages. Launch resolution also rejects conflicting enabled pins.

## D35. Gateway Center login uses the original client protocol (2026-10-31)

Use the original game chooser and audited Center/MoveChannel consumers for
Gateway-mediated login. Automatic decisions and health measurements stay outside
the game. Do not replace chooser rendering, clicks, selection or routing with
in-process Hooks. A build's demonstrated original login defect may retain a
strictly scoped authentication repair, including CN2012's credential continuation.

The login provider's one-use rule from D14 retains a narrow exception for the
audited CN2012 redirected LoginStage. The original callback address, invocation
bytes, active stage vtable, reset stage latch and image fingerprint must match.
Only one further callback is allowed on a different connected selected connection
whose native profile still has a fresh-login identity. Other images, arbitrary
callbacks and same-connection retries retain one-use behavior. The server's ticket
redemption rule remains independent; no new ticket is minted for the redirect.

Audit each build's packet readers, stage transitions and credential handling
before enabling it. CN2012's former presentation adapter is removed; its
authentication provider repair remains independent of chooser rendering and
endpoint routing.

Gateway retires obsolete bootstrap sockets only after a validated redirect write
and clean source release. Worker authentication and gameplay acceptance remain
separate evidence. Keep the general module metadata ABI for other capabilities.
Reopen only if original client evidence demonstrates a protocol limitation that
cannot be met by the external Gateway and the owner revises this design.

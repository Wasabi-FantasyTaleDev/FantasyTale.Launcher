# Changelog

## 0.1.69 (2026-11-08)

- Validate the selected build's native Center status protocol before exposing server endpoints to the game. TCP-ready nodes whose protocol is unavailable retain a local unavailable entry that completes native uniqueness cleanup; Automatic cannot select those nodes during that launch.
- Preserve per-node catalog capacity through native routing so unavailable status entries retain their original identities and bounded population sentinel. Keep admission withdrawal separate from cleanup lifetime.

## 0.1.68 (2026-11-08)

- Keep Gateway's accepted native cleanup connections alive when ticket redemption closes automatic admission or its grant expires. Withdraw candidates without prematurely stopping the sidecar, avoiding stale auxiliary connections and repeated login-method errors.
- Add validated include/exclude game-language conditions to file replacements and patch exclusions. Filter files before destination conflicts and hashing, retaining recovery after game exit.
- Update per-client required and recommended plugins and their initial check states. Refresh plugin descriptions and support independent Chinese font routes plus the KR1256 font variant.
- Reuse the immutable downloadable plugin ZIP for store installation, avoiding a duplicate archive upload.

## 0.1.67 (2026-11-08)

- Keep game launch preparation working when an older diagnostic file or directory is in use. Retention skips the blocked run, preserves its available evidence and retries cleanup on later launches.
- Avoid exclusive client and name-registry leases when plugin recovery or scheme application has no changes. Empty plugin-library maintenance and reader disposal also avoid the shared library write lease. Opening the plugin inventory can defer a busy scheme write while preserving the next-launch selection and continuing to read installed packages.
- Add View details to client configuration occupancy notices, including the requested operation, protected client path, lease path, operating-system code and recent launcher events. Actual configuration edits still require their exclusive lease.

## 0.1.66 (2026-11-08)

- Add validated compound resource conditions so independent snow and Christmas BGM settings select one exact sound-table replacement. Scene style selection 1.0.1 uses these conditions and provides independent NPC outfit choices.

- Preserve the store's checked Install and enable choice in the selected plugin scheme for every compatible build, including builds without a registered client. Switching versions or restarting the launcher no longer restores an older disabled scheme selection. Pending choices survive running games and transfer retries; cancellation and later manual edits retain their own selections.

## 0.1.65 (2026-11-08)

- Support independent resource style choices through validated string and enum conditions. The separately distributed Scene styles plugin offers Original or Snow for the main screen and plaza on CN2011, CN2012 and CN2013.
- Retain bounded Gateway connection and binding diagnostics through auxiliary retirement so late local-port evidence remains available when investigating repeated native prompts.

## Diagnostic mode 1.2.7 (2026-11-08)

- Retain readable runtime pages from the main client image in minidumps so background faults can be investigated with the code and globals that were actually loaded. Reserve at most half of the existing dump budget for these pages, skip guarded/unmapped memory and report partial capture explicitly.
- Preserve the existing total file limit and exception wait deadline. This improves crash evidence; it does not establish that a reported client crash is fixed.

## Diagnostic mode 1.2.6 (2026-11-07)

- Stop normal window destruction, including the startup splash, from consuming the process's one minidump. Explicit close/quit snapshots run asynchronously, and window-message diagnostics skip a busy recorder instead of blocking the game thread.
- Keep exception snapshots distinct from confirmed terminal failures. A close snapshot can finish only while the process remains alive; collecting a snapshot does not establish a crash.

## 0.1.64 (2026-11-07)

- Apply the store's Install and enable choice to every compatible registered client, including clients without a previous selection and disabled selections. Share one download and defer changes for running games until they exit. Download-only and automatic updates retain their existing activation behavior.

[Chinese](CHANGELOG.zh.md)

## 0.1.63 (2026-11-07)

- Finish a plugin's installation even when another required component is missing or disabled. Apply each selected client's configuration independently and resume saved pending installations without downloading them again; launch checks still report missing requirements.
- Keep verified downloads complete while configuration changes wait to be applied. Scope pending notices to the selected client and mention game exit only when that game is running. Show application failures separately from download progress.
- Distinguish first installations from updates in plugin cards and download progress.

## 0.1.62 (2026-11-07)

- Support temporary creation of missing BFZ resource archives, including combined plugin contributions, conflict checks and recovery after the game exits. This enables the separately distributed pirate expression repair for the affected Chinese clients.
- Add native two-button question dialogs for CN2011 and CN2012 through the existing Game Control Support component. Dispatch through the game's UI thread and wait for a stable active screen before presenting a dialog.

## 0.1.61 (2026-11-07)

- Keep Gateway connected until the game's server handoff finishes. This fixes a cause of repeated login-method prompts after joining a server. Closing the game still stops Gateway.
- Show recommended plugins unchecked by default. Server policy can explicitly preselect individual recommendations.

## 0.1.60 (2026-11-07)

- Require Launcher 0.1.60: versions through 0.1.59 are deprecated and must update before continuing.
- Consolidate Vue I18n messages into per-language JSON catalogs and add Thai interface selection with English text for translations still pending. Preserve existing locale preferences, dynamic metadata fallback and interpolation, and document frontend state ownership and asynchronous guards.
- Display client date labels using the Buddhist calendar when Thai is selected, and switch back with the interface language. Stored build dates, version identifiers and client names retain their original values.
- Hide redundant former plugin IDs when both packages are unsupported by the selected client. Retain old packages for clients that still need them.
- Recover missing or damaged plugin releases across registered clients and saved schemes, so switching versions does not repeat the same uninstall dialog. Preserve healthy replacements, other releases and unresolved imported packages. A scheme response cannot restore references removed during recovery, and dismissing a recovery dialog clears its pending notification.

## 0.1.59 (2026-11-07)

- Show one plugin card after an identity migration when the installed successor supports the selected client. Keep former packages available for clients without a compatible replacement. Resolve updates and saved selections against the successor even if its version series starts lower.
- Chinese authentication component 1.0.18 restores the verified native login bridge and passport table omitted from 1.0.17. The component is available independently from the store.

## 0.1.58 (2026-11-07)

- Repair stale plugin selections before applying a scheme or preparing a launch. A missing or damaged release is removed through the existing recovery dialog; an actually missing required component uses the normal installation prompt. Declared renames can start their own version series.
- Accept manually imported ZIPs for explicitly store-managed components while retaining their declared update channel, bundled same-version integrity checks and file restoration policy. This includes independently downloaded authentication and bundled-component packages; local imports retain their local origin.

## 0.1.57 (2026-11-06)

- Migrate plugin identities through manifest aliases, including several former IDs, while retaining enabled state, supported settings and saved schemes. Deprecated store components are removed with an explanation and one card per plugin; files still used by a running game are retained until it exits.
- Repair selections after completed plugin upgrades, including absent predecessors whose compatible successor is already installed. Installed required components are selected and enabled automatically; completed deferred upgrades are applied before startup component checks. Preserve optional disabled components and deliberate version pins.
- Respect server routing preferences during initial discovery, retry transient read failures once, and report directory timeouts through the normal network error contract. Authentication and admission writes are not replayed.
- Require Chinese authentication in Chinese build profiles, and Korean authentication plus shop access in KR-1256. Reject overlapping required and recommended rules, including rules reconciled through former plugin IDs.
- Apply Chinese font weight only when the selected game language is Chinese. The separately distributed font-support package replaces the configured game font with a Chinese/Korean font while preserving the original Chinese glyphs.
- Show public player build names and aggregate population completeness when supplied by the server, keeping admission capacity separate from historical peak population.
- Require Launcher versions through 0.1.56 to update for the new plugin identity and lifecycle metadata.

## 0.1.56 (2026-11-06)

- Keep monitoring a created game process when startup is cancelled or binding fails. If Windows refuses termination, retain its running slot and recovery files until the process exits; one cleanup failure no longer skips the remaining cleanup.
- Validate optional level definitions even when some artwork is absent, and report malformed definitions without hiding their errors behind missing assets.
- Reject contradictory parent/child resource edits and writes to detached cached nodes before changing a client archive.
- Accept external Overlay messages only from its startup document and block unrelated navigation and popup windows. The main window continues to use the unchanged official Photino dependency.
- Update Vue I18n to official 11.4.18, verify real translation and parameter rendering in all six languages, and omit referenced-project debug symbols from portable packages.
- Preserve existing artwork and user-customized update sources. New installations use archive-cn.600819.xyz with IP-preferred access enabled; the deprecated-version boundary remains 0.1.51. This user-testing release does not establish acceptance of every game build or interactive Windows elevation path.

## 0.1.55 (2026-11-06)

- Share framework assemblies between managed game modules and the Launcher, including self-contained installations. Temporary server-health refresh failures can now be recognized across the module boundary instead of unexpectedly stopping Gateway before the game finishes loading.
- Record only the HTTP status and a fixed error category when selection health refresh fails, without retaining request addresses, credentials or server response text.

## 0.1.54 (2026-11-05)

- Support build-scoped startup patches within file components and accept localized warning metadata in the native reader, allowing the Chinese authentication component to repair CN2014 startup without a separate compatibility plugin.
- Hide China 2014-04-03 and 2014-05-26 through their build profile configuration while retaining their definitions. China 2014-09-23 remains visible.

## 0.1.53 (2026-11-05)

- Restore the bundled game control and diagnostic native libraries omitted from 0.1.52, fixing launch failures reporting `native.library: native library is not installed`.
- Stage bundled native plugin payloads explicitly when packaging the Launcher, and reject a release before publishing its index if any bundled native library is missing, empty or excluded from the distribution.

## 0.1.52 (2026-11-05)

- Publish directly importable ZIPs for every plugin release and prefer verified ZIP installation with missing-archive fallback to individual files.
- Preserve historical plugin manifests when adding ZIP downloads and require Launcher versions through 0.1.51 to update.
- Present resource conflicts as plugin cards with explicit, launch-scoped choices and safe preparation retries.

- Resolve resource field aliases before conflict detection, check conditions against original content, and honor all losing whole-member claims during conflict retries. Expose managed resource manifest builders and structured host conflict replies, retain cached physical bindings, and reject inconsistent XML encoding and injected string-table keys.

- Add the `resource-edits` provider for BubbleFighter resource packages: add, replace, patch, upsert or remove item table and string table records by business key and patch, add, replace or remove `.ui` controls, in any language, without replacing whole members. Packages declare intent in data with `[[items]]`, `[[strings]]` and `[[edits]]`; built-in per-build locations for cn-406, kr-812 and kr-1256 can be overridden field by field. Edits from several packages and existing model and member plans merge into one archive output, and overlapping edits stop the launch before any client file changes with a structured, serializable conflict report that a retry answers with snapshot-bound choices. `IGameUiForms` lets client plugins read native `.ui` control parameters. Horizontal record tables such as FighterConfig are edited by record key and field name, and one CSV sheet with a column per language can carry item, string, horizontal and `.ui` edits together. `[[patterns]]` and CSV rows with `match` or `find` select records and controls by regular expression and substitute text per language, with a linear-time engine. CSV sheets may be UTF-8, UTF-16 or UTF-32 in either byte order, with or without BOM. Keys the client repeats address every record with them, and same-named controls are addressed as `Name@k`. Modern YAML string tables and hiding controls are not supported yet.

## 0.1.51 (2026-11-03)

- Name each launch's diagnostic folder by its local start time, such as `diag_20261103_044700`, instead of a random identifier. A diagnostic folder that holds a file the launcher did not write no longer stops the next launch with "automatic rotation was stopped"; that folder is left untouched and the other folders keep rotating, including the earlier `launch-…` folders.
- Automatically dismiss transient information, success and warning notifications after six seconds, pausing while hovered or keyboard-focused. Keep errors visible by default, allow explicit persistent notices or custom durations, and restart the lifetime for repeated plugin operations.

## 0.1.50 (2026-11-02)

- Route the in-game Messenger address and the in-game sign-in address like the launcher's own requests. A mainland host answers plain HTTP for an unregistered domain with its own registration block, so these addresses now use the server's IP when the launcher does, and the Messenger and the game's own sign-in reach the server again.
- Diagnostic mode 1.2.4 records who closes the game window when a client ends itself without an exception: each close, destroy and quit request on a top-level window is logged with its calling frames, and the first one saves a dump of every thread. Previously such an exit only showed the runtime exit path.
- Diagnostic mode 1.2.3 also saves the memory that thread stacks point at in its crash dump, such as the buffer of the packet a handler was processing when it faulted. The dump size limit is unchanged.
- Add the optional Legacy security compatibility plugin for nine audited legacy builds. It skips the retired native identity-report call with its original stack and return contract, preventing that empty-entry call after a server transfer. Native migration acceptance remains separate.

## 0.1.49 (2026-11-02)

- Keep plugin payload verification outside catalog and selection locks, reuse verification within each activation pass, and exclude unrelated maintenance from launch and download queries.
- Bound launch preparation and native readiness to one minute. Clicking the launch button again cancels preparation and stops a client created by that attempt.
- Retain installation progress until completed shared plugins have been activated, and reject stale client edits that would overwrite newer user settings.
- Bundle the pinned Gateway matching native short-name configuration and verify the delivered executable through process and pipe interoperability tests.

## 0.1.48 (2026-11-02)

- Preserve activated plugin versions and enabled-state edits in saved schemes, and follow the selected release in plugin cards.
- Allow plugin toggles, settings and uninstall during gameplay, with deferred cleanup and a next-launch InfoBar.
- Show missing startup components as package cards with Enable and continue, Start anyway and Cancel actions.
- Refresh update badges after automatic updates and report the version transition through a success InfoBar; shared notices support info, success, warning and error with opaque backgrounds.

## 0.1.47 (2026-11-02)

- Restore CN2011 native server captions using full directory names and the display-only v2 capability. Earlier authentication components ignore this payload; native selection and login remain unchanged.

## 0.1.46 (2026-11-02)

- Retry temporary native server-directory DNS and connection failures before freezing unavailable entries. Up to three attempts share the existing per-entry deadline, so one short failed probe no longer disables a reachable destination for the whole launch.
- Record the selector identity, bounded attempt number and fixed failure category in launch diagnostics without logging endpoints or transport exception text. Confirmed unavailable entries retain their names and disabled native status.

## 0.1.45 (2026-11-02)

- Bundle the newly compiled Gateway with optional native short names, startup compatibility repairs and bounded connection diagnostics.
- Keep CN2011 on its original chooser captions while the optional caption adapter is repaired, preserving native server selection and authentication.
- End the deprecation transition: publish the deprecated version boundary directly in the release index and remove separate policy generation and loading.
- Refresh installed plugin selections before reapplying a saved scheme after downloads, so a completed update retains its newly activated version, enabled state and options.
- Native login continuation for CN2012, CN2013 and CN2015 still requires original-client validation; the Gateway startup repairs do not establish that these login issues are resolved.

## Diagnostic mode 1.2.2 (2026-11-01)

- Load the system dump library by its full system-directory path so a preloaded game-local library cannot intercept diagnostic dump requests.

## Diagnostic mode 1.2.1 (2026-11-01)

- Preserve the first serious fault after handled exceptions exhaust the observation count, with bounded dispatcher and exception-handler evidence.
- Retry a rejected dump context as an explicitly labeled process snapshot without an exception stream, retaining the existing size limit.
- This diagnostic update does not change Center names, authentication, server selection or game behavior.

## 0.1.44 (2026-11-01)

- Preserve installed-client inventory when saving plugin settings without inspecting or preparing resource files.
- Keep the observed game exit code when connection cleanup also fails, with crash details available directly from the error notice.
- Trust issuer heartbeat readiness and successful player-side probes without excluding healthy automatic destinations because of observation timestamps; retain immutable launch admission and explicit unhealthy states.

- Add per-server and per-build plugin schemes with TOML import/export and optional missing-plugin installation.
- Add local hosting terminal and lifecycle controls, including delayed startup, running-server settings, no-account local onboarding, and automatic cleanup after game exit.
- Preserve installed-client inventory while applying plugin schemes and recover malformed local scheme data with a retained backup.
- Apply imported plugin selections after downloads complete during gameplay, and base running-server cleanup on its original launch settings when edits await a restart.
- Keep the bundled Gateway configuration within its reviewed schema while retaining optional directory metadata for client adapters.
- Pin automatic candidate deadlines to the immutable local grant deadline so clock-reading precision cannot make the bundled Gateway reject an otherwise healthy pool.

## 0.1.43 (2026-11-01)

- Supply optional directory names to the CN2011 native Center title adapter in the Chinese authentication component. Bind names by native selector identity, preserve native titles when names are absent, and retain the original native selection and redirect protocol.

## 0.1.42 (2026-11-01)

- Anchor launch admission and heartbeat leases to local receipt and elapsed time, so server clock skew does not exclude healthy workers. Repeated observations cannot renew an old lease.
- Preserve every configured manual Center entry. Unreachable destinations retain their original names as disabled native status entries; Automatic can still select a single healthy admitted destination. Remove fixed directory-count caps and size the Gateway control channel for the actual admission.
- Show installed plugin badges beside names, version transitions and updates first in the store. Check for updates in the background and show the distinct plugin count on Store navigation and the Updates tab. Distinguish author names from download-source names.
- Place progress below the information card for single-item downloads and installation dialogs; keep per-item progress within cards only when several items are shown.
- Record Automatic candidate eligibility changes by native center identity and fixed failure reason, without tickets, account details or transport error text. Derive the unavailable native status capacity from the authorized automatic pool.
- Include the client wire build in bounded Gateway lifecycle diagnostics so reports distinguish native protocol profiles without account or ticket data.

## 0.1.41 (2026-11-01)

- Update the bundled Gateway to retire Automatic selection before closing its native connection, preventing a concurrent health refresh from observing an active selection during teardown. Native handoff and server-controlled admission remain unchanged.

## 0.1.40 (2026-11-01)

- Preserve the complete configured manual server directory during Automatic selection without expanding its authorized candidate pool. Independent server authentication still requires server-side support.
- Add persistent connection-cleanup error notices with View details, Copy details and Open log actions, including bounded sanitized launch records without requiring a diagnostic plugin.

## 0.1.39 (2026-10-31)

- Keep Center disabled unless the server explicitly enables it; an enabled Center defaults to Gateway, while explicit native mode retains the server's own Center. Ordinary servers need no directory API. Keep Automatic limited to the server-authorized pool.
- Reserve launch-scoped Gateway start and stop interfaces for future Nirinsou Link integration, without enabling remote control handlers.

- Add native automatic selection with issuer-bound admission, expiring player-side measurements and Gateway refresh control, without an automatic-selector Hook.

- Discover plugin updates across enabled stores, preserve verified pending activations after source changes, and keep package hash validation independent of download origin.
- Show plugin update notices in closable InfoBars aligned with the first text line; distinguish built-in, update, required and unavailable badges by color.
- Project ordinary native server directories onto distinct physical backends without selector Hooks; prevent unavailable aliases from borrowing an available node's resolved address.
- Use the original chooser and native redirect protocol for Gateway Center login. Remove CN2012 selector rendering/click Hooks and their presentation metadata producer, retaining its necessary native authentication repair.
- Allow the audited CN2012 native LoginStage to request its existing credentials once more after a real connection switch, with image, callback, stage and fresh-login guards; other provider calls retain one-use behavior.
- Show plugin patch preparation inline below the launch controls, with measured progress, elapsed time, expandable file details and cancellation.
- Match sandboxed plugin settings to the Launcher's current light or dark theme.
- Stop the local selector Gateway after verified native handoff, independently of the game process lifetime.
- Report cleanup failures by stage with localized recovery guidance, while retaining original resource backups.


## 0.1.38 (2026-10-30)

- Bundle verified x64 and ARM64 Gateway binaries for native selection between independent server endpoints; keep selector aliases inactive until their native consumers are installed.
- Normalize client identities to region and wire version, add the evidenced cn-408 profile and omit unidentified historical builds.
- Repair missing optional plugin selections before launch and support declared former plugin identifiers while retaining compatible settings.
- Require updates from deprecated Launcher versions, retain the transitional feed format and accept additive future manifest fields.
- Preserve running-client monitoring independently of page navigation and retain the selected server endpoint for native authentication during Gateway bootstrap.

- Keep server API transport controlled by the existing HTTPS switch, disabled by default. An advertised secure address no longer overrides an HTTP selection.
- Migrate retained bundled Launcher update policies to the current archive after upgrade, preserving staged transfers and accepted revisions. Custom and signed sources retain their authority.

## 0.1.37 (2026-10-30)

- Move the bundled server and archive source to the new service domains. Migrate the retired bundled endpoints while retaining installed clients and plugin settings.
- Add optional IP-first HTTP access to store and Launcher update source editors. The bundled archive and server enable it by default; HTTPS retains hostname validation.


## 0.1.36 (2026-10-29)

- Add recoverable game resource overrides with measured preparation progress, verified cache reuse, original-file restoration after interrupted runs, and physical-archive diagnostics in Developer mode.
- Support per-character Original, New and Classic model preferences through a package-owned settings page and a game-module capability for future resource providers.
- Upgrade existing compatible selections after importing a newer local plugin, retaining settings and disabled state and safely deferring running clients across Launcher restarts.
- Keep mutually exclusive resource packages from applying together without removing their saved settings or installed library versions.
- Retry transient Windows source-file replacement locks without discarding the previous saved source.
- Automatically ask whether to apply a downloaded and verified Launcher update; Later keeps it ready without repeated prompts.
- Keep cached store results interactive during background refresh, show inline loading status, and animate catalog changes.
- Keep launch preparation and pending plugin checks alive when navigating away from Home; stop hidden Home media without cancelling the launch.

- Repair Korean authentication component 0.1.4 packaging by including its required native-login profile. Reject incomplete authentication packages before store publication.

## 0.1.35 (2026-10-29)

- Show plugin updates as badges with the installed and target versions beside the plugin name. Update directly beside the enable switch, with a loading ring and compact measured download progress, size, speed and remaining time.
- Animate version changes, badges, update actions and plugin reordering. List compatible plugins first, dim incompatible entries, and retain independent settings and uninstall actions with consistent button placement.
- Disable accidental touch zoom gestures in the Launcher workspace.

## 0.1.34 (2026-10-28)

- Keep plugin installation and version dialogs dismissible, show transfer progress inline, confirm whole-plugin removal, select updates without enabling disabled plugins, and add default-on cleanup of older releases.

- Fix startup update discovery racing restoration of a previously selected transfer.
- Isolate invalid installed plugin packages and repair source-bound releases through automatic or manual verified updates.
- Import local plugin packages into the shared library without selecting a client.
- Reuse ready single-player sessions through one selector entry; honor name-only login, prompt for a missing nickname and expose startup failure details.
- Keep local server setup dismissible during startup, follow the latest log output without resetting on status refresh, and show rejected packages in InfoBars with source-bound update and confirmed uninstall actions.
- Restore navigation after leaving local hosting and avoid waiting for the previous remote server's catalog when connecting to a ready local invocation.


## 0.1.33 (2026-10-27)

- 2026-10-27: Start plugin configuration frames at their minimum height so the first content measurement does not undo an oversized loading placeholder.

- 2026-10-27: Prevent transient iframe scrollbars while content-sized plugin settings expand; keep list and control edges aligned and move the Resolution Extension note below the controls.

- 2026-10-27: Let plugins declare their exact native and HTML settings pages and option membership; a single declared page opens directly without duplicate tabs.

- 2026-10-27: Refine plugin settings with keyboard-accessible tabs, content fades
  and smooth dialog height changes; custom pages can report their natural height.
- 2026-10-27: Fix custom plugin settings appearing empty when reactive option definitions
  prevent initialization messages from reaching the sandboxed HTML page.

- 2026-10-27: Replace the Launcher's game/build plugin rules with the same typed
  `clientPolicy` used by servers. Manually rewrite all 23 existing rule rows with
  exact build scopes. Remove package activation selectors, legacy build
  required/recommended lists and the old recommendation bridge, without aliases
  or automatic migration. Installation and launch share version/source constraints;
  native mandatory failures use only the managed session's policy decision.

- 2026-10-27: Servers can select `launcher_login: game` so players sign in on the
  game's own login screen after the Launcher starts it. Runtime 3.7.0 passes the
  launch metadata to the Messenger stand-in, which performs the existing sign-in
  and launch requests and receives the game ticket and Messenger access. Nirinsou
  Link connects to that same launch after sign-in. Native-client acceptance is pending.
- 2026-10-27: Add a per-server Use HTTPS switch, off by default. Requests after
  discovery use the server's advertised `secureUrl` first, otherwise the entered
  address upgraded to HTTPS when enabled, otherwise the entered address unchanged.
- 2026-10-27: Reduce Messenger wait time by using the friend list returned with a
  change, applying presence updates locally, opening chat from its reply and
  processing chat in a separate lane. Sent chat lines appear immediately without
  repeating the server echo. Native-client responsiveness still needs acceptance testing.
- Servers now send a client policy instead of plugin recommendations: per plugin,
  rules that require, recommend or disable it and set a minimum version, a maximum
  version or the latest version, each for chosen client builds. The Launcher
  enforces them at launch; the required-update dialog switches a plugin to the
  highest allowed version, downloading it from the player's added stores when it is
  not installed. A recommendation is shown once per rule. The server pages look the
  same. Launchers before this release cannot read the new policy.
- Single play and rooms save to a local SQLite file unless another storage is chosen.
- Removed caps nobody asked for: password, server name, preset, label and release
  lengths, and the recommendation record's size.
- Plugin installation and update dialogs now stay open with measured download,
  verification and installation progress, including local imports and retry actions.
  Client download lookup failures retain their underlying diagnostics behind View details.
- Single-player starts a compatible local server with default settings and connects
  the selected installed client without a setup wizard. New private and room profiles
  default to name-only login with optional account authentication. Local hosting
  reads the registered client's resources directly, preserves legacy gate settings,
  and configures modern relay listeners. Name grants require runtime 3.6.0 and the
  updated authentication components; ticket login remains available.

- Diagnostic mode (crash recorder 1.2.0) now explains clients that close themselves. It records
  C++ exceptions with their type and message, keeps the newest exception even after the record
  limit is reached, and when the game exits writes that exception and the exiting thread's stack.
  Clients with their own crash reporter, such as Taiwan 2019, previously left no trace. The plugin
  is now offered for China 2019 and Taiwan 2019, and keeps up to 128 records by default.

## 0.1.32 (2026-10-26)

- The in-game messenger no longer freezes the game. The game waits on every messenger call,
  so the stand-in now answers from a cached friend list and game table and runs every
  request to the server on a background thread; changes reach the game through the same
  refresh events the original service posted.

- Fantasy Tale Nirinsou Link connects for servers that run it: the launcher uses the
  grant that comes with each launch, asks the server for a fresh ticket of the same launch
  on every reconnect, and accepts plain `ws` only when the server's launcher API is plain
  http. A launch without a grant, or with a malformed one, plays without the channel. The
  communication plugin is now named "Fantasy Tale Nirinsou Link" in every language.

- The launcher shows "The server is not ready yet" when a server refuses sign-in or a
  launch with `server_not_ready` before it is ready, instead of a generic
  request failure.

## 0.1.31 (2026-10-26)

- Friend chat in the messenger stand-in: double-clicking a friend opens the native
  chat window, lines go both ways, and a friend's window opens by itself when a
  line arrives. Members carry their real presence, so the window only announces a
  friend as offline when they are.
- Reject malformed chat blobs and mismatched session replies, and report refused
  sends or failed session closure instead of acknowledging them as successful.
- A plugin that is switched on for a client but whose package is missing, for
  example after a download that never finished, is offered for download again
  when the client starts, instead of failing the launch with "service selections
  must resolve to exactly one installed package".
- The messenger stand-in knows every messenger function and event id the clients
  define, by the client's own class name, and its log names each call, including
  the ones it does not answer yet (such as opening a chat).
- Every enabled Bubble Fighter build now runs its messenger on the stand-in:
  CN2011, CN2013, CN2014, CN2015, CN2019 and KR1256 join KR2015, ID2014, ID2015,
  CN2012 and TW2019. Only the disabled CN2016 build keeps forwarding.
- Native builds are deterministic (`/Brepro`, trimmed source paths), so a rebuilt
  payload keeps the SHA-256 its plugin manifest publishes.

- The messenger stand-in passes the game code from the client's initialization
  call to the server at sign-in, and the server shows every friend, group and
  request under that code. Clients of different regions (CN2012, TW2019, ID2014)
  no longer filter accepted friends out of their lists.
- Handle CN2012's Passport Messenger sign-in with the server-issued Messenger
  ticket, including the callback that opens the friend window. Publisher passport
  contents are never used as server credentials.
- Preserve a client's required Launcher sign-in when a server advertises optional
  login. CN2012 explicitly uses the GameWidget ticket bridge alongside the
  Messenger stand-in, so missing credentials are caught before starting the game.

## 0.1.30 (2026-10-25)

- Game Control Support 0.3.1 adds statically verified cn-774 and tw-454 native UI
  descriptors, with reusable chat, compact announcement and global-clock
  conventions selected by TOML. Retail-game acceptance remains open.
- Include the Messenger integration and previous native UI ports in the x64
  self-contained portable distribution.

## 0.1.29 (2026-10-25)

- Add the Game Component Test developer page and native message boxes with button
  results, multiline notifications, expiring announcements and colored local chat.
- Add statically verified native game-control descriptors for cn-706, cn-404,
  cn-406 and cn-735 alongside kr-812, kr-1256, id-400, id-1809 and cn-717.
  Per-build adaptation remains TOML data; retail acceptance beyond kr-812 remains open.

- Native runtime 3.5.0 reads an optional `[messenger]` session section with a
  transport, URL and ticket; only the `http` transport is accepted so far.
- Carry the server's messenger sign-in from the launch reply into the session
  of builds whose messenger runs on the launcher's stand-in, after validating it.
- The Bubble Fighter messenger stand-in answers the native friend list from the
  server's messenger service: list, requests, categories, blocking, level, game
  names and change events delivered to the client window. The kr-812 author
  acceptance is recorded in PR #3; two-computer acceptance remains open.
- CN2012 now uses the messenger stand-in instead of forwarding to the original
  library; the stand-in also answers its older calling interface.

## 0.1.28 (2026-10-25)

- Add GitHub mirror settings with dynamic source discovery, concurrent speed tests,
  automatic ranking and transfer fallback. Offer a one-time mainland-China
  recommendation with a user-controlled switch.
- Align connection and developer expanders, simplify Overlay test options and
  show a blue Built-in badge for bundled plugins.
- Ship exactly five built-in plugins and explicitly enable communication, Overlay,
  Game Control Support and Native Style Components by default. Keep diagnostics
  off, preserve manual choices and enable newly supported defaults after upgrades.
- Support independent store upgrades for bundled plugins and newer bundled
  versions without downgrading newer store packages. Move optional packages to
  the store, recommend KR1256 shop access and keep Forward Jump unpublished.
- Rename Chinese font support and the native UI components. Reserve the native
  provider boundary; game-native hook implementation remains a separate task.
- Restore cached client selection and account connection feedback during startup;
  expose all twelve enabled local-hosting builds.

## 0.1.27 (2026-10-25)

- Add account record removal to the account picker with a character-name
  confirmation and protection against accidental deletion across server changes.
- Include startup plugin update discovery, cached required-update confirmations
  with live progress, and the three Overlay themes in the x64 distribution.

## 0.1.51 (2026-11-03)

- Show connection rings while restoring server accounts and loading sign-in
  information, prevent duplicate login prompts, and retain valid account details
  during background refresh. Manual sign-in also shows a blue busy button.

- Restore cached games, versions and installed clients before host discovery;
  allow selecting and launching immediately while background refresh preserves
  the current choice. Cold startup shows loading instead of a fictitious default
  version or an incorrect Add client action. Process-status errors no longer
  discard a successful client inventory.

- Honor GitHub's quota reset and retry deadlines across store sources while
  retaining pinned server releases and refreshing ordinary catalog resources.
- Identify running game images before their module loader initializes, so an
  unrelated same-named process does not block stopped-client recovery. Retain
  the recovery guard for matching or uninspectable live processes.
- Remove consumed installation-cache objects after a durable commit, while
  preserving verified retry payloads after rollback and unrelated pending files.
- Restore spacing between developer settings cards.

- Discover plugin updates during startup using the shared store request, download
  automatically when enabled, and replace launch-time source checks with cached
  required-update confirmation cards and live transfer progress.

- Integrate the v3 Overlay component theme for Bubble Fighter clients dated
  2017-02-07 onward, with shared extension-settings events and reproducible Vue
  source. Retain the approved v1/v2 text appearance and normal font weight.

- Follow GitHub server releases from an existing store catalog, pin and verify selected archives, and discover nested client/server installation roots from configured executables. Show an uncreated-character label instead of substituting a login identifier.


- Reveal the startup workspace after local game definitions load. Inspect saved
  clients, load server accounts and network preferences, and synchronize stores
  independently in the background, without delaying the splash transition.
  Preserve restored client selections and choices made while inspection runs.

- Bind a queued game launch to the account selected when the request arrives.
  Reject selection changes during catalog, plugin or metadata preparation before
  requesting a ticket, while allowing normal renewal of the same session.

- Show routine account and server rejections without technical-detail actions.
  Preserve maintenance explanations and the update shortcut, while retaining
  copyable details for network failures and unexpected server errors.

- Allow the external launcher updater to transfer ownership when a valid release
  is selected from another configured HTTP or AList source. The exact previous
  receipt remains pinned, product identity and owned-file hashes are rechecked,
  and rollback restores the previous source receipt. Ordinary client installs
  retain strict source ownership checks.
- Treat an optional update record removed during an open retry as absent, avoiding
  spurious file-not-found failures while the helper publishes lifecycle changes.
  Persistent sharing conflicts and malformed records remain errors.

- Add a local developer Overlay startup test and configurable outside-click
  forwarding. The test is off by default; its message box lets outside clicks
  reach the game by default while retaining dialog controls and drag capture.
  Report component readiness only after the native resource surface attaches,
  with explicit resource errors instead of an empty successful mount.
- Retain game activation while clicking an attached Overlay in windowed, borderless
  and fullscreen modes, avoiding display resets caused by activating the dialog.
  Message boxes and dialogs also dismiss on button actions when mounted directly.

- Restore and copy Overlay from the active build output, including isolated
  artifacts directories, instead of reusing files from a previous local build.
  Distribution checks also validate the updater and Overlay's private copies of
  shared Launcher assemblies.

- Use one repository version for ordinary builds and release packages. Reject
  inconsistent build overrides and verify shipped component product versions
  against the release label before generating a feed.

- Add a client removal dialog with the shared package card and separate file
  cleanup, registration-only and cancel actions. Cleanup rejects unsafe paths,
  links, active clients and unfinished transactions, deletes only hash-matching
  installation-listed files through locked Windows handles, and preserves all
  other files. Registration-only removal supports adding managed clients again.
- Fade and collapse Home's completed update notice after the corresponding client
  reaches readiness once, preserving download history and unrelated transfers.

- Keep published plugin entries together before client profiles. Appending a new
  plugin after profiles previously made the current TOML model reader omit earlier
  package entries. Restore all 11 clients, six plugin identities and the server
  entry without changing package contents, versions or profile definitions.

- Fix KR1256 authentication initialization after the runtime credential handoff:
  publish component 0.1.2 using the entry-time API instead of an embedded session
  parser, and align managed runtime compatibility with native 3.4.0.
- Show launch preparation immediately with a progress ring, retain process state
  separately from plugin failures, and ignore stale stop confirmations without
  terminating a replacement process.
- Consolidate duplicate registrations of the same physical client while archiving
  their metadata. Share physical installation leases, recover verified stopped
  deployments before launch, and move recovery and plugin inventory off startup.
- Gate client diagnostics on enabled plugin capabilities. Share plugin downloads
  across compatible clients and use package cards for server downloads.
- Connect signed HTTP/AList downloads to the external updater in process tests,
  covering offline-ready restoration, health-confirmed commit, failed-health
  rollback and retained-signature tampering before handoff.

- Fix store inventory requests without optional client filters. Align plugin
  version selectors, switches and trailing settings icons in stable columns, and hide the
  empty update summary and source guidance from the plugin library.

- Return the remaining server accounts when automatic renewal removes a rejected
  selected session, without suppressing service, discovery or compatibility errors.

- Keep renewable accounts visible after access-token expiry and Launcher restart,
  including inactive accounts on the same server. Account snapshots expose only
  renewal deadlines; selecting or signing out an account retains the other sessions.

- Select ordinary plugin upgrades supported by the running Launcher, retaining
  eligible intermediate versions when a newer package requires a newer Launcher.
  Latest-only components still require their newest release and report the required
  Launcher version before requesting package contents.

- Preserve each launcher update source's accepted revision and index hash across
  fallback and restarts, so returning to a source still rejects outdated or
  rewritten releases while respecting configured source priority.

- Merge the accepted CN2011 native timer into the generic Auto Dash 0.2.0
  package. Retire the separate preview catalog entry; CN2011 uses the same
  `bubblefighter.fantasy.auto-dash` plugin identity as its other supported builds.

- Accept freshly issued launch tickets independently of the player's clock. The
  server validates expiry when redeeming the ticket; account and ticket-content
  checks remain enforced, with specific errors for invalid responses.

- Publish Korean client authentication component 0.1.1 for KR1256 only, with
  mandatory enabled/latest-only policy and build defaults. Preserve the native
  DLL accepted with the original packed client; prepare the passport before the
  native login stage instead of submitting through its callback.

- Keep launcher self-update polling collapsed until the user opens it. Background
  discovery and restored ready tasks update the badge and progress without
  expanding a 100% completion panel. HTTP/AList staging, external replacement,
  health acknowledgement and rollback remain unchanged; a publisher public key
  and signed manifest format are still required before signature enforcement can
  be enabled.

- Reject the CN2011 Forward Jump capability prototype for ordinary movement:
  gameplay also allows jumping from a roll, matching an equipment effect.
  Retain its research evidence, keep it unpublished, and continue investigating
  the ordinary newer-client input path. The declarative CN2011 patch stays disabled.
  Follow-up testing shows the capability is conditional and does not itself
  convert a held W key into a reliable jump trigger.

- Refine the local CN2011 Forward Jump candidate to include the running movement
  state (`101/102/103`) while keeping roll state `104` excluded. Add native
  regression coverage for the running state; the candidate remains unpublished
  and requires real-client verification.

- Extend the local CN2011 Forward Jump candidate to the separate running state
  (`301`). When W/Up plus Space/Num0 is held, temporarily bypass the missing
  `bubbleJumpEnable` gate at `sub_7D6980`, then restore the original branch as
  soon as the request or running state ends. The remaining native checks stay
  active; the candidate remains unpublished pending gameplay verification.

- Synchronize an existing Bubble Fighter login.ini with the selected serverAddr
  and serverPort before launch. Preserve unrelated settings and encoding; do not
  create missing files or rewrite matching endpoints.
- Update the isolated CN2011 Auto Dash preview to 0.1.1: follow game-window
  recreation and use direct window-thread dispatch instead of filtered queued
  messages. Add bounded callback diagnostics; gameplay acceptance remains pending.

- Find existing clients inside enclosing release folders, reject missing or
  ambiguous executables before registration, and reuse existing registrations
  on retry. Correct earlier enclosing-folder registrations without moving files.
- Allow store plugins to download to the shared library before adding a client.
  Keep progress and restart recovery in the normal queue, leave plugins disabled,
  and explain unavailable game, plugin capability and build requirements.

- Publish an isolated CN2011 Auto Dash preview for x64/ARM64 comparison. It uses
  the native forward timer on the game window thread without rewriting game
  instructions. Login works locally; dash activation remains unverified. The
  preview does not replace the regular plugin or enable itself automatically.

- Temporarily exclude CN2011 from Auto Dash and Forward Jump after isolated
  packed-client runs reproduced pre-login crashes with either code patch.
  Restore disabled movement selections for the affected local test client;
  keep authentication enabled. The underlying runtime compatibility issue is
  still under investigation.

- Add the optional CN2011 `Forward Jump` plugin for build `cn-279`. It removes
  the verified jump-eligibility fallback gate while preserving the client's
  native input, state, scene, cooldown and action checks. Offline image and
  catalog validation pass; real-client acceptance is still pending.

- Add the required, latest-only Korean authentication component for KR2026 only. Validate native image signatures, select the server login endpoint, and hand the complete ticket to the native login callback after startup and UI initialization. Retail acceptance and store publication remain pending. Record the executable path and PE timestamp for launch diagnosis without credential or argument contents.

- Expand the optional Auto Dash plugin to verified build-scoped branches for KR2015, CN2011-CN2016 and ID2014-ID2015 under one plugin identity. One forward press requests the native running transition while the original movement restrictions remain. KR2026 is excluded because it has native support; modern inlined timer paths await separate verification. Gameplay acceptance is pending.

- Automatically recover a stopped client's leftover launch session after the launcher restarts. Live clients, locked files and uncertain recovery results remain protected and continue to show the explicit recovery action.

- Explain update-check failures in plain language: connection timeouts, unreachable or refusing servers, unavailable or inconsistent update information, and local file access failures. Keep HTTP status and stable reason codes in collapsed details; never expose server replies or private URLs. Preserve AList rejection codes instead of treating access errors as malformed metadata.

- Keep Home update details collapsed until explicitly opened. Dismiss shell error notifications after six seconds, pausing while hovered or focused. Do not replay recommendation prompts for restored download history, avoid locking already acknowledged recommendations, and release the client lease before publishing download completion.

- Keep plugin listings readable while another client operation owns the mutation lease. Separate actual full-disk failures from file sharing and permission errors; translate instance-lock conflicts instead of exposing raw instance identifiers.

- Retain readable historical store components when a newer bundled component changes file retention; enforce the new policy for current and future releases. Add CN2019 launch arguments and its existing ticket bridge, and select the ticket-aware messenger for TW2019. Keep actual retail startup acceptance separate from registration checks.
- Do not reopen completed download history on Home after restarting the launcher; retain completion feedback for transfers observed in the current session and keep all records on Downloads.

- Keep Chinese authentication files installed after exit; skip destination writes when the latest component size/SHA-256 already matches. Preserve temporary Hook deployment and rollback incomplete installations.

- Standardize Traditional Chinese source UI and catalog wording: client regions
  use 中國/韓國/台灣/印度尼西亞, plugins use 模組 and binary modules use 程式模組.
  Retain 泡泡大亂鬥 and the untranslated Bubble Fighter Fantasy server name;
  keep Simplified Chinese labels and internal identifiers unchanged.

- Keep room multiplayer and local hosting entries visible but disabled for the first release, with a development notice. Server browsing and connections remain available; reversible game declarations also guard backend operations.

- Add reversible build availability and visibility switches. Temporarily disable
  and hide CN2016 (`cn-735`) while retaining its identity and installed clients;
  block operations on disabled builds and omit their download/preset entries.

- Verify large client archive uploads against each validated local object's size,
  removing the stale 2 GiB remote verification limit without increasing limits
  for ordinary packages, indexes or launcher portable archives.

- Publish Overlay into the portable distribution with a matching architecture
  and its own WindowsDesktop runtime. Validate renderer assets, native runtime,
  WebView2 loader and archive decoder before indexing. Official releases target
  x64; ARM64 builds remain available for maintainer testing.

- Add one client archive transport path for ZIP, 7z and RAR packages, including
  ordered split volumes, per-volume resume and SHA-256 checks, bounded extraction,
  optional publisher-provided passwords and an interactive password retry. File
  transport remains available for incremental updates.

- Expose optional companion communication to native plugins through a sized ABI
  tail and a separate process-authenticated broker. Validate manifest grants on
  both sides, enforce bounded owner queues, discard canceled-epoch events and
  share the existing SDK session lifetime without requiring Overlay. Add runtime-only
  packaging to preserve published plugin payloads during development builds.

- Allow independent native plugins to use Overlay concurrently through up to 64
  launch-bound broker connections. Isolate malformed peers, recover slots after
  disconnect and wait for game-process binding before authenticating early clients.

- Add optional launcher-owned service packages for companion communication and its
  separate Overlay notification consumer. Validate provider/type/schema grants and
  per-client settings, exclude service rows from native sessions, and manage their
  lifetime beside running clients. Missing production authorization remains a
  credential-free unavailable state; no socket opens until that integration exists.

- Add an isolated experimental companion client with owner-scoped SDK channels,
  bounded UTF-8 WebSocket framing, negotiated capabilities, request correlation,
  heartbeats and fresh-grant reconnect without replaying old requests. Add a separate
  structured notification consumer for local game-style Overlay dialogs. Server
  production authorization/catalog integration remains pending; normal
  game startup does not open the channel yet.

- Retain credential-free companion initialization failures for the running client,
  complete later service cleanup even after an earlier release fails, and reflect
  notification delivery failures or lost dependencies in diagnostics. Verify old
  notification dialogs and late actions cannot cross a reconnect.

- Complete the optional overlay foundation: event-driven hidden startup, one-page
  multi-panel rendering, deterministic modal layers, game-style and custom HTML APIs,
  bounded idle suspension, draggable dialogs, backdrop/sound/localization placeholders,
  DPI/resolution tracking, build capability profiles and reloadable UI-resource anchors.
- Add bounded XML/JSON adapter parsing for normalized `.ui` layouts with design
  viewports, visibility, nested controls and explicit validation. Proprietary binary
  resource decoding and per-build native/image locators remain adapter TODOs.

- Defer overlay browser creation until the real game render window is visible and arranged, and flush queued panel commands only after the page listener reports readiness.
- Use the overlay window DPI for legacy client coordinates, hide the browser surface during minimization, and update foreground z-order only on transitions rather than repeatedly raising the overlay.
- Recognize the CN2011/CN2012 GameWidget login interface in the shared ticket bridge without changing credentials or callback layout. Request explicit Windows elevation through build definitions instead of elevating every launch.
- Track replacement graphical game windows and physical client bounds in the optional overlay, retaining the launch-bound process identity and hiding content over separate splash windows. Repair z-order above the game when it gains focus, allow other applications to cover it, and hide it with minimized games without stealing focus at startup.

- Automatically register enabled HTML overlay package entries, with a separate optional game UI preview. External overlays no longer require injected-hook readiness; bound startup waits and terminate renderers after failed handshakes.

- Keep client-scoped plugin updates and recommended downloads in the Home progress area. Resume the requested launch after verified installation only while the original client, server and account remain selected; prevent duplicate preparation requests.
- Resolve latest-only bundled components through their declared store before launch, preserving source policy and installed-byte verification. Add an explicit native UI overlay preview with external resource mapping, provider-backed component mounting and DPI-aware bounds.

- Report the packaged Launcher SemVer in API headers and authentication/launch JSON. Enforce server and plugin minimum versions with the shared update dialog, without signing out accounts; retain public catalog discovery and refresh-only logout for outdated clients.
- Merge build-specific catalog and login/refresh plugin recommendations. Required plugins are checked before tickets and again under the client lease; verified remote recommendations activate through configured stores without persisting server locks globally. Preserve existing task-reader shapes with a separate activation sidecar.
- Persist valid rotated refresh credentials before validating policy extensions, and reject stale or malformed policy without silently losing the session. Update the PKCS dependency to 10.0.13 to address dependency advisories.

- Add a Fluent circular update entry and separate download/installation confirmations. Download progress shows percentage, speed and ETA; its top-right X, background button and backdrop hide it without stopping downloads. Confirmed installation restarts the application and cannot be dismissed during handoff.
- Show live download progress as a determinate arc in the sidebar button, preserving the arc when paused. Transition to a spinning ring only for verification and installation; the download tooltip includes its percentage.
- Check for updates on every healthy startup; the saved check preference controls further thirty-minute checks. Manual discovery retains the same prompts. Bind installation to the exact ready update and task; downloads alone never authorize restart. Confirmed installation may stop owned clients and local servers after readiness validation, while failed preparation leaves normal launching available.

## 0.1.13 — 2026-10-11

- Distinguish unreachable update discovery from a failed verified newer release. Only the latter offers manual updating; successful discovery clears its own connection notice without hiding unrelated errors.
- Hide the home local-save footnote while retaining its template as a comment. Simplify client/plugin page descriptions and remove the plugin update explanatory hint; verified upgrades still automatically replace the unchanged enabled selection.
- Rename Chinese font weight fix, Shop access, Remove file verification and Health value display, and refresh ESC/diagnostic descriptions in all six UI languages. Publish new font 1.0.3, ESC 0.1.4 and health 0.2.11 metadata without replacing historical store packages. Health uses the accepted 0.2.9 DLL and unchanged options/patches; no rendering behavior changes in this release.

## 0.1.12 — 2026-10-11

- Lock mandatory recommendations checked in the installation prompt and keep cancellation from starting the game.
- Add source-bound latest-only components, verified automatic downloads and a pre-launch update gate; enable this policy for the Chinese authentication component.
- Switch a client's unchanged enabled older plugin to a successfully downloaded newer version, preserving compatible settings and durable pause/restart intent. Other clients and later user choices remain unchanged.
- Add a shared animated WinUI checkbox to sign-in and recommendation dialogs; remembered passwords remain opt-in.
- Use Fantasy Tale Studio for team attribution, publish font-weight 1.0.2 and ESC 0.1.3 without rewriting immutable historical packages, and retain preferences when retiring a bundled store-managed version.

- Add launcher-side file replacement plugins with hashed build variants, folder/ZIP/store imports, collision rejection and recoverable post-exit restoration.
- Bundle the Chinese client authentication component, selecting GameWidget or sdologinentry by build alongside the GPK stand-in. Existing clients acquire its default selection once; explicit disable choices require per-launch confirmation.
- Keep plugin-controlled login files out of the module's unconditional overlay, including when the user confirms disabling the component. File packages never enter the native hook session. Future authentication hooks retain the same identity; no additional hook is activated in this implementation.
- Validate the file workflow with real bridge calls and controlled child processes. Retail login with this newly packaged component remains unaccepted.

## 0.1.11 — 2026-10-11

- Add cn-404 identification, data-defined launch arguments and existing managed-ticket login bridge. Authorized desktop login reaches the existing character; original files are restored after exit.
- Publish the x64 portable release through identical canonical and retained preview schema-one feeds. The unchanged 0.1.5-preview.1 reader stages the release and restores readiness offline without explicit retries.

# Plugin management

Manual plugin enable, disable, version and settings changes are permitted while a game is running. They save the next launch configuration and show an informational InfoBar; the running session and its deployed files retain their original snapshot. Uninstall immediately removes the managed selection and library listing, retaining payloads until client leases are released. The retirement marker survives restart, and reinstalling an identical verified package can restore it without modifying live bytes.

Shared plugin notices support info, success, warning and error severities. Missing or disabled startup components appear as individual package cards. Enable and continue installs missing packages and enables the components before continuing; Start anyway confirms this launch only, and Cancel stops it. Failed or incomplete transfers never authorize launch. Automatic update completion refreshes the shared inventory before showing a success InfoBar with the old and new versions. Home and navigation update badges read the same inventory. Scheme tracking follows activated versions and retains subsequent enabled-state and option edits.

Plugin update settings include an automatic installation switch, enabled by default
and persisted in `client-updates.toml` as `auto_update_plugins`. When background
plugin checks are enabled, discovery queues verified upgrades in the shared library.
Running clients retain their current selection; activation is persisted separately
and retried after exit under the client lease. Concurrent clients share one release
transfer, and a verified installed package can be reused without downloading it
again. Turning automatic installation off leaves discovery and manual updates
available. Downloads retain the original selection stamp so later user changes
are not overwritten.

The plugin page shows pending activations and safe source, integrity or storage
failures. File replacement plugins are deployed from the newly selected package
by the normal preparation of the next game launch; running game files are not
overwritten by shared-library updates.

Starting a game with optional plugin transfers still pending opens a Fluent dialog
listing those plugins. Choose to wait and launch, launch with complete installed
versions, or cancel. Changing the client, server or account cancels the prompt and
any queued launch. New pending tasks require confirmation again. Required plugin,
minimum launcher and latest-version checks still run on the host before launch.
The store's multi-client checkbox selection also permits running clients; their
new selections are deferred until their leases are released.

Ordinary store browsing reuses the short-lived shared catalog snapshot. The store
refresh button and plugin update checks retrieve current metadata; concurrent
requests to the same source still share one in-flight retrieval. A failed refresh
retains the last validated snapshot and reports the failure instead of treating
the cached release as proof that no update exists.

[Chinese](plugin-management.zh.md)

Settings includes a persistent Developer mode switch, off by default. Enabling it
reveals Developer options, Store-source management and Resource override tests tabs. The optional work-mode
switch (`nsfw`, also off by default) hides Home video and artwork. Store-source
management in this developer page remains reserved for future implementation.

Store installation can select several compatible clients in one dialog. The
download task stores one verified plugin payload in the shared library and keeps
the client activation targets in a sidecar; activation occurs only after
the payload is installed and each target is revalidated.
Targets may use different supported builds of the same game. Each target's
selection is captured when the task is queued and survives restart; a later
change to that selection skips activation without discarding the verified shared
package or changing the other targets.

Launcher-side file replacements are also supported; see [File replacement plugins](file-plugins.md). They are not loaded by FtHook. Imported packages cannot declare `required_on` or `warn_on`.

Open Plugins and choose **Import plugin**. No installed or selected client is
required. Select a folder containing `plugin.toml`, or a ZIP with that file at its
root or inside one enclosing folder. The dialog verifies the package and shows
its name, version, file count and size before adding it to the shared game library.
Compatibility determines which clients can enable it. A new plugin remains
unselected. Importing a higher version automatically upgrades existing compatible
selections of the same plugin while preserving their enabled state and settings.

Imports go into the current user's plugin library, separate from the launcher
installation and game files. Turn a new plugin on for each client that should use
it. A local upgrade for a running client waits until it exits, survives a Launcher
restart and verifies the imported bytes again before switching. A later selection
change cancels that pending switch. Each client pins a specific version; selecting
another version asks for confirmation and retains compatible shared settings by
default; the confirmation can explicitly choose the new version's defaults.
Other clients retain their selections, and old versions remain installed.
Running clients cannot change their plugin configuration.

Packages may declare `plugin.exclusive_with` identities when their effects cannot
be combined. Explicitly enabling either member disables the optional conflicting
selection while retaining its version and settings. Required components cannot
be displaced. Importing a previously unselected package does not perform this
switch, and invalid configurations with both enabled are rejected before launch.

Browsing the library requires no client. Cards are grouped by game and plugin
identity; releases remain selectable within each card. Update hints belong only
to the selected client's game, and activation resolves an exact game/id/version
so identical plugin IDs in different games cannot select each other's releases.

Store plugins can also be downloaded before adding a compatible client. Choose
**Download / install**, then **Download to plugin library**. The normal download
queue handles progress, pause, retry and restart recovery; completed packages
remain in the shared library without enabling them. After adding a compatible
client, select the downloaded version on its Plugins page. If a release cannot
be used, the store explains whether the game, plugin capability or required
client version is unavailable.

The package format is documented in [Hook plugin format](hook/plugin-format.md).
Both declarative patches and native x86 DLL packages are supported. Importing
reads package data and PE headers without loading or executing the DLL. The
runtime still checks the plugin ABI, export, patch sites and readiness at launch;
successful import is not proof that the plugin works in a real game.

## Plugin configuration pages

Every plugin can declare settings in `plugin.toml`; the launcher validates their
type, range and enum values before writing the per-client selection. Enum values
may provide localized captions in an optional `plugin-ui.toml` sidecar. A package
may also declare a small `plugin-ui.toml` HTML page. The page is shown in a
sandboxed iframe, has no network or launcher bridge access, and exchanges only a
bounded schema-one options message. The launcher validates every returned value
against the manifest before saving it. Packages without the sidecar use the
standard launcher form.

A package can declare its exact navigation in a `[[pages]]` array in
`plugin-ui.toml`. Each page has a unique `id`, a `kind` of `native` or `html`,
a localized `title` containing `en`, and an explicit `options` list referencing
keys from `plugin.toml`. HTML pages additionally provide a packaged `path`;
native pages use the launcher controls for their listed options and have no path.
The declared order is the displayed order. A single page has no tabs, and the
launcher does not add a generated form to an explicit page list. For example:

```toml
schema = 1

[[pages]]
id = "resolutions"
kind = "html"
path = "settings.html"
options = ["custom_resolutions"]
title = { en = "Custom resolutions", "zh-CN" = "自定义分辨率" }
```

This declaration belongs to a plugin that declares `custom_resolutions`; it is
not a launcher rule for plugins with HTML. Do not combine `[[pages]]` with
`[ui].page`. Missing files, invalid page kinds, duplicate IDs and undeclared
option keys reject the package. HTML initialization and returned changes are
limited to that page's declared options. The host still validates the complete
draft against the plugin manifest when saving.

Settings can be saved before enabling a plugin and belong to the selected client,
not to the installed package. Cancel discards the editor draft. Changes take effect on the
next game start; they are not a live reload.

For an opt-in color preview, copy the files from
[the configuration example](examples/plugin-configuration/settings.html) into
a plugin package that declares the `font_color` enum (`white`, `yellow`), and
include its [sidecar](examples/plugin-configuration/plugin-ui.toml). This example
is not added to distributed plugins automatically. Publish a new package version
when adding it to an existing immutable package.

The HTML file must be valid UTF-8. Use inline CSS/scripts and
embedded data images: relative assets, network requests, forms, navigation to the
launcher and direct bridge calls are unavailable. Listen for parent messages
before posting `{ type: 'fantasytale:ready', schema: 1 }`. The parent replies with
`fantasytale:configuration`, `schema: 1`, `language`, `options` and `values`, and
resends current values after edits or language changes. Check `event.source ===
parent`. Post `fantasytale:change`, `schema: 1` and a `values` object containing
only changed manifest keys. This updates the draft; only the launcher's Save
button persists it. Unknown keys and invalid values reject the whole message.
Do not echo initialization back as a change, which would create a message loop.

An optional `fantasytale:resize` message with `schema: 1` and a positive safe-integer
`height` reports the page's intrinsic content height in CSS pixels. Observe a
natural-size content wrapper, not the iframe viewport, to avoid resize loops.
A page reporting intrinsic heights is fitted to its content and delegates scrolling
to the surrounding dialog, avoiding transient iframe scrollbars during expansion.
Pages without height reports retain their viewport and ordinary scrolling. The
launcher animates editor height changes. Use one dialog title; custom pages need no repeated heading.

## Installation rules

An import is first copied into a private staging directory. Validation checks
the game, selected build, runtime range, manifest options and patch definitions.
Native packages must supply a valid unmanaged x86 Windows DLL at the declared
path. Commit rechecks the entire staged file list and SHA-256 digests against
the preview, then moves the complete directory into the library atomically.
Canceling discards staging. Abandoned staging is cleaned on startup; installed
directories are never partially replaced.

User-confirmed imports may install components explicitly listed as store-managed
when they retain the game or bundled component's declared `update_source`.
Other reserved component identities remain protected. A local copy of a bundled
version must match its complete snapshot; a newer component must retain its
restoration policy. Local imports do not acquire a store-origin receipt merely
by declaring an update channel. Imported packages cannot declare `required_on`: importing a
package must not impose new requirements on existing clients. Game/build `client_policy_rules` own mandatory startup and version requirements;
packages cannot declare activation rules. Bundled components may declare managed
component metadata through `update_source`; an update cannot change that metadata
or profile policy. Any enabled store may serve a validated release of the component.
The normal preview, integrity checks and confirmation apply to component ZIPs too.

Versions use SemVer. The same game/id/version cannot be imported again or
overwritten silently; publish modified contents under a new plugin version.
This rule applies to plugin packages, not to the optional game-file update
labels. Importing a new plugin version never changes the game's original build
number. Store file packages now install into this same library through the shared
download queue; see [Store sources](store-sources.md). A successfully downloaded newer release replaces this client's enabled older selection, retaining compatible settings.

## Checking and applying updates

Select a client and choose **Check for updates**. The launcher searches every enabled
store for compatible releases of installed plugin identities, including imported
packages and managed components. Changing or removing the original download source
does not prevent another enabled source from providing an update. Equivalent mirror
offers share the same manifest digest; conflicting contents at the newest version
are reported instead of choosing an arbitrary package.

Check completion, pending activations and discovery failures use closable InfoBars.
Closing a notice keeps it dismissed during background polling; an explicit check can
show the current result again. Built-in badges are blue, available updates green,
required plugins amber, and unavailable plugins gray.

The highest newer SemVer release advertised for the selected build whose minimum
Launcher version is satisfied by the running application is offered. A newer release
that needs a newer Launcher does not hide an eligible intermediate upgrade. If no
eligible newer release exists, the installed version remains current for this
Launcher. Latest-only components still require the source's latest release for
their build and report a Launcher update requirement instead of using an older one.
Stable versions stay on stable releases; an installed prerelease may advance to
another prerelease or a stable version. Build metadata alone is not an upgrade, and
ambiguous equal-precedence releases are reported as conflicts. Package validation still checks actual build/runtime
compatibility when the bytes arrive; the catalog is not proof of a working hook.

**Download** rechecks enabled sources, the installed baseline, selected version and
manifest digest before using the existing HTTP/AList queue. File and ZIP packages
share verification, staging and immutable installation. View progress,
pause, resume and retry in Downloads. The old version remains installed and selected
throughout download and failure. After completion, an enabled older selection
automatically switches to the verified newer package with compatible option overrides retained. Disabled plugins and choices changed during the download are retained. Other clients keep their pinned versions; running sessions retain their startup selection while saved changes wait for the next launch.

Checks allow one operation at a time and fetch each source catalog once per check;
installed versions have no fixed count ceiling. Changing clients discards the previous view's
results. Downloads remain owned by the backend when the page is closed. Update
checks do not hot-reload native code. The `clientPolicy` `latest` rule also checks before launch and handles required updates.

## Limits and storage

Packages, ZIP input and the imported library have no fixed file-count, entry-count,
version-count or byte ceiling. Paths must be relative,
Windows-safe and at most 240 characters; traversal, links, reparse points,
device names, case aliases, file/directory conflicts and launcher metadata paths
are rejected. A failed import leaves existing clients and versions untouched.

The default library is `%LOCALAPPDATA%\FantasyTale\Launcher\plugins`. Installed
packages live under `packages/<identity-hash>/`, and one pending import lives
under `staging/<token>/`. Both are local storage details rather than plugin IDs.
With `--data-root`, the library follows that root. Do not edit staged or installed
package files while the launcher is using them. There is no uninstall UI yet;
retained versions can still be referenced by another client.

## Build recommendations

Servers may add build-specific recommendations through the public catalog and
login/refresh envelopes described in [Launcher account sessions](launcher-auth.md).
The selected destination's required items remain checked and locked, are merged
with local build recommendations, and cannot be omitted by a modified dialog.
Installed disabled packages can be enabled directly; missing packages use configured
stores and the existing verified download queue. An unavailable identity blocks
launch with an error rather than fetching code from an authentication response.

Required selections are checked before ticket creation, after session refresh,
and again under the client lease before module preparation. A required hook failure
is fatal for that launch. The destination's requirement does not persist a global
`Required` flag on the client or affect another server. Games without hook support
cannot bypass this gate. Recommendation downloads retain their bound selection
intent across restart; later user edits still prevent automatic activation.

Server activation uses a separate `.server-recommendation` sidecar. Existing
schema-one task JSON and `.recommendation` sidecars retain their exact shapes,
so older task readers remain usable. Older launchers ignore the new sidecar and
cannot automatically activate a remote-only recommendation without their own
local build declaration.

Acknowledgements are scoped to individual declared plugin identities, so a later
definition can add a new recommendation without repeating earlier ones. A server
that disables hook plugins suppresses optional recommendations; mandatory file components remain available. Activation sidecars retain deferred
selection checks without adding keys to schema-one download history; older launcher
readers can continue to read those task records.

A build may declare an optional `recommended` plugin list in its own `[plugins]` table. The launcher shows the recommendation dialog only when that list is nonempty. It does not use a game-wide or shared hard-coded list. An optional plugin that is already installed and enabled is omitted; mandatory recommendations are checked and locked even when enabled by defaults; an installed but disabled plugin is offered for activation without downloading it again.

For a newly installed client, the dialog appears after the client is ready. For an existing client it appears before the first launch that discovers a missing or disabled recommendation. The acknowledgement is stored beside that client, so dismissing the dialog does not make it return on every launch. Confirmed store downloads carry an activation intent in the durable download record and enable the plugin only after the package has passed its manifest and byte checks. A changed client selection cancels that activation instead of overwriting the user's later choice. An empty declaration creates no acknowledgement and no dialog.

A launcher release that retires a bundled store-managed version migrates a missing selection to a newer compatible bundled package under the client lease, preserving its enabled state and compatible options. An installed historical package remains selectable; other identities cannot trigger this migration.


Developer mode includes Resource override tests. Select a client to inspect the
latest prepared replacements, their contributing plugins and virtual member edits,
and whether each physical BFZ/PKN currently contains the original or prepared bytes.
The view reads files without applying patches. Graphical publishing and authenticated
AList connections remain separate planned capabilities.

Updates select the verified newer release by default while retaining each client's enabled state and compatible options. Settings includes **Uninstall old plugins after updating**, enabled by default. Cleanup waits for idle clients, migrates compatible selections, and removes superseded managed packages only when no client still needs them. Turning the setting off retains old releases. Bundled application files and releases still required by an unsupported client are retained.

[Client policy](client-policy.md)


Invalid installed packages are rejected individually. Their manifest or settings
errors appear separately with a View details action, while healthy packages stay
available. Rejected manifests are never converted into executable definitions.
Warnings use the shared InfoBar. A rejected package with a known identity and version
exposes Check for updates, which becomes Update now when a compatible replacement
is available. Plugin cards offer a card-based uninstall confirmation.
Confirmed uninstall removes every managed version of that plugin and its client
selections. Running clients retain their live payload until exit; other active file operations block removal before any selection changes.
Only verified library paths are removed; client files remain outside this operation.
Installation dialogs remain dismissible while work continues in the shared queue.
Transfers display measured progress, byte totals and speed; preparation uses an
indeterminate bar until a task supplies progress.
Automatic checks can discover a newer compatible release using the rejected
package's validated identity across enabled stores. Verified upgrades retain
compatible options and enabled selections; disabled selections remain disabled.
After no client references a rejected release, a verified newer release from the
configured store allows moving the original bytes outside the active package index
into the library's `rejected` folder for diagnosis.

Every `publish_plugins.py` publication also creates an importable ZIP at
`FantasyTale/plugins/<game>/<plugin>-<version>.zip`, with
`plugin.toml` at its root. The catalog's optional `plugin_archive` table pins
`source_path`, `size` and `sha256`. The download queue prefers this ZIP, verifies
it, and checks every extracted member against the original immutable release
manifest. Missing ZIPs (HTTP 404 or 410) fall back to individual file downloads;
permission, network and integrity failures remain explicit errors. Paused tasks
retain their original manifest and verified staged content across restart.

To publish a clean package folder:

```sh
python tools/publish-store/publish_plugins.py --package ./plugin-package --output ./publication --base-url https://files.example.org/
```

Set `FT_ALIST_USERNAME` and `FT_ALIST_PASSWORD` in the publisher process. ZIPs
and immutable objects are anonymously verified before the catalog is committed.
Use a new plugin version for changed content. Never publish working folders,
session files or private recovery archives.

For existing packages, `backfill_plugin_archives.py --base-url ... --output ...`
prepares ZIPs from their published, hash-verified objects. Add `--publish` to
upload and verify them, then add only `plugin_archive` metadata to the catalog.
It covers every published plugin version, preserves existing manifests and
unrelated catalog data, and rejects catalog changes during publication.

Publishers may still use `publish_plugins.py --archive` to distribute a package as one
deterministic ZIP with streamed creation, avoiding one request per virtual member.
The release manifest pins the complete ZIP; installation verifies it and the plugin
library retains a snapshot of every extracted file. Retiring predecessor model
entries requires the consolidated replacement to be published first. Verify its
anonymous download and installation before requesting retirement; other catalog
entries remain unchanged.

## Required components after an update

Plugin upgrades retain the previous enabled state and supported option overrides.
A current profile requirement keeps its component enabled and adds an already
installed compatible package to the client automatically. If an old selected
version is absent but a newer compatible installed version exists within the
profile's version limits, the launcher repairs the selection before presenting
missing-component warnings. An intact explicitly pinned version remains selected.
Completed deferred upgrades are applied before startup component checks; changes
to policy-derived required flags do not invalidate a pending user's selection.
Actual changes to version, options or enablement still invalidate stale requests.
Missing downloads remain subject to the installation progress and error flow.

Renamed packages show their current identity even when neither release supports the
selected client. A former identity remains visible only for a client that can use
it and has no eligible installed successor. Package files still needed by another
client or running game remain available.

Automatic recovery checks all registered clients that reference the same failed
release. Each client first attempts a compatible installed replacement; unresolved
references are removed. The recovery dialog reports the release once, and removes
that exact ID and version from every saved scheme and build, including schemes
not currently open. Other versions and unresolved imports remain pinned. Dismissing
the dialog also clears its pending notification.

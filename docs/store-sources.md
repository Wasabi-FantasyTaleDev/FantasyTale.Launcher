# Store sources

[Chinese](store-sources.zh.md)

For the unified catalog contract and a publishing walkthrough, see
[Self-hosted store reference](self-hosted-store.md).

The Store separates clients and plugins and keeps release details beside the
resource list. Only the list and detail body scroll; the title, filters and
action area remain visible. Manage sources supports adding, editing, disabling
and removing sources. The catalog-location field is an optional expander.

Server packages may follow GitHub Releases while the parent catalog stays on AList or HTTP. See [the subscription contract](self-hosted-store.md#follow-server-releases-from-github). Archive installation discovers executable roots instead of requiring fixed wrapper folder names.

## Discoverable server profiles

Schema-two catalogs can advertise persistent destinations under `[[servers]]`:

```toml
[[servers]]
id = "Community_Server"
kind = "server"
game = "bubblefighter"
name = "Community"
api_url = "https://game.example/"
host = "game.example"
port = 10101
builds = ["kr-812"]
profile = "servers/Community_Server.toml"
```

`profile` is optional and relative to the source root. AList, HTTP and GitHub use
one download abstraction. Listing, opening details and favoriting read catalog
metadata only. An explicit save/selection downloads the declared profile and imports
it using the same conflict checks as a local file. If no profile path is declared,
the listing is converted into the same local TOML contract. Profile game, ID, API
and advertised endpoint must agree with the selected listing.

The profile is a complete `schema = 1` document with `[server]` containing `id`,
`game`, `name`, optional `api_url`, paired `host`/`port`, `builds`,
`bundled_instances` and optional `prefer_ip`. Optional `[presentation]` contains
`summary`, `motd`, icons, gallery images, visibility and status data. Its
`summary_translations`, `motd_translations`, `icon_url_translations` and
`images_translations` tables keep language variants. `[[client_policy_sources]]`
and `[[client_policy_rules]]` retain the server's client policy. The
[Multiplayer guide](multiplayer.md) describes identity, filenames and overwrite
confirmation. Remote profiles cannot reference another user's local artwork or
claim bundled-only `icon_asset` authority. The AList publication tool includes
explicitly referenced profile files automatically. Put them at their declared
paths under `--input`; paths are relative to that source root, even when the
catalog is nested. Unreferenced TOML files are not uploaded. A server-only catalog
does not need dummy packages.

Before uploading, validate with the Launcher's own reader:

```sh
dotnet run --project tools/publish-launcher -c Release -- --validate-catalog public/FantasyTale/source.toml --source-root public
```

This checks every referenced profile against the advertised identity, game, API
and game endpoint, using the same validation as explicit downloads. Without
`--source-root`, paths resolve from the catalog's directory. The uploader pins the
exact profile bytes, records existing public remote contents, then uploads and
anonymously verifies profiles before publishing the catalog. Local changes or
observed remote conflicts stop publication; all referenced metadata is checked
again before the catalog write. This does not change user-side behavior: browsing
still does not download or save profile files.

## Plugins from multiple sources

The source filter defaults to All sources. Enabled sources are merged in the same
order as the source list. A plugin with the same kind, game and package ID appears
once, with a source count when several sources provide it. A matching display name
alone does not identify the same plugin. Selecting an individual source restricts
the view to that source.

The installation dialog offers a Download source selector when multiple sources
provide the plugin. Its default is the first enabled source in list order, even
when a later source provides a newer version. Each source keeps its own releases,
compatibility information and manifest hashes. If versions differ, source choices
also show the offered version; the selected source's releases can be chosen
separately. Switching sources keeps the selected version when available and
otherwise selects that source's newest release. Incompatible target clients are
removed from the selection without automatically selecting replacement clients.

The Enable immediately after installation checkbox is selected by default.
Clearing it downloads only to the plugin library and leaves client selections
unchanged; selected clients are used for activation only while it is checked.

Opening the dialog snapshots its candidates. Confirmation pins the source's
transport, base URL and catalog path together with the release's version and
manifest hash. Changing a source endpoint requires refreshing and confirming the
choice again; installation never silently substitutes another source.

## Client installation choices

Client confirmation uses the same package summary cards as plugin confirmation.
Required and recommended plugins are offered before installing the client. Required
items stay checked; optional items may be declined. The plugin section is collapsed
initially and its list scrolls independently when expanded.

When the build declares additional language packages, a second initially collapsed
Display languages section offers multiple selections. The native language stays
checked. Choosing an additional language also installs its declared translation
package; a native-only build does not show this section. Displayed choices remain
bound to the confirmed build and server. Plugin downloads can run alongside client
downloads and defer activation until the client is available.

## Unified catalog preparation

`tools/publish-store/compose_catalog.py` combines an existing package catalog and
versioned client definitions into one schema-two document. Its configuration uses
`[[profiles]]` rows with `definition = "path/to/build.toml"` and `revision = 1`.
Paths are relative to the configuration file. Definitions carry their own game and
build identity, login policy, required plugins and recommendations. Existing package
rows and unmodified profiles are retained. A changed definition must advance its
revision; duplicate identities and rollback are rejected before output is written.

Run it with `--catalog existing-source.toml --config profiles.toml --output
unified-source.toml`. The output path must be new.
Composition is local preparation, not publication. Validate the resulting catalog
with `dotnet run --project tools/publish-launcher -c Release -- --validate-catalog
unified-source.toml` before uploading and choose the public paths explicitly.
Catalogs and file indexes have no fixed application size or row-count ceiling.
Current readable definitions are authoritative; historical test-reader limits do
not constrain catalogs or file indexes.

## Current implementation

When adding an existing client, selecting an enclosing release folder is allowed.
The launcher searches up to eight levels for one verified client root and records
that root. Multiple clients require selecting a more specific folder. Missing
executables do not create registrations. Adding the same installation again
reuses its existing registration; an older registration pointing at an enclosing
folder can be corrected after verifying its build, without moving game files.

Source settings persist as `<data-root>/sources/<id>.toml` in the desktop host.
The bundled Fantasy Tale source comes from `Stores/default-source.toml` and uses
`http://archive-cn.600819.xyz:5244/` with IP-first access enabled by default. This
option resolves the hostname and connects to an IP using that IP as the HTTP authority; each source can disable it in the source editor. Older bundled
entries using the previous hostname migrate to the new address and enable IP-first
access while retaining their enabled/disabled choice. A source is
independent of account servers and installed clients. Removing a source does not
remove installed files. Source names are labels, not authenticated identities.

The host reads public catalogs through either AList or HTTP. Browsing, grouping,
search and metadata validation use the same models. AList resolves a virtual path
using `POST /api/fs/get`; HTTP resolves that path beneath the configured base URL.
Both fetch metadata with GET, without depending on HEAD. AList API credentials
are never attached to the resolved storage request. The source-management UI
currently configures public sources; private-source credential storage is pending.

AList file resolution retries transient connection failures, request timeouts,
and HTTP or API codes 408, 429, 500, 502, 503 and 504. It makes at most four
attempts with 250, 500 and 1000 ms backoffs. Cancellation interrupts the backoff;
authentication rejection, missing files and invalid metadata remain errors.
Each attempt has a thirty-second deadline covering both response headers and the
metadata body, including when the shared file-transfer client has an infinite
HTTP timeout. Retries do not relax file-size or checksum verification.

The default path is `FantasyTale/source.toml`. An AList administrator can mount a
folder exposing `/FantasyTale/source.toml`, so users need only the base URL. The
advanced catalog-path setting supports existing layouts. HTTP servers and APIs
may serve the same TOML bytes at the same relative path. Temporary signed URLs
are resolved when requested and never persisted as package identities.

## Server-recommended client downloads

A preset in `GET v1/catalog` may include this optional object:

```json
"downloadSource": {
  "kind": "alist",
  "baseUrl": "https://downloads.example.invalid/",
  "catalogPath": "FantasyTale/source.toml",
  "packageId": "bubblefighter.cn-406"
}
```

The transport is `http` or `alist`; both use the same store catalog and installation
pipeline. The package must be a client for the server game and exactly the preset
build. One release may omit a version; multiple releases require release mode with
a unique highest positive revision. Source URLs cannot contain credentials, query
strings or fragments, and catalog paths cannot escape their root.

Discovery does not save a source or start a download. Add client shows the package
and source address, with an optional download switch. Confirmation pins the source
and manifest checksum; installation rereads the recommendation and rejects changes.
Explicit installation saves an ordinary source scoped to the server API and
distribution address, so later updates and restart recovery use the existing receipt.
A disabled or edited source is never silently restored. These recommendations cannot
configure launcher self-updates.

Without a recommendation, adding or importing a client retains its existing behavior.
An installation-start failure retains the created client; retry reviews the refreshed
recommendation and reuses that client rather than creating a duplicate.

## Catalog schema one

```toml
schema = 1

[catalog]
id = "community"
name = { en = "Community catalog", zh-CN = "社区资源" }

[[packages]]
id = "bubblefighter.cn-279"
kind = "client"
game = "bubblefighter"
builds = ["cn-279"]
version = "0.1.5.005"
name = { en = "China 2011", zh-CN = "国服 2011" }
description = { en = "A separate client installation." }
publisher = "Community"
size = 1024
manifest = "releases/cn-279/0.1.5.005/manifest.toml"
manifest_sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
```

Replace the example size and digest with actual release values. A catalog may
contain multiple releases of a package, ordered newest first by its publisher.
Package identity is `(local source id, package id)`; another source cannot replace
it just by publishing the same id. Duplicate package/version pairs are rejected.
Paths are relative to the source root, not the catalog's parent directory.
They contain unescaped path segments, without traversal, URLs, backslashes,
percent escapes, query strings or fragments. Unicode and spaces are allowed.

Supported kinds are `client`, `plugin`, `runtime`, `launcher` and `server`. The Store UI
currently displays clients and plugins. A client release has exactly one build;
plugins may list compatible builds, with their own plugin definition remaining
authoritative. Client update labels are optional and independent of the original game build version;
plugin, runtime, server and launcher versions use SemVer. Every localized title includes
English. Descriptions are plain text, never executable markup.

Source lists, catalogs and AList resolution responses have no fixed count or byte
ceiling. Catalog loading uses four concurrent requests and a 30-second whole-document
deadline. Invalid schemas, hashes,
paths, duplicate releases and unknown keys fail explicitly.

## Download and update boundary

Client releases now support Download and install in the desktop Store. Choose
an existing matching client or a new installation path; a blank name uses the
build name and existing destinations require confirmation. Home and Downloads
show the same real tasks, with file/total progress, speed, ETA, pause, resume,
retry and cancel. Existing files absent from the manifest are retained.

Plugin packages now use the same queue, HTTP/AList transfer, SHA-256 validation,
restart history and progress UI. Choose an existing compatible client; the plugin
is installed into the user library without changing that client's selected or
enabled versions. ZIP delivery is supported; aria2 remains pending. Browsing alone
never installs or runs a package. An ordinary source cannot grant itself
launcher-update authority. A hash checks integrity against a catalog; it does not
authenticate a publisher. Launcher updates use a separate configured source and
external helper with health confirmation and rollback, without a signing-key
workflow; see [Launcher updates](launcher-updates.md).

The publication order is immutable files, immutable hashed release manifest,
then the catalog/channel reference. Both adapters use the same transfer and
transactional installer. AList resolves each file again on retry/resume; temporary
storage URLs are never persisted. Transport choice does not change installation.

## Client manifest schema one

```toml
schema = 1
[release]
id = "bubblefighter.cn-279"
game = "bubblefighter"
build = "cn-279"
version = "0.1.5.005"

[[files]]
path = "BubbleFighter.exe"
source_path = "releases/cn-279/0.1.5.005/BubbleFighter.exe"
size = 1024
sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
```

Replace the example length and digest. The catalog's `manifest_sha256` covers
the exact manifest bytes, including a BOM when present. Release identity and
total file sizes must match the selected catalog. `source_path` is relative to
the source root. `path` is relative to the client installation; device names,
traversal, case-insensitive collisions, file/directory collisions, reparse paths
and launcher session/metadata paths are rejected. A new installation must include the build's executable; an update may contain only resource files when the executable is already installed. Files are uncompressed; no remote scripts are executed.

Manifests, file lists, release sizes, backups and retained task records have no
fixed application ceilings. Two active transfers share the download queue. HTTP has a
30-second per-operation inactivity deadline, independent of the whole file's
duration. Range replies must match the requested offset and total; a full 200
reply restarts the partial. Full-file SHA-256 verification still runs after resume.

Verified content is staged under `.fantasytale-install` on the destination
volume. Before resolving a download URL, the queue rechecks an existing staged
file's size and SHA-256. Matching content can serve multiple destination paths
or a resumed task without another metadata request or transfer. A mismatch
resolves a fresh URL and downloads again; deleted, disabled or retargeted sources
still stop the task even when all content is already staged.
Installation holds the same client lease as launching or deleting it.
A durable journal precedes replacement; failures restore originals. A stopped
process's interrupted transaction is recovered when installation is retried.
External edits stop recovery and preserve backups. Launching is blocked while a
journal remains. Installation itself cannot be paused or canceled halfway through
replacement. Exiting waits for it to finish and cancels network work.

## Plugin file manifests

Plugins can be browsed before adding a client. The library shows one card per
game/plugin ID; its release selector retains older versions for rollback. One
plugin package can support several client builds through its compatibility and
per-build implementations. Without a client, download/import and activation
controls are disabled, but the library remains visible and searchable.

A plugin uses the same file rows, with `builds = ["cn-406"]` in `[release]`
instead of the client's single `build`. Game, plugin id, SemVer version, builds,
total size and exact manifest digest must match the catalog. The downloaded
`plugin.toml` must declare the same game/id/version and support the selected
client. The file list must include root `plugin.toml`. ZIP store releases and local ZIP import are supported.

Verified files are assembled in a task-owned workspace, checked again against the
entire manifest, then installed by the local plugin library's atomic version
commit. Bundled identities remain reserved; game/build `client_policy_rules` own activation requirements. The source
receipt is committed inside the same directory move. An exact source/manifest
retry after process loss succeeds only if installed bytes still match; another
source or altered contents cannot replace that version. Local-import previews
remain valid while a store installation commits. Clearing completed/canceled
tasks removes their private temporary workspace, preserving installed versions.

Installing a newer version is explicit. Changing an existing client's selected
version remains a separate confirmation in Plugins. Check updates discovers
compatible releases from each installed package's original source and revalidates
that source before downloading. Background discovery checks after frontend startup
and every thirty minutes, with an independent setting under Updates. It does not
download or activate plugins. Clients with the same game/build share one result;
the page reads cached results without starting network requests. Each pass checks the installed clients with a two-minute deadline. Foreground results fence older background
passes, and shutdown cancels/drains discovery before transports close. Bundled
plugins update with the launcher. No downloaded version is automatically enabled.

A failed game/build scope reports its own safe error category and retains its
last successful background result and check time. Other scopes continue checking.
Changing a client's build discards the old scope's cached results; a successful
subsequent check clears its error. The overall two-minute deadline still bounds
the pass.

Version-switch confirmation keeps shared option overrides by default. Keys removed
by the new plugin are omitted; remaining values must pass its current option
schema before the selection changes. An incompatible value leaves the old version
and its settings intact. Turn off Keep shared settings in the confirmation to
explicitly start the new version with its defaults instead. This affects only the
selected client; downloading a package never migrates or activates settings.

Public catalog and manifest reads retry transient connection failures and HTTP
408/429/500/502/503/504 at most three times. Each retry resolves fresh source
metadata without forwarding API credentials. All attempts share the same
thirty-second deadline. Access failures, invalid paths and
declared-size mismatches fail immediately rather than being retried.

Anonymous publication acceptance installs Numeric health and ESC mid-match exit
into an isolated library, verifies their manifest/file hashes and source receipts,
checks their color/mode options and repeats the same installation. Run against
the configured public AList source:

```powershell
$env:FT_TEST_PLUGIN_ALIST_URL = 'https://files.example.org/'
dotnet test FantasyTale.Launcher.slnx -c Release --filter PublicStorePluginTests
```

`FT_TEST_PLUGIN_ADDRESS` optionally pins only the source hostname for local DNS
troubleshooting. The test uses no account credentials and does not start the
launcher or a game, enable a plugin or execute native code. Successful delivery
does not establish retail-client behavior or compatibility with other builds.

The committed receipt records source and package identity, optional update label,
content snapshot digest and the configured endpoint fingerprint. Task records
persist atomically under `<data-root>/downloads` without a fixed count or byte
ceiling. Previously active tasks rejoin the two-worker queue after restart; manually
paused tasks stay paused. Each record retains the original verified index bytes,
never storage URLs or credentials. Changed destinations or removed, disabled or
retargeted sources block resumption. Damaged records are retained and reported.
An interrupted transaction recovers before downloading. Unused staged content is
pruned on a subsequent task; successful installation clears its staging files.

The development URL `#/store?storePreview=1` enables an in-memory sample catalog
for UI review only. Its sizes and package data are fixtures, not published
releases. Browser source edits stay in memory. Normal desktop browsing uses the
persisted host sources and actual catalogs.

## Automatic folder publication and file synchronization

Game build information, such as the original `0.1.5.005`, remains in the build
definition and never changes during file updates. Client update labels are
optional. The normal file-sync workflow needs neither a version bump nor a
manually maintained changed-file list.

Configure `tools/publish-store/publish.example.toml` once with each game's build,
input directory and display name. Keep the output outside the input directories.
Run the standard-library Python 3.11+ publisher:

```powershell
python tools/publish-store/publish_store.py --config D:/Store/publish.toml --watch 30
```

Then replace files in the configured input folders. The publisher scans them,
checks cached size/timestamps/file identity, and hashes/copies only new or changed
files. It automatically generates the store catalog and file index; operators do
not edit these generated documents. New content is stored once under its SHA-256
name, then the complete catalog is replaced atomically. Unchanged scans write no
new catalog. Publication failure retains the previous catalog. Old content stays
available for downloads already in flight. Publication has no artificial storage
quota; the former `storage_gib` setting is accepted for old configuration files
and no longer imposes a quota. Old snapshot cleanup is a deliberate maintenance action, not an automatic deletion of data
still needed by clients. `--full-check` bypasses the publisher's metadata cache.

Expose the output directory as the AList virtual root or an HTTP document root.
Consumers enter only the source URL: both adapters read `FantasyTale/source.toml`.
No custom store API or launcher rebuild is necessary. The scanner runs wherever
it can read the maintained game folders; AList provides storage and download
resolution. Cloud-only mounts require making the maintained input available to
the scanner or synchronizing its generated output into the mount. This tool does
not require every AList driver to expose file checksums.

Generated client entries use `update_mode = "files"` and have exactly one current
snapshot per package. `version` can be omitted in both catalog and file index;
when supplied, it is a display label and may remain unchanged as files change.
Snapshot digest changes trigger an update check, independent of the game version.
File removals from the server do not delete local files in this implementation.
Manually published immutable releases remain supported through `update_mode =
"release"` and optional increasing `revision` integers; they are an alternative,
not a requirement for folder publication.

The launcher initially validates unknown local files. It caches a successful
check together with size, creation time and last-write time; matching entries
are reused during later updates. A changed remote digest or local metadata forces
a new check. Only changed or missing files enter the actual transfer list and
its byte/ETA totals. Downloads are verified with SHA-256 before installation.
The metadata shortcut detects ordinary changes, not deliberate same-size edits
that preserve timestamps or silent corruption. Removing the client's
`.fantasytale-install/file-cache.json` forces full local checks on the next
installation attempt. SHA-256 is used for integrity; performance comes from
avoiding unnecessary reads, not from replacing it with MD5.

Settings > Client updates checks at startup and every 30 minutes by default.
Automatic download/install is separate and off by default; it skips running
games. Manual checking is available from Clients and Settings, with update
buttons on Home and client rows. Checks use the source/package recorded at
installation and never fall back to another source with a matching name. Changing
the source endpoint requires deliberately selecting the client in the Store
again. Older receipts without an endpoint fingerprint need that explicit binding
too. Locally imported clients are shown as local; no source is guessed for them.

## Archive delivery for clients

Client releases may use `format = "archive"` instead of the default
`format = "files"`. Server packages currently remain per-file releases. The transport is deliberately one logical archive mode;
`archive_format` selects its decoder and currently accepts `zip`, `7z` or `rar`.
The launcher uses the same private staging, SHA-256 verification and transactional
installation path for all three formats.

An archive can contain ordered volumes. Each volume is one `[[files]]` transport
entry with a one-based `volume` value. The launcher downloads and verifies every
volume independently, resumes individual volumes, and only opens the archive after
the complete contiguous set is present. ZIP split volumes, 7z `.001` sets and RAR
part sets are treated as the same logical archive; a missing, duplicate or mixed
set is rejected before extraction.

Client archive publication has no fixed volume-size or aggregate-size ceiling.
The uploader hashes anonymous downloads in fixed-size chunks and requires their
size to match the already verified local object; it does not trust a provider's
larger advertised size. Catalogs, ordinary file packages and launcher portable
archives likewise have no fixed publication size ceilings.

The optional `password` field supplies a publisher-provided archive password. It is
not copied into diagnostics or ordinary task snapshots; it remains present in the
publisher's release manifest and the installer's retained copy of that manifest.
This is a distribution convenience, not a way to keep a password secret. When it is absent and the
archive is encrypted, the launcher asks the user during installation; the entered
password is held only for the current retry. Archives are unencrypted by default.
Encrypted ZIP (including WinZip AES), 7z and RAR are accepted only when the selected
decoder can authenticate the archive; an incorrect password fails before any client
file is committed.

Extraction is performed in a private staging directory. Traversal, links,
special files, duplicate paths, case aliases and incomplete volumes are rejected.
Each extracted file must match its declared size and verified content identity. Extracted files become ordinary verified content objects and
then use the same backup, ownership, rollback and recovery journal as file releases.
The archive itself is never extracted directly over the client's live directory.

Expanded client archives, recovery journals and file-check caches have no fixed
file-count, directory-count or byte ceiling. Available storage and the platform
representation of file sizes and collections still apply. Literal
asset names may contain `#` or `%`; these characters are not URL escapes in an
installation path. Transport source paths retain their stricter URL rules.

File mode remains the preferred mode for incremental updates because an archive
requires downloading its complete compressed object when any member changes.

## Plugin ZIP delivery

Plugin releases may use `[release] format = "zip"` with exactly one `[[files]]`
entry naming a `.zip` file. Its size and SHA-256 describe the compressed bytes;
`size` in the catalog is the same transfer size. The other release identity fields
remain `id`, `game`, `builds` and `version`. Omit `format` or use `files` to retain
the existing per-file format. Both HTTP and AList use the same contract and queue.

The archive contains `plugin.toml` at its root or in one enclosing folder. The
installer verifies transport bytes before extraction, then uses the local importer's
ZIP validation and immutable commit without fixed compressed-size, expanded-size
or entry-count ceilings. Links, traversal, device names
and ambiguous paths are rejected. Repeating a completed install requires the same
source receipt and identical extracted bytes. Import previews remain independent;
download and installation never enable a plugin or change client selections.

## Server package publication

The same folder publisher accepts optional `[[servers]]` rows alongside `[[clients]]`.
A server-only catalog is also supported. Configure `id`, `game`, `builds`, `version`,
`name` and `directory`; `exclude` is optional. `builds` names 1..128 distinct compatible
client builds, and `version` is a SemVer package label, independent of any retail
client version. The generated catalog uses `kind = "server"`, `update_mode = "release"`
and an immutable content-hashed manifest. Server manifests use `[release] builds`
rather than the client-only `build`, and contain the same `[[files]]` entries
and per-file SHA-256 checks as client manifests. ZIP transport is reserved for plugins.

```toml
[[servers]]
id = "bubblefighter.server"
game = "bubblefighter"
builds = ["cn-406", "kr-1256"]
version = "2.25.0"
name = "Bubble Fighter server"
directory = "./server-package"
```

The publisher reuses its content objects and metadata cache, so unchanged server
files are not reread on each scan. Its default server exclusions cover local patch
overrides, environment files, common SQLite/database files, logs and private-key
containers. These filename exclusions cannot detect secrets embedded in other
configuration files: publish a clean distribution containing placeholders, not a
live deployment or player-save directory. Add deployment-specific exclusions when needed.

The Store's Servers category installs packages through the same persistent download
queue as clients and plugins, with file/total progress, pause, retry and source binding.
Packages are stored under `<data-root>/server-packages/`, scoped to the local source id,
endpoint, package id and manifest digest. New releases have separate directories;
existing hosting profiles keep their selected release. Completed package records survive
clearing download history and launcher restart. Incomplete transactions cannot appear
in the installed-package selector. The library has no fixed release-directory count ceiling.

Local hosting's setup dialog lists installed packages compatible with the selected
game/build and retains manual folder selection. Installation and hosting use the same
exclusive package lock: downloading an already-running release fails without changing
it and may be retried after stopping the server. Installing a different release does
not replace the running directory. Player saves remain in the separate hosting data
root. Browsing or installing a package never starts a server automatically. The full
af95d321 server distribution passed loopback HTTP and AList protocol-fixture download,
installation, per-file integrity comparison, startup and SQLite account restart checks.
Desktop interaction and the official remote AList deployment remain separate acceptance;
the official catalog still needs a published server package.

## Uploading a generated catalog to AList

After generating a clean publication, install Python `requests` and run:

```powershell
# Set FT_ALIST_USERNAME and FT_ALIST_PASSWORD in this process's environment.
python tools/publish-store/upload_alist.py --input ./public --base-url https://files.example.org/
```

Credentials are read only from the environment. The tool uploads only the catalog's
referenced manifests and file objects, never unrelated local files or publisher caches.
It verifies local hashes first, uploads content before manifests, and verifies anonymous
download bytes through AList's API. Existing immutable content must match. The catalog
is replaced last, only if its previous public digest has not changed during upload;
the final anonymous catalog is rechecked. A catalog-change check is optimistic and
does not replace administrator coordination for simultaneous publishers. Upload/API
failure is an error, not an empty successful publication. Partial content may remain
for a later retry, while the existing catalog remains selected. Configure a writable
AList storage driver; authentication alone does not prove that its upload path works.
The uploader normally respects environment proxies. If a local proxy cannot reach
the API or storage host, `--no-proxy` connects directly for this upload process,
including anonymous content verification. It does not change system proxy settings
or bypass authentication, hash checks or the catalog-last publication order.

Anonymous verification rejects invalid metadata sizes and stops reading when the
response exceeds its advertised size. `--verify-timeout-seconds` sets the total
streaming verification budget per object (30–3600 seconds, default 900); a blocked
read is separately limited to 120 seconds. This prevents a trickling response from
keeping publication alive indefinitely. Large objects on slow links may require
a larger budget. Exhausted transport retries leave the catalog unpublished.

`--workers` accepts 1–4 (default 2). Content transfers are bounded and finish before
manifest transfers; use 1 for a provider that limits concurrent upload sessions.
The optional real-source acceptance test reads `FT_TEST_ALIST_URL` and
`FT_TEST_SERVER_PACKAGE`, downloads anonymously, compares the complete distribution
and checks local SQLite/account/ticket lifecycle without opening a game window.

On 2026-10-09, this test passed against the configured public AList and the
complete d66cc86a server distribution (376 files). It verified every installed
file, registration and wrong-password rejection, launch-ticket issuance,
directory join/withdrawal, graceful stop, persistent-account restart and a
package-directory replacement retaining the same local save. This establishes
backend provisioning and lifecycle acceptance; it does not establish real-client
gameplay, desktop updater replacement or remote-player connectivity.

## Plugin authors and uploader

Plugin packages carry `authors = ["Fantasy Tale Studio"]` separately from the
catalog publisher. The plugin publisher copies this list from `plugin.toml`.
All current bundled plugins use this author. An optional catalog field
`uploader = "Display name"` identifies the person who uploaded the package; it
defaults to empty and is hidden when unspecified. Leave it unspecified for
current releases. The publishing CLI accepts `--uploader` for future releases.
Neither field is an authenticated identity or changes installation permissions.
Existing immutable plugin payloads are not republished to add display metadata.

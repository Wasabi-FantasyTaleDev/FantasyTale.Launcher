# Launcher updates

[Chinese](launcher-updates.zh.md)

The deprecation transition has ended. Releases declare the inclusive `deprecated_max_version` boundary directly in the update index. Packaging does not generate a separate release-policy file, and update discovery and offline restoration read the verified index. Unknown additive fields remain optional; product, platform, paths and file integrity retain strict validation.

## Native update and rollback acceptance

The 2026-10-24 verification used the current 0.1.25 native Windows ARM64 Host,
its copied external updater and two isolated installations. Both local HTTP feeds
required detached signatures from temporary test keys; those private keys were
discarded after fixture publication. Product sources still do not require signing
configuration unless the operator chooses it.

The healthy candidate changed one stylesheet. After verified staging, the source
returned HTTP 503. Restart still replaced that object, reopened the native Home
page and committed only after the new process acknowledged frontend readiness.
All 234 indexed files matched the selected manifest.

The failure candidate changed only the HTML entry point to a valid blank page
without the readiness bridge. The actual blank native window was observed. After
the existing 90-second startup deadline, the helper stopped the candidate,
restored the original HTML and reopened the working Home page. All 234 restored
files matched the independently reconstructed baseline. The rejected manifest
remained blocked from automatic retry.

Each scenario fetched one changed object. Source configuration and independent
local files survived; neither installation retained a journal or original-file
backups. Evidence is in the local
`artifacts/native-update-20261024/receipt.json`, with signed manifests, HTTP audit
and process lifecycle records beside it. This accepts the local native update
and rollback paths. It does not establish production key custody, a signed public
feed, x64 portable acceptance or real-client gameplay.

## Actionable completion states

Once an update is shown as Ready, Restart and Update can immediately use the
verified staging directory. Once a transfer is shown as Failed, Retry can immediately
start another attempt. Both task and discovery status are published together after
transfer, verification and persisted resume state are finished. Runtime Task
completion bookkeeping cannot briefly reject the action shown by the interface.
Cancellation keeps its task row until the transfer has stopped; application shutdown
still waits for the owned worker before releasing transport resources.

## Retrying an archive fallback

If incremental staging and its complete-archive fallback both fail, Retry starts
from the selected release's original changed-file plan. Archive-volume progress
rows never become the next attempt's update plan. Resuming a paused archive
attempt follows the same rule: incremental files are tried first, with the
complete archive used only if incremental transfer fails again. Verified resume
objects are retained and ordinary unchanged installed files stay out of the
incremental task list. A failed attempt cannot become ready by skipping payloads.

An isolated Windows x64 desktop run verified this path using HTTP 503 failures for
both incremental objects and the complete archive. After only the archive was
restored, the real Retry action attempted the original changed object again,
then downloaded the archive. Restart and Update installed the selected release
through the external helper and reopened Home with frontend health confirmation.
All 990 installed files matched the selected sizes and SHA-256 values, the local
source policy remained unchanged, and no installation journal or rejected-release
marker remained. The restarted frontend acknowledged readiness in 3,496 ms.
This was a loopback test with no signing configuration or production publication;
it does not establish public-source update acceptance.

## Readable update indexes

Update indexes prioritize human readability: blank lines separate file entries,
with spaces around assignment operators. Publication does not target older
readers or restrict indexes to their former 256 KiB ceiling. Canonical and alias
feeds receive the same UTF-8 bytes, and the recorded SHA-256 identifies those
exact bytes without platform newline conversion.

## Independent frontend releases

Build `web` with `npm run type-check` and `npm run build`, then run:

```powershell
python tools/publish-launcher/publish_frontend.py --baseline artifacts/accepted/public --frontend web/dist --output artifacts/frontend-release/public --backend-version 0.1.19 --revision 1792488000
```

The baseline must be the exact accepted backend distribution. The command verifies
every baseline object, retains all backend identities and replaces the entire
`wwwroot` file set. It does not rebuild .NET. The explicit backend version must
match the baseline; changes requiring new bridge methods need a new backend
baseline first. The complete resulting index also upgrades older installations
to that backend. Already matching files are reused locally, so only changed
frontend resources need downloading. Restart activates the complete snapshot;
obsolete hashed assets are removed transactionally and restored on rollback.

Publication runs in two independently retryable phases:

```powershell
python tools/publish-store/upload_alist.py --input artifacts/frontend-release/public --base-url https://files.example.org/ --catalog FantasyTale/launcher/win-x64/current.toml --phase incremental
python tools/publish-store/upload_alist.py --input artifacts/full-release/public --base-url https://files.example.org/ --catalog FantasyTale/launcher/win-x64/current.toml --phase archive
```

For a full release, both phases use the same public directory. Incremental
publication verifies immutable objects before making the feed available. Archive
publication requires that exact feed to be live and does not resend those
objects. A frontend release does not generate a portable archive; generate a
matching complete distribution if a new first-install archive is needed. The
default `all` runs feed publication first and the optional archive second.

Launcher updates use the existing HTTP/AList adapters and download engine. The
publisher scans a complete build directory and generates a file index. Operators
do not maintain file lists or keys. Game build versions are unrelated to launcher
release labels; a launcher label is optional too.

The About page reads the running host assembly version, not the update feed's
label. `Directory.Build.props` supplies the canonical version for ordinary builds
and release packages. To change it, edit that value first. The build and packaging
commands reject inconsistent overrides, and packaging verifies the actual managed
component versions before generating the feed. A newer label in Downloads does
not establish that the new executable is installed or running.

Discovery and update failures are distinct. If the feed cannot be reached or validated, the launcher reports that it could not check for updates. It offers the manual download page only after a newer release was verified and its staging or startup failed; an unknown latest version never triggers that dialog.

Settings > Updates > View details also shows the last unsuccessful external
installation for the selected source and exact release index. It includes the
recorded operation stage and hexadecimal HRESULT, when available; older helpers
may supply only the outcome. These cached diagnostics contain no exception text
or download URLs and can be copied from the existing dialog. Local storage
failures remain visible even after staging becomes ready to retry. A successful
installation, a different selected release or editing sources clears this view.

## Current index format and publication checks

`schema = 1` is the index format version. `version` is only a release label and
`revision` advances the published selection. Stable and preview release labels
use the same schema and default platform feed. `-SourcePrefix` changes immutable
object storage only; it does not change discovery. Retargeting a feed requires
an explicit `-FeedPath`, and existing installations preserve their source policy.

This product is unreleased. Human-readable TOML and complete file inventories
determine the current format; old test readers are not a compatibility target or
a publication gate. Preserve blank lines between file tables and spaces around
assignment operators. There is no fixed application ceiling on index bytes or
file count.

Publish every referenced content object before replacing discovery indexes.
Validate the current publisher and reader together: exact index bytes and hashes,
changed-file selection, verified staging, offline task restoration, external-helper
installation and frontend health confirmation. Exercise failed-transfer retry
and rollback separately. A staging-only receipt does not establish successful
installation. Superseded old-reader procedures are retained in the private
deprecated notes rather than imposed on release preparation.

## Source and publication

Official releases target Windows x64. ARM64 remains available for maintainer
testing with `-Platform win-arm64`; it is not a second official release target.
The distribution packaging entry point builds a portable launcher, verifies its
detached updater and complete payload, and generates the file index plus source configuration together:

```powershell
pwsh -File tools/package-launcher.ps1 -Output artifacts/launcher-release-next
```

The package includes a separate self-contained Overlay renderer with the same
architecture as the launcher, including its WindowsDesktop runtime, WebView2
loader and UI files. Users do not need a developer-installed .NET runtime for
either executable. WebView2 Runtime is still an external prerequisite. Packaging
also requires the architecture's native archive decoder and its license, and
rejects a missing renderer, mixed architectures or a local Overlay browser profile.
The verifier executes only the detached updater's no-argument entry point; it
does not open Overlay or establish real-game rendering acceptance.

Its default source is the official AList root at `http://archive-cn.600819.xyz:5244/`,
with feed `FantasyTale/launcher/<platform>/current.toml`. The `package/` directory
includes `launcher-update.toml`; its minimum revision equals this release's index
revision, preventing an initial installation from offering an older remote build.
The `public/` directory contains the uploadable feed and immutable objects, excluding
the source configuration. Uploading remains a separate step; successful packaging
does not establish that the remote feed is available. Use a fresh output directory.
Set the release version in `Directory.Build.props` before building. Local builds,
About, API requests and packages use that one value. A command-line build version
override is rejected; the optional packaging `-Version` asserts the same value.
Packaging checks every shipped managed component's actual product version before
creating the feed, so a stale or mixed build cannot be published.
The Host restores Overlay as a build dependency and resolves helper output paths
from MSBuild, including when `--artifacts-path` selects an isolated directory.
Run `python tools/test_host_helpers.py` to verify fresh restore, component labels
and copied helper/resource hashes without opening the Launcher.
Override `-BaseUrl`, `-Kind http`, `-SourcePrefix`, `-FeedPath`, `-Platform` or
`-Revision` for another deployment. `-FeedAliases` declares retained discovery
paths; the default canonical feed retains the previously distributed preview
entry point. Packaging writes identical indexes plus private publication metadata.
The AList uploader verifies and updates these aliases after the content graph.
The metadata is not part of the client index and does not change its schema.
No launcher or game window is opened by this
script. Cross-platform packages must run the detached helper verification on their
matching Windows architecture.

Packaging also creates a portable ZIP under the platform's `portable/` directory,
using the same verified file index and the distribution's source settings. The
uploader verifies this manual download before replacing the discovery indexes.
`-ManualDownloadUrl` overrides the download page for another distribution.
`launcher-download.toml` carries its own `schema = 1` and HTTP(S) `url`, independently
of the automatic feed. Automatic update failure offers this page in a dismissible
ContentDialog; it opens a browser only after the user chooses it. A detected version
is displayed when available. Failed discovery reports that the latest version
cannot be confirmed instead of inventing a release label. Dismissing one failure
does not reopen its dialog on every state poll.

Ship `launcher-update.toml` beside the launcher executable:

```toml
kind = "alist"
base_url = "https://files.example.org/"
```

Use `kind = "http"` for ordinary static hosting. By default the launcher reads
`launcher/<platform>/current.toml`, where platform is its process architecture:
`win-arm64`, `win-x64` or `win-x86`. An optional `feed_path` overrides this path.
For AList the base URL is the API root and paths are relative to its virtual root.
An optional `minimum_revision` records the initial distribution's baseline; it
defaults to zero. No update source is guessed from third-party game/plugin stores.

Put these two paths in a publication TOML; relative paths resolve beside that file:

```toml
input = "build"
output = "public"
```

Run from the repository:

```powershell
dotnet run --project tools/publish-launcher -c Release -- path/to/publish.toml
```

The input must contain the launcher executable, `wwwroot/index.html`, the updater
under `updater/`, and all runtime dependencies. Publish with the intended target
runtime. When cross-publishing, set `platform` in the publication TOML; otherwise
it defaults to the publisher's process architecture. Optional `version` is a display
label. Internal revisions use the publication timestamp automatically; `revision`
is available for CI systems that already supply a monotonic build number.

Publishing the desktop project also publishes the updater with the same runtime
identifier and self-contained setting. A portable distribution includes the
updater's own runtime because it is copied outside the application during updates.
It must not rely on the launcher's runtime directory or an installed .NET runtime.
Verify a portable Windows package without opening the launcher:

```powershell
dotnet publish src/FantasyTale.Launcher.Host -c Release -r win-x64 --self-contained true -o portable
python tools/verify-updater-package.py portable --platform win-x64
```

The verifier checks both executable architectures and the helper's runtime files,
and rejects local state, linked paths, browser profiles, local overrides, logs, dumps
and debug symbols anywhere in the input package before executing the helper. Normal
publishing retains PDBs only in local build output. This filename check is not a content
or media-permission audit; see [reviewed source exports](source-export.md).
It then starts a detached helper copy with external runtime lookup disabled. It tests
the no-argument entry point only; it does not replace application files. Both the
publisher and update reader require the launcher, frontend and updater executable
in every complete release. For framework-dependent distributions, pass
`--runtime-mode framework-dependent`; this verifies the installed-runtime mode and
executes the detached helper with the normal installed .NET runtime. Both modes
reject nested architecture build/publication folders inside the helper. Normal
Build copies exclude earlier RID outputs so cross-publishing does not turn a small
framework-dependent helper into an accidental second portable distribution.

The output contains immutable SHA-256 file objects and an atomically replaced
current index. Mount or synchronize the same output to AList or an HTTP directory.
Upload objects before replacing the current index. `source_prefix` and `feed_path`
can place the files under another virtual directory. Existing file objects are
reused. Source settings, debug symbols and installation journals are excluded;
local secrets and overrides cause publication to fail.

To sign the generated index, add `signature_path`, `signature_private_key_env` and
`signature_public_key_path` to the publication TOML. The environment variable
must contain a base64 PKCS#8 RSA private key. The publisher writes only the
detached signature and the public SubjectPublicKeyInfo; it never writes or logs
the private key. Copy the generated public key into the installed
`launcher-update.toml` as `signature_public_key` before enabling the signature
path for users.

For an explicitly signed publication, the publisher also writes
`.fantasytale-signature.json`. This local upload record binds the canonical index,
detached signature and public-key file to their exact SHA-256 digests. The AList
uploader checks those files before authentication, uploads and anonymously verifies
the signature artifacts before replacing the index, and refuses concurrent remote
changes or local mutations. Keep the record beside the generated output; it is not
uploaded as part of the release. Metadata paths, including their temporary files,
must be separate from each other and from immutable content objects. Reusing a
signed output directory for an unsigned release is rejected.

This remains optional publication support. Ordinary sources require no key setup,
and the uploaded public-key file never becomes a client's trust anchor automatically.
Clients verify signatures only against the public key already configured locally.

The generated launcher index can use the same AList upload tool as the store:

```powershell
# Set FT_ALIST_USERNAME and FT_ALIST_PASSWORD in the current environment.
python tools/publish-store/upload_alist.py --input ./public --base-url https://files.example.org/ --catalog launcher/win-x64/current.toml
```

Use the actual `feed_path` as `--catalog`. The uploader recognizes launcher file rows,
verifies content before publishing the mutable index and retries transient transport
errors with fresh download metadata. It never sends administrator authentication to
raw download hosts. A candidate feed may be kept separate from the production feed
until the external desktop update and health check are accepted.

The current index is ordinary TOML with product, platform, revision, optional
version, and generated file rows containing destination, source path, size and
SHA-256. Deployments that need signed discovery can add a detached RSA-SHA256
signature beside the index and configure both values in `launcher-update.toml`:

```toml
signature_path = "launcher/win-x64/current.toml.sig"
signature_public_key = "base64 SubjectPublicKeyInfo"
```

The signature covers the exact index bytes. The two settings are required
together; a bad key, missing signature or changed index is an integrity failure.
The verified detached signature is retained with the selected index. Staging,
offline transfer restoration and the detached installation helper verify those
saved bytes against the currently configured public key before using the index.
Enabling signatures or rotating the key therefore requires a newly authenticated
selection; a previously downloaded unsigned snapshot is not trusted implicitly.
An older signed snapshot without its retained signature needs a fresh update
check. Existing sources without signature settings continue to work unchanged.
Unsigned settings remain supported for existing deployments until the publisher
has provisioned its public key and signature files. Checksums still detect
incomplete or changed file transfers; the configured source determines which
release is offered.

## Download and restart

Settings > Updates separates launcher preferences from client preferences. Startup
and thirty-minute checks are enabled by default; automatic download is on by
default. Automatic downloading alone never restarts the launcher. Home and Downloads
share the real task, total/current-file progress, speed, ETA and pause/resume actions.
Paused transfers remain paused across restarts. Completed staging is reverified
locally and can resume without contacting the source when all objects are present.

Update status records are atomically replaced. A reader keeps a complete previous
snapshot while the helper publishes the next state, without blocking replacement.
The held-reader regression covers this boundary; it does not establish recovery
for unrelated external file locks or every power-loss timing.

Readers retry Windows sharing/lock conflicts for at most 200 ms during record
replacement. Persistent locks and other errors still fail; transient and
persistent lock tests cover both outcomes.

The navigation footer shows a circular download entry when a verified update is
available. Its tooltip identifies the update; selecting it opens the animated
WinUI confirmation dialog. Discovery alone does not open a download prompt.
The first dialog shows the installed and available versions and asks to download.
Cancel leaves the update entry available. Download progress shows
percentage, speed and ETA; its top-right X, background button, Escape and backdrop
only hide the dialog, leaving the transfer running. The sidebar shows a determinate
byte-progress arc until transfer finishes, then spins while verifying and ready.
Selecting it reopens the current stage. Verified staging opens a separate installation
confirmation automatically, including downloads completed in the background. Its
message explains that downloading has finished and asks whether to apply the update,
with a running-game interruption warning. Choose Update now or Later; Later retains
the staged update and the same manifest is not prompted again during this session.
The sidebar entry can reopen it manually. Only confirming installation
starts the external updater and restart; installation progress has no dismiss action
and ignores Escape and backdrop clicks. Settings and live download restart actions
use this same flow. Manual checks refresh the entry and version state; the user
opens the dialog from the entry when ready.

Every healthy frontend startup triggers discovery, including installations whose
periodic-check preference was previously disabled. That preference now controls
additional checks every thirty minutes. Automatic downloading remains optional and
never authorizes installation. Installation permission is bound to the exact selected
manifest and ready task. A changed selection, canceled transfer or failure invalidates
the earlier operation. A failed restart restores dismissal and requires another user
action; it does not retry automatically.

Closing the launcher with an uninstalled ready update preserves its selected
manifest and verified payloads. On the next startup they are checked locally and
the installation confirmation opens directly. No network download is repeated;
the ready selection can be restored even when the source is unavailable.

A confirmed automatic restart validates the selected hash and staged readiness
before stopping owned games and local servers. The legacy restart action without
this explicit permission continues to refuse running games. The host also blocks
new game starts once a restart handoff begins. Failed preparation reopens launching.
The installed updater is copied outside the application directory, checks the exact
parent process and staged payloads, and acknowledges readiness through a current-user
named pipe with peer-PID verification. It waits for that parent to exit before
replacing files. It never force-terminates the old launcher.

Installation keeps originals in a durable journal until the new process reports
that its frontend entrance completed. The new process cannot launch games before
the updater acknowledges commit. Failure to start or become ready stops only the
owned new process, restores originals, removes additions and restarts the old launcher.
External modifications retain recovery evidence instead of overwriting changed files.

After a candidate startup or integrity failure, the helper records the source identity and exact failed index hash
before reopening the restored launcher. That candidate appears as a startup failure,
not another ready-to-restart update. Automatic download, retry and clearing the task
cannot reinstall the same failed index while its rejection record is retained. Use Check
updates to discover a newer release. Outcomes from another source or index do not
block the selected release.

The latest rejection is also kept in a separate `rejected.json` record,
so running offline repair cannot erase it by replacing the repair outcome. Older
installations without this record still recognize their source-bound rollback outcome.

Local storage failures during staging or replacement do not reject the release.
After successful restoration, verified staging can be reopened and installation
requires another explicit confirmation. Free the required space or release file
locks before retrying. The helper's `outcome.json` retains its failure category,
phase and numeric HRESULT without exception messages or private request addresses.
New readers also recognize explicitly classified storage rollback results; older
rollback records without a category keep their conservative rejection behavior.
An existing startup/integrity rejection is not cleared by a storage failure.

On Home, total progress fills the available width when there is no current-file
progress. A current file animates the display into two columns. Open downloads
precedes secondary actions; the blue check, retry, resume or restart action stays
at the right, with animated button entrance and movement.

Successful installation also records the source-bound paths, sizes and hashes it
owns. A later update removes an absent file only if that record agrees with the
installed receipt and the file still has its recorded content. The removed file
is backed up in the same transaction as replacements and is restored if startup
fails. Changed obsolete files refuse installation; reintroduced files during the
health check refuse commit and preserve recovery evidence. Unrecorded files are
retained. The first update of an older installation establishes this record
without guessing ownership of its existing files. No manual deletion list is required.

The generated frontend directory `wwwroot/assets` is managed as a complete release
set. Updates remove files absent from the new manifest, including hashed JavaScript
and CSS left by older installations without ownership records. Nested asset folders
are included, with links rejected before traversal.
Files named in the new manifest remain. Removed assets share the update backup and
are restored if startup health confirmation fails. Unrecorded files outside this
generated directory, including user settings and client data, remain untouched.

## Integrity and recovery

The index, file list, release sizes and persisted update records have no fixed
application ceiling. Source-document readers expose no optional byte-limit setting.
Paths cannot escape the installation, traverse links, use ambiguous Windows names,
or replace update-source settings. Discovery rejects conflicting or older numbered
releases. Transfer history and hashes bind retries to the original selected source.

The installer adds, replaces and removes verified owned files. Ownership records
and replacement/removal journals have no fixed byte or entry ceiling.
File-to-directory shape changes are rejected before replacement rather
than deleting unknown directory contents. It requires write access to the installation
and does not request elevation.
The helper retains journals/backups when recovery cannot safely finish. The retained
external updater provides the offline repair entry point described below; do not remove
those journals or backups during repair. The implementation does not imply that
the official AList already hosts a published launcher distribution.

## Desktop acceptance scope

The signed transport-to-helper regression runs with:

```powershell
dotnet test tests/FantasyTale.Launcher.Core.Tests -c Release --filter FullyQualifiedName~SignedWireDownload
```

Its six Windows cases use real loopback HTTP/AList protocol responses, a
temporary signing key, the external updater executable and a test launcher.
Discovery fetches only metadata; download tasks account for the two changed
files without requesting unchanged executable payloads. The source stops before
ready-state restoration and process handoff. Valid health commits the new files;
invalid health restores replacements and removals, removes additions and records
the failed candidate. A modified retained signature prevents handoff altogether.
Every case preserves personal files and update source settings. This regression
does not render the desktop frontend or publish a signed feed; those require
separate native desktop and public-source acceptance receipts.

The update policy accepts schema 2 with multiple `[[sources]]` entries. Sources
are checked in configured order with an individual thirty-second deadline. The
first valid release ends discovery, including when it reports the current version;
later sources are fallbacks, not a search for a higher revision. A failed or
rejected source permits the next source to be checked. Canceling discovery restores
the preceding state. Each candidate is checked against installed and previously
seen release metadata before it can be selected. Each source retains its own
accepted revision and exact index hash across fallback, source reordering and
restarts. Returning to a source rejects a lower revision or different bytes at
the same revision. A backup source can still offer its own valid revision;
discovering it does not erase the primary source's history. The history and the
selected source are saved together in one atomic record.
Discovery reads the release index and any configured signature, without opening
installed payload files. Staging compares local payloads with two to eight bounded
workers and lists only changed files; unchanged verified content is reused locally.
Staging prepares up to four distinct content objects concurrently. Files with the
same digest share one transfer and resume object, with progress applied to each
affected destination. Cached objects are verified once, downloads retain the
engine's SHA-256 check, and local copies are verified after copying.

Incremental updates compare the installed bytes directly with the selected target
index. They do not require intermediate releases, so an older compatible reader
can skip several versions. If object transfers and equivalent-source fallback
still fail after a bounded retry, the updater attempts the complete archive.
The optional `<feed_path>.archive.toml` companion binds its volumes to the exact
selected index hash. It declares `format = "zip"`, `"7z"`, or `"rar"` and ordered
`[[files]]` entries with `path`, `source_path`, `size`, `sha256`, and `volume`.
There is no fixed archive byte or volume-count ceiling. The shared client
archive decoder checks paths, links and ordered volumes, requires every target file, and
checks each expanded file against the update index before staging it. Portable
source settings are ignored; local update sources are preserved. Automatic
archives must not require an interactive password. The default publisher creates
a ZIP; other formats use the same volume metadata and extraction path.

Deployment publishes verified incremental objects and discovery indexes first.
The complete archive is uploaded separately. Its companion is published only
after the archive is publicly verified, so a slow archive upload cannot delay
incremental discovery or expose an unfinished fallback. Older readers ignore the
companion and continue using their existing incremental download path.
The selected source identity is persisted with the staged release so the external
updater and restart recovery use the same source. Schema 1 remains supported.

The source list determines discovery priority. A typical configuration keeps
the official Fantasy Tale Archive first and an official HTTP mirror second; users
may add and reorder sources without changing the release files.

When a selected release comes from a different configured source than the
installed launcher, the external updater authorizes that ownership transition
only after the exact installed receipt is re-read under the installation lease.
The product and package identities must still match, and the old receipt,
owned-file list and external-file hashes are checked before any replacement.
The new source identity is committed only with the healthy release; a failed
startup restores the old files and old receipt. Ordinary client installations
do not receive this exception and continue to require matching source
ownership.

The source-transition process regression covers HTTP and AList after the previous
source is removed, with optional signatures enabled and disabled. Separate signed
cases retain a configured primary source that returns HTTP 503. Staging is restored
offline before the real helper runs. Tests check commit and startup rollback,
including obsolete files, the complete source receipt, local settings and user
files. This is process acceptance with a test launcher; it does not substitute
for desktop rendering or public-feed acceptance.

An isolated Windows desktop run accepted a newer healthy release after an earlier
startup rollback. It paused an actual transfer at 43%, persisted the paused state,
resumed, then installed a self-contained win-x64 release through the external
helper. The new launcher displayed Home and completed health confirmation; all
506 installed files matched the selected sizes and SHA-256 values, the existing
source policy remained unchanged, and the installation journal was removed.
This acceptance uses loopback HTTP. It does not establish official AList feed
availability, anonymous staging through that feed or remote desktop update success.

## Optional background archive uploads

For storage backends that disconnect while a synchronous upload waits for its
final response, `upload_alist.py --background-uploads` submits files of at least
8 MiB as AList background tasks. Indexes and other small metadata stay synchronous.
The publisher follows the returned task identity until storage reports success;
100% transfer progress alone is insufficient. A transient status-query failure
resumes the same task within the configured verification deadline without
retransmitting the archive. This task identity is retained only in the running
publisher, not across process restarts. A failed, canceled or expired task is not
accepted as success. A retry can accept already committed archive bytes only
after their complete anonymous SHA-256 matches the local archive. An expired
task may still be running remotely and is not canceled
automatically; inspect its status before restarting the publisher.

Task success is followed by anonymous SHA-256 verification of the complete
archive and the existing feed identity checks. Fallback metadata is published
only after those checks pass. This option does not change incremental-first
deployment or prove that a particular public storage backend has accepted it.

## Anonymous AList staging verification

The opt-in integration test downloads a published launcher feed through the real
AList adapter, verifies every staged file and its selected index hash, and reopens
the saved transfer with an unavailable network handler. It does not start the
launcher, install the release or launch a game. Run on the matching Windows
architecture:

```powershell
$env:FT_TEST_LAUNCHER_ALIST_URL = 'https://files.example.org/'
$env:FT_TEST_LAUNCHER_FEED = 'launcher/win-x64/current.toml'
dotnet test FantasyTale.Launcher.slnx -c Release --filter PublicAListLauncher
```

An optional `FT_TEST_LAUNCHER_ADDRESS` pins only this source hostname's connection
to a supplied IP for local DNS troubleshooting; it preserves the configured source
identity and HTTP host. Download URLs on other hosts retain normal resolution.
No AList account or administrator token is used. The test has a fifteen-minute
transfer deadline and removes its isolated data afterward.

The test process architecture must match the published distribution. On an ARM64
Windows machine testing a win-x64 feed, use VSTest's explicit platform selection
with the x64 .NET runtime installed, after building:

```powershell
dotnet vstest tests/FantasyTale.Launcher.Core.Tests/bin/Release/net10.0/FantasyTale.Launcher.Core.Tests.dll /Platform:x64 /TestCaseFilter:FullyQualifiedName~PublicAListLauncher
```

Keep the same `FT_TEST_LAUNCHER_ALIST_URL` and `FT_TEST_LAUNCHER_FEED` environment
variables. The runtime printed by the test should be `win-x64` for that feed.

On 2026-10-23 this test passed against the official AList source at
`http://archive-cn.600819.xyz:5244/`, using
`FantasyTale/launcher/win-x64/current.toml`. It verified 990 staged files totaling
388,573,961 bytes at revision 1792685966, then restored Ready with an unavailable
network handler. The x64 run completed in 5 minutes 37 seconds, with temporary
data on D:. This establishes anonymous discovery, transfer, file integrity and
offline task restoration for that release. It does not establish desktop
replacement, health confirmation or rollback using that public release.

On 2026-10-24 the explicit x64 test passed again against published 0.1.24,
revision 1792832242, verifying all 993 files totaling 388,850,295 bytes and
restoring Ready with the source unavailable. It completed in 4 minutes 17 seconds.
The TRX receipt is `artifacts/build-helper-results/public-alist-0.1.24-x64.trx`.
This remains transfer/restoration acceptance, separate from public-feed desktop
replacement and rollback.

## Interrupted-update recovery

At startup, before creating the normal window, the launcher checks for an unfinished installation journal. When the installed executable can still run, it hands the journal and its SHA-256 to the retained external updater, waits for the exact parent to exit, verifies the journal did not change, and restores the transaction before starting the launcher again. Each parent uses a separate temporary request file and removes it after handoff, so competing bootstraps cannot overwrite another parent's request. If the launcher cannot start, run the retained external updater with `--repair <installation> <data-root>`. It takes the same leases, refuses a running launcher, validates the journal and checks transaction file hashes before restoring it offline. An outcome record reports recovery-required on conflicts; no network or release metadata is needed. A busy installation lease leaves files and backups intact and permits retry once released; malformed journals are preserved for repair rather than discarded.

## GitHub mirror option

Settings provides GitHub mirror acceleration through the configured mirror providers.
When enabled, eligible GitHub requests, including update metadata and payload
requests, may be forwarded through the selected provider. Downloaded files still
must match the selected release's sizes and SHA-256 values. For an unsigned feed,
these checks detect corruption but do not authenticate the metadata or its author:
the configured source, its transport and any forwarding provider remain part of
the update trust boundary. Do not treat mirror forwarding as end-to-end
authenticated update delivery. See [network settings](network-settings.md).

## Editing update sources

Retained unsigned bundled AList policies using the retired default host resolve to the current bundled archive endpoint after upgrade, with IP access enabled. Their source identity remains stable so staged downloads and accepted revisions survive. Custom paths, signed policies and other hosts retain their authority; the policy file itself remains intact.

Settings > Updates lets you add sources, edit their names, transport, public root and feed path, and reorder them. Save applies the entire validated list atomically; invalid or duplicate endpoints leave the previous settings intact. Complete or cancel an active launcher update before editing its sources. The installed baseline and existing signature settings are retained. When a payload source is unavailable, the downloader checks other configured sources and uses only releases with the same revision, version, destination paths, sizes and hashes. Per-source failures remain visible in expanded diagnostics even when another source discovers an update. Official GitHub feed deployment and desktop acceptance are still pending.

# Store profiles and shared updates execution plan

Status: in progress. This plan supersedes bundled-only client policy distribution
and instance-scoped plugin transfer assumptions. Existing unrelated working-tree
changes and accepted authentication binaries must be preserved.

## Goal

Make configured stores the distribution authority for client profiles and plugin
metadata. Synchronize automatically at startup without confirmation. Download a
plugin once into a shared library, select activation independently for each client,
and update it safely while games are running. Make launcher updates reliable across
ordered, independently configured sources.

## Ordered work

Current policy clarification (2026-10-21): the original concurrent source-selection
proposal is superseded by ordered fallback. A valid response from the first healthy source is
authoritative even when it reports the installed version; lower-priority sources
are consulted only on failure, with bounded timeouts. Startup discovery runs in
parallel with window creation and does not gate starting a game. Developer mode
now reveals a disabled final navigation entry; its maintenance workspace remains
deferred. Sidebar expansion is session-only and defaults to compact on each launch.

- [ ] Define a versioned unified catalog containing client profiles, download
  definitions and plugin metadata; retain the old catalog/feed for older readers.
  Profiles contain executable/arguments, fingerprint, region/wire identity, login
  policy, required/recommended plugins and overlay configuration. Revisions select
  content updates independently of launcher releases.
- [ ] Share bounded startup synchronization, in-flight requests and cached results
  across views/services. Persist only fully validated snapshots, keep last-known-good
  data on failure, apply profiles automatically and preserve instance choices.
- [ ] Make store profiles drive version selectors and pre-launch requirements;
  retain endpoint/ticket responsibilities on game servers. Bind downloaded profiles
  and instances to their selected source rather than merging conflicting identities.
- [ ] Install each plugin release once globally. Use a Fluent checkbox dialog for
  compatible installed instances, all selected by default; permit library preload
  with no compatible client. Rename the plugin primary action to Install.
  Progress: one explicit install task supports multiple compatible builds, with
  bounded persisted per-target selection stamps. Two restart tests cover unchanged
  targets and a later disabled choice; the package is installed only once.
- [ ] Enable automatic plugin updates by default with a persisted opt-out. Preserve
  disabled selections and compatible options. Coalesce shared transfers, defer
  physical game-directory file replacement until exit, and retire unreferenced
  older releases rather than keeping indefinite version history.
  Progress: the default-on persisted switch, settings UI, and background idle-client
  installation callback are implemented. Shared library transfers now coalesce
  across client targets; bounded persisted upgrade intents defer selection changes
  while a game or another lease owns the client. Restart tests cover two clients,
  running-client deferral, changed selections, and installed-package reuse. The
  plugin page shows pending activation and safe failure categories. Retirement of
  unreferenced versions and real game acceptance still remain.
  Regression finding resolved: update checks and server release validation refresh
  source catalogs; ordinary browsing reuses the bounded cache, and the store refresh
  button explicitly bypasses it. StoreWireTests passes all 31 cases. Shutdown drains
  cache consumers before disposing the catalog cache.
- [ ] Pin complete launch snapshots while updating shared packages independently.
  Show a Fluent launch warning listing relevant pending plugin transfers, offering
  wait-and-launch, immediate launch and cancel. Immediate launch uses complete
  installed packages, omits unavailable optional plugins and cannot bypass missing
  required/minimum/latest requirements. Revalidate changed choices before launch.
  Progress: the Fluent pending-transfer dialog lists per-client shared downloads
  and supports wait, immediate launch and cancel. Seven UI regressions cover the
  choices and cancellation on context changes. The host rejects unconfirmed new
  tasks after its latest-plugin gate; store multi-client installs use the same
  lease-independent activation service. Real game launch acceptance remains open.
- [ ] Provide separate ordered launcher update sources: Fantasy Tale Archive first,
  official GitHub second, plus user additions. Read sources in configured order;
  the first valid Current or Available result ends discovery. Consult the next
  source only after failure. Do not compare healthy sources to select a higher
  revision. Keep discovery parallel with window creation and never gate game
  launch. Preserve source-specific failure diagnostics and cancellation recovery.
  Progress: the ordered-source regression checks cover Current short-circuiting,
  failure fallback and source editing. The public 0.1.21-to-0.1.22 archive-source
  desktop acceptance is recorded in the roadmap. GitHub publication and production
  signature custody remain separate gates; archive success does not establish them.
- [x] Add the GitHub mirror acceleration setting as visibly unavailable with a TODO
  adapter; do not pretend requests are accelerated or invent a mirror endpoint.
- [ ] Measure request counts and phase durations, remove repeated catalog requests
  and serialized per-item lookups, retain bounded retries/resume/integrity checks,
  and verify self-update replacement/restart/health recovery from an older package.
- [ ] Rename only our own AList site's display label to Fantasy Tale Archive;
  retain protocol names and user-defined source names.
- [ ] Expose plugin minimum launcher requirements only when unmet; verify inline
  store requirements against the actual package metadata before installation.
- [x] Reserve a default-off persistent developer-mode switch. TODO: developer tab,
  graphical store maintenance/publication and credential-based AList connections.
  Implement only the switch in this iteration; never persist credentials in UI preferences.
- [ ] Update paired user guides and release notes, run focused managed/frontend
  regressions and real publication/upgrade acceptance, then publish a new launcher
  with compatibility discovery retained. Never claim incomplete acceptance.

## Acceptance

Launcher frontend cleanup: the updater treats generated `wwwroot/assets` as a
manifest-owned release set, including installations predating ownership receipts.
Obsolete hashed bundles are backed up transactionally, removed on successful
updates and restored on rollback. Bounded traversal rejects links before descent;
unrecorded files outside the generated asset directory remain untouched.

Profile policy edits become effective without reinstalling the launcher. Multiple
clients share one plugin transfer; new compatible instances can reuse that package.
Games keep their original complete snapshot during updates, later launches use the
new complete version, and old files are cleaned only after their references end.
Timeouts never indefinitely block discovery; all-failed discovery is never Current.
Required plugin gates cannot be bypassed by the immediate-launch confirmation.
The existing CN2011 movement/overlay investigation remains low-priority future work.

## Superseded source-selection proposal

The original 2026-10-20 implementation plan proposed concurrent source checks,
publishing the first newer result, waiting for all responses, and selecting the
highest compatible revision with equivalent-content payload fallback. That design
is retained here as history only. The 2026-10-21 ordered-fallback decision above
replaces it; it is not an open implementation task or the current source policy.

## Verification checkpoint (2026-10-20)

Release solution build: zero warnings and errors. The combined launcher-update,
plugin-import, store-wire and store-profile regression run passed 202 tests with
no failures; one optional real-AList test was skipped. This is focused regression
evidence, not full-suite, remote publication or real-game acceptance.

The delivery target is now three hours for the current store/profile/plugin and
launcher update redesign, preserving integrity, rollback and required-plugin gates.
Unrelated client reverse engineering remains deferred.

Source-management regressions verify persisted order, unchanged settings after
invalid edits, and retained installation baselines. Payload fallback regressions
verify successful equivalent mirrors and rejection of differing payload sets.

Latest update regression: 84 passed, one optional remote test skipped; frontend
update flow: 31 passed; TypeScript checking passed. The wider regression exposed
a stale schema-two rejection assertion, a canceled test HTTP peer, and a short
Windows record-lock retry under load. These are repaired and await rerun. The
full managed suite has additional failures and is not accepted or reported green.

Retirement implementation now quarantines and removes unreferenced superseded
store packages only after all activation intents finish and client leases plus
the instance registry establish a coherent selection snapshot. Disabled selections
remain references. Imports, changed snapshots, different sources and different
build scopes are retained. Reference retention/removal and deferred activation
tests passed (three cases). Concurrent source-edit regression passed all twelve
cases after TOML readers were changed to allow atomic snapshot replacement.

A win-x64 0.1.19 candidate package was generated at
artifacts/launcher-0.1.19-x64-20261020-candidate1. This is a local candidate,
not a published or fully accepted release; subsequent source changes need a new
package before delivery.

## Delivery verification checkpoint (2026-10-20)

The combined update, store, profile and import regression rerun passed 210 tests
with one optional remote test skipped. Frontend update regressions passed 31
tests. Publisher regressions passed 52 tests; frontend publication passed three.
These focused results do not establish full-suite or real-game acceptance.

A real x64 package downloaded over local HTTP, handed off to the external updater,
restarted the packaged launcher and received its frontend health acknowledgement.
A subsequent frontend-only release completed the same external replacement and
health flow. All 961 installed manifest files match their declared hashes, backend
files retain their baseline identities, and no obsolete generated assets remain.
The frontend release fetched only three changed files (756,838 bytes), with no
backend payload requests. Evidence is retained in
`artifacts/frontend-update-acceptance.json` and the local HTTP request log.

Settings source management now uses a compact card and separate management/edit
dialogs. Settings cards use regular body weight; update sections and the developer
mode card use the existing section and card spacing conventions. TypeScript and
production frontend builds pass after these changes.

Public x64 discovery finds 0.1.18, but its payload download failed with a network
error. A resumed diagnostic is collecting the safe failure category and status.
Remote publication, GitHub feed deployment and public download acceptance remain
open. No newer local candidate is represented as already published.

The final local x64 delivery package, revision 1792489960, includes the latest
settings spacing and regular-weight card headers. Archive verification passed
(206,536,425 bytes, SHA-256
`f80095e09b0e30daa44c8aa86bef089b3f1140e64b351c5e55eafb3a3f9683b3`).
Its own real local HTTP download, external handoff and restarted frontend health
confirmation completed with manifest SHA-256
`02988d5e71a13d987d56f244c4b500138d31274dbe6e4a2d2f2638f115ee0085`.
The complete solution rebuild passed with zero warnings and errors. Frontend
publisher preflights aliases before creating any publishable output; all four
publisher tests pass. Public network acceptance remains separate and pending.

The full managed suite initially reported one intermittent access-denied failure
while saving a source during background plugin discovery (707 passed, six skipped).
All eight isolated cases then passed; a full concurrent rerun passed 708 tests with
six optional skips. This does not establish a root-cause repair for that first
intermittent failure. Test bridge assertions now include the failing method name.

Unified catalog composition now has a local publisher utility preserving package
rows, merging advancing profile revisions and optionally emitting a legacy package
projection. The uploader accepts the same 4 MiB unified-document bound as the
launcher parser while retaining the old limit for other indexes. The combined
uploader/composition tests pass all 56 cases. Production catalog validation and
endpoint migration remain open; no profile catalog has been switched remotely.

The publication CLI now exposes `--validate-catalog` through the actual managed
catalog parser. A local candidate composed from the anonymous production catalog
retains all 16 existing package rows and includes all 13 bundled client profiles;
the real managed reader accepted it. This candidate has not been uploaded, and its
per-profile mandatory policy and endpoint migration still need review.

Publication of the frozen 0.1.19 delivery graph is active in tool session 76638,
incremental phase followed by archive phase. Its sanitized log is
`artifacts/launcher-0.1.19-publication.log`. Do not start a competing publisher.
The old public 0.1.18 probe reached its fifteen-minute test deadline with 178
verified objects; it did not reach ready, and the slow serial per-file path remains
an optimization target. This is not reported as successful remote update acceptance.

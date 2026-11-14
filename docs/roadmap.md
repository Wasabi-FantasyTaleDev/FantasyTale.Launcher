# Roadmap and task cards

## Managed mode-extension controller (2026-11-10)

Move native create-room observation, owned entry buttons, catalogue policy and
confirmed mutations into the game module, selected by the package's controller
declaration. The browser receives display snapshots and submits session-bound
confirm/cancel actions. Hidden WebView suspension remains enabled and no page
timer drives native work. Registration subscribes the controller before loading
HTML; session teardown awaits controllers before closing their transports.
The local 0.1.2 candidate receives an owner-scoped Companion channel, subscribes
before synchronization, and requires both an active room-create context and the
`plaza` scene. Main menu, room entry and disconnect remain dormant. Connection
epochs, context revisions and per-connection receive positions reject retired or
late state. No periodic server query or plaza waiting deadline is used.

Queued native reads retain their original identity and purpose without canceling
the shared Hook pipe. Confirmation reads after the user action; accepted mutations
have bounded result checks and cannot replay an unknown outcome. Cleanup retains
owned entry handles through temporary contention and precedes reactivation.

Release build: zero warnings and errors. The focused backend suite passes 257
cases, and the relevant frontend suite passes 31. The full managed run reports
2435 passed, two store-fixture timeouts and 21 skipped; a separate rerun of the
two affected methods passes all 16 combinations. This is not a single clean full
run. Version 0.1.2 and the managed candidate are installed locally. Real kr-812
acceptance on JD remains open until its server deployment and native entry,
confirmation and exit lifecycle have been observed.

## Native control activation readiness (2026-11-10)

Live-control activation waits within validated plugin limits for the complete
descriptor signature set before preparing observers. The loader can start its
worker while client code is still packed. An isolated harness reproduced the
production adapter's activation failure with the packed image and successful
activation with the unpacked image. Exact signature checks and transactional
patch ownership remain mandatory. Activation errors retain a bounded reason;
actual mode-entry acceptance remains open.

## Overlay browser profile isolation (2026-11-10)

The renderer clears an inherited WebView2 user-data-folder override so the launcher's
Photino browser cannot replace its own profile. A live reproduction failed during
controller creation with HRESULT 8007139F; the same executable initialized after
removing the override. The repaired installed renderer also initialized with the
launcher override present, loaded its page and rendered the native-style preview.
Release compilation has zero warnings and errors. Mode Extension's native entry
still requires verification in a new game session.

## Room mode extension (2026-11-09)

An optional overlay package presents native mode artwork in the active client's
window theme, with three-column scrolling and draft/confirm/cancel selection.
Explicit build allow/hidden rules filter the shared catalogue without treating
it as a proven compatibility table. The baseline native entry follows current
selection, uses native mode activation where available and localized room-name
tags for configured extensions. Controller regressions and browser scrollbar
checks pass; real-client acceptance and additional native bindings remain open.

Legacy confirmation rows use compact localized widths and original icon-bearing
skin heights. Shared caption buttons preserve icon proportions in other consumers.
Ban Pick retains every first page while bounding rolling text work and invalidating
late drawing, opening and confirmation results after room changes or unload.

## Live native controls (2026-11-09)

The Native UI Bridge exposes visible-control inspection, direct and exact-match
text assignment, repeated-name array selectors, owned native buttons and bounded
events. kr-812 selection groups and button activation follow the client's own
callback and distinguish a selected disabled button from a locked one. Managed,
overlay, Companion and developer-page surfaces use the same scoped contract.
Static evidence and synthetic tests cover the baseline; real-client acceptance
and descriptor ports remain open.

## Ban Pick editor UI (2026-11-08)

Completed the first Vue 3 inventory-style preview with a reusable
`BfBanPick` component, native window/card assets, four item categories,
localized search and restriction filters, native category tabs and paging,
each theme's original description panel, and draft/apply/cancel selection state.
The three-column, three-row inventory retains 172px slots and 180/178px pitch.
Official Chinese names and descriptions come from the workbook and translation table.
Named presets persist locally, including same-name replacement and allow-all schemes.
Forty-seven transparent ItemViewer samples are generated from local client resources;
the preparation tool rejects the retired viewer source and records source hashes.
The original kr-812 item bob uses a 2,000 ms period and 2.5-pixel amplitude.
Strict Vue typing, the production build, renderer event/preset regression tests and
browser interaction/layout checks pass for seven locales and a narrow viewport.
Mounted native controls now emit from themselves so Vue wrappers receive changes
after adopting them from temporary construction parents. Real-client outline A/B rendering confirms
the model contour. Private assets remain ignored. Accessory attachment calibration,
room transport and server enforcement remain open; this card completes only the
requested UI prototype. See `tools/game-ui/ban-pick-demo/README.md`.

The second presentation pass isolates v1 CN2011 and v2 CN2015 assets, preserves
32px/27px native tab heights and left-aligned v1 titles, and puts search and the
native restriction checkbox on the main-category row. The full-width description
keeps each source form's title and label regions. Presets now open in a separate
native-theme modal. Captioned buttons use compact measured widths and stable state
layers; pointer transitions cannot detach a pending click target. The demo maps
weight 700 to the existing heavier Chinese W9 face. Item names center visible ink
using the same alphabetic baseline for measurement and drawing; footer text uses
the atlas's actual status region. Theme archive audits, 19 synthetic regressions,
strict typing and both UI builds pass; three optional tutorial/audio resource groups
remain skipped. Browser checks cover preset persistence/replacement/deletion,
selection restoration, captured scrollbar drags and keyboard paging, theme changes,
Chinese/Korean/English and 400px width. The complete 1024-by-768 preview uses
integer final font sizes and browser SVG glyphs rather than resized text bitmaps.
Titles, name/status bands, compact footer alignment and the search/checkbox order
follow their isolated theme geometry. Atomic appearance refreshes and propagated
rendering errors prevent blank captions after resource regeneration. The browser
font export retains all glyph outlines, hints, advances and kerning while normalizing
legacy CFF/maxp and kerning records; all-glyph geometry and 16 raster comparisons pass.

The final card pass crops native glow padding without rescaling the interior,
keeps only the model-region restriction border, insets its upper-left badge and
uses identical footer colors for allowed and restricted items. Complete canonical
subtype ordering includes melee/special weapons in the older theme. Samples add
two hammers, two grenades and three head-accessory crystals. An explicit private
preview adapter renders crystals alone with a declared angle and proportional fit;
its manifest records demo framing rather than claiming a verified native camera.


## Native auxiliary protocol readiness (2026-11-08)

Launcher 0.1.69 carries catalog capacities into a bounded Gateway status probe before
the original client opens its directory. A TCP-ready service whose native status protocol is not
active becomes an existing local unavailable endpoint that can still finish uniqueness retirement.
Automatic snapshots cannot revive a protocol-failed endpoint within that launch. Validation includes
fragmented native replies across all 14 dialects, TCP-only services, malformed replies, cancellation
and actual late uniqueness cleanup. Original-client popup acceptance remains a separate check.

## Game-language font routes and admission cleanup (2026-11-08)

Launcher 0.1.68 separates automatic admission from native connection retirement.
Closed admission withdraws candidates without stopping accepted cleanup work.
File payloads and memory patches support validated language inclusions/exclusions
so Chinese and non-Chinese font routes remain independent of the Launcher UI.
Chinese font support 1.0.10 defaults optional bold off, retains the accepted
legacy fusions and adds a KR1256 donor and verified weight normalization site.
The approved client recommendation matrix is pinned by build-level regression
tests, including omitted default=false and the CN2014 scene-style exclusion.

## Prebaked Chinese font outlines (2026-11-08)

Chinese font support 1.0.9 bakes the native outside stroke into the Chinese
base's Chinese, Latin, digit and punctuation outlines. It preserves advance
widths and each 1.0.8 fusion's Korean glyphs, without changing the runtime bold
patch or option behavior. The 1.0.8 ZIP, font declarations, source snapshots
and Git anchors are retained for rollback. The native quarter-pixel stroke is
scaled to an explicit 11-pixel reference size; stored outlines scale with text
size, so they approximate the native fixed-pixel effect at that size. Fourteen
font-tool fixture checks pass. All ten fonts pass serialized geometry and
advance checks; 30 Korean raster comparisons remain exact. Published Launcher
0.1.67 assemblies import the final ZIP and deploy/restore the three supported
clients in temporary copies. Runtime weight behavior remains unchanged at the
maintainer's request; live game rendering remains player acceptance.

## Independent Korean font fusions (2026-11-08)

Chinese font support 1.0.8 retains Chinese and Latin glyphs from the Chinese
base while preserving each available native font's Korean glyphs. Ten pinned
donors produce distinct payloads; incomplete NexonGothic syllables use the
matching Arita weight. Referenced fonts without original files retain a declared
complete-Korean fallback. Serialized geometry, scaled metrics, CFF outline
origins and primary/fallback precedence pass seven generated-fixture tests.
All ten real fonts pass complete modern Hangul and serialized-geometry checks;
30 FreeType comparisons retain exact Chinese/Latin pixels. All 66 definition
checks pass, and published Launcher 0.1.67 assemblies import the final ZIP and
deploy/restore all three clients' native font destinations in temporary copies.
Actual game rendering remains a player acceptance check.

## Optional bold weight across game languages (2026-11-08)

Chinese font support 1.0.7 removes the redundant language filter from its optional
bold patch. Disabled retains the native Chinese-only conditional store; enabled
bypasses that condition for every language. Font deployment and existing option
values are preserved. Native regression cases execute the verified x86 store
under both option states, three builds and Chinese/non-Chinese/absent tags.

## Native-language font destination coverage (2026-11-08)

Chinese font support 1.0.6 replaces the native font filenames as well as ngulim.ttf.
The earlier package left Korean and Indonesian interfaces using their original
language tables' font files. The private merged Chinese font is reused for every
destination, while the optional Chinese bold patch keeps its existing language gate.
Six regression cases cover both bold settings, native defaults, disabled selections
and recoverable deployment. A local original-archive audit covers all 14 KR2015 and
15 Indonesian TTF/OTF destinations and verifies byte-exact restoration in temporary
client copies. The import ZIP contains one font payload. The Release build has zero
warnings and errors; the complete managed suite passes 2,093 cases with 20 external
fixtures skipped. Live rendering remains a player acceptance check.

## Advisory log retention and readable plugin inventory (2026-11-08)

Launcher 0.1.67 makes historical diagnostic cleanup nonblocking when files or directories are held open. Recovery and unchanged scheme application avoid exclusive leases; inventory refresh can defer a busy scheme write without discarding the next-launch selection. Occupancy notices expose copyable local lease details and recent launcher events. Regression coverage holds real Windows file and directory handles, reads inventory and applies unchanged schemes under a held client lease, rejects actual edits with structured context, and retries after release. Frontend checks also preserve details across translation and reject stale diagnostic replies. Empty library maintenance and reader disposal avoid write leases, while pending import disposal retains its lease. Release compilation has zero warnings; the complete managed suite passes 2,086 cases with 20 external fixtures skipped, and the complete frontend suite passes 445 cases with three skipped.

## Store activation across build schemes (2026-11-08)

Implemented for Launcher 0.1.66. Checked store installation persists an enabled
selection in every compatible build's selected scheme, including unregistered
builds. Pending transfer/running-client snapshots cannot erase that choice;
cancellation and later manual edits supersede it. Regression coverage switches
to a previously inactive disabled scheme, restarts, registers another build and
verifies that a subsequent manual disable persists. The retracted scene crash
report belongs to server-side character investigation; scene package 1.0.0 is
restored in the public store without changing its immutable payload.

## Launcher 0.1.65 release readiness (2026-11-08)

Release compilation is warning-free. The complete managed suite passes 2,075
cases with 16 explicitly external fixtures skipped; all four published Gateway
interop cases pass on both architectures. The required native executables pass
953 checks. The matching Gateway is pinned to its tested source and reviewed
main integration; each AOT architecture passes 42 directory, 196 automatic and
30 auxiliary-retirement process cases across all 14 builds. The upstream merge
leaves the generated Gateway data unchanged.

The independent scene-style candidate retains its nine verified real-archive
composition cases and exact final package identity. Publish the compatible
Launcher feed before the scene package. Client rendering, camera placement and
click targets require player acceptance. Diagnostic mode 1.2.7 improves the
next crash capture; the reported CN2011 crash and repeated prompts remain open.

## Runtime image evidence in bounded dumps (2026-11-07)

Diagnostic mode 1.2.7 requests readable pages of the main PE image through the
existing dump callback. Guarded and unmapped ranges remain absent; image requests
consume at most half of the configured total dump cap, which remains enforced by
the output callback. Release native compilation passes with warnings treated as
errors. All 139 writer checks pass, including unreadable-page skipping, truncated
range budgets and actual retained main-image code in system-written dumps.
Real-client capture and the reported CN2011 crash remain under investigation.

## Selector connection diagnostic continuity (2026-11-07)

Retain fixed per-listener metadata through native auxiliary retirement rather
than dropping all stderr after the first 32 lines. Coordinate the exact optional
trace fields and event grammar with the pinned Gateway, preserve redaction and
bounded output, and retain enough copyable lifecycle records to correlate local
ports. Release compilation and 112 focused managed checks pass; four checks
requiring published Gateway artifacts remain deferred until the new artifact pin.
This does not establish the cause of a
reported repeated native modal or repair a client crash.

## Independent resource style choices (2026-11-07)

The existing archive-member provider accepts explicit string/enum equality gates,
preserving boolean gates and original-resource authority. Focused checks cover
matching and nonmatching choices, enum constraints and malformed declarations.
The game module's separate scene package retains independent main/plaza settings;
its real rendering remains a client acceptance step. Release packaging still
requires the matching Launcher version and reviewed module payload.

## Nonblocking diagnostic close snapshots (2026-11-07)

Normal top-level window destruction must not consume the exception dump or block
the game thread, including startup splash teardown. Explicit close/quit requests
queue a bounded best-effort snapshot and window logging uses a nonblocking recorder
lock. Complete: the Release diagnostic library builds with warnings treated as
errors, and 121 dump-writer checks pass, including a blocked writer with an
independently returning caller and retained exception-stream verification.

## Store activation across compatible clients (2026-11-07)

Apply explicit install-and-enable to every compatible registered client, including
those without a previous selection. Respect the checkbox when a library package
is classified as an update, enable previously disabled selections on this explicit
request, and defer running clients. Complete: frontend target and dialog regression
checks plus backend shared-transfer, option-migration and deferred activation checks
pass; production frontend and managed builds remain warning-free.

Amendment (2026-11-08): preserve the checked action in every compatible build's
selected scheme, including future client registrations. Persist pending task
choices across restart and old host snapshots; cancel only unchanged choices and
respect later manual edits. Full frontend checks and 2081 managed checks pass;
Release builds with zero warnings. Compound archive conditions enable Scene style
selection 1.0.1's independent Christmas music and NPC outfits. Its module verifies
144 setting combinations and 34 real compositions; rendering and playback remain
player acceptance checks.

## Independent first plugin installations (2026-11-07)

Repair first-install activation when unrelated required components are absent or
disabled, including shared downloads selected for several clients. Retain saved
intents for automatic recovery and keep launch policy enforcement. Verified
downloads must not regress to installation progress while client settings wait.
Scope pending notices and failures to their client and distinguish installation
wording from upgrades. Completed: Release builds without warnings, all 314 focused
plugin and store checks pass, and the actual published pirate ZIP's pending 0.1.62
installation resumes successfully under the corrected implementation. Full-suite
results plus the focused rerun cover 2,045 passing cases and 16 external fixtures
that remain skipped. Frontend type checks, build and 82 related cases pass.

## Missing expression archives (2026-11-07)

Implemented the explicit absent-BFZ baseline in resource preparation and shared
runtime deployment. Conflicting edits use the existing resource dialog; a file
that appears after preparation is never overwritten. New archives are removed
through the existing exit and interrupted-launch recovery journal.
The pirate expression candidate imports through Launcher 0.1.62 and passes real
resource preparation plus isolated deployment/recovery for nine Chinese builds,
including CN2019's existing newer PKN. Its 193 missing timelines preserve all
original members; CN2019 retains 958 original members and their storage metadata.
Launcher 0.1.61 rejects the candidate with the required upgrade. These checks do
not establish visual acceptance of expressions in a running pirate match.

## Native question boxes in CN2011 and CN2012 (2026-11-07)

Add two-button question boxes to Game Control Support through its existing
`game.native-ui` service. The early clients use game-owned UTF-16 storage and
build-specific constructor conventions. Confirm the modal targets and button
setters against both client images and exercise the calling conventions in the
native fixture. CN2011 display and two consecutive button selections are
confirmed in the isolated client; the process remained alive until the bounded
test ended, and all test files were restored. CN2012 also passed two consecutive
button selections. All 158 native UI checks pass; both images pass eight exact
function signatures, modal binding and layout validation.
Use asynchronous sent-message dispatch so filtered client message pumps cannot
leave a control operation indefinitely queued. Native service callbacks remain
independent of the modal's lifetime.

The game module now shares a zero-hook Nirinsou `game.session.state` query.
Its typed result distinguishes ordinary matches, tutorials, waiting rooms and
unavailable or malformed responses. The early ESC candidate queries it before
presentation, preserves the original tutorial input path, and sends a bare
`plaza` only after confirmation. Actual server-bound command acceptance remains
pending; source and focused tests alone do not establish deployment.

## Native bootstrap retirement ownership (2026-11-07)

Implemented: distinguish successful native handoff from disposal. Gateway
keeps refresh and its control pipe alive while its own bounded protocol retires
auxiliary sockets. Game exit cancels observation and retains immediate STOP and
cleanup. The Launcher passes 132 focused checks and four real process/pipe
checks against each pinned Gateway architecture. Both Gateway binaries pass 30
native retirement scenarios. Retail match acceptance follows publication.

## Configurable recommendation defaults (2026-11-07)

Implemented: recommend rules accept optional `default_selected` in profile/store
TOML and `defaultSelected` in server JSON. Omission means unchecked; every optional
card remains visible and selectable. Installation plans and first-run prompts
carry the same default. Other rule types reject the field, required/recommended
scope conflicts remain errors, and merged recommendations default to checked only
when every applicable offer opts in. The 0.1.61 distribution includes this
support. Only clients at 0.1.61 or later understand the new field.

## Launcher 0.1.60 minimum policy (2026-11-07)

Set the release index's deprecated maximum to 0.1.59, requiring earlier launchers
to update to 0.1.60. This is publication metadata; the verified 0.1.60 payloads and
portable ZIP remain immutable. Publish a newer index revision and matching archive
companions, and coordinate the server's advertised minimum version separately.

## Frontend locale catalog review (2026-11-07)

Completed: consolidate the existing Vue I18n messages into locale JSON catalogs,
retain the six existing languages and add the requested Thai interface selection.
The previous catalogs retain every message; scheme recovery gains translated
labels. Review corrects 24 missing Thai keys, three lost interpolation contracts
and comments that confused stale-result suppression with transport cancellation.
Seven-locale coverage, parameter parity, saved preferences and dynamic metadata
fallback are exercised. Thai still contains English text pending translation.
Thai client date labels use Intl's Buddhist calendar, with UTC validation of date-only
values to avoid timezone shifts. Language switching restores Gregorian labels without
altering profiles, protocol identities or saved client names.

## Shared plugin recovery and unsupported rename visibility (2026-11-07)

Completed: omit irrelevant former identities when neither package supports the
selected client, while retaining predecessors needed by another supported build.
Recover the same missing or rejected release across registered clients, migrating
healthy replacements independently per build. Remove its exact scheme references
across every build and guard in-flight scheme responses against restoring them.
Recovery dismissal acknowledges the pending notification by game, identity and
version. Unrelated releases, imported pins and running payloads remain protected.

## Plugin rename visibility and Chinese authentication packaging (2026-11-07)

Hide a former package from the plugin view and update discovery once an
installed successor can serve the selected build. Other clients and active
sessions retain their existing files. Offer selection prefers the eligible
successor before comparing versions, and a saved scheme keeps its explicit
successor when both IDs remain in the same selection.

Chinese authentication 1.0.18 restores the byte-verified bridge, security stub
and passport table from 1.0.16. The shared runtime build no longer stages its
forwarding DLL into the store package. Store publication checks that the three
deployment payloads exist and match their declared size and digest. The
published ZIP passed 1,166 native fixture checks using its actual DLL.

## Plugin recovery before launch policy (2026-11-07)

A scheme can be saved without a mandatory package present. Startup preflight
removes unresolved selections and reports them through the recovery dialog
before displaying missing required components. Bundled defaults can restore
their verified packaged version after an invalid pin. Former identities resolve
to the successor's actual installed version, even when the successor starts
a new version series. Same-identity upgrade and version-bound checks remain.

## Confirmed component ZIP import (implemented locally, 2026-11-06)

Allow user-confirmed ZIP and folder imports of explicitly store-managed
components while retaining the declared update channel, immutable bundled
snapshots and restoration policy. Imports remain local installations without a
store-origin receipt. The existing preview, full validation and deferred upgrade
activation apply unchanged. Other reserved identities remain protected.
All 171 focused import and local-upgrade cases pass without skips. A Windows x64
verifier using candidate libraries imports all eight published successor ZIPs,
checks former-ID migration with both enabled states and retained options, and
resolves file-component payloads. No native game code is executed. This follow-up
targets 0.1.58 and does not rewrite the published 0.1.57 package.

## Required component continuity (2026-11-06)

Repair absent selected predecessors from newer compatible installed packages while
retaining user enablement and supported options. Installed required components
are selected and enabled through profile policy. Completed deferred transfers are
applied before warning and recommendation checks; policy-derived required flags
do not invalidate user-choice stamps, including restored older pending records.
Intact pins, user edits, incompatible builds and version bounds remain protected.
Satisfied required offers need no write lease. Shop access is explicitly declared
store-managed so new clients can obtain it through the normal component installer.
All 294 focused definition, upgrade, restart, policy, component-installation and
client-lease checks pass with no skips and no compilation warnings. The final full
Core run passes 1,992 cases with zero failures and 20 opt-in/environment skips.
Its temporary directory is on the workspace volume; earlier runs exhausted system
volume resources or an inactivity deadline and are retained as unsuccessful runs.
The 8,200-file archive transaction and both earlier environmental failures also
pass in isolation. Publication and another-computer acceptance remain pending.

## Game language patch gates and optional locale visibility (2026-11-06)

FtHook 3.7.2 accepts a game-language session value and per-patch language filters.
The launcher supplies the selected game language independently of its interface
language. Chinese font-weight patches opt into Simplified and Traditional Chinese
only; all other game languages retain their original weight path.
File packages may expose boolean patch gates without making file deployment
conditional. The Chinese font candidate includes a generated private font with
22,073 retained base codepoints and 11,265 Korean codepoints; every retained and
added outline and horizontal metric was compared before and after serialization.
Original font inputs and generated payloads remain excluded from public source.

Game profiles may declare optional `unsupported_regions` interface locales.
The home selector updates immediately on locale changes without removing installed
data, and switches away from a hidden selection when idle. Existing profiles keep
this list absent. Definition validation and frontend locale-switch cases pass.

All declared Chinese build profiles now require the Chinese login repair through
typed client-policy data. KR-1256 also requires shop access alongside its existing
Korean login repair. Both independent repositories contain the same policy rows.
`require` and `recommend` are mutually exclusive for overlapping scopes in raw
policies and after profile/destination or former-ID reconciliation. Disjoint build
scopes remain valid. Required components no longer carry duplicate recommendations.

Aggregate presence metadata and roster completeness are additive. Incomplete
counts are presented as lower bounds, separately from local admission capacity;
incomplete or truncated public lists explain their limitations. Server deployment
and full user acceptance remain pending.

Validation: 192 focused managed cases pass with one opt-in public-store skip;
the Release compilation emits no warnings. The native runtime suite passes 196
checks, with host/loader/dump suites at 415/203/106. All 62 focused frontend cases,
type checking and production build pass. Fourteen publisher checks and an offline
import-ZIP integrity check pass; the generated font is not yet published.

## Launch transport and cancellation (implemented locally, publication pending, 2026-11-06)

Initial discovery respects explicit server routing and all-regions IP preferences
without assuming an unknown server is in China. Once discovered, server routing
requirements still control authenticated requests. Safe reads replay one transient
failure, while authentication, tickets and admission writes remain non-replayed.
Internal directory timeouts use the structured network failure contract and
startup cancellation identifies its failed phase without recording request data.
Transport failures racing updater shutdown no longer escape lifetime disposal.
Focused routing, session, timeout and lifecycle checks pass; the full first run's
six routing failures were corrected and pass in the subsequent 208-check run.

Online-player cards accept an optional public build ID and show the known localized
client name on a second line. Unknown IDs remain printable and older servers omit
the line. Aggregate population and historical peak values still require server
support; capacity is never substituted for historical peak.

## Plugin identity migration and retirement (implemented locally, publication pending, 2026-11-06)

Use manifest aliases for multiple former IDs, including later identity generations,
without a shared hard-coded rename map. Retain source-pinned downloads, enabled
state, supported settings and selection-safe deferred activation. Reconcile policy
rules and schemes. Runtime 3.7.2 accepts the same aliases as the managed reader.
Schema-two retirement records hide obsolete offers, remove writable
installations through the existing game-safe cleanup path, and present a localized
dialog with one plugin card per removal. Retirement follows identity across changed
sources and imported ZIPs. The local candidate targets Launcher 0.1.57, with the deprecated
boundary through 0.1.56; publication and live policy changes remain pending.
The focused managed run passed 284 checks with one public-store test skipped;
12 subsequent scheme, retirement and source-change checks also passed. The native
runtime and host suites pass, as do 415 frontend checks with three private-resource
skips, type checking and both production builds. Publisher composition/retirement
checks pass. A broader managed run exposed a separate background-update shutdown
failure in an offline fixture; the subsequent startup change repairs that disposal race.

## Launcher 0.1.56 user testing (published, 2026-11-06)

Published the reviewed startup ownership, optional-resource validation, resource-edit
conflict and Overlay document-boundary fixes for user testing. Includes the patched
translation dependency and portable-symbol exclusion. Uses the complete private
distribution inputs, retaining existing game artwork and the deprecated boundary
through 0.1.51. Bundles archive-cn.600819.xyz with IP-preferred access for new
installations while preserving customized update sources. Official publication targets Windows x64; ARM64 remains a maintainer
test target. PR #60 merged the implementation at aa9991e; the normal package was
built from that commit and published at revision 1793961979.

The main and preview feeds, both archive companions and all 593 unique content
objects passed anonymous full-content or exact-metadata verification. The 1,012
indexed destinations retain all 69 existing game resource files. The complete
169,955,257-byte portable ZIP has SHA-256
`70a60cb24df65e5e169c9a7c7f16718324297e4003366f4c0e1401b5d9e9b91d`.

The unchanged 0.1.55 reader with its normal Host transport resumed all 35 changed
destinations and restored Ready offline without requesting unchanged objects.
That resume reused four verified content objects and downloaded the remaining
23; its duration is not a fresh-download benchmark. A separate fresh public
fallback test injected incremental failures, downloaded the real complete ZIP,
verified all expanded contents and restored Ready offline. Both tests preserved
the installed source policy and did not install or launch the update. Actual game
and interactive Windows elevation acceptance remain part of user testing.

## Translation dependency security update (published in Launcher 0.1.56, 2026-11-06)

Pin Vue I18n and its matching internal packages to official 11.4.18. The previous
11.4.12 lock was affected by translation HTML sanitization advisories; the five
high-severity audit entries described one dependency tree, not five demonstrated
application exploits. Launcher translations use text rendering, with no identified
translation-to-HTML sink. Keep that boundary and test the real six-language catalog,
named/list interpolation and the upstream fixes. Strict type checking, production
and game UI builds pass; 413 frontend tests pass with three private-resource skips.
The refreshed npm audit reports zero known vulnerabilities. Photino is unchanged.

## Source-only export and distribution hygiene (reviewed, publication decision pending, 2026-11-06)

Separate synthetic frontend/native checks from optional private resource integration tests.
Export an explicit SHA-256 file list from one immutable commit without copying Git history,
ignored state or nested repositories. Empty the exported preview level catalog, retain
optional resource declarations and exclude private assets in the reviewed list. Reject
local state and debug output before distribution verification. These mechanisms do not
complete historical disclosure, dependency provenance, browser bridge or real-client review;
public visibility and new releases remain separate approvals.

The external WPF overlay now validates the event's document source before updating
readiness/input state, cancels other top-level documents and handles popups. The
same event policy passes 29 real WebView2 checks on each of Windows x64 and ARM64,
with 45 focused managed checks. The main Photino window remains a separately
documented limitation; third-party source is read-only review evidence.

The final application-source candidate contains 1,137 files with 72 explicit
exclusions. Its separate build directory completed self-contained x64 and ARM64
packaging, including official Photino identity, runtime notices, decoder source,
detached updater execution and independent package/object/archive checks. The
latest managed implementation passed 1,937 tests with 16 environment skips in
the root working tree; its fresh export also built with zero warnings and errors.
The final frontend passed 413 tests with three private-resource skips. Whole-history
publication, the main Host boundary decision and real-game/UAC acceptance remain
separate from these completed local review checks.

## Shared module framework identity (published in Launcher 0.1.55, 2026-11-06)

Managed game modules resolve framework assemblies from the host before consulting adjacent package files. Private module dependencies retain their package isolation. A real self-contained 0.1.54 distribution reproduces a host network exception bypassing the module's catch filter and stopping Gateway; the fixed loader recognizes that same exception and performs the next refresh. A subprocess regression carries an adjacent framework copy and checks the actual host callback exception. Selection-refresh diagnostics retain only a fixed error category and numeric HTTP status.

All 205 focused checks pass. The complete Release run with the pinned Gateway enabled reports 1,881 passes, 16 environment skips and one room-shutdown timeout; both cases of that unchanged shutdown test pass on isolated retry. The maintainer confirms that cn-706 now reaches the native chooser and enters the game after updating to 0.1.55.

The incremental feed, preview alias, all 593 content objects, complete portable ZIP and automatic archive fallback pass independent anonymous full-hash verification. The unchanged 0.1.54 update reader stages 22 changed destinations using 16 objects, reuses 990 installed destinations without unchanged-object requests and restores update readiness offline. The feed SHA-256 is `fab8cc3ec10de1b20919186bdcef8743adc1d51ad67f2f0d5a2538a514faac47`; the complete ZIP SHA-256 is `a03f1fe80e7bbfd67f7c5f49243ef8d03ccb944ef3f5ccb2fc8d0835f36fa852`.

## Chinese 2014 startup compatibility (published in Launcher 0.1.54, 2026-11-05)

Keep China 2014-09-23 visible and hide 2014-04-03 and 2014-05-26 through profile `hidden` fields. Allow file components to carry build-scoped memory patches without changing file deployment ownership. The Chinese authentication component contains only the cn-706 retired startup-chain repair; no identity-report patch is included. Native readers validate localized warnings and file metadata, and reject file-only packages from native activation. Offline checking confirms the unique startup site and its original continuation. Original-client local-listener testing passes the former Update Error and plugin-loading dialogs; actual server login remains unverified. The Release solution builds with zero warnings and errors; 118 focused managed tests and 184 runtime, 203 native and 415 host checks pass. Launcher 0.1.54, authentication component 1.0.16 and hidden profile metadata have been published and anonymously verified. The managed module refresh failure discovered during formal launch is tracked separately above.

## Bundled native payload repair (published in Launcher 0.1.53, 2026-11-05)

Launcher 0.1.53 restores the game control and diagnostic libraries missing from the 0.1.52 distribution.
Packaging explicitly stages bundled plugin DLLs without changing store releases or their immutable manifests.
The release publisher rejects missing, empty or publication-excluded native libraries before creating output,
including optional components that are not selected on the packaging machine. Regression tests cover all three
failure cases and successful publication after staging the missing payload.

The Release solution builds with zero warnings and errors; 118 relevant managed regressions, 138 game-control
checks and 106 diagnostic dump-writer checks pass. The portable ZIP contains both x86 libraries, and independent
anonymous verification confirms all 593 content objects, the primary and preview feeds, the complete archive and its fallback
metadata. The unchanged 0.1.52 reader discovers and stages 24 changed destinations using 18 objects, including
both restored DLLs, reuses 988 installed destinations and restores update readiness offline. The release feed
SHA-256 is `08fce77a01899a3eebd0536f54d4b394f37ab00e6cfcbcfd4ccfb0c9d57c24f6`;
the complete ZIP SHA-256 is `c2a6091e53df46ef7a904165f8680ec4687343da045c9604bb3f636586d7ecb4`.

## Launcher 0.1.52 release (published, 2026-11-05)

The public incremental feed, preview alias, complete portable ZIP and automatic archive fallback passed
independent anonymous verification, including all 591 unique object hashes. The unchanged 0.1.51 reader
successfully discovered and staged 36 changed destinations using 27 content objects, reused the other
974 destinations without payload requests and restored the ready update with networking disabled.
The release deprecates versions through 0.1.51.

All 47 published plugin versions now offer root-importable ZIPs. Each ZIP was reconstructed from its
original hash-pinned release, uploaded and anonymously verified before additive catalog registration.
The shipped 0.1.52 reader accepts all 59 catalog packages, including those 47 ZIP offers; every original
catalog field and immutable manifest remains unchanged. ZIPs use one game directory with plugin and
version in each filename. Missing optional ZIPs retain individual-file installation.

## Importable plugin ZIP publication and preferred installation (published, 2026-11-05)

Publish a deterministic, human-downloadable ZIP for every plugin version alongside its immutable release.
Prefer catalog-pinned ZIP transport, verify all extracted members against the original file manifest,
and retain individual-file download when the ZIP is absent. Persist either transport's progress without
changing package receipts. Backfill historical ZIPs from verified published objects and commit additive
catalog metadata only after anonymous verification. Launcher 0.1.52 retires versions through 0.1.51.

## Composable client resource edits (published in Launcher 0.1.52, 2026-11-05)

Provide build-aware item, string, horizontal table and native UI edits through data manifests and a managed capability.
Resolve aliases to physical addresses before merging, check conditions against original content, and transport
snapshot-bound conflict reports and retry decisions through the host. Keep member and address caches independent of
the archive composer. Whole-member choices remove every losing claim, including provenance; staged field decisions
can resolve conflicts revealed by an earlier member choice. YAML strings and unproven UI visibility remain explicitly
unsupported. The frontend shows each conflicting plugin as a card and requires explicit choices before retrying;
canceling or expiring a launch dismisses the prompt, and changed snapshots discard previous choices.

The Release solution builds with zero warnings and errors. Resource, character-model and bridge regressions pass
157 cases with one environment-gated skip, including three acceptance tests against copies of real kr-812 archives.
The original client archives remain unchanged; these checks do not establish in-game display acceptance.

## InfoBar notification lifetime (implemented, pending release, 2026-11-02)

Use a shared six-second lifetime for transient info, success and warning notifications. Errors remain until dismissed by default. Callers can request persistent notices or another duration; hover and keyboard focus pause expiry. Replace repeated operations with a fresh notification identity and ignore stale close callbacks. Keep state-dependent inline banners persistent. Hold this change for the next Launcher release.

Frontend type checking and production build pass. All 386 frontend tests pass, including expiry, severity changes, explicit persistence, interaction pauses, replacement and stale-dismissal regressions.

## In-game HTTP routing (implemented, pending release, 2026-11-02)

Apply the existing server network policy to Messenger and game sign-in addresses before writing the launch session. Plain HTTP follows the selected IP preference; HTTPS retains its certificate hostname. Rewrite only the authority while preserving path, query and fragment text. Release 0.1.50 bundles the existing reviewed Gateway pin and retains current client and plugin selections.

## Legacy security identity-report compatibility (retired, 2026-11-06)

The optional identity-report package is retired at the maintainer's request and excluded from further store publication. Catalog withdrawal retains all unrelated package versions and metadata. The authentication component carries only its independently verified startup repair; the retired identity-report implementation is not incorporated into it. Historical implementation details remain in the [package contract](../store-plugins/BubbleFighter/bubblefighter.fantasy.legacy-security-compatibility/README.md).

## Bounded and responsive game startup (implemented, pending release, 2026-11-02)

Remove full-library maintenance from launch prerequisites and download polling. Hash immutable packages outside catalog and selection locks, reuse validation per activation pass, and activate only the requested client's pending selections before launch. A shared one-minute deadline and a second-click cancellation stop late startup continuations. The release must pass Gateway process interoperability against its exact pinned payload, including both Windows architectures.

## Live plugin configuration and update feedback (implemented, pending review, 2026-11-02)

Permit plugin configuration and logical uninstall during running sessions without changing deployed files. Track activated scheme versions and release stale card browsing after upgrades. Present missing startup components as cards with Enable and continue, Start anyway and Cancel actions. Refresh the shared update inventory on package events and display automatic completion through the four-severity InfoBar notification path. Native sessions remain immutable and removed payloads retire only after client leases are released. The managed suite passed 1,754 tests with 17 environment-gated skips; the final plugin subset passed 304 tests with one gated skip. Frontend plugin and store regressions passed 100 and 33 tests respectively; type checking and the production build passed.

## Native directory transient failure recovery (implemented, pending release, 2026-11-02)

Retry transient native directory DNS and connection failures before freezing disabled entries. Share one deadline across up to three attempts, retain caller cancellation and fixed bindings after publication, and add sanitized selector/attempt/result diagnostics to both Gateway launch paths. Targeted directory and Automatic tests pass 98 cases. Original-client CN2011 full/disconnect attribution remains open and is not established by resolver tests.

## Launcher 0.1.45 release (published, 2026-11-02)

Publish the index-only deprecation policy, plugin selection reconciliation and the reviewed Gateway rebuild together. Forward explicit Center short names and retain only fixed auxiliary and Automatic diagnostic metadata. The native caption adapter remains disabled in the build descriptor until a replacement passes original-client validation; the caption projection and native authentication remain separate capabilities. Completing that repair remains open.

The exact-source Gateway rebuild passed 495 tests and both architectures passed 42 bootstrap and 196 automatic process scenarios each. Launcher bootstrap tests using the delivered executable passed all 70 tests without skips. Native login continuation for CN2012, CN2013 and CN2015 remains open and is not established by these process checks.

The release index, preview alias, portable archive, automatic archive fallbacks and all 593 unique object hashes passed anonymous public verification. The separately published Chinese authentication component 1.0.11 now provides verified native provider continuations; original-client acceptance remains pending.

## End the launcher deprecation transition (reviewed, pending release, 2026-11-01)

Publish deprecated_max_version directly in the release index. Remove the separate policy generator, download and restore paths, and the packaging transport switch. Reject retired policy input during publication so stale build files cannot reintroduce it. Keep inclusive semantic-version enforcement, offline restoration from the verified index, and additive unknown-field tolerance. The next Launcher release carries this change.

Independent read-only review found no blockers. The Release solution build passed with zero warnings and errors; the managed suite passed 1,733 tests with 17 environment-gated skips. Update regressions passed 233 tests with one gated skip. A strengthened indexed-policy regression separately confirms that discovery does not fetch a retired policy payload and offline restoration still enforces the index boundary.

## Plugin update selection reconciliation (implemented, 2026-11-01)

Read authoritative client selections after a package finishes installing, including when an older inventory request was already in flight. Capture the installed release before reapplying the active scheme; preserve enabled state, plugin options and unresolved imported selections. Both stale-selection regressions reproduced the downgrade before correction and pass afterward. Launcher publication remains pending.

## System dump library isolation (published, 2026-11-01)

Diagnostic mode 1.2.2 loads the dump writer by its full system-directory path instead of relying on search flags after a same-named game library is loaded. A standalone fixture reproduces bare-name library reuse and verifies real system exception and fallback snapshots while that incompatible library remains loaded. The writer suite passes 98 checks; original-client CN2011 fault attribution remains open.

The public catalog, immutable snapshot, archive and every package member passed anonymous verification for Diagnostic mode 1.2.2. A remote CN2011 exception dump now reports a successful system-library write; successful capture does not establish a gameplay fix.

## First-fault evidence and dump fallback (published, 2026-11-01)

Diagnostic mode 1.2.1 retains the first serious fault when earlier handled exceptions exhausted the count, records context flags and bounded dispatcher/SEH evidence, and retries system dump error 87 without an exception stream. The fallback status distinguishes a process snapshot from an exception dump. Original-client CN2011 failure attribution remains open; diagnostic success is not gameplay acceptance. Release Win32 builds passed without warnings; the dump writer passed 92 checks and the native parent suite passed 203 checks with all child scenarios. Independent review found no blocking issues. The public catalog, immutable snapshot, archive and every package member passed anonymous verification for Diagnostic mode 1.2.1.

## Local hosting lifecycle and plugin schemes (implemented, pending release verification, 2026-11-01)

Save hosting settings without starting a server; launch private servers only when the player starts a game and stop them on game exit, destination changes or launcher exit. Permit running-server settings with an explicit restart decision. Keep server information and a bounded command terminal in the same detail pane.

Remember plugin scheme selections by server and client build, while each named scheme stores independent build settings. Import and export TOML through the managed parser. Preserve unresolved selections, offer only available store packages for installation, and apply newly installed selections from the application shell even after navigating away from the import dialog. Default schemes cannot be deleted; custom deletions require confirmation.

Release 0.1.44 remains unpublished until final review, the distribution checks and anonymous public verification complete.

## Installed-client state and actionable exit failures (implemented, pending review, 2026-11-01)

Retain installation inventory when applying saved plugin settings without client scanning or resource preparation.
Preserve observed process exit codes across bootstrap cleanup failures and expose both through the existing error details.
Trust issuer online readiness and real TCP probes instead of heartbeat observation timestamps, retaining immutable admission.
Targeted process, notification, inventory and Automatic regressions pass; original-client crash diagnosis remains separate.

## Optional native Center titles (implemented, pending release verification, 2026-11-01)

Project directory labels by native selector identity for the audited CN2011 title adapter.
Reuse the generic module metadata envelope and the existing bootstrap lease; do not change
Gateway wire data, routing, admission or the shared host. Missing names keep native titles.
The Chinese authentication component owns the optional callsite adapter and the separate
CN2012 through CN2015 native passport repair. Verify both release artifacts before recording publication.

## Native CN2013 Automatic marker repair (implemented, 2026-11-01)

Remove wall-clock skew from heartbeat admission, retain failed manual destinations with native disabled status, and remove fixed directory-count limits. Include background store update counts, installed status and single-item progress outside information cards. Release 0.1.42 includes these changes, the Home update shortcut and the simplified Prefer IP editor. Its incremental feed, complete portable archive, preview alias, all 594 unique object hashes and both automatic archive fallback manifests passed anonymous public verification. The final managed gate passed 1,710 tests with 13 skips and zero failures; all 360 frontend tests, type checking and production build passed. Remote destination authentication and original-client gameplay acceptance remain separate deployment evidence.

Include bounded candidate-state change diagnostics to distinguish missing or stale
source observations from failed player-side probes. Derive the native unavailable
status capacity from the authorized Automatic pool. Keep single healthy candidate
routing covered independently of the unavailable-status fallback. The deployed
server separately verified six-selector grants and refreshes, including scoped
ticket redemption for each independent manual destination. Original-client
gameplay acceptance remains separate.

Consume the reviewed Gateway protocol-data repair for the native empty marker
sent by cn-406 during Automatic status startup. Preserve native manual handoff
and the absence of selector hooks. Include the client wire build in bounded
lifecycle diagnostics so subsequent reports identify the actual protocol profile
without account or ticket data. Final artifacts, bundled process checks and
anonymous public verification passed for release 0.1.42.

## Gateway Automatic teardown ordering (implemented, 2026-11-01)

Bundle the reviewed Gateway update that retires Automatic state before closing
its accepted native socket. A concurrent health refresh observes the retired
state before connection EOF. Keep native redirect, admission and manual server
selection contracts unchanged. Both architecture artifacts retain exact source,
capability and redistribution notice verification. Release 0.1.41 carries this
update; original-client acceptance and unrelated remote crash diagnosis remain
separate from protocol and process tests.

## Complete manual Center directories and actionable diagnostics (implemented, 2026-11-01)

Keep canonical manual destinations separate from the narrower launch-bound
Automatic grant. Verify admitted bindings against the directory, retain independent
manual authorities, and never expand the automatic pool or its credentials.
Independent authority login still requires its own reviewed authentication contract;
showing a directory entry does not establish that it accepts the current ticket.

Expose cleanup stage, process state and bounded sanitized Launcher events from the
error InfoBar through View details, Copy details and Open log actions. These actions
do not require a diagnostic plugin. Remote-client failures remain unconfirmed until
the affected run's records are available.

The bounded regression and frontend production build pass. Release 0.1.40 includes
these Launcher changes. Remote crash diagnosis, native label verification and
independent-authority login acceptance remain separate.

## Launcher 0.1.39 native Center login (implemented, 2026-10-31)

Use the original chooser and native Center/MoveChannel redirect protocol for
Gateway Center login. Remove selector rendering and click adapters; retain only
the client authentication adaptations required by each audited build. The
packaged Gateway descriptor declares the supported bootstrap and Automatic builds.

Center is disabled unless the server explicitly enables it. An enabled Center
defaults to Gateway; explicit native mode keeps Center on the server. Ordinary
single-server login does not require directory or automatic-admission APIs.
Manual directory entries retain their own physical endpoints. Automatic selection
uses only the server-authorized pool and cannot expand the launch grant during
health refreshes. Stop Gateway after verified native handoff while the game
continues running. Reserve launch-scoped Gateway start and stop contracts for
future Nirinsou Link consumers, without registering runtime control handlers.

Show resource preparation below the launch controls, defer it until launch, and
reuse verified unchanged output. Match plugin settings to the application theme.
Preserve original resources after interrupted runs and report cleanup failures
with stable stages and localized recovery guidance.

Discover plugin updates across enabled stores while retaining immutable package
hash validation. Verified pending activations survive source changes. Use closable
InfoBars with first-line icon alignment and distinct semantic badge colors.

Managed regression, independent Gateway integration review and pinned dual-architecture packaging are complete. Launcher 0.1.39 and authentication component 1.0.7 are public, with incremental objects, the complete portable archive and automatic archive fallback verified anonymously. Server deployment and its minimum-version switch are coordinated separately.

## Launcher 0.1.38 release integration (implemented, 2026-10-30)

Bundle hash-pinned x64 and ARM64 Gateway binaries and redistribution notices.
Bootstrap changes the game's transport endpoint while native authentication keeps
the selected server's credential endpoint. Independent real endpoints are enabled;
automatic/manual aliases sharing a destination remain rejected until the native
selector bridge is complete. Six-selector acceptance remains open.

Launch recovery removes missing optional package selections with a user notice.
Inventory refresh and plugin upgrades retain selections, including offered packages
awaiting download. Declared former plugin IDs retain compatible options. Deprecated
Launcher 0.1.38 required the normal update flow through a transitional policy
file; the next release ends that transition by publishing the boundary in the main index. Additive unknown
index fields no longer prevent update discovery. Version definitions retain only
locally evidenced region/wire identities, including cn-408.

## Explicit server transport selection (implemented, 2026-10-30)

Retain HTTP defaults and both transport choices. Advertised secure addresses are
used only when the existing Use HTTPS preference is enabled. Profile persistence
and actual launch requests are covered by 29 passing transport/login tests.
Cluster authentication must use the same configured transport semantics.

## Archive domain migration and optional IP access (implemented, 2026-10-30)

Retained unsigned bundled Launcher update policies also resolve to the new endpoint without changing their saved source identity, staged transfers or accepted revisions. Custom paths and signed policies keep their authority.

Bundled archive and server endpoints move to the new domains. Store and update source editors persist an independent IP-first HTTP choice, with enabled defaults for the bundled endpoints. Synthetic proxy DNS results are replaced by public addresses; HTTPS preserves certificate validation. Existing retired bundled endpoints migrate without changing installed clients or plugin configuration.

## Launcher download completion prompt (implemented, 2026-10-29)

Verified ready updates automatically open a separate installation confirmation,
including background transfers and restored staging. The message distinguishes a
completed download from discovery and offers Update now or Later. Dismissal keeps
staging ready and suppresses repeated prompts for that manifest during the session.
Only explicit installation starts handoff; stale tasks, failed verification and
late superseded polls cannot start installation. Update-flow regression tests and
an actual browser preview cover background completion and dismissal/reopening.

## Store background refresh (implemented, 2026-10-29)

Retain successful catalogs and plugin inventories in memory for this frontend
session. First discovery keeps the existing loading presentation; revisits and
refreshes keep cached items and actions available with an inline progress ring.
Source identity changes invalidate only that source's display snapshot. Successful
empty results replace old entries, failures retain them with visible errors, and
superseded requests cannot overwrite newer results. Keyed card additions, removals,
reordering and release changes animate, respecting reduced-motion preferences.
An isolated development preview exercises delayed refresh and new arrivals.

## Startup recovery and local play flow (implemented, 2026-10-28)

Restore selected self-update transfers before fresh discovery. Isolate rejected
installed packages, expose their errors and discover replacements through their
original source receipts. Imports target the shared game library with no client
selection. Single-player reuses ready private sessions, presents one selector
entry and requests a remembered nickname instead of account login when
authentication is off. Startup errors expose bounded invocation output.
Setup dismissal remains available during asynchronous startup and invalidates
automatic connection continuations. Log details follow the newest output without
resetting on equivalent status snapshots. Rejected-package InfoBars expose only
receipt-backed update discovery, support another compatible installed build, and
offer confirmed removal under all affected client leases.


## Plugin maintenance interactions (implemented, 2026-10-28)

Plugin dialogs remain dismissible during admission, copying and configuration.
Revision guards prevent late results from closing or retargeting a newer dialog.
Client, server and plugin downloads display shared task progress inline, including
indeterminate preparation before measured transfer data exists. Plugin removal
confirms one identity and removes all its managed releases under affected client
leases. Updates preserve disabled selections while switching versions; default-on
older-release cleanup is a persisted independent update preference. Settings
cards reserve the expander-arrow column so adjacent controls align.

## Plugin configuration initialization (2026-10-27)

Custom settings pages receive detached JSON snapshots of the launcher's reactive
option definitions and draft values. This fixes missing captions and disabled
controls when structured cloning rejects a Vue proxy. Regression coverage uses
the actual component setup and a structured-clone message boundary for initial
load, page readiness, locale/draft refresh and validated page changes.
All 71 plugin frontend tests, type checking and the production build pass.
An isolated browser check using this component and the Resolution Extension HTML
confirmed initialized captions, adding/removing entries, draft synchronization
and locale refresh with the existing sandbox policy.

## Unified profile client policy (implemented, 2026-10-27)

Game definitions, bundled builds, store profiles and server destinations now use
the existing six typed `clientPolicy` rules. The 23 existing declarations were
rewritten manually with exact build scopes. Package activation selectors, build
required/recommended lists and the old recommendation bridge are removed, with
no migration reader or compatibility aliases. Source ownership and initial
default selections remain distinct from policy. See [client policy](client-policy.md)
and D33. Native game acceptance remains the owner's separate verification step.

## Local play and installation progress (2026-10-27)

Implemented measured progress in plugin installation/update confirmations, client
component installations, server package setup and local plugin imports. Dialogs
retain their admitted tasks, offer retry/resume and wait for completed snapshots
before continuing a launch. Client download discovery preserves structured errors
for the details action while expected authentication failures remain concise.

Single-player starts a compatible installed server with default local saves,
available ports and explicit name-only login, then selects its registered client.
Missing server packages require installation consent. Account authentication is
optional, existing profiles retain their choice, and advanced settings are collapsed.
The adapter reads client-relative resources at their actual installation root,
preserves legacy gate parameters and configures modern relay listeners. Runtime
3.6.0 adds explicit name grants with authentication extension revision 2; KR auth
0.1.3 and CN auth 1.0.2 support them. Actual packaged CN2011 server startup, the
name-only API policy and clean port release passed an isolated memory smoke.
Native suites passed; the final managed suite passed 1,335 tests with twelve
environment-gated skips, and all 302 frontend tests passed. Managed/native Release
builds have zero warnings; frontend type checking and production builds passed.
Actual client gameplay acceptance remains pending.

## Native game controls (in progress, 2026-10-25)

Implement actual native controls through Game Control Support, starting with
kr-812, followed by kr-1256 and individually verified adapters for other builds.
The requested operations are configurable message boxes with button results,
text-only notifications, automatically expiring translucent announcement banners, and local
chat/system entries with colored text. Add a localized developer test tab for
all four control families. Keep fallback policy in Communication; Companion
integration follows separately. Evidence and acceptance requirements are recorded
in `docs/hook/native-game-controls.md`. Static research has identified kr-812's
generic message box, transient notification and server-notice/chat paths. The
maintainer confirmed kr-812 message boxes, notifications, icons, expiring banners
and local chat in the game.

The shared contracts, launch-local service channel and six-language developer
test page are implemented. The tab is named `游戏组件测试` and follows source
management. Capability refresh preserves live operation polling, stale process
results are discarded, and malformed provider replies cannot report success.
The native package now registers `game.native-ui` through runtime 3.5.0, with a
kr-812 adapter for one/two-button message boxes, the four resource-verified icons,
text notifications, native expiring banners and uniform-color local chat.
The kr-1256 descriptor supplies independently verified message-box, notification
and uniform-color chat ABIs; its banner path remains unsupported. Mixed-color
runs within one chat entry remain unsupported on both adapters. The test page
limits icons, buttons and color controls to the active provider's capabilities.
Announcements use the game's own lifetime and fade without periodic replay or
a Launcher timer; the current adapter does not expose explicit banner closure.
Presentation is dispatched onto the game window's message thread; modal results
are polled without blocking Launcher. Caller-scoped result retention, duplicate
suppression, queue backpressure and late-callback isolation have focused tests.
The previous notification receiver moves to Communication and resolves Overlay
only on an actual notification. Native-first Companion routing remains pending.

Managed Release builds without warnings; 46 focused managed tests, thirteen native-UI/color
frontend interaction tests, frontend type checking and production build pass.
Native checks pass: 54 control-service, 23 service registry, 126 runtime, 26 host,
35 dump-writer and the complete native loader suite. The kr-812 descriptor's eight
and kr-1256 descriptor's six function signatures match their local images, as do
both timestamps. A full managed run found
a transport/notification-composition regression, fixed and covered by the focused
rerun, plus a startup store automatic-update timeout that also reproduces alone
(one case passed, one failed). That run was stopped after seven
minutes without a final suite result. Full managed acceptance remains open.
The maintainer accepted the kr-812 additions in the game. All kr-1256 operations
still need actual game acceptance. Mixed-color chat and additional builds remain open.
No release acceptance is claimed.

Follow-up accepts empty and whitespace-only message-box labels, preserves
multiline notification text and exposes caller-provided local chat captions.
The developer page adds a visual chat color picker, synchronized six-digit hex
and RGB input, fourteen verified native palette presets and persistent custom
colors. Local color preferences contain no message text or account information.

## Plugin presentation and native UI boundary (2026-10-25)

Updated localized names and descriptions for Chinese font support, Fantasy Tale
Game Control Support and Fantasy Tale Native Style Components, including browser
installation previews. Game Control Support retains its existing Overlay delivery.
Native presentation is reserved at that boundary: managed orchestration, a
game-local native adapter, per-operation capability reporting and Overlay fallback
only after a definite unsupported or not-presented result. Native hooks and a
managed plugin-package loader remain unimplemented, as explicitly deferred.
The five-package distribution and explicit four-plugin defaults are complete.
Optional and research packages are outside the host distribution; Forward Jump
is excluded from publication. Store releases for all five bundled identities,
shop access, verification removal and Chinese font support 1.0.4 are published.
KR1256 store profile revision 3 recommends shop access. Existing manual choices
and newer store versions are preserved, while newly compatible defaults and
newer launcher-bundled packages reconcile automatically. Retired required flags
are refreshed from the current package and build policy.

The full managed suite passes 1,273 tests with 11 environment-gated skips; the
final plugin-policy rerun passes 196 with one public-network test skipped. All
277 frontend tests pass. Release 0.1.28 builds without warnings, and the x64
package passes managed-version, detached-updater and complete-payload checks.
Daily deployment verifies all 991 indexed files and retains 60 client, account
and settings files byte-for-byte before first restart. Actual Windows rendering
confirms version 0.1.28, five built-in identities, enabled defaults and diagnostics
off. Existing separately installed packages remain available without built-in
badges. Logs and screenshots are under artifacts/github-mirrors-20261025.

## GitHub mirrors and settings (2026-10-25)

Completed connection settings for GitHub mirror acceleration with aligned Win11
expanders, source selection, concurrent probes, per-result Use actions and
transfer fallback. The dynamic mirror directory is refreshed without excluding
failed local probes. Country detection runs once independently of prior launcher
installations and offers mainland-China users a default-on switch with one
confirmation action. It does not silently enable acceleration. Overlay testing
uses the same expander, with the outside-click option inside it. All six
languages and the paired user guides are updated. The installation-directory
shortcut is hidden, leaving client management as the existing entry point.

## Local hosting build selection (2026-10-25)

Completed the missing local-play versions repair. Twelve enabled server-backed
builds now declare the existing shared hosting adapter in bundled and published
store definitions; the disabled cn-735 build remains unavailable. The server
package and GitHub subscription advertise those same builds and use the display
name BubbleFighter.Fantasy. Profile revisions advance for changed definitions;
the pinned release archive, digest and executable are unchanged. The deterministic
manifest digest advances with its build list.

Release compilation passes without warnings. Focused hosting/catalog tests pass
69 cases, with three environment-gated package/client cases skipped. All thirteen
server-profile configuration dumps passed during investigation; twelve enabled
builds are exposed. Actual Edge renders at 1280x760 and 960x600 show all twelve
options and allow selecting CN2011 from the initial KR selection. This validates
configuration and rendering, not complete gameplay for each build. Evidence is
under artifacts/hosting-catalog-20261025.

## Account connection feedback (2026-10-25)

Completed the account-restoration feedback requested after interactive startup
caching. Before initial account and sign-in metadata requests finish, Home and
its account picker show connection rings instead of implying that a saved session
is signed out. The blue primary button blocks duplicate login prompts without
blocking cached client/version selection. Valid accounts remain visible while
refreshing the same server. Empty, rejected and failed responses end the indicator;
request generations prevent stale server completions from clearing a newer state.
No credentials or additional account records are cached in frontend storage.

ContentDialog now has an optional primary busy state, used by manual sign-in to
show the same blue button and ring while preventing duplicate submissions.
Account display changes fade, and rings retain the shared reduced-motion behavior.
All six interface languages and paired account-session guides are updated.

All 270 frontend tests pass, and type checking, production compilation and the
zero-warning Release solution build pass. Edge-rendered fixtures hold account
restoration pending, verify version selection remains usable and no signed-out
frame appears, then exercise restored, signed-out and failed outcomes. Manual
sign-in verifies exactly one submission, a busy primary ring, and its successful
return to the character display. Screenshots and logs are under
`artifacts/account-connect-20261025/`. Native account/session regressions pass
105 cases with one optional package integration skipped. The daily frontend is
replaced in place and restarted successfully; 163 protected client/account files
remain byte-identical across replacement and deployed frontend hashes match the
production output. The native process acknowledges frontend readiness. No backend
or native runtime contract changes, and no public release payload is replaced.

## Interactive cached client startup (2026-10-25)

Completed the startup selection repair. The frontend previously restored only a
client identifier while its client/build lists were empty, producing a fictitious
default-version placeholder and an Add client action for an installed client.
It now persists public game/build display definitions, minimal client display
records and per-game selection. A warm startup becomes usable before the host
catalog response; users can select a cached version and request launch immediately.
Actual launch resolves the saved registration in the backend. Cached fields do
not rewrite client settings. A cold startup shows an honest loading state.

Local inventory and running-process responses update independently. Successful
inventory replaces stale records; failed inspection retains the display cache.
Late responses preserve a valid choice made during refresh. Client language edits
and registration editors still use full authoritative records rather than incomplete
cached fields. Installation paths, plugin options and account/session credentials
are excluded from the cache. No native runtime or backend contract changed.

All 262 frontend cases pass, including warm/cold/corrupt/stale-cache startup,
per-game restoration, early selection and a cached launch without an inventory
wait or a cached-settings rewrite. Type checking and production compilation pass.
Release solution compilation has zero warnings/errors. The managed suite reports
1,232 passed, eleven environment-gated skips and one external updater timeout;
the four cases in that updater theory pass on a focused rerun. This is not a claim
of an entirely green initial full-suite run.

Actual Edge renders hold both host game discovery and client inspection pending,
open the cached version flyout, choose a second installed client, and confirm that
the button remains usable and the selection survives later refresh. Warm, cold
and stale-cache screenshots and per-frame invalid-state checks are under
`artifacts/startup-cache-20261025/`. The daily installation's frontend is updated
in place; all 162 registration/configuration files are unchanged across replacement,
and deployed frontend hashes match production output. Its native process acknowledges
frontend readiness. The frozen public 0.1.27 payload is unchanged.

## Public x64 native startup-failure rollback (2026-10-25)

Completed for the previously verified public 0.1.27 payload. The existing isolated
acceptance installation was reused as a 0.1.26 baseline; no additional complete
installation was copied. Its entry-point seed enables local work mode and requests
the normal restart bridge only after the actual frontend-ready response. Seventeen
previously verified public objects are reused by hard link, so this run establishes
native replacement and rollback rather than another public payload transfer.

The normal external helper closes the original parent and starts the published
x64 candidate. The harness confirms the owned candidate's executable path and
creation time, then terminates it after its 0.1.27 startup event and before frontend
health. The updater reports a startup rollback and starts the restored 0.1.26
frontend, which acknowledges readiness normally. Continuous lifecycle capture
retains all three process identities, architecture records and transition events.

Independent verification confirms all 1,256 pre-update file sizes and SHA-256
hashes, including the source policy and independent installation file. Independent
data is preserved, uncommitted receipt/ownership records are removed, and no
transaction journal or original/next backups remain. The isolated restored process
was stopped after verification. The receipt is
`docs/release/public-native-rollback-acceptance-20261025.json`. Its fixture files
include the explicitly seeded entry point; this is not an unchanged-baseline UI
claim. No screenshot was captured. The isolated installation now retains the
restored baseline for further tests; the daily installation, user data, original
0.1.26 distribution, frozen 0.1.27 package and server deployment are unchanged.

## Public GitHub subscription download gate (2026-10-25)

Completed for public archive download, installation and task restoration. The
public repository returned HTTP 200 at 08:01 UTC with the
expected v0.1.1 x64 asset, size and SHA-256. Later real Launcher transport
requests returned HTTP 403 with zero anonymous quota and reset at 09:00:31 UTC.
The successful API observation does not establish sustained Launcher refresh
availability. The public AList catalog still provides its validated release pin.

A new opt-in acceptance uses the actual anonymous AList source, GitHub transport
and download queue without pre-populating staged files. It checks that browsing
does not fetch the archive, installs only after explicit admission, independently
compares installed payloads against a separately held archive with the selected
size/hash, and restores the completed task without network traffic. It records
metadata refresh status separately from installation. It starts no server and
cleans its isolated installation when it ends.

The first complete-download attempt timed out at the test's five-minute deadline
while still transferring; no hash or installation failure was observed. The second
attempt completes in ten minutes with a thirty-minute public-network budget. It
downloads all 45,069,097 bytes from GitHub and independently verifies 401 installed
payloads. Exactly one archive request follows explicit installation; task and
package restoration perform no network requests. Metadata refresh remains failed
because of the observed API quota, so this establishes public validated-pin
fallback installation rather than fresh Launcher metadata discovery. The receipt
is `docs/release/public-github-server-download-20261025.json`. Results are under
`artifacts/github-live-download-20261025/`. Release solution compilation has zero
warnings/errors; 39 focused offline cases pass and two public cases skip without
explicit settings. Paired store guides document reproduction and remove obsolete
metadata-byte/row limits and old-reader projection recommendations. The frozen
0.1.27 release, daily installation and server deployment are unchanged. Its
existing AList portable upload is still an unfinished running storage task.

## Managed module native-dependency acceptance (2026-10-25)

An SDK-only synthetic module now executes a real native archive export through
its own assembly load context. Two packages containing the same library name
each resolve their own directory, with and without a component dependency
manifest. The probe queries the held library handle directly and releases it
after the call. A missing native dependency fails without starting a game.
The existing production loader passes these cases without modification.

The first x64 run exposed a test packaging error: the fixture library was built
without a runtime identifier but copied from a runtime-specific output path.
The copy target now queries the actual library output and rejects missing output.
An intermediate ARM64 run also exposed unreliable diagnostic module enumeration
during parallel loads; direct handle inspection replaces that diagnostic.

After both fixture corrections, all 38 ARM64 module, instance and recovery cases
pass. All ten module cases also pass in an actual x64 child; both its reported
architecture and executable PE machine were checked. Release solution compilation
has zero warnings and errors. Results are under
`artifacts/module-native-boundary-20261025/`. Paired architecture guides explain
the distinction between Launcher-native dependencies and game-process Hooks.
This synthetic acceptance does not establish private-game integration, desktop
visual acceptance or gameplay. The frozen 0.1.27 payload remains unchanged.

## GitHub store quota recovery and public dependency (2026-10-25)

Anonymous discovery of the configured server repository returned HTTP 403 with
an exhausted API quota. A separate quota endpoint confirms 60 used, zero remaining,
and reset at 2026-10-25 08:00:28 UTC. This observation does not establish whether
the latest release or its configured archive is publicly available; no archive was
downloaded. The sanitized operational receipt is
`artifacts/github-rate-limit-20261025/public-dependency.json`.

The catalog cache now shares GitHub's 403/429 retry/reset deadline across its
sources. Repeated refreshes keep matching validated release pins without querying
GitHub before that deadline. Parent catalogs and ordinary package updates continue
to refresh. The provider deadline expires normally; shorter later responses cannot
reduce it. Ordinary denials and invalid or expired reset metadata do not suppress
the next query. The cooldown is scoped to the current Launcher lifetime, not a
persisted availability claim or a repair of the provider's exhausted quota.

Both quota and retry regressions fail against the old integration. With the fix,
103 store, source-selection, profile, GitHub and HTTP/AList wire cases pass, with
one opt-in public archive case skipped. Deterministic clock cases cover relative
and absolute retry dates, quota epochs, expiry and shorter responses.
Release solution compilation has zero warnings and errors. Results are under
`artifacts/github-rate-limit-20261025/`; the paired store guides describe the
runtime behavior and its limits. No source credentials, public catalog, frozen
0.1.27 package or daily Launcher installation changed.

## Stopped-client recovery during process initialization (2026-10-25)

A real Windows child with its primary thread created suspended reproduces an
inspection failure before its module loader initializes. The old recovery path
rejects an unrelated same-named image in another directory. This is an established
startup boundary; it does not prove the timing of the earlier complete-suite
failure, whose null-module observation was not instrumented.

Recovery now queries the Windows process image with limited query access instead
of enumerating its modules. The same-directory suspended child is still rejected
as running, retaining the session. The other-directory child no longer blocks
recovery. Inspection errors are ignored only after the enumerated process is
confirmed exited; unknown live processes still prevent restoration.

The before-change unrelated-child case fails, while the matching-child safety
control passes. All 32 focused instance, recovery and installation-cache cases
pass after the change. Release solution compilation has zero warnings/errors.
The complete managed regression passes 1,222 cases, skips ten environment gates,
and has no failures out of 1,232 cases, including the previously failing
stopped-session launch case. Results are under
`artifacts/process-image-recovery-20261025/`. These source-only changes do not
modify the frozen 0.1.27 package or the user's daily installation.

## Committed installation-cache cleanup (2026-10-25)

The public native update left 17 consumed objects (2,843,948 bytes) in the
installation transaction directory after successful commit. Recovery now removes
only the non-removal objects named by a durably committed journal. Rollback keeps
verified objects for retry, and unrelated staged payloads and independent files
are preserved. Duplicate destinations can share one object until every replacement
has finished. Committed-journal recovery also finishes interrupted cleanup.

Three new cases reproduced the leak before the fix; the rollback case already
passed. All four pass after it. Ownership fixtures stage each release after the
baseline commits, matching the downloader's consumed-cache lifecycle. Release
solution compilation has zero warnings/errors. The complete suite passes 1,219
cases and skips ten environment gates, but fails one unrelated stopped-session
launch case when a same-named process has no inspectable main module. The focused
instance/cache rerun passes all 22 cases. This is not a green complete regression;
the process-inspection failure remains a separate investigation. TRX results are
under `artifacts/launcher-0.1.27-x64/source-cache-fix-results/`.
This source-only repair does not change the staged or published 0.1.27 payload.

## Public x64 native replacement and health confirmation (2026-10-25)

The isolated 0.1.26 installation continued from its verified public download.
A temporary entry-point script enabled local work mode and requested the normal
restart bridge only after the real frontend became ready. The installed external
helper closed the original parent, installed the exact 0.1.27 public manifest,
started the new x64 Host and committed after its normal frontend acknowledged
health. The test did not simulate that acknowledgement. The entry-point seed is
replaced by the unmodified published entry point.

Independent auditing verifies all 1,008 installed hashes, exact generated frontend
asset membership, the durable receipt, preserved source policy and independent
installation/data files, and absence of transaction journals and original backups.
The isolated child was stopped after acceptance; the daily Launcher was untouched.
The receipt is `docs/release/public-native-update-acceptance-20261025.json`.
Initial lifecycle events were inspected live before bounded log rotation; the
receipt explicitly identifies the console-derived sequence and its one-second
precision. This run establishes native replacement and frontend health, without
visual screenshot, new rollback or production-signature acceptance. The separate
public portable storage task remains pending.

## Public 0.1.27 incremental transfer acceptance (2026-10-25)

Completed for staging and offline restoration. The x64 Core reader from the
0.1.26 local test distribution discovers the exact public 0.1.27 manifest and
automatically stages its 22 changed destinations. All 1,008 destinations pass
independent SHA-256 verification: 22 staged and 986 reused from the installation.
Only the 16 changed content objects are requested. Two timed-out payload requests
recover through the downloader's own retry path, with no manual task retry.
The transfer becomes ready again after reopening it with networking disabled.

The original 0.1.26 test directory had no update policy. An explicit public AList
policy was added only to the isolated copied installation; the original directory
and the user's daily installation/data were not changed. This is shipped-reader
acceptance with an isolated source configuration, not an unchanged-distribution
configuration claim. The sanitized receipt is
`docs/release/public-update-acceptance-20261025.json`.

Native-window continuation was not attempted: Computer Use initialization failed
with a missing kernel-assets path, including after a reset. No desktop restart,
health acknowledgement or rollback is claimed for this run. The separate existing
portable upload remains a live AList storage task; the incrementally published feed
does not establish successful portable publication. The staged release is unchanged.

## Plugin upgrade loopback fixture isolation (2026-10-25)

The source-edit upgrade matrix now accepts the configured changed API base path
and disables the unrelated public default store before constructing its backend.
The explicit refresh operation synchronizes enabled sources, so leaving the public
default enabled made a loopback test depend on live network access. All eight
HTTP/AList, loose-file/ZIP and required-component cases pass in seven seconds.
These changes affect the test fixture only; the staged 0.1.27 payload is unchanged.
The full Release managed suite passes 1,216 cases with ten environment-gated
skips and no failures out of 1,226 cases. Release compilation has zero warnings
and errors; frontend type checking and production compilation also pass.

## Developer card spacing (2026-10-25)

Completed. The developer options panel declares its own settings-group layout;
the settings page's scoped style does not apply across page components. Cards
use the same 4px gap as settings. Rendered screenshots at 1280 by 760 and 960
by 600 were inspected; type checking and the frontend build pass. This cosmetic
fix is excluded from the already staged 0.1.27 release payload.

## Account picker record removal (2026-10-24)

Completed. Each saved account has a trailing remove control. A shared WinUI
confirmation names the character and requires explicit confirmation before the
existing account-scoped logout removes the saved session. Server changes cancel
the confirmation; late results cannot change another server's selection. Login
identifiers remain hidden. Account tests (40), frontend type checking and the
production build pass. Rendered menu and confirmation screenshots were inspected
at 1280 by 760; the rebuilt frontend was deployed to the running local Launcher.

## Three-theme Overlay source integration (2026-10-24)

Completed for the external renderer and component bridge. The game module
resolves Bubble Fighter builds dated 2017-02-07 onward to v3 while preserving
earlier declared themes, capabilities and targets. The renderer accepts all
three themes. Shared extension settings report change/save/cancel events; save,
cancel, close and Escape hide their panel without applying gameplay changes.
The Vue source and reproducible build use the frontend's existing dependencies.
Game images, fonts and sound banks remain external local resources.

The imported source changed some default fonts. The integration retains the
previous v1/v2 BFMalgun body and v1 button selection, weight 400, four-times
Canvas sampling and logical output dimensions. Explicit canonical v1 button
skins retain their original action names, including lowercase and custom names.

Acceptance: Release solution and final Host builds have zero warnings/errors;
33 managed Overlay/module tests and 18 source/panel tests pass. Actual Overlay
HTML in headless Edge renders all three settings themes, changes mutually
exclusive choices, saves the draft, and cancels with Escape. Message-box
confirmation closes each theme; v1/v2 message-body PNGs are pixel-identical to
the prior library. Screenshots were visually inspected and are retained under
`artifacts/overlay-v3-20261024/`. The latest files were copied into the maintainer's
usual local Release directory and that Launcher process was started. Windows
capture returned an unrelated application after a timeout/reselection, so no
native-game/fullscreen screenshot acceptance is claimed. The v3 renderer
implements a subset of native controls; this is not public binary publication.

## GitHub server releases and executable-root discovery (2026-10-24)

Completed. An AList or HTTP catalog can subscribe to a stable GitHub server
release by repository, asset and executable. Refresh materializes a pinned tag,
size and SHA-256; tasks retain that exact selection. A verified catalog snapshot
keeps the package available during API outages or rate limiting. Publishing tools
preserve subscriptions without uploading GitHub payloads to AList.

Client and server ZIP installations find the unique configured executable root
after verified extraction, strip arbitrary nested wrapper folders and reject
missing or ambiguous roots. Client executable aliases in one directory share a
root. Other executables do not choose an installation destination.

Acceptance: the Release solution build has zero warnings/errors. The final
focused store/GitHub/archive suite passes 71 cases, and the publishing suite
passes 141. An integration test hashes and installs the actual 45,069,097-byte
server v0.1.1 archive through the download service using a previously downloaded
verified payload. The live GitHub API was rate limited; the test used the
catalog's verified metadata fallback. AList publication was anonymously verified
against the exact local catalog bytes; all 26 other packages and 13 build
profiles retain their original data. Results are under
`artifacts/github-server-store-20261024/`.

## Account names before character creation (2026-10-24)

Completed in the Launcher. Account summaries and selection menus display a
localized no-character label when the API reports `hasCharacter = false` or
provides no nickname. Responses without the flag that repeat the private login
identifier also use that label. Explicitly created characters retain their
nickname even when it happens to equal the login identifier.

Acceptance: all 36 frontend account tests, type checking and production
compilation pass. The latest native Launcher was opened and screenshot-inspected:
the existing created character retains its nickname, and local work mode keeps
the home media absent. No new remote account was created for this test. The
server contract change is isolated on `codex/launcher-character-status`; its
99 HTTP tests cover provisioned players before creation and refreshing after
creation. That branch has not been merged or deployed.

## Workspace startup with independent background loading (2026-10-24)

Completed. The splash waits only for local game definitions. Saved-client
inspection, server/account discovery, network preferences and store synchronization
start independently and update the visible workspace as they finish. A failed
background task cannot prevent the other areas from loading. Client inspection
checks paths from saved registrations; it does not search the user's disks.
Restored selections remain intact, and late startup results preserve build
choices made while inspection was pending. Server selection changes use the
existing watcher without issuing a duplicate explicit account/catalog refresh.

Acceptance: frontend type checking and production compilation pass. Three new
startup cases hold client inspection and store synchronization unresolved, check
independent failure handling and verify selection preservation. All 35 account,
nine navigation and 37 multiplayer/detail tests pass. Release solution and final
Host compilation have zero warnings and errors. The final generated frontend is
copied into the running Host output. Computer Use window capture timed out on
both the original attempt and a fresh-window retry; screenshot acceptance is not
claimed. Native lifecycle observations are retained under
`artifacts/startup-background-20261024/`.
The measured native run created its visible window at 350 ms and reported
frontend readiness after the entrance transition at 2,449 ms. All four initial
background requests were dispatched between 882.2 and 882.7 ms. This run's client
inspection took about 305 ms and store synchronization about 1,235 ms; the held
promise regressions establish independence when those operations remain slow.

## Account selection across queued launch preparation (2026-10-24)

Completed. The launch bridge captures the selected server session before waiting
for catalog synchronization, plugin checks or client metadata. Ticket acquisition
checks the same opaque selection again, rejecting account switches, replacement
logins, logout and endpoint changes instead of adopting a later account. Normal
token renewal and changes to unrelated accounts preserve the selection.

Acceptance: the Release solution build has zero warnings and errors. All 58
focused account and launch-bridge tests pass, including three loopback bridge
scenarios that hold catalog synchronization while changing the selection and
verify no ticket request, running client or session file is produced. All 35
frontend account tests pass. The expanded account, session, remembered-login and
version-contract regression passes 124 tests, with one optional real server
package case skipped. Results are retained under
`artifacts/account-launch-selection-20261024/`. No user game was launched.

## Routine server rejections without technical-detail actions (2026-10-24)

Completed. The shared failure notice hides technical details for expected
credential, registration, access-policy, capacity, maintenance and launcher-version
rejections. Their localized explanations remain visible, including maintenance
text and times; an outdated launcher retains the update shortcut. Network and
unexpected server failures retain the diagnostic action and copyable record.
Hidden actions no longer leave an empty action-row margin.

Acceptance: frontend type checking and production build pass. Browser inspection
of the actual shared Vue component confirms compact password and maintenance
notices, the version-update shortcut and working network details open/close.
The screenshot is retained under
`artifacts/server-failure-notice-20261024/preview.png`. This isolated rendering
check does not submit user credentials or change the running native Launcher.

## Source transition through external launcher replacement (2026-10-24)

Implemented. A valid release from another configured update source can now
replace a launcher previously installed from a different source. The helper
rechecks product identity and revision after recovery under the installation
lease, then pins the complete old receipt when authorizing the source transition.
Ordinary client transactions still reject unapproved source changes. Existing
ownership consistency, obsolete-file hashes and external-edit protection remain
required. Healthy commit records the new source; rollback preserves the exact
old receipt and restores replaced and removed files.

The process regression first reproduced four replacement failures before the
fix. It now covers HTTP/AList source replacement with optional signatures on and
off, plus HTTP 503 primary-source fallback with signatures. Sources stop before
offline task restoration and actual helper handoff. Record reads also tolerate
removal between observation and open or during a sharing-conflict retry, while
persistent conflicts and malformed records remain errors.

Acceptance: the complete Release solution build has zero warnings and errors;
216 update/install/record checks pass with one optional public AList test skipped.
Another 130 client installation, update, removal, archive and wire checks pass,
with three optional real-archive cases skipped.
The regression includes real helper commit and startup rollback, exact receipt
and ownership validation, subsequent same-source installation, preservation of
external edits, source settings and user files. TRX evidence is retained under
`artifacts/source-transition-20261024/results/`. These tests use isolated test
launchers and loopback sources, not desktop rendering or public publication.
The user's running Launcher was left undisturbed.

## Current native signed update and startup rollback acceptance (2026-10-24)

Completed for the local native paths. Two isolated 0.1.25 Windows ARM64
installations used the current Host, copied external helper and HTTP manifests
signed with disposable test keys. The healthy candidate changed one stylesheet;
after staging, the source returned 503. The real restart action still replaced
the object, reopened Home and committed after frontend readiness. The failure
candidate changed only the HTML entry point to a blank page without the health
bridge. Computer Use observed the blank native window; after the existing
90-second deadline, the helper restored the original entry point and reopened
the working Home page. The rejected candidate remained excluded from retry.

Independent audit verifies all 234 indexed destinations in each scenario,
including the restored baseline reconstructed from the frozen build and exact
local preference seed. Each scenario fetched one object. Source settings and
independent local files survive; journals and original backups are cleared.
Signatures are independently verified using the platform RSA API. No test
private key was persisted, no public feed changed, and the user's existing
Launcher/game were not stopped. Runtime evidence remains under
`artifacts/native-update-20261024/`; the sanitized checked-in receipt is
`docs/release/native-update-acceptance-20261024.json`.

The current updater regression passes 187 cases, with zero failures and one
optional public-network skip, in 3 minutes 10 seconds. This adds current native
commit/rollback evidence to the separate public x64 transfer and signed
HTTP/AList process tests. It does not claim production signing custody, signed
public-feed desktop acceptance, x64 portable acceptance for this build, retail
Hook gameplay, remote-player rooms or private-game module completion. The update
guide now also documents canonical version enforcement instead of the obsolete
packaging-override instruction.

## Overlay startup diagnostics and mouse activation (2026-10-24)

Implemented. A local developer setting enables the startup test independently of
installed Overlay plugins. The shipped default remains off. Its outside-click
forwarding defaults on, is persisted separately from game definitions, and is
also available per panel through the SDK. Modal ordering still excludes lower
Overlay panels; drag capture remains active until release. Native component
readiness waits for the resource-backed surface rather than an empty Vue mount.

Game-attached Overlay windows retain game activation for mouse interaction in
all display modes. Message boxes and dialogs dismiss on actions, including direct
component mounts, releasing their input regions. Standalone preview activation
remains independent. Keyboard text entry without deactivating the game remains a
separate input-path task.

Acceptance: the user confirmed the startup popup, outside-click forwarding and
the fullscreen activation fix in the actual game. The final all-mode extension
build has zero warnings or errors; 52 focused Overlay/service tests, ten browser
logic tests, eleven payload tests and the fresh helper provenance test pass.
Frontend type checking and production build passed for the developer controls.
Computer Use clicked OK on an actual resource-backed message box mounted through
`game-ui.mount`; the action receipt, hidden surface and idle browser suspension
are recorded in `artifacts/overlay-focus-20261024/overlay-dialog-dismissal.json`.
The all-mode extension has not yet been accepted inside the windowed game; the
user's current launch on the previous build was left undisturbed. External game
resource distribution remains separate from this local resource-root test.

## Helper output provenance and distribution regression (2026-10-24)

Completed. Overlay is an explicit non-referencing Host build dependency, so fresh
isolated restores include its assets without a separate manual restore. Copying
resolves the actual MSBuild target directory instead of a fixed ordinary `bin`
directory. The shared distribution verifier also checks the updater's private
Core, Downloads and SDK copies and Overlay's SDK copy; a current helper entry
assembly cannot conceal stale private dependencies.

Acceptance: a fresh Host build and the complete solution build with an isolated
artifacts path have zero warnings or errors. The helper regression verifies
copied executable, assembly, configuration, browser resource and dependency
hashes against that build's own output. Four version guard tests pass, including
rejection of stale private helper dependencies. A win-x64 self-contained publish
passes managed version, architecture, detached updater execution, portable WPF
runtime, compiled frontend and archive decoder checks. No manual component copy
is used. The focused update/account/room/module suite passes 268 cases with one
optional public-source case skipped. Logs and TRX results are retained under
`artifacts/build-helper-*`. The user's running Launcher remains undisturbed.

The separate explicit win-x64 public AList run passes without skips in 4 minutes
17 seconds against published 0.1.24, revision 1792832242. It downloads and verifies
all 993 files (388850295 bytes), then restores Ready with an unavailable network
handler. Its TRX is `artifacts/build-helper-results/public-alist-0.1.24-x64.trx`.
The initial ARM64 run correctly refused the x64 manifest; the accepted run uses
VSTest's explicit x64 platform. This accepts anonymous public transfer and offline
staging restoration, not public-feed desktop replacement, production signing or
rollback. Existing signed fixture/process acceptance remains distinct.

## Consistent local and release versions (2026-10-24)

Completed. `Directory.Build.props` is the canonical version for local builds and
release packages. MSBuild rejects inconsistent version overrides. Packaging reads
the repository value, rejects a different requested label before creating output,
and verifies the actual product version of each shipped managed component before
generating the feed. This prevents a local build from retaining 0.1.18 while a
separately overridden release reports a newer label.

Acceptance: four focused version tests pass, including actual assembly metadata
checks for matching and mismatched labels. The isolated 0.1.25 Host build has zero
warnings or errors, and distribution verification confirms matching components.
Computer Use verifies the running native About screen displays 0.1.25. This local
acceptance build does not replace the published 0.1.24 release.

## Safe client removal and completed-update acknowledgement (2026-10-24)

Completed. The client removal dialog reuses the download package card, with
accent file cleanup followed by registration-only removal and cancellation.
All six interface languages describe the two actions. Both actions retain
recovery evidence and refuse active client leases or matching external game
processes. File cleanup rejects drive roots, protected locations, registration
overlap and linked paths before using a validated installation receipt and file
inventory. Each deletion verifies length and SHA-256 through the same locked
Windows handle that receives the deletion disposition. Modified or unlisted
files are preserved; no recursive game-directory deletion is used. Missing
inventory refuses cleanup and leaves registration-only removal available.
Retired managed registrations can be added again without moving their files.

Home acknowledges completed transfers when their client first reaches readiness,
fading and collapsing the notice while preserving download history. Other client
tasks and transfers completed after that launch remain independent. The shared
package card retains the existing store installation layout.

Acceptance: solution build has zero warnings or errors; frontend type checking
and 229 tests pass. The focused backend regression passes 61 cases, including
dangerous paths, modified and unrelated files, links, occupied files, external
processes, interrupted work, both removal modes and managed reactivation. The
broader backend run passes 1130 cases with nine optional public/client fixtures
skipped. One catalog rollback case interrupted during the earlier run passes
separately after restart. A plugin-upgrade test now takes the physical installation
lease instead of an obsolete identifier-named lock file.

Production Vue rendering at 1360 and 720 pixels verifies the dialog, cancellation,
both action requests and safe refusal. A real transition capture confirms opacity
and height decrease before the completion notice is removed, retaining history
after the game exits. These are browser renders with a bridge fixture; file
mutation acceptance uses isolated temporary directories. Receipts are under
`artifacts/client-removal-20261024/`. The native Launcher was rebuilt and reopened
for the user's multiplayer login test; the user's game process is left running.

## Launcher 0.1.24 x64 publication (2026-10-24)

Completed. The user confirmed successful Korean login before publication. The
canonical and preview incremental feeds publish version 0.1.24, revision
1792832242, with 993 files and manifest SHA-256
`7c0c65e1cb7dccfce7016d3add40d4aaee5765788ab1e0113b22321a02c0f7f5`.
The portable archive has 208412448 bytes and SHA-256
`e4cf583662e2d607d3f8a47654bcb4ef80bc5129eeab0171f8dba908cb93a2ed`.
All 994 ZIP entries pass CRC verification; local account/session settings are
excluded. The accepted background storage upload completed after the publisher
timed out. Archive-only finalization then verified the anonymous complete archive
and both feed-bound fallback companions without replacing immutable feed objects.
The publication receipt is under `artifacts/launcher-0.1.24-win-x64-20261024/`.
The later removal and acknowledgement changes above are local development changes
and are not included in the already published 0.1.24 archive.

## Public store catalog grouping repair (2026-10-24)

Appending KR authentication 0.1.2 after the catalog's client profiles retained all
metadata on disk, but the current TOML model reader replaced the earlier resumed
package array. The actual Launcher bridge consequently returned only the new
plugin. Publication now groups each root table array contiguously and verifies
that parsing before and after grouping yields identical metadata. A regression
covers append, repair, idempotence and preservation of profiles and server data.

The repaired public catalog is anonymously verified as
`ccbd215f90554e9a4606743bce633cd85d7d4744f07c9d20af5ff1180ec6cf8d`.
The existing Launcher binaries read its 27 package releases: 11 clients, 15 plugin
releases across six identities, and one server. All 13 client profiles are
unchanged. A separate backend using isolated data fetched the public source and
returned all 27 entries through `store.catalog`. The production frontend rendered
11 client cards and six plugin cards from the managed parser's response, with no
script errors; screenshots were inspected. This is browser rendering acceptance
with a bridge fixture, not a captured native desktop. Native Computer Use could
not initialize because its kernel asset path was unavailable. Receipts are under
`artifacts/store-display-20261024/`.

## Signed HTTP/AList transfer through external updater acceptance (2026-10-24)

The focused update audit passes 181 cases with no failures; its optional public
AList transfer case is skipped because the test source was not configured. Six
additional process tests now connect signed HTTP and AList discovery to the real
external updater executable. They verify that discovery downloads no payloads,
only changed objects transfer, AList object resolution is used, a ready release
restores after the source is stopped, and the helper revalidates the retained
signature before handoff. The actual child acknowledges frontend health before
commit; rejected health restores old files and records the candidate rejection.
Tampering with the retained signature refuses handoff without changing the
installation. Personal files and update source settings survive both commit and
rollback.

All six process tests pass on Windows. This exercises a local HTTP/AList protocol
fixture and test launcher, not the public signed feed or production key custody.
Prior signed desktop acceptance covers the current frontend with a real isolated
installation, external helper, native host and rendered Home. Public-source
signed installation and rollback remain separate acceptance gates.

## KR1256 credential handoff and client lifecycle repair (2026-10-24)

KR authentication 0.1.1 embedded a session parser that rejected the new
`login.credential_plugin` field. Native status reported a required-plugin failure
and entry status 2. Component 0.1.2 obtains credentials through the explicit
runtime authentication API during entry, with no independent session parsing.
Managed plugin compatibility now matches native 3.4.0, with a regression test
against the native build configuration. The immutable 0.1.2 public package is
published and anonymous payload hashes are verified; old releases are preserved.

Launch preparation displays its spinner immediately. Process state and plugin
initialization errors are separate, with failed component names shown in the
notice. Confirmed stop validates the expected PID inside the coordinator lock;
already-exited and replaced targets are no-ops. Duplicate physical client
registrations retain one selected identity and archive the others' metadata;
leases are keyed by physical installation. Verified stopped session recovery runs
before deployment. Initial inventory no longer waits for background recovery or
plugin preparation. Diagnostics visibility comes from enabled plugin metadata.
Plugin downloads share the library and target compatible clients automatically;
server downloads use the shared package card.

The first focused backend run passes 29 tests and the native credential tests
pass 58 checks. Startup-to-ready measured approximately four seconds in the
native application. The actual public download installed and enabled 0.1.2 in
the local library. Real KR1256 process 7552 reported `ready`, with both selected
components installed; owned stop removed its session and restored deployment
files. This run ended after initialization, before server login acceptance.
Real frontend screenshots verify a white animated ring on the unchanged accent
fill, stable button dimensions, and a separate localized component error notice.
Receipts are under `artifacts/kr-auth-20261024/`.

## Store inventory and plugin library alignment (2026-10-24)

The unfiltered `plugins.list` bridge call accepts omitted or null optional
filters as an empty object. Non-object and unknown parameters still fail. The
store can inspect the whole plugin library without selecting a client or
triggering account discovery. A real backend regression fixture covers all
three valid parameter forms and rejects malformed requests.

The plugin library retains the commented empty update summary without rendering
it or its source guidance. Available updates and failures remain visible.
Version selectors, switches and trailing settings icons occupy fixed columns even when
a plugin omits one control; narrow windows place controls below the text.
Production frontend screenshots cover light/dark themes at 1360, 960 and 720
pixels, with column positions measured and no horizontal overflow or script
errors. Receipts and screenshots are under
`artifacts/plugin-layout-20261024/`.

## Actual CN406 login survives an owned server restart (2026-10-23)

The actual installed CN2013 client now passes
`ActualClientRetainsPlayerIdentityThroughOwnedServerRestart` against the complete
server package from revision `f10075fb`, provisioned with local CN406 data. Its
published binaries retain their package hashes. The fixture uses isolated SQLite
storage and a literal 600-second launch-ticket override in its copied profile;
the package's typed environment expansion remains a separate server issue.

Both real game processes reach `MainMenu` with the native hook reporting `ready`.
After the first client exits and the owned server stops gracefully, the restarted
server accepts the same account with the same player identity, 1. Initial
equipment publication succeeds in both cycles, replacing the earlier missing-item
failure during login. The user independently confirmed entry into the game.
Windows elevation was confirmed manually; it was not automated.

The actual-client test passes with zero failures or skips in 5 minutes 42 seconds.
It verifies graceful server shutdown, released ports, persistent SQLite storage,
client-file restoration and session-file removal. The permanently installed
authentication plugin retains its expected hash. The TRX and extracted two-cycle
receipt are retained under `D:/Temp/ft-real-cn406-results-20261023/` as
`real-cn406-f10075fb-manual-elevation.trx` and
`manual-elevation-acceptance.json`. This accepts the isolated CN406 login/restart
path; other builds, gameplay, remote multiplayer and deployment into an existing
user server installation remain separate gates.

## Size-versioned native authentication handoff (2026-10-23)

Runtime 3.4.0 now exposes an optional C ABI authentication tail. The launcher
only includes it when a game module explicitly names one selected native plugin
in `LaunchPlan.AuthenticationPlugin`; manifests and patch plugins cannot grant
themselves access. The session parser accepts `login.credential_plugin` only for
schema 2 sessions that have the selected plugin, a ticket and an endpoint.

The callback validates launch, game and build identity, reports caller-owned
buffer capacities without partial copies, and returns only the current ticket,
endpoint and port. Invalid descriptors and mismatches return `FTHOOK_INVALID`.
The runtime revokes access and securely clears its temporary ticket copy after
entry returns. The launcher rejects an unselected owner, a non-native owner, a
missing ticket or endpoint, and runtime packages older than 3.4.0 before any
deployment. The SDK export and bilingual usage notes document the contract.

The synthetic native suite passes 173 checks, including valid, denied,
wrong-owner, incomplete, mismatched identity, short-buffer, null-buffer,
invalid-descriptor and post-entry revocation cases. The Launcher suite passes
14 focused integration cases covering the same ownership and runtime gates, with
zero failures or skips. Release C# builds have zero warnings/errors, the native
Release|Win32 solution builds with zero warnings/errors, and the independent SDK
sample builds from the 3.4.0 export. The current Korean authentication plugin
still reads its private session file and has not been migrated or accepted on a
real Korean client; only the public handoff and its boundary are complete.

## Diagnostic plugin builds and loads across the independent repository boundary (2026-10-23)

Game repository commit `2af17a5` moves diagnostic mode from its integration
snapshot to `native/crash-recorder` and supplies standalone Release projects for
the DLL, public-ABI loader tests and dump writer tests. Its six production source
and export files are byte-identical to the Launcher implementation. The pinned
FtHook 3.3.3 SDK passes its seven-file integrity check; no runtime C++ headers,
static library, sibling checkout or private session reader is required.

A clean export of that commit independently builds all three projects with
warnings as errors. The loader passes nine fresh-process scenarios with 67 parent
checks and 120 nonfatal child checks. Dump writer outcome tests pass 35 checks.
The actual DLL captures handled and fatal exception dumps with validated streams,
traces a real loopback TCP receive through staged import replacement, and cleans
up observers before synthetic host imports are restored. Its sole undecorated
plugin entry is present; the built DLL depends only on USER32 and KERNEL32.
The fatal child exits with the expected access violation, rather than being
misreported as a successful normal exit.

The independently built DLL also passes the existing Launcher FtHook test host's
recording, disabled, file-failure, byte-limit and fatal scenarios in an isolated
directory. Nonfatal scenarios pass 15 checks; the fatal case preserves its access
violation record. SDK, source, archive, DLL and host hashes and scenario outcomes
are retained under `D:/Temp/ft-standalone-diagnostics-20261023/` and
`D:/Temp/ft-standalone-diagnostics-runtime-20261023/`.

This accepts the standalone source/build and native host boundary, not game-client
receive paths or gameplay. Existing Launcher integration, store package bytes,
plugin identity/options and the approved UI remain unchanged. Korean authentication
still needs a narrow authorized credential callback; `nmcogame` still needs an
explicit runtime host contract. No remote source or package was published.

## Server plugin policy rejects overlapping scopes and honors profile fallback (2026-10-23)

The existing identity-only `pluginRecommendations` contract already represents
recommended, required and forbidden policies with `required` and `disabled`.
Metadata validation now rejects required/disabled overlaps across separate rows
for every build, including wildcard scopes, before a client is selected. Disjoint
scopes remain valid; duplicate recommendations and account requirement promotion
keep their existing behavior. JSON, local profile and store readers share this
validation. No executable path or download URL is added to policy.

Saved profile declarations now reach launch enforcement for servers without an
API and before live discovery. Omitted catalog policy retains that fallback;
an explicit empty list clears it, null is invalid, and a rejected catalog preserves
the previous validated policy. Per-launch suppression still leaves the saved
client selection unchanged and refuses conflicts with mandatory or permanent
components. Bilingual multiplayer documentation records the shared contract for
the server persistence/editor integration, which is owned by the server chat.

Ten new cases cover scope intersections, wildcard and disjoint behavior, TOML
readers, local policy enforcement, omitted/empty/null live policy and replacement
by endpoint. Focused policy, presentation, profile and compatibility tests pass
63/63 with no skips. Complete Release solution compilation has zero warnings and
errors; the full suite passes 1,066 tests with nine explicit environment-gated
skips and no failures out of 1,075. Results are retained under
`D:/Temp/ft-server-policy-20261023/`. This accepts Launcher policy handling, not
server editor delivery, actual-client login or remote multiplayer.

## Native descriptor prerequisites are checked against actual client images (2026-10-23)

The offline verifier previously validated supported numeric-health descriptor
syntax but deferred all native image checks. It now compares PE timestamp, image
size and preferred base, the exact render call and original target, seven-slot
caller cleanup, resource signatures and the declared maximum-health vtable entry.
Addresses use positive 32-bit values; incorrect types and wildcard evidence are
refused. Native activation, player provenance and rendered behavior remain deferred,
so matching static prerequisites still return exit code 2, never full acceptance.

All 26 verifier regressions pass, including wrong image identity, corrupted calls,
cleanup, resources and vtable entries, wrong targets, field-based maximum health,
and malformed descriptor addresses. The actual unpacked KR2015 `kr-812` executable
passes all seven supported-descriptor checks for numeric-health 0.2.11, retaining
two deferred native/locator checks and exit code 2. An isolated descriptor with its
draw target advanced by one byte fails the real-image command with exit code 1.
No executable, installed plugin, published package or runtime binary changed.

A separate eight-image inspection covers actual unpacked CN406, CN404, ID400,
ID1809, KR812, KR1256, CN774 and TW454 files. Across 56 applicable definition/build
combinations it records 25 offline checks, 14 deferred runtime checks and no failed
byte/descriptor checks. Definitions with no offline checks are not accepted by
this inventory. Independent full SHA-256 comparisons confirm all eight EXEs are
unchanged. Image, manifest and descriptor hashes, verifier hash and individual
outputs are retained under `D:/Temp/ft-real-plugin-matrix-20261023/`; the later
receipt includes native descriptor checks. This advances H8 prerequisites, not
actual login, gameplay, plugin combinations or private-module acceptance.

## Fresh-account actual-client login exposes a server inventory ordering failure (2026-10-23)

An opt-in CN2013 `cn-406` acceptance now starts the actual installed client through
the Launcher backend against an isolated SQLite server package. It waits for
source-bound authentication-plugin checks, requires both server MainMenu and Hook
readiness, and attempts the same persisted player after an owned-server restart.
Client-file assertions respect permanent plugin installation policy; temporary
runtime files must restore, and the session file and server ports must be released.
The fixture explicitly stops owned processes before passive backend disposal, so
a failed login cannot leave disposal waiting indefinitely for the game window.

The first actual connection was refused as an invalid launcher ticket. The native
bridge loaded over 62 seconds after the recorded launch preparation, while the old
package defaults to a 60-second ticket. Expiry is a timing-based explanation; no
issuance/expiry receipt was captured for that refusal. A controlled copied-profile
override of `ticket_seconds: 600` retained ticket authentication. One attempt failed
before launch because the mandatory source check returned `plugin_latest_network`;
its cleanup completed without a running game. The subsequent attempt delivered
the ticket and authenticated the fresh player at 20:29:56 UTC, but failed before
MainMenu: `InventoryWearSelection.Apply` reported that the required equipment
position lacked initial item `202001`. The protocol closed the login connection.

Read-only server inspection places the expiry sweep before the login inventory
resolver's first-account seeding. The sweep repairs a missing character and calls
equipment selection, which also requires the still-unseeded initial weapons. The
failure stack and ordering were delivered to the authorized server Sol chat; no
server source or package in place was edited. The test-only lifetime override
changes only its copied profile and records its value and acceptance timestamps.

The failed 600-second run is retained under
`D:/Temp/ft-real-cn406-pzjf32dh.m03/`, with TRX results under
`D:/Temp/ft-real-cn406-results-20261023/`. Its failure cleanup removed both client
session and runtime transaction, stopped the owned server, and passed file-policy
assertions. This is a diagnosed fresh-account failure, not completed client login,
restart acceptance, room gameplay or a verification of the forthcoming server fix.
Release solution compilation passes with zero warnings and errors.
The focused file-plugin, native-launch and local-package selection passes 56 tests
with three explicit environment-gated skips and no failures out of 59. Its TRX is
`local-client-launch-focused.trx` in the same results directory; those skips do
not replace the failed actual-client run.

Subsequent source comparison confirms that server commit
`436da0cc119547c00c54d5cc503857ab3c1a6a7f` already changes the sweep to leave an
entirely empty inventory for complete starter seeding. The frozen `6e09728e`
acceptance package predates that fix. The server chat is checking complete
required equipment positions, SQLite and retry paths before delivering an updated
package. Do not rerun this stale package or treat a source-level fix as MainMenu
acceptance; the updated-package actual-client gate remains open.

The replacement login-order package based on `f10075fb` passes independent SHA-256
verification for all 481 published files, both before and after provisioning the
existing CN406 client data. Its first run fails during activation because the
`ticket_seconds` environment expression reaches integer configuration unexpanded.
A copied-profile literal override of 600 seconds activates all 31 rows and issues
a fresh launch ticket. Windows then cancels the client's administrator request
with error 1223 before a game process starts. Both failures stop owned services
and remove the client session and runtime transaction. The corresponding
`real-cn406-f10075fb-login-order*.trx` results and retained fixtures document these
separate gates; neither run verifies the inventory fix, MainMenu or reconnection.
The server chat has both reports. Production configuration expansion and an
actual-client retry with manual Windows confirmation remain open.

## Native frontend health failure restores the original installation (2026-10-23)

An isolated native 0.1.23 installation accepted a loopback HTTP candidate whose
only changed payload was a valid blank HTML entry point without frontend health
acknowledgement. Actual Restart and update and Update clicks handed off from
parent PID 2620 to helper PID 8876. Updated child PID 29372 displayed the intended
blank window; the helper's existing 90-second health deadline then expired.
Without manual termination, it restored the baseline and opened PID 16992.
The restored Home displayed the startup-failure notice and expanded diagnostics
explained that the current version was restored. Its work-mode media remained
hidden throughout the baseline and restored frontend observations.

The helper outcome is `rolled-back`, failure `startup`, phase `frontend-startup`.
The rejection marker binds candidate revision 1792784666 and manifest SHA-256
`33d761c82d003be99534012fcdca299d24e39e62febf65544bd10231c35d4e06`.
Independent full size/SHA-256 verification matched all 1,003 baseline files,
totaling 389,318,004 bytes, including the unchanged update-source policy. Neither
an installation journal nor a candidate receipt remains. The HTTP audit records
one changed-object download and a post-rollback feed check, with no repeated object
download during the subsequent two-minute observation. The restored UI remains
failed rather than offering the rejected candidate as ready, even though its
persisted transfer checkpoint still says ready.

Private setup, HTTP audit, lifecycle snapshots and the independent receipt are
under `D:/Temp/ft-native-rollback-20261023/`. The frozen x64 package came from
the earlier 0.1.23 archive-retry candidate based on 9255f397 with uncommitted
candidate inputs; it does not contain the later module-loader change. Relevant
updater/host source is unchanged between that revision and this acceptance.
This accepts actual native startup-health rollback and retry suppression for that
package, not a new distribution, current-module deployment or gameplay. No server
source or remote publication changed.

## Public AList desktop replacement accepted with retained local extensions (2026-10-23)

The previously staged anonymous AList release completed an actual native update
from the isolated installation. The user-visible Restart and update confirmation
started the detached helper, exited parent PID 28468 and automatically opened child
PID 29604. The helper reports `completed`; the installed receipt binds revision
1792685966 and the selected index SHA-256
`5de9d7d1e1e1299dc8315f156dd16171b5c4d74faef249ece122ccea4e97f90f`.
An independent four-worker audit verifies all 990 installed file sizes and complete
SHA-256 values, totaling 388,573,961 bytes. The installation journal and rejection
marker are absent, and the distribution's update-source policy is unchanged.

This frozen 0.1.22 frontend lacks work mode. A separate local placeholder game with
no media was selected solely in the isolated WebView profile before installation,
so native restart acceptance did not play a home video. The unindexed game TOML
survives with its exact original hash and the restarted Home renders that selection.
This is not a work-mode acceptance claim for the published frontend. Current-source
work mode has the separate observation below. Lifecycle log rotation discarded
startup timestamps, so no startup-duration claim is made. The successful helper
commit, receipt and rendered child are retained as the installation evidence.

The private verifier and receipt are under
`D:/Temp/ft-public-desktop-4d6cae3768814ef99459fd2344e708f6/`. This accepts real
public-source incremental replacement and automatic native restart, not an updated
0.1.23 publication, a public signature or a new rollback run. No remote files changed.

## Managed game modules resolve package-local dependencies (2026-10-23)

A separate SDK-only module package reproduced a real loader failure: its main DLL
loaded in the default context, but its launch method could not resolve a dependency
beside that DLL. Each installed module path now has a reused component load context,
resolving its manifest and adjacent managed dependencies while sharing the host SDK.
Game-local precedence, application-folder fallback, optional SDK interfaces and
game-identity validation remain intact. Installed packages with the same assembly
names resolve their own copies independently. The architecture guides document the
process lifetime and restart requirement; no private game source was imported.

Seven subprocess acceptance cases pass, including concurrent repeated loads of two
packages, with and without component manifests, a packaged SDK copy, missing private
dependencies, invalid definitions and the existing bundled hosting interface.
Child-process exit releases Windows DLL mappings before fixture cleanup. Release
solution compilation has zero warnings and errors.

Enabling the eight-build retail/unpacked corpus exposed its obsolete expectation
that registering a detected client must first fail without overwrite consent.
Existing detection already registers read-only and reuses the record. The updated
acceptance checks both registrations return the same identity, the list has one
record, and full EXE hashes are unchanged. All eight real-client cases pass together
with the seven module cases. The initial complete opt-in run failed only those eight
old assertions; all 1056 other cases passed, with seven optional skips. A later
complete run passed 1062 cases but failed two successful-start hosting fixtures:
one exhausted its three-second adapter budget and another ignored a failed child
while waiting ten seconds for readiness. Those results are not a green regression.

Successful `LocalHostingTests` probes now use the SDK's existing startup budget,
with a watchdog five seconds beyond it and exact invocation selection for two-child
cases. Explicit startup-failure cases retain their three-second adapter deadline;
heartbeat, withdrawal and shutdown latency assertions are unchanged. Focused
hosting/module validation passes 63 cases with one optional corpus gate skipped.
The first complete regression with the eight-build corpus enabled passed 1063
cases, with seven optional skips and one native login-bridge cleanup timeout.
After the separately diagnosed cleanup-fixture correction below, the final
complete Release run passes 1064 cases with zero failures and seven optional
environment gates skipped out of 1071 in 7 minutes 6 seconds. This includes all
eight real-client identity/registration cases. Its TRX is
`D:/Temp/ft-module-regression-20261023/results/complete-release-final.trx`.
These results do not establish retail gameplay, public-source desktop replacement
or real private-game module acceptance.

## Public-source desktop staging and older frontend limitation (2026-10-23)

The isolated x64 installation under
`D:/Temp/ft-public-desktop-4d6cae3768814ef99459fd2344e708f6/` automatically staged the
current anonymous AList release. Its independent audit verifies all 990 destinations:
987 match installed bytes and three changed destinations have verified staged
content from two objects. Source policy is unchanged. Setup and staging receipts
remain beside its data. After manual unlock and foreground selection, actual
screenshots confirm responsive Settings tabs and Ready status. The native window
still groups under another application's identity in the automation inventory;
input was sent only after screenshots showed the actual Launcher.

The isolated bootstrap wrote `nsfw=on` and `developerMode=on`, but the frozen
published frontend contains no work-mode implementation and leaves Developer
unavailable. Actual Home observation showed media; the bootstrap did not suppress
it. The old isolated process was stopped after Settings acceptance, retaining all
staging data. No restart installation was confirmed and no external updater
outcome exists. A work-mode claim for this older package is incorrect. Staging
and process presence do not establish desktop installation or rollback success. Existing current-reader
desktop acceptance below remains separate from this public-source run.

The newly compiled Host was then launched with separate temporary Launcher data
and the seeded isolated WebView profile. An actual Home screenshot confirms that
both video and cover media are absent. The product's default remains off. The
local observation receipt is
`D:/Temp/ft-current-module-desktop-20261023/desktop-receipt.json`. Other foreground
applications still occasionally appear in the capture result; no input was sent
when those captures did not show the Launcher.

## Native login cleanup retains diagnostics during file replacement (2026-10-23)

Independent native login-bridge reruns reproduced cleanup timeouts after the
probe had already exited and the login assertion succeeded. A managed thread
snapshot shows `RuntimeDeployment.Restore` inside Windows `MoveFileEx`; this is
evidence of synchronous file replacement, not proof of a specific disk or scanner
cause. A fifteen-second test watchdog could abandon the live cleanup monitor and
then let fixture deletion fail on its still-held instance lease.

The login fixture now waits up to two minutes for cleanup while preserving all
original-library and removed-session assertions. Timeout diagnostics include the
native process state, readiness state and completion task state; cleanup failures
retain the fixture instead of deleting its evidence. Production restoration and
authentication behavior are unchanged. All eight targeted login cases now pass in
3 minutes 31 seconds, including original-library restoration and session removal;
the longest case takes about 40 seconds on this machine. The subsequent complete
Release run also passes with 1064 passed, zero failed and seven optional skips.
The final targeted TRX and thread snapshots are retained under
`D:/Temp/ft-module-regression-20261023/`.

## Current server package retains its account and storage lifecycle (2026-10-23)

The immutable local x64 server package was exercised through the new module
loader in isolated temporary directories. The temporary-data case passes once;
a subsequent filter runs both temporary and persistent-local cases, and both
pass. These tests cover account registration and sessions, login rejection,
launch-ticket metadata, owned room publication, readiness, graceful shutdown and
restart. The local case additionally retains accounts and saves across restart
and replacement of the package directory, and isolates another profile's data.
No retail game process was started, no server source changed and no package was
published. The TRX files remain under
`D:/Temp/ft-module-regression-20261023/results/`.

## Local hosting fixtures use the adapter readiness budget (2026-10-23)

The previous full-suite capacity failure came from a successful-start fixture's
three-second startup budget. A separate metadata-transport case reproduced the
same deadline with the child emitting its readiness marker during shutdown.
Successful plan fixtures now use the SDK's existing 90-second default; production
timeouts are unchanged and explicit timeout/forced-stop tests remain separate.
The concurrent-package case also verifies three distinct child PIDs, three ready
destinations and graceful shutdown, with recent child logs on failure.

The focused hosting/plan run passes 56 cases. The subsequent complete Release
managed regression passes 1049 cases, with zero failures and eight optional skips
out of 1057 in 6 minutes 28 seconds. Its TRX is
`D:/Temp/ft-hosting-regression-20261023/full-regression.trx`. This establishes the
hosting fixture correction against the complete suite before the separate managed
module-loader change; it does not establish retail game or public desktop-update
acceptance.

## Real anonymous AList update staging and offline restoration (2026-10-23)

The current published win-x64 release was checked through the real AList adapter
without an account, administrator token or pinned connection address. The test
discovered revision 1792685966, downloaded and verified all 990 indexed files
(388,573,961 bytes), then reopened the persisted task using an unavailable network
handler and recovered Ready with the same selected manifest identity. The x64
acceptance run passes in 5 minutes 37 seconds; all temporary payloads were on D:.
The sanitized receipt and TRX are under
`artifacts/public-alist-staging-current-20261023/`.

The first invocation used this ARM64 machine's default test runtime and correctly
rejected the win-x64 feed. Explicit VSTest x64 platform selection exercised the
matching runtime without weakening distribution validation. Both update guides
document that invocation and remove stale archive byte/count ceilings that are
already absent from the implementation. This closes real-source staging and
offline-restoration acceptance for the published release. Public-release desktop
replacement/health/rollback, GitHub deployment, retail Hook/server joins, private
module runtime integration and the concurrent-hosting full-suite failure recorded
below remain separate work. No public artifacts were changed by this acceptance.

## Referenced server profiles publish before their catalog (2026-10-23)

The AList uploader now includes explicitly referenced portable server TOML files
from the source root. It rejects identity, game, API and endpoint disagreements,
missing files, secret tables, bundled-only fields and publication-path collisions
before authentication. Unreferenced files remain excluded. Schema-two server-only,
inline-only and directory-only catalogs no longer need dummy packages. Profile
updates use observed public remote baselines; local changes and observed remote
conflicts stop publication. All profile and optional signature artifacts are
anonymously verified again before the mutable catalog write. The catalog digest
is pinned before graph preparation, closing a preflight edit window.

The managed `--validate-catalog` command uses the same profile-binding validation
as explicit store downloads, with `--source-root` for nested catalogs. It returns
exit code one with a diagnostic for invalid input instead of an unhandled process
exception. A real CLI fixture accepts a server-only nested catalog, rejects a
mismatched endpoint and rejects a missing file. Browsing still downloads no profile;
the existing HTTP/AList/GitHub explicit-selection tests remain covered.

All 138 Python publication regressions pass, including real loopback HTTP uploads
that verify exact profile bytes, ordering and anonymous downloads. The 43-case
managed profile/discovery/catalog group passes. Release solution compilation and
the final publisher build both have zero warnings and errors. The subsequent full
managed run reports 1048 passed, one failed and eight optional skips out of 1057
cases in seven minutes. The failure is the concurrent-three-package hosting
fixture: two processes hit its three-second startup deadline. An isolated rerun
passes, but this does not establish a clean complete-suite result. This completes the
profile publication gap recorded in the previous checkpoint. Public deployment,
desktop replacement and retail-client/server joins remain separate acceptance
gates; these fixtures do not establish them.

## Optional signed publication artifacts reach the uploader (2026-10-23)

The signing publisher previously produced detached signature and public-key files,
but the shared AList uploader ignored them. Explicit signed output now contains a
local publication record binding the exact index and both artifacts. The uploader
validates it before authentication, checks anonymous remote baselines, publishes
and verifies signature artifacts before the mutable feed, and rejects local changes,
remote conflicts and collisions with content, aliases or portable archives. The
publisher rejects partial signing configuration, metadata/temp-path collisions and
reuse of signed output for an unsigned release. Ordinary sources still need no key
setup; no bundled source policy or production signing requirement was introduced.

All 118 Python publication regressions pass, including a real loopback HTTP AList
fixture that verifies upload order, exact public bytes and absence of administrator
authorization on downloads. Eleven focused managed publisher tests pass. A separate
temporary RSA-2048 fixture ran the C# publisher, verified the exact index signature,
and passed its generated record through Python graph/signature preflight (three
content objects and two signature artifacts). The private key was discarded; the
sanitized receipt is `artifacts/signed-publication-20261023/receipt.json`. These are
local fixtures, not a public signed release or a new desktop replacement run.
The combined publisher and HTTP/AList/GitHub signature regression passes 41 cases;
the final Release solution build has zero warnings and errors.

Server profile publication remains a separate identified gap: browsing and explicit
download already share HTTP/AList/GitHub, but the uploader's package graph does not
yet include standalone server profile references. Current guides require publishing
those files separately. Automatic profile graph preparation and its acceptance
remain work to complete; this signed-update fix does not claim that scope.

## Frontend and publication handoff verification (2026-10-23)

The committed Launcher workspace now includes the server-profile, multiplayer,
client-install, plugin-source and update-dialog UI flows. The production Vite build
completed successfully with 323 transformed modules and zero reported errors. The
complete frontend regression run passes 219 tests with zero failures; TypeScript
strict checking passes. Publisher tooling tests pass 103 cases, including readable
large catalogs, content-addressed literal asset names, archive registration and
retry-safe AList uploads. A clean Git snapshot of the managed solution builds with
zero warnings and errors, and its focused server/source/installation regression
passes 79 cases.

This evidence covers local implementation and fixtures. Production signing custody,
public GitHub publication, retail-client and real server joins, private game modules,
and an end-to-end desktop update from a public endpoint remain separate acceptance
gates; they are not claimed by this checkpoint.

The subsequent complete Release solution build has zero warnings and errors. Its
full managed regression passes 1030 tests, with zero failures and eight optional
environment-dependent skips out of 1038 cases in 4 minutes 48 seconds. A follow-up
publisher audit found that server advertisements still reused lowercase package
ID rules. Composition now preserves uppercase and underscore server IDs under the
same file-safe contract as the Launcher, rejects device names, and compares server
identities without case. The runtime catalog parser likewise rejects case-only
duplicates before accepting a snapshot.
After that correction, all 106 publisher tests and 31 focused profile/discovery
tests pass. These focused runs validate the final correction; the 1038-case full
run above preceded it.

Fresh visual inspection is not established by this checkpoint: the browser tool
rejected access to the localhost preview. The existing accepted layout was retained;
the successful build and component tests are not a substitute for new screenshot QA.

## Shared transports and signed GitHub update regression (2026-10-23)

The shared source resolver now covers HTTP, AList and GitHub for store discovery,
server profiles and launcher updates. GitHub mirror routing remains an explicit
TODO that retains direct access. Saved server identities, native TOML import and
export, local artwork, installation recommendations, launch-only disabled plugins
and LAN hosting are connected through the host bridge.

The source, discovery, directory, definition and installation selection group
passes 122 tests without skips. The existing update and record group passes 163
tests with one optional public AList test skipped. Ten additional GitHub signature
cases extend exact-index authentication, cached proof, key changes and post-check
tampering; the complete signature group passes all 30 cases. These tests use
ephemeral keys and injected transports, not a production signer or public release.
The shipped-reader verification tool builds with zero warnings and errors when
given the unchanged local baseline package. Clean-snapshot compilation and the
next full regression remain separate gates.

## Local hosting inputs without arbitrary capacity ceilings (2026-10-23)

Local hosting no longer limits the number of saved profiles, save directories,
diagnostic directories or independent server processes. Adapter-generated files,
arguments and environment overrides have no launcher-specific count or aggregate
byte ceilings. Files remain confined to the invocation directory; duplicate names,
device names, log collisions, null values and NUL process input fail before launch.
Environment override names are checked without case sensitivity for Windows.
Output rotation, recent-log retention and startup/shutdown deadlines remain bounded.

Twenty-one new regressions exercise actual child-process input, a 2 MiB generated
configuration, 65 persistent profiles and saves, three independent running packages,
invalid plans and release of startup leases. Failed hosting fixtures now include
the process ID, exit code, forced-stop state and recent output in their diagnostics.
The earlier startup timeouts did not reproduce in a 1005-case baseline rerun;
their root cause is not established and no timing threshold was relaxed.

The final Release solution build has zero warnings and errors. The full solution
test run passes 1018 cases, with zero failures and eight optional skips out of 1026
tests in 4.2166 minutes. This supersedes the previous hosting-failure checkpoint
as current managed validation, without claiming retail-client, public-source or
production-server acceptance.

## Live server profile fallback and explicit refresh (2026-10-23)

The launcher now merges `/v1/catalog` presentation fields with the saved server
profile by field presence. Missing values retain the local fallback; explicit
false, null, minus-one and empty-array values remain authoritative. Descriptions
and artwork translations merge by language tag, and malformed remote values cannot
replace the last validated catalog. Successful responses are deserialized directly
from the response stream.

The Server information tab now offers an explicit Update from server action. It
atomically saves the latest validated server name, presentation, supported builds
and plugin declarations while retaining the stable profile ID and connection
address. Endpoint migration still requires a separate confirmation contract.
Thirteen new backend regressions and the frontend description suite pass. Release
build passes with zero warnings and errors. The complete managed rerun remains
non-green because five local-hosting startup fixtures failed under the current
machine state; an isolated rerun passed 33 of 35 local-hosting cases and showed
two startup-timeout fixture failures. This is recorded as validation evidence,
not hidden as test flakiness.

## Complete managed regression checkpoint (2026-10-23)

After the update terminal-state fix, the complete Release .NET suite passed:
984 passed, zero failed and eight skipped, out of 992 tests in 3.7962 minutes.
The preceding Release solution build produced zero warnings and errors. The run
includes independent server profiles, optional file-safe server identities,
import/export, source discovery, accounts, local hosting and native companion
regressions. Earlier failed full-suite methods also pass in this complete run.

The eight skips require explicit retail/archive fixtures, a complete server
package or remote publication endpoints. They remain separate real-source and
real-client acceptance gates. Server-published complete profile JSON, local
profile refresh and confirmed endpoint migration are still unfinished. This
checkpoint supersedes earlier full-suite failure results as the current managed
regression status; it does not establish a public deployment or game-server join.

## Actionable launcher update terminal states (2026-10-23)

A verified update no longer exposes Ready while rejecting an immediate external
handoff because its worker Task has not completed runtime bookkeeping. Failed
transfers likewise accept Retry as soon as failure is published. Transfer activity
and terminal task/discovery state now change under the same gate; the real worker
remains owned and awaited during shutdown. Canceled rows cannot be cleared while
transfer cleanup is still running.

Two deterministic regressions extend worker lifetime after actual staging and
both fail on the previous implementation. Release compilation has zero warnings
and errors; the update regression group passes 156 tests with one optional remote
acceptance test skipped. A separate cancellation-cleanup regression also passes.
The full suite remains a separate acceptance gate.

## Independent server profiles and stable identities (2026-10-23)

Bundled servers now live in separate game server profiles; local, imported,
exported and store-delivered destinations share one readable TOML contract.
Explicit saves fetch declared store profiles through the common HTTP/AList/GitHub
transport. Browsing and favorites do not download profiles. Import compares stable
identity and destination, skips identical writes and confirms different saved data
against an inspected fingerprint. The information tab contains import/export
controls without another tab.

The optional new-server ID accepts ASCII letters, digits, hyphens and underscores,
with a host-and-port default. New filenames match the ID; identity survives address
changes and manually renamed files. Case-insensitive collisions cannot overwrite
from the add form. The built-in Bubble Fighter profile is `FantasyTale.toml`.
Focused server/profile/discovery tests pass 102 cases, including native bridge
creation, file import confirmation and all three source transports. The complete
frontend suite passes 218 cases; type checking, production compilation and five
catalog-composition tests pass. Actual browser screenshots cover the optional ID,
invalid-character feedback and information-tab file actions at narrow and desktop
widths. Release compilation has zero warnings/errors. The complete .NET run
reported 976 passes, four failures and eight optional skips. Three failures were
communication/download timeouts; the fourth was an update-ready state check.
After the run ended, the 115-case focused rerun including every failed method
passed. This does not claim a subsequently clean complete-suite run.

Remote profile JSON/persistence, server-driven endpoint migration and live-server
presentation publication remain separate unfinished contract work.

## Plugin installation summary cards (2026-10-23)

The plugin confirmation dialog now presents its selected package as an icon,
name, version and source card. The shared list accepts multiple package/source
entries and scrolls within the dialog, so future resolved dependencies can use
the same presentation. Current installation still plans only the selected plugin;
dependency resolution is not claimed. Source and activation controls retain their
existing behavior. The owner inspected and accepted the live preview. TypeScript
checking, production build and all eleven store regressions pass.

## Merge plugin offers with explicit source selection (2026-10-23)

The default All sources view groups plugins by kind, game and package ID. Source
order, versions and manifest hashes remain independent; the first source wins
the default even when a later source has a newer release. A source picker and
source-specific version picker preserve compatible selected targets and keep an
open confirmation stable across background catalog changes. Plugin bridge calls
validate the confirmed transport, base URL and catalog path before discovery.
The plugin-target selection no longer has a fixed 32-client ceiling.

Release compilation has zero warnings/errors. All 41 focused actual HTTP/AList
store/selection tests and 119 frontend plugin/account/multiplayer/store tests pass;
type checking and the production frontend build pass. The standalone store suite
adds eleven repeatable source/version/activation tests. The activation checkbox
defaults on; clearing it queues only a library download. Empty/loading lists hide
the corner counter, and the download-only explanatory line stays commented out. A real browser screenshot records three
ordered offers on one plugin card, including a newer second-source release.
Repository-only release resolution, schema-three catalogs, public deployment and
actual remote game acceptance remain separate work, not implied by these checks.
A full-suite run reported 946 passes, one fixture shutdown race and eight optional
skips. The fixture now accepts disposed sockets only after cancellation; all 41
focused store cases pass after that fix. The full suite has not been rerun.

## Batch joined-room discovery without client capacity ceilings (2026-10-23)

Passive membership checks now read a source once per round and apply the result
to its joined rooms, with four sources in flight. A stalled source no longer
serializes unrelated sources, and malformed replies do not terminate the loop.
Rejoined generations remain protected from old replies. Joined-room, publication
status, directory-policy, listing-row and transport-byte ceilings are removed;
streamed JSON retains structural and lease validation.

Five new regressions cover 97 joined rooms sharing one read, independent progress,
stale rejoin responses, malformed-source recovery and 67 sources with a 2,051-row
listing exceeding 20 MiB. The focused directory and catalog suite passes 26 tests
without skips. Remote directory deployment and actual game joins remain separate.

## Reuse validated discovery during account launch (2026-10-23)

The host now reuses the selected server endpoint's validated catalog during game
launch instead of performing another public catalog request. Explicit discovery
still refreshes it. Cache scope includes server identity, exact API URL and game;
editing/removing a server invalidates its snapshots and fences pending responses.
An older response cannot overwrite newer completed discovery, and the minimum
launcher-version requirement still applies to cached catalogs. Local hosting
continues to remap the host player's endpoint against its current owned process.

Five new regressions cover cache scope, refresh/failure, stale removal responses,
out-of-order responses and the version gate. The focused account/catalog suite
passes 71 tests without skips. Directory membership and expired access credentials
can still require their own verification/renewal requests; this change does not
claim one total network request for every possible launch state.

## Account selection and server contract handoff (2026-10-23)

Account sessions, protected refresh storage, remembered passwords and cached
server policies no longer impose fixed account, server or file-byte ceilings.
Catalog presets, registration fields/choices and profile statistics no longer
impose fixed row counts. Scope, credential, duplicate and schema validation remain.
Large protected-profile and multi-server regressions exercise restart restoration
beyond the former limits.

A launch pins the selected account before token renewal. Changing that selection,
including switching back, prevents the pending operation from requesting a ticket
for a different account. A valid session still uses one launch request, carrying
numeric protocol_version and optional patch/release/language metadata. Catalog
presets retain optional numeric protocol and patch labels through the SDK bridge.

The focused online suite passes 157 tests without skips, including accounts,
directory discovery, presentation, local artwork, owned server readiness and
room renewal/withdrawal. Frontend account/hosting/multiplayer tests pass 70 cases,
and TypeScript checking passes. Prior full-suite process-startup failures are
not resolved by these passing focused checks. A real loopback HTTP probe against
the server work branch passes 19 checks: catalog metadata, two accounts, password
and admission refusals, a single launch request, build-bound one-use redemption
with UI language, disabled roster privacy and scoped logout. This probe does not
start a game or deploy either repository. Game-client acceptance remains separate.

## Remove residual source and activation metadata ceilings (2026-10-23)

The source-document API no longer accepts a caller-supplied byte ceiling. Update
handoff, selection, transfer and outcome records also have no fixed byte ceiling.
Deferred plugin activations no longer cap record bytes or count, plugin update
checks no longer cap installed versions, and configuration HTML/enum labels no
longer cap page bytes or label rows. Instance rendering and stopped-client session
recovery no longer reject large documents. Recovery inspects all matching
processes while retaining identity, stopped-process and lease checks.

Regressions exercise 777 saved activations across enqueue and restart, records
larger than 256 KiB, a 5 MiB chunked source document, a large configuration page,
instance options and matching/mismatching interrupted-session identities.
Malformed UTF-8, duplicate/null activation records, unsafe paths and source
identities remain validated. No UI layout or production publication is changed.

The Release build passes with zero warnings and errors. All 28 focused capacity,
source-retry, configuration and stopped-client recovery tests pass without skips;
the wider suite still reports owned-process startup and timeout failures and is
not a release sign-off. An independent x64 desktop fixture stages the 262,768-byte,
990-file readable index through loopback AList, fetching exactly two changed
objects and reusing 988 installed files. Its source policy remains unchanged.
Desktop installation/restart acceptance and public deployment remain pending.

## Plugin storage and deployment have no fixed capacity ceilings (2026-10-23)

Plugin imports, installed versions, source receipts, file replacement selection,
native payload bindings and deployment journals no longer impose fixed byte or
entry ceilings. ZIP directory checks read metadata sequentially and let the shared
decoder validate ZIP64. Paths, expected sizes, hashes, source identity and foreign
entry checks remain part of installation and recovery. Repository instructions
now explicitly prohibit arbitrary content-capacity ceilings and old test-reader
compatibility requirements for this unreleased product.

A new stopped-client recovery regression exposed another hard-coded cleanup slot
range after successful resource restoration. Cleanup now derives permitted names
from the actual transaction slots, including a partially staged slot, without a
fixed file count. The 181-resource fixture includes a 65 MiB payload and a journal
larger than 128 KiB; all replacements restore their originals or disappear.

Other regressions cover a 521-file imported ZIP across library restart, a store
receipt larger than 64 KiB with idempotent reinstall before and after restart,
large folder/expanded packages and abandoned staging cleanup. All 160 focused
plugin, runtime deployment, launch, recovery and language tests pass without
skips. These are local fixtures; no plugin code or game was executed and no
distribution was uploaded.

## Current update inventories and recovery accept large manifests (2026-10-23)

Current metadata transport, TOML/index parsing, update staging, updater copying,
archive decoding and recovery records no longer impose fixed inventory, file-size
or metadata-byte ceilings. Shared installation journals, ownership, file caches
and saved tasks retain path, digest, identity and atomic-write checks. Literal
archive asset names containing hash or percent characters remain valid destination
names; transport paths keep their separate URL rules.

Grouped update progress now copies the task snapshot once per report, rather than
once per destination sharing a digest. The 1,103-file readable-index fixture
exceeds 256 KiB, fetches only its two distinct content objects, preserves earlier
immutable snapshots and restores ready state after restart. A 4,200-file archive
fixture covers rollback, reinstallation and a file cache larger than 1 MiB. An
8,200-file download fixture covers extraction, transaction cleanup and completed
task restoration with one archive transfer.

Release solution build passes with zero warnings and errors. The complete managed
suite passes 916 tests with eight environment-gated skips and no failures. This
accepts the current managed implementation; no new distribution was uploaded.
D22's old-reader publication gate is explicitly superseded, and paired operator
guides describe current-reader acceptance and readable indexes instead. Superseded
procedures are preserved in ignored private deprecated notes.

## Readable update indexes take priority (2026-10-23)

The maintainer clarified that this unreleased product does not require older
reader compatibility. Both complete and frontend-only publishers now retain
blank lines between file tables and spaces around assignment operators. Index
size is not reduced to fit an older reader ceiling. The frontend publication
regression now requires a complete readable index larger than 256 KiB.

The exact-byte hash fix remains: canonical and alias feeds use the same UTF-8
buffer that supplies their recorded SHA-256. The superseded compatibility notes
are retained under ignored docs/private/deprecated rather than serving as release
requirements. Earlier loopback receipts remain investigation artifacts.

## Native archive-failure retry installation accepted (2026-10-23)

The standard unsigned x64 candidate built from revision 9255f39 completed a real
native desktop acceptance against an isolated loopback source. Both incremental
objects and the archive first returned HTTP 503. With only the archive restored,
the UI Retry action retried the original changed stylesheet before falling back
to the complete 208,357,890-byte ZIP. No unchanged object was requested.

The real restart action handed installation to the external updater. The parent
shut down normally; the child rendered Home and acknowledged frontend health in
3,496 ms without the earlier white screen. An independent four-worker audit
verified all 990 installed files and 388,608,108 bytes against the exact selected
index. Receipt and outcome hashes agreed, local update policy stayed unchanged,
and no transaction journal or rejected-release marker remained.

The fixture seeds work mode only inside its isolated distribution; the product
default is unchanged. The candidate includes inherited uncommitted work and was
not uploaded. This accepts desktop retry, archive staging, replacement and health
for the current reader/helper; older-reader and production-source acceptance are
separate. Evidence is retained in ignored
`artifacts/archive-retry-desktop-20261023/desktop-receipt.json`.

## Archive fallback retry retains its incremental plan (2026-10-23)

Archive-volume progress previously replaced the task file list used by the next
incremental attempt. After an archive failure or pause, Retry or Resume could
skip every selected payload and report ready with no staged objects. Four new
regression cases reproduced that behavior before the fix. The service now retains
the original changed-file plan independently of transport progress and restores
it before each worker starts, without another installed-file scan.

All 17 focused archive tests pass, covering unavailable and corrupt archives,
recovery through incremental files or a repaired archive, unchanged installed
content, repeated failed retries and pause/resume. Each successful new fixture
checks the exact selected SHA-256 content; repeated failures cannot expose a ready
directory. These are isolated reader/extractor fixtures, not a published release
or renewed native installation acceptance. Existing UI and source configuration
stay unchanged; no release was uploaded.

Release solution build passes with zero warnings and errors. The full managed
suite passes 916 tests with 8 environment-gated skips and no failures.

## Responsive server cards and retained store refresh (2026-10-22)

Server cards reserve their supported-version row before asynchronous metadata
arrives, pulse placeholders during initial loading and fade in labels without
changing height. Refresh retains existing version labels until replacement.
The grid uses a 380-pixel minimum and the available workspace width, including
navigation expansion. Favorite and Details use standard buttons; Select server
uses an accent button with a connection icon. Actions have equal heights and
retain complete labels. Very narrow cards move the primary action below the
other two controls instead of clipping it. Reduced motion disables the pulse.

Rendered browser checks at widths 960, 1100, 1360 and 1920 show two, two, three
and four columns without card or action overflow. Expanded navigation at width
960 reduces the grid to one column. Loading, loaded, empty and long-version
fixtures retain the same 276-pixel card height at desktop widths. Screenshots
and the verification fixture are retained in ignored local artifacts.

Store refresh keeps the previous catalog and selection visible while the shared
progress ring indicates loading. Changed source endpoints discard old content;
failures retain the previous list with a source diagnostic. All 180 frontend
regressions, type checking and production build pass. These UI changes remain
with the ongoing server-interface worktree and have not been published.

## Responsive native bridge dispatch (2026-10-22)

Native web messages now schedule synchronous backend handlers, diagnostics and
serialization on workers instead of entering them on the window callback.
Backend admission is bounded to 64 requests and eight active workers; window
operations have separate admission and marshal native changes to the UI thread.
Canceled queued calls retain error replies, overload is explicit, and slow-call
diagnostics contain registered method names and elapsed time without arguments.
Local workspace initialization no longer awaits remote server catalog loading.

The Release solution build passes with zero warnings and errors. All 180 current
frontend regressions pass, including startup readiness before delayed catalog and
store replies. Dispatcher tests cover blocked synchronous handlers, saturated
workers, independent window commands, cancellation and malformed requests. The
earlier full managed run had one process-inspection fixture failure; its targeted
recovery rerun passed. Renewed native acceptance completed: the window appeared
in 356 ms, frontend readiness was acknowledged in 2,320 ms, and none of 357
samples during one minute reported an unresponsive visible window. Native Home,
server cards, navigation expansion/collapse and normal shutdown were observed.
Store synchronization and remote catalog requests continued independently; the
slowest recorded catalog request took 5,290 ms. This is one local run, not a
guarantee for every machine. No updated distribution has been published.

## Current-package signed desktop revision acceptance (2026-10-23)

A fresh self-contained x64 0.1.23 package includes the responsive bridge and
accepted server-card interface. Package verification accepts its managed
versions, architecture, detached updater and complete frontend/overlay payload.
An isolated higher-revision fixture changes only a stylesheet comment; both
installations keep version 0.1.23. Its RSA-3072/SHA-256 detached index signature
is required by the copied installation policy and independently verified. The
temporary signing key was held in process memory/environment and discarded;
no production signer or remote publication is involved.

The current reader discovers and stages the signed fixture, requests one changed
object, reuses 989 installed destinations and restores ready state offline. Native
installation confirmation hands off to the current external updater, which
completes replacement and automatically restarts an x64 STA host. Its actual
Home page renders with work mode retained, and frontend health is acknowledged
4,646 ms after the child startup event. An independent four-worker audit matches
all 990 installed sizes and full SHA-256 values, totaling 388,607,996 bytes. The
source policy is unchanged; no installation journal or rejection marker remains.
The local receipt and retained lifecycle events are under
`artifacts/signed-desktop-20261023/`.

This accepts a signed same-version resource revision with the current reader,
helper and native frontend. Earlier separate receipts cover version-changing
binary replacement and rollback cases. It does not establish production signing
custody, public-source signed installation, or acceptance on another computer.

## Unconfigured source-build transfer recovery (2026-10-22)

An installation without update sources no longer restores a transfer left in
shared local data by a configured distribution. It remains unconfigured without
reporting invalid remote metadata, and preserves that transfer unchanged. Adding
a source still permits ordinary discovery. The focused regression reproduces
the stale-transfer case and verifies preservation and later source discovery.
Release solution build passes with zero warnings and errors. This is a local
recovery fix; it does not configure a feed for source builds or publish a release.

## Developer workspace and optional work mode (2026-10-22)

The developer destination now opens a workspace with Developer options and Store
source management tabs. Source management is reserved for later implementation.
The optional `nsfw` preference defaults to off and persists locally. Enabling it
unmounts home videos, cover images, companion audio and media controls, retaining
the existing layout with a theme-colored background. Hiding developer mode does
not silently reenable media. The work-mode card uses a briefcase icon.

All 173 frontend tests pass; frontend type checking, production build and the
Release desktop-host build pass, the latter with zero warnings and errors.
Browser inspection confirms the card, tabs and media-free home after reload.

## External installation diagnostics in update details (2026-10-22)

The existing update-details dialog now includes the selected release's last
unsuccessful helper outcome, fixed installation phase and numeric HRESULT.
Source identity and exact index hash scope the view; successful, unrelated and
unknown outcomes are omitted. Unknown phase/category text is discarded. Older
helpers remain readable without invented diagnostics. Discovery and restoration
cache the safe projection, so frontend polling performs no additional file or
network work. Source edits clear it, and canceled discovery restores the previous
view. Retryable storage diagnostics remain available when staging is ready again.

Release solution build passes with zero warnings and errors. The complete managed
regression passes 902 tests with eight environment-gated skips and no failures.
All 172 frontend tests pass, including 34 updater checks for ready-state diagnostics, success
cleanup, unmount races and rejection of arbitrary diagnostic text. Frontend type
checking and production build pass. The isolated current host renders its home
page; detailed diagnostic desktop inspection remains separate from component
regression. These changes have not been packaged or published.

## Retryable local updater failures (2026-10-22)

The helper records an allowlisted failure category, operation phase and numeric
HRESULT without exception messages. Staging/replacement storage failures no
longer mark the candidate as a failed startup release: restoration preserves
retryability and installation still requires explicit consent. Startup health
failures and post-startup integrity failures retain exact-source rejection.
Legacy rollback records without a failure category remain conservative, and
existing rejection markers cannot be cleared by a storage result.

A regression first reproduced a storage rollback permanently blocking the
same index. It now restores the verified ready selection. The external-process
fixture also locks a staged file, observes the storage diagnostic, restores the
old installation and confirms no rejection marker. Healthy, crashed-child,
invalid-readiness and configured-signature helper cases remain covered. All
137 updater checks pass; one opt-in public transfer check is skipped. These are
working-tree validations, separate from the earlier published 0.1.22 acceptance;
this helper repair has not yet been packaged or published.

## Public 0.1.21-to-0.1.22 desktop replacement (2026-10-22)

An unchanged 0.1.21 x64 installation reused the previously verified public
0.1.22 staging, accepted the normal installation confirmation and handed off
to its shipped external updater. With sufficient disk space, the helper
completed replacement and restarted a 0.1.22 x64 STA host. The frontend
acknowledged readiness and its actual home window rendered normally. An
independent four-worker audit matched all 990 installed files, 388,573,961
bytes, the installation receipt and the unchanged source policy. The local
receipt and retained lifecycle events are under
`artifacts/public-archive-desktop-retry-20261022/`.

The first isolated attempt rolled back before the replacement frontend was
observed while disk capacity was constrained; its exact failure was not recorded
by the shipped helper. Preserve that outcome separately from the successful
retry. This establishes the published candidate's unsigned installation and
rendered health on this machine, not production signature custody, another
computer's acceptance or the later working-tree changes.

## Structured name-resolution failures (2026-10-22)

Server routing translates resolver socket errors, missing usable addresses,
resolver I/O failures and public-DNS transport failures into the shared public
failure fields. Diagnostics retain only transport categories and HTTP status;
exception messages, request URLs and inner exceptions are discarded. Both API
routing and direct game endpoint resolution use this boundary. Caller
cancellation remains cancellation, while resolver-owned timeout remains a
timeout. Invalid API data keeps its validation error. Eleven deterministic
regressions cover these paths, including credential-free catalog fallback and
refusing to send login credentials after resolution fails. The targeted server,
presentation and multi-account suite passes all 87 tests. The full Release
solution build passes with zero warnings and errors. Live service admission and
rendered desktop verification remain separate acceptance gates.

## Shipped-reader public archive fallback acceptance (2026-10-22)

The verifier builds directly against unchanged 0.1.21 x64 distribution
assemblies and checks their identities before acceptance. All 20 HTTP/AList
fixture cases passed, including incremental success, ZIP and split ZIP fallback,
release mismatch, corrupt contents and unsafe archive entries. The actual public
AList 0.1.22 feed then passed discovery, injected incremental failure, archive
companion selection, resumed ZIP transfer and complete archive/content SHA-256.
The 208,347,184-byte ZIP was requested once with Range after retaining
142,475,264 bytes from the interrupted run. The download task listed one archive;
offline restoration recovered exactly 95 changed destinations from the 990-file
index. Installation files and source policy remained unchanged. The receipt is
`artifacts/public-archive-final-20261022/case/receipt.json`; the fixture receipt is
`artifacts/fallback-fixture-20261022-sdkfix/receipt.json`. These ignored local
receipts establish staging and offline recovery, not external replacement or
rendered frontend health. The resumed run's elapsed time is not a fresh-download
benchmark. Paired verifier guides document resumption without another full copy.

## Authenticated cached launcher selections (2026-10-22)

Discovery retains a configured detached signature beside the selected index.
Staging, offline restoration, the parent handoff and the external updater share
one reader that authenticates the saved bytes under the current source policy.
Missing proof, modified signatures and key changes cannot implicitly authorize
an older cached selection. Sources without signature settings remain supported.
Signature and index snapshots use the existing durable atomic record writer;
an interrupted pair fails validation rather than starting replacement.

Twelve regressions first reproduced the missing checks across HTTP and AList.
The repair and current repository-definition assertion pass 13 focused checks.
Real-process helper coverage now includes authenticated healthy startup and
invalid-readiness rollback. Integrated working-tree regression passes 866 tests
with eight environment-gated cases skipped and no failures. These checks use
ephemeral test keys and do not provision a production signer or
establish public release or rendered desktop acceptance.

## Loose-asset client installation acceptance (2026-10-22)

Removed artificial file-count, directory-count, package-size and metadata-size
ceilings across extraction, transport readers, installation journals, ownership,
file-check caches, saved downloads, updater staging and publication tools. The
abandoned raised limits are not retained. Client cloning and library enumeration
also no longer impose a fixed size, count or depth budget. Source catalogs and configured source
lists also have no fixed count or byte ceiling. Concurrency, cancellation,
protocol representation, exact declared sizes and integrity checks remain.
A durable 4,200-file rollback/cache case and an 8,200-object queued installation
case cover cleanup and restart. Additional regression exercises a published
index over the former 256 KiB and 1,024-file ceilings through download and
ready-state restoration, plus streamed metadata beyond the former 4 MiB ceiling.

Local installation destinations accept literal `#` and `%` asset names while
transport source paths retain their URL restrictions. CN2012 contains the original
`Images/#Screen` tree. Path traversal, device names, aliases and launcher metadata
remain rejected. Folder publication retains these literal destination names and
uses hashed transport paths. All 90 publisher tests pass. The local CN2012 archive
passed native extraction, transactional installation and cache restart; the combined
archive selection passed 24 with two environment gates skipped. CN2012 then
passed actual anonymous download, complete archive SHA-256, native extraction,
installation, executable discovery and receipt/task restoration after restart.
No game was executed. Managed regression passed 801 with eight environment
gates skipped; publisher regression passed all 90 cases. Final release publication
remains pending.

Installation tests and release builds use an explicit workspace on the data
volume. The earlier interrupted system-temp test workspace has a separate,
confirmation-gated local cleanup script; it is excluded from source control.

## Registering pre-uploaded client archives (2026-10-22)

The dedicated registration path preserves existing package rows and accepts new
archive packages only. Local SHA-256 and provider-MD5 matching share one local
read; anonymous one-byte access is checked separately. Provider evidence is
explicitly labeled and does not substitute for the installer's complete SHA-256
check. Immutable manifest conflicts, archive changes and catalog-baseline changes
prevent publication. Check-only mode writes a local credential-free receipt and
never uploads metadata or payloads. Publisher regression passed 89 cases. All 11
uploaded client archives passed the local/provider identity and anonymous-access
preflight. The CN2011 archive passed actual anonymous download, whole-archive
SHA-256, transactional 7z installation, executable discovery and restored task/
receipt checks after restart. No game process was started. Registration itself
remains pending a compatible Launcher rollout; other client installation checks
are not implied by this one archive acceptance.

## Native languages, translations and optional build dates (2026-10-22)

Client profiles distinguish `native_language` from an `additional_languages` map
of translation codes to file-plugin identities. Removed planned-language and
build-level client-language fields are rejected. The UI merges the choices,
labels missing translations and opens their package in the store. Launch
validation requires a compatible enabled restorable file package, filters other
translations from the transient launch selection and retains native restoration.
The existing ticket request carries launcher, native and selected languages.
No unfinished translation payload is advertised as an available distribution.

Optional `build_date` replaces the required year. Its exact invariant string
format is `yyyy-MM-dd HH:mm:ss`; missing dates are valid. Bundled dates retain
previously investigated signing evidence and its existing presentation convention.
Profiles preserve their supplied titles without rewriting names from dates.
Language/file-restore and culture-independent date cases have automated coverage;
actual translation-package and desktop acceptance remain separate gates.

The per-client integrity checkbox is connected to both creation and editing.
It defaults on and persists with the instance. Turning it off skips automatic
client update discovery, while explicit manual checks and download SHA-256
verification remain available. Startup plugin retirement checks whether any
store release can be retired and excludes queue admission while holding client
leases. Active or queued transfers defer retirement. A synchronized transfer test
covers both sides of that exclusion; the HTTP/AList store bridge covers persisted
client preferences. Full managed regression passed 792, with six environment
gates skipped; frontend type-check and production build passed.


## Streaming download verification and archive inventory (2026-10-22)

HTTP downloads hash newly received bytes while writing, avoiding a second complete
read of a fresh download. Resume hashes the saved prefix before requesting the
remainder; a server ignoring Range resets both the file and hash. Corrupt-prefix
tests cover accepted ranges and complete restarts. Existing-file and recovered
partial validation still checks their full contents.

Local archive audits read payloads without copying them and retain size, file
identity, timestamps and SHA-256 receipts. Anonymous remote names and sizes have
been compared separately; this does not prove remote byte identity. The original
CN2012 archive lacked the game executable. Its replacement contains the executable
and has a new local digest. Its remote name and size now match the replacement;
this remains metadata evidence, not a remote SHA-256 proof or installation
acceptance. The intermittent queued-transfer busy failure was traced to plugin
retirement acquiring client leases during download polling; the maintenance/queue
exclusion above addresses that race without weakening the instance lease.

## Protocol and distribution labels (2026-10-22)

Client profiles now use one required numeric `protocol_version` instead of
`client_version` and the separate wire identity field. Client IDs can distinguish
multiple releases of the same protocol; launch ticket requests retain the region
and numeric protocol identity without a client-disambiguation suffix. Optional
release and patch labels travel in that same request. The game module reads
bounded optional version metadata without making its presence a launch condition.
Client selector captions display release labels before patch labels while keeping
their existing titles. Bundled profiles include independently checked distribution
labels. Public source migration and desktop acceptance remain open; local unit
coverage alone does not establish publication or installation acceptance.

## Invalid frontend readiness rollback (2026-10-22)

The real detached-helper fixture now starts a child that connects to its health
pipe but sends an invalid readiness byte and remains alive. The helper must
terminate that exact child before reporting rollback, restore the previous
receipt and retired files, remove newly installed frontend chunks, retain the
source-bound rejection marker and restart the previous launcher. This extends
the healthy-start, early-exit and installation-lease cases without changing the
production handshake. The fixture uses controlled processes, not a rendered
frontend; actual desktop readiness remains a separate acceptance gate.
All four real-process cases pass, including the explicit rejected-child exit
assertion, with zero build warnings or test failures.

## Signed update transport acceptance (2026-10-22)

Eight service-level cases pass across HTTP and AList with ephemeral in-memory
signing keys. A valid detached signature permits verified staging; a missing or
corrupt signature, or an index changed after signing, rejects the source before
any payload request, download task or persisted release index. Discovery itself
requests no payloads, and shared content hashes download only once after an
authenticated index is selected. These checks exercise sources configured to
require signatures; they do not change the optional-signature policy or establish
production signing-key custody, desktop replacement or retail acceptance.
The complete update-service regression passes 114 tests with zero failures and
one opt-in public-network test skipped; that public staging path has its own
successful receipt below.

## Cross-revision incremental request accounting (2026-10-22)

The changed-file staging regression now exercises both HTTP and AList from
revision 1 directly to revision 4 and counts actual payload requests. All four
cases pass: an unchanged frontend object is neither downloaded nor staged;
content already present in another installed path is reused without a payload
request; two changed paths sharing one new hash download only one object. AList
resolution counts also exclude unchanged and locally reusable objects. These
are deterministic transport-fixture checks, not a public-network speed benchmark.

## Anonymous public update staging acceptance (2026-10-22)

The opt-in public-source test passed on win-x64 with .NET 10.0.12: one passed,
zero failed and zero skipped. Starting from an empty isolated installation, it
downloaded and independently verified all 964 release files (389,694,005 bytes)
for revision 1792579979, then restored the ready state after restarting with
network access disabled. The test took 9 minutes 41.780 seconds; this is a full
fresh staging measurement, not an incremental update or discovery benchmark.
The TRX is retained locally under
`artifacts/public-alist-staging-20261022-results/`.

This accepts anonymous public payload staging and offline readiness restoration.
It does not accept GUI rendering, actual installation of this public release or
completion of its separate manual-download archive. Desktop automation failed
initialization twice with a missing kernel-assets path, so fresh desktop
acceptance remains open. Existing focused tests cover changed-files-only staging,
concurrent distinct-object downloads and duplicate-hash reuse; the public test
does not independently establish incremental transfer speed.

## Compiled frontend entry validation (2026-10-22)

The complete-package gate now parses the frontend HTML and checks its external
scripts, stylesheets and module preloads before indexing. An absent or empty
compiled asset, a missing script entry, a remote entry or a path escaping wwwroot
fails publication. Root-relative paths and query strings remain supported.
Eleven payload-gate tests and the real x64 verification output pass. This gate
covers HTML entry references, not dynamically constructed resource paths or
browser rendering. Frontend type checking and production compilation also pass.
The updater regression passed 108 tests, including all three real-process helper
cases; one opt-in public staging test was skipped. Anonymous reads of the formal
and preview feeds both returned 0.1.21, revision 1792579979, with manifest SHA-256
`b253e6f54bc6d3165a08416910f13650aa012e1ab058c8469661162d35a069fb`.
These reads establish current metadata availability, not archive completion or
retail update acceptance.

## Portable renderer restore targets (2026-10-22)

A real x64 host publication failed with NETSDK1047 because the renderer's
framework-dependent build left restore assets without its portable target.
The renderer now declares both supported Windows runtime identifiers, so the
initial restore also retains the targets required by the later nested publish.
The same host publication then completed successfully, including its detached
updater and self-contained renderer. Version-matched runtime notices were copied
from the installed packages into the new verification output. Detached updater
execution, complete x64 payload verification and all eight payload-gate tests
passed. This output is local validation only; no frozen release or remote feed
was replaced, and retail renderer acceptance remains open.

## Version-matched runtime notices in portable packages (2026-10-22)

Portable packaging now reads each host, detached updater and overlay runtime
configuration and copies the matching installed runtime package license files.
NETCore includes its license and third-party notices; WindowsDesktop includes
the license supplied by its package. Missing or empty inputs fail packaging
before indexing. The complete-payload verifier independently rejects missing or
empty runtime notices beside each executable. All eight payload-gate tests pass
for x64 and ARM64 fixtures. A separate run using the installed x64 10.0.12
runtime packages copied seven real notice files and rejected a missing cache.
No frozen package was changed; complete new publication remains unverified.

## Cross-volume fixture validation and dependency evidence (2026-10-21)

Repository-backed tests now find the source checkout from either the output
ancestry or the compiler-provided test source path. This preserves real fixture
validation when isolated outputs are on a different drive. The service manifest
rejection theory normalizes line endings and asserts that its mutation target
exists before writing the invalid input; CRLF sources previously left two inputs
unchanged. The complete Core suite rebuilt and passed 739 tests with zero failures
and six environment-dependent skips using isolated outputs. This does not replace
retail client or public-network acceptance. The bilingual public repository audit
also records installed managed package versions, license declarations and available
source commits; native package provenance and combined UI licensing remain open.

## Archive fallback metadata transfer budget (2026-10-21)

Complete-archive fallback now allows thirty seconds for its metadata transfer,
matching discovery and the source document reader. Previously its outer fifteen-
second cancellation rejected valid slower responses before the reader's budget
expired. A sixteen-second archive-index regression failed with the old limit
and passes with the corrected limit; it checks ready staging and exactly one
archive payload request. The focused updater and update-record suite passed
108 tests with one public-network test skipped. This is local automated
acceptance; the frozen release and remote archive publication are unchanged.

## Bounded helper integrity concurrency (2026-10-21)

The external updater now checks staged objects and performs its post-startup
full installation hash check with two to eight concurrent workers, matching
the staging comparison bound instead of awaiting each file serially. Every
post-startup file still receives a full SHA-256 check before durable commit.
The updater regression passed 107 tests with one public-network test skipped.
The real-process helper fixture now includes 16 distinct frontend assets and
checks every installed or rolled-back asset; all three healthy, crashed-child
and occupied-installation-lease cases passed after that fixture expansion.
No measured end-to-end speed claim or new remote release is implied.

## Update record publication under Windows locks (2026-10-21)

Atomic update record replacement now retries Windows sharing and lock violations
for at most one second, matching the existing bounded reader tolerance. Persistent
locks still fail without replacing the previous record; failed temporary records
are removed so a later write can retry normally. Tests cover brief and persistent
destination locks, unchanged previous state, retry after release and open-reader
snapshot preservation. Release compilation and focused updater regression passed:
107 tests passed and one public-network acceptance test skipped. This is automated
local acceptance, not a new remote release or retail update acceptance.

## Remote source delivery verified (2026-10-21)

Both Launcher and Bubble Fighter remote `main` branches now contain source.
Launcher starts at a new root commit while the original development history is
preserved in local archive branches. Remote development branches were removed
after local preservation. Bubble Fighter includes 13 build profiles, 14 plugin
definition directories and native integration sources in addition to standalone
Auto Dash. Integration projects still depend on shared Launcher build inputs;
independent migration and the remaining redistribution audit stay open.
Both repositories remain private. The bilingual repository inventory and audit
record distinguish source delivery from public redistribution approval.

## Private game repositories created (2026-10-21)

Created the private BubbleFighter, TalesRunner and AirRider launcher integration
repositories with bilingual boundary documentation. The existing launcher retains
SDK, generic runtime and overlay ownership. Source migration, pinned dependencies
and independent game build acceptance remain open; server repositories are unchanged.

## System font packaging (2026-10-21)

Removed the frontend font URL and bundled icon TTF from the current source tree;
the original is preserved privately. Text uses installed Windows fonts, and icons
use Segoe Fluent Icons with Segoe MDL2 Assets fallback. Clock and blocked icons
now use inline SVG because the inspected MDL2 cmap lacks their selected glyphs.
Added an ignored private-font input for local builds: Vite includes the supplied
font and restores its original glyphs when present. An explicit system-only mode
produces no font asset. Type checking and both build modes pass; private output
font SHA-256 matches its input. Local output is restored to private-font mode.
See the bilingual audit for Windows 10 and Git-history boundaries.

## Clean source build audit (2026-10-21)

An isolated export of committed source passed frontend type checking and its
production build, native Release/x86 compilation, and all six native test
executables. Added bilingual clean-build evidence and a publication gate that
rejects missing or empty decoder source, provenance and LGPL files; all seven
payload-gate tests pass. Vite is pinned to 8.3.5 after development-server
advisories: isolated frontend type checking, production build and 140 tests pass;
the working tree passes all 148 tests and npm audit reports zero vulnerabilities.
Full clean publication, remaining provenance review and game repository migration stay
open; repository visibility remains private.

## Native repository split preparation (2026-10-21)

Decision D30 keeps the generic launcher/runtime separate from game-specific
implementations. Added a narrow native C ABI SDK exporter, bilingual migration
guide and a standalone x86 sample build. The sample builds from an exported
temporary directory with Visual Studio v145; the package includes a per-file
SHA-256 inventory and no game binary or implementation. Current authentication
and movement projects still link in-repository helpers, `nmcogame` remains in
the launcher checkout and native packaging combines runtime and game payloads.
Source migration and separate repository creation remain open. This is not a
public-release approval; outstanding legal and clean-checkout gates remain in
the repository audit.

## Unified archive extraction (2026-10-21)

Consumer callback failures now retain their original managed exception even when
the native decoder stops before reporting an entry result. Cleanup releases the
archive object despite partial-file deletion failures; repeated disposal is safe,
and extraction after disposal is rejected. The archive, plugin import and launcher
update regression selection passed 202 tests with two environment-dependent skips.
An isolated source export passed all 17 archive tests with the external fixture
matrix. Completing the SDK catalog metadata contract also corrected an existing
publication-tool compilation gap; the isolated solution build then passed with
zero warnings and errors. Both decoder DLLs match official 26.04 installers.
Corresponding source, complete LGPL text and retrieval hashes are now retained
beside the libraries for subsequent build output. Final publication inspection
and the remaining repository audit gates are still open.

Client installation, launcher complete-archive fallback and plugin ZIP import now
share the packaged native 7-Zip decoder. SharpCompress and the separate RAR-only
adapter are removed; archive writers used by packaging and fixtures remain
unchanged. ZIP, 7z and RAR4/RAR5 coverage includes passwords, solid and split
archives, checksum/hash failures, missing volumes, unsafe paths, cancellation and
recovery. The focused managed suite passed 239 tests with one public-network skip;
the win-x64 native archive matrix passed all 13 tests. The Release solution build
has zero warnings or errors, and the full managed suite passed 730 tests with six
environment-dependent skips. The implementation currently targets Windows x64
and ARM64 only; package redistribution review and clean-checkout publication
verification remain open.

## Incremental progress and metadata deadline acceptance (2026-10-21)

A frozen 0.1.21 reader staged a three-file release with one changed payload:
exactly one payload GET, one visible task entry, and one visible entry after
offline restoration. The unchanged payloads were not requested. The acceptance
tool now records distinct object identities, resolution and payload requests,
visible counts, and restoration counts; arbitrary source filenames are mapped
to their declared hashes. Storage addresses stay in memory and out of receipts.
The working-tree update suite passes 99 tests with one explicit public-network
skip. A separate sixteen-second metadata regression passes after aligning the
working-tree source deadline with the metadata reader's thirty-second budget.
That deadline change is not in the frozen release. Public older-reader staging
failed during discovery; an independent .NET probe read the correct public index
in 26.88 seconds after a 2.21-second resolution, exceeding the prior fifteen-
second discovery budget. Public staging and complete-archive acceptance remain
unproven; the existing archive uploader was confirmed live and not restarted.

## Optimized publisher live acceptance (2026-10-21)

The previous publisher was stopped at the owner's request before switching to
file-list reuse. The replacement reused 568 published objects and completed all
569 selected objects. Anonymous reads of the official and preview feeds match
0.1.21 revision 1792579979 and manifest
`b253e6f54bc6d3165a08416910f13650aa012e1ab058c8469661162d35a069fb`.
Incremental updates are available. The separate complete-archive process is still
running; archive fallback acceptance is not yet claimed. The full managed suite
passes 725 tests with six environment-dependent skips and no failures. All 148
frontend tests pass; lifecycle warnings from the test harness remain visible.
Plugin author/uploader changes are committed separately and are not part of this
frozen release.

## Plugin author and optional uploader metadata (2026-10-21)

Store packages expose plugin authors separately from their publisher and optional
uploader. All 14 current plugin definitions credit Fantasy Tale Studio. Empty
uploader values are omitted from publication and UI; the publishing CLI can set
them for future releases. Parser validation, bridge metadata, plugin/store UI and
six locale labels are implemented. The 22 focused managed store tests, frontend
type-check and four working-tree plugin publisher tests pass. Frozen release
contents and existing remote plugin payloads remain unchanged.

## Multi-volume publication concurrency (2026-10-21)

Archive publication supports ZIP, 7z and RAR volume lists and captures all
volume baselines before its first archive write. A second-volume mutation during
the first upload is rejected instead of silently becoming the new baseline.
The regression fails against the previous implementation and passes with the
fix; all 60 working-tree uploader tests pass. Feed aliases and archive fallback
metadata also reject changes made during archive transfer. The live incremental publication
uses the file-list optimization and remains separate from complete-archive
acceptance. No frozen release contents were changed.

## Publication file-list reuse (2026-10-21)

Launcher publishing reuses unchanged content-addressed objects from the prior
public file list instead of downloading every object on every publication.
The bounded inventory is bound to the initial catalog digest; new objects and
mutable catalogs retain byte verification and concurrent-change checks. The
working-tree uploader suite passes 58 tests; the isolated staged uploader suite
passes 57. Existing running publishers retain their loaded implementation.
Plugin author/uploader presentation is implemented in the working tree; 22 store
checks, frontend type-check/build and four plugin publisher checks pass. Frozen
release contents have not been changed. Store-wide inventory reuse and online
publication acceptance remain open.

## Launcher startup regression (2026-10-21) — local acceptance passed, publication pending

An async executable entry point did not carry the STA contract to the generated
runtime entry point. Real x64 launches logged MTA, created an empty native window,
and never started their WebView child. A synchronous STA entry point with the same
frontend resources starts the WebView child and receives frontend bridge messages.
Keep the real entry-point attribute regression test and the bounded apartment and
window-initialization lifecycle events. External update health confirmation and
publication of the rebuilt candidate were the remaining gates. Local acceptance
now records a completed external replacement for manifest
`b253e6f54bc6d3165a08416910f13650aa012e1ab058c8469661162d35a069fb`,
with the restarted host serving frontend bridge requests. The final package adds
session-only navigation expansion, so each launch starts with a compact sidebar.
Its older-reader staging and offline restoration checks passed, and its restarted
0.1.21 settings window was inspected with a compact sidebar. Incremental publication
is running; online verification and archive publication remain pending. The rejected release marker
continues to block the exact failed manifest; a newer manifest must remain eligible.
The earlier 0.1.21 publication was stopped before publishing its mutable feeds.

## Store profiles and shared update redesign — in progress

The active execution plan is [Store profiles and shared updates](decisions/store-profile-update-plan.md).
Implement unified startup-synchronized client profiles, globally shared plugin
updates and launch snapshots, Fluent multi-client installation and pending-update
confirmation, and ordered multi-source launcher discovery with bounded waits.
Only the official AList display label becomes Fantasy Tale Archive. Existing
publication and gameplay acceptance remain separate; do not broaden CN2011 work.


## KR1256 authentication store release

Public component `bubblefighter.fantasy.kr-auth` 0.1.1 targets only `kr-1256`.
Its trusted bundled definition requires installation, enabled selection and the
latest release from `fantasy-tale`; the build defaults and recommendations include
it. Release x86 rebuilding reproduces the accepted DLL SHA-256
`4567c5351b48f8bfec7529623db61384316621f46cf27472ee2edaa54ab1acaa`.
All 50 native contract checks pass. The owner accepted the original packed client,
existing character main menu and shop in the handoff. The publisher appends one
immutable release while preserving the prior 15 catalog entries. Anonymous
catalog, manifest and five payload files are verified against local bytes in
`artifacts/kr-auth-publication/publication-receipt.json`.
A fresh temporary plugin library also installed 0.1.1 through the real anonymous
AList transport and accepted its source receipt, mandatory scope and latest-only
policy. This check executes no native game code. The 80 focused policy/import
tests and two publisher tests pass. The full managed suite was stopped after
four crash-recorder fixture timeouts (CN279, CN706, KR812 and CN717); it had
643 passes and six skips and is not reported as fully passing.

A new clean-instance gameplay launch was not repeated during publication; prior
owner acceptance uses the identical DLL. Frozen Launcher 0.1.18 lacks the trusted
component registration and remains unchanged. Existing old installations require
a launcher release carrying the updated game catalog before this newly mandatory
store identity can be installed. This distribution step remains open.

## Launcher self-update handoff (2026-10-18)

HTTP and AList discovery, bounded resumable object downloads, exact file
verification, external replacement, startup health acknowledgement and rollback
are implemented and covered by managed and frontend tests. Background polling
updates progress and the update badge without opening a dialog; installation
requires an explicit user action. Release signing is not claimed: the
publisher still needs to provide the public verification key and the signed
manifest format before a signed feed can be enabled.

## CN2011 native forward-jump preview (2026-10-18) — rejected behavior

Gameplay feedback identifies the native capability preview as the forced-jump
bracelet behavior, including jumping out of a roll. It is not accepted for the
ordinary W + Space change. Preserve the effect-setter evidence for future work,
keep the prototype out of the store, and disable it for subsequent launches.
The older declarative branch remains excluded. Auto Dash succeeds and remains
independent. The ordinary CN2012 input path and the source of its default
behavior still need to be established without granting an equipment capability.

## Existing login settings synchronization (2026-10-18)

The Bubble Fighter module synchronizes only existing login.ini endpoint fields
through a launch preparation operation after read-only validation. Matching
settings retain their bytes and timestamps; unrelated fields, ANSI bytes, UTF-8
and UTF-16 BOMs, and CR/LF line endings are preserved. The accepted CN2011 timer
implementation follows recreated game windows and dispatches on their thread;
callback and actor-timer diagnostics remain bounded.

## Existing-client discovery and plugin preloading (2026-10-18)

Resolve a unique client root below a selected folder with bounded traversal.
Verify existing imports before registration, reuse registrations on retries, and
repair an invalid enclosing external path only after matching the original build.
Game files are never moved or removed. Ambiguous selections require a narrower
folder. Store plugins may download into the shared library without a client or
activation intent, through the same persisted queue. Explain unavailable game,
plugin capability and build requirements in all six UI languages.

## CN2011 movement plugins (2026-10-18) — startup compatibility blocked

Earlier executable-patch and capability experiments caused startup failures and
were excluded. They are superseded by the accepted native timer implementation,
which was tested in Preview and merged into the generic plugin. Reports of
movement issues without plugins remain tracked separately.

The accepted CN2011 native timer implementation is part of the generic
`bubblefighter.fantasy.auto-dash` plugin under its `cn-279` build branch. The
separate preview catalog entry has been retired; CN2011 does not require a
separate plugin identity or download. Reports of movement issues without
plugins remain tracked separately.

The optional `bubblefighter.fantasy.forward-jump` research packages target only
the `cn-279` CN2011 build and remain unpublished. The first native capability
prototype was rejected because it reproduced the forced-jump equipment effect,
including jumping from a roll. The current local candidate gates a temporary
capability write on the actual W/Up plus Space/Num0 request and actor states
101/103, restoring its own write when the request ends or state 104 begins.
This remains an unverified hypothesis; it is not a released feature and must
not be described as the official newer-client behavior.

## KR1256 authentication component (2026-10-18) — in progress

The required, latest-only `bubblefighter.fantasy.kr-auth` native plugin applies
only to `kr-1256`. Verified image/signature data selects the native login
destination builder and allocating string assignment. GamePatch startup
completion runs before login-stage readiness; ticket submission runs once on
the game UI thread, with a bounded 120-second cold-start wait. Native protected
values and relay destinations retain their original paths. Each launch records
the actual executable path and PE timestamp without arguments or credentials.
Contract checks pass; real-client ticket redemption and public store publication
remain open. The frozen Launcher 0.1.18 package is unchanged.

## Auto Dash plugin (2026-10-20) — CN2011 gameplay accepted

The optional `bubblefighter.fantasy.auto-dash` package uses one plugin identity
with build-scoped implementations for KR2015, CN2011-CN2016 and ID2014-ID2015.
CN2011 uses the accepted native forward timer; other listed builds retain their
verified double-press timer patches. The configured forward input and native
movement eligibility checks remain. The plugin option defaults on; changing it
requires restarting the client. `kr-1256` is excluded because it already has
native support; modern inlined timer paths remain pending separate verification.
CN2011 gameplay was accepted in Preview and the unified 0.2.0 package was
published and anonymously verified. Its separate preview catalog entry has been
retired. The publication receipt is
`artifacts/Auto-Dash-Unified-0.2.0/publication-receipt.json`.

## Restart-safe stopped-client recovery (2026-10-18) — complete

When the launcher lists instances after a restart, it now attempts the existing verified recovery for a leftover session or native transaction. Recovery first inspects every configured executable and refuses live or inaccessible processes; only a confirmed stopped client has its native files restored and session removed. Failed or uncertain recovery keeps the evidence and the manual recovery action. The fix is version 0.1.18 and does not alter the frozen 0.1.17 package.

## Plain-language update discovery failures (2026-10-18) — complete

Discovery exposes allowlisted reasons and an optional HTTP/source status, without exception text or remote reply bodies. Shell notices and Settings explain timeouts, DNS/connection/security failures, denied/missing/busy server responses, invalid metadata and local storage errors. An older public feed explicitly reports that the server has not caught up with the installed/seen version. Settings retains technical codes in collapsed details. AList preserves non-transient 4xx envelope codes without retrying them. These source changes follow the frozen 0.1.17 package; its ongoing publication remains unchanged.

## Notification lifetime and plugin completion coordination (2026-10-18) — complete

Home update details start collapsed and reset on task changes; only an explicit click opens them. Shell error notifications expire after six seconds with hover/focus pause and retain manual dismissal. Restored client history no longer triggers recommendation claims, claims wait for active same-client transfers, and acknowledged recommendations have a read-only fast path. Download workers release the instance lease before exposing completion. Focused frontend notification/history/recommendation tests and managed held-lease/import/download tests cover these paths. This change is version 0.1.17; frozen earlier publication packages are unchanged.

## Modern launch repair and restored download history (2026-10-18) — in progress

Emergency storage correction: current component selections can be read without taking a mutation lease; plugin discovery defers migrations when another process owns that lease. Typed sharing failures retain the OS error, download UI distinguishes space/busy/access from generic local I/O, and lock conflicts are localized. Eight focused managed storage/historical-component tests pass, including a real held instance lease. Frontend type-check passes. The 0.1.16 x64 distribution is being generated independently of the frozen 0.1.15 publication.

CN2019 now declares its corpus-confirmed SDO argument order and version-512 ticket bridge. TW2019 selects the shared ticket-aware messenger instead of an unverified launch definition. Historical store components remain readable after bundled file-retention policy changes; current and future releases still have to preserve that policy. Installed-corpus registration covers both 2019 builds. Reopened Home ignores previously completed transfers while preserving Downloads history and current-session completion feedback.

Validation: 125 existing/expanded managed cases and the new historical-component regression pass. Nine focused frontend history/prerequisite cases and frontend type-check/build pass. Host Release build has zero warnings/errors. Actual Launcher registration of all three modern builds succeeds. TW2019's retail executable completes ticket login to the main menu with the existing character. CN2019 delivers the ticket but crashes at the login screen (0xC0000005, RVA 0x0064EB8D). Packed KR2026 reaches a Themida loader failure (`d3d9.dll` on local ARM64; remote x64 reports `NGClient.aes`). CN/KR acceptance remains open. The frozen 0.1.15 publication continues independently at the user's request; these changes belong to 0.1.16.

## Permanent Chinese authentication installation (2026-10-18) — complete

File packages may declare `restore_files = false`. Matching size/SHA-256 destinations are not rewritten; completed replacements persist after exit and stopped-client recovery. Incomplete replacement rolls back, and temporary runtime bindings still restore. Chinese authentication 1.0.1 enables this policy and requires Launcher 0.1.15. Package policy is retained across bound-source upgrades. Focused transaction/definition tests cover retention, unchanged timestamps, replacing stale files, mixed temporary/permanent cleanup and incomplete recovery. Release solution build passes with zero warnings/errors; 135 focused managed definition, file transaction and plugin-import cases pass. Retail acceptance and public publication are tracked separately.

## Traditional Chinese source terminology (2026-10-18) — complete

Traditional Chinese UI dictionaries, navigation labels, build/plugin display
definitions and browser preview data use 中國/韓國/台灣/印度尼西亞 for regions,
模組 for project plugins/mods and 程式模組 for binary/program modules. Preserve
泡泡大亂鬥 as the game title and Bubble Fighter Fantasy as the server name.
Paired architecture and maintainer guides record the locale-specific policy.
Simplified Chinese retains its existing server labels and plugin terminology;
internal identifiers and wire contracts are unchanged.

Validation: 621 merged Traditional Chinese UI messages and 21 changed catalog/
preview files pass the source audit. The other five UI locales, nonlocalized
definition values, TypeScript literals outside zh-TW and interpolation
placeholders match the prior commit. The generated production bundle contains
the updated terms, and host output includes the revised TOML definitions.
Frontend type-check/build pass; 48 navigation/plugin cases and 37 managed
definition cases pass. Release solution build: zero warnings/errors. No Launcher
UI or real game was opened; public stores and update feeds were not modified.

## Reversible build availability (2026-10-17)

Build TOML accepts `enabled` (default true) and `hidden` (default false).
Public version, store client/server and server-preset discovery omits hidden or
disabled rows, while the internal catalog retains identity evidence. Disabled
builds reject new client creation, client release installation, game launch and
local hosting. Existing installations and plugin compatibility data are retained;
removing the overrides restores default availability. Browser previews follow the
same declaration. CN2016 `cn-735` is the currently declared 2016–2018 build and is
disabled/hidden pending adaptation. Undeclared research identities remain outside
the selectable catalog; TW2019 keeps its declared product year despite its 2018
executable signature. Focused definition, download and availability coverage passes
79 tests without skips; frontend navigation/plugin regressions pass 6/41 tests,
and frontend type-check/production build pass. The Release solution builds with
zero warnings/errors. Availability tests reject new
creation and existing-client launch, retain the original files and prove that
editing only the TOML restores server-preset discovery. No retail game or
Launcher UI was opened for this change.

## Client archive delivery (2026-10-17) — complete

Client releases now support one declarative archive transport for ZIP, 7z and RAR,
including ordered split volumes and optional publisher-provided passwords. Each
volume remains a separately resumed, content-addressed download. Extraction is
bounded and path-safe; expanded files enter the existing transactional installer,
so an error cannot partially replace a client. Password failures become a local
retry action and do not download the volumes again. Encrypted multipart RAR uses
the bundled 7-Zip decoder for Windows ARM64 and x64; the managed decoder handles
the other supported archive cases. Server package releases remain per-file.

The publisher discovers the common numeric, ZIP split, modern RAR and legacy RAR
volume naming families, writes the archive metadata and password into the pinned
manifest, and excludes unrelated neighboring files. The uploader applies the
100 GiB client-archive object limit while retaining the smaller limits for other
products. Anonymous remote verification uses each graph-verified local object's
exact size, so archive volumes above 2 GiB are not rejected by the old ordinary
object limit. Catalogs and mutable launcher archives retain their existing
bounds. Publisher regression: 62 tests pass, including large-object streaming
control with simulated sizes rather than multi-gigabyte allocations. The archive
runtime license is recorded in `NOTICE.md`.

Validation: the focused managed archive and download tests pass, including a
13-case independent matrix covering plain/encrypted ZIP and 7z, plain/encrypted
RAR4/RAR5, solid RAR5, and multipart archives. End-to-end tests cover split ZIP
installation, encrypted password retry after reopening a task, rollback and
archive-root handling. Publisher tests cover all supported naming families and
reject incomplete volume sets. Frontend type-check and production build pass.
No retail game or Launcher UI was started for this work; the changes are local
and unpublished.

## Official x64 distribution audit (2026-10-17) — complete

Official releases target Windows x64; ARM64 remains a maintainer test build.
The Overlay development build stays independent of the Host RID, but publication
now explicitly publishes the renderer into the distribution's `overlay/` folder
with the requested architecture and its own WindowsDesktop runtime. The prior
build-only copy did not include Overlay in a portable publication. The complete
payload gate now checks renderer/runtime/loader architecture, required UI files,
the archive decoder/license and absence of a local renderer browser profile.
Six regression tests cover missing files, wrong architectures and developer-only
runtime dependencies. Older packages can still use the updater-only verifier.

A fresh x64 local audit candidate completed the full packaging path, frontend
checks, detached updater execution and portable ZIP verification. Its schema-one
index contains 951 files and is 251,827 bytes, within the old reader's unchanged
256 KiB/1024-file limits. The portable ZIP is 206,065,400 bytes. The ignored
`artifacts/release-audit-20261017-x64-complete/receipt.json` records its hashes and
explicitly marks its placeholder source and unpublished state. No Launcher UI or
retail game was opened; this does not replace desktop update acceptance.

The earlier ARM64 runtime-cache download problem was resolved from the already
downloaded local package, and the updater-only ARM64 audit completed. That first
candidate lacked Overlay and is not a complete distribution. ARM64 source and
packaging support remain available for tests, without a separate public release.
Existing x64 0.1.12/0.1.13 detached updater checks also passed. No remote index was changed.

## Native companion consumer bridge (2026-10-16)

Runtime 3.3.3 exposes an optional sized ABI tail with state, subscription, event,
request and bounded polling operations. Native consumer manifests declare exact
type/schema grants; both readers validate them, and launch planning rejects schema
conflicts and excessive owner/route counts before starting the process. The Host
router lazily obtains restricted SDK channels and follows game teardown. Native
events are count/byte bounded and canceled-epoch entries are discarded.

Overlay and companion now share the tested process-authenticated, multi-connection
pipe server but retain independent protocols and lifetimes. A local random IPC
name never substitutes for server launch identity. No renderer is required for
communication, and missing foundation/authorization does not open a socket.

A real x86 runtime client has completed subscribe/send/request/poll/unsubscribe
through the actual Host manager and pipe to a loopback WebSocket, including mixed
Chinese/Korean sends and Chinese event payloads. Native DLL activation additionally checks
the optional ABI tail, local grant denial and older-table fallback. Development
runtime-only packaging preserves existing plugin payloads and published hashes.
Production server authorization/catalog/lease/snapshot integration, live TLS and
retail notification acceptance remain open; no server files were modified.

Validation: the managed solution passed 616 with five environment-gated skips and
no failures, including the actual x86/Host/WebSocket integration. Native checks
passed 132 runtime, 26 messenger, 157 parent activation, 35 dump-writer and 499
synthetic health-rendering checks. Hook launch tests stage the newly built crash
plugin in their private fixture and verify its hash, preserving installed packages.
Tests used a process-local scratch directory under ignored `artifacts/` after the
system temp volume exhausted its free space; no global temp settings or user files
were changed. The feature remains local and unpublished.

## Concurrent native Overlay broker (2026-10-16)

Native plugins previously opened independent clients against one persistent pipe
instance, so the first connection could prevent every later plugin from using the
Overlay. The broker now owns a bounded accept loop for 64 concurrent streams and
authenticates each against the bound game process before dispatch. Invalid UTF-8
and disconnected clients affect only their stream. Disconnect returns capacity;
shutdown closes listeners and active connections. The renderer and native wire
remain unchanged; no extra WebView page is created. The shared panel dictionary's
unregister lookup now uses its existing lock under concurrent native dispatch.

Validation: 37 focused broker, control-channel, Overlay and service tests passed.
Four new Windows pipe tests exercise simultaneous owners, reconnect, malformed
input isolation, delayed binding, incorrect PID rejection, capacity exhaustion and
capacity recovery using real named pipes. No retail game or Launcher UI was started.

The complete managed solution regression subsequently passed 612 with five
environment-gated skips and no failures. Existing frontend type/production checks,
41 plugin UI tests and six panel JavaScript tests passed in the foundation batch;
native suites passed 119, 26, 153 and 35 checks before the managed-only broker fix.
The work remains unpublished; production companion authorization and its native
broker are still separate open integrations.

## Companion service lifetime hardening (2026-10-16)

Authorization initialization failures remain observable until game completion or
Host shutdown, without exposing provider exception text. Teardown attempts later
dependencies even if one release fails. Notification state reflects unavailable
channels and failed bounded result delivery. A reconnect regression composes the
real session and notification consumer, closes the old dialog, ignores its late
button action and mounts a new notification on the fresh connection. Production
server authorization, catalog discovery and native broker integration remain open.

Validation: the final Release solution build passed with zero warnings/errors;
communication, service-lifetime and Overlay checks passed 46, and the six Overlay
JavaScript tests passed. No Launcher UI or retail game was started, no server files
were changed, and no release was published.

## Optional launcher service plugin activation (2026-10-16)

Added data-defined `kind = "service"` packages for the companion foundation and
the separate Overlay notification consumer. Both remain optional and disabled by
default. Provider/message declarations are validated, duplicate active providers
are rejected, and launcher-only service rows never enter the x86 hook session.
Existing library, import and options flows support the new kind; provider-specific
option types/ranges are checked before game start.

The Host now composes enabled services for a running game lifetime and exposes
credential-free `instances.services` diagnostics. Missing transport/Overlay or
server authorization records an unavailable dependency without enabling it or
blocking ordinary login. The production account factory remains absent until the
server launch-binding/connection-ticket contract is implemented. No local PID,
username or Overlay pipe is substituted for server-issued launch identity.

Validation: full managed regression passed 603 with five environment-gated skips;
subsequent service/communication/catalog checks passed 30, including actual service
composition over loopback WebSockets and dialog queued/mounted/action results plus
game-lifetime cleanup. Frontend type check and production build passed; plugin
frontend tests passed 41 and Overlay panel tests six. No Launcher UI, retail client
or server repository was opened or modified for this work. Nothing is published.

## Companion client transport and Overlay notification consumer (2026-10-16)

The independent Communication assembly now contains a provisional WSS/JSON-v1
client, launch identity negotiation, restricted disposable owner channels, bounded
send/receive queues, request correlation, heartbeat and epoch checks. Session
reconnect obtains a fresh grant, restores subscriptions and never replays old
requests. Authorization/protocol failures stop retries. A callback ignoring
cancellation retains cleanup ownership and prevents overlapping epochs.

The separate notification consumer uses only public channel/Overlay interfaces.
Structured text and buttons become local game-style dialogs, with distinct queued,
page-mounted and selected results; disconnect closes old dialogs and prevents
their result queue from crossing epochs. No network-supplied HTML/script executes.

Validation: solution Release build completed with zero warnings/errors. Full
managed regression passed 595 with five environment-gated skips; subsequent focused
communication/Overlay checks passed 31 after lifecycle hardening. The six JavaScript
panel tests passed. Communication tests use real WebSocket pairs over loopback TCP
and an Overlay SDK double, not a deployed server or retail game.

Open integration work: shared server wire fixtures and launch-binding/ticket
endpoints, catalog discovery, account
refresh, authorization leases/snapshots, native broker exposure, live WSS and retail
notification acceptance. The transport is not connected to normal game startup or
published. The server owner is temporarily unavailable; no server files were edited.

## Overlay foundation completion (2026-10-16)

The optional overlay foundation now keeps one page per game session, starts hidden,
and wakes panels only after explicit plugin commands. Hidden sessions stop the input
timer and attempt bounded WebView suspension; later commands resume the same page.
Game-style dialogs and custom HTML share deterministic layers, modal input, backdrop,
dragging, locale fallback, sound-cue placeholders and DPI/resolution-aware geometry.

Build profiles select v1/v2 themes, capabilities and hook/image/ui-resource target
identities. The SDK now includes a bounded adapter-normalized UI-resource parser and
reloadable anchor provider; private binary `.ui` decoding remains a build adapter
responsibility. Parent targets are validated for existence and cycles. The renderer
smoke test covers handshake, hidden preparation, idle suspension, two wakeups and
disposal without starting a game. Remaining UI-resource work is per-build decoding,
verified locators and native action adapters. The separate bidirectional server
companion channel is intentionally a subsequent foundation task.

Validation: Release solution and native builds completed without errors, managed
regression passed 576 with five environment-gated skips, Overlay-focused tests
passed 14, and JavaScript panel tests passed six. Native runtime, messenger, parent
and dump suites passed 119, 26, 153 and 35 checks respectively. A standalone
renderer smoke confirmed hidden preparation, real browser suspension, repeated
dialog wakeups and disposal; its receipt is under ignored `artifacts/`. No retail
game or Launcher UI was started for this batch. Changes are local and unpublished.

The separate communication design has been reviewed with the server owner: optional
WSS plus JSON v1, a public owner/launch-scoped SDK, independent of Overlay, with
server-issued launch binding and bounded reconnect semantics. See decision D29 and
`docs/hook/companion.md`. Implementation is the next independent batch; no server
files were modified here.

## CN legacy login and owned overlay tracking (2026-10-15)

Fresh-start follow-up: launcher-managed startup reached pipe connection, panel
registration and Vue mounting while the WPF window was hidden during the splash,
but the maintainer saw no panel after the render window appeared. Browser
controller creation is now deferred until that render window is visible and WPF
has arranged its final bounds; commands remain queued until explicit page-ready.
The maintainer confirmed the corrected renderer on the current CN2012 session,
then exited the game and started it again through the launcher: the overlay
appeared automatically. The new renderer log confirms browser creation after
render-window selection, followed by page-ready and component mounting. Overlay
and Host Release builds completed with zero warnings/errors; the local renderer
is deployed. No release was published.

Overlay follow-up acceptance: the maintainer confirmed that the current renderer
appears over the running CN2012 client, is covered by other foreground windows,
hides with game minimization and returns on restore. Coordinates use the overlay
DPI. Minimize transitions hide the browser host and WPF surface together; WPF
Topmost is changed only when foreground ownership changes, and background
z-order is repaired directly above the game. This was a standalone preview
attached to the existing client, not a fresh launcher-to-game startup acceptance.
The renderer is copied to the local launcher output; no release was published.

The shared login bridge now recognizes the earlier CN2011/CN2012 GameWidget
identifier while retaining its existing callback layout and marked ticket flow.
Native runtime version is 3.3.1. Explicit elevation is a build capability on
cn-279/cn-324, propagated through the module launch plan; unrelated clients and
controlled helpers no longer receive a forced runas request.

Overlay tracking rebinds replaced HWNDs, positions in physical client pixels,
and supplements window events with a disposable 100 ms observer. The maintainer
confirmed CN2012 login and overlay movement/minimization. After a reported
splash flash and subsequent disappearance, the renderer now selects the
CS_OWNDC graphical window, keeps its visual root dormant until that window is
visible, and retains the launch-bound PID independently of the initial HWND.
Foreground-aware topmost transitions and immediate-above-game z-order repair preserve rendering when
other applications gain focus without repeatedly raising the overlay. Desktop checks
confirmed the new preview above the current CN2012 main menu and unrelated
Explorer windows covering it. A complete fresh startup awaits maintainer
acceptance. Exact scene readiness remains open: the graphical window transition
does not prove every resource has loaded, and the host retains its initial
bounded window-stability wait.

Validation: native suites passed 119 runtime, 26 messenger, 153 parent, 35 dump
and 499 health checks. Host and overlay Release builds had zero warnings/errors.
The expanded eight-scenario bridge run had three passes and five canceled UAC
requests before the elevation correction; it is not a passing regression run.
No further helper runs were performed after the maintainer requested immediate
retail testing. Published authentication 1.0.0 payloads remain unchanged. These
changes are deployed locally, not published.

## Legacy 2019 builds and deferred native overlay (2026-10-15)

The launcher catalog now includes `cn-774` (China 2019) and `tw-454` (Taiwan
2019), using the external PE identity table already maintained for those builds.
The optional overlay receives a build-derived theme: builds through 2012 use
the v1 native layout and later builds use v2. Overlay creation waits for a
visible stable client area and a bounded four-second settle period; it no longer
depends on injected-hook readiness. Foreground child windows belonging to the
same game process keep it visible during legacy loading transitions. This is a
window-stability heuristic, not proof that the game has finished loading its 3D
scene.

The overlay page now exposes the restored game UI component library as an
optional `overlayBus.loadGameUi()` capability. It provides the selected theme,
Vue components, and `mountGameUi`; the foundation remains dormant until a
plugin explicitly mounts a panel. Incomplete instance directories without an
`instance.toml` are excluded from the instance list instead of poisoning the
whole page with a transient file-not-found error.

## Optional overlay panel transport (2026-10-14)

The optional x64 overlay now has a complete launcher-side session transport and
an inline Native plugin bridge:
the WPF process attaches to the game window, completes a launch-bound named-pipe
handshake, continuously handles register/unregister/show/hide/send commands,
and returns panel events. Frames and queues are bounded, the connecting process
is checked, and failures remain isolated from ordinary game startup. The SDK
contract and package validation are documented in `docs/hook/overlay.md`.

Native plugins receive the service through a versioned x86-to-launcher broker.
Inline HTML panels are available through the stable ABI tail extension; old
plugins remain compatible because the new pointer is optional and guarded by
`struct_size`. Package-relative file resources still need a launcher-side
resource broker before file-backed panels can be called complete. Native page
events remain a follow-up ABI extension.

## Launcher compatibility and server plugin policy (2026-10-14)

Launcher implementation complete, unreleased. Packaged application SemVer is shared by About, API version reporting and minimum-version comparisons; default source version is 0.1.14. API requests carry a version header and authentication/launch JSON fields. Optional catalog/login/refresh minimums and build-specific plugin recommendations are validated and merged. Required plugins are checked before tickets, after refresh and under the instance lease; server policy does not become a global persisted lock. Remote recommendations activate through configured stores with selection-safe durable sidecars, retaining exact schema-one history and old activation-file shapes. Valid rotated credentials survive rejected extension metadata. Outdated restored sessions can still discover routing and log out.

Plugin manifests can declare `minimum_launcher_version`; incompatible installed packages remain visible but cannot enable or resolve. Native parsing accepts and validates the optional field. Structured compatibility errors route to the shared update dialog; stale discovery callbacks cannot reopen dismissed dialogs or obscure nondismissible installation. PKCS 10.0.13 resolves restore-time dependency advisories without suppression.

Validation: Release zero warnings/errors; complete managed suite 554 passed / five environment-gated skips. Frontend update 30, plugins 33 and accounts 30 passed; typecheck and production build passed. Native runtime 110, messenger 26, parent 153 plus every child scenario, and dump writer 35 checks passed. No desktop launcher or retail game started. Existing update UI browser acceptance remains valid; real updater-helper application and live server-policy acceptance are outstanding. Server chats own Authentication WebUI/config implementation and have the contract; neither implementation nor deployment is claimed here.

## Fluent launcher update entry and separate installation consent (2026-10-13)

Completed the circular update entry above Settings and separate download/installation dialogs. Startup always checks after frontend health; the saved check preference controls additional thirty-minute checks. Available prompts show current and target versions. Download progress exposes speed, ETA and percentage; X, the background button, Escape and backdrop hide it without stopping transfer. Ready staging prompts separately before restarting. Installation progress cannot be dismissed; failed handoff restores dismissal. Manual discovery and live download restart actions use the same flow, including reopening previously canceled prompts. No download grants restart permission.

The sidebar shows a determinate byte-progress arc during transfer, including pause, with a faint complete track and a percentage in its tooltip/accessibility label. Verification and restart crossfade it to an indeterminate spinning ring. The same shared ring exposes determinate values to accessibility consumers.

Changed selections, cancellation and failure revoke the automatic handoff. Stale task snapshots cannot restart the application, and a failed handoff does not retry in a loop. The native restart bridge accepts an explicit selection hash plus permission to stop owned clients/hosting sessions. It verifies readiness before stopping anything and retains the normal restart gate during cleanup. The previous no-argument restart behavior still refuses running clients. Failed preparation reopens normal launching.

Validation: update frontend 24 passed; type check and production build passed. Browser preview verifies the separate prompts, download X/background action and determinate sidebar ring. The maintainer accepted the UI and requested real integration; external helper replacement with this new path remains a separate acceptance step. Startup regression covers a persisted disabled periodic preference and rediscovery after restart. Screenshots are under ignored `artifacts/update-ui-preview`. The new flow is not published.

## 0.1.13 UI copy and update-failure classification

Home retains its hidden local-save note as a template comment. Client and plugin descriptions are concise; the plugin update hint is removed without changing automatic selection. Six-language plugin metadata matches the requested titles/descriptions. Font 1.0.3 replaces retired bundled 1.0.2 with selection/options migration. Public font 1.0.3, ESC 0.1.4 and health 0.2.11 preserve historical immutable releases; health retains accepted 0.2.9 native bytes and operational definitions.

A discovery failure has no verified newer manifest identity and cannot open the automatic-update failure dialog. Network/integrity check failures use distinct localized notices in the global info bar and settings. Verified staging/startup failures retain manual download guidance. Successful discovery clears only its own error; version changes during link resolution suppress stale prompts.

Validation: update frontend 10 passed, plugin frontend 33 passed, type check/build passed, managed focused definitions/update tests 106 passed / one public-source skip; Release zero warnings/errors. No desktop launcher or game was started. Publication acceptance is recorded separately after readback.


## Official 0.1.12 x64 publication and old-reader acceptance (2026-10-11)

Source 66c1e4c is published as 0.1.12, revision 1791758218: 517 files / 226002414 bytes. Canonical and retained-preview schema-one feeds anonymously match, and the complete portable ZIP passes public SHA-256 verification. Unchanged 0.1.5-preview.1 assemblies automatically stage every file and restore ready state offline; no helper, desktop launcher or game is started. Real AList acceptance verifies latest CN authentication, automatic font 1.0.1-to-1.0.2 selection with options retained, and ESC 0.1.3 installation. Receipts are under `artifacts/launcher-0.1.12` and `artifacts/public-studio-acceptance`. Other-PC helper application and retail login using the new CN component remain separate acceptance steps.

The complete regression passed 524 / five skips. An additional run passed 523 / five skips with one native login-fixture process-completion timeout and a temporarily retained client lease. All four login-bridge scenarios subsequently passed in isolation; the trigger remains unproven. This evidence is not retail acceptance and did not justify an unproven production change.

## Required components and plugin download activation (2026-10-11)

Mandatory installation recommendations now remain checked and locked. Latest-only components bind one configured source, verify its catalog/manifest/local payloads and queue required upgrades before allowing launch. CN authentication enables this policy. Ordinary successful newer store downloads switch only an unchanged enabled target-client selection, preserving compatible options and pause/restart intent. Retired bundled store versions migrate missing selections without changing disabled state. Sign-in/recommendation checkboxes share animated WinUI styling. Team attribution is Fantasy Tale Studio; immutable historical releases are preserved through new font 1.0.2 and ESC 0.1.3 packages.

Validation: managed regression 524 passed / five environment skips; frontend plugins 33, accounts 30; publisher 56 tests; native runtime 108, messenger 26, parent 153 plus every child scenario and dump writer 35, all passed. Release build has zero warnings/errors. Plugin publication is verified; x64 launcher 0.1.12 publication is verified. No real launcher or retail game was started in this turn.

## File plugins and Chinese authentication component (included in 0.1.12)

Launcher-side `kind = "files"` packages now provide build-selected, hashed DLL/resource replacements, folder/ZIP/store import validation, a shared recoverable runtime overlay, collision rejection and exact post-exit restoration. Chinese builds default to bundled `bubblefighter.fantasy.cn-auth`: GameWidget for cn-279/cn-324, sdologinentry for cn-404/cn-406/cn-706/cn-717/cn-735, plus GPK. Existing clients receive the selection once; explicit disable choices persist and require a WinUI confirmation every launch. Confirmations are checked before credentials and under the client lease. File packages never enter hook sessions. Component ownership suppresses the old module overlay even when disabled. Future authentication hooks retain this identity, but this file-only contract activates no extra hook.

Managed regression: 510 passed / five environment skips. Frontend plugin tests: 31 passed. Native runtime: 108; messenger: 26; native parent suite: 153 plus all child scenarios; dump writer: 35, all passed. Release builds have zero warnings/errors. Controlled file-only processes verify replacement and exit restoration. Retail login using the newly packaged GPK stand-in remains unaccepted. This work is not in official 0.1.11.

## Official 0.1.11 publication (2026-10-11)

Source 6fb3b5a is published as win-x64 0.1.11, revision 1791740083: 514 files / 225741469 bytes. Canonical and retained preview schema-one indexes are identical and verified anonymously. The portable ZIP is 134302434 bytes. Unchanged 0.1.5-preview.1 assemblies discover/stage the release and restore readiness offline with zero explicit retries. Receipts are under `artifacts/launcher-0.1.11`. Other-PC helper installation remains a separate acceptance step. This release includes cn-404 and the confirmed-exit fix, not later file-plugin work.

## cn-404 launcher login acceptance (2026-10-11)

The CN2014 `0.2.46.001` client is now declared as wire build cn-404, replacing
its temporary resource-version identity in the shared identification table.
Its signed original and unsigned unpacked executable match only this build;
Version.dat independently confirms the release. The data-defined launch plan
uses the existing forwarded login bridge and server-selected endpoint, with
default port 7240. The deployed catalog confirms this build and required login.
Forty-one definition/corpus checks pass with no skips; Release builds with zero
warnings/errors. Authorized desktop registration detects the nested client
directory, preserves the installation and displays the KST signature date with
the retail version on the secondary line. The maintainer confirms login, and
a recovered game-window capture shows the existing character in the main menu.
The client already has maintainer-supplied GPK replacements; no GPK payload is
added to the launcher. After the game exited, original packed/unpacked EXEs,
messenger, login module and GPK hashes all matched; the session and runtime
journal were removed. This work was subsequently included in published 0.1.11.

## Official 0.1.10 publication (2026-10-11)

The affected-PC startup/exit correction is now published for win-x64. Canonical
and retained preview indexes anonymously match schema one, revision 1791737276,
513 files and 225740705 bytes. Publication verifies all 312 immutable objects;
300 reuse the previous complete verification receipt with fresh size/MD5 metadata,
and new content receives public SHA-256 verification. The 134301808-byte portable
archive also passes complete public SHA-256 verification. The unchanged
0.1.5-preview.1 reader automatically discovers and verifies all 513 files and
restores ready state offline, with zero explicit retries. This checker installs
no update and opens no launcher or game. The affected PC's separate diagnostic
startup/exit acceptance is retained below; the original failure trigger remains
unproven. Subsequent cn-404 work is not part of this release.

## Cross-PC startup and exit investigation (2026-10-11)

The maintainer reports an immediate shutting-down info bar and an ineffective
confirmed exit on another PC, without starting a game; the current PC opens
normally. The remote cause is not established. Confirmed exit now shares one
awaited operation and sends cleanup errors back to the requesting dialog.
Owned process cleanup keeps remaining services usable after failure; permanent
disposal follows native window closure. Startup requests normal foreground
activation and restores a minimized window without making it always on top.
Bounded local lifecycle records include registered method outcomes and window
events, excluding arguments, replies and exception messages. Five exit/privacy
tests and isolated previously failing renewal cases pass. Full regression then
passed 492 with five environment gates skipped; Release builds with zero
warnings/errors. The affected PC's supplied 0.1.9-diagnostics.1 lifecycle record
now confirms frontend readiness after approximately 5.7 seconds and complete
confirmed shutdown approximately 0.13 seconds after the exit request. It contains
no failed method outcomes or game launch requests, and the maintainer reports
normal behavior in this run. This accepts one diagnostic startup/exit cycle;
the original failure's trigger remains unproven. No retail game ran, and the
official 0.1.9 feed is unchanged by this investigation.

## Official 0.1.9 publication (2026-10-11)

The canonical and retained preview schema-one indexes anonymously match the
0.1.9 win-x64 release: revision 1791718598, 513 files and 225729301 selected bytes.
All 312 immutable objects were rechecked against the complete prior public
verification receipt and fresh provider metadata. Independent full public ZIP
verification passed before both feed writes. Mid-transfer provider HTTP 403 was
resolved by obtaining a fresh anonymous URL per bounded range; no credentials
were sent to provider downloads. The unchanged 0.1.5-preview.1 reader discovered
this release, automatically staged and verified all 513 files, and restored
ready state offline with zero explicit network retries. The checker installed
no update and opened no launcher or game; remote installation/startup health
remains user-controlled.

## Interrupted publication responses (2026-10-11)

An official release attempt encountered a chunked-response interruption after a
manual-archive upload timeout. The publisher now classifies this transfer failure
as transient under the existing four-attempt limit; logs retain only its type and
HTTP status, never temporary download URLs. Complete size and SHA-256 checks
still gate publication. Both eventual success and retry exhaustion are covered;
all 36 uploader tests pass. Remote release acceptance is still pending.

## Opt-in remembered passwords and rotation-safe logout (2026-10-11)

The shared six-language login form offers Remember password, unchecked by default.
Successful opt-in writes separate Windows-user DPAPI storage; failed login never
saves a candidate password. The exact server ID/API endpoint scopes restoration.
Logout retains remembered form credentials while revoking refresh; successful
opt-out forgets that account, and server edits/removal forget their passwords.
Form revisions prevent stale fills across typing, server/tab changes and unmount.
The publisher and release parser reject protected account files and temporary
copies. Actual DPAPI reopen, bounds, corruption, host-bridge restart and frontend
race checks cover the path; native desktop acceptance remains pending.

Logout now waits for in-flight rotation and revokes the latest credentials under
the same account gate. Four cases cover switching selection, cancellation and a
newer login; three reproduced the old race before the fix. No server changes,
password-based deployment requests or game starts were performed.
Release builds with zero warnings/errors; full regression: 465 passed/five gated
skips; frontend account cases: 30 passed, typecheck/build passed. A transient
Windows sharing conflict in the atomic native passport result probe is handled
by a bounded test-only retry; persistent sharing errors still fail. The 0.1.8
win-x64 candidate contains 512 files and passes detached self-contained updater
execution. Its distribution is prepared; public upload/old-reader acceptance
remain separate gates.

## Restored-account routing discovery (2026-10-11)

Restored refresh and refresh-only logout now establish the process-local server
routing policy through credential-free catalog discovery before sending their
credentials. Explicit server domain/IP requirements override local preferences.
Catalog rejection remains distinct from refresh rejection, retaining the saved
session for retry; explicit logout continues removing the local selection even
when remote revocation fails. Four new cases first failed against the previous
code and now pass; all 47 account/routing checks pass. A read-only deployed probe
received domain HTTP 403 then IP HTTP 200 and retrieved the catalog without a
bearer or password. Full managed regression: 454 passed/five gated skips;
Release build: zero warnings/errors. This change is not included in published 0.1.7 and does not
claim desktop or retail acceptance.

## Stable update discovery and manual fallback (2026-10-11)

The missing 0.1.5-preview.1 discovery entry is restored alongside the canonical
feed with identical verified 0.1.6 indexes. The unchanged older reader accepted
real automatic staging of all 511 selected files and offline ready restoration;
the maintainer confirmed discovery on the other PC. Packaging now separates
release/object paths from stable schema-one discovery and retains old feed aliases.
Future schema migrations require a dual reader delivered through the old format.
The retained old-reader acceptance tool is a release gate, not a current-reader
substitute, and does not claim desktop installation.

The 0.1.7 candidate supplies an independent manual-download definition and a
six-language animated failure dialog. Confirmed versions are shown when known;
failed discovery leaves the latest version unknown. Publication verifies the
generated portable ZIP before replacing either discovery index. Release labels
are also passed to the host build to keep About aligned with the executable.
Full managed: 450 passed/five gated skips; Release zero warnings/errors; frontend
failure cases: six passed; uploader: 34 passed; archive isolation: three passed.
Public 0.1.7 publication is accepted: canonical and original preview feeds have
identical index hash ae9dede4618f0570c34d7c63911f9b18cdd2ad95b10d2c6d8b316ab41c09cf41.
The 134279880-byte manual ZIP passed fresh anonymous SHA-256 verification.
The unchanged 0.1.5-preview.1 reader automatically staged all 512 files through
its original feed and restored ready offline, with zero explicit retries. An
earlier attempt failed during transport; do not imply uninterrupted availability.
Neither acceptance installed this release on a desktop. The retained checker
can explicitly retry at most three network failures and records that count;
integrity, disk and startup failures remain terminal.
No retail game was started in this acceptance.

## Access and refresh account sessions (2026-10-10)

The launcher now accepts the explicit access/refresh session envelope, renews a
selected account shortly before access expiry, serializes concurrent renewals,
rotates returned refresh credentials, and retries one request after an otherwise
unexpected access rejection. A rejected refresh removes only that server/account
session. Passwords and access tokens remain process-local; refresh tokens are
encrypted with the current Windows user's DPAPI key and restored after launcher
restart. Twenty-eight multi-account tests and two Windows protected-store tests cover rotation, concurrent renewal,
restart restoration, rejection, and the retry path. Deployed real-account API
acceptance now passes login/profile, separate-process restoration, rotation,
old-token rejection, refresh-only logout and six catalog-build ticket requests.
An injected access 401 successfully renews against the real endpoint and retries.
The deployed server was not restarted and no retail client was started in this
acceptance. Three controlled x86 process tests load the packaged messenger DLL
and verify its marked passport for kr-812, id-400 and id-1809 session identities;
they do not establish retail-game login for those builds. Full managed regression
passes 445 checks with five gated skips; native Release and frontend builds pass.

## Descriptor-driven native font and sprite layouts (2026-10-10)

Numeric-health number tables, draw/measure style, frame lists, sprite textures
and texture residency now consume five explicit descriptor fields. Early
CN2011/CN2012 use style 21, sprite word 2822 and member offsets 48/60/72;
CN2013 keeps that style/index with offsets 120/136/144. CN2014 and KR2015 use
style 22, word 3079 and offsets 120/136/144. These differences are established
by each build's consuming number and device-binding functions. Reusing KR style
22 in an early renderer would leave its frame pointer unset.

The parser and offline schema checker bound private tables to 4096 words, reject
invalid styles/indices and unaligned/overlapping member offsets. Seven controlled
resource fixtures exercise each measured row, ten-frame yellow digits, percent
artwork, correct style/maps, residency and white fallback on invalid frame owners.
Native Release|x86 builds with zero warnings/errors; 499 numeric-health checks
and all 22 verifier tests pass. CN rows remain research-only: string/options/
manager loading contracts, entry signatures and retail execution are unfinished.
No game was started and no public plugin release changed.

The measured resource path contracts are now data as well: the UTF-16 string
object records inline capacity seven, while the per-load options object is 32
bytes for CN2011, 40 bytes for CN2012 and 88 bytes for CN2013/KR2015. The native
loader allocates the configured size instead of assuming the KR buffer for every
row. This records the early ABI boundary without enabling those research rows;
their resource manager and callable entry contracts still need controlled
runtime acceptance.

## Legacy HUD owner and gauge-anchor evidence (2026-10-10)

Six CN2011--CN2014 descriptors now retain the exact argument preparation before
their floating HP call and explicitly select HUD-owner player capture. Gauge
consumers establish X/Y byte offsets 184/188 (CN2011), 224/228 (CN2012),
248/252 (CN2013 and early CN2014) and 276/280 (cn-706). The later CN2014
layout must not inherit the earlier anchor. Native research parsing validates
capture names and a bounded setup byte range ending at the HP call. The offline
tool checks those bytes independently of the HP target and floating consumer.

All six images pass 30 offline checks, with six runtime checks still deferred.
Numeric-health passes 237 checks; all 21 verifier tests pass. Native Release|x86
builds with zero warnings/errors. Early CN sprite frames/texture use offsets
48/52/60 rather than the active 120/124/136 layout; those resource ABIs still
need independent adaptation. No research row was enabled, no game was started
and no public plugin release was changed.

## Descriptor resource-manager address is consumed (2026-10-10)

Native numeric-health sprite loading now reads the resource-manager global from
`resource_manager_rva`; it previously retained a KR2015 constant despite the
external field. Controlled resource tests move the global to a different RVA and
require failed loads until the descriptor is updated, without calling the sprite
loader on an unreadable/null global or zero configured RVA. The existing percent
draw path still passes. Native Release|x86 builds with zero warnings/errors and
228 numeric-health checks pass. No game was started; CN resource layouts remain
unaccepted and public compatibility is unchanged.

## Descriptor-selected legacy HP call adapters (2026-10-10)

Supported numeric-health rows now explicitly choose `health_accessor_abi` and
`maximum_health_source`. Runtime supports thiscall floating HP and an EDI field
adapter preserving EDI/ST(0), plus either a verified virtual maximum accessor or
a safely read float maximum field. KR2015 still selects its existing thiscall and
virtual contract. Field offsets are bounded; conflicting maximum sources reject.
Unreadable fields, invalid maxima and invalid values retain only the original
Life draw. Descriptor parsing clears previous output/error before each request.

Release|x86 builds with zero warnings/errors. Numeric-health passes 215 checks,
including fractional EDI returns, repeated x87 calls, register preservation,
field/source rejection and original Life text. Hook (85), messenger (26), native
runtime (153) and dump writer (35) checks pass; all 19 Python verifier tests pass.
CN rows remain research-only pending player/anchor and resource contracts plus
retail acceptance. Neither a game nor a public store release was changed here.

## Separate legacy HP and Life evidence (2026-10-10)

Corrected six CN2011--CN2014 research rows: integer accessors divided by 20
belong to the original Life counter. Actual HP uses separate floating getters,
whose consuming x87 stores and exact call sites are now verified offline.
CN2011 supplies EDI = player + 372 and reads maximum HP as a float at player +
5184; the previously recorded virtual offset 544 belongs to another HUD path.
External fields and parser tests now distinguish HP, maximum HP and Life.
The verifier rejects Life calls substituted for HP and unrecognized float ABIs.
All six rows remain research-only, and public compatibility stays kr-812.
Native Release|x86 builds with zero warnings/errors; 182 numeric-health checks
and 19 verifier tests pass. Six actual images pass 24 offline checks with six
runtime checks deferred. No game was started and no store release was changed.

## Plugin defaults, version selection and native ticket identity (2026-10-10)

Numeric health 0.2.9 defaults to original Indonesian placement, yellow digits,
normalized HP and enabled percent artwork. Existing explicit overrides survive.
The maintainer confirmed both percent and placement on an x64 kr-812 retail
client using 0.2.9-rc.1. ARM64 rendering remains a separate unresolved comparison.
The immutable 0.2.9 package was published to the official AList catalog with all
five file hashes and the merged catalog verified; previous entries remain.

Store release choices sort newest first and default to the latest catalog release.
Choosing an installed plugin version now atomically replaces the one client-scoped
selection, retaining enabled state and shared settings. Disabled selections can
switch without the old disable guard rejecting a legitimate save; stale expected
versions still reject. Four real HTTP/AList file/ZIP backend scenarios and 22
frontend plugin/version cases pass. No game or desktop automation ran here.

Native NMCO passport replies now mark launch tickets with `bff-ticket:`, matching
the server's redemption contract and the existing GameWidget bridge. An unmarked
random ticket was interpreted as an account name, explaining repeated character
creation. Two distinct tickets are checked through the native reply codec without
logging secrets. This fixes the launcher path; recovery of the existing character
and deployed server redemption configuration still need real-account acceptance.

Release build: zero warnings/errors; full managed regression: 432 passed, five
gated skips. Native numeric rendering: 90 checks; native messenger: 26 checks.
Portable x64 `0.1.5-preview.2` contains the fixes, has 510 files and is copied to
`D:\Files\Downloads\FantasyTale-x64-test`. Launcher, runtime, Photino and updater
PE headers are AMD64; injected game DLLs remain x86. ZIP SHA-256:
`49ab8f2cc0cc21c283a07b578c185d0c5de53ff81b735f454e4c0ab37c22eef8`.
The preview launcher feed was not published. The already open desktop was not
restarted and may still use the previous UI/backend until manually reopened.

## Directory timeout presentation (2026-10-10)

Directory listing distinguishes a transport deadline from caller cancellation.
Only a timeout becomes the stable `directory_timeout` bridge error; the page
renders a retry message in all six UI languages and preserves other concrete
errors. A Vue setup regression checks loading cleanup, empty results and removal
of the old error after a successful refresh. All ten multiplayer frontend cases
pass; types/bundling and isolated/default Release builds pass with zero warnings
and errors. The default host exited normally, was rebuilt and reopened, and its
deployed JavaScript hash matches the current bundle. Desktop Home rendered
normally. No retail game ran. Production directory probes were intermittent:
the domain succeeded before a subsequent IP request timed out. This change does
not establish continuous endpoint availability or remote-room gameplay.

## Hook disconnect and explicit stop acceptance (2026-10-10)

Two new controlled x86 process cases verify that closing an authenticated hook
channel leaves the native client alive and able to receive loopback data, both
with diagnostics disabled and enabled. A later explicit stop uses the owned
process fallback, removes the session, restores the original messenger DLL and
releases the instance lease. Enabled receive traces/status remain available;
disabled diagnostics create neither trace nor dump. An ordinary forced stop does
not manufacture an exception dump.

Two additional Windows named-pipe cases close during connection/reply waits and
require cancellation/disposal/I/O failure within two seconds, rejecting an eventual
timeout as success. Subsequent requests reject the disposed channel. All 33 hook
channel/launch checks pass, including the existing nine compatibility-id controlled
crash/dump cases. Receipt: `artifacts/hook-disconnect-acceptance/hook-disconnect.trx`.
These are test clients, not retail game acceptance; no UI or production behavior
changed in this checkpoint.
Final full regression: 424 passed, five environment-gated skips; isolated solution
Release build has zero warnings/errors. All fixture processes exited.

## Desktop plugin delivery and localized task names (2026-10-10)

Actual Photino desktop installation from the configured official AList source
completed numeric health 0.2.7 (three files, 130325 bytes) and ESC 0.1.2 (one file,
3700 bytes). Both tasks reached completed/100%; installed file sizes/hashes and
source receipts match the saved manifests. Numeric color was changed to yellow,
saved and reopened, then restored to white while the plugin remained disabled.
The color flyout remained above the configuration dialog. Receipt:
`artifacts/desktop-plugin-acceptance/receipt.json`. No game or native plugin ran;
this does not accept desktop upgrades or confirmed ESC return.

Download snapshots now supply optional localized package names from the pinned
saved package for plugins and servers. Existing history is localized without
network refetch, rewriting records or changing task identity; user-named clients
retain their names. Downloads, accessibility labels and Home task choices use
the shared locale resolver. The plugin install dialog has its own six-language
title instead of the client install title. Four HTTP/AList file/ZIP bridge cases
reproduced the missing names before the fix and now pass for the initial response,
polling and restored legacy history. Related store/download checks pass 58 cases;
type checking/bundling and the default solution Release build pass with zero
warnings/errors. The default host was rebuilt and reopened; desktop observation
confirmed the two existing tasks display their Chinese names after restart.
Final full managed regression: 420 passed, five environment-gated skips.

## Directory join selection ownership (2026-10-10)

Joining captures the directory source, entry, game, build and selection revision.
Source/entry/build/tab/game changes, including away-and-back switches, and page
disposal invalidate delayed results before global selection or navigation. The
refreshed server collection must still contain the returned destination for the
captured game; vanished rooms and mismatched game replies show a localized error.
No matching client clears the previous selection. Stale failures are suppressed
without hiding current failures. Nine actual Vue setup cases pass, together with
ten hosting cases and frontend type checking. Release solution/default host builds
have zero warnings/errors.

The old default Launcher exited normally and the default host was rebuilt and
reopened with the current hosting bridge and frontend. Bundled JS/CSS hashes match
the latest Vite output. Desktop observation confirmed startup and the saved Bubble
Fighter server/catalog view. Directory requests still encounter the previously
diagnosed proxy HTTP 502; this does not claim remote-room acceptance. Further page
input stopped after user-input detection. No retail game ran.

## Exact invocation with a complete server package (2026-10-10)

Extended the opt-in complete-package integration to verify `hosting.destination`
against the first ready run, reject a stale identity, read 1..128 process log lines,
reject the stopped run, and accept only the replacement run after restart. All four
cases passed with the retained complete package: temporary storage, SQLite local
storage, HTTP installation and AList installation. The full run took 2m41s with no
skips. Receipt: `artifacts/alist-live-acceptance/exact-hosting-package-recheck.trx`.

The existing scenarios also verified two-account login/select/refresh/logout,
opaque launch tickets, directory join and invalidation, SQLite restart/package
replacement persistence, independent saves and released ports. Distribution cases
use a controlled HTTP/AList endpoint with the actual complete package; they do not
claim a new official-public-source check. No retail client or native plugin ran.

## Exact local hosting connection (2026-10-10)

The hosting page captures its profile/build and run id before asynchronous
connection work. A new `hosting.destination` bridge request verifies that exact
ready owned invocation; stopped, faulted and replaced invocations cannot supply a
destination. Selection changes and disposal suppress later global selection and
navigation. Missing compatible clients clear prior client selection. Refresh and
log responses are revision-bound; stale failures are discarded without hiding a
current failure. Ten actual Vue setup cases pass; the first five failed before
the changes. Native gameplay and remote-room acceptance remain separate.

Final Release solution build has zero warnings/errors. Full managed regression
passed 420 cases with five environment-gated skips, including the extended hosting
bridge case: no destination before startup, exact ready run accepted, wrong/missing
run id rejected and stopped run rejected. Account/plugin/navigation suites passed
18/13/6. This checkpoint is in isolated validation output; the already-running
default host still has the prior loaded version.

## Account page asynchronous lifecycle (2026-10-10)

Account addition now retains its selected game/server context across the render
wait, including an away-and-back switch. Page disposal invalidates pending opens
and prevents completed selection/profile operations from scheduling more page
loads. Logout completion closes only its original confirmation. Five actual Vue
setup regressions failed against the prior page; all eight page cases now pass,
including normal add/select/logout paths. The standard account suite includes
them and passes 18 cases; plugin/navigation suites pass 13/6. Type checking and
production bundling pass; isolated solution and default host Release builds have
zero warnings/errors.

The default host was rebuilt while no Launcher process was running and launched
with the current UI and plugin cache fix. Desktop observation verified server
account groups and the filter flyout. Selecting a filter was interrupted by user
input detection, so further input stopped. Existing sessions were empty. This
does not prove multiple real account login, selection/logout or retail gameplay.

## Scope-bound foreground plugin discovery (2026-10-10)

Update snapshot reads now compare the client's current game/build with the cached
scope and discard stale or removed-client results immediately. Foreground discovery
records its original scope and refuses delayed results after a build change or
removal. A later network failure in the same scope preserves the confirmed manual
result and its timestamp. Two new regressions failed against the prior code; all
ten monitor cases pass after the fix, including a late foreground completion case.
Release solution build has zero warnings/errors. No native plugin or game ran.

Combined monitor, HTTP/AList upgrade and update-record checks passed 22 cases.
The first full run had a transient-lock fixture failure: its asynchronous release
continuation could run beyond the production 200 ms retry window under load.
The fixture now uses a dedicated reader and releases the lock on its owning thread;
production retry limits are unchanged. The final full regression passed 420 cases
with five environment-gated skips. Frontend plugin/account/navigation checks passed
13/10/6 cases. These checks do not prove desktop installation or retail execution.

## Game-scoped navigation (2026-10-10)

Completed: all non-Bubble-Fighter games show only Home, Downloads and Accounts.
Each `game.toml` declares `game.navigation_entries`, validated by the catalog and
provided through the bridge. These allowlists also enforce game switches, bookmarks and
history navigation, replacing unavailable pages with Home. Keyed pane transitions
fade and slide entries while retained buttons move; selection is remeasured after
list changes and animation completion. Six actual Vue setup regression cases
passed, along with frontend type checking and production bundling. No game ran.

Desktop acceptance: the stale desktop process was normally exited, the default
Release host rebuilt and relaunched, and the maintainer confirmed menu switching
succeeded. Home shortcuts now share the declaration, and a server's plugin
restriction cannot hide a declared plugin library entry. Those refinements passed
frontend checks and are in the default static output; an already-loaded web view
needs a reload/restart to read them. No retail plugin execution is implied.

## Desktop plugin library without a client (2026-10-10)

The running desktop host was switched to Air Rider with no registered client.
Opening Plugins still displayed all four bundled packages. Download, update
check, import, activation and options controls were disabled; clicking diagnostic
options did not open an editor. Returning to the original game was interrupted
by detected user input, after which capture returned a Codex surface; further
desktop input stopped. This verifies browsing and disabled configuration, not
installation, authentication or game execution. Air Rider was the last observed
game selection. `npm run test:accounts` now includes the existing UI race tests;
all ten account cases and frontend type checking passed.

## Installed client corpus recheck (2026-10-10)

With `FT_TEST_CLIENT_CORPUS=<client-corpus>`, the five identity cases passed:
cn-406, id-400, id-1809, kr-812 and kr-1256. The bridge recognized both retail
and unpacked executable candidates, registration rejected an occupied folder
unless overwrite was explicit, and SHA-256 checks confirmed that no executable
was modified. This is file and bridge verification only; no game process ran.

## Stopped invocation during room renewal (2026-10-10)

Heartbeat completion rechecks the exact owned invocation before marking a room
published. A stopped process triggers withdrawal after either a valid renewal
or a malformed response, without waiting for the next heartbeat tick or retry.
Withdrawal reports withdrawing while its HTTP request is in flight. Two controlled
tests stop a real owned fixture process during a delayed heartbeat, hold the
withdrawal response to inspect status and verify no further renewals occur.
Full .NET regression passed 412 cases with five environment-gated skips; the
isolated Release build completed with zero warnings and zero errors.
These lifecycle tests do not launch a retail game or prove remote reachability.

## Room lease malformed-response isolation (2026-10-10)

Invalid directory JSON and lease validation failures now stay within the affected
publication instead of faulting the shared heartbeat loop or shutdown. Renewal
reports reconnecting and withdrawal reports withdrawing, using bounded diagnostics
without management tokens. Rejected replies never extend the last confirmed expiry.
Two-room process fixtures cover stalled and malformed renewal alongside healthy
renewal and withdrawal, plus unavailable/malformed shutdown responses with one
shared deadline. Hosting/directory regression passed 35 cases without skips.
Full .NET regression passed 410 cases with five environment-gated skips; the
isolated Release build completed with zero warnings and zero errors.
Use `--artifacts-path artifacts/core-validation` while a desktop host holds the
default output DLLs; rebuilding that live directory produces Windows file locks.

## Plugin page asynchronous context ownership (2026-10-10)

Client/server changes synchronously invalidate update checks, configuration-page
reads and editor confirmations. A delayed check no longer clears the loading
state of a newer check; old errors and page HTML cannot affect another context.
Save and activation completions only close the dialog that started them. A
download remains in the global queue after switching clients without attaching
its progress to another client's plugin card. Unmount invalidates pending work.
Nine delayed-response cases execute the actual Vue page setup; together with
four library cases, all 13 pass. Type checking and production frontend build pass.
These controlled tests do not replace retail plugin/game acceptance.

## Desktop client registration and diagnostic options (2026-10-10)

Fresh screenshot-backed input targeted the actual Launcher window successfully.
Adding `<client-corpus>/BFLegacy_KR2015` automatically selected kr-812 and
the default Korean 2015-04-22 name. The version flyout appeared above the modal,
and confirming use of the existing directory registered the client. Plugin
management then enabled applicable actions and marked unsupported packages.
The official store displayed numeric-health 0.2.7 and ESC exit 0.1.2 with kr-812
compatibility. No plugin installation or gameplay acceptance is claimed here.

The diagnostic form saved `max_records = 16` while retaining disabled activation.
After exiting the old inspection build and opening the current Release host,
the client and saved value survived. Desktop inspection exposed untranslated
diagnostic fields; the manifest now supplies descriptions and all eight labels
in six locales. The rebuilt form visibly uses Chinese for every label. Definition,
plugin and configuration tests passed 47 cases with one remote-source skip.

## Exact rollback snapshots (2026-10-10)

Patch application validates every recorded original before the first write and
uses that exact snapshot for the write check, including manifest wildcard bytes.
A controlled guard-page observer changes a wildcard byte between journal reads;
the write is rejected and the newer value survives (seven checks passed).
Rollback tests separately force a later non-executable write failure after the
first write and confirm restoration, while an already-present first patch stays
installed. Native Release|x86 builds successfully; the native activation driver
passes 121 parent checks and all isolated children, runtime 85 and messenger 18.
After packaging current binaries, all 27 managed Hook launch cases pass.
Journal restoration failure now reports `rollback-failed` and blocks readiness
with `patch-restoration` even for optional plugins. Later plugins are not loaded
after that failure. A controlled IAT protection failure verifies the actual
rollback path in an isolated process (nine checks). Native activation driver:
153 parent checks and all children passed; the other four native suites passed.
Low-level write helpers now propagate incomplete byte/cache/protection recovery
through the same failure path. IAT recovery only undoes an exchange made by the
current call; an already-present pointer stays installed after protection failure.
Isolated protection and cache failure scenarios cover transient recovery,
persistent failure, preexisting IAT replacement and stopping later plugin loads.
Managed Release build passed with zero warnings/errors; full regression passed
408 cases with five environment-gated skips and no failures. Four new reader
cases cover optional rollback failure and reject inconsistent readiness reports.
The opt-in installed-client corpus suite also passes five cases against
`<client-corpus>`: cn-406, id-400, id-1809, kr-812 and kr-1256. Retail/unpacked
identity and inspection/registration through the host bridge pass, with executable
hashes unchanged. This is not desktop interaction or gameplay acceptance.

## Hook patch isolation (2026-10-10)

The Native Hook session now tracks the address range of every successfully
applied patch. A later plugin whose range overlaps an earlier plugin is rejected
before it writes, with both plugin and patch ids in the readiness detail. This
prevents a later plugin from silently overwriting an earlier plugin. The native
fixture suite covers full, partial and identical overlap rejection, adjacent
writes and continued independent-plugin application (four cases, eight checks
each). The native activation driver passes 117 parent checks and its child cases. Native
Release|x86 build passed with zero warnings/errors using the installed compiler
at `<VisualStudio>/MSBuild/Current/Bin/MSBuild.exe`.
Runtime, messenger, native activation driver, numeric-health and dump writer
suites all passed. After packaging current native binaries, managed regression
passed 404 cases with five environment-gated skips and no failures.

## Continuation checkpoint (2026-10-10)

Delayed account authentication now preserves a newer current-account selection.
A successful additional login remains in the server's account list without
selecting itself after another selection revision. Same-account responses cannot
replace a session refreshed or removed while authentication was pending. Two
new cases verify delayed addition and reauthentication; all 20 account cases
pass, including ticket identity after the preserved selection.
Release build: zero warnings/errors. Full regression: 404 passed, five
environment-gated skips, zero failures. No desktop launcher or retail game
was started.

Joined-room lease expiry now removes its transient account scope and capacity
when listing, joining or resolving instead of waiting for passive directory
requests. Resolving an already expired room fails before network access; stale
membership responses are also checked against the connection owner's clock.
Fifteen directory tests pass, including expiry, exact-once invalidation and
reclaiming a full 32-room set before another join. No retail game was launched.
Release solution build has zero warnings/errors. Full regression: 402 passed,
five environment-gated skips, zero failures.

Plugin update presentation and activation now use game/id/version identity.
Other games' cards cannot inherit the selected client's update hint, and a
same-ID release in another game's library cannot win activation lookup.
Download UI tracking keys now include the game. Four frontend plugin tests and
six account tests pass; strict type checking passes. Paired plugin guides also
correct the stale claim that switching versions always resets options.
Production frontend compilation and the Release solution build pass with zero
managed warnings/errors. No desktop launcher or retail client was started.

Hook IPC now retains a valid connection after a framed status/not-ready response
with an empty body. The caller receives an explicit retryable state; later ping
and stop requests keep their sequence and connection. Invalid response identities
still close the channel. Two controlled Windows pipe cases verify these paths
without launching a retail client.

The next full run exposed a sharing conflict opening an atomically replaced
updater outcome. Record reads now retry only Windows sharing/lock violations
for up to 200 ms. Dedicated transient/persistent lock cases preserve loud failure
for ongoing contention; the external helper scenarios pass their focused rerun.

Diagnostic rotation now recognizes the native dump writer's
exception.dmp.status.txt. Previously a retained crash status could block a later
launch when old runs reached the rotation limit. Two rotation tests verify the
known file is removed with its owned run and unknown files still stop rotation
without deletion.
Final Release build: zero warnings/errors. Complete regression: 401 passed,
five environment-gated skips, zero failures. No retail game or desktop launcher
was started during this checkpoint.

Plugin background discovery isolates failures by game/build scope. A failed
scope retains its last successful background result and timestamp with a safe
error category; later scopes still refresh. Cached results are retained only
for the same scope, so changing a client's build cannot reuse an old package
list after a failed request. Recovery clears the scope error. Seven monitor
tests pass, including mixed failure/success and build-change coverage. Release
solution build has zero warnings/errors. No game was launched.
Complete regression exposed an updater status-publication race: the parent had
exited but outcome remained installing, and the Windows crash record proved an
access-denied exception during outcome replacement. Record readers now allow
delete sharing, and existing records use atomic replacement rather than
overwrite-move. A deterministic held-reader test proves the old snapshot stays
readable while the helper publishes the new complete record; overwrite-move
fails that control. External helper tests poll through the shared typed reader
without increasing their deadlines. Final complete regression: 393 passed,
five environment-gated skips, zero failures; Release build has zero warnings
and errors. No desktop launcher or retail game was started.

Configuration writes now use an expected snapshot after asynchronous server
catalog discovery. A stale client or plugin settings request fails explicitly
instead of overwriting a newer save. Two bridge regressions cover client and
plugin settings races; the newer configuration remains intact and a retry
succeeds.

Frontend account displays now expire against their server-provided timestamps
without waiting for another bridge call. Home and the account page clear an
expired selection without choosing another account, prune at response receipt
and recheck when the window regains focus. Timers stop on empty lists; expiry
waits are capped at one minute to re-evaluate clock changes. Three new frontend
checks cover boundary expiry, invalid/missing sessions, retained selection and
bounded waits. All six account frontend checks, strict type checking, production
compilation and Release build pass (zero managed warnings/errors). Desktop
interaction acceptance remains separate; no game was launched.

Room publications now track one independent background operation per room, with
four transport slots shared across ticks. A stalled renewal no longer blocks
other rooms' later renewals or process-exit withdrawal. The same-room gate fences
explicit withdrawal against renewal; shutdown cancels and drains workers before
its existing bounded final withdrawal. Replaced terminal publications release
their gates after prior operations finish and stale tick snapshots cannot restart
them. A controlled two-process regression holds the first heartbeat, requires
two healthy peer renewals, stops the peer, checks timely withdrawal and verifies
shutdown cancellation. The earlier serial implementation fails its initial
renewal control. All 18 local-hosting tests pass without retail game execution.
Complete Release build: zero warnings/errors. Full regression: 391 passed,
five environment-gated skips, zero failures.

Four real process-kill tests now interrupt installation after owned-file removal,
replacement, addition and durable commit. The external offline helper restores
uncommitted changes, retains committed ones, preserves user files and ownership
receipts, and accepts repeated repair. Tests deploy the updater from the current
build output, removing a stale default-Release dependency. These checkpoints
cover process interruption between file operations, not every power-loss window.

The pending-launch cleanup failure reproduced with exitCode=-1 and an enumerated
process whose HasExited was true; startup image or dependency metadata remained
locked. Fixture deletion now retries Windows access/sharing errors for at most
two seconds only after observed exit, never for a live process or read-only image.
Two deterministic cases check transient release and persistent-lock failure;
all existing session, shutdown and lease assertions remain unchanged. Complete
Release regression: 390 passed, five environment-gated skips, no failures.

A subsequent complete regression passes 384 tests with five environment-gated
skips. The intermittent pending-launch executable deletion failure did not
reproduce and remains unresolved. Its failure now records the owned PID,
observed exit code, remaining process presence, file attributes and temporary
root without retrying deletion or relaxing lifecycle assertions. A corpus run
also accepts all five registration families (ten retail/unpacked executables)
through the bridge and verifies their bytes remain unchanged; no game executes.
Anonymous live AList acceptance installs numeric-health 0.2.7 (three verified
files) and ESC early-exit 0.1.2 (one verified file), validating source receipts,
repeat installation, white/yellow selection and the all-modes option without
loading native code. The focused run passes all nine cases.

The remaining legacy no-client copy now describes an empty client list rather
than implying that application settings or plugin browsing require a client.
Its only current consumer is Settings > Client directories; the plugin page
already uses its independent library and action-availability messages.

Server-session capacity now reclaims empty groups left by failed authentication
or expired credentials before allocating another group. In-flight logins retain
their exact owner until completion, so cleanup cannot fence unrelated successful
authentication. Two tests fill 128 failed/expired groups, begin a held login,
authenticate another server and verify both fresh sessions survive. All 18 account
tests pass. API policy caching retains its separate existing bound.
Release build has zero warnings/errors. Full regression: 383 passed, five gated
skips, one failure deleting the pending-launch fixture executable after exit
(`PendingLaunchTests.ExitRequestedDuringPreparationStopsTheSubsequentlyStartedProcessAndCleansItsLease`).
The recurring file-release failure remains unresolved; this is not a green full run.

Five new account-ticket race cases hold an in-flight launch response while its
session expires, logs out, reauthenticates under the same username, is removed,
or changes API endpoint. All reject the stale ticket even when the response
contains a currently valid ticket expiry. Unrelated server accounts and renewed
same-name sessions remain intact. All 16 account tests pass; this is managed API
validation without client execution, not desktop or retail login acceptance.

The account page now loads server groups through four concurrent bridge workers,
instead of opening up to 128 requests against the bridge's 32-request limit.
Superseded refreshes and unmounted pages stop queuing the remaining groups;
existing requests drain without publishing stale results. Three frontend checks
cover 128 servers, failed requests and superseded batches. Frontend type checking
and production compilation pass. No retail game was started; current maintainer
instructions require fresh permission before any game test.

Plugin browsing no longer requires an installed client. The bridge exposes all
game libraries with optional client context; the UI groups releases into one
card per game/plugin and retains a release selector for rollback. Compatibility
is evaluated only for activation, not library visibility. Download/import and
client configuration remain disabled without a target. A two-build bridge test
verifies a shared package before and after registration; three frontend grouping
checks cover empty-client browsing, selected versions, rollback and game isolation.
Frontend types/build and Release compilation pass without warnings. The full
managed rerun passes 377 tests with five gated skips. An earlier run hit test
process file-release and helper deadline failures; the pending-launch case and
the subsequent complete run passed without changing their implementations.
Desktop visual acceptance remains pending because capture is black.

The plugin switch regression now reloads persisted client selections and installed
packages, resolves the launch entries and parses the generated native session.
Retained yellow, incompatible-switch rejection, explicit white defaults, rollback
to an older installed version, disable and re-enable all preserve the expected
version/path/options contract. Removed options never reach the session. This
verifies the bridge-to-session boundary without executing patches; retail drawing
and ESC return still require their separate runtime acceptance.
Release validation passes with zero warnings/errors; the full managed suite
passes 376 tests with five environment-gated skips.

Plugin activation now preserves shared per-client options when configuration is
omitted, dropping only keys removed by the target schema. Incompatible retained
values reject the switch without changing the old selection. The confirmation
has a default-enabled Keep shared settings toggle in all six locales; disabling
it explicitly submits empty overrides to restore defaults. A bridge regression
imports three versions and verifies retained color, removed keys, incompatible
schema rejection, old selection preservation and explicit reset. HTTP/AList
folder/ZIP upgrade checks also pass; retail activation remains unaccepted.

Room shutdown now shares one ten-second withdrawal deadline with bounded request
concurrency instead of accumulating a separate timeout for each publication.
A regression starts two controlled server processes, publishes both rooms, stalls
directory DELETE responses and requires disposal before fifteen seconds while
retaining unconfirmed withdrawal state. Normal lease renewal and automatic
withdrawal after an owned server exit also pass. This verifies controlled hosting
lifecycle behavior; it does not establish retail gameplay or remote NAT traversal.

New-client naming no longer pre-fills a server preset's name. Empty input uses the
currently selected build label, so automatic executable recognition also updates
the default client name. Explicit custom names and edit/clone naming are retained.
Desktop retesting is pending: window capture returned black frames with no
accessibility content, so blind input was stopped. After the user's requested
restart, the user confirmed the Home renders normally and only capture is black.
That observation is not evidence of a launcher renderer failure.

The installed corpus bridge acceptance now also exercises actual registration:
each of the five families rejects an unconfirmed occupied destination, accepts
explicit confirmation, and appears in `instances.list` with the correct wire ID,
external path and `clientInstalled=true`. Before/after SHA-256 checks verify that
both retail and unpacked executable bytes remain unchanged. These hashes verify
test non-mutation only; executable identity still uses signature/PE evidence.
Registrations are isolated in temporary launcher metadata and removed afterward.

Real installed identity acceptance: the opt-in `ClientCorpusTests` suite passed
five installed families (cn-406, id-400, id-1809, kr-812 and kr-1256). Each case
reads both retail and unpacked executable metadata and requires exactly one
matching catalog build, then sends the real installation folder through the
game-aware host bridge and requires the same unique build ID. No executable is
run or copied, and no client directory is modified. This verifies inspection
against ten real binaries, not registration, plugin injection or gameplay.
Enable with `FT_TEST_CLIENT_CORPUS` pointing to the local corpus root.

Desktop inspection found that `instances.inspectInstallation` consumed the optional
`game` argument but omitted it from the bridge whitelist. The real Add Client
dialog therefore returned `Unknown parameter` instead of recognizing CN2013.
The whitelist now accepts `game`; a bridge regression covers game-aware and
legacy calls while preserving rejection of unknown fields. Modal version menus
were observed above the dialog, with localized KST dates and region groups.
Actual post-fix client recognition and registration still require desktop retest.

Multi-account capacity now prunes expired sessions across all server groups before
enforcing the global account limit. An isolated injected-clock regression fills
all 128 slots on eight servers, expires them without visiting their account lists,
and verifies that a new server can authenticate rather than reporting a false
capacity failure. Access tokens remain process-local; protected refresh-session
restoration is now implemented and desktop acceptance remains a separate scope.

Client identity correction: external evidence now uses the established wire IDs
`kr-1256`, `tw-454`, `cn-717`, `cn-735`, `cn-738`, `cn-742` and `cn-774`
instead of folder/year/resource labels. Three regression cases verify that signed
metadata actually attaches to existing Korean and Chinese definitions, including
KST dates and executable aliases. Numeric ID syntax alone cannot prove a wire
version. Remaining resource-labelled evidence rows still require corpus research
before being assigned wire IDs or offered as supported builds. Release signing
remains excluded in accordance with the maintainer's explicit decision.

The current acceptance boundaries and next verification commands are in
[maintainer-handoff.md](maintainer-handoff.md). Older time-window prohibitions
below are historical: the maintainer subsequently authorized desktop/game tests.
Official AList launcher publication and independent Photino desktop replacement have
passed. Anonymous public plugin/server-package delivery has also passed independently. Do not equate those scopes or reintroduce
the rejected signing requirement. The core handoff goal remains active.

## Public metadata transport retries (2026-10-10)

The shared public catalog/manifest reader now retries transient GET transport and
gateway failures up to three times, re-resolving download metadata on each attempt.
All attempts retain one thirty-second deadline; permanent access failures and
metadata size mismatches are not retried. Exact returned byte counts must match
known source sizes. API credentials remain confined to metadata resolution.
Eight new cases cover fresh URLs, credential isolation, bounded retries,
cancellation and size mismatch; the full suite passes 367 tests with four gated
skips and Release builds with zero warnings/errors. Validation uses isolated
outputs while the earlier x64 official-source staging process remains live.

## Official launcher publication (2026-10-10)

The single-worker direct upload completed all 307 object checks, published the
launcher feed last and exited zero with `Published verified public catalog`.
Anonymous re-download confirmed the 132287-byte index exactly matches the local
candidate, SHA-256 `03f1fa975dc4b2c2ddf24310ea755842e72d0ffc8c8dd8c9548333b1aa0f5d12`.
Revision 1791562273 contains 506 files and 224702406 indexed bytes.
Receipt: `artifacts/official-launcher-update-20261010-r2/publication-receipt.json`.

The first remote acceptance run correctly rejected win-x64 under this machine's
default ARM64 test runtime. Run this distribution's acceptance with `--arch x64`;
do not weaken release/platform validation. The x64 run completed actual remote staging and offline restoration in 11m15s: 506
files and 224702406 bytes were verified, then the service restored ready with network
unavailable. Receipt: `artifacts/alist-live-acceptance/public-launcher-x64-receipt.json`.
The independent Photino run downloaded the official feed, displayed total/file progress,
restarted through the external updater and produced `outcome=completed`; all 506 installed
hashes match. Receipt: `artifacts/desktop-official-acceptance/official-desktop-receipt.json`.

## Background plugin discovery (2026-10-10)

Plugin discovery now runs after frontend readiness and every thirty minutes,
controlled by an independent persisted update preference. It only checks original
sources; installation and client activation remain explicit. Same-game/build
clients share results, and the plugin page polls cached snapshots without network
discovery. Each pass has client/version limits and a two-minute cancellation budget.
Foreground results fence older background passes; changed/deleted clients cannot
receive stale results. Shutdown drains the monitor before the discovery service.
Manual operations serialize with background discovery rather than failing busy.

Six monitor/preferences tests and four HTTP/AList folder/ZIP bridge cases cover
startup enablement, shared results, cancellation, source checks and pinned selections.
The full managed suite passes 359 cases with four environment-gated skips.
Frontend type checking and production build pass; light/dark page checks verify
cached discovery without manual checks, explicit activation/cancel and bounded layout.
The official candidate already uploading predates this feature; it is not silently
modified during publication.

## Publication verification bounds (2026-10-10)

Anonymous uploader verification validates advertised sizes before requesting raw
bytes, rejects excess bytes immediately and refuses truncated streams. A configurable
per-object streaming budget prevents indefinite trickling; blocked reads retain
their separate timeout. Defaults allow 900 seconds and accept 30–3600 seconds.
Six added tests cover size validation, early overrun, truncation, total budget,
URL-safe timeout errors and exact hashing. All 36 publication-tool tests pass.
The already-running single-worker upload keeps its original code; this change
does not restart it or claim remote publication success.

## Current maintainer steering: KR 2015 optional gameplay plugins

Investigate numeric player health and in-match Escape-to-lobby as two
optional plugins for kr-812. The maintainer authorized desktop/game testing
only during 12:00–13:30 Asia/Shanghai on 2026-10-09. Static resource extraction
confirmed byte-identical quit-dialog definitions in kr-812/id-1809;
resource and callback investigation continues in
[the evidence note](hook/kr812-ui-plugin-research.md). No plugin behavior
or full retail acceptance is claimed by this investigation. The exit dialog
appeared in retail testing; confirmation through the inactive relay path did
not exit. Version 0.1.1 sent the original game leave request, received replies,
then crashed in the game-stage renderer. Version 0.1.2 withdraws that fallback;
ordinary exit without relay remains unfinished. Desktop testing is prohibited
again after the rest window. See the evidence note for the lifecycle gate.

Numeric-health native sample is implemented separately under
`native/samples/bubblefighter.fantasy.numeric-health`, limited to kr-812.
Release|Win32 builds warning-free; 31 synthetic checks cover all seven original
arguments through the actual x86 stub, native-style current/max normalization, raw HP and
invalid player/value suppression. Managed manifest/default/position-bound
verification passes. Retail loading and visible normalized full health (100) pass
after correcting the packed PE size and capturing the selected player from EDI.
The maintainer also verified that self-inflicted damage updates the displayed value.
It remains unbundled; respawn, lifecycle, mode behavior and complete plugin
combinations remain open. The maintainer authorized renewed desktop/game testing
after work on 2026-10-09; safe ESC 0.1.2 and numeric health load together.

Yellow numeric-health styling now follows the evidenced HUD formula and loads
the existing ten-frame font through validated target-native functions. Private
font tables preserve shared renderer state; invalid frames retain white fallback.
Synthetic resource lifetime, mapping, rendering and fingerprint rejection checks
pass. Version 0.2.0 styling remains pending retail acceptance; the running
0.1.x test is not silently replaced.

This file is the single source of truth for progress. Any agent or contributor
picks the first `todo` card whose dependencies are `done`, sets it to `doing`
with their name, and sets it to `done` with the commit id when its acceptance
criteria pass. Keep cards small; split a card instead of half-finishing it.

Status: `todo` / `doing (who)` / `done (commit)` / `blocked (reason)`.

## Where things stand (update when you stop)

Plugin configuration is available through the shared form, with optional package
HTML pages declared by `plugin-ui.toml`. The published KR2015 packages are numeric
health 0.2.7 (white default, optional experimental yellow) and ESC exit 0.1.2.
Anonymous real-source discovery, installation and option persistence were verified;
other builds, immediate health visibility and full ESC return remain unaccepted.

The configuration authoring example is documentation-only, not bundled into every
plugin. Integration checks now cover custom-page import/readback, configuring before
activation, independent client selections, invalid-key/value refusal, persistence
after backend restart and refusal of page edits after import preview. These checks
start no retail game or launcher window. Framework-dependent win-x64 desktop
self-update replacement and rollback are accepted in the loopback fixture;
public AList feed acceptance remains open.

Server-directed client downloads now accept an optional preset downloadSource
descriptor for HTTP or AList. Discovery is read-only; Add client presents the
package and source address, and explicit installation rechecks source and manifest
identity before using the shared queue. A failed start retains the created client
for reviewed retry. Sources remain bound to installation receipts for updates and
restart recovery; disabled or retargeted sources are not silently restored.
Four actual HTTP/AList bridge tests cover installation, optional-version file
updates, recovery and incompatible or changed recommendations. The full suite
passes 318 tests with two opt-in cases skipped; frontend types/build pass.
Desktop rendering and real client-download acceptance remain open while desktop
testing is prohibited.

Latest source reliability increment: AList file resolution now retries bounded
transient HTTP/provider failures and transport timeouts, without retrying caller
cancellation, authentication rejection or malformed metadata. Eleven targeted
headless tests pass. The separate public-source full-package acceptance now
passes as recorded below; the retry tests alone do not establish installation.

Verified staged content now avoids repeated source resolution and transfers for
identical files and resumed tasks, after a fresh size/SHA-256 check. Source
removal, disablement and retargeting remain installation fences. The public
AList catalog contains the published d66cc86a server distribution; complete
remote installation and backend lifecycle acceptance passed on 2026-10-09.
Validation: the full managed solution builds with zero warnings and zero errors;
314 tests pass with two external-package tests skipped. Coverage includes
same-size staged corruption, shared content and cached-file source-policy fences.
All four opt-in real-package cases also pass against d66cc86a after this change:
temporary storage, SQLite restart, and full HTTP/AList-fixture provisioning.
The fixture checks every installed file against the complete distribution and
counts AList resolutions by unique content objects, plus catalog and manifest.
The separate public-source test also passed in 21 minutes 34 seconds against
the pre-deduplication binary; current deduplication is covered by the four
complete-package cases above, including the AList fixture.

Last update: 2026-10-09 (Asia/Shanghai). See [the historical continuation handoff](maintainer-handoff.md) and [Chinese translation](maintainer-handoff.zh.md). Explicit SQLite activation, account restart and public server package provisioning are accepted through the backend; remaining desktop and gameplay acceptance are unfinished.

Current verified increment: the latest default suite passes 314 managed cases with two opt-in checks skipped; all four external-package cases passed against d66cc86a, including full HTTP/AList-fixture provisioning and startup. The separate public AList installation and backend lifecycle also passed against that distribution. Server packages use the shared persistent download queue, separate source/content-bound directories and the hosting package lock; Store installation and compatible package selection are connected. Official server publication is accepted; official launcher publication and desktop updater acceptance remain open. Recovery requests are isolated per parent, and offline repair preserves malformed journals and refuses an occupied installation lease. Launcher obsolete-file cleanup and retained-updater power-loss repair are source-bound and transactional. Explicit local SQLite storage is integrated; real-client durable gameplay remains open. Frontend types/build and
headless light/dark account, updater and hosting flows pass. Local and store file/ZIP plugin delivery are
implemented. CN2013 ticket login reached the real server lobby and original DLL
restoration/session cleanup passed. The default AList catalog contains a published server package. Accounts
now support multiple protected sessions per server; access tokens remain process-local
while refresh credentials survive launcher restarts. Earlier per-card snapshots
below retain historical validation counts; see the latest execution notes.

The maintainer reopened multi-account, rooms/local hosting and private-game module
work. The maintainer reopened real launcher and game testing after work on 2026-10-08.
Desktop acceptance and real-client tests were authorized for that session; the
2026-10-09 rest window has since ended and desktop/game testing is prohibited
until the maintainer authorizes it again.

The handoff milestone is completion of the difficult Hook, login, plugin, local
server hosting and multiplayer core, including implementation and verification.
It is not completion of every product feature, and it is not satisfied by empty
interfaces. The maintainer subsequently requested a model switch before that
milestone; the continuation must finish the remaining core work rather than
reclassify it as done. The maintainer
explicitly rejected public-key/signature provisioning for launcher updates; use
ordinary generated file indexes and download checksums.

* Native: FtHook 3.3 has the versioned C ABI, verified IAT writes, schema-two
  readiness and process-authenticated local control, plus optional minidumps
  and bounded receive traces. Release|x86 builds warning-free. Runtime 85 checks and
  host 18 checks pass; native integration covers 25 fresh-process scenarios
  including an actual fatal access violation with a preserved diagnostic file.
  CN2013 native ticket login and cleanup have passed; H8 remains open for the other
  builds, optional plugin combinations and gameplay scenarios.
* .NET: the solution builds with 0 warnings; the latest default suite passes 295 cases with one opt-in package theory skipped. One preceding full run exposed transient access-denied failures during descendant executable cleanup and native DLL restoration; native restoration now retries transient Windows locks with hash rechecks and a bounded deadline, verified by three contention tests. Hosting now terminates surviving owned descendants and verifies the job is empty before releasing package/save leases or reporting its final state. Explicit-stop and unexpected-parent-exit tests cover immediate child exit and reuse. Unrelated external file readers remain outside this guarantee. All four real-package cases passed separately, including complete HTTP/AList-fixture download, per-file integrity, startup and storage checks. Managed plugin and
  instance definitions, instance storage, process monitoring and the host bridge
  are implemented. Additional real game launch acceptance remains H8.
  Runtime package deployment has hashes, a durable rollback journal and conflict
  detection. Synthetic process tests cover native readiness, IPC, dump output
  and cleanup. Windows static-resource navigation was repaired and the maintainer
  confirmed that the desktop Home page renders. Tray activation still needs
  desktop interaction acceptance; explicit orphan-session recovery is connected on the Clients page.
* Web: the frontend handoff is complete. Real instance/plugin controls, manual
  endpoints and appearance settings replace demonstration state. Type checking
  and production build pass. Server authentication and independent server storage are implemented; client download/install, local folder/ZIP imports and store plugin downloads are connected.
* Update UI: client and launcher tasks have a development-only, in-memory preview
  with separate total/file progress, speed, ETA and task controls. Home keeps its
  card boundaries fixed and shrinks the video when details expand. Explicit
  expand/collapse labels and a separate downloads link make the controls visible.
  Downloads keeps progress fixed while task/file lists scroll independently.
  Type checking, production build and nine frontend tests pass. Real client transfers and transactional installation are connected; automatic update
  discovery and restart scheduling are connected. The external launcher updater now
  stages files through HTTP/AList, defers commit until frontend readiness and rolls
  back failed startups. Official package publication remains open.
* Client dialog: flyout layers now inherit their owning dialog's level, including
  plugin enum controls and server-defined registration selects. Escape dismisses
  the active flyout first. The installation path is a real persisted destination;
  nonempty destinations require confirmation and overlapping installations are
  rejected. Language controls are retained as commented markup. Registration
  preserves existing files; store installation performs manifest-based replacement. An
  isolated Release output builds with zero warnings and passes all 59 managed
  tests while the running host retains its locked binaries.
  Client names can be blank in the add dialog; they default to the selected build's
  localized title. The store allocates and persists the first unused numbered
  name on collisions, under a creation-name lease.
* Store: client/plugin browsing, search, release details and source management are
  implemented. AList and HTTP share a bounded TOML catalog parser and path rules;
  source definitions persist in the desktop host. The bundled Fantasy Tale source
  is configurable and points to the supplied AList endpoint with a published server package. An opt-in browser fixture
  supports visual review. The maintainer accepted the layout; source catalog-path
  content now aligns with its expander title and uses height/opacity/chevron motion.
  Real client downloads, manifest installation, plugin packages and launcher update
  staging/restart are connected. Official launcher publication remains pending.
* Home: the three-game carousel, localized titles, private-game display states,
  regional notice and default server names are wired to definitions. Video covers
  start muted and support ordered playlists, manual next and automatic wrap with
  fades. Browser playback checks pass for sound toggling, default mute on game
  changes, single-clip control hiding, two-clip manual/automatic wrap, invalid-clip
  skipping and the all-failed fallback. Real server endpoints and private modules
  still need to be supplied.
* Window: the maintainer accepted the larger DPI-aware default size. The caption
  content now sits in the top 32 pixels, navigation defaults to a compact rail,
  and card outlines have slightly more contrast. A layered-window click-through
  fix is implemented; native regression tests cover input bounds and repeated
  maximize/restore cycles. Browser checks pass for settings navigation, expanded
  and compact navigation, and the 800-pixel overlay layout. Settings cards now
  fill the content width; the expanded pane is 256 pixels and the compact rail
  remains 56 pixels. Both widths and absence of horizontal overflow were verified
  in the browser; frontend type checking and production build pass. The desktop inspection
  helper failed to capture the latest window, so physical-click acceptance of
  the click-through fix is still pending.

Current order: finish durable local package/storage integration,
then remaining H8 checks when live testing is authorized. Plugin upgrade discovery, immutable installation and explicit activation are implemented.
Multi-account selection and store plugin delivery are implemented. Preserve the accepted UI and keep live progress
independent from development fixtures.

The opt-in `LocalServerPackageTests` additionally passes against the complete
managed-stdio server package: readiness, six-character password registration/login,
ticket issuance, local administration, graceful exit, released ports, restart,
session invalidation and distinct host-player/remote catalog endpoints. It copies
the package and excludes local overrides/environment files; no launcher or game
was opened. Temporary accounts intentionally disappear on restart; local SQLite
accounts retain their password across restart. This does not claim real-game local
play or full gameplay-state restoration acceptance.

Facts that are easy to get wrong:

* CN builds and kr-1256 ship the original Nexon `nmcogame.dll` (10 exports, v1 call
  interface); they run in `forward` mode with the original renamed to
  `nmcogame.nexon.dll`. kr-812, id-400, id-1809 and tw-454 already use the stand-in.
* The font-weight patch NOPs the `jne` so the bold store runs for every language group;
  it matches only kr-812, cn-706, cn-717, id-400 and id-1809 so far.
* `pe_timestamp` identifies a build in packed and unpacked images; `image_size` differs.

## Overview

| Card | Title | Depends on | Status |
| --- | --- | --- | --- |
| P0 | Repository, docs, roadmap | - | done |
| H1 | FtHook runtime core: memory, pattern scan, log, PE info | P0 | done |
| H2 | Plugin and session loading (toml++), compatibility, options | H1 | done |
| H3 | Apply engine: preflight, patch, timing/retry, result lines | H2 | done |
| H4 | `nmcogame.dll` host: union exports, standin + forward modes | H1 | done (needs H8) |
| H5 | Built-in plugins: font-weight, no-file-verify, shop (kr-1256), crash recorder | H3 | doing: diagnostic mode 1.1.2 adds bounded dump outcome records, remains opt-in and passes controlled id-400/cn-406 dump/receive/cleanup tests; retail diagnostics, font variants and client acceptance remain |
| H6 | Native plugin ABI + sample native plugin | H3 | done (608dea6) |
| H7 | `tools/verify-plugin`: check plugins against client images offline | H2 | done (pattern and rva; locators in game only) |
| H8 | Real-client verification per build; deprecate BFClientHook V2 | H4, H5 | doing: cn-406 ticket login, native readiness and exit restoration accepted; other builds/plugins and round gameplay pending |
| L1 | .NET solution skeleton: Sdk, Core, Downloads, Host, Games.BubbleFighter | P0 | done |
| L2 | Definitions: game/build/plugin/instance TOML models + validation | L1 | done: validated definitions, external client identities, per-build recommendations and immutable plugin versions |
| L3 | Photino host: window, Mica, custom chrome, bridge, embedded assets | L1 | done: desktop chrome, taskbar/tray, single instance, local resource origin and bridge; startup/update health verified separately |
| W1 | Frontend skeleton: shell, NavigationView, theme, splash, i18n x6 | L3 | done: shared WinUI controls, settings, per-game navigation, animated shell/dialogs and maintainer desktop acceptance |
| W2 | Home page: game switcher, video background, server/instance card, start button | W1 | done: contained layout, playlists, server/account/language controls, refresh sessions and launch/update state wiring; retail coverage remains H8 |
| L4 | Instances: create/clone/delete, folder layout, launch with session.toml | L2 | todo |
| L5 | Plugin manager: install from folder/zip, enable/disable, locked required, options form | L2, W1 | done: staged imports, immutable versions, per-client selection and bridge/UI tests |
| D1 | Download engines: aria2 JSON-RPC + HTTP ranged resume | L1 | doing: verified HTTP resume implemented; aria2 pending |
| D2 | Sources: HTTP + AList; manifest model; verify + atomic apply (add/replace/delete/move/rename) | D1, L2 | doing: common client manifests, size/hash verification, transactional add/replace and recovery implemented; delete/move/rename pending |
| S1 | Server API contract (OpenAPI) shared with BubbleFighter.Fantasy | P0 | todo |
| S2 | Launcher side: add server, server info, account login/register, profile, ticket | S1, L2 | todo |
| S3 | Server side plugin in BubbleFighter.Fantasy (separate repo card) | S1 | todo |
| P1 | Plugin stores: source list, index format, install/update by SemVer | L5, D2 | done: source-bound discovery, HTTP/AList file/ZIP delivery and explicit activation |
| U1 | Client file update discovery and scheduling | S2, D2 | doing: pinned snapshots, optional labels, cached checks, restart recovery, queue, settings, Home/Clients UI and server-directed source binding implemented; real client acceptance remains |
| U0 | Client and launcher update UI, progress model and isolated development preview | W1, W2 | doing: accepted UI connects real tasks; official AList x64 staging and Photino desktop replacement/health acceptance passed; further interruption coverage remains |
| U2 | Launcher file publication, staging and external restart/rollback | D2, U0 | done: source adapters, publication, staging and isolated helper acceptance; see execution note |
| U3 | Launcher power-loss repair and obsolete-file cleanup | U2 | done (d52695c): source-bound obsolete-file cleanup, rollback and retained-updater offline repair implemented; real hard-power timing remains a follow-up |
| R1 | Owned local server lifecycle and hosting UI | S2 | done: real published CN2013 package verified through backend registration/login/tickets, stop/restart and host-player catalog; real-client play remains H8 |
| R2 | Room directory publication, leases, discovery and join | R1 | doing (Astra): real self-hostable API, reviewed servers, backend room leases, join/account fencing and UI implemented; official deployment verified; remote-player acceptance remains |
| R3 | Local server package installation and durable player storage | R1, D2 | doing: SQLite/account restart, publication, shared HTTP/AList download/installation and hosting selection implemented; complete public AList provisioning and backend lifecycle accepted against d66cc86a; desktop acceptance and real-client gameplay pending |
| X1 | Content-addressed shared store for instances | L4, D2 | todo |

## H1. FtHook runtime core

Goal: a static x86 C++20 library `fthook` under `native/runtime`.
Files: `include/fthook/{memory,scan,log,pe,version}.h`, `src/*.cpp`, `fthook.vcxproj` in `native/FantasyTale.Native.sln`.
Content: verified page-local patch with readback and rollback; masked pattern
search over executable sections with unique/first semantics; bounded log
(size cap, try-lock, pre-opened handle); PE header info (timestamp, image size,
sections) for the main module.
Acceptance: builds warning-free (`/W4 /WX`) for Win32; unit tests for pattern
parse/search and patch rollback pass.

## H2. Plugin and session loading

Goal: parse `session.toml` and each `plugin.toml` per
[hook/session-contract.md](hook/session-contract.md) and
[hook/plugin-format.md](hook/plugin-format.md); unknown keys are errors.
Acceptance: tests cover every documented error (bad schema, unknown key,
id/version mismatch, option out of range, unavailable build, rva without a
single build, overlapping variant builds).

## H3. Apply engine

Goal: resolve patches for the build, preflight all, then write; `unpacked`
timing retries every 25 ms up to the deadline on a worker thread; result lines
exactly as documented.
Acceptance: tests run against a synthetic code buffer: success, already-present,
mismatch leaves memory untouched, timeout.

## H4. `nmcogame.dll` host

Goal: `games/BubbleFighter/native/nmcogame`. Exports the 11 entry points
(D1). `standin` answers like the previous stand-in (codes in its source);
`forward` lazily loads `nmcogame.nexon.dll` on first call (never in DllMain)
and forwards. DllMain starts the runtime worker. The login ticket from the
session is the passport when present.
Acceptance: exports verified by a test; forward mode passes a fake original's
calls through; stand-in replies are byte-identical to the previous stand-in.

## H5. Built-in plugins

* `bubblefighter.fantasy.font-weight` (pattern from the ID2014 localization work:
  `83 3D ?? ?? ?? ?? 02 75 03 89 ?? 3C`, jump at +7, `75 03` -> `90 90`).
  Claimed builds must each be checked with H7 before being listed.
* `bubblefighter.fantasy.no-file-verify`: locator `fthook.config-md5-verdict` (port of
  the existing scanner), `32 C0` -> `B0 01`.
* `bubblefighter.fantasy.kr1256-shop-gates`: the kr-1256 shop gates, required on kr-1256.
* `bubblefighter.fantasy.crash-recorder`: native; vectored exception recorder with
  bounded output (port of the previous recorder).
Acceptance: each plugin passes H7 on every build it claims.

## H6. Native plugin ABI

Goal: `include/fthook/plugin_abi.h` (C, versioned struct of function pointers),
loader, locator registration, and a minimal sample in `native/samples`.

Implemented in `608dea6` (`Add versioned native plugins and bounded crash diagnostics`).
The ABI includes stable host storage, options, unique scanning, bounded reads,
entry-time locator registration and staged verified writes. Activation finishes
in session order. Failure cleans up before unloading; unsuccessful cleanup or
uncertain write restoration retains the DLL and API. Successful modules have
bounded process lifetime. See [hook/native-abi.md](hook/native-abi.md).

Validation: Release|x86 `/W4 /WX`, zero warnings/errors; runtime 62 checks, host 18
checks, 25 child-process scenarios with 101 parent assertions. The C-only sample,
entry C++ exceptions, entry/locator SEH failures, provider failure isolation, undo journal,
actual fatal access violation, record and file-size limits pass. .NET Release
build has zero warnings/errors and all five tests pass. The first build's test
fixture incorrectly declared a deliberately throwing export as nonthrowing;
it was corrected to `noexcept(false)` without suppressing warnings. Original
build output remains under ignored `native/out/h6-build-first.log`.

## H7. verify-plugin

Goal: offline checker that maps a client image (unpacked dump or file) and
reports, per plugin and build: match count, expected bytes, and for rewritten
relative jumps the original and new targets.

## H8. Real-client verification

Goal: per build, a checklist row (build, plugins, result lines, user
confirmation). Then mark BFClientHook V2 deprecated in BubbleFighter.Fantasy.

Still pending: the maintainer has not tested V3 in a client. See
[the test procedure](hook/real-client-test.md) and its Chinese translation.
`python tools/prepare-hook-test/prepare.py --build id-400 --output artifacts/h8-id400`
prepares the host, plugin files, separate session templates and SHA-256 manifest.
It does not install files into a client or launch one. KR812, ID400, ID1809 and
CN324 are supported by the preparation tool; the font template is omitted where
the font plugin does not declare support.

Procedure for one build:
1. Back up the client folder's `nmcogame.dll`. For builds whose definition says
   `messenger = "forward"` (all CN builds, kr-1256), rename the original to
   `nmcogame.nexon.dll`.
2. Copy `native/out/Release/nmcogame.dll` (built from `native/FantasyTale.Native.sln`) into the folder.
3. Write `session.toml` beside it (see docs/hook/session-contract.md) listing the
   plugins to test, with `path` pointing at `games/BubbleFighter/plugins/<id>`.
4. Start the client as usual. Read
   `%LOCALAPPDATA%\FantasyTale\BubbleFighter\logs\fthook.log` (or the session's
   `log`): it must show the messenger mode and one `plugin id=... result=...` line
   per plugin.
5. Record build, plugins, results and what was seen in game in the table below.

| Build | Messenger | Plugins | Result lines | In game | Date |
| --- | --- | --- | --- | --- | --- |
| KR2015 (`kr-812`) | standin | `bubblefighter.fantasy.mid-match-exit` 0.1.2 | Offline verifier: 5/5 RVA checks matched expected bytes and branch targets | Not run; static prerequisite only | 2026-10-10 |


## L1-L5, W1-W2, D1-D2, S1-S3, P1, U1, X1

See [architecture.md](architecture.md) for the intended behaviour. Each card
must end with: warning-free build, tests for new logic, updated docs (English
and Chinese for user-facing guides), and a roadmap status update.

W2 display definitions include Bubble Fighter, Air Rider and Tales Runner in
that order. The private games have no bundled executable module or build;
Air Rider uses the normal page and Tales Runner shows Preparing and its regional
service notice. Server names are presentational until endpoints are supplied.
Definition tests cover ordered/legacy video lists, invalid media paths and the
16-item cap; the managed suite passes 57 tests with a warning-free Release build.


### Server integration working tree

S1/S2/S3: common schema-one discovery, separate server TOML storage, authentication,
profiles, preset import, server-overridden capabilities, game languages and IP policy
are implemented. Companion plugin uses interface-owned account storage and bounded
sessions. Native game-ticket redemption and actual client authentication remain
unverified. Private modules and client download manifests are still required for
those games. Separate soundtrack playback is implemented; browser verification is
recorded with the current task artifacts. No claim of H8 acceptance.

The common rank-data reader now loads BF/AR normalized tables and TR original kind-1
rows across two atlases. Piero/unknown icon overrides preserve normal EXP. Managed
rank boundary, future-row, atlas-split and invalid-file checks pass; three frontend
rank tests cover icon overrides and progression. API deployment 3f8c310b includes
cumulative EXP and configurable first-password enrollment for legacy identities.
Public registration/login/profile/ticket/logout and server session isolation checks
pass. The deployment accepts six-character initial passwords. Actual MySQL tests
cover concurrent first enrollment, retained character identity and rejection of
password replacement; native game-ticket consumption remains separate.
Native game-ticket consumption and H8 remain unimplemented/unverified.

## Connected client installation

The Store now creates or selects a client and starts a real host task. HTTP and
AList share exact-byte manifest integrity checks, bounded streaming/range resume,
per-file SHA-256, same-volume staging, instance leases and durable rollback.
The receipt pins source/package/release identity. Home and Downloads consume
separate live snapshots while preserving the accepted fixed-card layout.

Actual loopback HTTP tests exercise the desktop bridge, catalog, both source
adapters, file download, installation and installed-client status. Fault tests
cover mismatched ranges/hashes, full-response restart, pause/resume, external
changes, rollback and repeatable process-loss recovery. The 129-test managed
suite passes; frontend types, production build and nine tests pass.

Remaining: client archive packages, store plugin delivery, signed external self-update,
server lists/rooms and local hosting. H8 and CN authentication bridge acceptance
remain separate. No published official source or game distribution was invented.


## Persistent downloads and automatic folder updates

Download history now survives restarts with exact index bytes, bounded atomic records,
source fingerprints, previous-receipt checks, two-worker scheduling and explicit-pause
preservation. Completed/canceled records can be cleared persistently. Client checks
bind to installation receipts; sources are never guessed for imported clients. Settings
separate automatic discovery (on) from automatic installation (off). Running clients
are skipped; Home and Clients show update actions within the accepted workspace layout.

The maintainer clarified that original game version labels never change for ordinary
file updates. Folder publication now requires only a one-time client directory mapping.
The Python watcher generates catalogs/file snapshots automatically, hashes changed files,
retains immutable content for in-flight tasks and supports a storage budget. AList and
HTTP consume identical generated output. Optional update labels need no version bump;
unlabelled snapshots are valid. Cached metadata avoids repeated large-file reads on both
ends; actual downloaded bytes still receive full SHA-256 checks. Transfer totals include
only files requiring download. No official distribution or production source was invented.

Validation includes real HTTP/AList resource-only updates without changing/redownloading
the installed executable, task restart/resume/source changes, queue capacity, file-cache
checks, catalog selection and publisher cache/publication tests. Browser checks cover
update settings, Home and client rows, unchanged main-card size and optional-label UI.
The desktop bundle is built separately; real game H8 acceptance remains pending.

Final validation for this increment: 151 managed tests, four publisher tests and nine
frontend tests pass; frontend types/build and DesktopQA build pass with zero managed
warnings. Headless Edge checks confirm update preferences, unlabelled Home updates,
client rows and stable game-switch geometry. Under concurrent visual/test load, existing
native hook tests intermittently hit readiness timing or a temporary restore-file lock;
the isolated hook suite and subsequent complete run passed. Keep this observation for
H8 stress testing rather than treating fixtures as physical-client acceptance.

## Verified local plugin imports

The Plugins page now selects a client and previews either a folder or a ZIP before
import. A per-user library stages bounded copies, validates game/build/runtime and
native x86 PE headers without execution, pins preview bytes, and commits a whole
version with an atomic directory move. Startup clears interrupted staging; cancel
and failure preserve installed packages and client configuration. Bundled IDs and
global required_on declarations cannot be overridden by imported manifests.

Instances keep exact plugin versions. The list matches selection by id and version,
and explicit version switching resets only that client's options. Old versions
remain available to other clients. Native ABI/export checks and real game behavior
remain launch-time/H8 responsibilities. Store downloads and source-bound upgrades are covered by later increments. Bundled plugins follow launcher updates; an uninstall UI remains future work.

Validation: 26 new import tests include folder/ZIP copies, game/build/runtime
rejection, immutable versions, preview mutation, missing/invalid/managed DLLs,
ZIP traversal/aliases/links/central-directory bounds, expanded-byte limits,
cancellation, abandoned staging, and real bridge selections for two clients.
The 177-test managed suite, nine frontend tests, frontend typecheck and build pass.
Headless Edge verifies the import dialog, dialog/flyout layer ordering, disabled
initial state and version-switch confirmation at 1360 x 840. Desktop native-file
picker interaction and actual in-game loading of imported packages are not claimed.
The existing required-plugin readiness fixture timed out once during the expanded
suite. Its isolated 10-test hook suite and the subsequent full 177-test run passed;
no timeout was increased and this intermittent behavior remains open for H8.
The refreshed desktop bundle is under `bin/PluginQA/Release/net10.0`.

## Current execution scope

The earlier temporary deferral of multi-account selection, rooms/local hosting and
private Air Rider/Tales Runner modules was lifted on 2026-10-08. Continue store
plugin delivery, real-client authentication/H8 using cn-406, and the separately
trusted launcher updater. CN406 launcher ticket consumption and native lobby entry
are now accepted; see the latest CN2013 execution note below.

The hidden-startup fix explicitly shows the native parent before WebView controller
creation. Its window regression and the complete 178-test managed suite pass;
Release build has zero warnings/errors. Frontend typecheck/build and nine frontend
tests pass. This does not replace desktop visual or real-game acceptance.

## Store plugin delivery and configured default source

P1 file-package installation now shares the persisted client download queue,
HTTP/AList adapters, ranged transfers, checksums and download views. Plugin files
are rechecked against the entire catalog-pinned manifest and committed as an
immutable library version. Source receipts support exact retries after process
loss without allowing another source to replace the version. Installation leaves
client selections unchanged and preserves an open local import preview.
Store ZIP delivery and source-bound update discovery are implemented in later increments below.

The bundled source now contains the maintainer-provided AList endpoint. Old blank
overrides inherit it, while explicit addresses and disabled preferences survive.
The remote /FantasyTale mount and public source.toml were initialized through the
AList API; anonymous resolution and exact-byte retrieval succeeded. The catalog
is empty until actual release files are published. Ordinary sources still have no
authority to replace launcher binaries.

The native window now declares an independent application identity and app-window
style instead of tool-window style. The 185-test managed suite passes with zero
Release build warnings/errors. This includes shared HTTP/AList plugin delivery,
restart history, source pinning, byte tampering, preview preservation, blank-source
migration and taskbar window-style checks. A previous loaded full-suite run hit
existing hook readiness/exit timeouts; retain that observation for H8.

The maintainer confirmed taskbar-button behavior on the physical desktop. Startup
now displays the supplied transparent logo rather than a standalone colored ring.
The optimized PNG is 30,665 bytes; a ten-size ICO is 71,296 bytes and is embedded
in the executable and used by the window/tray. The original 2K file is not shipped.
The real title-bar component is teleported outside the fixed shell so it remains
above the startup surface. Headless frontend checks hold initialization pending
and verify light/dark logo loading, caption hit-testing and bridge commands, then
release initialization and verify the completed fade with no page script errors.
Frontend types/build and the complete 185-test managed suite pass; the desktop
bundle is under `bin/BrandingQA/Release/net10.0`.

Startup refinement removes all splash text and progress rings. Only the centered
logo remains, with the real caption controls available throughout initialization.
The splash fades out while the workspace fades in after two paint frames; no
minimum startup delay is imposed. Light/dark checks recorded intermediate opacity
frames for both surfaces, verified logo-only content and all caption commands,
and reported no script errors. Frontend types/build and the StartupQA desktop
build pass with zero warnings/errors.

Startup close now uses the normal application shutdown path until the frontend
reports completion of its entrance transition. Caption and native close requests
therefore exit during initialization, while subsequent closes retain tray behavior.
Workspace entrance uses a 500 ms decelerating fade, small translation and scale;
transition completion drives cleanup with a bounded fallback for suppressed events.
Frontend light/dark motion and caption checks pass. The StartupMotionQA desktop
build reports zero warnings/errors. A direct native process-close check was blocked
by tool policy and has not been claimed as physical desktop acceptance.

The initial frontend background and logo now fade in from white. Once startup
finishes, manual caption closes open the shared animated ContentDialog with End
and Run in background choices. Confirmed application exit reuses shutdown and
client cleanup without a duplicate native confirmation; tray exit retains its
existing confirmation. Light/dark frontend checks exercise both choices, ensure
no startup dialog and verify entrance/exit animation frames. Frontend checks and
the StartupDialogQA Release host build pass with zero warnings/errors.

Startup motion now waits for the logo entrance animation before beginning the
600 ms workspace entrance, avoiding cancellation by a fast catalog response.
Dialog transitions have explicit parent/child lifetimes and emit afterLeave;
caption close actions defer their bridge command until the leave animation ends.
Reduced-motion mode retains brief opacity changes without spatial dialog motion.
Light/dark QA in normal and reduced-motion modes records intermediate frames for
logo entrance, workspace entrance, dialog entrance and dialog departure, then
checks caption actions and both exit choices with no page script errors. Frontend
types/build and the SmoothStartupQA desktop build pass with zero warnings/errors.

Startup now keeps a pure-white logo surface in both themes. Workspace entrance
is a separate Web Animations phase after the splash clears, so visible movement
is no longer consumed beneath the splash. The exit dialog has explicit background,
end and cancel choices; Escape cancels without issuing a bridge close command.
Four light/dark and normal/reduced-motion checks verify white background, visible
workspace opacity frames after splash removal, both close actions and cancellation
by button and Escape, with no page script errors.

## Server-scoped multi-account selection

Session and launch-ticket expiry checks now share an injectable TimeProvider.
The targeted MultiAccountTests suite passes all 10 cases, including advancing
the clock past expiry and verifying both the account list and selected account
are cleared. This is managed lifecycle validation, not desktop multi-account
or per-build retail launch acceptance.

Client identification investigation: 41 real local executables were inspected
without execution. The 19 retail releases have distinct signer-certificate plus
timestamp-authority-time tuples; signer certificates alone overlap across builds
and regions. Unpacked variants retain their corresponding PE linker timestamps,
including candidates with stale certificate directories. Nine probe tests pass.
The resulting replaceable `games/BubbleFighter/client-versions.toml` contains
all discovered local releases except BFReplay and BFClassic aliases, which share
the existing KR2015/KR1256 identities. Release dates are normalized to Korean
Standard Time (UTC+09:00) for year/name sorting; the user-facing UI does not
expose the timezone. Build ids remain stable wire ids. See
`docs/hook/client-identification-research.md`; launcher alias/identification
integration is wired, while desktop per-build launch acceptance remains open.

The Home account menu now lists server accounts and ends with Add account. Accounts
shows all servers, independent profile/rank values and targeted logout with a
confirmation. Passwords and access tokens remain process-local; protected refresh
sessions restore accounts across launcher restarts. Expiry, failed login, token
rejection and logout affect only the correct identity. Server edits fence pending
login completion and account-selection changes invalidate in-flight launch tickets.
Explicit resource limits remain enforced.

Nine new managed tests cover account/server isolation, failed login, stale rejection,
targeted revocation, pending login after server removal, launch switching back, stale
profile updates, session bounds and failed remote logout. The 194-test suite passes;
headless light/dark checks cover Home selection, adding an account, all-server lists,
selection and targeted logout. Caption choices are now End / Run in background /
Cancel, verified by button text and order. No desktop launcher or game was started.

## Shared store ZIP plugin delivery

Plugin manifests may select format=zip and one catalog-pinned transport file.
HTTP/AList downloads and restart history reuse the existing queue; extraction and
immutable installation reuse PluginLibrary. Transport is copied and reverified
before and after extraction; installed retries compare source receipts and the
expanded bytes. Pending local previews survive store operations. Targeted store
and import tests: 44 pass, including both transports, retry/source conflict,
malicious paths/links, archive mutation and malformed format rejection.

## CN2013 native ticket login and restoration

FtHook 3.3 adds a thin login_bridge.dll forwarding to the existing host, and the
cn-406 build definition selects its sdologinentry.dll destination. Interface GUIDs,
record offsets and stack cleanup follow the CN406 corpus; the 75-character marked
ticket is never truncated. Schema-two readiness gates delivery. No optional
plugins were enabled in the first live acceptance run.

Nine new managed cases cover launch planning, unsafe targets and four controlled
native login scenarios. All 211 managed tests pass, native suites report 76/18/101
checks without failures, and frontend types/build pass. One earlier 202-test run
reproduced the pre-existing required-plugin fixture timeout; its isolated retry and
the final 211-test run passed without increasing timeouts.

During the maintainer-authorized test window, the real backend bridge registered
and signed into the live service, obtained a cn-406 ticket, started the unmodified
client executable and reached its lobby with the correct character. The server
independently confirmed successful ticket redemption. instances.stop then completed;
both original DLL hashes matched and session.toml was absent. The real desktop
home rendered correctly and the caption dialog order was End / Run in background /
Cancel. Other builds, full gameplay and optional-plugin combinations remain open.

## Source-driven launcher publication and restart

A publication command scans a complete build into ordinary TOML metadata and
immutable file objects. HTTP and AList share staging, checksum validation and the
existing progress views. A source URL is sufficient; no signing or key setup is
required. Paused downloads survive restart and verified cached objects work offline.
The selected index is pinned again before the external helper receives ownership.

The helper waits for the exact parent to exit, retains original files until the new
frontend reports readiness, and restores/restarts the previous installation when
startup fails. New game launches are blocked throughout handoff and health checking.
An occupied installation lease before replacement also restarts the old process.

Validation: 228 managed tests pass; Release builds with zero warnings/errors.
The final handoff selection change also passed 27 targeted update/download tests.
Isolated dummy processes cover successful commit, child startup failure and an
occupied installation lease. Frontend types/build and headless light/dark updater
settings/progress/restart-dialog checks pass. These are not real desktop updater
acceptance: an earlier authorized desktop capture timed out and its owned process
was stopped. No launcher or game is started under the current prohibition.

No official launcher package is published yet. Interrupted power-loss repair,
obsolete-file deletion and real desktop update acceptance remain separate work.
The core handoff milestone still includes implemented and verified Hook/login,
plugin upgrades, local server hosting and multiplayer, not just their interfaces.

## Owned local server processes and hosting controls

The shared service now actually starts and owns server processes using private
stdio readiness/stop, package leases and kill-on-close Windows Job Objects. Each
run has a unique id. Startup/stop have deadlines; unexpected exit, logging errors
and forced cleanup remain visible. Process logs and history are bounded. Restart
updates, profile editing and deletion are fenced while hosting is active.

The CN406 module generates an external deployment patch. Private play stays on
loopback; directly reachable rooms use the explicit host address. The existing web
administration remains loopback-only. Temporary in-memory persistence is opt-in;
the default uses the package's configured database. Original profiles are preserved.
A ready invocation becomes a transient server destination using existing accounts,
registration and launch tickets. Stopped invocations disappear from selection.

The Hosting page has fixed main controls, independent profile/log scrolling,
animated configuration expanders and six locales. It exposes setup, start, stop,
Go to game and the existing web administration. Ten new tests use isolated dummy
server processes and the real bridge, covering graceful/forced stops, failures,
timeouts, stderr marker rejection, stale ids, leases, descendants, output bounds,
transient destinations and updater/profile fences. All 238 managed tests pass;
Release is warning-free. Frontend types/build and light/dark headless QA pass.

The companion server chat is implementing the minimal --managed-stdio contract.
No real launcher/game has been started. Actual package readiness, full local-client
play, automatic package/database provisioning and room directory publication/join
are not claimed complete. Continue these core areas before recommending handoff.

## Real directory service, owned publication and joined-room accounts

An independent HTTP service now persists reviewed servers and pending submissions,
and manages temporary rooms with bounded private leases. The shared client uses the
existing IP preference and explicit source policy; writes never follow redirects.
Backend room publication starts only for ready owned invocations, renews outside
the page, and withdraws on stop or crash. Expiry and stale owner tokens cannot affect
a newly published room. Source edits are fenced while publication remains active.

Directory join rechecks the source record and the target's real build catalog.
Joined rooms remain transient, their membership refreshes in the backend, and
changed/removed destinations discard the old account scope before a connection.
Approved persistent servers enter the ordinary collection. Directory and store
sources remain independent. The default directory slot exists but still needs an
actual official endpoint; no address is presented as working before deployment.

The Multiplayer page and Hosting publication controls reuse fixed workspaces,
shared flyouts/dialogs and six locales. Headless light/dark QA verifies sources,
submission, version selection/join and publication/withdrawal without document
overflow or script errors. The actual CN406 package additionally passes directory
join, real account login and invalidation after room withdrawal. Isolated process
tests prove background renewal and automatic withdrawal after a killed owned child.
No real launcher or game was opened. See `docs/multiplayer.md` and its Chinese pair.

Still core: official directory deployment and remote acceptance, durable local
storage/package installation, plugin upgrade boundaries and remaining H8 scenarios.
The server chat accepted the separate SQLite repository-plugin task; it is not yet
implemented and temporary memory is not described as durable single-player storage.

## Source-bound plugin upgrades and independent directory deployment package

P1 now checks each compatible installed version against its original source receipt,
using one catalog fetch per source. Stable installs exclude prereleases, SemVer build
metadata cannot force an upgrade, and same-version content changes or ambiguous equal
precedence are conflicts. Source retargeting/disablement blocks discovery and stale
version/digest confirmation blocks download. Manual and bundled packages are not
adopted by unrelated store listings. One operation and 256 versions bound each check.

Updates reuse the existing HTTP/AList file/ZIP queue and immutable library. The
accepted plugin list now adds Check for updates, animated per-plugin update strips,
download progress navigation and explicit version-switch confirmation. Downloading,
canceling and failures leave pinned versions alone. Completion offers activation;
changing a client clears stale check results. Old versions remain available.

Validation: 257 managed cases pass, one external-package case is skipped without its
opt-in setting. Nine new plugin upgrade cases cover four actual HTTP/AList + file/ZIP
combinations, source and digest changes, incompatible builds, prerelease policy,
SemVer metadata ambiguity, retained versions, per-client activation and occupied
instance leases. Frontend types/build pass. Headless Edge light/dark checks verify
check/download/completion/confirmation/cancel, selection isolation and fixed page
bounds, with no script errors. No real launcher or game was opened.

Commit a759e1e also supplies a separate non-root directory container with persistent
storage and bounded memory/logs; empty optional deployment regions now work. Nine
directory tests pass. The Linux x64 archive was handed to the authorized server chat
for independent deployment and public-endpoint verification. The official bundled
address remains unconfigured until that verification returns. SQLite persistence,
server package provisioning and remaining H8 acceptance remain core work.
## Verified official directory and stable local-save ownership

The authorized server chat deployed a759e1e as an independent directory container.
External info/list requests, unauthorized administrator rejection and restart health
passed. An isolated executable using the launcher's actual DirectoryClient also
verified the domain and IP entry points, including discovery fallback and the
server-advertised IP policy. The bundled source now uses the verified domain so DNS
migration remains possible. Old empty overrides inherit it without changing an
explicit replacement URL, custom label or enabled preference. No test rooms were
published to the public directory.

LocalServerContext now supplies a stable per-profile DataDirectory, independent of
program packages and invocation logs. An exclusive save lease prevents two package
copies from sharing a live save. Ownership records reject another game/build,
corruption and unowned existing files before startup. Restart, profile renaming,
package replacement and profile deletion preserve data. Roots cannot overlap and
at most 32 retained save directories are allowed. This is the storage boundary for
the upcoming SQLite provider; it does not redefine existing temporary/package modes.

Validation: all 262 managed cases pass with FT_TEST_SERVER_PACKAGE enabled, including
real CN2013 account registration/login, tickets, restart, directory join and account
invalidation. Four new cases cover stable save paths, exclusivity, corruption,
retention, limits and bundled-source migration. No real launcher/game was started.
SQLite persistence is still being implemented in the authorized server chat; its
complete package will be integrated and verified before local durable play is called
done. Server package installation, remaining H8 scenarios and remote-player acceptance
remain part of Astra's core work, ahead of a model handoff.
## Unified server workspace and desktop layout refinement

Saved-server management now lives in Multiplayer > Servers alongside directory
listings. The old navigation entry and page are retained with deprecated comments;
old links redirect to the maintained tab. Persistent and transient destinations are
identified by the backend, so temporary rooms do not appear as editable saved servers.
Browsing catalog details is independent of the Home account selection. Add, edit,
remove, select and server-provided client imports reuse the existing bridge/dialogs.
Directory selection survives room/server tab changes. Cards use inset margins,
subtle borders and the existing theme tokens, and each pane scrolls independently.

Home now orders server, language and account, with the remaining width assigned to
the account. The caption dialog orders Run in background, End and Cancel while
retaining background as the accent/default action and the existing exit animation.

Validation: 262 managed tests pass, including the external-package integration and
transient destination classification. Release builds with zero warnings/errors;
frontend typecheck/build pass. Headless light/dark checks cover legacy routing,
server CRUD, catalog browsing isolation, client-import flyout layering, source
retention, joining rooms/servers, operation without a directory and Home layout.
Desktop acceptance also confirms the live server catalog, inset server cards,
fixed footer and the reordered exit dialog with successful cancellation. Both
exit commands are checked independently in the headless bridge fixture; no game
process was needed for this UI increment.

## Execution note: explicit local SQLite saves (2026-10-08)

Sol continued after the model handoff. Hosting adds package/local/temporary modes;
absent mode preserves legacy temporary_data semantics. New UI configurations choose
local saves, with six-locale descriptions and existing animated controls. The game
adapter requires the complete SQLite provider/native dependency and writes save.sqlite
in the separately owned stable directory. Conflicting selections fail validation;
provider errors never fall back to temporary memory. No account/data migration occurs
when switching providers, and no production database configuration was changed.

Validation: all 265 managed tests pass with the complete af95d321 external package.
Temporary accounts disappear on restart. SQLite accounts/passwords survive restart,
profile rename and package replacement; a second profile can register the same name
with another password without affecting the first save. Profile removal retains data.
Original package profiles remain untouched, ports are released and graceful stops pass.
An isolated-artifact test-copy bug was repaired and all 15 hosting tests also pass
under --artifacts-path. Release build has zero warnings/errors. Frontend types/build
and light/dark storage selection, bridge payload and existing hosting controls pass.
Real-client durable gameplay, package installation and remote-player acceptance remain
open; R3 is still doing.

## Execution note: readiness replacement and live local login (2026-10-08)

CN2013 local testing reproduced a refused login after native status publication
failed. Native snapshot readers now permit delete sharing; report replacement
retries transient sharing/access-denied contention for at most 200 ms and logs
credential-free Win32 errors. Persistent contention still fails authentication,
preserves the previous complete report and removes the unpublished temporary file.
Deterministic native tests hold a non-delete-sharing reader through both transient
and permanent replacement attempts. Release|x86 builds warning-free; native runtime,
messenger and integration suites pass 85/18/101 checks.

After packaging the rebuilt native host, two real CN2013 launches against the
owned af95d321 local SQLite package reached MainMenu with ready hooks. The server
was stopped and restarted between launches; the account password and player
identity were retained. The harness intentionally stopped each client immediately
after observing MainMenu; these exits are not crashes or gameplay-duration tests.
Each client stop restored original nmcogame/sdologinentry
hashes and deleted session.toml. The maintainer confirmed the first successful
login visually. A subsequent failed pre-fix attempt is not counted as success.
The computer-use native pipe is unavailable in this continuation, so automated
visual acceptance is not claimed. Gameplay-state restoration and remote-player
room acceptance remain open.

## Execution note: transactional obsolete launcher files (2026-10-08)

The external updater now enables source-bound file ownership in the shared installer.
Successful installation records bounded paths/sizes/hashes with its receipt. A later
release backs up and removes only recorded absent files that still match their old
contents. Health commit checks removals remain absent and all new owned files match,
including files that did not need replacement. Rollback restores removed files and
retains the original receipt/ownership. External edits are preserved with recovery
materials. Legacy installations establish ownership without guessing old files.
The journal and ownership records are size-bounded before filesystem mutation; the
journal is capped at 4 MiB/2048 entries and ownership at 1 MiB. File/directory shape
migration is explicitly refused. Game client installers retain additive behavior.

Validation: 25 self-update cases pass, including the real external helper's healthy,
crashed-child and locked-installation branches with obsolete-file backup/restoration.
New cases cover rollback/commit, edits before and during health, unrecorded files,
source mismatch, interruption immediately after removal and unchanged-file health
verification. All 276 managed tests pass without the optional external package, and both af95d321 package cases pass separately;
Release builds with zero warnings/errors. No launcher window or game was opened
for this increment. U3 is implemented; independent hard-power timing remains a follow-up.

## Execution note: retained-updater recovery (2026-10-08)

The updater now repairs an interrupted transaction offline. Before normal bootstrap,
the launcher detects journal.json and hands its exact hash and parent identity to the
retained helper outside the installation. The helper waits for parent exit, acquires
both leases, rechecks the hash, restores the journal and restarts the launcher. A
broken launcher can be repaired directly with `--repair`; no network or release index
is needed. Committed and uncommitted journals are handled. External edits, missing
backups, running installations and helper contention leave the journal and backups
untouched and write recovery-required.

Validation: 30 launcher-update tests pass, including bootstrap handoff, direct offline
repair, committed/uncommitted recovery, external edit preservation and helper lease
contention. Full managed tests pass 276/276 without the optional external package;
Release builds with zero warnings/errors. The environment-gated package pair is
skipped in this run. Durable journal recovery tests represent power-loss timing;
OS hard-power testing remains outside normal CI. U3 is implemented; official
publication and remaining client/gameplay acceptance remain open.

## Execution note: pending native startup ownership (2026-10-09)

Shell startup now runs on a worker outside the running-state lock. Pending starts
reserve a bounded slot, reject duplicate starts and retain the instance lease until
startup succeeds or rolls back. Explicit exit waits for pending starts before
stopping returned processes. Update, plugin installation and local-server stop
guards also treat pending starts as active. A Windows permission prompt is neither
bypassed nor programmatically accepted. Two shutdown/decline cases verify responsive
snapshots, concurrency fencing and lease cleanup. The test client now publishes its
ready marker atomically, and its lifecycle test awaits cleanup even on assertion failure.

The attempted retail cn-406 diagnostic run ended with Windows reporting canceled
startup before a game process was returned. Its isolated server stopped, original
DLL hashes were restored and the session file was removed. This does not constitute
retail diagnostic or gameplay acceptance. Native computer-use window enumeration
worked in this continuation; no security prompt was automated. The latest Release
build has zero warnings/errors and the default suite passes 289 cases with one
environment-gated external-package theory skipped. The four full-package cases
were accepted in the preceding provisioning increment, not rerun for this change.

## Execution note: pending-start successful exit and refreshed server package (2026-10-09)

A real child-process fixture now covers explicit launcher exit while module
preparation is waiting, followed by successful process startup. Exit stops that
returned process and waits for its monitor, session deletion and instance lease
release. All three pending-start cases pass; the complete default suite passes
290 cases with one optional package theory skipped, with zero Release warnings.

The companion server reports final commit d66cc86a5a8b64b46167b3d022d6ade50d596388.
The Windows ZIP SHA256 was independently checked as
`a7231ce81a74a85aa0d7e3125464209c757958ad73382600cd02b1aedd68e0c1`.
The extracted ZIP passed the temporary-storage backend lifecycle case. This is
package API/startup evidence, not real-client room or gameplay acceptance.
The server maintainer identifies login, plaza and ordinary room waiting as available
CN2013 paths; `browser_only` remains enabled, while quick matching, tutorial entry
and NPC waves remain disabled. Do not remove these gates in Launcher or infer
complete battle acceptance from the new packet tables.

All four opt-in cases then passed against the complete `windows-d66cc86a/server`
distribution (2 minutes 54 seconds): temporary storage, durable SQLite, full HTTP
and AList-fixture provisioning. They cover account/password/tickets, directory
join and withdrawal, owned/advertised addresses, clean shutdown and released ports,
SQLite retention across restart and package replacement, independent profile saves
and preservation when deleting a hosting profile. This run did not open a Launcher
window or retail client, and therefore leaves desktop and gameplay acceptance open.

## Execution note: honest offline Hook verification (2026-10-09)

The offline verifier no longer reports success when nothing applicable was checked.
It requires an image, separates checked patches from deferred runtime locators/native
entries, and returns incomplete status 2 when a runtime check remains or no patches
apply. Proven byte/pattern failures retain status 1. Mixed offline/runtime plugins
cannot inherit success from their pattern checks. Nine Python cases cover matching,
duplicate sites, mismatched bytes, unavailable builds, no image, native-only and mixed
native/locator cases. The real kr-812 unpacked image confirms the existing font gate
at VA 0x00AE3EE7 with expected bytes 75 03 and a unique match; it returns 0. The cn-406
image correctly returns 2 because that plugin has no cn-406 variant. No compatibility
selector was expanded and no game process was opened; runtime acceptance remains H8.

## Execution note: interrupted client recovery (2026-10-09)

Clients now expose an explicit recovery button only for inactive clients with a
leftover native staging directory or launch session. The existing animated dialog
asks the user to confirm the game is closed. The backend acquires the instance
lease, inspects matching executable paths and refuses running/uninspectable games.
Session schema/instance/game/build are validated without returning credentials or
parser excerpts. Hash-based native restoration must finish before session deletion;
diagnostic logs remain. Invalid session directories, identity conflicts, malformed
documents and externally changed DLLs preserve recovery materials. An unfinished
native overlay cannot be cloned/imported; session-only copies still omit secrets.

Five new managed cases cover confirmation/lease fencing, identity/malformed-session
preservation, invalid directory shape, interrupted overlay restoration and a real
untracked child process. Release builds warning-free and the default suite passes
295 cases with one optional external-package theory skipped. Frontend type checking
and production build pass. Headless light/dark fixture checks cover cancel with no
recovery request, explicit confirmation parameters, list refresh and removal of the
recovery action after success; screenshots were inspected. Retail crash recovery
and native desktop interaction remain additional acceptance, not claimed here.

## Execution note: real AList publication dependency (2026-10-09)

The configured public AList catalog was checked and remains a 97-byte empty catalog.
Administrator API authentication succeeds. A clean final d66cc86a server publication
was generated locally: 376 files, 285374478 bytes, manifest
`ad7ebeba45280c08607748b00df1e129595a39bdf2d4fc2d1e44ee2b40142263`.
The new API uploader verifies the referenced graph, anonymous content hashes and
catalog ordering, omits unrelated files, and detects changed catalogs before replacing
them. Four graph tests and the eight existing publisher cases pass.

Actual upload failed before the first object was accepted: the selected cloud storage
provider returned a date-parsing error for its upload timestamp (API code 500).
The publication catalog was not replaced. This is a writable-driver dependency,
not missing Launcher authentication or a successful official package publication.
Fix the provider or provision a dedicated writable publication mount, then rerun
the same graph and real-source install verification. No Launcher/game was opened.

## Execution note: writable cloud publication and real-source acceptance path (2026-10-09)

The dedicated publication mount's stream upload setting reproduced the cloud timestamp
parse failure. Its documented old upload method was tried instead and returned success;
anonymous SHA256 verification passed for that actual object. Only that dedicated mount
was changed; unrelated mounts were preserved. Complete publication is still in progress,
so no populated official catalog or real-source installation acceptance is claimed yet.
A two-worker attempt terminated with a network error before catalog publication; the
single-worker continuation reuses independently verified objects. Inspect its live
process/session before retrying, and wait for terminal status plus the final public
catalog hash before marking publication done.

The uploader now supports 1–4 bounded workers, content-before-manifest phases and safe
network failure classification without signed download URLs. Fifteen publisher tests
pass, including refusal to publish after object failure or a concurrent catalog edit.
An opt-in anonymous real-AList installation/SQLite/ticket/restart case is connected to
the existing full-distribution harness and reads FT_TEST_ALIST_URL; it must run only
after publication. The default Release suite passes 295 cases with two opt-in checks
skipped, zero warnings/errors. Retail Launcher/game windows were not opened.

## Execution note: bounded network retry and actual published desktop (2026-10-09)

The prior single-worker upload terminated with HTTP 502 before catalog publication.
A new continuation retries transient transport failures at most four attempts with
fresh metadata and bounded backoff, while 403 and other permanent failures remain
immediate errors. AList's known missing-object reply is distinguished from unrelated
provider failures. Repeating catalog publication first checks whether the exact
expected bytes already committed, then preserves the original digest fence. Store
and launcher indexes share the verified upload graph. All 22 Python publisher cases
pass. The current server upload is live and progressing; inspect the same process
handle before any retry and do not claim a populated official catalog yet.

A complete framework-dependent win-x64 desktop distribution was published (about
64 MB). Both launcher and updater PE machines are 0x8664. A separate candidate
launcher file index was generated and all 121 unique object hashes verified locally.
Using the Windows computer-use runtime, the actual isolated published executable
displayed Home and its video, opened the WinUI close dialog, canceled back to Home,
then exited through End. Window disappearance and process exit were independently
confirmed. This used a unique data root and launched no game. Actual desktop
replacement/health/rollback and remote candidate publication remain open; normal
published startup is not substituted for updater acceptance.

## Execution note: diagnosable minidump failures (2026-10-09)

Diagnostic mode 1.1.2 writes a separately bounded 1 KiB dump outcome file.
It distinguishes initialization, temporary-file creation, byte-limit rejection,
dump writing, flushing and final publication. A successful outcome is written
only after publication succeeds; publication failure retains the temporary dump
and preserves an existing final file. No account, ticket or file path is added
to the outcome records. The feature remains opt-in and observes first-chance
exceptions; it does not claim that every observation is an unhandled crash.

Release native builds pass `/W4 /WX`. The writer suite passes 35 checks covering
success, write failure, size rejection, existing final/temporary files and the
status cap. Runtime/host suites pass 85/18 checks, and native process scenarios
pass 101 parent checks. Both id-400/cn-406 controlled fatal-process tests retain
parseable dumps, successful outcome records, receive traces and restored DLLs.
The managed solution builds with zero warnings/errors and 314 cases pass with
two external-package checks skipped. Hook test setup now requires packaged
DLL hashes to match the latest native build, preventing stale binary acceptance.
No retail game or launcher window was opened.

TODO: Independent percent-symbol presentation is deferred pending maintainer-provided artwork or direction. The existing gauge decoration is not acceptance of this feature.

Retail 0.2.0 initially failed locator validation, then loaded on a fresh launch. Exact first-run field remains unobserved. Locator diagnostics now distinguish image fingerprints from a not-yet-ready maximum-health entry; the latter waits within the existing 30-second patch deadline. Visible yellow-digit acceptance remains pending.

The follow-up live launch logged both optional plugins installed and reached the visible lobby. Native synthetic rendering now passes 58 checks, including a vtable-not-ready retry followed by valid activation. Version 0.2.1 records the retry/diagnostic fix; yellow appearance in a match is still unverified.

## Execution note: retail yellow health visibility and residency fallback (2026-10-09)

The diagnostic retail match executed the yellow draw at anchor (860,755),
position (964,654), viewport 1024x768 and full clipping rectangle. Generated
vertices carried alpha 0xE5 and valid 256x32 texture dimensions for a 222x28
image. The maintainer and a screenshot confirmed yellow 100 above temporary
white 100 after a window change; immediate visibility was not established.
The game later reported a server disconnect, not a confirmed hook crash.
The run stopped and verified original DLL restoration and session deletion.

Version 0.2.2 removes the comparison and inspects native GPU texture residency
after normal lazy drawing, preserving white digits while pending. This guard
is not claimed as the proven cause of the delay. Synthetic tests cover pending
fallback, resumed yellow drawing and bounded logging. Independent percent
artwork remains deferred; initial visibility and lifecycle acceptance stay open.

## Execution note: yellow health regression and white-path restoration (2026-10-09)

Retail 0.2.2 and the direct-glyph experiment stayed invisible despite valid
vertices, a resident texture and non-error draw returns. Moving drawing after
the HUD pass also failed visibility; the maintainer reported brief torn imagery,
whose cause is not established. Both experiments were stopped and original DLL
restoration/session cleanup passed. Experimental source is retained privately
under ignored artifacts for analysis, not active deployment.

Version 0.2.5 restores the original slot-5 white path and (80,-30) offsets while
retaining bounded unpacking retries and one first-draw diagnostic. The original
path previously passed full-health/self-damage acceptance; fresh acceptance of
this restored package is pending. Its 33 synthetic checks pass. Yellow styling
and independent percent presentation remain open, not marked complete.

The restored 0.2.5 retail run initially remained invisible, then the maintainer
reported white digits appearing after a wait. A screenshot confirmed white 71
in the HP gauge. First-draw coordinates were (940,725), opacity 0.8, scale 0.65.
Thus the delay also affects the original white path; yellow texture residency
alone cannot explain it. The screen later showed a server disconnect. The next
investigation must distinguish HUD branch activation from invisible submission;
no root cause or immediate-visibility fix is claimed. Thirteen managed plugin
cases pass alongside the 33 native checks.

## Execution note: bounded health timing observations (2026-10-09)

Version 0.2.6 adds opt-in, default-off timing snapshots at seven fixed checkpoints
per process. They distinguish time before first intercepted life draw from ongoing
submissions, include bounded call counts/validation/HP/placement, and coalesce missed
checkpoints instead of emitting catch-up bursts. No additional render hook, texture
loading or HUD reordering is introduced. Native checkpoint tests cover disabled
mode, interval edges, stalls and exhaustion; managed manifest tests cover defaults
and opt-in resolution. Retail timing acceptance remains pending; no new retail
process was started in this increment. Yellow styling and initial visibility remain
open, and public-key provisioning remains excluded by maintainer direction.

## Execution note: portable external updater distribution (2026-10-09)

The desktop publish target now publishes the updater with the host RuntimeIdentifier
and SelfContained setting instead of copying framework-dependent build outputs.
The update reader also requires the helper executable, matching the publisher's
complete-package contract. A no-window verifier checks matching PE architecture,
bundled runtime dependencies and detached execution with external runtime lookup
disabled. The real self-contained win-x64 package passed that verification. This
proves detached helper startup, not desktop update/health acceptance. No launcher
or game window was opened. Public-key/signature provisioning remains excluded by
maintainer instruction; source-bound generated indexes and hashes remain in use.

Verification: Release build completed with zero warnings/errors; all 33 launcher
update cases passed, then the full default suite passed 328 cases with two opt-in
external-package cases skipped. The portable verifier also passed after the final
publish-target path change. Only win-x64 portable execution was verified here;
win-x86 and win-arm64 remain separate distribution acceptance.

## Execution note: updater build-output boundaries (2026-10-09)

Reusing build folders after portable cross-publication exposed a recursive copy:
ordinary net10.0 helper staging included a prior win-x64 output tree (211 files,
83514784 bytes). The build target now excludes known RID and publish directories
without deleting existing user/build outputs. Its selected copy items are exactly
12 current helper files, with no prior RID tree. A fresh framework-dependent x64
publication contains 16 helper files (1756111 bytes). The verifier now checks both
host/helper runtime modes and refuses nested build distributions. Detached startup
passed for both self-contained and framework-dependent x64 packages. This does not
claim desktop replacement acceptance or new public feed publication.

## Execution note: desktop update health rollback and retry suppression (2026-10-09)

An isolated framework-dependent win-x64 Photino distribution used a loopback HTTP
feed for actual download, confirmation, external replacement and frontend health
acceptance. Revision 100 entered Home and committed. Revisions 101 and 102 deliberately
omitted frontend readiness; the 90-second health deadline restored the old launcher.
For the patched revision-102 run, all 144 baseline file hashes matched after rollback,
the added payload was absent, and the transaction journal was removed. No game was
launched. This does not establish public AList feed or other-platform acceptance.

The helper publishes a source/index-bound rollback outcome before reopening the old
process. Restoring the pending transfer now reports startup failure instead of making
the same candidate ready again. Tests cover automatic download across restart, direct
retry/download refusal, cancellation/clearing, acceptance of a newer revision, and
unrelated or legacy outcomes. The original implementation scoped failure to the
retained outcome; the later offline-repair rejection note records the additional fence.

Home total progress uses the full width without a current file and animates to two
columns when a current file appears. Shared update actions place Open downloads
before secondary actions and keep accent actions at the right; a keyed transition
group animates entrance, removal and movement. Frontend types/build pass. The Release
managed build has zero warnings/errors; the full suite passes 335 tests with two
environment-gated skips. Public launcher publication and real client gameplay remain open.

## Execution note: real AList publication preparation (2026-10-09)

A clean self-contained win-x64 0.1.4 distribution contains a normal frontend and
a matching detached updater. Detached execution passes without a system runtime.
Its generated revision-1791558578 feed contains 506 files totaling 224699933 bytes;
content addressing deduplicates this into 307 upload objects totaling 141388038 bytes.
The candidate remains isolated under ignored artifacts until remote verification.

Actual source metadata revealed that absent nested directories return code 500
with repeated directory-lookup wrappers around `object not found`. The uploader
now recognizes only those known wrappers, preserving failures for unrelated errors.
It also retries non-JSON API responses and transient authentication transport
failures within the existing four-attempt bound, with sanitized failure types.
All 28 publication-tool tests pass. Remote attempts encountered transient transport
and API JSON failures; the public launcher index has not yet been accepted as
published. Contents are verified anonymously before the index is written last.

A new environment-gated integration test uses the actual AList adapter and update
service to download and hash every file, then restore readiness with an unavailable
network handler. It does not launch the application or game and is not claimed as
live acceptance until run against the published feed. Release build has zero
warnings/errors; normal full-suite verification passes 335 tests with three gated
skips. The paired update guides document how to run this acceptance independently.

## Execution note: offline repair preserves failed-release rejection (2026-10-09)

Successful rollback now writes one bounded source/index-bound rejection record
before restarting the restored process, independently of the helper's outcome.
Offline repair can replace its outcome with `repaired` or `no-recovery-needed`
without enabling the same failed candidate. Older rollback outcomes remain readable.
The latest rejection is retained; this does not create an unbounded history.

Tests cover both repair outcomes, a newer downloadable release, unrelated source
and index records, legacy/unbound records and non-rejection statuses. The actual
external-helper rollback test checks that the record is written, and both committed
and uncommitted offline process repairs preserve it across a second no-op repair.
Release build has zero warnings/errors; the complete suite passes 341 tests with
three environment-gated skips. The final two repair process checks also pass.
The in-flight remote upload uses the previously built candidate; this fence will
enter the subsequent refreshed package rather than changing published objects.

## Execution note: account UI request ownership (2026-10-09)

Account-menu refresh now retains its server and request generation. A server
switch, closing the menu or unmounting invalidates the request before it can open
or focus the flyout or report an error. Synchronous invalidation also covers
switching away and back within one Vue render cycle.

The authentication dialog clears secrets on server changes and fences login,
registration and account-refresh completions against its opening generation.
An old response cannot close a newly reopened dialog, display an old server's
error or replace a registration notice after the selection changes. Backend
sessions retain their existing server ownership; this is a UI completion fence,
not remote login cancellation.

Four tests compile the actual Vue component scripts and drive deferred bridge
responses through real Vue reactivity, covering server-switch-back, stale errors,
reopened dialogs and secret clearing. All 13 frontend behavior tests pass alongside
type checking and the production build. No desktop/game process was started for
these checks. The AList upload session remains live but has not accepted its feed;
existing public catalog metadata and raw download independently returned HTTP 200.

## Execution note: rejoined-room isolation and publication queue bounds (2026-10-09)

Every transient room join now has a generation retained across membership refresh.
An old directory response cannot remove a subsequently rejoined room or invalidate
its sessions, including disconnect-and-rejoin with identical source/address/build.
Two delayed-response tests cover direct replacement and source disconnection.
All 13 directory tests pass; Release build has zero warnings/errors and the complete
managed suite passes 343 tests with three environment-gated skips.

The prior AList upload terminated with ReadTimeout and did not accept its mutable
index. Inspection found that executor map had queued all objects; an early failure
still waited for queued uploads during executor shutdown. Publication now submits
at most one worker-sized batch, so failure cannot leave the entire remaining graph
running. A twelve-object failure test proves at most two writes with two workers
and no index write. All 29 publication tests pass. Existing catalog metadata and
raw bytes remain readable, and both mounted storage drivers report working state;
this does not establish successful new publication. A new bounded upload attempt
reuses existing verified content without modifying the generated candidate.

## Execution note: actual local-server multi-account bridge acceptance (2026-10-09)

All four complete d66cc86a package cases passed again: direct temporary storage,
direct SQLite storage, HTTP provisioning and AList-protocol-fixture provisioning.
The expanded cases register two accounts through the real API and Launcher bridge,
verify the selected second account and two-session list, select/refresh the first,
then log out only the second while retaining the first. Owned-server restart clears
the launcher session list; existing durable-account, password, ticket, independent
save and package-replacement checks still pass. The expanded run took 4m09s, with
its receipt at artifacts/alist-live-acceptance/multi-account-hosting-recheck.trx.
No launcher window or retail game was started.

An initial rebuild while the previous 4m15s package run held the test DLLs failed
on Windows copy locks. After that owned run exited successfully, the sequential
Release build passed with zero warnings/errors. Nine targeted multi-account cases
also pass. This is actual local API/bridge acceptance, not remote-player gameplay.
The paired hosting guides now distinguish accepted public server provisioning from
the remaining desktop provisioning and durable-gameplay checks. Launcher feed
publication remains in progress, with 66/307 objects verified at the latest poll.

## Execution note: diagnostic process lifecycle across compatibility IDs (2026-10-09)

The real native process fixture now exercises all nine currently declared diagnostic
compatibility IDs: kr-812, cn-279, cn-324, cn-406, cn-706, cn-717, id-400, id-1809
and kr-1256. Each loads the packaged runtime and diagnostic DLL, receives loopback
data, raises an access violation and verifies a bounded parseable minidump exception
stream, successful dump status and receive trace. Completion restores the original
messenger library and removes the session file. All nine targeted cases pass.

These are controlled native executable fixtures carrying build/session identities,
not nine retail-client acceptance runs. Per-client import paths, handled exceptions
and graphics lifecycles remain separate evidence. The paired diagnostic guides state
this boundary explicitly. Release build has zero warnings/errors; the full suite
passes 350 tests with three environment-gated skips. The server chat has received a
coordination update requesting its native KR2015 relay/scene readiness before another
ESC exit acceptance; server implementation remains in that chat. Official launcher
AList upload continues, with 93/307 objects verified at the latest poll.

## Execution note: single-row update actions (2026-10-10)

Home update actions retain the order Open downloads, Cancel, then the accent
Check updates or Restart action. The shared animated button group no longer wraps
its primary action onto a separate row. Frontend type checking and production
build pass; the Release solution build has zero warnings and zero errors.

## Execution note: shared native stop requests (2026-10-10)

Repeated client stop requests and application-wide exit share one operation for
the exact owned invocation. Two native-process cases verify graceful exit, DLL
restoration, session deletion and lease release. The full managed suite passes
352 tests with three environment-gated skips; Release builds without warnings.

## Execution note: source-configured portable distributions (2026-10-10)

The distribution entry point builds the frontend and portable host, verifies the
detached updater, and generates an index and matching bootstrap source policy.
The official AList default remains configurable; HTTP uses the same pipeline.
Initial minimum revision equals the generated release revision, so a fresh build
cannot offer an older feed. Existing output directories and unsafe source inputs
are rejected before packaging. The policy remains excluded from update payloads.

An actual win-x64 run generated revision 1791561836 with 506 indexed files.
All package/object pairs match their recorded size and SHA-256, and detached
self-contained updater execution passes. Credential-bearing URLs, unsafe prefixes
and existing destinations were rejected. The prior remote upload remains live;
this local packaging result does not establish publication or real-source staging.

## Execution note: bounded AList file resolution (2026-10-10)

Each AList resolution attempt now bounds response headers and metadata body reads
to thirty seconds even with the infinite-timeout file-transfer HTTP client.
Transient timeouts retain the existing four-attempt retry limit; caller cancellation
still stops without retry. A concurrent stalled-header/stalled-body test uses the
real deadline and verifies recovery through a second request in both cases.
Release build has zero warnings/errors; the full suite passes 353 tests with
three environment-gated skips. The live official upload last verified 240/307
objects; no production index or live-source staging acceptance is claimed.

## Execution note: healthy desktop release after rollback (2026-10-10)

The actual isolated v3 launcher accepted loopback HTTP revision 103 after its
rejected revision 102. A UI pause at 43 percent persisted paused intent, and resume
completed staging. Confirmed restart replaced parent 26488 with process 29508;
the new Home rendered and the external helper recorded completed. All 506 files
(224702406 indexed bytes) match their size/SHA-256, the source policy is preserved,
and the journal is absent. Receipt: artifacts/desktop-update-acceptance-v3/healthy-103-receipt.json.
The accepted index SHA-256 is cde503034009ef92a063ec23a0845ff120610361ee459833a8819fc37453cddc.

The prior official AList upload ended on exhausted HTTP 502 retries without
accepting its index. A fresh attempt uses the latest portable candidate, revision
1791562273, and can reuse previously verified immutable objects. This is still
separate from anonymous real-source staging and desktop acceptance.

## Execution note: anonymous published plugin installation (2026-10-10)

The real public AList test downloaded and installed numeric-health 0.2.7 (three
verified files) and mid-match-exit 0.1.2 (one verified file) without administrator
credentials. It validates manifest pinning, every staged hash, immutable library
commit, source receipt, idempotent repeat installation and white/yellow/all-modes
options. No plugin is enabled and no native code or retail game is executed.
The real-source test passes in 25 seconds; receipt: artifacts/alist-live-acceptance/public-plugins.trx.
Release build has zero warnings/errors; full suite passes 353 tests with four
environment-gated skips. Paired source guides remove obsolete signature, archive
and plugin-discovery claims while retaining the actual pending background checks.

## Execution note: publication proxy-path diagnosis (2026-10-10)

The environment routes HTTP/HTTPS requests through a local proxy. A read-only
probe returned HTTP 502 through that proxy and HTTP/API 200 when directly
querying the same AList catalog metadata endpoint. This identifies a concrete
transport-path failure, not proof that all storage provider requests are healthy.
The previous live upload ended in ReadTimeout without accepting its index.
A new task-scoped direct upload reuses the same immutable candidate.

The uploader now accepts an explicit --no-proxy option for its process only;
default behavior still respects environment proxies. A test verifies both routes
without changing publication ordering or checks. All 30 publication-tool tests
pass. The new live upload last verified 74/307 objects, with transient direct
connection retries; official launcher publication remains unaccepted.

## IP directory transport acceptance (2026-10-10)

DirectoryClient and LauncherApiClient share a bounded HTTP transport factory.
Plain HTTP IP authorities bypass the configured proxy; domain HTTP and all HTTPS
retain it. The URI still comes from the effective server/local policy, redirects
remain disabled, and no system proxy setting is changed. A failing local proxy
regression verifies credential-free domain discovery, server-directed IP fallback,
and a successful directory listing. IPv4, IPv6, domain and HTTPS proxy selection
are covered separately; the focused directory/server suite passes 31 cases.

Read-only production-client checks against the configured directory domain and
its resolved IP both returned schema 1, region CN, host policy ip and zero entries.
This resolves the observed proxy-path failure, not remote-room gameplay acceptance.
The running default desktop host has not yet been rebuilt for this change.
Full regression for this checkpoint: 430 passed, five environment-gated skips;
Release solution build has zero warnings and zero errors. No retail game ran.

## Desktop directory and ESC configuration acceptance (2026-10-10)

The default desktop host was rebuilt after confirming no Launcher process remained,
then opened through Computer Use. The configured directory completed with the normal
empty-list state and no HTTP 502. ESC 0.1.2 all_modes was saved true, reopened and
read back, then restored false; disk verification confirms enabled=false and
all_modes=false. No retail game ran. Checking official plugin updates completed
without reporting a new version; actual upgrade transfer remains unaccepted.

The plugin download-completion watcher now captures its context revision before
refreshing the library. Switching client/server or unmounting suppresses a later
update check and stale refresh errors. Five actual Vue setup regressions cover
those transitions and the unchanged-context success path. All 18 frontend plugin
tests, type checking and bundling pass; default Release build has zero warnings/errors.
The default host was normally exited, rebuilt and reopened with this frontend.

## Account operation failure ownership (2026-10-10)

AccountsPage now suppresses a delayed profile-refresh or logout failure after
client context changes or page disposal. Current failures still reach the normal
error UI, and logout cleanup still refreshes the retained account groups. Six new
actual Vue setup cases verify stale server-away-and-back failures, unmounts and
current errors for both operations. All 24 account frontend cases pass, together
with type checking/bundling; default solution Release build has zero warnings/errors.
The default host was normally exited and rebuilt. This is failure-path verification,
not acceptance of desktop multi-account login or real gameplay.

## Plugin upgrade option and rollback preservation (2026-10-10)

The four existing real-loopback bridge/queue upgrade scenarios now carry distinct
font_color settings for two clients. HTTP/AList and file/ZIP installation all verify
that selecting the downloaded release preserves the first client's yellow setting,
leaves the other client pinned to white on the original release, retains options
when disabled, and preserves them when rolling back. Fresh InstanceStore reads
verify persisted state; hashes of every original package file remain unchanged.
All four cases pass without skips. This is transport/bridge acceptance with a
controlled source, not public-store desktop upgrade or native game execution.

## Ready self-update cancellation state (2026-10-10)

A regression reproduced cancellation leaving discovery status ready after the
task became canceled. ActionAsync now makes ordinary cancellation available and
pause downloading, while preserving failed status for rejected startup candidates.
ReadyDirectory rejects both interrupted tasks. Verification covers offline reuse
of verified objects after cancellation, canceled intent not resuming on startup,
and rollback candidates remaining ineligible without a newer check. The self-update
suite passes 49 cases with one public-source gate skipped; full managed regression
passes 431 with five gates skipped. Isolated and default Release builds have zero
warnings/errors. The default output was rebuilt after confirming no running host
and reopened through Computer Use. No retail game ran.

## Canceled releases stay excluded from automatic staging (2026-10-10)

The update monitor now retains the canceled manifest identity across restart and
checks it inside the serialized download gate. Automatic staging skips that exact
selection; explicit manual download remains allowed, and a newer manifest stages
normally. Starting a worker clears the old exclusion. A regression verifies an
online restart with auto-check/download enabled, manual retry, and automatic staging
of the next revision. Self-update suite: 50 passed, one public-source gate skipped;
isolated/default Release builds have zero warnings/errors. Default host was normally
exited and rebuilt. No retail game ran. During desktop observation the directory
request timed out with 'The operation was canceled'; prior successful connectivity
is not proof of uninterrupted current endpoint availability.

## Numeric-health native placement and percent candidate (2026-10-10)

The maintainer accepted the restored Indonesian HP-number placement in a kr-812
retail match. Native/custom placement is a plugin option; white remains the default.
Candidate 0.2.9-rc.1 adds the supplied unchanged 20x18 PNG and a generated 40-byte
single-frame SMF, loaded by absolute plugin path without replacing client files.
The original formatting wrapper receives an escaped literal percent and the
native renderer measures the digits before placing the separate symbol.
Bounded activation and draw-time checks reject invalid assets and sprite layouts.

The ARM64 retail run confirmed a valid resident texture but no visible percent;
architecture is not an established cause. The owned game was stopped through its
test driver and original DLL/session restoration passed. Later launch attempts
failed at the real API/DNS transport before game creation. The latest candidate
adds measured symbol/digit sizes and final coordinates to bounded diagnostics;
that additional record has not yet been exercised in retail.

Native synthetic coverage passes 90 checks. Focused managed plugin coverage passes
63 tests with one environment-gated public-source skip; Release builds have zero
warnings/errors. A production PluginLibrary ZIP import validates all five files,
their hashes, original artwork and resolved options without native execution.
The portable 0.1.5-preview.1 package includes the self-contained x64 host and
updater. PE checks verify x64 host/runtime/native dependencies and x86 game Hook;
ZIP integrity passes. Cross-PC setup is documented in paired user guides.
Public publication and x64 hardware retail visibility require separate receipts.

The public plugin catalog now includes 0.2.9-rc.1 after anonymous verification
of all five objects and its immutable snapshot; existing entries were preserved.
Code checkpoint 1b3cf53 is pushed. Publishing the separate x64 preview feed failed
during object verification, so its index was not committed. The stable feed is
unchanged. Portable ZIPs are copied to the maintainer's download directory with
matching SHA-256 values for direct transfer to the x64 PC. The separate remote ZIP
upload is still in progress and is not yet accepted as a downloadable release.

## Legacy numeric-health descriptor research (2026-10-10)

CN 2011 through CN 2019 now have corpus-backed client identity rows, while the
CN 2011-2014 subset has candidate HUD render wrappers and normalization accessors recorded in
`docs/hook/legacy-numeric-health-research.md`. CN 2011 was not omitted: its
`sub_716880` path calls `sub_53AD30` at `0x7175D0` and uses a different
register/object boundary from KR 2015. The other rows likewise use different
wrappers and vtable slots. They remain out of the published compatibility list
until a descriptor-driven locator and controlled render trace prove the selected
player and anchor; the Indonesian build remains excluded because it already has
the native display.

The first descriptor-driven native loader is now in place. `client-versions.toml`
ships beside the numeric-health DLL; it validates the selected build's timestamp,
image size, fixed base, render call, health entry points and HUD anchor before
staging the patch. The CN rows are deliberately research-only and are rejected
at activation, while KR 2015 uses the same external data path as its previously
accepted implementation. CN 2014 resource releases remain under their measured
external identities, and the launcher `cn-706` row now carries the later
`0.2.51.002` timestamp instead of being conflated with the earlier `0.2.48.001`
research image. The native descriptor/parser and rendering tests pass 102 checks.
The offline verifier also checks the measured CN2011--CN2014 call bytes and PE
identity while keeping every row deferred until a controlled runtime trace.

## Native descriptor capture and vtable selection (2026-10-10)

The numeric-health drawing path now consumes the descriptor's maximum-vtable
byte offset instead of fixing it at 356. Supported rows require a four-byte-
aligned value in 4..4096, checked by both native loading and offline publication
verification. The previously parsed hud_owner capture now reads the HUD's first
pointer; caller_edi retains its original path and neither silently falls back.
Actual x86 stub tests distinguish the branches with invalid EDI and a non-baseline
slot, preserving the original draw when ownership validation fails. Numeric-health
coverage passes 136 checks; native runtime/messenger/integration/dump checks pass
85/26/153/35. Native Release|x86 builds have no warnings or errors, and 15 offline
verifier tests pass. Later CN2014 0.2.50.004 and cn-706 call sites also pass their
PE/byte/target checks. No research descriptor is enabled; other HUD-register,
health-accessor and resource ABIs still require proof and retail acceptance.

## Descriptor-selected HUD register forwarding (2026-10-10)

Supported numeric-health rows now require hud_register to be ebp or ebx. Native
patch staging uses the corresponding forwarding stub; research rows remain
ineligible. Controlled x86 invocation keeps EBP deliberately invalid on the EBX
path, verifies all seven original draw arguments and checks preservation of EBP,
EBX, ESI and EDI. Both player-capture modes work on this path; invalid owners
preserve the original drawing without an EDI fallback, and caller_edi does not
consume a deliberately invalid HUD owner. Native loading and offline validation
reject missing, mistyped and unsupported register values. Numeric-health checks
pass 167; the native solution Release|x86 build has zero warnings/errors, and
the other runtime/messenger/integration/dump checks pass 85/26/153/35. All 15
offline verifier tests pass. This supplies the CN2011 register boundary, not
its unproven accessor/resource ABI or retail compatibility.

## Build-declared plugin recommendations (2026-10-11)

Completed the per-build optional recommendation path. ID2014, ID2015 and KR2015 declare the localized font-weight plugin; each other build remains silent. The dialog is shown after a new client is ready or once before the first launch of an existing client with a missing or disabled recommendation. Installed and enabled plugins are suppressed, and an installed disabled plugin is enabled without a duplicate download. Confirmed store tasks persist a selection stamp and activate only after verified installation. Core recommendation, restart, stale-selection and publisher renewal tests pass. Real retail ID2014 launch acceptance remains a user-controlled step.

## Mutable archive publication change detection (2026-10-11)

The publisher uses provider size/MD5 only to detect concurrent edits to the old mutable portable ZIP. Missing provider digests fall back to complete SHA-256 reads. Newly uploaded archives still require complete anonymous size/SHA-256 verification before either update index changes. Interrupted uploads can be accepted on retry only after that same full verification. Publisher tests: 54 passed. Release 0.1.9 remains pending remote acceptance.

## Desktop recommendation and installed-client identity acceptance (2026-10-11)

The self-contained 0.1.9 x64 distribution opened normally through Computer Use.
Adding the existing ID2014 installation identified CrazyShooter.exe as id-400,
used its default localized name and requested confirmation for the nonempty path.
Its recommendation dialog selected Font weight by default; confirmation enabled
the already bundled 1.0.1 package, and saving its bold-weight option persisted the
selection. The launcher exited through the normal caption dialog. No game ran.
Separate read-only corpus cases verified original and unpacked executables for
CN2013, ID2014, ID2015, KR2015 and KR2026 through real host registration, retaining
the original executable hashes: five passed without skips. Full managed regression
passed 486 with five environment gates skipped; frontend plugin tests passed 27,
and Release builds had zero warnings/errors. Public update acceptance is separate.


## Home plugin prerequisites and native UI preview (2026-10-15)

Completed Home progress integration for required updates and recommendation downloads. Launch preparation is serialized in the view; verified completion resumes only the captured client/server/account context, and failures, cancellation and unmount suppress continuation. Eight frontend race checks, 33 existing plugin UI checks and eight store upgrade checks pass. Declared latest-only store components install a catalog-pinned snapshot instead of comparing bundled checkout bytes to published bytes; local imports still cannot override component policy, and modified installed snapshots remain rejected.

The explicit overlay preview maps an external resource directory, mounts the restored components through BfUiProvider and retains Shadow DOM styles. A CN2012 v1 component was visibly captured over the running client. Stable window geometry remains a startup heuristic, not proof of 3D readiness; render-ready gating and a distributable resource provider remain open acceptance work. Native game assets are not committed.

Validation note: the full managed run passed 556 and skipped five, with two failures in process recovery and the external updater fixture. A focused rerun of all four recovery/helper cases passed. Keep the parallel-run instability visible; it was not treated as a clean full-suite result. Release solution build completed with zero warnings/errors.

## Automatic overlay package entries (2026-10-15)

The launcher now registers enabled overlay HTML entries after its process-bound handshake, with a separate optional game UI preview package. External rendering no longer waits for native hook readiness, which is absent for some file-only client paths. Window stabilization is bounded, elevated-client input-idle failures do not block the external renderer, and failed handshakes terminate their renderer process. The local resource provider is staged only in ignored output. Build and definition checks are complete; automatic retail startup still requires loading this host binary after the currently running launcher and game exit. Genuine 3D-ready gating remains open.


## First-release room and hosting availability (2026-10-17)

- [x] Keep multiplayer server browsing, submissions and connections usable.
- [x] Preserve disabled room and local-hosting entries with a development notice.
- [x] Gate room joins/publications and local-server configuration/start/destination through reversible game definition switches; retain saved profiles and cleanup operations.
- [ ] Re-enable room multiplayer and local hosting after their future release acceptance.

Validation: 76 existing definition, directory and local-hosting cases passed; the new backend availability case passed after correcting its source-save request fixture. All 29 frontend navigation/hosting/multiplayer checks passed. Frontend type-check and production build passed. Final Release solution build: zero warnings and zero errors. No launcher UI or real game was started; no remote publication.


## Unified Auto Dash package (2026-10-20)

- [x] Merge the user-accepted CN2011 native timer into the generic Auto Dash identity.
- [x] Remove the CN2011 executable branch write; retain build-scoped patches elsewhere.
- [x] Keep KR1256 excluded and Forward Jump unpublished, preserving its research.
- [x] Include the native implementation and tests in the Release solution and packaging.
- [x] Publish the verified generic 0.2.0 package and retire the separate preview catalog entry.

The native timer gameplay acceptance was supplied by the maintainer. Publication
and anonymous verification are recorded in the receipt above.

## Server detail presentation refinement (2026-10-22)

- [x] Preserve the accepted frontend in an ignored checkpoint before refinement.
- [x] Keep the same detail dialog dimensions for servers with and without images.
- [x] Use a server identity panel for missing artwork and omit the repeated game name.
- [x] Move supported versions to a dedicated navigation tab with readable names,
  compact rows, preset actions and synchronized client selection.
- [x] Hide disabled roster entry points, retain searchable rank-aware player tiles,
  and separate image enlargement from the detail dialog.
- [x] Align directory actions, open details from the card body and bound the
  recommended plugin source presentation.

Validation: frontend type-check and production build passed; 18 multiplayer
checks and eight navigation checks passed. Browser screenshots cover artwork,
missing artwork, version selection, plugin policy and the player list. Keyboard
image navigation and returning from the lightbox were checked in the browser.
The checkpoint ZIP was read successfully (175 entries). The browser viewport
override did not change the actual 1280-by-900 viewport, so smaller viewport
visual acceptance remains pending. This is frontend preview acceptance only;
no game, room publication, launcher release or repository publication occurred.


## Directory motion and optional local server information (2026-10-22)

Implemented shared sliding directory indicators, page transitions, animated
content-sized setup dialogs and the changing primary action label. New room setup
separates room information from server configuration, with compact connection
settings. Adding or editing a server separates connection settings from optional
local description, icon and gallery fields. The bridge and TOML store retain
these fields across restarts; remote display values take priority when supplied.
Local fields cannot advertise an online roster or change admission policy.
All three server dialogs dismiss on backdrop clicks unless an operation locks them.

The toolbar groups tabs/search on the left and sources/settings/refresh on the
right, with submission beside the primary add-server action. A single keyed grid
orders the current destination before favorites and animates cross-row changes.
Explicit development preview now includes sixteen server cards and twelve rooms.
The maintainer accepted the sorting animation. Introduction scrolling uses an
alpha mask at both edges, proportional to remaining scroll distance, with no
painted overlay; reduced transparency and forced colors disable the mask.

Validation: frontend type-check and production build pass, with 22 multiplayer
and 11 hosting behavior cases. Thirty-five focused managed cases pass, including
local metadata round-trip/removal and invalid-artwork preservation. Release
solution build completed with zero warnings and errors before the final frontend
polish. Browser control timed out; native screenshot acceptance was stopped by
its URL validation guard. Final fade and resize effects still require rendered
acceptance. No release upload, repository push or game execution occurred.

## Imported local server artwork (2026-10-22)

Server information retains HTTPS fields and adds native file selection for one
icon or multiple gallery images. Imported raster images are copied into the local
data library under content-addressed names; only managed image references are
served, without exposing arbitrary filesystem paths. Mixed remote/local values
round-trip through TOML, while public catalog metadata retains its network-only
validation. Canceling or closing a chooser cannot overwrite a newer draft.

Release solution build passed with zero warnings/errors. All 57 focused managed
cases and 27 multiplayer frontend cases passed. An isolated desktop session
verified both native choosers, multiple image import, saving, the card icon,
detail thumbnails and centered image enlargement. A fresh self-contained x64
0.1.22 candidate (revision 1792685078) reopened the same isolated data library:
the saved card icon, both gallery images and editable references remained intact.
Its native icon chooser opened successfully; canceling preserved the draft.
All eight artwork backend cases and 27 multiplayer frontend cases pass. The
candidate package passes managed-version and detached-updater payload checks.
No public server was created and no files were uploaded.

## Portable archive index and file count limits (2026-10-22)

Removed the remaining 256 KiB index and 1,024-file ceilings in portable ZIP
creation. Empty indexes remain invalid. Each selected file still streams through
exact size and SHA-256 checks; mismatches remove the unfinished archive and an
existing destination is preserved. Six regressions cover both former ceilings,
late-file corruption and existing archive preservation.

Local acceptance used the existing 0.1.22 candidate: its 262,766-byte index selects
990 files and 388,530,050 bytes. ZIP creation succeeded, then an independent reader
rehashed every entry and checked the source policy. The resulting 208,334,496-byte
archive is recorded in an ignored acceptance receipt. This verifies that frozen
candidate's portable archive, not current-worktree packaging, remote publication
or desktop updater installation.

## Publication catalog command size parity (2026-10-22)

Removed the publication CLI's separate 4 MiB catalog ceiling so its validation
uses the same managed catalog reader as the launcher. Two process-level cases
first reproduced the ceiling, then verified acceptance beyond it and continued
rejection of an unsupported schema. The combined archive/CLI suite passes all
eight cases. The CLI build retains warnings-as-errors.

The integrated managed suite passes 876 cases with eight environment-gated cases
skipped; the launcher update subset passes 131 with its real AList case skipped.
An anonymous read of the official feed still selects 0.1.21, revision 1792579979,
with 964 files. This inspection does not exercise payload staging or installation.
The 0.1.22 archive acceptance above used a frozen package predating the latest
server UI changes; build a fresh candidate before publication, then verify older
reader staging and actual desktop handoff. Production signature provisioning and
private module/gameplay acceptance remain separate open gates.

## Shipped-reader index compatibility and desktop upgrade (2026-10-22)

The unmodified 0.1.21 reader rejected the first current-worktree 0.1.22 index:
262,766 bytes exceeded its 256 KiB parser ceiling. Generated file rows now omit
optional TOML whitespace, retaining every path, size and SHA-256. A representative
1,000-file regression first reproduced the failure, then passed. New readers
still accept larger indexes; publication adds no new size or file-count limit.
The upgrade verifier validates the index before copying a distribution and
reports malformed input without an unhandled exception or an unnecessary copy.

A fresh complete x64 candidate, revision 1792685966, has 990 indexed files and a
253,856-byte index. The original shipped 0.1.21 assemblies were exercised under
the x64 runtime; only the cloned test installation's primary source endpoint was
retargeted to a loopback AList-compatible fixture. Automatic discovery, verified
staging and offline ready-state restoration passed in 16.1 seconds. The transfer
contained 95 changed destinations represented by 73 unique content objects;
no unchanged content object was requested. The original distribution was not
modified.

Desktop acceptance then used that staged installation: confirmation closed the
old process, the external helper installed the candidate, and the restarted
Photino frontend painted successfully and acknowledged health. The helper outcome
is completed. Independent checks matched all 990 installed sizes and hashes,
confirmed source-policy preservation and removal of two retired frontend chunks,
and found no pending installation journal. The old process and helper exited.
The launcher update subset passes 132 cases with one environment-gated public
AList case skipped. Fresh packaging passes managed version, architecture and
detached updater checks.

The official AList incremental phase is now published. Anonymous canonical and
retained-preview indexes match the candidate. Publication reused 512 existing
content objects and uploaded 73 new objects totaling 5,558,646 bytes. Unmodified
shipped 0.1.21 binaries with the original source policy then staged the official
release in 91.4 seconds and restored ready state offline: each of the 73 required
objects was resolved and downloaded once, and no unchanged object was requested.
This official-source acceptance did not launch a GUI; actual desktop installation
was accepted separately against the identical index and payloads above. The
portable archive's second publication phase remains running, with its matching
fallback sidecars unaccepted until that phase completes. Production signature
provisioning and the wider gameplay/module gates remain open.

## AList upload progress and transport diagnostics (2026-10-22)

Large uploads now report file-read byte progress separately from waiting for
AList to respond. Completing the request is explicitly distinct from anonymous
integrity verification. The scoped reporter stops before the input stream closes.
Transport failures retain nested exception classes and integer OS error codes
without rendering request URLs, tokens or arbitrary exception messages.

Validation: all 62 uploader cases pass, including nested-secret redaction and
cyclic-exception traversal. A real loopback HTTP upload received and independently
hashed an 8 MiB payload, delayed its response, and produced the waiting-response
progress state. The reporter exited with no remaining progress thread. The
already-running production archive task predates this instrumentation; its
retries have not established a root cause or accepted the new remote archive.

## Shipped-reader archive fallback compatibility (2026-10-22)

A standalone verifier references the original 0.1.21 distribution's Core,
Downloads and SDK assemblies and checks their loaded SHA-256 identities against
the untouched package. All 20 in-memory HTTP/AList cases pass under the x64
runtime: incremental success requests no fallback metadata or archive; failed
incremental transfer retries before complete ZIP or split-ZIP fallback. Both
fallback successes verify every selected object and restore ready state offline.
The visible fallback task contains only one or two archive volumes. Wrong-release
metadata, corrupt transferred bytes, changed contents, missing or unexpected
files, duplicate paths and traversal paths are refused. Every case preserves the
installation and its source policy. The verifier builds with zero warnings and
errors and writes a local receipt; it starts no launcher, game or network service.
This accepts the shipped reader's protocol behavior, not public ZIP availability
or installation of fixture executable bytes.

The separate official complete-archive task ended unsuccessfully after its final
network retry. Anonymous metadata still identifies the previous 207,240,774-byte
ZIP. A new archive-only attempt uses scoped byte progress and sanitized nested
transport diagnostics. The successfully published incremental release remains
available; the matching full archive and fallback sidecars are still unaccepted.

## Optional AList background archive tasks (2026-10-22)

Large synchronous archive transfers repeatedly read the complete local payload,
then lost the connection while waiting for the server response. This establishes
the failing transport stage, not a storage-provider root cause. An opt-in
`--background-uploads` publication mode now follows the returned storage task
identity. Polling failures retain the same task without retransmission, and
small metadata stays synchronous. Only the succeeded state is accepted;
progress 100, failed/canceled tasks, wrong identities and expired deadlines are
refused. Local archive identity is checked before resuming. Complete anonymous
SHA-256 verification and feed concurrency checks still precede fallback metadata.
Task identities are process-local; a deadline does not cancel the remote task.

All 70 uploader cases pass, including a real loopback HTTP 8 MiB transfer whose
first status query disconnects: only one PUT is sent, subsequent status queries
reach storage success, and credentials do not appear in diagnostics. Deadline,
late-success and changed-source regressions pass. English and Chinese operator
guides describe the mode and its restart limits. Public background-task
acceptance remains pending; the existing synchronous publication attempt must
finish before another archive publication can begin.

## Current-package local hosting acceptance (2026-10-22)

An isolated source export of server revision 6e09728e published a Windows x64
framework-dependent package with zero warnings or errors. Ordinary source
publication does not provision client data: the first startup refused the absent
CN406 data directories. Provisioning the existing local CN406 archives and loose
resources repaired the test package without disabling client-data validation or
changing server source. Its 452 published code/resource files retain their build
hashes; 6,576 local client-data files have a separate private inventory.

Both actual-package direct-transport cases now pass against the current Launcher
worktree: temporary player storage and persistent local SQLite storage. They
exercise real registration, bad-password rejection, two account sessions,
selection, targeted logout, launch credentials with descriptive/language metadata,
room publication/join/login/withdrawal, owned endpoint selection, graceful stop
and port release. The local case additionally preserves accounts through restart
and package-directory replacement, isolates a second save, and retains saves when
profiles are removed. Server logs are checked for the test password and ticket.
No game process or remote player was involved; the current server package was
not published to a store. HTTP/AList provisioning of this current package and
rendered desktop/gameplay acceptance remain separate gates.

The integration fixture enables hosting/rooms only in its copied definitions,
handles asynchronous bridge events without racing reply lookup, and sends the
current build/language ticket fields. Its publication fixture no longer truncates
packages at 2,048 files. Release availability switches remain unchanged. Earlier
local-case attempts failed while copying a second package on a full data volume;
retained build/update fixtures were compressed without deletion before the final
successful run. This does not turn those earlier failures into passing results.

## Official complete archive publication accepted (2026-10-22)

The previous synchronous archive publisher terminated after all network retries.
A subsequent opt-in background submission sent the 208,347,184-byte ZIP once and
received a storage task identity. Task-status polling reported running with zero
progress until its wait budget expired. The outer publication retry then found
committed public bytes and streamed their complete SHA-256 successfully; it did
not retransmit the archive. The digest is
`96a1d2b97ee6d3258178886012d6a35f4b228a116cf94fe62dbaa32aef31e88c`.
Both canonical and preview archive companions were published only after that
verification. The publisher exited zero, and a separate anonymous verifier
confirmed both feeds and companions bind the exact 0.1.22 index hash
`5de9d7d1e1e1299dc8315f156dd16171b5c4d74faef249ece122ccea4e97f90f`.
The local public-archive receipt records revision 1792685966 and all 990 files.

All 71 uploader regressions pass, including acceptance of already committed
bytes after a task timeout without a second PUT. The paired operator guides now
distinguish a task timeout from cryptographic evidence that its bytes committed.
This accepts public complete-archive availability and matching fallback metadata;
it does not claim that task polling reached a succeeded state, a production
signer was provisioned, or a stock reader performed a fresh public archive
installation. Shipped-reader fallback fixtures and actual desktop incremental
replacement have their separate receipts.

## Durable transfer checkpoints and a hosting dependency (2026-10-22)

Per-file checking, downloading and verifying transitions now share the existing
two-second progress checkpoint. Serializing and durably flushing the entire
manifest at each file boundary made large inventories quadratic. Queue, install,
pause, failure and completion transitions still persist immediately; progress
callbacks continue to update in-memory snapshots on every report.

The test-project Release build has zero warnings/errors, and all 82 focused
download/store/plugin-update/client-update regressions pass with no skips.
The full current-server HTTP fixture previously timed out after verifying only
158 files. With the checkpoint change, all 7,028 files were downloaded and verified;
the first rerun then failed during installation because the data volume was full.
After ordinary NTFS compression of retained build copies recovered space without
deletion, the next run completed installation and compared every installed file's
full SHA-256 with the source. Its disk receipt retained at least 361 MB free on D.
The original inventory, integrity checks and installation deadline were preserved.

The end-to-end HTTP case remains failed at first server startup, independently of
the successful installation. The server loader rejects an existing dependency when
the .NET resolver returns its Windows extended path while the directory prefix has
ordinary syntax. An independent reproduction with identical DLL bytes accepts the
201-character main path and rejects the 265-character main path under the server's
containment predicate. Consistent path normalization belongs in that loader while
retaining its directory boundary. No server source changed. Current-package AList
provisioning and full HTTP hosting are still separate unaccepted gates. The frozen
published 0.1.22 distribution does not include this checkpoint change.

## Per-source launcher update revision history (2026-10-24)

Selecting a backup update source previously replaced the primary source's last
accepted revision and index hash. A three-check sequence across separate service
lifetimes reproduced acceptance of both an older primary revision and changed
bytes at its previous revision after fallback. The unchanged-primary control
already passed.

The saved selection now retains a revision/hash checkpoint for each source in
the same atomically replaced record. Returning to a source checks its own history;
fallback remains ordered, and a valid backup can retain its independent revision.
Existing selected revision metadata is retained when the first source history is
written. No interface, source configuration or publication files change.

The Release solution build has zero warnings/errors. The updater regression group
passes 181 cases with one optional public-network check skipped, including all
three new fallback/restart cases. Full-suite validation is recorded separately
when complete. This code change is not a new published binary or production
signature acceptance; it does not close real-game or remote-player verification.

## Launcher-compatible plugin upgrade selection (2026-10-24)

Ordinary plugin discovery previously offered a newer release even when its
minimum Launcher version exceeded the running application. A focused regression
reproduced the incorrect available state. Discovery now chooses the highest
eligible newer SemVer release for the build and current Launcher, allowing an
intermediate supported upgrade. Installed-version content conflicts are still
rejected before candidate filtering.

Latest-only components retain their separate newest-release requirement. They
return the structured Launcher update requirement before reading package contents
or changing a selection when that release needs a newer Launcher. The HTTP/AList
and file/ZIP integration matrix covers this gate and the ordinary intermediate
upgrade while preserving the other client's pinned version and options.

Release solution compilation has zero warnings/errors. The focused store,
plugin-monitor and compatibility suite passes 64 cases without skips; the full
managed suite passes 1,087 cases with nine environment-gated skips and no failures
out of 1,096. Results are under
`artifacts/plugin-launcher-compatibility-20261024/`. Paired plugin guides also
remove obsolete metadata/version ceilings and describe the current developer
tabs. No store package, native runtime, game image or public feed changed.

## Renewable account visibility across access expiry (2026-10-24)

The frontend previously filtered account snapshots by access-token expiry alone,
even though the backend retained their valid refresh sessions. This hid inactive
accounts on the same server and restored accounts after a Launcher restart. A
frontend regression reproduced the disappearance before the fix.

Public snapshots now include the optional refresh expiry without exposing a
credential. The account list and its expiry timer retain a session while either
deadline remains valid. Invalid or expired renewal metadata cannot extend access;
selection still renews through the backend, and logout remains account-scoped.
The managed regression restores two accounts, renews only the selected identity,
checks serialized credential exclusion and preserves the other account on logout.

Release solution compilation has zero warnings/errors. The focused managed
account/session/routing suite passes 64 cases; the frontend account suite passes
32 cases, and frontend type checking and production compilation pass. The full
managed suite passes 1,088 cases with nine environment-gated skips and no failures
out of 1,097. Results are under `artifacts/renewable-accounts-20261024/`.
This change does not publish a binary or establish additional desktop, retail-game
or remote-multiplayer acceptance.

## Account listing survives selected-session revocation (2026-10-24)

A loopback HTTP fixture with two DPAPI-restored accounts reproduced a bridge
failure when automatic renewal rejected the selected account. The API removed
only that account correctly, but the bridge threw before returning the remaining
account list. The same failure prevented returning an empty signed-out list when
the rejected session was the only account.

Account listing now returns the remaining snapshot after renewal has removed the
selected session. It does not select another account or suppress discovery
rejections that retain a selection. Seven real HTTP/bridge cases cover both
revocation scenarios, successful renewal, service unavailability, catalog refusal,
malformed renewal and a structured Launcher upgrade requirement. The first two
failed before the change; all seven pass after it. Selecting the retained account
also renews and persists its rotated credential successfully.

The focused account, routing and remembered-login suite passes 61 cases without
skips or failures. Release solution compilation has zero warnings/errors, and the
frontend production build passes. Results are under
`artifacts/account-list-revocation-20261024/`. The maintainer-requested current
source build started as process 27980 and reported frontend readiness; Computer
Use's unavailable native pipe prevented screenshot inspection. No public feed or
game package was changed.

## Native control ports to additional legacy builds (2026-10-25)

Implemented static adapters for id-400, id-1809 and cn-717 in the existing
Game Control Support plugin. Each descriptor covers native message boxes and
button results, multiline text notifications, native expiring announcements and
uniform-color local chat with a caller-supplied caption. The resources and
consuming paths are recorded in `hook/native-game-controls.md` and each TOML.
Other declared legacy builds remain unadapted.

Constructor and modal ABI selection are now independent descriptor values.
The shared native implementation adds verified stack-based construction,
three-flag modal calls, owner notification calls and register-based chat calls;
it contains no build-ID branches. cn-717 reuses the existing conventions entirely.
Synthetic x86 callees exercise argument ordering, results, multiline borrowed
strings and id-1809's mixed constructor/modal pair. The offline PE verifier checks
timestamps, exact function signatures, dialog bounds and modal vtable bindings.
Retail-game presentation and lifecycle acceptance for the three ports remain
open; static verification does not establish runtime acceptance.

Release native and managed compilation succeeded; managed compilation reported
zero warnings and errors. The native control suite passes 95 checks, and all five
descriptors pass offline executable verification. The focused managed run passes
all 18 native UI cases and nine other cases, but its automatic store-update case
times out. The full managed run also reports file-access and update-helper
failures and was interrupted after prolonged lack of progress; it is not a
passing full-suite result. The two initially failing file-access test groups pass
in the focused rerun. Results are retained under `artifacts/native-ui/`.

## Missing Chinese client launch definitions (2026-10-25)

cn-706, cn-717 and cn-735 now declare their ordered endpoint arguments, default
ports and existing version-512 login bridge contract. Each argument reader,
module loader and login callback is recorded in `hook/cn-login-bridge.md`.
cn-735 remains disabled and hidden because its server adaptation is incomplete.
All enabled builds and all native-control descriptors have launch-plan coverage;
configuration coverage does not establish successful lobby entry or game UI
presentation.

The public store previously overrode bundled definitions with the same missing
configuration. Published only the three corrected profile rows, advancing
cn-706 and cn-717 to revision 3 and cn-735 to revision 2. Anonymous read-back
verified the catalog SHA-256 as
`88b30f3fc0e76b10fbddaa269f8193c22142b8e521eaf74f192dd4f86d3b70b4`.
The local cache received those same rows without changing its 35 packages,
resolved release metadata, other profiles or server discovery settings.

Existing-client registration now permits a required store plugin to be absent;
launch validation still rejects missing or disabled required plugins. Read-only
corpus tests cover retail and unpacked client registration, repeat registration
identity and unchanged executable hashes, including cn-706 and cn-717. The
required-plugin login fixture now declares its requirement in the manifest so
selection reconciliation retains it and failed activation denies ticket delivery.

Release solution compilation reports zero warnings and errors. The focused
definition, native UI, installed-client corpus, plugin and login-bridge run
passes 136 cases with one opt-in public-network skip and no failures. Artifacts
are under `artifacts/native-ui/launch-fix/`. The rebuilt Launcher was restarted
against the existing user data root and reports a responsive window and bridge.
Computer Use misattributes that window to Codex, so no automated retail-game
launch or desktop presentation acceptance is claimed for this fix.

## Additional native control descriptors (2026-10-25)

Added evidence-backed static descriptors for cn-706, cn-404, cn-406 and cn-735.
Per-build addresses, object fields, signatures and calling-convention selection
remain TOML data. The shared plugin adds a context-bearing allocator convention
and an ESI/EDX chat convention, without build-ID branches. cn-735 remains hidden
and disabled until its server adaptation is ready.

All nine descriptors pass offline executable verification. Release native and
managed builds succeed with zero warnings; the native control suite passes
102 checks and the focused definition/native UI run passes 63 tests. The full
managed run reports a timeout in the automatic startup store-discovery case and
was stopped after further prolonged inactivity; it is not a passing suite.
Logs remain under `artifacts/native-ui-*`. Retail acceptance remains open and
has explicit checklists in `hook/native-game-controls.md`.

Research for cn-279, cn-324, cn-774 and tw-454 is recorded in
`hook/native-game-controls-porting-research.md`. The older pointer-backed string
contract needs a verified ownership adapter before those calls can be enabled.

After integrating Messenger PR #3, the Release solution builds with zero warnings
and errors, and all 174 focused definition, native UI, Messenger session, hook
launch and account cases pass. Native suites pass 150 runtime, 89 Messenger,
196 loader, 102 native-control and 35 dump-writer checks. All thirteen frontend
native UI/color cases, type checking and production build pass.

The requested 0.1.29 win-x64 portable ZIP was built from this integrated tree.
Managed assembly versions, portable Overlay, detached updater execution, native
archive decoder and archive CRC checks pass. It contains exactly five bundled
plugins, runtime 3.5.0, Game Control Support 0.3.0 and nine UI descriptors.
The ZIP SHA-256 is
`1fb7e9b1b2db6160123121b29468f24efeb06c2a5dc506c7b523efc46826f574`.
The archive is under `artifacts/launcher-0.1.29-x64/public/FantasyTale/launcher/win-x64/portable/`;
packaging did not publish a remote update feed. These focused checks do not
supersede the full-suite timeout or the outstanding retail acceptance above.

## Compact-record native UI ports (2026-10-25)

Game Control Support 0.3.1 adds cn-774 and tw-454 descriptors after independent
message, notification, chat, banner and icon-resource inspection. Generic
TOML-selected conventions cover four-argument chat, 68-byte announcement
records, end-iterator insertion and ownerless game clocks. Existing descriptors
retain their defaults; no runtime ABI or Messenger file changes are required.

All eleven descriptors pass offline image/signature/layout verification. Release
native compilation succeeds, native controls pass 138 checks, managed Release
compilation reports zero warnings/errors, and all 174 focused definition,
native UI, hook launch, Messenger session and account tests pass. Results are
under `artifacts/native-ui-modern-ports/`. The earlier full-suite timeout remains
unresolved. Retail presentation and lifetime acceptance remains open for both
ports. cn-279/cn-324 still require pointer-string ownership research.

The delivered 0.1.29 x64 archive remains unchanged and contains the preceding
0.3.0 plugin; the new 0.3.1 batch is not a published Launcher release.


## CN2012 Messenger integration and login policy (2026-10-25)

Merged the older Messenger calling-interface support on an isolated integration
branch. CN2012 explicitly selects the existing GameWidget ticket bridge and
requires Launcher credentials independently of the server's optional-login
preference. The frontend uses the same precedence as the launch backend.

Release managed compilation reports zero warnings/errors. All 103 focused
definition, Messenger-session, hook-launch and login-policy tests pass; frontend
type checking, production build and 42 account cases pass. The native Messenger
suite passes 89 checks after Release x86 packaging. The server's CN2012 Messenger
game-code PR #79 is merged after its Release build, isolated profile validation
and 16 Messenger cases pass. Actual CN2012 lobby/friend-window acceptance remains
open.

Published only the advancing CN2012 store definition, preserving all 35 package
rows and the other 12 client profiles. Verified public bytes and synchronized the
existing local cache; no client registrations or account records were removed.

### CN2012 Passport sign-in acceptance

The retail CN2012 client selects its locale-2 Passport path, rather than the
ordinary Messenger login path. The stand-in now handles that function with the
same server-issued Messenger ticket and callback. The publisher passport remains
opaque and is not sent to the service. Native coverage includes accepted and
refused tickets and the friend-window callback.

Release x86 compilation succeeds; the native Messenger suite passes 99 checks.
After packaging, all 51 focused hook-launch and Messenger-session cases pass.
The live CN2012 session reaches the lobby, receives event 4097 and successfully
reads an empty friend list. Request/accept, category and presence-label acceptance
remain open; an empty test database does not validate populated records. The
isolated local server disables automatic tutorial entry for this test only.

### Regional Messenger game-code translation

The owner confirmed the CN2012 request prompt and acceptance, but its populated
friend list remained visually empty while the service returned two records.
CN2012 filters root friends and groups by its native initialization game code.
The stand-in now consumes that code and translates the authenticated service's
own-game code for friend records, categories, supported games and request events;
game-name lookups translate it back. Foreign game codes remain unchanged.

The mapping is selected by client calls, without build checks or a server restart.
Release x86 compilation succeeds and the Messenger suite passes 119 checks,
including regional friends and groups, incoming requests, reverse name lookup,
unchanged foreign codes, baseline initialization and malformed initialization.
Visual acceptance of the populated CN2012 list remains pending after restarting
the game with the updated runtime; HTTP counts alone do not establish rendering.

## Plugin configuration presentation refinement (2026-10-27)

Removed the Resolution Extension custom page's repeated heading and excess blank space. Plugin settings tabs now retain drafts while fading between panels, and their content height transitions smoothly through WAnimatedSize. Sandboxed pages may report validated intrinsic heights without gaining bridge or filesystem access. The transform-independent size measurement prevents dialog entrance scaling from distorting layout dimensions.

Validation: 72 plugin frontend tests and 7 game-module settings tests pass; frontend type checking and production build pass. The refined frontend and Resolution Extension 0.2.1 are installed in the existing local test Launcher. Desktop visual acceptance remains with the owner, who stopped UI automation and requested independent testing. No remote publication is included.


## Package-owned settings page declarations (2026-10-27)

Plugin sidecars can explicitly declare an ordered list of native forms and sandboxed HTML pages, each referencing validated manifest option keys. Declared lists fully control navigation; one page is displayed directly. No plugin identity is hard-coded in the renderer. The host loads HTML only by a declared page ID and exposes only its declared options to the iframe.

Resolution Extension 0.2.2 declares a single HTML page, moves input captions below the controls, preserves focus-outline space and animates keyed row insertions/removals while retaining existing nodes. Invalid saved drafts can be cleared explicitly without bypassing the enclosing Save/Cancel flow.


## Resolution settings alignment and scrolling (2026-10-27)

Resolution Extension 0.2.3 aligns rows and entry controls, places the shortened next-launch note below the controls, and uses inset focus outlines. Sandboxed pages that report their intrinsic height delegate scrolling to the surrounding dialog, preventing temporary inner scrollbars while rows expand. Pages without height reports retain ordinary scrolling.

Validation: 76 plugin frontend tests and 10 plugin settings tests pass; type checking and production build pass. A browser fixture with the production dialog, animated size wrapper and sandbox component confirms identical left/right row and entry coordinates, bottom note placement, successful addition and no outer overflow. Desktop visual acceptance remains with the owner.


## First-open plugin settings height (2026-10-27)

The HTML settings frame now starts and resets at its 160px CSS minimum, replacing the unmeasured 380px placeholder that briefly enlarged the dialog before real content sizing. Subsequent height changes retain the existing smooth transition.

Validation: 76 plugin frontend tests, type checking and production build pass. An instrumented browser fixture using the production dialog, size wrapper and sandbox component recorded 25 height observations from 326px to 355px with zero decreases on first open. No plugin package, native runtime or policy/profile changes are required.

## Declarative character resource model switching (2026-10-29)

Implemented the game-owned resource provider and the single Character Model Switcher package. Each character exposes Original, New and Classic choices while the package starts with an empty preference map, so first installation is a strict no-op. Native descriptors and extension registrations determine available characters; plans and archive ownership are data. BFZ, version-2 PKN and the China 2011 early PKN reader/writer preserve untouched records and restore exact original files through the runtime transaction. Resource preparations use a bounded four-archive worker pool, disk-backed PKN spooling and verified cache reuse. The developer page now exposes physical-archive override provenance.

Validation includes a complete managed solution build with zero warnings, native Release x86 tests, 19-client read-only corpus composition, cancellation, stale input, damaged cache and recovery cases. Store publication and retirement of the three older model entries remain gated on final package materialization, installation conflict migration and anonymous catalog verification.
## Korean authentication package repair (2026-10-29)

Version 0.1.3's store snapshot contained only the native library and plugin manifest, omitting native-login.toml. Both affected computers failed before login because the native entry loads that profile from its package directory. Version 0.1.4 restores the unchanged verified profile and keeps the same DLL bytes. Publisher requirements now reject a missing profile or library. Seven publisher tests and 64 native authentication checks pass. No game wire, credential handling or client executable patch changed.

## Launch preparation survives page navigation (2026-10-29)

Keep one Home workflow alive across route changes so its pending plugin checks, confirmations and requested launch are not discarded by page disposal. Preserve client, server and account mismatch guards; explicit cancellation and final application teardown still stop the flow. Deactivated media tears down its video/audio subtree, and hidden Home layout measurements do not change its saved controls height.

Validation: 89 plugin frontend tests and 10 navigation tests pass, including an actual RouterView/KeepAlive mount that changes pages during a deferred prerequisite and observes exactly one subsequent launch. Type checking and the production frontend build pass. No client, account or plugin configuration is removed.

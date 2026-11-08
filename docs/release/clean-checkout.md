# Clean source build checkpoint

[Chinese](clean-checkout.zh.md)

## Final 2026-11-06 application-source checkpoint

The application candidate at `f379ba415d12e6efd6cba201947b6c2cc347f6e9`
contains 1,137 files and 72 explicit exclusions. Its manifest SHA-256 is
`67c9ade4a12f22185b824c7f91b0d1c44f5ce00b39862c062d80879cf656decb`.
The pristine export passed exact inventory and per-file hash verification;
packaging used another directory whose manifest-listed sources still matched.

The managed/native implementation is unchanged from `ef2c7fb`. That root
working tree passed a zero-warning, zero-error Release build and 1,937 tests,
with 16 environment skips and no failures. A fresh export of the same source
also passed the complete managed solution build with zero warnings and errors.
The 1,937-test result belongs to the root working tree, not the fresh export.
The final frontend dependency update passed type checking, production and game
UI builds, and 413 tests with three private-resource skips. It includes real
six-language translation and parameter-rendering checks; npm audit reported
zero known vulnerabilities after pinning official Vue I18n 11.4.18.

Both win-x64 and win-arm64 self-contained packages completed frontend rebuild,
managed publication, Gateway source/hash/architecture verification, runtime
notice collection, detached-updater execution without an external .NET runtime,
complete payload verification, file indexes and portable archives. The official
Photino dependency packages were not replaced. Native input files came from the
already verified cold Release/x86 build below; their source inputs were unchanged
and their four reused DLLs were recorded by size and SHA-256. This is not a
claim that native compilation was repeated for each package.

Both independent module managed tools also built with zero warnings and errors
using explicit Launcher project references. They are not fully standalone builds
and were not run against real game assets. Public Host API XML documentation
was checked with `GenerateDocumentationFile=true`; later browser policy types
are internal. Ordinary zero-warning builds alone do not establish that every
project enables documentation generation.

These are local candidate checks, not a release or repository-visibility change.
The main Host document-source/navigation limitation, real game/UAC acceptance
and whole-history disclosure remain explicit boundaries in the
[repository audit](public-repository-audit.md). Preserve the pristine export;
never publish the build directory recursively.

## Earlier 2026-11-06 source candidate

The explicit Launcher export at `373dbea7d8a363fc3a4924fda220fa2445ef8686`
passed exact file-set and SHA-256 checks before building. It excludes private
game assets and Git history. On Windows ARM64 with .NET SDK 10.0.401/runtime
10.0.12, its Release managed build produced zero warnings and errors; the full
suite passed 1,908 tests with 20 environment skips and no failures. Frontend
`npm ci`, strict type checking, production and game UI builds passed; `npm test`
passed 410 cases with three explicitly optional private-resource groups skipped.

A cold native build exposed a missing solution configuration for the legacy
dump-writer fixture. Applying the six-line solution correction committed in
`4bd80cc` and rebuilding Release/x86 made all eight native executables pass:
184 runtime, 415 host, 203 native integration, 106 dump writer, 501 synthetic
numeric-health, 138 game-control, 20 auto-dash and 64 authentication checks.
Native integration also runs its own subprocess scenarios. The original cold
failure is retained in the review evidence; a populated development output had
masked the absent Release fixture.

The later startup-ownership change in `00c70f2` is not covered by that export's
full managed run. Its separately rebuilt working tree passed all 80 pending,
instance and hook launch tests. With reviewed Gateway artifacts pinned to
`e3585e4bf27fdcb6f1575adf37360526b354f5fc`, the Gateway bootstrap suite passed
70 cases without skips for each of x64 and ARM64. Tests use controlled children;
they do not exercise an interactive Windows elevation prompt or real gameplay.

The independent module export at `dcc780f0476520a289e46e3b102c5f807625a23f`
passed SDK hash verification and its six standalone native project builds.
Auto-dash, resolution and authentication tests passed 25, 1,719 and 1,166 checks.
Integrated snapshots and managed tools with explicit Launcher project references
are not established as independent builds by these results.

Building adds deployment files and output to an export. That build directory
must not be recursively published; deliver a fresh export from its reviewed
manifest. These earlier results do not cover later source changes; the final
checkpoint above records the subsequent packages and remaining audit boundaries.

## Historical source candidate (2026-10-21)

On 2026-10-21, an isolated export of committed source at `70a51c9` was
built outside the working checkout. The export did not copy ignored deployment
DLLs, frontend output, local configuration, game installations or uncommitted
source. Dependencies were restored separately with `npm ci`.

## Verified baseline

- Frontend strict type checking and production build passed.
- The native solution compiled in Release/x86 with Visual Studio v145 and
  its warnings-as-errors settings.
- Runtime, login host, native integration, dump writer, Korean authentication
  and synthetic numeric-health test executables passed. The authentication
  test requires the tracked plugin-definition directory as its argument.
- The earlier isolated managed solution build passed with zero warnings and
  errors after the SDK catalog contract was included in source control.

The native integration tests use synthetic clients. These results do not
establish acceptance of real game builds or of uncommitted movement plugins.
They also do not establish that the current dirty working tree is ready to ship.

## Findings and remaining checks

The baseline npm audit reported Vite 8.3.1 affected by advisories
GHSA-698v-4r3x-3vcv, GHSA-r825-w8mm-5pxw and GHSA-587h-7wmj-h5pm.
These concern the development server's editor integration. Vite was updated
to the pinned 8.3.5 patch release with its lockfile. The isolated source export
with only these dependency files replaced passed type checking, production build
and all 140 committed frontend tests. The working tree passed the same checks
and all 148 frontend tests. The updated npm audit reported zero vulnerabilities.
Existing Vue lifecycle warnings in the working-tree test harness remain visible;
passing tests do not establish visual acceptance of every screen.

The publication payload gate now rejects missing or empty decoder source,
provenance and LGPL files, in addition to checking decoder architecture. This
prevents an incomplete decoder distribution from being indexed for release.
It does not verify the remaining dependency notices or replace inspection of
an actual newly published package. Frozen packages remain unchanged.

Full clean-checkout publication, historical disclosure review, dependency
attribution, font and artwork redistribution remain open. The repository stays
private. The planned game implementation repository split is documented in
[Native repository boundaries](../native-repositories.md); no source has moved.

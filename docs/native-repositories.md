# Native repository boundaries

[Chinese](native-repositories.zh.md)

The launcher and Bubble Fighter implementations will be maintained in separate
repositories with independent versions and releases. This is a migration target;
the current source tree still contains both. Repository visibility remains a
separate maintainer decision and the current GitHub repository remains private.

## Ownership

| Launcher repository | Bubble Fighter plugin repository |
| --- | --- |
| Managed SDK, shared host, core and downloads | Managed Bubble Fighter module |
| Generic FtHook runtime and public C ABI | `nmcogame` host and login bridge |
| Generic overlay and companion contracts | Authentication, movement and game UI plugins |
| Store/profile parsing and package validation | Build profiles, game locators and plugin manifests |
| SDK export and synthetic ABI examples/tests | Game-specific fixtures and integration tests |

Store profiles continue to control required/recommended plugins and compatible
builds. Downloading a game package does not require a launcher release unless its
manifest requires a newer launcher or runtime contract. Released package versions
remain immutable. Existing plugin IDs and user settings must survive migration.
Tales Runner and Air Rider implementation modules remain in private repositories;
this decision does not publish them or move server code.

## Standalone ABI distribution

Run `powershell -File tools/package-native-sdk.ps1` to export an allowlisted SDK
under ignored `artifacts/native-sdk`. An optional `-OutputDirectory` selects a
new destination; existing destinations are rejected. The export includes the C
header, license, bilingual instructions and a standalone x86 locator example.
`sdk.json` records exact file sizes and hashes and the runtime and ABI versions.
It contains no binaries, game implementation, session files or parser dependency.

Compile the exported sample with Visual Studio v145 and the Windows SDK, from a
directory outside the checkout, before distributing the SDK. This proves the C
ABI sample can build independently; it does not prove existing C++ plugins have
already been separated or that game behavior has passed acceptance.

## Migration gates

The source-level dependency inventory and the ordered C ABI migration plan are in
[Native Plugin Repository Migration](native-plugin-migration.md).

1. Inventory internal helper use first. Current movement plugins use pattern and
   string helpers; Korean authentication additionally uses the internal session
   reader. Their project references still link `fthook.lib` from this checkout.
   Replace those references with host callbacks or a deliberately versioned,
   narrowly scoped helper package. Do not export all runtime internals as an SDK.
2. Move the game-specific `nmcogame` host with its forwarding and login behavior.
   Define how it consumes the runtime without introducing a dependency from the
   launcher onto game implementation code. Preserve session and launch readiness
   contracts; never put tickets or credentials in build artifacts.
3. Move tests that directly include game implementation sources into the game
   repository. Keep synthetic ABI/transaction tests in the runtime repository.
   Each checkout must build using pinned, documented SDK inputs.
4. Separate runtime, host and plugin packaging. The existing
   `tools/package-native.ps1` still combines them and rewrites authentication
   payload hashes. Migration must preserve immutable published packages and must
   not run that script against a frozen release as a shortcut.
5. Verify a clean checkout, plugin loading and required-plugin/profile behavior
   before deleting or relocating supported implementation files. Deprecated
   implementations and abandoned experiments are removed from the public tree.
   Record provenance and dependency notices for each repository independently.

## Private repository inventory

The launcher/client integration is divided into four repositories. SDK and generic
runtime stay in the launcher repository; individual plugins are grouped by game.
All four repositories were confirmed private when the new repositories were created.

- [FantasyTale.Launcher](https://github.com/Wasabi-FantasyTaleDev/FantasyTale.Launcher): shared launcher, SDK, generic runtime and overlay.
- [FantasyTale.Launcher.BubbleFighter](https://github.com/Wasabi-FantasyTaleDev/FantasyTale.Launcher.BubbleFighter): Bubble Fighter integration and plugins.
- [FantasyTale.Launcher.TalesRunner](https://github.com/Wasabi-FantasyTaleDev/FantasyTale.Launcher.TalesRunner): private Tales Runner integration and plugins.
- [FantasyTale.Launcher.AirRider](https://github.com/Wasabi-FantasyTaleDev/FantasyTale.Launcher.AirRider): private Air Rider integration and plugins.

The local Bubble Fighter checkout contains standalone Auto Dash and an integration source
snapshot with build profiles and supported plugin definitions, including
authentication, overlay and diagnostic modules. The snapshot's native projects
still reference shared Launcher build inputs. Auto Dash and diagnostic mode now
have independent public-ABI builds; diagnostic source lives under
`native/crash-recorder` in that repository. Its clean-source build and actual DLL
loader validate exception capture, failure cleanup, minidump structure and real
synchronous receive tracing in a synthetic host. These checks do not accept game
behavior or migrate `nmcogame` and authentication. Abandoned forward-jump sources
and superseded standalone plugin packages have been removed. Tales Runner and Air Rider remain
boundary-documentation repositories. The migration gates above remain open.
Existing server repositories are independent and are not part of this split.
Public redistribution still depends on the outstanding checks in
[the repository audit](release/public-repository-audit.md).

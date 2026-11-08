# Native Plugin Repository Migration

This document records the source-level dependency audit and the gates for moving
Bubble Fighter native integrations into their private game repository. It is a
migration plan. Auto Dash and diagnostic mode now have independent public-ABI
builds in the game repository; authentication and host extraction remain open.
Those standalone native checks do not accept actual gameplay.

## Current dependency inventory

| Component | Current source dependency | Boundary problem |
| --- | --- | --- |
| Auto Dash | `plugin_abi.h`, `pattern.h`, `session.h`, TOML parser; links `fthook.vcxproj` | Uses the runtime's C++ pattern parser and string conversion, and its project imports the launcher's native build files. |
| Diagnostic mode | `plugin_abi.h` and Windows system APIs | Its former project imported shared Launcher build files despite having no runtime implementation dependency. The independent project now consumes the pinned public SDK only. |
| Korean authentication | `plugin_abi.h`, `pattern.h`, `session.h`, TOML parser; links `fthook.vcxproj` | Still reads `session.toml` itself. Runtime 3.4.0 supplies a separately granted authentication callback; converting this plugin and independently verifying its build remain open. |
| `nmcogame` host | Runtime engine, session, logging, status, control and plugin-runner APIs; links `fthook.vcxproj` | It owns game-specific forwarding and login behavior while calling internal runtime services directly. |
| Native SDK export | `plugin_abi.h`, sample, license and instructions | It is a useful C ABI boundary, but does not currently provide the services needed by the above C++ integrations. |

The plugin ABI already supplies bounded logging, validated options, image
scanning and reads, locator registration, staged writes, overlay and companion
callbacks. Preserve that C ABI and its no-CRT-ownership rule. Do not turn internal
C++ headers or the runtime static library into a public SDK by copying them.

## Target dependency boundary

1. Game repositories pin a versioned SDK export. Their native projects import
   only that export and Windows build tools; they do not use relative paths into
   the launcher checkout or link its runtime project.
2. Keep pattern parsing and generic file/config helpers behind versioned SDK
   functions or a deliberately pinned, independently licensed dependency. Each
   choice must have an explicit version, ownership boundary and license notice.
3. Use the narrow, size-versioned host callback for the authentication data the
   KR plugin actually needs. Access is denied by default and granted only to an
   explicitly authorized plugin identity. It must return only the selected
   launch's ticket, endpoint and correlation ID into caller-provided bounded
   buffers, return a clear error for absent or mismatched session state, and
   never log the ticket. Do not expose the complete session object or make game
   modules parse the launcher's private session file. Runtime 3.4.0 implements
   this as `fthook_get_authentication` and an entry-time `copy_credentials` API.
   The managed module explicitly names its selected native owner in
   `LaunchPlan.AuthenticationPlugin`; an omitted grant exposes no API. Grant
   validation rejects old runtime packages before deployment. The runtime checks
   launch/game/build identity, reports caller buffer capacities without partial
   copies, revokes access and wipes its temporary ticket when entry returns.
4. Define a runtime-owned host contract for `nmcogame`: session validation,
   plugin activation, readiness/status reporting, control requests and the
   credential handoff must cross an explicit C ABI. Game-specific message
   forwarding and login behavior stays in the Bubble Fighter repository; generic
   session, plugin lifecycle and control implementations stay with the runtime.
5. Preserve plugin IDs, option names/defaults, build compatibility, required and
   recommended profile rules, and immutable published package bytes throughout
   the transition. Runtime minimums change only when the selected profile and
   release metadata are updated together.

## Migration order and acceptance

1. Publish and pin the SDK export, including ABI version, runtime compatibility,
   headers, sample, license notices and a machine-readable hash inventory.
2. Move Auto Dash first because it does not need authentication material. Convert
   it to the exported C ABI, build from a clean game-repository checkout, and run
   its native contract tests.
3. Add and test the bounded authentication callback before moving Korean
   authentication. Verify missing, mismatched and valid session cases without
   exposing tickets in logs or build artifacts.
4. Migrate `nmcogame` only after the runtime host contract can reproduce its
   current readiness, failure, control and login behavior in isolated tests.
5. Build each target repository from a clean checkout with pinned inputs; verify
   plugin loading, profile-required behavior and saved options against the real
   launcher. Retain the current in-tree implementations until those checks pass.
6. Move source ownership and package publication independently. Never rebuild or
   replace an immutable store release merely to match the new repository layout.

Auto Dash has a standalone implementation and tests in the game repository.
Diagnostic mode has moved from the game repository's integration snapshot to
`native/crash-recorder`, with production source bytes unchanged. A clean export
of game commit `2af17a5` verifies the pinned SDK and builds the DLL and two test
executables without Launcher build files or a runtime static library. The loader
uses only the public ABI and checks nine fresh-process scenarios: exception
records, disabled and invalid options, output failure, byte limits, handled/fatal
minidumps and real synchronous loopback receive tracing. Cleanup drains observers
and restores the synthetic host's import slots before unloading. The actual fatal
child must exit with its access violation and retain a parseable dump.

The source/payload receipt is under `D:/Temp/ft-standalone-diagnostics-20261023/`.
The independently built DLL also passes the existing Launcher FtHook test host's
recording, disabled, file-failure, byte-limit and fatal scenarios in an isolated
fixture under `D:/Temp/ft-standalone-diagnostics-runtime-20261023/`.
This advances independent build and native lifecycle gates, not game-client
receive behavior, plugin combinations, package publication or host extraction.
The Launcher integration and published plugin packages are unchanged. Repository
visibility and source redistribution rights remain separate technical gates.

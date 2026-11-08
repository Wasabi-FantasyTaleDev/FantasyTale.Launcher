# Mid-match exit

Version 0.2.5 of `bubblefighter.fantasy.mid-match-exit` applies its mode extensions
whenever the plugin is enabled. There is no separate `all_modes` option, and
previously saved false values cannot disable the native adapter or its patches.
The client's active-match, tutorial, loading and existing-dialog guards remain.
Initialization reports the compiled version, active build and selected route.
Its build inputs are
`native/mid-match-exit/plugin.preview.toml`, `native-patches.toml`, and `exit.toml`.
`tools/package-mid-match-exit.py` combines them into a clean importable package.
The native routes cover twelve clients. All 58 original branch instructions,
both early-client readiness signatures and both keyboard vtable pointers are
checked against their own executable images. The executable branch checks
reach native dialog setup with modeled helper results; they do not render a
dialog or establish live input delivery. Server deployment and real match exit
still need end-to-end acceptance.

## Diagnostic contract

Initialization emits one line containing `version`, `build` and `modes=all`.
A second line distinguishes a loaded native profile, a loaded companion profile,
or the precise profile failure.
The companion adapter reports passive Escape polling without executable or
vtable writes. Readiness checks are read-only and register no runtime locator. The plugin does not log keys, account data or endpoints, and these
initialization messages do not repeat on each key press.

The native-profile message precedes patch application. Read the runtime's
plugin outcome in the same launch's hook log or `fthook-status-<launch>.toml`
to establish whether those patches were installed. An entry returning success
does not by itself prove that any patch was enabled. `signature-mismatch`,
`timeout` or another failed outcome must be resolved before diagnosing input.
With an enabled native route and an installed outcome, the next missing
observation is whether Escape reaches the client's keyboard consumer. This
plugin adds no new input observer.

The packaged DLL is invoked for every declared build with no legacy option,
a saved false value and a saved true value. All three cases must activate the
same route without reading that option. These checks establish package activation;
they do not establish live input delivery or successful server departure.

## Evidence and scope

| Build | Keyboard consumer | Route | Declarative checks |
| --- | --- | --- | ---: |
| cn-279 | cn-279 sub_7AA7D0 | Companion | 0 |
| cn-324 | cn-324 sub_576670 | Companion | 0 |
| cn-404 | cn-404 sub_617FB0 | Native | 6 |
| cn-406 | cn-406 sub_561CF0 | Native | 6 |
| cn-408 | cn-408 sub_61AFF0 | Native | 6 |
| cn-706 | cn-706 sub_5FAB20 | Native | 6 |
| cn-717 | cn-717 sub_6C1780 | Native | 5 |
| cn-735 | cn-735 sub_817AF0 | Native | 5 |
| cn-774 | cn-774 sub_90EF70 | Native | 5 |
| id-400 | id-400 sub_5D26A0 | Native | 1 |
| id-1809 | id-1809 sub_61B3A0 | Native | 2 |
| kr-812 | kr-812 sub_668A60 | Native | 5 |
| kr-1256 | kr-1256 sub_75F690 | Native | 5 |
| tw-454 | tw-454 sub_882940 | Native | 6 |

The native input paths retain their context, Escape-key, duplicate-dialog and
active-match checks. Later clients also require their loading counter to reach
its completed value. For example, cn-717 sub_6C8D70 advances the counter at
stage `+2068` to 30; kr-1256 sub_75F690 requires 29 at stage `+2648`.
These counters alone do not prove that a match is running. The retained
cn-717 sub_B20B70, cn-774 sub_8BF580, kr-1256 sub_6FF0E0 and
tw-454 sub_7330D0 evaluate a match timer or a client state property. Removing
those checks would broaden behavior outside the client's active-match path.

The checked fixtures in `native/tests/data/mid-match-exit-routes.toml` describe
each consumer's own offsets, calls and tutorial branch. Escape is the literal
27 in the older Chinese handlers and a stored key binding in the other native
handlers. cn-774 sub_F594E0, kr-1256 sub_FBCF20 and tw-454 sub_EF9B60
initialize those binding fields through the client's key-map reader with the
default value 27; a saved binding can override that value.
The modeled ordinary mode is 1: tw-454 sub_471110 includes mode 4
in its special tutorial branch, while kr-1256 sub_11511C0 selects modes 15,
103 and 36. The test must not treat those two selectors as interchangeable.

CN2011 (`cn-279`) dispatches keyboard input through `GameStage` vtable
`0x9C0E5C`, slot `+148`, to `cn-279 sub_7AA7D0`. Its native Escape confirmation
is tutorial-only and invokes `cn-279 sub_818630`, which sends the end-stage
packet. CN2012 (`cn-324`) uses vtable `0xAE892C`, slot `+148`, and
`cn-324 sub_576670`; its tutorial confirmation invokes `cn-324 sub_46C000`.
Those tutorial packets do not establish a general match-leave contract.

`exit.toml` keeps each build's image identity, exact handler signature, original
vtable pointer and context offsets. A worker verifies the image using read-only
host access, then polls the Escape high bit every 16 ms while ready (100 ms while
waiting). Only a rising edge while this process owns the foreground window can
start a query. Holding Escape or moving focus with Escape held never repeats it.
There is no input interception, keyboard-table replacement, executable write,
window procedure hook or delayed key replay. The original tutorial and Logo
input consumers run independently and retain their original behavior.
CN2012's `cn-324 sub_576670` additionally has a native mode-16 exit; the
descriptor's `native_exit_modes` keeps that path from receiving a second dialog.
Its `cn-324 sub_6F3690` checks the `isSinglePlayTutorial` property for the other
tutorial branch. Ordinary-match eligibility comes from the server query, not
these client pointers. Disabling the plugin in the launcher remains the way to
disable the extension; the plugin itself requires no additional opt-in.

The early profiles list both original and unpacked `image_sizes`: CN2011 uses
`0x9C6000` / `0x9C8000`, and CN2012 uses `0xA28000` / `0xA2B000`.
Both variants retain the same build timestamp, keyboard slot and handler bytes;
the smallest declared image bounds every configured address. A different size
is rejected. Readiness logs identify image identity, image size, pointer and
instruction failures without treating an unverified image as ready. Executable-image checks verify those identities and signatures independently.
Package tests activate the actual DLL without mutation APIs and require zero
patch staging or locator registration on every route.

## Confirmation and delivery

CN2015's native confirmation (`cn-717 sub_9256B0`) selects the original tutorial,
game-session or relay departure path. Ordinary modes call `cn-717 sub_926590`,
which sends relay `rcpReqLeaveGame` (20008). Its vtable `0xE9ED8C` points both
reader and writer at `cn-717 sub_BD9960`, an empty `ret 4`; the request body is
zero bytes. The relay connection guard remains, so server support must include
an authenticated relay-to-game-session binding as well as the request handler.

The corresponding game-connection consumers are `cn-717 sub_881320` for
`gspRoomLeaveSlot` (511), `cn-717 sub_880760` for `gspRoomSetSession` (501), and
`cn-717 sub_8A6BE0` for `gspGotoLobby` (109). The first two update room state;
the last requests stage 3. The relay dispatcher `cn-717 sub_896050` provides
no evidence for a separate leave acknowledgement. These consumers establish
the native messages, not that a particular deployment accepted the request.

The early-client companion route uses the following confirmation flow.
A single worker observes foreground Escape edges without consuming input.
It first queries `game.session.state` through the shared
[session-state helper](session-state.md). Only a server-confirmed ordinary match
that currently permits leaving proceeds. The server must deploy and bind the
companion channel; local package checks do not establish that deployment.
It then invokes `game.native-ui`
with the question icon, title `中途离场`, text `要立即返回广场吗？`, and the
button labels `  确认` and `  取消`. Two leading spaces separate the text from the
icons already present in the native button textures.

An absent or unsupported service, a not-ready/busy adapter, or a confirmed
failure before presentation permits a Windows `MessageBoxW` fallback. Windows
uses its own OK/Cancel labels, the question icon and Cancel as the default. Unknown
presentation results never open a second modal. A scene change or shutdown
cancels command delivery. The native UI bridge owns the early-client dialog
descriptors, UTF-16 storage and modal calls. The ESC plugin only invokes the
bridge's public service.

Yes sends one Nirinsou Link `request` of type `game.command.execute`, version 1:

```json
{"operation_id":"exit-<random hexadecimal identity>","command":"plaza"}
```

The identity also occupies the transport operation-id field. No identity,
account or endpoint is supplied by the plugin. The server authenticates the
bound game session and applies the same permission checks as its command system.
Chat requires its command prefix; this typed API sends the command without it.
There is no automatic replay. The native broker returns the channel outcome in
`status` and the server payload in `response`. A completed transport is successful
only when that payload repeats the matching `operation_id` and contains both
`status: "success"` and `code: "success"`, with a string `message` field. Known
rejections remain failures; malformed, mismatched, unknown or executor-error
responses remain uncertain. The server retains an operation identity per bound
launch/session for its configured retention period (600 seconds by default).
Reusing an identity with different command text is an `operation_conflict`.
An uncertain result keeps the per-scene guard, preventing another Escape action
from duplicating the modal or the command.

## Build and checks

From a Visual Studio v145 developer environment, run:

```powershell
pwsh -File tools/verify-native-sdk.ps1 -RuntimeVersion 3.7.2
msbuild native/mid-match-exit/mid_match_exit.vcxproj -p:Configuration=Release -p:Platform=Win32 -m:1
msbuild native/tests/mid_match_exit_tests.vcxproj -p:Configuration=Release -p:Platform=Win32 -m:1
msbuild native/tests/mid_match_exit_activation_tests.vcxproj -p:Configuration=Release -p:Platform=Win32 -m:1
artifacts/mid-match-exit/mid_match_exit_tests.exe
python tools/package-mid-match-exit.py --destination artifacts/mid-match-exit/candidate-0.2.5 --library artifacts/mid-match-exit/mid_match_exit.dll --activation-test artifacts/mid-match-exit/mid_match_exit_activation_tests.exe
```

Packaging invokes the actual copied DLL with every declared client and all three
legacy-option states before creating the archive. It checks that the DLL's logged
version agrees with the manifest, exercises missing-profile and unknown-build
rejections, and saves an activation transcript beside the archive. A stale DLL
or a failed native entry prevents archive creation.

The separate executable-image check requires Python packages `pefile`,
`capstone` and `unicorn`. Supply a local JSON object mapping all fourteen build
IDs to their unpacked executable paths; those game files are not committed:

```powershell
python tools/verify-mid-match-exit.py --clients artifacts/local-client-paths.json --package artifacts/mid-match-exit/candidate-0.2.5 --output artifacts/mid-match-exit/candidate-0.2.5-verification.json
```

The check pins all 58 declarative signatures and executes 102 keyboard cases
using the clients' actual x86 instructions with explicit stand-ins for called
helpers. It covers ordinary Escape, the unmodified branch, a different key,
inactive match state, an existing modal, missing context, the original tutorial
branch, boss presence, and loading rejection where the client has that guard.
Unknown helper calls fail the check instead of being assumed safe. No game
process is launched and no wire operation is executed.

The checked-in 3.7.2 SDK export supplies the local service ABI; the plugin does
not link launcher internals. Its bounded JSON reader comes from the game module's
messenger implementation, with duplicate-key rejection and checked integer
conversion for service replies. Tests cover confirmation outcomes, fallback,
stale identities, scene cancellation, malformed/ambiguous replies and no replay.
They do not establish actual client rendering or live-server command success.

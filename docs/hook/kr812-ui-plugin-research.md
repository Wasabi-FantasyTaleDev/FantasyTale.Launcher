# KR 2015 numeric health and in-match exit investigation

Target: `kr-812` (Korea 2015). Reference behavior: `id-1809` (Indonesia
2015). Both requested features are separate optional plugins. No retail
process was started during the initial static investigation. Subsequent retail
tests were authorized only during the maintainer's rest window.

## Verified resource boundary

Read-only extraction of `Ui/Ui.bfz` produced 287 `.ui` members for kr-812
and 275 for id-1809. `Ui/Stages/Stages.bfz` produced eight `.ui` members
for each build. UI definitions are resources; archive and member paths must
retain their original names because archive keys depend on the relative path.
Extracted client assets remain outside version control.

`Ui/DlgQuit.ui` is byte-identical in both builds, SHA-256
`fd22f7cb6cdb52045a3b5e6727edc247f04131493d8a7f8f167cf70847d07739`.
It defines Home, Quit and Cancel controls. This proves that replacing this
resource alone cannot explain a difference in in-match Escape behavior.
It does not prove that this dialog is the in-match confirmation dialog.

kr-812 `sub_8BB4E0` loads `Ui/DlgQuit.ui`, binds its named controls and
assigns their command identifiers. Its destructor is `sub_8BBBD0`.
kr-812 contains both `Packet_gcpRoomReqLeaveGame` and
`Packet_rcpReqLeaveGame` type names. Presence of a type is not proof that
it is the correct request for returning directly to the lobby.

No directly named numeric-health control was found in the main or Stages
UI archives. That does not establish programmatic rendering: remaining
resource/config/script paths and the HUD consuming functions still require
inspection. `RenderBossHp` is a separate boss-health entry and must not be
used as evidence for the player-health display.

Both builds contain all nine members of `Images/GameStage/myHP/myHP.bfz`,
including `myHPtext00.png` and `myHPtext00.smf`. Read-only decrypted member
SHA-256 comparisons match for every member. id-1809 `sub_A7D2A0` loads
the text sprite into number-rendering slot 23; `sub_789AB0` uses that slot
after computing a current-to-maximum ratio. The image's `dbl_CCC8F8` is
100.0, so this display is normalized health, not the raw HP value.
id-1809 `sub_50FB80` binds `GetHp` through `sub_510500`; its caller supplies
`sub_425C50`, the same current-HP accessor used by `sub_789AB0`.
The maximum accessor still needs independent verification. Resource availability alone
does not enable the display, and no archive replacement is currently justified.

kr-812 `sub_AE6D80` initializes number slots only through 22, whereas
id-1809 `sub_A7D2A0` additionally loads the health sprite into slot 23.
kr-812 `sub_AE6A40` also has no sprite-selection case for slot 23, so calling
the Indonesian slot number in the Korean renderer is unsafe. A native plugin
must supply a verified rendering path rather than merely enable the asset or
reuse that slot number. Keep one plugin identity with build-scoped descriptors.

kr-812 `sub_53B9C0` supplies `sub_428200` to the `GetHp` binder
`sub_53EA10`; the accessor decrypts the float at character offsets 452/456/460.
HUD `sub_7DF1A0` already calls number wrapper `sub_AE6CB0` with slot 5 for
the nearby life count. That wrapper uses a shared formatting buffer, so any
additional draw must run synchronously in the render thread, not a timer or
background callback. Reusing its slot is a possible initial rendering strategy,
but hook calling convention, HUD lifetime and maximum-HP semantics remain gates.

An experimental exit package now lives under
`native/samples/bubblefighter.fantasy.mid-match-exit`, with a single identity,
kr-812-only compatibility and default-false `all_modes` option. Five sites
pass offline byte verification; managed parsing/defaults and incompatible
build rejection pass. Retail 0.1.0 displayed the confirmation but did not exit;
0.1.1 sent game 506 but subsequently crashed. Version 0.1.2 withdraws that
fallback and restores the relay confirmation path. It is not bundled or complete.

kr-812 `sub_897850` stamps relay opcode 20008; the tested client has only its
game TCP connection. `sub_6688E0` already constructs `Packet_gcpRoomReqLeaveGame`
(vtable `0xD3FD98`, reference `0x6689AF`) and uses game object `0xE94B24` with
`sub_6711B0`, which stamps opcode 506. Both sender wrappers share the same
packet-in-ESI and connection-on-stack ABI. Version 0.1.1 changes the ordinary
confirmation's object, packet vtable and sender together to that existing game
path; it preserves the null guard and the special/session branches. This is
not a relabeling of relay 20008 as game 506. That experiment is now withdrawn.

## Retail failure evidence and remaining lifecycle gate

The 0.1.1 run received game 506 at 04:28:56.625 UTC on 2026-10-09.
The server returned 511, 501 and 109; Windows Application event 1000 records
an access violation at main-image RVA `0x2679AF` in PID 18308 at 12:29:08
Asia/Shanghai. This is kr-812 `sub_667850`, immediately after `sub_524720`
returns the selected player: `cmp byte ptr [eax+0x3061], 0`. The getter may
return zero when the selected player is unavailable. No exception context was
obtained, so the actual register value is not established.

kr-812 `sub_7FDE50` consumes 511 in the game stage and calls `sub_50A680`,
which destroys the departing player and clears its player-array entry.
`sub_81FF00` consumes 109 but requests stage 3 only when the game connection's
byte at offset 266 is set. `sub_B270E0` writes this byte for successful socket
event 16 and clears it for event 32. This is a connection lifecycle guard,
not a server-provided flag in the four-byte 109 body. Clearing or bypassing
that guard is not justified by the captured reply alone.

The next acceptance must establish stage teardown and selected-player lifetime
across the leave responses, with receive trace and an exception/hang dump.
Do not suppress the renderer dereference or treat server script acceptance as
proof of native-client acceptance. Desktop tests are prohibited again after
the rest window; static and headless work may continue.

## Numeric-health accessor and render-call evidence

id-1809 `sub_789AB0` obtains the normalization denominator from character
vtable offset 336. The `ChrPlayer` primary vtable at `0xC6C8B4` resolves that
slot to `sub_522810`. It decrypts a float through character offsets
13556/13560/13564 and applies mode-specific multipliers for modes 13, 14 and 18.
kr-812's `ChrPlayer` primary vtable at `0xD0C6A4`, offset 356, resolves to
`sub_552060`: it decrypts the corresponding float at 13736/13740/13744 and
applies the corresponding single-PvP/team-survival multipliers. The Korean
HUD `sub_7DF1A0` already uses this virtual slot for its life gauge. The
normalization denominator must use this accessor, not an unscaled raw field.

kr-812 HUD call site `0x7DFB55` is `E8 56 71 30 00`, targeting `sub_AE6CB0`.
The caller removes `0x1C` bytes after the call; the callee returns with plain
`ret`. This is evidence for seven stack argument slots at this specific call,
not permission to assume a general fixed signature for every caller of the
variadic formatting wrapper. Any interception must preserve original life-count
rendering, flags/register obligations and render-thread execution. The experimental
sample now loads in the retail client; the HP gauge visibly displays normalized
full health as 100. The maintainer verified updates after self-inflicted damage;
respawn and lifetime validation remain acceptance gates.

The retail packed kr-812 executable retains timestamp `0x55376C47` and image
size `0xF0B000`; the reconstructed corpus executable has size `0xF0E000`.
Both descriptors still require the measured unpacked call and maximum-HP slot.
HUD offset zero is an index holder, not a player pointer. `sub_7DF1A0` loads the
selected player from the context array at offset 1188 and restores it into EDI
at `0x7DFADF`, before the number-render call. The thunk captures EDI for health
access and EBP for gauge position, without reading the index holder as a player.
Thirty-one synthetic checks cover packed/unpacked headers, rejected unknown
headers, not-yet-unpacked code and actual x86 thunk argument forwarding.

## Legacy relay establishment details

kr-812 game dispatcher `sub_837170` routes inbound opcode 601 to
`sub_81CAB0`. The latter reads `Packet_gspConnectToRelayServer` through
`sub_844660`, releases the previous relay object, allocates a `0x9B8`-byte
connection through `sub_810510`, stores the decoded session-key members and
calls `sub_B262B0` with the decoded address and port. Do not infer wire field
widths from decompiler local-variable sizes; the NetAddress and RelaySessionKey
readers must be followed before implementing a codec.

Relay dispatcher `sub_810C20` branches on the incoming packet's opcode at
header offset 2. Its opcode 1 path constructs connection/session data and calls
`sub_811D50`, which stamps relay C12. Its opcode 13 path constructs
`Packet_gcpReportRelayConnected` and calls `sub_811C70` on the game connection;
that sender stamps game C601. Relay callback `sub_810BA0` also uses this report
sender. This establishes a real legacy relay lifecycle, not merely residual
packet names. It does not establish protocol compatibility with later builds.

The shared listener and connection registry may be reusable, while handshake,
session-key decoding, connection reports and leave teardown require per-build
dialect evidence. The relay C20008 path remains distinct from game C506.

Initial join is separate from the dispatcher opcode-1 response: kr-812
`sub_810AA0` constructs `Packet_rcpReqJoinRelaySession` with `RelaySessionKey`
and stamps outgoing relay C1. `sub_8119A0` has a second join construction path
using the same class and opcode. A later-build authentication payload cannot
be substituted merely because both use opcode 1.

### Relay key and initial join serialization

kr-812 `Packet_rcpReqLeaveGame` vtable `0xD76A3C` selects `nullsub_2` at
`0x5C8FC0` for both read and write. Its body is empty before framing; sender
`sub_897850` stamps relay opcode 20008. This does not specify a leave response.
The confirmation's `sub_A61E70(4)` is a language comparison, not a game-stage
check: it compares global `dword_E9617C`, which XML parser `sub_848ED0`
populates from `Language/Lang`. Caller `sub_8E8850` supplies
`Config/Lambient.xml` (wide string at `0xD7FD9C`). A live relay alone does not establish
that this branch is reachable for every client language.

kr-812 `RelaySessionKey` vtable `0xD69A54` selects writer `sub_812240`
and reader `sub_8122A0`. These consume six fields in object-offset order
4, 8, 12, 16, 18, 20, with byte widths 4, 4, 4, 2, 2, 4 respectively:
20 serialized bytes, excluding the vtable. Primitive writers `sub_A56A80`
and `sub_A56A60` pass exactly four and two bytes to stream slot 36;
readers `sub_A56C10` and `sub_A56BF0` use stream slot 32 with the same
widths. The decompiler's `char` argument types are not the wire widths.
These facts establish widths and order; field meanings and stream byte order
still require their own consuming-path evidence.

kr-812 `Packet_rcpReqJoinRelaySession` vtable `0xD699B8` selects writer
`sub_8100E0` and reader `sub_810110`. The writer serializes the nested key
at object offset 4, then one byte at offset 28 and four bytes at offset 32.
Thus its body is 25 bytes before framing. In the TCP join construction
`sub_810AA0`, the trailing byte comes from connection offset 2440 and the
four-byte value from 2444. `sub_81CAB0` fills these from the current player
index and the member at global object `dword_E82614 + 528`, respectively;
the latter's meaning remains unproven. The second construction in
`sub_8119A0` must be checked independently before sharing its defaults.

This narrows the initial handshake implementation boundary. It does not prove
the complete relay exchange, leave acknowledgment or safe game-stage teardown.

## Implementation gates

### Multi-build package boundary

Keep one identity per feature, not one identity per build. Existing schema-one
`[[patches]].builds` variants already support build-specific locations and
expected bytes inside a single plugin. A native implementation can dispatch
through validated build descriptors under the same identity. Only evidenced
builds enter compatibility; no wildcard compatibility is justified yet.

### In-match exit evidence

The actual game-exit dialog is `DlgGameExit`, separate from `DlgQuit`.
kr-812 `sub_668A60` and id-1809 `sub_61B3A0` construct it. The ordinary
exit-key branch compares the configured key at stage offset 672. In kr-812
it additionally requires `sub_4FB590`, `sub_660CA0` and mode not equal to
101; id-1809 reaches its corresponding branch without those three predicates.
kr-812 `sub_660CA0` accepts modes 101, 102, 103 and 16.

kr-812 `sub_896B60` is the dialog's confirmation callback (vtable slot
at `0xD76838`). It selects special-mode cleanup, session leave, or
`Packet_rcpReqLeaveGame`; the last path requires `sub_A61E70(4)` and
an active relay object. Therefore changing only dialog visibility can still
leave confirmation without the intended ordinary-match exit request. The
meaning and side effects of these predicates must be established first.

The offset-2132 equals-30 check in kr-812 `sub_6688E0`/`sub_668A60` is
initialization protection: `sub_66FA00` advances setup stages and finally
writes 30. Preserve it. The nearby `sub_6711B0`/id-1809 `sub_6228B0`
path creates a five-byte TCP packet and writes opcode 506 at header offset
2 (`sub_B25E90`/`sub_ABB1C0`); it is not proof of a confirmation text ID.

1. Trace player HUD creation and update in both builds; establish which
   resource/control or drawing function receives current and maximum HP.
2. Trace Escape in the active game stage, confirmation creation, accepted
   callback, leave request and subsequent state transition in both builds.
3. Implement only the proven difference. Reuse target-build UI and normal
   exit lifecycle instead of copying another build's whole archive or
   forcing a local stage transition.
4. For resource changes, pin original member/archive identity, preserve
   originals and detect conflicting edits. For hooks, pin executable
   identity and expected bytes. Keep both plugins disabled by default.
5. Later retail acceptance must cover plugin on/off, normal HP updates,
   cancel/confirm, server room cleanup, and restart/restoration. Static
   investigation is not runtime acceptance.

## Native yellow numeric-health presentation

id-1809 `sub_789AB0` computes truncated current/max HP times 100, determines
its decimal digit count, and draws at gauge anchor +(125 - 7*digits, -101).
`sub_A7D1D0` selects style 23, scale/depth 0.9. `sub_A7D2A0` loads
`Images/GameStage/myHP/myHPtext00.smf` and maps decimal digits into that slot.
The percent glyph is baked into the gauge background, not the ten-frame font.

kr-812 contains the same digit resource but `sub_AE6A40` supports styles only
through 22. A private slot-22 table maps digits to the resource sprite through
index 3079. `sub_AC4D60` obtains the cached sprite, using the 24-byte native
wide-string layout and options constructed/destructed by `sub_426D70` and
`sub_426D10`. Each draw touches the cache again; plugin code does not destroy
engine-owned sprites. All ten frame pointers and rectangles are checked before
calling the renderer. Original shared font tables and archive bytes are untouched.
The loader/option/renderer prologues join the existing strict image/call checks.
Resource validation failure retains white-font fallback. Synthetic coverage
proves these calls and failure behavior, not retail yellow-style acceptance.

The maintainer supplied an independent 20x18 percent PNG. Version 0.2.9 packages
it unchanged with a generated version-2 one-frame SMF. kr-812 `sub_AF2E40` reads
the frame count, adds one to the stored right/bottom edges, and normalizes the
four -1 offsets to zero. Thus the generated rectangle is `(0,0,19,17)`;
copying an existing ten-digit descriptor is not valid. `sub_AC4D60` derives the
PNG path from the SMF, and `sub_A4B340` probes both managed resources and loose
files. The plugin loads its absolute asset path without modifying client files.

The private slot-22 table maps ASCII percent to frame zero and checks exact frame
count/ownership/20x18 bounds. `sub_AE6CB0` is a variadic formatting wrapper, so a
literal percent uses `%%`; an unescaped `%` is not a valid call. `sub_AE6A40`
flag `0x400` measures without rendering. Its centered alignment and `(width-1)`
cursor advance determine the symbol placement after the actual digits. Raw HP
suppresses percent semantics. Resource and synthetic ABI checks are separate
from retail pixel acceptance; the existing gauge decoration proves neither.

### Endpoint configuration and launch-argument boundary

kr-812 `sub_848ED0` is the XML configuration reader, not the command-line parser.
Its caller `sub_8E8850` constructs `dword_E827F8` and supplies
`Config/Lambient.xml`. The reader also consumes the `Server` node; endpoint
consumers and the exact lifetime of copied values require further tracing before
any runtime override is staged.

kr-812 `sub_8E8C30` invokes argument parser `sub_8E6670` only when language
predicate `sub_A61E70(2)` succeeds. In that path `sub_8E6670` accepts IP, port,
area and group argument prefixes and writes destination object fields 178 (string),
185, 186 and 187. The parser also contains a separate language-3 path, but the
observed caller does not reach it with that language. This does not prove the
absence of other callers. Adding the language-2 argument form to the default
Korean launch command is therefore not an established endpoint override.

Launcher session endpoint values being parsed by the host is not evidence that
the client consumes them. Local-host acceptance needs a measured, build-specific
endpoint adapter and a real connection to the selected local listener. Do not
redirect every socket: game and relay endpoints have separate lifecycles.

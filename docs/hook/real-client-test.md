# V3 real-client test procedure

Build the Release|x86 native solution first. The host and recorder are
`native/out/Release/nmcogame.dll` and `crash_recorder.dll`. This procedure is H8;
passing native tests alone does not complete it or deprecate V2.

## Modern launch acceptance (2026-10-18)

The installed corpus now covers CN2019 (`cn-774`) and TW2019 (`tw-454`) registration as well as Korean builds. These are read-only identity and registration checks, not proof that a game reaches the lobby. CN2019 uses the ordered SDO arguments parsed by cn-774 sub_A22290 and the version-512 bridge requested by cn-774 sub_A129B0; the interface identifiers match the existing bridge. TW2019 imports `NMCO_CallNMFunc2`, uses the shared authentication envelope in tw-454 sub_B926B0, and needs the ticket-aware stand-in rather than the official messenger forwarder.

The KR2026 packed executable was launched through the actual Launcher on the maintainer's ARM64 machine and stopped in a Themida dialog claiming `d3d9.dll` was missing. A separate x64 report names `NGClient.aes` despite that file being present. Earlier Korean acceptance used a modified unpacked executable and does not establish support for the packed executable. Keep this loader issue open; do not describe registration tests or unpacked startup as packed-client acceptance.

The repaired Launcher registered and started both 2019 retail executables. CN2019 reported native readiness and `login bridge: launch ticket delivered`, then crashed at executable offset `0x0064EB8D` (exception `0xC0000005`, cn-774 sub_A4EB50) at the login screen. This confirms bridge delivery, not successful server redemption or lobby entry. TW2019 reported `ticket provided`, completed the shared messenger calls and reached the main menu displaying the existing `[GM]Wasabi` character. This accepts TW2019 startup and ticket login; round gameplay and an unchanged retail installation without preexisting compatibility files remain separate checks.

## Managed CN2013 ticket login

For `cn-406`, build and run `tools/package-native.ps1`, then use the launcher's
client definition and server account flow. The build selects `gamewidget-ticket`
and `sdologinentry.dll`; the runtime deployment automatically preserves and restores
both this library and the messenger. Do not use the legacy schema-one example below
for this bridge: it requires a managed schema-two launch and a fresh server ticket.

The server must publish `cn-406` and enable `lobby.config.launcher_tickets` in that
profile's local override. Its API and listener must share the ticket service.
The original game version is display metadata, not the update version.
The [native contract](cn-login-bridge.md) records the interface evidence and bounds.

Accepted on 2026-10-08: the actual Launcher backend's registration, account login,
client registration and launch bridge started the CN2013 client against the live
server. The native host reported ready in forward mode, delivered the marked ticket,
and the game reached the lobby with the test account and visible character. The
launcher stop operation succeeded; hashes of both original libraries matched after
restoration and session.toml was removed. No optional plugins were enabled in this
run. This verifies native ticket login and cleanup, not complete round gameplay,
all plugins, other builds or every frontend interaction.

## Prepare one build

Use a test client copy. Preserve its original `nmcogame.dll`, any
`nmcogame.nexon.dll` and any existing `session.toml` outside the test copy's active
file names. Record their hashes. Exit the client before changing these files.

* Every enabled build uses `standin`. CN2011 and CN2012 call the older interface,
  which is the later one without its flags argument.
* A build switched back to `forward` (only the disabled `cn-735` today) needs the
  original Nexon library preserved as `nmcogame.nexon.dll`. Do not use another
  stand-in DLL as the forwarding target.

Copy the new `nmcogame.dll` beside the client executable. Continue using the
existing per-build launch arguments and account source. ID2014 takes the real
account in its in-game login dialog. A host-only first pass has no plugin entries;
verify its messenger mode and login before enabling plugins.

## Session example

Replace the paths and choose the build's timestamp from its tracked build TOML.
Use a separate runtime log directory for each concurrently tested instance.

```toml
schema = 1
[session]
game = "bubblefighter"
build = "id-400"
branch = "id"
instance = "v3-id-400-test"
messenger = "standin"
log = "C:/Users/PLAYER/AppData/Local/FantasyTale/BubbleFighter/logs/v3-id-400/fthook.log"
log_max_kib = 1024
[session.fingerprint]
pe_timestamp = 0x52E07057

[[plugins]]
id = "bubblefighter.fantasy.crash-recorder"
version = "1.1.1"
path = "D:/TestClient/fthook-plugins/bubblefighter.fantasy.crash-recorder"
required = false
[plugins.options]
enabled = true
max_records = 32
max_kib = 1024
external_privileged = false
```

Put the recorder's `plugin.toml` and `crash_recorder.dll` together at that plugin
path. For a subsequent font test, copy its manifest into another plugin folder
and add a session entry with id `bubblefighter.fantasy.font-weight`, version
`1.0.0`. It is currently available on kr-812, id-400, id-1809, cn-706 and cn-717;
do not add it to cn-324 or cn-279.

The recorder truncates `crash.log` beside `fthook.log` when armed. Preserve both
logs before another launch. It observes severe exceptions and always continues
the original exception search: a record may describe an exception the client
subsequently handles, and is not by itself proof of a crash. C++ exception
notifications and, by default, privileged instructions outside the main image
are filtered. `external_privileged` can be enabled for a diagnostic retry.

The recorder has a file-size cap, a record cap, 128 modules per immutable snapshot,
four refreshes, a 4096-byte stack scan and at most 12 candidate addresses per
record. Candidates are not a symbolized call stack. Exceptions beyond the caps,
simultaneous/reentrant faults, fail-fast termination and some stack overflows may
not yield a complete record. The text log does not intentionally record account
credentials, but dump memory and received packet bytes can contain private data.

Diagnostic mode 1.1.1 also declares cn-406 compatibility and remains optional: no
client build selects it by default. The native implementation uses exception handlers
and verified import-table writes rather than client-specific fixed addresses.
With `write_dump = true`, it attempts one bounded `exception.dmp` on the first serious
exception (`max_dump_mib`, default 32, maximum 64). With `receive_packets = true`, it
records supported synchronous main-image `recv`/`recvfrom` calls in `receive.ftnet`
(`max_packet_mib`, default 16, maximum 64). Dynamic/asynchronous receive paths and
outbound traffic are not captured; the runtime log explicitly reports unavailable
tracing when no supported imports exist. No trace file is created in that case.

## Offline prerequisite evidence

On 2026-10-10, `tools/verify-plugin/verify_plugin.py` checked
`native/samples/bubblefighter.fantasy.mid-match-exit` 0.1.2 against the tracked
KR2015 `BubbleFighterU.exe` using build id `kr-812`. All five RVA patches matched
their expected bytes, and the reported original and rewritten branch targets were
consistent with the patch definitions. This is only a preflight check; it does not
prove that the dialog, relay response or stage transition works in a retail round.
The verifier also checks each supported numeric-health descriptor against its
image: PE identity, the exact render call and original target, seven-slot caller
cleanup, resource function signatures and the maximum-health vtable pointer when
declared. A mismatch returns failure. Matching bytes still leave native activation
and runtime locators deferred, with exit code 2 rather than an offline pass.

```powershell
python tools/verify-plugin/verify_plugin.py native/samples/bubblefighter.fantasy.numeric-health --image 'kr-812=D:/test-clients/KR2015/BubbleFighterU.exe'
```

The October 23 check of the actual KR2015 unpacked executable passes all seven
descriptor prerequisites. This does not verify player provenance, visible health,
respawn or plugin combinations in the running game. Native descriptor schema
validation alone is not image verification. Research descriptors retain the
separate opt-in `--research-descriptors` check and cannot enable an unsupported build.

Fresh-process managed integration tests exercise both id-400 and cn-406 session
identities with a synthetic x86 client: readiness, receive bytes, a genuine access
violation, a parseable exception minidump, retained logs and original-DLL restoration.
This verifies the generic diagnostic chain, not the retail cn-406 executable's
receive path or gameplay stability. Those remain real-client acceptance work.

## Record the result

1. Confirm `messenger: standin` or `messenger: forward`, and the expected build.
2. With the recorder enabled, require `plugin ... result=installed` and the
   recorder's armed line. A failed plugin is not a successful test.
3. Test login and entering the menu; then one ordinary room and its start/loading
   path. Record the account privately, build, mode, map and approximate time.
4. If the client exits, preserve `fthook.log`, `crash.log` and the matching server
   packet capture. Match the address to this build's corpus and the packet sequence.
5. Test the font patch separately and record its visible effect. A scan match is
   not visual acceptance.
6. Restore the saved host/session when finished, or keep using this test copy.
   Never distribute a session containing an account or ticket.

Add the actual build, plugin set, result lines and user observations to H8's table.
Keep untested builds explicitly pending.

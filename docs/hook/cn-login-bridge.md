# Native ticket bridge for the CN login module

The CN2013 build is wire id `cn-406`, retail version `0.2.35.001`, with packed
and unpacked PE timestamp `0x51CD5DE5`. Its launch arguments come from the build
definition; no build-id branch is added to the shared launcher.

## Client interface evidence

cn-406 sub_7CC3E0 loads `sdologinentry.dll`, resolves `igwInitialize`,
`igwGetModule` and `igwTerminal`, then initializes interface version 512.
The first two exports are stdcall with two stack arguments; terminal is cdecl.
The module identifiers at cn-406 C1BCC4 and C1BCD4 are respectively the 16-byte
values `ab89b759e9be684caa23bca9ab1a3e50` and
`847418c85f1cdf4884972136a72a5733`. Resolution compares the identifiers, so
repeated queries and reversed query order retain the correct object.

cn-406 576EC2 pushes four arguments for sign-in slot 3: self, callback, zero,
zero. The callback cn-406 sub_577D00 copies narrow strings from record offset
8 into the first login wstr and offset 4 into its third wstr. The second wstr
remains empty. The 16-byte record's first and last DWORDs have no proven meaning
and stay zero. cn-406 sub_576510 serializes the request. Strings are copied
synchronously and are not limited to the old stand-in's 15 characters.

Display slot 9 has six stack arguments at cn-406 7CED00; login slot 15 has one
at cn-406 7CF300. The callers do not clean these arguments, despite the
decompiler's cdecl annotations. The bridge supplies callee-cleaned signatures.
Only observed lifecycle and notification slots are implemented. An unexpected
slot fails explicitly instead of returning through an unknown x86 signature.

The CN2014 retail `0.2.46.001` release uses wire id `cn-404`. Its packed and
unpacked executables share PE timestamp `0x533CFD5F`; `Version.dat` confirms
the retail release. cn-404 sub_88B9D0 loads the same three exports, initializes
interface version 512 and resolves the same module identifiers at cn-404
D03BF4 and D03C04. cn-404 sub_62F220 calls sign-in slot 3 with self, callback
sub_630130 and two zero arguments. That callback copies the account from record
offset 8 and the ticket from offset 4 into the same three-string login layout.
cn-404 sub_893590 parses the build-defined IP, port, area and group arguments.
The existing forwarded bridge is reused without a native build branch.

The remaining SDO launch definitions use the same verified bridge contract:

| Build | Ordered IP, port, area and group reader | Version-512 module loader | Sign-in callback |
| --- | --- | --- | --- |
| cn-706 | cn-706 sub_86EE00 | cn-706 sub_867510 | cn-706 sub_6134A0 |
| cn-717 | cn-717 sub_9820E0 | cn-717 sub_978F20 | cn-717 sub_6DB7A0 |
| cn-735 | cn-735 sub_B18A20 | cn-735 sub_B0F040 | cn-735 sub_834B10 |

Each loader requests the same display and login identifiers documented above.
Each sign-in caller supplies self, callback and two zero arguments; its callback
copies account text from record +8 and ticket text from +4. The build definitions
now declare `gamewidget-ticket`, `sdologinentry.dll` and the ordered endpoint
arguments. Configured default ports follow the corresponding server profiles;
an explicitly selected endpoint takes precedence. cn-735 remains disabled and
hidden because its server adaptation is incomplete. These findings establish
launch configuration, not successful lobby entry or native UI acceptance.

## Host and credential lifetime

CN2011 (`cn-279 sub_6D4230`, identifier at `9EA780`) and CN2012
(`cn-324 sub_786890`, identifier at `B211F8`) request the earlier login identifier
`6c13353f6170694ebf3acc78d026f48f`. It aliases the same login object, with the
unchanged display identifier and API version 512. Their callbacks
(`cn-279 sub_8580A0`, `cn-324 sub_5852D0`) consume record offsets +8 and +4;
the bridge preserves the existing callback layout and marked single-use ticket.
Unknown identifiers are still rejected.

The build definitions for cn-279 and cn-324 explicitly set
`[launch] requires_elevation = true`. The module passes that capability through
`LaunchPlan.RequiresElevation`; other launch plans default to false. Windows
permission prompts remain user-controlled, and an executable's own manifest can
still request elevation independently.

`login_bridge.dll` has three PE-forwarded exports targeting versioned entries
in `nmcogame.dll`. It contains no second runtime, file reader or worker.
The launcher maps this payload to the build's `login_module` target, preserves
the previous target and restores it after confirmed process exit. The original
messenger is still forwarded through `nmcogame.nexon.dll` for builds in forward mode;
CN2012 answers it in the stand-in.

The host accepts only its managed schema-two session after required-plugin
readiness. The bridge requires a nonempty ASCII account of at most 128 characters
using letters, digits, underscore, hyphen, period or at-sign, and exactly 64
lowercase hexadecimal ticket characters. These are deliberate bridge bounds;
they are not claimed to be limits of the original client. ASCII avoids silent
loss under the client's narrow-string conversion. Delivery prepends the agreed
`bff-ticket:` marker, producing 75 characters. Invalid credentials, host failure
and a second delivery receive a failure callback without a login record.
No credentials appear in log lines or the control channel.

The server's `launcher_tickets` lobby setting must be enabled for the selected
profile. The API and game listener must consume the same ticket service. Under a
combined server deployment, configure the source profile's local override before
build expansion. Ticket redemption, expiry, exact-build binding and replay rejection
remain the server's responsibility.

## Verification boundary

Controlled processes test the actual forwarded exports, module lookup order,
long accounts, complete tokens, malformed/missing credentials, required-plugin
failure, repeated delivery, termination and restoration. The managed launch plan
and unsafe destination names are checked separately. The real-client procedure
and its current acceptance boundary are in [real-client-test.md](real-client-test.md).

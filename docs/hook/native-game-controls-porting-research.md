# Native game controls: legacy porting research

Research state: 2026-10-25. See [the capability contract](native-game-controls.md)
for service requests, outcomes and in-game acceptance. Each descriptor belongs
to Game Control Support; callers use `game.native-ui`. Presentation fallback
policy does not belong in this provider.

## Completed descriptor ports

The following ports have consuming-path and resource evidence plus offline PE
verification. They have not received retail-game presentation acceptance.
All addresses in the descriptors are RVAs; function names below are VAs.

| Build | Constructor / modal / notification / chat ABI | Allocation and dialog lifetime | Announcement |
| --- | --- | --- | --- |
| cn-706 | 3 / 1 / 4 / 1 | cn-706 sub_444FE0 allocates 0x300 through cn-706 sub_9DBDD0, retains +8 and releases secondary +4 slot 0 | cn-706 sub_86BC40 appends; cn-706 sub_86B1C0 expires |
| cn-404 | 3 / 3 / 3 / 1 | cn-404 sub_765C10 allocates 0x300 through cn-404 sub_A10500, retains +8 and releases secondary +4 slot 0 | cn-404 sub_8903E0 appends; cn-404 sub_88F960 expires |
| cn-406 | 3 / 3 / 3 / 3 | cn-406 sub_6BD750 allocates 0x2D8 through cn-406 sub_93D220, retains +8 and releases secondary +4 slot 0 | cn-406 sub_7D04F0 appends; cn-406 sub_7CFAA0 expires |
| cn-735 | 3 / 1 / 1 / 4 | cn-735 sub_98C450 allocates 0x350 through cn-735 sub_CD8E40 with a three-word context, retains +4 and releases primary slot 4 | cn-735 sub_B14F10 appends; cn-735 sub_B14050 expires |

Message-box evidence, including layout and command consumers:

| Build | Construction | Button initialization | Layout | Modal |
| --- | --- | --- | --- | --- |
| cn-706 | cn-706 sub_81C4E0 | cn-706 sub_81D4A0 | cn-706 sub_81D610 | cn-706 sub_A08460, slot 208 |
| cn-404 | cn-404 sub_845E90 | cn-404 sub_846E50 | cn-404 sub_846FC0 | cn-404 sub_A39020, slot 208 |
| cn-406 | cn-406 sub_78E8A0 | cn-406 sub_78F860 | cn-406 sub_78F9D0 | cn-406 sub_96A450, slot 208 |
| cn-735 | cn-735 sub_AB4680 | cn-735 sub_AB5840 | cn-735 sub_AB59B0 | cn-735 sub_CFAE50, slot 232 |

Each build's mounted `Ui/DlgMessageBox.ui` maps `pnlIcon` indices 0/1/2/3
to notice/warning/question/error. The first three resources were read from
`Ui/Ui.bfz`; cn-735's resource was read from `Packs/Ui_00000.pkn`. Extraction
was read-only. Extracted files and client executables are not package inputs.

All four builds deep-copy the existing 76-byte announcement record. The native
appender receives the source in ECX and vector in EBX. The copy consumers are
cn-706 sub_87B060, cn-404 sub_89EEC0, cn-406 sub_7DE9C0 and cn-735 sub_B276F0.
Their renderers use text at 0, color at 28, clock tick at 32, duration at 36,
secondary string at 40, type at 68 and fade at 72. All enforce a seven-entry
limit. The provider returns busy instead of evicting an existing game notice.

cn-735 sub_B14050 uses duration literally, whereas the older renderers replace
zero with 13,000 ms. cn-735 sub_A1E160 supplies 13,000 ms to the normal native
announcement path, so this descriptor explicitly sets
`announcement_duration = 13000`. Expiry remains owned by the game.

## Descriptor design

For a build with an existing calling convention, adaptation changes only
`native-ui/<build>.toml`. The descriptor states image identity, exact function
fingerprints, addresses, offsets, allocation size, lifetimes and icon indices.
It cannot execute scripts or supply arbitrary instruction sequences.

The constructor, allocator, modal, notification and chat conventions are
independent. cn-735 proves that a stack constructor can coexist with the newer
allocator and primary-object lifetime. The loader therefore requires an
allocation path independently of `message_abi`, a `text_slot` unless `set_text`
is supplied, and `chat_offset` unless a direct `chat_module` global is supplied.
Optional icon-free layout and announcement support remain explicit capabilities.

`allocate_abi = 1` (the default for an `allocate` address) calls a cdecl
size-only allocator. `allocate_abi = 2` calls a cdecl allocator wrapper with an
allocator address, size and temporary `{0,-1,0}` context, as cn-735 sub_98C450
calls cn-735 sub_CD8E40. The context is borrowed for the synchronous call.
Without an `allocate` address, the existing UI-allocator-object path remains
available, as established by kr-1256 sub_899620.

`chat_abi = 4` passes module/body in ESI/EDX and caption/color on the stack,
as cn-735 sub_A344A0 calls cn-735 sub_9A7820. The adapter preserves the caller's
ESI. This changes no service requests or caller-facing plugin ABI.

These two reusable calling conventions are implemented once in `native_calls.h`.
There are no build-ID branches in the provider. Synthetic x86 callees validate
the new allocation context, register/stack argument delivery, empty and long
captions, multiline body and color before a retail client is used.

## Corrections to scan-only leads

Cross-build byte matches are search anchors, not adaptation evidence. In
particular:

- cn-706 RVA 0x44FE0 is cn-706 sub_444FE0. Adding the image base 0x400000
  does not produce a function named cn-706 sub_844FE0. The allocation call at
  VA 0x4451A8 is a real instruction when decoded from the wrapper's boundary.
- cn-706 sub_9DED40 formats strings. The message-box wrapper actually allocates
  through cn-706 sub_9DBDD0; the two operations must not be conflated.
- cn-706 uses four modal flags, not the three used by id-400.
- The cn-735 constructor candidate RVA 0x4DDC70 is unrelated to DlgMessageBox.
  Its earlier chat and announcement candidates were also unrelated functions.
  Following the resource consumer instead locates cn-735 sub_AB4680,
  cn-735 sub_9A7820 and cn-735 sub_B24780 respectively.
- cn-735 is not a simple offset translation of cn-717: allocation, modal slot,
  reference location, chat registers and announcement duration all differ.

Raw E8 searches can find byte sequences inside another instruction. Decode from
a known function boundary, verify the call target and callee epilogue, then
follow the arguments into their consumers. Obfuscated PE section names are not
reliable code-section labels; use section flags and file-backed mappings.

## Remaining builds: established starting points

These are research results, not shipped descriptors. They deliberately do not
claim complete capability support.

### cn-279

cn-279 sub_8A2F10 is the five-stack-argument DlgMessageBox constructor.
cn-279 sub_729870 allocates 376 bytes through cn-279 sub_4E9D60, retains +8,
invokes modal slot 212 with flags 0,1,1 and releases secondary +4 slot 0.
The constructor binds controls at +324/+328 and buttons
at +356/+360, then invokes cn-279 sub_8A2CA0. These differ substantially from
the 2013-2015 UI layouts.

The more important difference is string ownership: cn-279 sub_401570 constructs
a pointer-backed string and cn-279 sub_401510 releases a header at `data - 20`
by decrementing its reference count at header +8. Passing `BorrowedString`
would treat inline UTF-16 bytes as a pointer. A port needs an independently
verified owned-string construction/release path before any UI call is enabled.
No descriptor is provided while that contract is incomplete.

### cn-324

cn-324 sub_75C8B0 constructs DlgMessageBox with coordinate registers and five
stack arguments. cn-324 sub_75D650 assigns button pointers +356/+360 command
values 257/256 at button +256, sets text through slot 20, and clears the
icon-free byte +372. This is a different object layout from cn-706.

cn-324 sub_401C30 allocates a header followed by UTF-16 data, stores the length
at `data - 20` and returns a one-pointer string. The empty-string path retains
the shared allocation at header +8. It cannot use the current 24-byte borrowed
string. cn-324 sub_6AD4B0 now proves that cn-324 sub_8A3160 allocates 376
bytes for this dialog. It retains +8, invokes modal slot 212 with flags 0,1,1,
and releases secondary +4 slot zero. The clock scan anchor RVA 0x4A63C0
remains unproved by an announcement consumer.

### Owned UTF-16 contract for the older builds

cn-279 sub_4015E0 and cn-324 sub_401C30 share a verified callable shape:
thiscall with a one-pointer destination in ECX, source UTF-16 pointer and
explicit code-unit count on the stack, returning with RET 8. Nonempty input is
copied into native storage and terminated; empty input retains the native
shared empty allocation. cn-324 sub_8A1230 allocates `2 * count + 22` bytes,
initializes capacity and reference count one, and leaves 20 bytes before the
UTF-16 data. cn-279 sub_401570 is the null-terminated convenience wrapper;
the explicit-length cn-279 sub_4015E0 is the appropriate adapter boundary.

cn-279 sub_401510 releases the wide string through cn-279 sub_4D8BD0 after
decrementing header +8. cn-324 sub_480390 performs the same reference operation
and releases through the client's CRT. Use these native release paths rather
than the plugin's allocator. cn-279 sub_4010E0 is a different string-family
release path through cn-279 sub_4D8B70 and must not be selected just because
its instruction pattern is similar.

A reusable adapter should select this owned representation through TOML, with
signed-off construction and release functions covered by image signatures. Keep
the representation independent of constructor, notification and chat calling
conventions. Each temporary owns one reference, cannot be copied implicitly,
and releases only after the native consumer has copied or retained its input.
Before enabling a descriptor, follow notification/chat/banner ownership and
record layouts, verify empty/space-only labels and embedded newlines, and test
normal completion and exception cleanup. This contract is research evidence;
no older-build descriptor or owned-string implementation is shipped yet.

## cn-774 and tw-454 descriptors

Both builds use the 24-byte small-string representation. Their message,
notification, local chat and banner consuming paths have now been followed
independently. These remain static ports pending retail acceptance.

| Consumer | cn-774 | tw-454 |
| --- | --- | --- |
| Message wrapper and size | cn-774 sub_C76760, 1536 bytes | tw-454 sub_9F0710, 1232 bytes |
| Constructor and layout | cn-774 sub_779CB0 / cn-774 sub_77A980 | tw-454 sub_B04FB0 / tw-454 sub_B05D30 |
| Modal | cn-774 sub_F54E60, interruption byte then four flags | tw-454 sub_EF2C00, four flags via slot 268 |
| Buttons and commands | cn-774 sub_77A6B0: +1488/+1492, command +1144 | tw-454 sub_B05A30: +1184/+1188, command +912 |
| Text setter | cn-774 sub_F505A0, callback then string-copy fallback | tw-454 sub_EEE790, callback then string-copy fallback |
| Notification wrapper and owner | cn-774 sub_A31C00 / cn-774 sub_A1CAC0, MainApp +412 | tw-454 sub_DBC470 / tw-454 sub_B50570, MainApp +388 |
| Chat caller and insertion | cn-774 sub_BBC600 / cn-774 sub_56FBD0, MainApp +480 | tw-454 sub_A99620 / tw-454 sub_A0CCF0, MainApp +456 |
| Banner wrapper and vector | cn-774 sub_A1D9D0, MainApp +364 | tw-454 sub_B51470, MainApp +340 |
| Banner insertion and copy | cn-774 sub_A2DFD0 / cn-774 sub_A2DF50 | tw-454 sub_B5F550 / tw-454 sub_B5F4D0 |
| Clock and renderer | cn-774 sub_F40160 / cn-774 sub_A1BF50 | tw-454 sub_EDB9A0 / tw-454 sub_B4FA10 |

The UI allocator globals are VA 0x177D128 for cn-774 and VA 0x163EFF0 for
tw-454. Both resolve the allocator object and invoke virtual slot +4 with
zero, allocation size and a stack allocation context. The apparent fourth
argument in the tw-454 decompilation is a saved ESI: tw-454 sub_9F0710 pushes
it at VA 0x9F0755 and restores it at VA 0x9F087E. Both wrappers retain
reference +4 and release primary slot +4 when the decrement reaches zero.

The tw-454 decompiler also omits the last modal flag. Instructions at
VA 0x9F0844 through VA 0x9F084C push 0,1,1,0 before the virtual call.
It selects existing `modal_abi = 1`; cn-774 selects the independent
interruption-byte convention `modal_abi = 2`. Neither descriptor claims an
icon-free layout byte. Each build's own `Ui/DlgMessageBox.ui` maps notice,
warning, question and error to pnlIcon entries 0,1,2,3.

`chat_abi = 5` adds a reusable thiscall with caption, color, body and a fourth
zero stack argument. Both callees return with RET 16 and use the active chat
view at module +72. tw-454 sub_A99620 explicitly passes zero; cn-774
sub_56FBD0 does not consume its fourth argument.

`announcement_layout = 2` selects a 68-byte record: text +0, color +24,
tick +28, duration +32, secondary string +36, type +60 and fade +64.
The copy functions deep-copy both strings. `announcement_abi = 2` passes the
vector in ECX and end iterator/source record on the stack to the native
reallocation insertion function. It copies the new record before moving old
storage; the source is not retained. `clock_abi = 2` selects a no-argument
cdecl game clock, including paused state. Existing 76-byte records and
EBX/ECX insertion remain the defaults for earlier descriptors.

cn-774 sub_BA69E0 and tw-454 sub_A842D0 supply 13000 ms and type zero to their
announcement wrappers. The descriptors preserve that lifetime, yellow color,
type zero and the native 2000 ms fade. Renderers expire and release entries;
the plugin does not replay notices or evict existing entries from a full vector.

## Validation and next acceptance

Eleven descriptors pass `tools/native-ui/verify-descriptors.py` against their
respective unpacked images. This verifies identity, code signatures, modal
vtable target and dialog bounds; it does not prove presentation or lifetime in
a running game. The four new ports additionally have independently read icon
resources and consuming paths documented above and in their TOMLs.

Retail acceptance is still required for cn-706, cn-404, cn-406, cn-735, cn-774 and tw-454, as
well as the previously static-only ports. cn-735's build remains hidden and
disabled because server adaptation is incomplete; adding a UI descriptor does
not enable that build. Use the checklists in `native-game-controls.md` and
record build, image fingerprint, game stage, outcomes and observed expiry.

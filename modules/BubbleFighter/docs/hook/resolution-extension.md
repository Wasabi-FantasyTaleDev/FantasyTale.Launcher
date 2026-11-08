# Resolution Extension: native display-mode table

This optional plugin expands the original resolution dropdown without replacing
the game's settings window, display-device initialization or option-file writer.
The shared implementation covers eight independently described fixed-table legacy
builds. kr-1256 is the comparison build and already has a dynamic list. cn-279 and
cn-324 are excluded because they have no native resolution switching.

## Client evidence

| Build and consuming path | Established behavior |
| --- | --- |
| kr-812 sub_ABBFE0 | Enumerates the selected Direct3D9 adapter and format into 16-byte `D3DDISPLAYMODE` records at graphics object offsets 1800/1804. |
| kr-812 sub_ABA920 | Tests width and height against that native mode vector. The plugin preserves this call. |
| kr-812 sub_694130, 0x696109 through 0x6961D6 | Iterates seven 8-byte width/height pairs at 0xE68518 through 0xE68550. An additional `index <= 6` condition restricts the same loop. Each candidate passes sub_ABA920 before a dropdown row is created. |
| kr-812 sub_696B50 / sub_696A90 | Creates a text row and copies the selected row's text into the resolution control. Selection does not map through a seven-element index array. |
| kr-812 sub_696C40 | Copies selected text into the saved settings object's resolution string. A resolution change uses the existing restart-required notification. |
| kr-812 sub_8E3AA0 | Writes that resolution string under `resolution` in `Option.ini`, alongside the existing screen-scale option. |
| kr-812 sub_8E8E30, 0x8E92F4 through 0x8E9335 | Parses `%d x %d`, validates against the same fixed table and substitutes 1024x768 when absent. The search pointer starts at table+4 and ends at table_end+4. The accepted width/height feed the existing display-size globals. |
| kr-1256 sub_102CD90 | Enumerates Direct3D9 modes into a dynamic width/height vector at graphics object offset 1788, deduplicates and filters aspect ratios below the float constant 1.78. |
| kr-1256 sub_5BCE20 | Populates the resolution popup from that vector, retaining 800x600 or dimensions of at least 1024x768. |
| kr-1256 sub_5BD360 | Copies selected resolution text through the normal popup callback. |

The original kr-812 candidate table is 800x600, 1024x768, 1152x864, 1280x800,
1280x960, 1366x768 and 1600x900. These seven candidates do not imply seven visible
menu choices: the native adapter predicate filters them. The maintainer's cn-406
native menu exposes 800x600, 1024x768, 1366x768 and 1600x900. Replacing entries in place would overwrite adjacent data
when the table grows, so the plugin owns a separate process-lifetime table.

### Additional independently checked builds

All addresses below are preferred-base VAs in the named build. Iterator and
validator columns identify instruction sites, not function entry points. Each
menu constructs text rows, advances by eight bytes and retains its display
predicate. Each validator compares both dimensions, advances by eight bytes and
retains the original 1024x768 fallback.

| Build | Menu iterator VA | Native predicate | Text-row creator | Saved validator VA |
| --- | --- | --- | --- | --- |
| cn-406 | 0x58A7E0 | cn-406 sub_9B71F0 | cn-406 sub_58B180 | 0x7D5FDC |
| cn-404 | 0x6444C1 | cn-404 sub_A6CC30 | cn-404 sub_644E60 | 0x89623C |
| cn-706 | 0x628311 | cn-706 sub_A3C4D0 | cn-706 sub_628CB0 | 0x871B84 |
| cn-717 | 0x6F2288 | cn-717 sub_B81A50 | cn-717 sub_6F2C40 | 0x985392 |
| cn-735 | 0x840A44 | cn-735 sub_D40060 | cn-735 sub_8416F0 | 0xB1C260 |
| id-400 | 0x5FE39D | id-400 sub_A5BBF0 | id-400 sub_5FEEB0 | 0x86082C |
| id-1809 | 0x64797A | id-1809 sub_A51C60 | id-1809 sub_6483C0 | 0x8872A7 |

Each descriptor records independently read PE timestamps and packed/unpacked
image sizes. Read-only validation matches five instruction sites, seven original
modes and the native predicate guard in every build. Executable-section searches
found only the four adapted original-table pointer operands per build; their
consuming loops were checked on instruction boundaries. cn-717's short count
branch is removed without its following null guard; cn-735's counter increment
remains before its end test.

The cn-735 descriptor does not enable its globally disabled client profile.
cn-774 and tw-454 belong to a later generation and are not declared compatible
with this fixed-table strategy. These ports add no per-build C++ branches.

## Implementation and ownership

`native/resolution-extension` builds independently against the pinned FtHook
3.3.3 public C ABI and Windows Direct3D9. It does not link the launcher runtime
or call its private C++ helpers. No runtime version change is needed.

At entry, the plugin enumerates X8R8G8B8 and R5G6B5 modes across adapters. It
retains the original choices, adds modes satisfying the descriptor's dimensions
and aspect policy, removes duplicate width/height pairs and sorts by width/height.
The native selected-adapter check remains the final dropdown filter. Different
refresh rates therefore produce only one visible choice.

The declared string option `custom_resolutions` adds explicit candidates while
preserving the original and automatic policies. Explicit entries can supplement
the automatic aspect-ratio policy, including ultrawide sizes, but still pass the
game's selected-adapter check. Unsupported hardware modes are never forced.

The parser accepts positive signed-32-bit width/height pairs with `x` or `X`,
ASCII whitespace and semicolon, comma or newline separators. Input is bounded
at 4096 UTF-8 bytes and 256 entries. Malformed input fails before staged writes.
All candidates are deduplicated and sorted together.

`plugin-ui.toml` exposes packaged inline `settings.html` through the existing
launcher plugin settings contract. Its six-language controls modify only custom
entries and keep Add visible while long lists scroll. It accepts messages only
from its parent and emits draft changes; launcher Save/Cancel owns persistence.
Host echoes preserve unfinished inputs. Invalid saved text is not erased and
can be corrected through the standard form. Changes apply on the next launch.

The descriptor rebinds four instruction operands: menu begin/end and saved-option
validator height begin/end. A fifth staged write removes the redundant seven-item
count guard. The iterator-end comparison remains unchanged. The replacement
table has a stable allocation until process exit; it is never resized after its
address has been staged.

A declarative unpacked prerequisite precedes all five native writes. Its locator
checks the image timestamp/size, original table, native display predicate and
every instruction in the write plan. Packed or mismatched bytes return not ready,
and the runtime timeout ends activation without writing any site. All writes then
use the runtime's existing transaction and rollback journal. The mode-table
extension installs no observer, new game thread, raw live write or file replacement.
The optional stage adapter below adds two verified loader calls. Failed
activation frees the table after runtime rollback; uncertain rollback retains
the native plugin under the runtime's existing lifetime contract.

## Explicit custom menu candidates

Each build's `menu_filter_call` pins the settings menu's exact five-byte E8 call
to its existing display predicate. The replacement uses a fastcall thunk for the
original thiscall/ret-8 ABI, preserves ECX and the two dimensions, and calls the
native predicate first. Only an explicitly configured non-stock size fitting the
active monitor and covering an original candidate can replace a false result.
The Direct3D9 mode vector and device initialization predicates remain unchanged.
The locator verifies this call alongside every table operand before activation.
Cleanup clears the callback owner only after the runtime has restored its calls.
The offline verifier checks the target and expected instruction for every build;
native fixtures check both native and custom results, rejection and stack balance.

## CN2013 main-stage canvas

Only the cn-406 descriptor enables `main_stage`. cn-406 sub_578B90 loads
`Ui/MainMenuStage2.ui` at 0x578CEF or 0x57938A on exclusive construction branches.
That resource gives the root a fixed 1600x900 size. cn-406 sub_964C40 intersects
the root rectangle with the current viewport before calling cn-406 sub_57AD00,
which draws the stage UI and the scene through cn-406 sub_5CA860. The fixed root
therefore clips larger displays although the graphics device has accepted them.

The adapter replaces only these two five-byte calls with a thiscall-compatible
loader wrapper. It invokes cn-406 sub_96C170 once with the original object and
resource argument and preserves its return value. After loading, original native
table candidates return without calling a size setter, including the four modes
visible in the maintainer's native menu.
The screen manager's physical dimensions at +24/+28 are the same fields consumed
by cn-406 sub_9748A0. An extended mode may expand each canvas dimension to the
larger of the stock canvas and the initial logical UI size. The wrapper saves
that size before loading the resource: cn-406 sub_9748A0 already divides the
physical window dimensions by the UI scale when constructing the stage.
No canvas dimension shrinks; a modified non-stock root is retained with a diagnostic.

Expansion calls cn-406 sub_9656A0 and cn-406 sub_9656D0, the same width and height
setters used by cn-406 sub_96BAC0 for an automatically sized root. They retain
cn-406 sub_964FA0's layout callbacks and the native centered child controls.
The camera projection, input pipeline, resources and files are not rewritten.
Native load calls and all three function signatures join the existing unpacked
preflight and transaction. Rollback removes the calls before freeing their state.
The loader guard skips its relocated exception-handler operand; call displacements
and setter guards remain unchanged when the image is rebased.

Regression fixtures execute both x86 callsites and verify object/argument identity,
stack balance, original return values, native-mode preservation, scaled logical
sizes and repeated construction. Static checks establish the exact sites in the
unpacked cn-406 image. Full lobby rendering and mouse acceptance still require
an original-client run.

## Descriptor contract

`games/BubbleFighter/plugins/bubblefighter.fantasy.resolution-extension/builds/<build>.toml`
contains the schema/build identity, PE identities, preferred base, original table,
display predicate guard, mode-selection limits and exact instruction sites.
Each site has an RVA, exact expected bytes and one target:

- `begin` / `end`: first width and one-past-last width.
- `height_begin` / `height_end`: first height and table_end+4.
- `remove_count_guard`: the proven redundant index test and its branch.

Pointer targets also declare the four-byte operand offset. The reader rejects
unknown keys, wildcards, overlapping sites, incorrect original pointer operands,
out-of-image ranges, missing target families and x86 address overflow. Signed
pointer comparison ranges may not straddle 2 GiB. Count-guard removal accepts only
the original last-index compare followed by an unsigned above branch, rejecting
extra instructions. Packed and unpacked images have independently recorded sizes.

Another build needs only a descriptor and compatibility row **when its consuming
paths have this same table/string model**. Each new build still needs its own
evidence and exact bytes. Builds with fixed-index selection, different persistence
or no native resolution support require a separately established strategy.

## Validation and acceptance

Build and run from the game-module repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-native-sdk.ps1
msbuild native/resolution-extension/resolution_extension.vcxproj -p:Configuration=Release -p:Platform=Win32
msbuild native/tests/resolution_extension_tests.vcxproj -p:Configuration=Release -p:Platform=Win32
artifacts/resolution-extension/resolution_extension_tests.exe
artifacts/resolution-extension/resolution_extension_tests.exe --display-modes
node --test tools/tests/resolution-settings.test.mjs
python tools/verify-resolution-descriptor.py games/BubbleFighter/plugins/bubblefighter.fantasy.resolution-extension/builds/kr-812.toml <unpacked-client.exe>
powershell -NoProfile -ExecutionPolicy Bypass -File tools/package-resolution-extension.ps1
```

The native checks exercise every descriptor, custom option handling, mode
policy/deduplication, pointer rebasing, both iterator
sentinels, descriptor rejection, packed-image waiting, tampered instruction/table
rejection, staging failures, cleanup and actual DLL entry/loading with an isolated
synthetic image. The display-mode command enumerates this machine's hardware;
it does not start a game. The PE verifier is read-only. A clean build must produce
zero warnings.

The maintainer confirmed the initial kr-812 extension in the game. The new custom
settings and seven other ports still need the following real-client acceptance:

- Menu includes additional hardware-supported choices without duplicates and scrolls correctly.
- Choose 1920x1080, acknowledge the original restart message, exit, then relaunch with the plugin enabled.
- Confirm the chosen dimensions survive `Option.ini` reload and match the actual client area/backbuffer.
- Repeat for another supported aspect ratio, windowed and fullscreen modes.
- Confirm settings and lobby layout, HUD/camera framing, mouse hit testing and overlays.
- Disable the plugin for the next launch: the original menu and original out-of-table fallback return.
- Confirm normal startup and settings persistence while other selected plugins are enabled.
- Save/cancel custom entries through the launcher and verify supported custom
  modes, duplicate suppression and removal on the next launch.

Static, synthetic and import results do not satisfy these presentation/behavior
checks. Do not add other legacy builds to compatibility before their independent
descriptor evidence and validation are complete.

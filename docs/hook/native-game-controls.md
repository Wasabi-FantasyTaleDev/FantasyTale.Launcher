# Native game controls

The developer test surface must exercise the game's own controls through the
Game Control Support plugin. An Overlay panel is not evidence that a native
operation worked. Companion routing and fallback belong to Communication.

## Implementation boundary

`IGameNativeUi` and its request/result records live in the existing Launcher SDK
so callers can share contracts without referencing a provider implementation.
`GameNativeUiClient` transports those contracts; the Host and developer page are
callers. The game-specific implementation belongs to Game Control Support and
must register the launch-local `game.native-ui` service after activation commits.
Other native plugins can invoke that service through the optional host service
table. An interface or transport does not provide a native control on its own.

Game Control Support is now a native package registering `game.native-ui` through
the runtime 3.5 service API. Presentation is scheduled on the game window's
message thread. The maintainer confirmed kr-812 message boxes and text
notifications in the actual game. The adapter now also implements the four
resource-verified icons, automatically expiring announcements and local chat
with one foreground color per entry; the maintainer subsequently confirmed
these kr-812 additions in the actual game. The kr-1256 adapter implements message boxes, notifications and
uniform-color local chat, with static evidence and compilation checks only.
The id-400, id-1809 and cn-717 descriptors now cover message boxes, text
notifications, expiring announcements and uniform-color local chat. These three
ports have static image verification and synthetic ABI tests; actual game
presentation, repeated-call lifetime and stage transitions still need acceptance.
Unsupported operations do not substitute Overlay or claim native presentation.

The dispatcher selects the largest visible, unowned game window and delivers a
sent notification from its own worker. A `WH_CALLWNDPROC` callback executes the
queued operation on that window's thread. This also reaches clients whose message
pump filters posted custom messages (cn-279 sub_4A9100). The service callback
returns promptly while a native modal remains open; it never waits for a button.

The CN2011 (`cn-279`) and CN2012 (`cn-324`) adapters expose a two-button question
box through the same service. Their native string objects use a game-owned
UTF-16 pointer rather than the later inline string layout. Each descriptor
specifies the verified string constructor, release function, button text slot,
and modal call. The caller owns fallback policy and handles the returned button ID.
Other native control operations on these two clients remain unsupported.

The early adapters require the manager's root, the application's active form,
and readable exit/transition fields before construction. The descriptor keeps
these layout differences as data. A validated dialog that fails preparation is
detached from the parent's child list before its owned reference is released.
The modal loop may still dismiss a dialog when the game changes scenes.

The earlier Companion notification receiver now belongs to Communication, with
its existing Overlay delivery retained there. Native-first Companion routing is
separate follow-up work; the developer surface calls the native provider directly.

Other native plugins obtain the optional service table using
`fthook_get_services(host)`, then call `invoke` with service `game.native-ui` and
a TOML request. Caller ownership is supplied by the runtime, not by request text.
Actions are `capabilities`, `invoke`, and `result`; `invoke` carries the same
operation fields as the managed contract using snake_case keys. Replies are JSON.
The operation ID is unique within a caller's retained results for one launch.
Identical retries return the recorded result while retained; conflicting requests
fail. The configurable result history evicts only terminal entries, so callers
must not replay an operation after its result becomes `operationUnavailable`.
Polling never repeats presentation. Successful native plugins live until process
exit; this adapter does not support live unload while a modal call is active.

## Required operations

| Operation | Parameters | Observable outcome |
| --- | --- | --- |
| Message box | Title, body, icon, buttons with caller-owned IDs and labels | Shown, selected button ID, dismissed or failed |
| Text notification | Text | Presented or not presented |
| Announcement | Text, optional caller-owned announcement ID where supported | Shown and automatically expired by the game |
| Chat entry | Normal or system entry, ordered text runs with individual colors | Inserted locally or not inserted |

Button labels may be empty or consist entirely of spaces: some native button
textures already contain their labels. The caller's text is not trimmed or
replaced. Button IDs remain nonempty and unique, independent of visible labels.
Notifications retain embedded LF and CRLF line breaks; the test page uses the
same multiline editor as message-box content.

Chat requests accept `chatCaption` (`chat_caption` in the native TOML service).
An omitted caption defaults to `Fantasy Tale` for system style and to an empty
caption for normal style. An explicitly empty caption remains empty. All native
adapters copy the supplied caption into the same local insertion call as the body.
The developer page exposes it as an editable message source.

Colors accept `#RRGGBB` with opaque alpha or explicit `#RRGGBBAA`. The test page
offers a visual picker, synchronized six-digit hex and 0-255 RGB fields, and
fourteen presets from kr-1256 sub_4D78B0's native chat palette. Custom colors are
saved in local frontend preferences and can be reused or removed. No chat text
or credentials are stored with these preferences.

Announcements use the native translucent banner and expire after the game's
normal display duration. There is no periodic replay or Launcher-owned expiry
timer. An adapter may expose explicit closure independently, but closure support
does not imply persistence. The current kr-812 adapter exposes automatic expiry
only. Its native renderer, kr-812 sub_8E23D0, interprets duration zero as 13,000 ms
and applies the record's 2,000 ms fade. The renderer owns removal and destruction.
Chat tests insert local entries; they do not send chat packets to other players.
The current adapters support one color per entry. Different-colored runs in one
entry remain unsupported and are rejected rather than flattened or split.

## Research sequence

Start with kr-812, establish each consuming path and its lifetime, then inspect
kr-1256. Other client builds require their own verified adapters. A package's
wildcard compatibility declaration is not a native-capability declaration.

The test page is named `游戏组件测试` in Simplified Chinese and follows the
store-source management tab. It selects
a running client, shows per-operation availability and supplies separate tests
for message boxes, text notifications, expiring banners and chat entries. It
must display button outcomes without blocking the Launcher UI. Unknown outcomes
must not be retried automatically.

## kr-812 evidence established so far

| Consuming path | Observation | Remaining verification |
| --- | --- | --- |
| kr-812 sub_892470 | Constructs DlgMessageBox and binds its title, body, icon array and two button controls; kr-812 sub_896B00 proves ECX/EDX coordinates and five stack arguments | Runtime validation of custom labels and UI-thread invocation |
| kr-812 sub_893430 | Selects the icon and button mode; assigns button command values 257 and 256 | Map dismissal separately from explicit selection |
| kr-812 sub_8935A0 | Measures and lays out message text and buttons; flag +776 hides icons | Runtime layout after changing button labels |
| kr-812 sub_7B1D80 | Retains the dialog, invokes primary vtable slot 208, reads command 257, and releases through the secondary vtable | Runtime button selection and repeated-call lifetime |
| kr-812 sub_4724B0 | Cdecl text-notification wrapper, passing MainApp to kr-812 sub_8E2A80 | Runtime presentation in multiple game stages |
| kr-812 sub_8E2A80 | Owns the stage's transient notification at offset 424 and replaces an earlier notification | Exact timing and availability across game stages |
| kr-812 sub_8EFF90 and kr-812 sub_8F2F70 | Append and deep-copy the 76-byte announcement record, including both native strings | Runtime presentation, expiry and repeated insertion |
| kr-812 sub_8E23D0 | Draws translucent type-1 banners and removes expired records using the game's clock | Runtime fade and removal without replay |
| kr-812 Ui/DlgMessageBox.ui | Maps notice, warning, question and error to icon array entries 0, 1, 2 and 3 | Runtime appearance for each icon |
| kr-812 sub_7C9360 | Copies caption, body and one color into local chat without sending a packet | Runtime insertion and colors; different colors within one entry remain unsupported |

The announcement adapter follows the native seven-entry limit without evicting
game-owned announcements. It returns busy when the native vector is full.
This is local caption formatting, not a server packet or a network chat sender.

## kr-1256 evidence established so far

| Consuming path | Observation | Remaining verification |
| --- | --- | --- |
| kr-1256 sub_6265D0 and kr-1256 sub_627630 | Copy title/body, configure the four resource-verified icons and bind commands 257 and 256 | Runtime labels, icon appearance and selected-button results |
| kr-1256 sub_899620 | Uses the UI allocator, retains the dialog and releases through the primary destructor slot | Repeated-call lifetime in the actual client |
| kr-1256 sub_FCDFD0 | Runs the native modal loop with a separate interruption byte | Runtime dismissal and interruption |
| kr-1256 sub_D5EB80 | Receives a borrowed native string in ECX and invokes the notification owner | Runtime presentation in different stages |
| kr-1256 sub_4DC0D0 | Copies explicit caption/body UTF-16 pointer-length pairs and a single color into local history | Runtime local insertion and colors |

The kr-1256 announcement path and icon-free message boxes remain unsupported.
Additional builds require individually verified descriptors and adapters.

The adapter validates the main-image timestamp and exact code signatures before
calling a function. New adapter findings remain static until runtime acceptance;
capability availability alone does not establish successful game presentation.

## Legacy adapters and reusable calling conventions

Every build has its own `native-ui/<build>.toml` inside Game Control Support.
Addresses are RVAs in that build's unpacked main image. Layout offsets, icon
indices, native allocation size and calling conventions are descriptor data;
the shared provider does not compare build IDs or search for approximate code.
An unsupported image has no native capability until its descriptor is verified.

Constructor and modal conventions are independent: id-1809 uses the same stack
constructor as id-400 but the four-flag modal used by kr-812. Changing one must
not implicitly select the other. All existing descriptors now state `modal_abi`.

CN2011 uses legacy constructor convention 4, established by cn-279 sub_7AA7D0
calling cn-279 sub_8A2F10. CN2012 uses convention 5, established by cn-324
sub_576670 calling cn-324 sub_75C8B0 with both register coordinates set to 0x8000.
Both use the three-flag modal at vtable slot +212, with their native question
icon and yes/no command IDs 257 and 256. CN2011 has passed actual display,
two consecutive button selections, and continued-process checks through the
existing developer service pipe. CN2012 has also passed actual display and two
consecutive button selections. Both builds still require ordinary-match
departure and stage-transition checks in the consuming ESC plugin's live flow.

| Key | Value | Calling convention and evidence |
| --- | --- | --- |
| `message_abi` | 1 | Coordinates in ECX/EDX, then storage/title/body/mode/icon; kr-812 sub_896B00 and cn-717 sub_9255E0 |
| `message_abi` | 2 | Storage in ECX, then title/body/mode/icon; kr-1256 sub_899620 |
| `message_abi` | 3 | Five stdcall arguments: storage/title/body/mode/icon; id-400 sub_739FF0 and id-1809 sub_75D190 |
| `modal_abi` | 1 | Thiscall with flags 0, 1, 1, 0; kr-812 sub_7B1D80, id-1809 sub_75D190 and cn-717 sub_82C410 |
| `modal_abi` | 2 | Thiscall with interruption-byte pointer followed by four flags; kr-1256 sub_899620 |
| `modal_abi` | 3 | Thiscall with flags 0, 1, 1; id-400 sub_739FF0 |
| `notify_abi` | 1 | Cdecl borrowed-string wrapper; kr-812 sub_4724B0 and cn-717 sub_4858D0 |
| `notify_abi` | 2 | Borrowed string in ECX; kr-1256 sub_D5EB80 |
| `notify_abi` | 3 | Duration in ECX, then application/body/sound/two defaults; id-400 sub_93E8A0 |
| `notify_abi` | 4 | Seven stdcall arguments including duration and final style byte; id-1809 sub_95AB50 |
| `chat_abi` | 1 | Thiscall caption/color/body; kr-812 sub_7C9360, id-1809 sub_7740C0 and cn-717 sub_846020 |
| `chat_abi` | 2 | Thiscall color and explicit UTF-16 pointer/length pairs; kr-1256 sub_4DC0D0 |
| `chat_abi` | 3 | Module/body in ECX/EDX, then caption/color; id-400 sub_754ED0 |

The three added legacy builds use the same 76-byte announcement record and
ECX-source/EBX-vector append convention. The evidence is id-400 sub_8669A0 /
id-400 sub_869520, id-1809 sub_88D5D0 / id-1809 sub_8901D0, and cn-717
sub_98C6E0 / cn-717 sub_98F7B0. Their renderers (id-400 sub_85A0B0,
id-1809 sub_8809E0 and cn-717 sub_97D570) read the native clock and own expiry.
Their message-box resources independently map the four icon indices 0 through 3
to notice, warning, question and error. Each descriptor cites its own consumers.

Run `python tools/native-ui/verify-descriptors.py --client "id-400=<unpacked.exe>"`
with one or more `--client BUILD=EXE` arguments to verify code signatures, image
identity, dialog offset bounds and the modal vtable target without launching a
game. The tool uses Python's standard library and does not modify client files.
`native/out/Release/game_controls_tests.exe` additionally calls synthetic x86
callees through every supported constructor, modal, notification and chat ABI,
including id-1809's mixed constructor/modal pair, empty captions and multiline text.

The remaining declared legacy builds do not gain capabilities from these ports.
They need individual consuming-path, resource and executable verification before
adding their descriptors. No game executable or extracted resource is distributed.

## Additional Chinese client ports

cn-706, cn-404, cn-406 and cn-735 now have descriptors for message boxes, button
results, multiline text notifications, automatically expiring banners and
uniform-color local chat with custom captions. Their consuming paths, resource
verification, scan corrections and remaining builds are recorded in
[porting research](native-game-controls-porting-research.md). These ports are
statically verified and exercise synthetic native callees; retail-game acceptance
remains open. cn-735's disabled build definition is unchanged.

The first three ports reuse existing code entirely. cn-735 adds a reusable
contextual allocation convention (`allocate_abi = 2`) and ESI/EDX chat convention
(`chat_abi = 4`), each selected in TOML. Allocation, text setting and chat-module
selection are independent of constructor ABI. Its explicit
`announcement_duration = 13000` follows cn-735 sub_A1E160 because cn-735
sub_B14050 does not substitute a default for zero. The game owns expiry.

## Retail acceptance checklists

Every unchecked item below is unverified in a running retail game. Record the
exact image timestamp, stage and caller outcomes; do not infer acceptance from
the developer page showing an available capability.

### kr-1256

- [ ] Open one-button and two-button boxes with custom title/body and verify
  returned caller button IDs for both choices and dismissal/interruption.
- [ ] Verify notice, warning, question and error icons. Icon-free boxes remain
  unsupported and must report that limitation.
- [ ] Submit empty and space-only labels and verify that the requested text is
  preserved, including native textures with baked-in labels.
- [ ] Show LF and CRLF multiline notifications in the lobby and another stage.
- [ ] Confirm announcements remain unsupported without presenting a fallback
  inside Game Control Support.
- [ ] Insert uniform-color normal/system chat with custom, empty and long captions.
- [ ] Repeat all supported calls, including after closing boxes and changing
  stage; verify responsiveness and correct ownership without duplicate insertion.

### id-400

- [ ] Verify one/two buttons, each returned ID, dismissal, all four icons and no icon.
- [ ] Verify custom title/body, multiline text, empty labels and space-only labels.
- [ ] Verify LF/CRLF notifications and replacement of an earlier native notification.
- [ ] Show a translucent announcement once, observe its normal lifetime, fade and
  removal, then repeat; no provider replay should occur.
- [ ] Insert normal/system local chat with uniform color and custom/empty captions.
- [ ] Repeat calls and change stage; verify the three-flag modal and secondary-object
  release do not leave a stuck modal, duplicate chat or an unresponsive game.

### id-1809

- [ ] Verify one/two buttons, caller IDs, dismissal, four icons and no icon.
- [ ] Verify custom multiline body, empty labels and space-only labels.
- [ ] Verify LF/CRLF notifications and replacement through the seven-argument owner.
- [ ] Verify the translucent announcement's automatic lifetime, fade and removal.
- [ ] Insert normal/system local chat with uniform color and custom/empty captions.
- [ ] Repeat calls across stage changes; verify the mixed stack-constructor/four-flag
  modal convention and release remain stable.

### cn-717

- [ ] Verify one/two buttons, caller IDs, dismissal, four icons and no icon.
- [ ] Verify custom title/body, multiline content, empty labels and space-only labels.
- [ ] Verify LF/CRLF notifications and replacement of earlier notifications.
- [ ] Verify the native translucent announcement expires and fades without replay.
- [ ] Insert normal/system local chat with uniform color and custom/empty captions.
- [ ] Repeat calls and change stage; verify modal closure, button results and
  object lifetime remain correct.

Apply the same six categories to cn-706, cn-404, cn-406 and cn-735. For cn-735,
also observe that a banner remains for 13 seconds followed by its native fade,
and exercise repeated ESI-based chat calls without corrupting later game actions.

## cn-774 and tw-454 static ports

Game Control Support 0.3.1 adds cn-774 and tw-454 descriptors for message boxes,
button outcomes, multiline notifications, expiring banners and uniform-color
local chat. Both use the native text-setting wrapper, including its string-copy
fallback when the virtual callback declines. Four resource-verified icons are
available; icon-free boxes remain unsupported until a consuming path is proved.

The shared provider adds a four-stack-argument chat convention, a 68-byte
announcement record, end-iterator insertion and a global game clock. Each is
selected independently by TOML; no build-ID branch is added. Earlier descriptors
retain their existing conventions. Build-qualified evidence is in the porting
research document.

Apply the six acceptance categories above to both builds. Check the two modal
conventions, returned IDs, blank labels, custom captions, stage changes and
repeated calls. Observe the native 13000 ms banner duration and 2000 ms fade
without provider replay. Neither port has retail-game acceptance yet. cn-279
and cn-324 still need a separately verified pointer-string adapter.

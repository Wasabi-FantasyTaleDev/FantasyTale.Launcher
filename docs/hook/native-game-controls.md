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

The shared hook channel rejects concurrent admission before sending any bytes.
`InvokeAsync` reports this as `busy` with reason `channelBusy`; capability and
result queries throw `GameNativeUiBusyException`. Callers may defer or retry the
unsubmitted request within a deadline. After an operation is queued, retry only
its result query. A lost response, service failure or cancellation after submission
can leave the operation's outcome unknown and must never authorize replay.

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
The cn-406 descriptor adds native controls with the kr-812 Control layout, a
member-map selection group for `Ui/DlgCreateRoom8.ui` and `click_abi = 2`; it has
static image verification and synthetic tests only and still needs acceptance in
the actual game.
The cn-774 descriptor adds native controls with the kr-1256 Control model
(`control_abi = 2`) at cn-774's own offsets, a record-based selection group for
`Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui` and `edit_text_abi = 2`; it has
static image verification and synthetic tests only and still needs acceptance in
the actual game.
The cn-324 descriptor adds native controls with the kr-812 Control model at
cn-324's own offsets, `control_text_abi = 2`, `click_abi = 2` and inline-header
member-map selection groups (`member_layout = 2`) for `Ui/DlgCreateRoom5.ui` and
`Ui/DlgCreateRoom3.ui`; it has static image verification and synthetic tests only
and still needs acceptance in the actual game.
The cn-279 descriptor adds the same model at the same offsets with an inline-header
member-map selection group for `Ui/DlgCreateRoom2.ui`, the only resource its
DlgCreateRoom constructor loads; it has static image verification and synthetic
tests only and still needs acceptance in the actual game.
The cn-404 descriptor adds native controls with cn-406's Control model and base
offsets (edit limit +616) and a form-value selection group for
`Ui/DlgCreateRoom13.ui`: cn-404 sub_826F70 writes the chosen mode at +728 and
cn-404 sub_626580 reads it on submission. It has static image verification only
and still needs acceptance in the actual game.
The cn-706 descriptor uses the same model and offsets with a form-value
selection group for `Ui/DlgCreateRoom.ui`, the only resource cn-706 sub_7F9B30
loads: cn-706 sub_7FD790 writes the chosen mode at +720 and cn-706 sub_608FD0
reads it on submission. It has static image verification only and still needs
acceptance in the actual game.
The cn-717 descriptor keeps those field offsets, but its text setter and getter
sit at vtable slots 24 and 28, and its button handler, cn-717 sub_B53360, takes the
"clicked" byte as kr-812's does (default `click_abi`, `click_hook_bytes = 6`). Its
form-value selection group for `Ui/DlgCreateRoom.ui` reads +720, written by
cn-717 sub_8FB420 and read on submission by cn-717 sub_6D17A0. It has static image
verification only and still needs acceptance in the actual game.
The cn-735 descriptor uses the same model with its own offsets: name, array index,
geometry and visible/enabled sit eight bytes earlier than in cn-717, the parent is at
+372, the owner form at +376 and the children at +380, the text setter and getter
are vtable slots 28 and 32, and the UI manager keeps its root and layer at +20 and
+24. cn-735 sub_A88A90 loads `Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui` when
cn-735 sub_CB5840 reports features 507 and 23, and `Ui/Stages/Lobby/DlgCreateRoom.ui`
otherwise; that feature table is filled in protected code, so the descriptor has a
form-value selection group for each resource, both reading +800 (written by
cn-735 sub_A8D0A0, read on submission by cn-735 sub_828F90). It has static image
verification only and still needs acceptance in the actual game.
The id-400 descriptor keeps cn-404's Control model and offsets; each function was
matched from cn-404 and its offsets reread in id-400's own code, with the root and
layer read by id-400 sub_438300 and sub_A05A20 at UI manager +24 and +28. Its Create
Room dialog, `DlgCreateRoom_ID`, is built by id-400 sub_462360, which loads
`Ui/DlgCreateRoom13.ui` under feature 87 and `Ui/DlgCreateRoom12.ui` otherwise; the
id-400 `Ui/Ui.bfz` ships only the latter, so that is the one resource bound. Like
cn-406 it keeps no mode field: the member-map selection group reads the map at +768,
whose first disabled member id-400 sub_465780 returns and id-400 sub_4656C0 maps to
the mode id-400 sub_5E0D00 requests. It has static image verification only and
still needs acceptance in the actual game.
The id-1809 descriptor keeps id-400's Control model and offsets, each function
matched from id-400 and its offsets reread in id-1809's own code. Its
`DlgCreateRoom`, built by id-1809 sub_8136D0, always loads `Ui/DlgCreateRoom.ui`
and, unlike id-400's, stores the chosen mode: id-1809 sub_8172E0 writes +736 and
id-1809 sub_6290F0 reads it on submission, so the descriptor has a form-value
selection group. The mode buttons that id-1809 sub_816DF0 shows only under client
feature switches sit across btnCreate and below the form's edge in the resource,
so the entry is placed on the native button row as for the other builds. It has
static image verification only and still needs acceptance in the actual game.
The tw-454 descriptor uses cn-774's Control model with its own offsets, read from
tw-454 sub_F0E8D0 (resource attributes), sub_EEE790 and sub_F0B120 (text) and
sub_EF03E0 (input gate). Its `DlgCreateRoom`, built by tw-454 sub_ADE670, always
loads `Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui`; tw-454 sub_AE2820 matches
the clicked btnGameMode's ArrayIndex against 52-byte records at +1272 and passes
record +4 to tw-454 sub_AE2BF0, which stores the mode at +1348. It has static
image verification only and still needs acceptance in the actual game.
The cn-408 descriptor covers messaging and controls. Every function was matched
from cn-404 and compared instruction by instruction; only MainApp fields moved,
four bytes later (cn-408 sub_8942E0 notification +360, cn-408 sub_686FE0 chat
+448, cn-408 sub_8947D0 announcements +304). Its `DlgCreateRoom`, built by cn-408
sub_827560, always loads `Ui/DlgCreateRoom.ui`; cn-408 sub_82B8E0 maps the
clicked btnGameMode's ArrayIndex through cn-408 sub_82B4F0 and stores the mode at
+720, which cn-408 sub_629BF0 reads after command 257. It has static image
verification only and still needs acceptance in the actual game.
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

The Companion notification receiver in Communication is native first: a server
`ui.notification.show` becomes the game's own message box when Game Control Support
reports that operation supported and ready and every button id is a valid native
identity. Its outcome is read without presenting it again and reported as the usual
queued, shown, selected or closed results. Otherwise the existing Overlay dialog
shows it. Overlay package pages reach the same provider through
`overlayBus.nativeUi(panelId)` (`capabilities`, `invoke`, `result`, `available`),
answered by the launch's hook channel; the launcher prefixes each operation id with
the package id. A page uses its own Overlay drawing only when the native operation
is unavailable or fails. The developer surface calls the native provider directly.

The server can also ask for any native operation directly. Nirinsou Link declares
`ui.native.invoke` and `ui.native.result` (schema 1); the server's
`companion.native_ui` row sends a request with an operation id, kind, text, title,
icon, buttons, announcement id, chat style, caption, coloured runs and a `fallback`
flag. Communication's `CompanionNativeUiRequests` runs it through Game Control
Support when the operation is supported and ready, and otherwise, when `fallback`
allows, shows a message box or notification as an Overlay dialog. Each result names
its `presenter` (`native` or `overlay`): `shown`, then the selected button for a
message box, or `unsupported`, `busy` or `failed`.

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

## Live control discovery and text

The kr-812 descriptor supports `control.inspect`, `control.setText`,
`control.addButton`, `control.remove`, `control.activate` and `control.events`.
The kr-1256 descriptor supports the same operations except `control.addButton`
and `control.remove`, which it reports as `controlCreationUnsupported` because no
button factory has been verified there (see [Control ABI 2](#control-abi-2-kr-1256)).
Other builds report these operations as unsupported until their control layouts
and consuming functions are verified. These additions have static evidence and
synthetic checks; they do not inherit the earlier in-game presentation acceptance.

Every control request can use an optional `target` with `form`, `name`,
`arrayIndex`, `id` and `text`. `form` is a relative resource such as
`Ui/DlgCreateRoom.ui`; omitting it searches the current visible control tree.
`arrayIndex` qualifies a repeated native name, including index zero. `id` is an
opaque, owner-scoped handle returned by inspection, never a process address.
`target.text` is an optional exact-current-text filter. The top-level `text` is
the new value: knowing the previous value is unnecessary when using a resource,
control name or inspected handle. Empty new text clears a control.

```javascript
await nativeUi.invoke({operationId: crypto.randomUUID(), kind: 'control.inspect', text: ''});
await nativeUi.invoke({
  operationId: crypto.randomUUID(), kind: 'control.setText', text: 'New room name',
  target: {form: 'Ui/DlgCreateRoom.ui', name: 'edtRoomTitle'},
});
await nativeUi.invoke({
  operationId: crypto.randomUUID(), kind: 'control.setText', text: 'New caption',
  target: {text: 'Exact old caption'},
});
```

The overlay bridge returns `{status, result}`. A successful bridge call still
requires inspecting `result.status`: `queued` means admitted to the game thread,
and callers poll `nativeUi.result(operationId)` until terminal. `completed`
confirms the control operation; `ambiguous` returns candidates without mutating
any; absent or stale controls return `notReady`. An uncertain outcome must not
be replayed with a new identity. Companion requests and results use snake_case,
including nested `target.array_index`, `maximum_length`, `selection_value` and
event `control_id`; overlay and managed JSON use camelCase. The native TOML
service uses `array_index` inside `[target]`. The Launcher receiver accepts these
control requests, but the server's existing typed native-UI service still exposes
presentation operations only. Local overlay control operations do not depend on
that server service.

Inspection returns `controls` with `id`, `form`, `name`, `kind`, `text`,
`writable`, coordinates, the live edit `maximumLength`, `enabled` and optional
`arrayIndex`. A form-only inspection enumerates its visible descendants as well
as its own `kind: 'form'` entry. A form-only text mutation selects the form's
caption. Several instances of the same resource remain ambiguous; inspect first
and choose an opaque handle. The developer test page exposes inspection, direct assignment, optional
current-text matching and native button creation separately.

Text uses the native virtual getter/setter, preserving the edit control's own
handling (kr-812 sub_420C30/sub_420C70 and sub_A9A7A0/sub_A9A6F0).
An edit rejects over-limit text instead of silently truncating it. An explicit
`maximumLength` changes its limit through kr-812 sub_A98A90. Active IME composition
rejects mutation until composition finishes. kr-812 sub_86D310 overrides the
create-room edit's resource limit of 15 to a live limit of 40; consumers must use
the inspected value rather than assuming the resource value remains current.

## Native buttons and selection state

`control.addButton` takes a parent target, button `text`, and
`definition: {name, x, y, width, height}` in native parent coordinates.
The current kr-812 adapter constructs a real UiButton through sub_A927F0 with
the game's default skin. A `template` request is currently unsupported.
Rectangles must fit the actual parent. `control.remove` only removes controls
created by the same owner. Parent disappearance removes retained owned controls;
initialization and destruction invalidate handles before object addresses can
be reused.

`control.events` takes `afterSequence` and returns ordered click events with
`sequence`, `controlId`, `event: 'click'`, plus a `cursor` even for an empty batch.
Events and handles are scoped by the runtime caller and a trusted Host owner
scope. Overlay packages and the Companion receiver cannot select another
package's handles or consume its button events. Retention is bounded; an expired
cursor returns `eventHistoryLost` rather than silently skipping clicks.

`control.activate` invokes the verified native button click path, kr-812
sub_A91840. Descriptor `selection_groups` can describe a form's numeric selection
field and its control-index mapping, without embedding mode rules in shared
code. Such controls additionally expose `selectionValue` and `selected`.
A group either lists `values` by ArrayIndex, or names a record vector inside the
form (`records_offset`, `record_size`, `record_key_offset`, `record_value_offset`)
whose records pair a button's ArrayIndex with its value; kr-1256 DlgCreateRoom keeps
56-byte mode records this way (kr-1256 sub_5C9070, sub_5C9CA0).
A form that stores no chosen value names its member map instead of `value_offset`
(`members_offset`, `member_key_offset`, `member_control_offset`,
`member_nil_offset`): the map pairs each button's ArrayIndex with the button, and
the selection is the first member, in key order, whose button is disabled.
cn-406 DlgCreateRoom works this way: its click callback cn-406 sub_7711C0 disables
the clicked mode button and enables the others, and cn-406 sub_770E80 walks the
map at +712 on submission. The walk is bounded by the control tree limit, and a
broken link is a read failure rather than an empty selection.
`member_layout` names the tree's shape. 1 (the default) is cn-406's: the form
holds a pointer to a separate header node whose links are left +0, parent +4 and
right +8, and every leaf link points at the header, which carries the nil byte.
2 is cn-324's: the 16-byte header node sits inside the form at `members_offset`
(root +4, smallest +8, largest +12), nodes link parent +4, left +8 and right +12,
a missing child is a null link and the end of the walk is the header's own
address, so `member_nil_offset` is not accepted. cn-324 DlgCreateRoom keeps its
mode buttons this way at +352, keyed at node +16 with the button at +20
(cn-324 sub_7407D0, successor cn-324 sub_935340), and cn-324 sub_743F30 returns
the first disabled key on submission.
The create-room descriptor proves the group with kr-812 sub_870B10/sub_870ED0:
the selected button is disabled to draw its selected skin, while other disabled
buttons may be locked by game state. Activating an already-selected button
returns `completed`/`alreadySelected`; activating a different disabled button
returns `notReady`/`controlDisabled`. Successful selection is read back from the
client's own state after its callback. The bridge never writes the mode field.

`control.setEnabled` takes `enabled` and applies it through the game's own
setter, kr-812 sub_A81270, which also drops input focus from the control. kr-1256
has no such function; its create-room callback (kr-1256 sub_5C9070) clears focus
through the focus setter and writes the enabled byte, and the bridge does the same.
Other builds whose descriptor has no verified `control_set_enabled` report the
operation as unsupported. On a selection button with a form value it is cosmetic: kr-812
sub_870ED0 draws the selected mode as a disabled button, so enabling it draws it as
unselected while the form's selection value stays unchanged. On a member-map group
the disabled button is the selection, so enabling it clears the selection and the
reply reports the button as no longer selected.

The button handler takes a "clicked" byte in kr-812 and kr-1256 (kr-812
sub_A91840) and fires only when it is set. cn-406's handler, cn-406 sub_9812D0 at
UiButton slot +0xB4, takes no argument and is the activation itself: the Enter key
handler cn-406 sub_980110 calls it directly. The descriptor states this as
`click_abi = 2`, which changes both the observer and `control.activate`.
cn-324's handler, cn-324 sub_8797A0 at UiButton slot +0xB8, has the same form.

`control_text_abi` names how an ABI 1 build stores strings. 1 (the default) is
kr-812's 24-byte wide string with its length at +16 and capacity at +20, released
with `control_string_release`. 2 is cn-324's: a string is one pointer to UTF-16
data, with a 20-byte header before the data that holds the length at -20 and a
reference count at -12 (cn-324 sub_401B10, sub_480390). The bridge reads such
strings from the header, builds its own with `string_create` and releases them,
and the getters' results, with `string_release`; both functions then need
signatures. The key is only accepted with `control_abi = 1`.

`GetFormsAsync` (the `forms` action) reads the live forms loaded from one `.ui`
resource straight from the load and destroy observers, without queueing behind
game-thread UI work. Each form reports its load `sequence`, visibility, geometry
in client pixels, the UI root size, its selection-group value when the descriptor
names one, and `clicks`, the client-wide count of native button clicks.

### Control ABI 2 (kr-1256)

`control_abi = 2` describes Controls that keep their text in place. The same
fields exist as in kr-812, at different offsets, all established from kr-1256's
own code: the `.ui` attribute loader kr-1256 sub_FF4480 (the counterpart of
kr-812 sub_A843F0) writes Name, ArrayIndex, PosX/PosY/Width/Height and Visible;
the input handler kr-1256 sub_FB8120 (kr-812 sub_A80560) proves the enabled byte
and the owner form; the child lookup kr-1256 sub_FB7670 proves the children vector.

| Difference | kr-812 (ABI 1) | kr-1256 (ABI 2) |
| --- | --- | --- |
| Text read | vtable getter into a game-allocated string | `text_offset`, read in place |
| Text write | vtable setter | `control_set_text` (kr-1256 sub_FB5E70), or `edit_set_text` for edits (kr-1256 sub_FC91C0, UiEdit slot +220) |
| Edit limit | `edit_set_maximum` | the client writes `edit_maximum_offset` (kr-1256 sub_5C8330) |
| Enable | `control_set_enabled` | focus cleared through `control_focus` (stdcall, kr-1256 sub_FC04F0), then the enabled byte |
| Reference count | +8, released through the secondary vtable | +4, released through the primary vtable (no secondary base) |
| Button creation | `control_button_create` | not offered |
| Observer prologues | 7, 7, 7 and 5 bytes | `load_hook_bytes`, `initialize_hook_bytes`, `destroy_hook_bytes`, `click_hook_bytes` (9, 6, 5, 9) |

The hook lengths are copied whole into trampolines, so each must end on an
instruction boundary of position-independent code; the descriptor loader accepts
5 to 16 bytes and kr-812 keeps its defaults. The UI manager keeps the focused
control at +20 in both builds; kr-1256 keeps its root at +28, the active form at +8
and the modal form at +4 (kr-1256 sub_FB6600, sub_FC05D0, sub_FCDFD0). IME
composition is not observed on kr-1256: text set while the player is composing in
an edit replaces the edit's text as the client's own setter does.

cn-774 uses ABI 2 with its own offsets: text +596, Name +572, ArrayIndex +712,
geometry +732..+744, enabled +788, owner +820, children +840, Visible +980 and the
edit limit +1112. They come from cn-774 sub_F96CD0 (the attribute loader),
sub_F505E0, sub_F52430, sub_F51980 and sub_F68290, each aligned instruction by
instruction with its kr-1256 counterpart. The UI manager slots match kr-1256
(cn-774 sub_F5C2B0, sub_F50B00, sub_F5C330, sub_F5C560). The edit setter
cn-774 sub_F68290 differs: it takes the text alone and returns with RET 4, where
kr-1256 sub_FC91C0 also takes a notification byte. `edit_text_abi = 2` states
this; 1 (the default) is the kr-1256 form, and the key is only accepted with
`control_abi = 2`. cn-774 DlgCreateRoom (cn-774 sub_6B5370) keeps kr-1256's
56-byte mode records at +1584 and stores the selected mode at +1664
(cn-774 sub_6B9D00).

## Research sequence

### Future form visibility subscriptions

The current adapter observes resource loading (kr-812 sub_A88160), initialization
(kr-812 sub_A851A0), destruction (kr-812 sub_A84C10), and owned button clicks
(kr-812 sub_A91840). Resource loading does not establish that a form is open.
No show/hide hook has yet been verified. The visible flag at offset 356
(kr-812 sub_A80850) must be checked together with visible ancestors and membership
in the current UI roots; the form owner field is not a parent pointer.

A reusable visibility API should combine the existing resource index with a
shared, bounded structural check for subscribed forms, accepting an exact `.ui`
or automatic discovery. It can omit text getters, names and geometry, and stop
descending after finding the target form. It should expose an initial snapshot
and cursor atomically, stable form generations, owner-scoped subscriptions and
explicit event-loss reporting, and stop sampling without subscribers. This is
an investigated design, not an implemented operation or a measured CPU claim.

`control.events` currently refreshes the visible control tree even when it omits
body text. It is not a constant-cost event-queue read. Consumers must not assume
that replacing inspection with this operation removes structural traversal.

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

Live-control observers are prepared on the host worker after all descriptor
signatures become readable and match the unpacked client. `signature_timeout_ms`
(default 15000, maximum 60000) bounds this activation wait; `signature_poll_ms`
(default 10, maximum 1000) controls its polling interval. Neither UI pointers nor
service requests are awaited during activation. Timeout fails before staging any
observer, and the runtime still verifies every staged write before committing it.
Activation exceptions are logged with a bounded reason.

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
without provider replay. Neither port has retail-game acceptance yet. cn-324's
and cn-279's native controls use the pointer-string adapter `control_text_abi = 2`.

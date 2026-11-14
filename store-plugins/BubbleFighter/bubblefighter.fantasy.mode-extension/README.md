# Mode Extension

[简体中文](README.zh.md)

The managed game-module controller adds a native **Mode Extension** button to the
configured room-creation form through the native UI bridge. Its overlay dialog uses the current client's v1, v2 or
v3 window, a three-column scrolling list, modern mode artwork, and FreeType text.
Titles and introductions occupy separate native label regions. An unavailable
localized introduction uses the localized **Hidden mode extension** label.

Opening the dialog reads the current native selection. A recognized extension tag
in the room title takes priority. Clicking a card changes the draft; the bottom
Confirm button applies it. Cancel or closing the native form closes the dialog.
The controller activates an available native mode button when it can; another
allowed mode is represented by `#LocalizedNameWithoutWhitespace#` at the front of
the room title. The room-creation caption receives the localized selected-mode
suffix after a successful application.

The controller owns native form discovery, entry creation, mode availability,
room-title tags, mutations and recovery for the game session. These operations
continue while the overlay browser is hidden or suspended. The frontend only
displays backend state and collects draft selections and user actions; it does
not read native controls or poll the game. `controller.json` configures the bounded
observation/action timing, pending-action capacity and selected-mode captions.

Native observation starts only when the owner-scoped Nirinsou Link channel is
connected, its room-creation context is active, and its current scene is `plaza`.
`main_menu` remains dormant even if the server accepts room creation there.
Leaving the plaza, losing the connection, or replacing the game-login context
immediately revokes observation and invalidates the current native operation
generation. A missing channel, binding, lifecycle API or negotiated grant also
keeps the controller dormant.

The package requires `bubble.fighter.fantasy.nirinsoulink` and declares v1 grants
for `game.room.create.context`, `game.room.create.context.get`,
`game.session.changed` and `game.session.state`. The controller subscribes to
both feeds before requesting the two snapshots with empty `{}` payloads. It
requests them once per connected transport epoch and once after a context
replacement; there is no periodic network request. Scene evidence from the old
login is discarded, and a replacement requires a fresh scene synchronization.
If that synchronization fails, a later valid plaza event can trigger one new
coalesced synchronization; the event alone cannot restore authorization.
Incoming events and replies carry a transport epoch and receive sequence, so an
older event dispatched late cannot overwrite a newer snapshot. Lower context
revisions, duplicate states and replies from retired connections cannot restore
observation.

Queued native reads retain their original operation ID and purpose without the
mutation deadline canceling their shared pipe. Confirmation always takes a fresh
native snapshot after the user action. Accepted writes have a soft result budget
checked between complete transport exchanges; an uncertain write is never replayed.
Entry cleanup retains ownership through transient contention before a new entry
can be created.

Native form bindings cover `kr-812`, `kr-1256`, `cn-279`, `cn-324`, `cn-404`, `cn-406`, `cn-408`, `cn-706`, `cn-717`, `cn-735`, `cn-774`, `id-400`, `id-1809` and `tw-454`. Other builds require their own
native bridge adaptation and `room-bindings.json` entry. Support verification is
separate from the 47-entry labels catalogue. The catalogue includes modes from the
original room dialog, and the default availability policy displays every entry.

## Build-specific availability

`mode-catalog.json` retains all identities, translated names and server tag tokens.
`mode-availability.json` controls the displayed choices. The native controller also
receives the complete catalogue so that an existing hidden mode tag can still be
recognized and replaced correctly.

The shipped policy is:

```json
{"format":1,"rules":[]}
```

The following is a configuration example, not a supplied support matrix:

```json
{
  "format": 1,
  "rules": [
    {"builds": ["kr-*"], "hidden": [8]},
    {"builds": ["kr-812"], "allow": [1, 3, 12]}
  ]
}
```

| Field | Meaning |
| --- | --- |
| `format` | Required format version, currently `1`. |
| `rules` | Ordered array of explicit restrictions. An empty array retains the complete catalogue. |
| `builds` | Nonempty list of build IDs or `*` wildcard patterns. Matching is case-insensitive and covers the entire ID. |
| `allow` | Optional list of mode IDs to retain. Matching allow lists intersect. An empty allow list retains no modes. |
| `hidden` | Optional list of mode IDs to remove. A matching hidden list takes precedence over an allow list. |

Each rule requires `allow`, `hidden`, or both. IDs must be distinct integers present
in the complete catalogue. Invalid policy data fails before opening a partial
list. Unmatched builds retain the catalogue unless another matching rule restricts
them. The schema is `mode-availability.schema.json`; the runtime additionally
checks IDs against the actual catalogue. Populate rules from verified client and
server behavior for the deployment.

The separate `room-bindings.json` identifies the exact `.ui` form, room-name input,
native mode button group, and entry-button placement. The UI bridge reports each
native button's enabled state and selection value at runtime. A build whose dialog
may be loaded from more than one resource lists one binding per resource; the
entry follows whichever of them is open. Changes to form bindings and native
adapters require their own client evidence.

## Display state protocol

The module installs its `mode-extension.state` listener before emitting
`mode-extension.ready`. The backend responds with the current state and publishes
changes through the same event:

```json
{
  "revision": 1,
  "sessionId": "ed550ef0b0f2449489ea4b5b3e574522",
  "open": true,
  "busy": false,
  "selectedId": 1,
  "modes": [{"id": 1, "key": "Pvp", "names": {"en": "No Item"}}],
  "error": null
}
```

Revisions increase for the panel's lifetime. An open dialog requires a nonempty
session ID; closed or error-only state can use `null`. `selectedId` can also be
`null`. Errors carry `{status, reason, recovery?}`, with `recovery: "restart"`
requiring a game restart; absent recovery may be omitted or `null`.

`modes` is the complete display list selected by the backend. The frontend adds
localized introductions only, and fetches artwork, window resources and fonts.
It does not load the policy, native bindings or complete tag catalogue. The draft
starts from `selectedId` when a dialog opens or its session changes. Busy updates
preserve that draft and do not redraw an unchanged mode list.

| User action | Event | Payload |
| --- | --- | --- |
| Confirm the draft | `mode-extension.confirm` | `{sessionId, modeId}` |
| Cancel the dialog | `mode-extension.cancel` | `{sessionId}` |
| Dismiss an error | `mode-extension.dismiss` | `{sessionId}` |

Each action waits for the next backend state to close the dialog or clear an
error. Stale revisions and callbacks from a replaced session are ignored.
Unmounting unsubscribes the state listener and hides the panel.

## Private artwork and staging

Original client artwork remains a private distribution input. Keep generated
images, fonts and resource manifests outside source control. The source package
contains code, policy and localized data; private staging adds `mode-skins.json`
and the referenced `modes/` images. The package requires the separately staged
`bubblefighter.fantasy.game-ui-resources` and the native UI bridge.

Prepare native mode art with `tools/game-ui/mode-extension-demo/prepare.py`, using
the maintained ItemViewer source and already prepared v1/v2/v3 resources. To stage
an overlay package from the launcher root:

```powershell
python tools/game-ui/mode-extension-demo/stage.py --package store-plugins/BubbleFighter/bubblefighter.fantasy.mode-extension --resources artifacts/mode-extension-resources --output artifacts/mode-extension-package
```

The output must be new or empty. Staging validates all referenced images before
creating output, rejects linked or escaping paths, strips private archive
provenance, and copies only the mode button art. Shared windows and fonts remain
in their existing resources package. Staging does not publish or enable a package.

## Preview and focused validation

The Vite preview is configured in `tools/game-ui/mode-extension-demo/vite.config.mjs`.
Set `BF_UI_RESOURCE_ROOT` to the private resource folder. `LAUNCHER_WEB_ROOT` can
point at an existing compatible frontend dependency folder. The preview listens
on `http://127.0.0.1:4191/`; query parameters select `theme=v1|v2|v3`, `locale`, and
an optional `build` for the availability policy. Changing the theme changes the
window shell. Zombie mode uses the ghost artwork in v1/v2 and its later artwork in
v3 while retaining its own name, introduction and numeric identity.

Run `node --test tools/game-ui/mode-extension.test.mjs` for draft/confirmation,
scroll capture, theme artwork and policy regressions. Run
`node --test web/tests/overlay-panels.test.mjs web/tests/mode-extension-panel.test.mjs`
for the production display protocol and subscription lifecycle. Managed controller
regressions live in `tests/FantasyTale.Launcher.Core.Tests/NativeRoomModeControllerTests.cs`.
`ModeExtensionDataTests.cs` in the same directory covers package validation,
availability and multilingual title tags. `RoomCreateObservationGateTests.cs`
covers plaza authorization, context replacement, reconnects and stale replies.
Real-client acceptance remains part of
validating the native bridge and each configured build.

# Optional game overlay

The launcher can run an optional x64 overlay process beside a 32-bit game. The
overlay follows the game client area and provides isolated HTML panels. Its
window follows the game z-order, remaining above it when another application gains
focus while allowing unrelated foreground windows to cover both. Minimizing
the game hides the overlay. It is enabled only when the selected client
instance has an installed and enabled package with `kind = "overlay"`, or the
local developer startup test is enabled.

Window tracking selects the largest visible game render window with `CS_OWNDC`,
excluding the separate splash class. The launch-bound process id survives the
initial window handle becoming invalid. Physical client-area pixels are converted
to WPF coordinates using the overlay window DPI; a 100 ms visible / 1000 ms idle observer supplements window events and
stops when the overlay closes. The visual root stays hidden before a render
window is available. The overlay becomes topmost while the game is foreground.
After focus changes, it returns above the game and below unrelated foreground
windows without repeatedly raising a normal window.
Minimizing hides both the WPF surface and browser host while retaining panel state.
Initial click-through setup does not steal focus.

Every game-attached Overlay retains `WS_EX_NOACTIVATE`, answers
`WM_MOUSEACTIVATE` with `MA_NOACTIVATE`, and never explicitly activates itself.
Mouse controls retain game foreground ownership in windowed, borderless and
fullscreen modes, avoiding display resets caused by deactivation. Standalone
previews remain independently interactive. Keyboard entry in an Overlay text
field without deactivating the game requires a separate input path.

Message boxes and dialogs hide automatically when a button action or close event
arrives, including components mounted directly through `game-ui.mount`. The owner
still receives the action, and hiding releases the panel's mouse input region.

WebView2 controller creation is deferred until a render window is visible and WPF
has arranged its client bounds. The pipe may connect and queue panel commands
during the splash, but it does not initialize a browser in a hidden splash-bound
window. An explicit page-ready message releases queued commands only after the
JavaScript listener is installed.

Component mount readiness also waits for the asynchronous native surface to
attach after its theme sprites and fonts load. An empty Vue mount point is not
a successful `game-ui.mounted` event; resource failures or the readiness deadline
produce `game-ui.error` instead.

## Local developer test

Enable **UI Overlay test** under **Developer > Developer options** to display
`Overlay Test` after the next game launch. The test is off by default and is
independent of installed Overlay packages. **Pass clicks outside the dialog**
defaults to on for this test. Both preferences are saved only in the local data
directory's `overlay-test.toml` and apply to future launches. Closing the developer
workspace does not change a saved test preference.

Native game-style components still require an approved resource provider. A local
developer may set `resource_root` to an existing absolute directory containing
`fonts/`, `v1/` and `v2/`; the preferences UI preserves this optional setting.
Game assets are not included in the source repository or supplied by this switch.

The class distinction is verified by cn-324 sub_87D8B0 (render style 40) and
sub_795920 (splash style 8). This detects the graphical window transition, not a
particular in-game scene or the completion of every resource load.

An overlay package is a service layer. It does not enter the x86 hook runtime,
does not change game files, and is never a requirement for starting a normal
client. A package may provide a bounded inline document or one package-relative
HTML file. Network URLs, absolute paths, and parent-directory traversal are not
accepted.

The launcher and overlay use a per-launch named pipe with protocol version 1.
The launcher creates the pipe, starts the overlay, checks the connecting process
identity, and validates a launch-bound handshake. Messages use a four-byte
little-endian length followed by UTF-8 JSON. Frames are capped at 256 KiB.
Commands are `register`, `unregister`, `show`, `hide`, and `send`; page events
are returned as `panel-event`. Unknown commands, malformed frames, timeouts, and
disconnects fail the optional overlay session only.

The current release exposes the contract in `FantasyTale.Launcher.Sdk`. Native
hook plugins can also use the optional versioned x86-to-launcher bridge. The
runtime supplies a plugin-owned panel namespace and forwards inline HTML,
visibility changes, and outbound events through the launch-bound broker pipe.
An absent or failed overlay returns `not-found`, so a native plugin must still
function without an overlay. The optional ABI tail `poll_event` consumes owner-scoped
page events on a worker. Check `struct_size` before using it. Each panel retains
at most 32 events of 4096 UTF-8 bytes; overflow and oversized events produce explicit
`overlay.events-overflow` / `overlay.event-too-large` events. Supply a 4097-byte
output buffer. File-backed native panels still need an approved package resource broker.

Native plugins keep independent connections to the same launch-bound pipe name.
The broker accepts at most 64 connections, with a separate framed request/reply
loop for each; one stalled or malformed connection cannot occupy another plugin's
stream. Disconnect returns its slot for a later plugin or reconnect. Requests wait
for the launched process id to be bound before authentication, closing the early
startup race. Shutdown cancels listeners and closes all connected streams. This
keeps protocol 1 unchanged and does not create additional WebView pages.

The renderer starts when the first game window appears and prepares its browser
when the render window becomes visible; there is no fixed scene settle delay.
The external renderer does not require native hook readiness;
file-only clients may not send a native readiness report. The
game module resolves the native UI theme before renderer startup. Bubble Fighter
dates on or after 2017-02-07 use `v3`, including that date; earlier builds retain
their declared `v1`/`v2` theme. If no profile exists, the earlier fallback is `v1`
through 2012 and `v2` afterward. The resolved theme reaches both the renderer and
`OverlayBuildContext`, and the page exposes `window.fantasyTaleOverlayTheme`.

The optional foundation ships the restored game UI component library under
`/ui-library/`. A panel can await `overlayBus.loadGameUi()` to receive Vue and
the exported components, then call `overlayBus.mountGameUi(panelId, component,
props, target)`. The library remains dormant until a panel explicitly mounts a
component and requires that panel to supply its approved resource root.

## Native UI components and local preview

A panel can register `<div data-game-ui-root></div>` and send `game-ui.mount` with `{ "component": "BfMessageBox", "props": { "text": "Example", "buttons": [{ "name": "Ok", "label": "OK" }] } }`. The host supplies the build-selected theme and resource provider and reports `game-ui.mounted` or `game-ui.error`. Styles remain inside the panel shadow root, and unregister unmounts the Vue application. No script execution through HTML insertion is assumed.

For a local integration test, run the overlay executable with `--game-hwnd <handle> --theme v3 --resource-root <external-directory> --preview game-ui`. All three themes are supported. The external directory contains `fonts/`, the requested theme and `sounds/`; game assets are not part of the repository. Omit the preview option for a visually empty foundation. The local preview is click-through and does not start or log in to a game. Geometry stabilization is not an actual 3D-ready signal.

The component source and rebuild command are documented in
[the library guide](../../tools/game-ui/README.md). `BfExtensionSettings` shares
one state/control tree across all themes; its `.change`, `.save` and `.cancel`
events return to the owning panel. Save, cancel and the close button hide that
dialog; Escape emits `.cancel` as well. The component does not implement gameplay
changes or persistent storage. The Launcher retains its approved v1/v2 body-font
selection, normal weight and Canvas sampling. V3 supports a subset of native
controls; browser font rasterization remains approximate.

## Automatic panel entries

Enabled overlay packages may declare a package-relative HTML `overlay.entry`. After the launch-bound handshake, the launcher registers each entry **hidden**. Only an explicit event-driven `Show` or dialog request displays content. A root with `data-game-ui-root`, `data-game-ui-component` and optional JSON `data-game-ui-props` mounts a library component without executing inline scripts. Local tests provide resources under the overlay page directory at `resources/`; resource distribution remains a separate packaging requirement.

## Event-driven UI and idle state

`IOverlayHost.IsReady` / `WaitUntilReadyAsync` report the installed page listener.
The renderer sends `renderer-ready` separately from pipe readiness. There is one
WebView page for the whole session: panels never create browser windows or navigate.
When all panels are hidden, the native surface and input timer are hidden/stopped.
After a short idle debounce the browser attempts `TrySuspendAsync`; any command
resumes it before delivery. The page and panel state remain allocated for quick
wakeup. Suspend completion checks activity again to avoid racing a new command.

Game-style UI is the default convenience path; a plugin supplies content and
handles, not CSS or asset names:

```csharp
await overlay.WaitUntilReadyAsync(cancellationToken);
using var confirmation = overlay.ShowConfirmation(
    "my-plugin.confirm", "提示", "是否继续？",
    onAction: args => HandleAction(args.Data.GetProperty("name").GetString()));
confirmation.SetBackdrop(true); // Optional; no final scrim color is assumed.
```

`ShowMessageBox`, `ShowConfirmation`, `ShowDialog`, `ShowGameUi` and
`CreateGameUiPanel` share the same page. Message boxes are centered, modal,
draggable by their title bar, and hide on action, close or Escape. Dispose the
handle when its plugin or owning feature ends. `game-ui.action`, `.change`,
`.close`, `.mounted` and `.error` return to the owning plugin. Managed callbacks
run on a separate bounded dispatch queue so they can send new pipe commands.
Default labels are currently Simplified Chinese. `locales.json` reserves zh-TW,
ko, id, vi and en as explicit TODOs, falling back to zh-CN until translated.

Native plugins include `fthook/overlay_ui.h`, call `fthook_create_game_ui` once,
then `fthook_show_message_box` with JSON `{ "title":"提示", "text":"内容",
"buttons":[{"name":"confirm","label":"确定"},{"name":"cancel","label":"取消"}] }`.
Use `fthook_poll_ui_event` on a worker for `{ "name":"game-ui.action",
"data":{"name":"confirm"} }`. This ABI tail ships in runtime 3.3.2.

Custom HTML is still supported by `RegisterPanel`. A button with
`data-overlay-action="retry"` emits `html.action`; `Send("html.update", new
{ status = "..." })` updates elements with `data-overlay-text="status"` safely.
Inline script tags are not evaluated. Rich controls may use the existing page
bus and exported Vue component contract. Trusted package HTML remains CSS-isolated
in a ShadowRoot, with a local box-sizing reset; text canvases use explicit logical
sizes so container padding cannot stretch glyphs.

## Layers, input, geometry and optional effects

Registration accepts `Layer` (0..10000), `Interactive`, `Modal`, `PassThroughOutside`, `Backdrop` and
`BackdropColor`. Hidden panels receive no input. Ordinary panels share input by
bounds; only the top modal receives input while present, and its close restores
lower receivers. `BringToFront` raises an equal-layer panel; modal panels always
precede ordinary panels. Tab stays inside the top modal. A single full-client
scrim sits beneath the highest visible requesting panel, avoiding compounded
opacity. Final scrim colors remain TODO; the neutral default is configurable.

Modal priority and outside game input are separate policies. Set
`PassThroughOutside = true`, pass `passThroughOutside: true` to a game-UI helper,
or call `panel.SetPassThroughOutside(true)` to keep the dialog interactive while
clicks beyond its bounds reach the game. The top modal's policy wins; lower
Overlay panels remain blocked. Other modal panels block outside input by default.
Disabling the option restores full-client input capture. Dragging retains pointer
capture until release even when outside clicks normally pass through.

Client resize events, render HWND replacement and WM_DPICHANGED update the
surface. The page re-centers untouched dialogs; dragged dialogs are clamped back
into the client viewport. Active pointer capture stays enabled during a drag.
No system DPI settings are changed. Cross-monitor retail acceptance remains
separate from deterministic coordinate tests.

`OverlaySoundCue` / `PlaySound` reserve dialog-open, button-press and notification
cues. The shared page adapter is called by default dialogs and buttons. **TODO:**
provide approved audio assets and configure their mapping; unassigned cues are
silent and create no audio objects or playback timers.

## Native UI anchors and future locators

`IOverlayAnchorProvider.Subscribe` abstracts hook and future image locators.
`panel.FollowAnchor(provider, targetId)` returns a disposable subscription;
`SetAnchor` and native `fthook_set_ui_anchor` update the same page contract.
Rectangles are game client pixels, never desktop coordinates; optional
ReferenceWidth/ReferenceHeight declare a fixed source viewport. The renderer
converts source pixels to overlay CSS pixels, independently of desktop DPI.
Placement supports cover, center, left, right, above and below. Source visibility
controls the attached panel. Source moves are pushed; anchored HTML does not
detach when dragged. Null removes the binding and restores its original layout.

**TODO:** verified per-build control locators, image capture/template tracking,
confidence/lost-target policy, and a rate/region budget for each image locator.
No image-scanning loop runs in the foundation. Pixel matching must avoid finding
the replacement overlay itself and must stop when the game is minimized.

`IOverlayNativeUiBridge.InvokeAsync` reserves semantic Press, Focus, SetValue and
Close operations on named targets. HTML events reach their owner first; the
owner explicitly forwards approved actions. **TODO:** verified per-build native
handlers and target allowlists. No simulated game click, arbitrary address call,
or game packet is sent by the foundation. Unsupported and NotReady are explicit
outcomes, distinct from an HTML button being clicked.

## Build profiles

Each build's `[overlay]` table selects `theme`, optional capability names and named
targets. Builds through 2012 currently declare `v1`; later builds declare `v2`.
`v3` is reserved and rejected as unavailable by the process manager until its
renderer/assets are supplied. Selecting a theme does not imply that a native
operation is implemented. `IOverlayHost.BuildContext` and `overlayBus.buildContext`
expose the selected build/profile; `overlayBus.hasCapability(name)` reads declared
capabilities. One plugin can keep shared behavior while choosing supported modes,
assets and locator identities from this data.

```toml
[overlay]
theme = "v2"
capabilities = ["room.mode.extra"]

[[overlay.targets]]
id = "room.shell"
locator = "hook"

[[overlay.targets]]
id = "room.create"
locator = "ui-resource"
resource = "Ui/CreateRoom.ui"
control = "CreateRoom"
parent_target = "room.shell"
```

Targets accept `hook`, `image` or `ui-resource`. Resource identities must remain
relative; parent targets must exist and cannot form cycles. These are locator
declarations, not automatically active scanners or native memory readers.

### Optional UI-resource locator

Build targets may declare `locator = "ui-resource"`, a relative `.ui` resource
identity, and a control name. `OverlayUiResourceParser` accepts the bounded XML/JSON
layout subset used by an adapter and converts nested design-time rectangles to the
shared `OverlayAnchor` contract through `OverlayUiResourceAnchors.Resolve`. The
resource design viewport is retained as `ReferenceWidth`/`ReferenceHeight`, so the
page scales it for the current game client size and DPI in the same path as hook
anchors.

This parser is a foundation contract rather than a claim that every game's private
binary `.ui` format is understood. A build adapter must decode a proprietary format
and publish the same rectangle through `IOverlayAnchorProvider`; unsupported,
missing or malformed resources fail explicitly. Static resource coordinates cannot
observe a player dragging a native control after loading, so a runtime hook or a
bounded image locator remains the source of truth when the game moves a control.

An adapter-normalized layout can be bound without a continuous scan:

```csharp
var layout = OverlayUiResourceParser.Parse(normalizedLayoutText);
using var provider = new OverlayUiResourceAnchorProvider(layout);
using var binding = panel.FollowAnchor(provider, "CreateRoom");
panel.Show();
// When the resource/layout changes, provider.Update(newLayout) publishes new bounds.
```

XML/JSON adapters normalize controls to `id`, `x`, `y`, `width`, `height`, optional
`visible`, and nested children (XML elements or JSON `children`). The root may
declare `referenceWidth`/`referenceHeight`. Bounds are relative to their parent;
the provider publishes accumulated rectangles. Hidden or removed controls hide
their attached panel on reload. Parser input is bounded to 1 MiB, 4096 named
controls and bounded nesting; a provider accepts 128 live subscriptions. DTDs,
duplicate control names, nonfinite/partial bounds and partial viewports are rejected.

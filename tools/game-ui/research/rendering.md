# Native rendering evidence

The screenshot is a visual reference, not the authority for logical dimensions or coordinates. Capture crop, Windows DPI, browser scale, and client rendering scale are separate transforms.

## Configuration verified in both clients

Both `Ui/UiScheme.ini` files set dialog title font `NexonBTBold`, size 14, weight 400. V1 uses decoration 1 and color `0xff888888`; v2 uses decoration 4 and color `0xff3a85cb`. Decoration 1 does not enter the outline pass in the verified cn-717 renderer. Do not interpret the word Bold in a registry name as a request for CSS weight 700.

The cn-717 `Config/Fonts_cn.csv` maps Default, DefaultBold and NexonBTBold to `GB13000.ttf` with OpenTrueType; Malgun maps to `DF_GB_Y9.ttf`. Titles now use the declared GB font. Several hand-composed body labels still use the Malgun font approximation and require per-control evidence.

## cn-717 call chain

- cn-717 sub_B71980 reads UiScheme.ini, including title color, decoration, font and margins. Title_Margin_Top=38 is the content margin, not the title text Y position.
- cn-717 sub_B44F90 reads control layout properties, including position, size, font size and weight.
- cn-717 sub_BAE810 clamps the font size to at least 11 and normalizes requested weight to 400 or 700. Its country-dependent forced-700 branch must not be generalized to Chinese rendering.
- cn-717 sub_C5ACD0 passes font dimensions in 26.6 units (`size << 6`) into cn-717 sub_C5AC40. The 4/3 expression in list row calculations is not sufficient evidence for scaling all font sizes by 4/3.
- cn-717 sub_B82770 accepts a rectangle; cn-717 sub_B828B0 accepts origin and dimensions and converts to a rectangle. Both reach cn-717 sub_BB0440.
- cn-717 sub_BB0440 caches a rendered text texture. Alignment flags 1/2 mean horizontal center/right; 4/8 mean vertical center/bottom; 0x10 enables wrapping. Centering uses integer halves of the rectangle and texture sizes. The resulting sprite is transformed by a separate rendering scale.
- cn-717 sub_BAEAC0 builds the text texture, measures lines, and invokes cn-717 sub_BAD380. Decorations 2, 3 and 4 use two passes; the other values use one pass.
- cn-717 sub_BAD380 obtains glyphs with load flags 40968, converts them to bitmaps, and composites grayscale coverage into BGRA texture pixels. It places glyph rows using font size minus one and glyph bearing. Weight 700 invokes a separate outline expansion, distinct from text decoration.
- cn-717 sub_BAE810 initializes decoration expansion values 32 and 128 through cn-717 sub_C61AE0. In the 26.6 representation these correspond to 0.5 and 2. The browser uses a 2-pixel stroke approximation for title decoration 4; browser stroke geometry is not identical to the native outline expansion.
- cn-717 sub_BAD8F0 includes decoration in text measurement: decoration 3 adds one to width and two to height; decorations 2/4 add two to width and four to height. Text layout must account for these dimensions before centering.

## Coordinates verified in cn-717

cn-717 sub_B46F20 draws the title at `(3 - marginLeft, 9 - marginTop)` relative to the dialog content origin, with outer width, top-margin height and alignment flag 1. In outer-window coordinates this is `(3, 9)`, horizontally centered, not vertically centered. The Web v2 title now follows this rule without an empirical baseline offset.

cn-717 sub_B6C9E0 draws group text at `(26, 4)` in group coordinates with alignment flag 0. The scheme default is size 12, but the option forms explicitly override it to 13. Option-style groups use this override and top alignment; centering a 22-unit label moves it below the original header baseline.

V1 has separately verified scheme data, but its title coordinate function has not yet been matched. Do not cite cn-717 function addresses as cn-279 evidence.

## Current limitations and further work

The Canvas renderer retains four-times supersampling for smooth browser display. This is a browser approximation, not an identified native supersampling factor. Browser font hinting, glyph advances, stroke shape and bitmap bounds can differ from the client. The native-height calculation uses browser glyph descent as a proxy for native bitmap metrics.

## Runtime message-box sizing

The `.ui` message-box dimensions are template minima rather than the final size for every message. cn-717 `sub_921AB0` measures the rendered message texture, keeps the text width at least 200 logical units, and uses at least the measured text height plus 16 units before laying out the frame, icon, and buttons. One-button mode centers the button; two-button mode places the buttons from the right edge using their measured widths and spacing. cn-279 `sub_8A27D0` contains the same sizing algorithm with build-specific constants, so the cn-717 addresses are not evidence for v1.

The Web announcement keeps each v1/v2 manifest size as its minimum and expands the content rectangle only when the measured message needs more space. FrameBox height, icon Y, message rectangle, and button Y follow the expanded geometry. Announcements use the native one-button mode and therefore render only a centered confirm button; the confirm dialog keeps each build's two-button layout. The implementation does not compress line spacing or insert extra line breaks to fit a fixed box.

Announcements, confirmations, warnings, and errors share `DlgMessageBox.ui`. The warning entry reserves a 100x58 layout slot, while the extracted `MsgBoxIconWarning.png` is 63x58; the renderer keeps that slot semantic but draws the bitmap at its natural size. The library uses one `renderMessageBox` implementation for all of these variants. The public `dialog` helper accepts the Boolean shortcut or an options object with `icon`, `title`, `text`, and a `buttons` array. The default confirmation is simply a two-button configuration and an announcement is a one-button configuration; additional buttons use the same measured group layout and sprite pipeline.

Close button coordinates, runtime option-tab positions, bottom-button anchoring, trackbar ticks, and several room-mode layouts remain manually composed or screenshot calibrated. They are not yet verified native constants. Raw .ui conversion preserves source properties but does not reproduce all widget types, runtime visibility or controller mutations.

## Asset provenance

`assets-v1.tsv` and `assets-v2.tsv` list original member path, archive, SMF SHA-256, frame index and inclusive source rectangle. Manifest provenance retains extraction logs. The source roots are the Chinese 2011 and 2015 client directories supplied by the user.

Close: Images/Ui/DialogButtonClose.smf. V1 confirmation/cancellation: Images/Ui/DialogButtonOk-{Up,Down,Hover,Disabled}.smf and DialogButtonCancel variants. V2: Images/Ui/DialogBtnOk.smf and DialogBtnCancel.smf. Tabs: Images/TaskBar/Option/OptionTab01.smf and OptionTab02.smf. Window shell: Images/Ui/DialogSkin-Basic.smf. Crop each SMF frame before stretching to exclude atlas gutters.


## Native shell coordinates and option bounds

cn-279 sub_4A7550 draws the title at outer (37,11), format 0. cn-279 sub_4A7170 positions the close child at (contentWidth-closeWidth-5,11-titleMarginTop); cn-717 sub_B469E0 uses (contentWidth-closeWidth+3,8-titleMarginTop). With the scheme margins, the outer right/top distances are (15,11) and (7,8), respectively. Inclusive SMF bounds supply natural sizes 17x17 and 21x21. The renderer retains natural dimensions and uses original Up/Down/Hover frames.

cn-279 sub_4A7550 adds the margins to the skin dimensions; cn-279 sub_4A0650 passes the parent content origin to children. Both schemes use left/right 10, top 38, bottom 12. Option layouts DlgOption.ui (294x270 content) and DlgOption2.ui (300x310 content) therefore produce 314x320 and 320x360 outer bounds. cn-717 sub_6EFA30 loads Ui/DlgOption2.ui at 0x6F00DF. That function has other layout branches; this demonstration implements this resource variant only. Tabs, inner panels and bottom buttons now read root layout coordinates and add the content origin instead of screenshot offsets. Previously calibrated option dimensions were 314x315 and 314x355.

This supersedes earlier statements that v1 title coordinates and option shell controls remained screenshot-calibrated. Track marks and portions of the room layout still require native runtime investigation.


## Track controls, room buttons, and scale composition

cn-717 `sub_B62940` computes the track step as `(width - 41) / (max - min)`, validates the requested interval using `(max - min + 1) % interval == 1`, and places the knob at `truncate(15 + step * (position - min))`. The executable double at `0xEBF7E0` is 15. `sub_B62D80` draws the rail inside x=15..width-15, ticks at `truncate(20 + interval * step * i)`, and the full padded sprites at y=height-35. cn-279 `sub_4B66B0` independently confirms the padded tick drawing. `Images/Ui/TrackBar.smf` frames 6/7 are disabled/enabled 1x31 ticks; frames 3/4/5 are normal/hover/disabled 12x31 knobs. The implemented interactive range currently renders the enabled normal knob; full disabled/hover state parity is pending. Min/max text uses the scheme font, format 37 centering, and original text rectangles. The 178x55 option control is retained without replacing it with a narrower rail-sized control.

Room mode assets live under `Images/DlgCreateRoom/BTN_*.smf`. Their normal frames are 190x51; the preview now preserves that size instead of stretching to 183x43. Version 1's mode list comes from its own form. Runtime mode ordering and conditional option visibility are still a reconstructed preview, not a full port of the room logic. Checkbox composite text starts at x=30, y=1, following the default sprite width convention in cn-717 `sub_B6AF90`; text uses a centered native-metric rectangle.

Nine-slice skins are now composed into one logical-size canvas with shared integer boundaries before the host applies scale. This removes independent DOM tile rounding as a source of fractional-scale gaps. It does not claim native GPU sampler parity. Each original cropped sprite and its SMF path remain in the manifest and provenance tables.

## Shared library and custom page

`createGameUi({theme:'v1'|'v2'|'v3',resourceRoot:'./local/'})` exposes `windowShell`, `panel`, `label`, `checkbox`, `groupBox`, `trackBar`, `button`, and `renderNode`, plus example pages. `dialog` is the shared dialog renderer: a message body is the default, while a `body` callback can populate the same shell with an arbitrary native-style control tree and use the same button array. The Vue library exports `BfDialog` for that API and keeps `BfMessageBox` as a thin compatibility wrapper. `groupBox` and `trackBar` accept the original XML-style attribute names. `customSettings()` now uses the shared dialog shell and its v2 button group is centered from the content width. It emits `bf-change` for values and `bf-action` for buttons. The overlay host owns persistence; the example does not save settings.

Run `node tools/game-ui-lab/native-layout.test.mjs` to check the original option slider coordinates, endpoint clamping, interval count, and invalid geometry rejection. Canvas text remains a browser font-raster approximation, even though family and weight 400 come from the original resources.

The custom settings page preserves `SpriteSetBackground` from the original option group instead of looking up `SkinSpriteSet`. The native option group width is 287. The extension page uses a wider shared custom body; it retains the option-page 13-unit main font and separate checkbox/label rectangles.

The shared dialog's v1 multi-button group ends at the content right edge, following cn-279 `sub_8A27D0`, with an 8-unit gap (`dword_AA55E8`). The scheme supplies the additional 10-unit outer right margin. Custom settings no longer supplies outer-window X coordinates as content-local button coordinates; for 300-unit content, two 68-unit buttons start at 156 and 232, leaving 10 units to the 320-unit outer edge. V2 keeps its centered group.


## Extension settings, footer bounds and font inheritance

`BfExtensionSettings` uses `extension-settings.js` for all themes: a 350-unit
content width, panel at (3,27) with width 344, groups inset by 5, and actions at
y=260. The shared host is 370 units wide. Panel outer margins are 13 units on
both sides; group outer margins are 18. These are deliberate custom page
choices based on the option-page nesting pattern, not native constants.

The shell includes max(body bottom, action bottom), then the title/content
origin and native bottom margin. V1/v2 use top 38 and bottom 12; v3 uses top 42
and bottom 12. Buttons retain native theme dimensions. V1 multi-action alignment
remains right with the 10-unit outer margin; v2/v3 center the group. An explicit
outer height is a minimum and cannot truncate the footer.

V1/v2 option group backgrounds come from `DialogSkin-Basic04.smf`, explicitly
set by `DlgOption-VideoSound.ui` / `DlgOption-VideoSound2.ui`. The generic
`GroupBox.smf` has a different header and is inappropriate for this option
style. V3 uses `dialogueNew/dialogue_basic03.smf` and its section-label pattern
at (9,5), size 14, vertically centered in a 20-unit rectangle. Text tab surfaces
are CSS approximations; their words are rendered with the original font.

Both inspected Chinese font tables map Default/DefaultBold/NexonBTBold to
GB13000.ttf. Its SHA-256 is
`2d372f0568676b0a45a939fc460dd6cf5d269599b6a348f6f429df729f25794f` in the
2011 client, 2015 client and prepared local pack. UiLabel's blank FontName now
inherits Default rather than the renderer's BFMalgun fallback. V1 rendered
confirm/cancel labels likewise use BF (Default), scheme size 12 and weight 400.
Explicit Malgun remains DF_GB_Y9.TTF. This corrects family selection without
changing the existing global Canvas sampling or adding synthetic bold. The
extension body already used BF before these family fixes. Browser rasterization
still differs from the prepared FreeType glyphs used by tutorial dialogue.

Browser verification covers both tab pages, all themes, footer containment,
symmetric panel margins at 1x/1.5x, actual Canvas font selection, mutually
exclusive values and draft retention through theme switches.

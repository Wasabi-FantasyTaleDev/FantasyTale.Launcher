# Overlay game UI source

This is the source snapshot used by the Launcher's optional game UI renderer.
Run `npm ci` in `web/`, then `npm run build:game-ui` there to type-check and
rebuild the two committed files under
`src/FantasyTale.Launcher.Overlay/overlay/ui-library/`. The same installed Vue and
Vite dependencies build both applications; the library keeps Vue external and
uses the Overlay's existing import map at runtime.

`node --test tools/game-ui/launcher-presentation.test.mjs` runs the independent
font, Canvas, revision-boundary and draft-state regressions from the repository
root. `npm test` in `web/` also runs the complete synthetic game UI suite. Three
private-resource integration groups are skipped when `FT_GAME_UI_RESOURCES` is
unset. Setting it opts into those checks: the prepared external directory must
exist and contain the required resources, or the tests fail.

`README.vue.md` describes the component API. The game module resolves the UI
revision before starting the renderer. Bubble Fighter dates from 2017-02-07
onward use v3, including the boundary date; earlier builds retain their declared
v1/v2 selection. The provider applies the same boundary to a supplied client date.
`BfExtensionSettings` shares its control tree and state across all themes. It
emits changes and save/cancel events; no gameplay setting is applied by the
component library.

The Launcher retains its previously approved v1/v2 text appearance. Blank
`UiLabel` font names, extension body labels and text tabs use BFMalgun
(`DF_GB_Y9.TTF`); explicitly named Default/DefaultBold/NexonBTBold labels and
section/window headings use BF (`GB13000.ttf`). V1 confirm/cancel text retains
BFMalgun at 13 logical pixels. V3 uses its own native font declarations.
All these fonts keep weight 400, the four-times Canvas buffer and explicit
logical output dimensions. No synthetic bold is applied. These presentation
choices intentionally differ from the source lab's default-font inheritance.

The library contains no game images, fonts or audio. A local resource root must
provide `fonts/`, the requested `v1/`, `v2/` or `v3/` manifest and sprites, plus
`sounds/` when sound is enabled. Prepared resources stay outside Git and are not
automatically added to release payloads. UI sound uses the original event gains,
with provider volume 0.75 by default; browser audio needs an initial user gesture.
The provider owns the player across theme changes and disposes it on unmount.

This library implements a subset of native controls. Browser glyph rasterization
remains approximate; the tutorial's optional prepared glyph atlas follows a
separate rendering path. See `TUTORIAL.md` for that optional resource contract.

The Ban Pick demo opts into weight 700 using its own W9 font face and
visible-ink item-name centering; these host options do not change the settings
renderer defaults above. Panel skins are composed at integer dimensions and
encoded as one PNG image before mounting.

The Ban Pick host supplies `textScale` so captions choose an integer final
pixel size and use browser SVG text instead of a resized glyph bitmap. `BfWindow`
accepts optional `fontFamily`, `fontWeight`, `textRenderer`, `textScale` and
`titleBounds`; callers that omit them retain the existing renderer defaults.
The Ban Pick appearance carries `nameBounds`, `statusBounds` and `titleBounds`
from its own theme assets. Native drawing errors are forwarded to the host.

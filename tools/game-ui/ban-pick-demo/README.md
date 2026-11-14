# Ban Pick editor demo

[Chinese](README.zh.md)

This Vue 3 preview reproduces the inventory half of the reference equipment UI.
It has four categories (weapons, characters, clothing and accessories), the original three-column,
three-row inventory grid, localized search, native subtype tabs, pagination and per-category
restriction counts. The native checkbox sits left of the rightmost search field on
the main-category row and filters to restricted items. Category and action captions
use a thin outline without an offset shadow. The footer buttons form one group,
aligned right in v1 and centered in v2.
Clicking an item edits a draft; Apply retains it in the current page's memory,
and Cancel or Close restores the last applied selection. Reloading resets that draft.
A Presets button opens an independent native-theme modal with saving, same-name replacement, loading and deletion. Presets
persist in browser localStorage across reloads, including an empty allow-all selection;
storage errors are displayed and cannot silently replace the current preset list.
The demo does not send room rules or enforce them in a game session.

`BfBanPick` is exported by the shared game UI library. Its host supplies
localized labels, item data, transparent preview URLs and a `modelValue` array of
item IDs, original category skins and saved presets. The component emits
`update:modelValue`, `apply`, `cancel`, `save-preset`, `delete-preset` and `error`; its host owns
storage and network transport.

## Prepare private resources

Use the maintained `tools/item-viewer` source with the `kr-812` profile. The retired
standalone checkout has older camera, pose and weapon drawing rules and is rejected
by the preparation script. Python needs the viewer's numpy and Pillow dependencies,
plus fontTools for the browser font export.
The renderer's displayed version remains `0.1.0`, so `preparation.json` records hashes
of its Python sources and profiles rather than relying on that version alone.

Run from the launcher repository, replacing the five input paths with local ones:

```powershell
$ViewerRoot = 'D:/path/to/maintained/tools/item-viewer'
$ClientRoot = 'D:/path/to/kr-812/client'
$TextClient = 'D:/path/to/Chinese/2015/client'
$V1Client = 'D:/path/to/Chinese/2011/client'
$FontData = 'D:/path/to/Chinese/client/Data'
python tools/game-ui/ban-pick-demo/prepare.py `
  --viewer-root $ViewerRoot --client $ClientRoot --text-client $TextClient `
  --v1-client $V1Client --font-data $FontData `
  --output artifacts/ban-pick-demo/resources
```

The v1 window, checkbox, cards, tabs, buttons and item-info form come exclusively
from the Chinese 2011 client. V2 uses the Chinese 2015 client for the same set of
resources. Each manifest records its own archive provenance; UI themes are not
assembled from different clients. Add `--assets-only` to refresh both themes while
retaining the prepared catalog, fonts and item previews; an incomplete output is rejected.

The Chinese font directory must contain `GB13000.ttf` and `DF_GB_Y9.TTF`.
The KR client supplies `Data/NexonBTBold.otf`, models and camera rules. Its CFF font
declares a TrueType maxp layout and contains malformed legacy kerning record lengths.
`NexonBTBold-web.otf` rebuilds those tables while retaining all glyphs, outlines, hints
and advances; the original file is retained. `preparation.json` records both hashes.
The Chinese
client supplies `Config/ShopItem2.xml` and the UTF-16, tab-delimited
`StringTable/TranslatedXmlTable.csv`; official Chinese translations take precedence
over the workbook's untranslated cells. No sample names are hand-translated. Samples
without an original Chinese name are rejected. Traditional Chinese currently shares
the original Simplified Chinese item text; locales without an official English name
keep the original Korean name. Missing descriptions are identified in the UI.
The script reads archives without editing the client, exports the 47 samples declared
in `items.json`, and refreshes
existing PNGs. Its output, fonts, client sprites and screenshots belong under ignored
`artifacts/` and must never be committed.

The reviewed renderer snapshot uses Git tree
`b2cc8e9956d57a1c652588567def797deaee8ca6` for `tools/item-viewer`, including the
inventory fidelity fix `276b015c` and source evidence correction `6532d1b3`.
Its outline remains the renderer's native inverted-hull pass; the demo does not
add a CSS stroke or change model geometry.

Samples include two hammers, two grenades and three crystals under the original
Head Accessories category. Their IDs are 100412, 100431 and 100432; their names come from the native Chinese
translation table. Their `standalone_slot` declarations use only their own meshes and motion,
with a declared rotation and proportional fit of projected geometry including the
outline margin. This adapter leaves the maintained renderer unchanged. Its manifest
labels the camera `demo-standalone-fit`; the native standalone camera path has not
been verified. The preparation record hashes the adapter and sample definitions.

## Run and build

```powershell
cd web
npm ci
npm run dev:ban-pick
npm run build:ban-pick
```

The preview is served at `http://127.0.0.1:4187/`. Production output is written to
`artifacts/ban-pick-demo/dist/`, including the prepared private resources.
Seven locales are available: Simplified Chinese, Traditional Chinese, Korean,
English, Indonesian, Vietnamese and Thai. Narrow viewports scale the original
584-by-949 v1 or 584-by-944 v2 logical window instead of rearranging its inventory grid.
The preview fits both available width and height. At 1024 by 768 the toolbar moves
to the side and the complete window fits without scrolling. Captions are browser
SVG text with integer final pixel sizes, not resized text bitmaps; the models and
full description layout retain their proportions. The original character preview is omitted. The native item description panel fills
the width below the inventory; its horizontal label/panel regions expand together.
Preset controls live in a separate 320-by-205 modal. Use `?theme=v1` or `?theme=v2`,
or switch the theme in the toolbar; the current draft survives theme changes.

## Original UI evidence and validation

- `Ui/DlgInventory6.ui` describes nine 172-by-172 markers at X 449/629/809 and
  Y 140/318/496. The inventory atlas supplies the actual 182-by-183 normal and
  selected card frames. A centered 172-by-172 crop removes their outer glow padding
  without rescaling their interior geometry.
  Horizontal and vertical pitch remain 180 and 178 pixels; the transparent preview
  occupies 164 by 120 pixels. The demo displays nine items per page in the original three columns and three rows.
- `Ui/UiScheme.ini` and `Images/Ui/` supply the existing `BfWindow`, checkbox,
  confirmation/cancel button state surfaces, panel skins and horizontal pager assets. Main tabs
  retain their own 67-by-32 v1 and 67-by-27 v2 sizes. Category identities and order come from the client table.
- Hover updates the description without an outer yellow card frame. The native
  `inventory_use.png` border indicates a restriction; its baked-in status text is
  removed from the private border export and replaced with the localized status.
  Card footers contain no checkbox. Names are centered using measured visible glyph
  bounds inside the white header; status text aligns with the gray footer in the atlas.
  The localized yellow badge sits inset at the upper left of the model region;
  Allowed and Restricted footer captions both use the same gray text color.
  Both themes use the complete canonical subtype order: rifles, shotguns, snipers,
  pistols, melee weapons, special weapons; accessories use head, beauty, ornament
  and back subtypes, with crystals included under head accessories.
- `Ui/DlgItemInfo.ui` supplies each theme's description layout. V1 retains the blue
  slogan bar with the item name below it; v2 retains the green item-name bar.
  `kr-812 sub_95BB60` fills item name, slogan, description and additional detail labels
  from the selected item. The form's vertical regions and alignment are preserved;
  horizontal coordinates expand from the original 420/417 pixels to 554 pixels.
- `kr-812 sub_9B23D0` adds `sin(time * 2*pi / 2000) * 2.5` to the item view.
  The component uses that period and amplitude, stops while the page is hidden,
  and honors reduced-motion preferences.
- The original font table maps Default/DefaultBold/NexonBTBold to `GB13000.ttf`
  (the W7 face), and Malgun to `DF_GB_Y9.TTF` (W9). At the owner's request, this demo
  maps weight 700 to the existing heavier W9 face, rather than synthesizing bold.
  This override is separate from the original table's weight 400. Korean labels
  retain NexonBTBold. Captioned buttons keep stable DOM nodes across pointer states
  and size their text area from actual glyph advances, preventing excessive padding
  and lost clicks. Window/panel skins remain one integer-composed raster surface.
  Title, name and status rectangles are extracted from their native atlas bands.
  Their text centers measured glyph ink without an offset shadow.
- Resource refreshes reload the appearance and manifest after Vite updates or a
  completed `ui.json` export. The exporter replaces that JSON atomically; rendering
  errors reach the host instead of silently leaving blank names. Font failures
  identify the requested face and weight.
- Strict Vue TypeScript and the Vite production build pass. Browser checks cover
  combined search/restriction filters, removal from filtered results, native paging by dragging, track clicks and keyboard navigation,
  apply/cancel restoration, persistent presets and deletion, all four categories,
  Chinese, Korean and English presentation, image loading and a 400-pixel viewport. Shared renderer regression
  tests verify that controls adopted by a Vue host still deliver their events; events
  originate on the mounted control instead of its detached construction parent.
- A real-client Dao export with outline enabled differs from the same render with
  only its outline disabled at 1,740 pixels in a 328-by-240 image. The gray-background
  comparison confirms that the black contour comes from the renderer.

The web font audit compares all 18,280 glyph outlines, 17,258 Unicode mappings,
advances and kerning with the original, plus 16 equal FreeType text rasters.

Some accessory socket placements remain inferred in ItemViewer; the export manifest
retains those warnings. Their sample previews need further attachment calibration
before being treated as exact native reproductions. This does not affect the demo's
selection state or category controls.

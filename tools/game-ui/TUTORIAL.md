# Tutorial dialogue: evidence, integration and copy generation

This guide covers `BfTutorialDialogue` in `tools/game-ui`. Native evidence comes from cn-279, the supplied Chinese 2011 client. Runtime dependencies are Vue, Canvas, Web Audio and static assets. Python prepares assets offline.

## Preview

Run `npm run dev` in this directory and open `http://127.0.0.1:8766/`. Select the tutorial view. Its copy selector offers the five-step Fantasy setup guide and the original cn2011 tutorial.

The setup guide now uses one native-size balloon per step. The demo enables automatic advancement and looping. A click reveals the current sentence; another requests the next page. If browser autoplay policy blocks audio, the first sentence waits behind a start button until the audio context is running and both samples are decoded. The hold timer starts after typing finishes.

Original tutorial progress can lead into movement or shooting tasks rather than another dialogue. Looping presentation mode skips those gameplay-only nodes and continues through the extracted dialogue. The preview background is a neutral backdrop, not a reconstructed 3D tutorial level.

## Verified cn-279 behavior

Coordinates below belong to the native 1024x768 logical canvas, not the supplied video capture's pixel dimensions.

| Item | Evidence / value |
| --- | --- |
| Construction | `sub_857740` creates TutorialMgr; `sub_8AAE10` creates NarratorObject |
| Page configuration | `sub_784BC0`, fed by the manager's current progress |
| Drawing and typing | `sub_785010` |
| Font | `DefaultBold`, mapped by `Config/Fonts_cn.csv` to `Data/GB13000.ttf` |
| Size and weight | 15 and 400; the alias does not request synthetic bold |
| Glyph load flags | `sub_53F870`: 40968 (`0xA008`) |
| Fill | `0xFF222222` |
| Decoration | Mode 1, recorded decoration color `0xFF666666`; mode 1 does not enter the two-pass outline-expansion branch |
| Text rectangle | `(727,400,250,100)` |
| Alignment | Format 4: left aligned, complete text texture vertically centered |
| Wrapping | No `0x10` flag; original text uses explicit translated line breaks |
| Balloon | Center `(847,474)`, image 328x192, top-left `(683,378)` |
| Portrait | Center `(884,646)`, image 272x243, top-left `(748,524.5)` |
| Character interval | `Config/InGameUI.xml`, `textPrintSpeed=70` ms; overrides the code's 50 ms fallback |
| Click behavior | `sub_7849D0`, `sub_784950`, `sub_785010`: reveal first, advance on the next click |

`sub_53FDF0` and `sub_540180` measure and compose explicit lines. `sub_540FF0` centers the resulting whole texture. The Web component remeasures the visible prefix as it types, so a newly revealed newline can recenter the text block.

Official Chinese text comes from `StringTable/TranslatedXmlTable.csv`, for example `TutorialStage-Stage1-[progress]-dialogueString / 21`. Its line break is retained:

```text
在正式进入游戏之前，先要
接受训练。
```

## Original images and audio

| Content | Original source | Pack-relative runtime file |
| --- | --- | --- |
| Dizni | `Pack/Images0007.pkn` / `Images/TutorialStage/Dizni.png` | `Images/TutorialStage/Dizni.png` |
| Dao | Same archive / `Images/TutorialStage/Dao.png` | `Images/TutorialStage/Dao.png` |
| Balloon | Same archive / `Images/TutorialStage/ChatBalloonNew.png` | `Images/TutorialStage/ChatBalloonNew.png` |
| Font | `Data/GB13000.ttf` | Provider's `fonts/GB13000.ttf`; atlas described below |
| Typing | `Sounds/ui.fsb`, zero-based subsound 9 | `Sounds/ui-tut-type.wav` |
| Balloon appearance | `Sounds/ui.fsb`, zero-based subsound 14 | `Sounds/ui-tut-textbox.wav` |

The actual spelling is `ChatBalloonNew.png`. The mouse/skip hint is already present in the original PNG. `Sounds/SoundList.xml` maps `ui-tut-type` and `ui-tut-textbox` to tutorial type/textbox events. cn-279 `sub_512040` passes the bank key to the sound loader; the preparation script reads it from the corresponding executable.

Both decoded WAVs are 44100 Hz mono PCM16. Typing has 2240 samples (50.79 ms); textbox has 8512 samples (193.02 ms). The Python outputs were compared byte for byte against PCM buffers decoded by the client's own sound DLL. Native event queries reported volume 1 and pitch offset 0. No synthesized replacement sound is used.

Typing triggers once per UTF-16 unit, including punctuation and newline units. A delayed frame advances one unit instead of replaying a backlog. Audio sources are bounded at eight; unmounting stops sources and animation frames.

## Glyph atlas

The atlas is generated from the original TTF at size 15, weight 400 and the verified load flags. It contains individual grayscale glyphs, not screenshots of complete sentences. `glyphs.json` retains source rectangles, bearings, integer advances, line height, font hash and generator version. Runtime composition uses the native `size - 1` baseline without synthetic bold, extra outlining or horizontal compression.

Unprepared characters fall back to Canvas using the same loaded TTF. They remain visible when the font supports them, but can differ in edge coverage and metrics. Regenerate an atlas for fixed copy so every character uses the same path. The original pack has 50 translated dialogues and 368 characters; the current five-page setup pack has 236 characters.

The offline rasterizer used here is version 2.13.2. That is not a claim of pixel-identical output to the older version linked into the 2011 executable. Native font choice, flags, size, weight, coordinates and advances are reproduced; runtime scaling and the reference video's compression still affect appearance.

## Vue integration

The component is exported by `src/index.ts`. The host supplies Vue, the library stylesheet and static game resources. The build does not embed `local/` assets into JavaScript.

```vue
<script setup lang="ts">
import { ref } from 'vue';
import { BfUiProvider, BfTutorialDialogue, type BfTutorialPage } from '@bubble-fighter/game-ui-vue';
import '@bubble-fighter/game-ui-vue/style.css';
const progress = ref(1);
const narrator = ref<InstanceType<typeof BfTutorialDialogue>>();
function advance(next: BfTutorialPage | undefined) {
  if (next) progress.value = next.progress;
  // Validate real setup forms before accepting a step transition here.
}
</script>
<template>
  <BfUiProvider theme="v1" resource-root="/game-ui/">
    <button @click="narrator?.start()">Start guide</button>
    <BfTutorialDialogue ref="narrator" stage="FantasySetup" :progress="progress"
      resource-root="/game-ui/tutorial/fantasy-setup/"
      :sound="true" :autoplay="false" :loop="false" @advance="advance" />
  </BfUiProvider>
</template>
```

The provider root supplies theme manifests and original fonts. The dialogue root supplies its own `manifest.json`, `glyphs.json`, `glyphs.png`, the three `Images/TutorialStage` PNGs and the two `Sounds` WAVs. These are distinct roots.

| Prop | Default | Meaning |
| --- | --- | --- |
| `stage` | `Stage1` | Dialogue group in the pack |
| `progress` | `21` | Controlled page identifier |
| `resourceRoot` | `/local/tutorial/cn-279/` | Narrator pack URL prefix |
| `text` | Unset | Optional text override; fixed copy should use a generated pack |
| `character` | `Dizni` | `Dizni` or `Dao` |
| `sound` | `true` | Wait for unlocked, decoded audio before typing |
| `autoplay` | `false` | Request advancement after typing and the page hold interval |
| `loop` | `false` | Presentation sequence skips gameplay-only nodes and wraps at its end |

Events are `ready(pages)`, `complete()`, `advance(nextPage | undefined)` and `error(error)`. `complete` means the current sentence has been revealed, not that setup is finished. The host must update `progress` after `advance`. With looping disabled, native gameplay transitions or the end can return `undefined`. The exposed `start()` method should be called from a user gesture for first-use audio; `restart()` reinitializes the page.

The component is a transparent 1024x768 layer. For an overlay, uniformly scale the entire layer by `min(availableWidth/1024, availableHeight/768)`. Do not stretch the portrait, balloon or font independently. `src/demo/TutorialPreview.vue` demonstrates preview fitting and pack switching.

Actual setup workflows should usually disable automatic advancement and looping, and wait for form validation. `sourceProgress` preserves the source setup step; `part/parts` describe pagination. The current edited setup guide has one page per step.

## Generate fixed copy

Preparation requires Python with Pillow and `freetype-py==2.5.1`. Use x64 Python on Windows so the package can load its matching native library. The current machine's default Python is ARM64; generation was verified with the workspace runtime's x64 Python. Python is not a browser dependency.

```sh
python -m pip install Pillow freetype-py==2.5.1
```

`--python-dependencies` optionally selects a directory populated through `pip --target`. This machine has one at `artifacts/game-ui/tutorial-python-x64`.

Recommended UTF-8 JSON input:

```json
[
  { "progress": 1, "text": "你好！\n我们开始配置吧。", "holdMs": 4500 },
  { "progress": 2, "text": "先给服务器起个名字吧？", "holdMs": -1 }
]
```

`holdMs=-1` disables automatic advancement for that page. String arrays and UTF-8 TXT paragraphs separated by blank lines are also accepted. Explicit line breaks are retained; `clickComplete` defaults to true.

Run from the repository root:

```sh
python tools/game-ui/generate-tutorial-text.py --input <dialogue-input.json> --output tools/game-ui/local/tutorial/fantasy-setup --stage FantasySetup --paginate
```

The command copies original images and audio into an independent output pack and replaces its copy/atlas. It refuses to overwrite the original base pack directly. `--paginate` measures integer advances, keeps Latin phrases and trailing punctuation together, and keeps supplied paragraphs together when they fit one page. It paginates copy that exceeds the original text rectangle, without shrinking the font or stretching the balloon. Original client copy is never automatically wrapped.

Without `--paginate`, text beyond 250x100 fails with a page/line error. A generated `text-report.json` records heights and UTF-16 lengths. New combinations of existing glyphs can reuse an atlas; new characters or punctuation require regeneration.

The existing atlas is not replaced until copy validation succeeds. Temporary generation files use the output directory's disk rather than the system temporary directory.

Supply a local dialogue JSON file; private example copy is not included in the source distribution.

Example command (provide your own UTF-8 dialogue input and locally prepared resources):

```powershell
python tools/game-ui/generate-tutorial-text.py --input <dialogue-input.json> --output tools/game-ui/local/tutorial/fantasy-setup --stage FantasySetup --paginate --python-dependencies <python-dependencies>
```

## Rebuild original resources

```sh
python tools/game-ui/prepare-tutorial.py --client-root <cn-279-client-root>
```

This uses the repository archive tool to extract images/configuration/Chinese translations, decode original audio and build the atlas. It targets the verified cn-279 archive locations and executable addresses. Do not apply it to an unrelated build. Output is `local/tutorial/cn-279/`; intermediate files and archive source logs are under `artifacts/game-ui/tutorial-v1/`.

## Validation and scope

Run `npm run build:game-ui` and `npm test` in `web/`. To run just the game UI tests, use `node --test tools/game-ui/*.test.mjs` from the repository root. Timing, delayed-frame sound behavior, clicks, page transitions and audio lifecycle use synthetic fixtures and always run without game files.

Atlas coverage, setup pagination and original PCM checks are separate integration tests. They skip only when their private resource group is absent from `tools/game-ui/local/`. Set `FT_GAME_UI_RESOURCES` to a locally prepared resource root to require all three groups (`tutorial/cn-279/`, `tutorial/fantasy-setup/`, `sounds/`); a missing or invalid explicitly selected resource fails the tests. A skipped integration test does not establish original-client acceptance or permission to redistribute the assets.

This delivery implements dialogue rendering and example copy. It does not execute server naming, client import, database installation or administrator creation. Other tutorial image actions, gameplay, camera paths and all native runtime branches remain outside this component.

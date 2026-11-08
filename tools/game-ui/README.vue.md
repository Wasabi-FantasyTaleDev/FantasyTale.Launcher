# Bubble Fighter Vue 3 UI library

This package wraps the same native evidence renderer in Vue 3 components. The renderer still uses the unpacked SMF/UI resources and the original Chinese font files; Vue owns composition, state and events.

## Use

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { BfUiProvider, BfGamePage, BfCheckbox, BfSlider } from '@bubble-fighter/game-ui-vue'
const theme = ref<'v1' | 'v2' | 'v3'>('v2')
const enabled = ref(true)
const volume = ref(75)
</script>

<template>
  <BfUiProvider :theme="theme" resource-root="/bubble-fighter-ui/">
    <BfGamePage page="settings" @action="onAction" @change="onChange" />
    <BfCheckbox v-model="enabled" label="启用界面叠加" :x="17" :y="33" />
    <BfSlider v-model="volume" name="overlayOpacity" :x="62" :y="123" :width="210" />
  </BfUiProvider>
</template>
```

`resource-root` must contain `fonts/` and the requested theme folders `v1/`, `v2/`, `v3/`, each with its own `manifest.json` and extracted sprites. With sound enabled it also requires `sounds/manifest.json` and the exported WAV files. The component library does not bundle game client files or fonts. Set `theme` to switch the same component tree. `client-date="2017-02-07"` or any later date automatically selects v3; earlier dates retain the explicit theme choice.

Available components are `BfUiProvider`, `BfWindow`, `BfPanel`, `BfGroupBox`, `BfLabel`, `BfCheckbox`, `BfSlider`, `BfButton`, `BfControl`, and `BfGamePage`. `BfCheckbox` and `BfSlider` use `v-model`; all controls forward `bf-change` as Vue `change` and buttons/windows forward `bf-action` as Vue `action`. `BfWindow` emits `close` when its native close button is used.

The demo is a Vite Vue app in `src/demo`. Run `npm run dev` for a local preview, `npm run type-check` for strict Vue TypeScript checks, and `npm run build` for the distributable library.

`BfDialog` supplies the shared message/custom-body dialog. `BfMessageBox` wraps it. `BfTutorialDialogue` supplies the cn2011 narrator with original character/balloon assets, glyph atlas and typing samples. Its separate resource root and copy-generation workflow are described in [TUTORIAL.md](TUTORIAL.md) and [TUTORIAL.zh.md](TUTORIAL.zh.md).

## Shared extension settings

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { BfUiProvider, BfExtensionSettings, type BfExtensionSettingsValue } from '@bubble-fighter/game-ui-vue'
const settings = ref<BfExtensionSettingsValue>({
  oneClickRun: false, healthDisplay: 'percent', allowEscLeave: false, bgm: 'old'
})
function save(value: BfExtensionSettingsValue) {
  // Persist the selected values in the host application.
  console.log(value)
}
</script>

<template>
  <BfUiProvider theme="v2" :volume="0.75" resource-root="/bubble-fighter-ui/">
    <BfExtensionSettings v-model="settings" @save="save" />
  </BfUiProvider>
</template>
```

The title is rendered as extension settings in Chinese. The component uses one
body layout and one settings model for all themes. Its game/sound tabs contain
rendered text and CSS surfaces, with no tab text bitmap. Changes emit `change`
and `update:modelValue`; confirmation emits `save`, and cancel/close emits
`cancel`. Draft changes remain across theme switches. The host owns persistence
and cancellation rollback. The library only renders and emits selected values.

`healthDisplay` is `percent` or `value`, and `bgm` is `old` or `new`; exactly one
value in each pair remains selected. The shared body has width 350 logical
units, a 3-unit panel inset and a 5-unit group inset. Theme tokens supply group
skin, text treatment and native button sizes/alignment. Window height includes
the actual button bottom plus the scheme's bottom margin. These are custom page
dimensions, not a claim that every native option page shares one size.

Native group surfaces are `Images/Ui/DialogSkin-Basic04.smf` for v1/v2 and
`Images/Ui/dialogueNew/dialogue_basic03.smf` for v3. Other button/skin paths are
listed in [research/rendering.md](research/rendering.md). The custom tab surfaces
approximate the theme in CSS; they are not original tab sprites.

## Shared sound player

Prepare resources with `python prepare-ui-sounds.py --client-root <client-folder>
--key <original-bank-key>` from this directory. Extraction keys and private
research records are outside the public source export. Keep prepared resources
local; see [resource validation](TUTORIAL.md) for the external-resource checks.
Provider `sound` defaults to true and `volume` defaults to 0.75, corresponding
to the verified game effects setting of 75/100. Do not normalize exported PCM
or apply the event gain a second time. Use `effectVolumeGain(percent)` to convert
an integer game effects value to a normalized provider value.

The provider exposes `unlockSound()` for a host's initial gesture and
`playSound(event)` for explicit events. `BfDialog`/`BfMessageBox` accept
`open-sound="fail"` or `:open-sound="false"`; the icon does not choose the sound.
The player automatically binds normal hover, press, checkbox, open and close
events and reuses the bank across v1/v2/v3. Tutorial sound remains independent.

## Launcher text presentation

The named `Default`, `DefaultBold` and `NexonBTBold` fonts map to the client
`GB13000.ttf`, loaded as BF at weight 400. Explicit Malgun uses `DF_GB_Y9.TTF`,
loaded as BFMalgun. The Launcher retains the previously approved v1/v2 body,
blank-label and text-tab presentation in BFMalgun. V1 confirm/cancel labels keep
BFMalgun at 13 logical pixels; section and window headings keep BF. V3 retains
its own native declarations. This is an intentional host presentation choice;
the native inheritance evidence remains in `research/rendering.md`.

The Canvas renderer retains four-times sampling and explicit logical output
sizes. Tutorial prepared FreeType glyphs remain a separate implementation.
Identical files and weight do not guarantee identical antialiasing to the native
client. See `README.md` for the Launcher build and resource requirements.

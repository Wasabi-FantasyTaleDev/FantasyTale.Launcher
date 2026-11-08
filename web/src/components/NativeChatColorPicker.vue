<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { colorChannels, hexColor, rgbColor, savedColors } from '../colors'
import WButton from '../controls/WButton.vue'

const props = defineProps<{ modelValue: string }>()
const emit = defineEmits<{ 'update:modelValue': [string] }>()
const { t } = useI18n()
const storageKey = 'nativeUi.savedChatColors'
const hex = ref(props.modelValue)
const rgb = ref(colorChannels(props.modelValue))
const saveError = ref(false)
const selected = computed(() => hexColor(props.modelValue))
const saved = ref<string[]>(readSaved())
// kr-1256 sub_4D78B0 maps the native chat palette to these opaque ARGB colors.
const presets = ['#FFFFFF', '#FFCC00', '#FF0000', '#7C5DFF', '#30DB02', '#F6A822', '#F145A6', '#00EBDE', '#E05CF0', '#C0C0C0', '#FAFAD2', '#FF6400', '#FF544C', '#FFFF00']
function readSaved() {
  try { return savedColors(localStorage.getItem(storageKey)) } catch { return [] }
}
watch(() => props.modelValue, value => {
  if (value === hex.value) return
  hex.value = value
  rgb.value = colorChannels(value)
})
function choose(value: string) {
  hex.value = value
  rgb.value = colorChannels(value)
  emit('update:modelValue', value)
}
function editHex(value: string) {
  hex.value = value
  rgb.value = colorChannels(value)
  emit('update:modelValue', hexColor(value) ?? value)
}
function editRgb(index: number, value: string) {
  rgb.value[index] = value
  const color = rgbColor(rgb.value)
  hex.value = color ?? ''
  emit('update:modelValue', color ?? '')
}
function persist(colors: string[]) {
  saveError.value = false
  try { localStorage.setItem(storageKey, JSON.stringify(colors)); saved.value = colors }
  catch { saveError.value = true }
}
function save() {
  if (selected.value) persist([...new Set([...readSaved(), selected.value])])
}
function remove(value: string) { persist(readSaved().filter(color => color !== value)) }
</script>

<template>
  <div class="chat-color-picker">
    <div class="color-inputs">
      <input class="color-well" type="color" :value="selected ?? '#FFFFFF'" :aria-label="t('nativeUi.chooseColor')" @input="choose(($event.target as HTMLInputElement).value.toUpperCase())" />
      <input class="hex-input" :value="hex" :aria-label="t('nativeUi.color')" placeholder="#FFFFFF" spellcheck="false" :aria-invalid="!selected" @input="editHex(($event.target as HTMLInputElement).value)" />
      <div class="rgb-inputs">
        <label v-for="(channel, index) in ['R', 'G', 'B']" :key="channel"><span>{{ channel }}</span><input type="number" min="0" max="255" step="1" inputmode="numeric" :value="rgb[index]" :aria-label="channel" @input="editRgb(index, ($event.target as HTMLInputElement).value)" /></label>
      </div>
    </div>
    <span class="palette-label">{{ t('nativeUi.gameColors') }}</span>
    <div class="swatches">
      <button v-for="color in presets" :key="color" type="button" class="swatch" :class="{ selected: selected === color }" :style="{ '--swatch': color }" :aria-label="color" :aria-pressed="selected === color" :title="color" @click="choose(color)" />
    </div>
    <div class="saved-heading"><span class="palette-label">{{ t('nativeUi.savedColors') }}</span><WButton :disabled="!selected || saved.includes(selected)" @click="save">{{ t('nativeUi.saveColor') }}</WButton></div>
    <div v-if="saved.length" class="saved-colors">
      <div v-for="color in saved" :key="color" class="saved-color">
        <button type="button" :aria-pressed="selected === color" @click="choose(color)"><span :style="{ background: color }" />{{ color }}</button>
        <button type="button" class="remove-color" :aria-label="t('nativeUi.removeColor', { color })" :title="t('nativeUi.removeColor', { color })" @click="remove(color)">×</button>
      </div>
    </div>
    <span v-if="!selected" class="color-error" role="status">{{ t('nativeUi.invalidColor') }}</span>
    <span v-if="saveError" class="color-error" role="alert">{{ t('nativeUi.colorSaveFailed') }}</span>
  </div>
</template>

<style scoped>
.chat-color-picker { display: grid; gap: 10px; min-width: 0; }
.color-inputs, .rgb-inputs, .rgb-inputs label, .swatches, .saved-heading, .saved-colors, .saved-color, .saved-color button { display: flex; align-items: center; gap: 8px; }
.color-inputs, .swatches, .saved-colors { flex-wrap: wrap; }
.color-well { width: 40px; height: 34px; padding: 3px; cursor: pointer; }
input { box-sizing: border-box; min-width: 0; border: 1px solid var(--ctrl-stroke-default); border-radius: var(--radius-control); background: var(--ctrl-fill-default); color: var(--text-primary); font: inherit; }
.hex-input { width: 104px; padding: 6px 10px; font-family: monospace; }
.rgb-inputs { flex-wrap: wrap; font-size: 12px; }
.rgb-inputs input { width: 64px; padding: 6px; }
.palette-label { color: var(--text-secondary); font-size: 12px; }
.swatch { width: 28px; height: 28px; border: 1px solid var(--ctrl-stroke-default); border-radius: 5px; background: var(--swatch); cursor: pointer; transition: outline-color 120ms ease; }
.swatch.selected { outline: 2px solid var(--accent); outline-offset: 2px; }
.saved-heading { justify-content: space-between; flex-wrap: wrap; }
.saved-color { gap: 0; border: 1px solid var(--ctrl-stroke-default); border-radius: var(--radius-control); overflow: hidden; }
.saved-color button { padding: 6px 8px; background: var(--ctrl-fill-default); color: var(--text-primary); font-size: 12px; cursor: pointer; }
.saved-color button:hover { background: var(--ctrl-fill-secondary); }
.saved-color button[aria-pressed=true] { box-shadow: inset 0 -2px var(--accent); }
.saved-color button span { width: 16px; height: 16px; border-radius: 3px; border: 1px solid var(--ctrl-stroke-default); }
.saved-color .remove-color { border-left: 1px solid var(--ctrl-stroke-default); }
.color-error { font-size: 12px; color: var(--system-critical); }
@media (prefers-reduced-motion: reduce) { .swatch { transition: none; } }
</style>

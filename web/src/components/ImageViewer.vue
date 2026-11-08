<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import WDialog from '../controls/WDialog.vue'
import WButton from '../controls/WButton.vue'
import FluentIcon from '../controls/FluentIcon.vue'

const props = defineProps<{ open: boolean; images: string[]; index: number; title: string }>()
const emit = defineEmits<{ close: []; 'update:index': [value: number]; failed: [url: string] }>()
const { t } = useI18n()
const current = computed(() => props.images[props.index])
function move(direction: number) {
  if (props.images.length > 1) emit('update:index', (props.index + direction + props.images.length) % props.images.length)
}
function failed(event: Event) {
  const url = (event.target as HTMLImageElement).getAttribute('src')
  if (url) emit('failed', url)
}
function onArrow(event: KeyboardEvent) {
  if (!props.open || !current.value || event.altKey || event.ctrlKey || event.metaKey || !['ArrowLeft', 'ArrowRight'].includes(event.key)) return
  event.preventDefault(); event.stopPropagation()
  move(event.key === 'ArrowLeft' ? -1 : 1)
}
onMounted(() => document.addEventListener('keydown', onArrow, true))
onBeforeUnmount(() => document.removeEventListener('keydown', onArrow, true))
</script>
<template>
  <WDialog :open="open && !!current" :title="t('serverInfo.imagePreview')" image-viewer show-dismiss-button dismiss-on-backdrop :dismiss-label="t('titlebar.close')" @close="emit('close')">
    <div class="image-viewer-shell">
      <header><strong>{{ title }}</strong><span v-if="images.length > 1" aria-live="polite">{{ index + 1 }} / {{ images.length }}</span></header>
      <div class="image-viewer-stage"><WButton v-if="images.length > 1" variant="subtle" :aria-label="t('serverInfo.previousImage')" @click="move(-1)"><FluentIcon glyph="chevronLeft" :size="22" /></WButton><div class="image-viewer-frame"><Transition name="viewer-image" mode="out-in"><img :key="current" :src="current" :alt="t('serverInfo.imageAlt', { name: title, index: index + 1 })" referrerpolicy="no-referrer" @error="failed" /></Transition></div><WButton v-if="images.length > 1" variant="subtle" :aria-label="t('serverInfo.nextImage')" @click="move(1)"><FluentIcon glyph="chevronRight" :size="22" /></WButton></div>
    </div>
  </WDialog>
</template>
<style scoped>
.image-viewer-shell { height: min(820px,calc(100dvh - 72px)); display: flex; flex-direction: column; min-height: 0; }
header { display: flex; align-items: center; gap: 14px; padding: 18px 64px 18px 24px; min-height: 56px; box-sizing: border-box; flex: none; }header strong { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 14px; font-weight: 500; }header span { color: var(--text-secondary); font-size: 12px; font-variant-numeric: tabular-nums; flex: none; }
.image-viewer-stage { display: flex; gap: 12px; align-items: stretch; flex: 1; min-height: 0; padding: 0 16px 20px; }.image-viewer-stage > .w-btn { flex: none; align-self: center; width: 42px; height: 56px; border-radius: 7px; }.image-viewer-frame { display: grid; place-items: center; min-width: 0; min-height: 0; flex: 1; }.image-viewer-frame img { display: block; width: 100%; height: 100%; min-height: 0; object-fit: contain; }
.viewer-image-enter-active { transition: opacity 180ms ease-out; }.viewer-image-leave-active { transition: opacity 100ms ease-in; }.viewer-image-enter-from, .viewer-image-leave-to { opacity: 0; }
@media(max-width:720px) { .image-viewer-stage { gap: 4px; padding-inline: 6px; }.image-viewer-stage > .w-btn { width: 30px; } }
@media(prefers-reduced-motion:reduce) { .viewer-image-enter-active, .viewer-image-leave-active { transition: none; } }
</style>

<script setup lang="ts">
// SVGs cover glyphs absent from MDL2 and keep the account silhouette stable.
import { computed } from 'vue'
import { glyphs, type GlyphName } from '../icons'

const props = withDefaults(defineProps<{ glyph: GlyphName; size?: number }>(), { size: 16 })

const char = computed(() => glyphs[props.glyph])
const usePrivateFont = typeof __PRIVATE_ICON_FONT__ !== 'undefined' && __PRIVATE_ICON_FONT__
</script>

<template>
  <svg v-if="glyph === 'contact'" class="fluent-icon" :width="size" :height="size" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><circle cx="12" cy="7.5" r="3.25" /><path d="M5 20v-2.25A4.75 4.75 0 0 1 9.75 13h4.5A4.75 4.75 0 0 1 19 17.75V20" /></svg>
  <svg v-else-if="!usePrivateFont && glyph === 'clock'" class="fluent-icon" :width="size" :height="size" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="9" /><path d="M12 6.5V12l3.5 2" /></svg>
  <svg v-else-if="!usePrivateFont && glyph === 'blocked'" class="fluent-icon" :width="size" :height="size" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="9" /><path d="m5.64 5.64 12.72 12.72" /></svg>
  <svg v-else-if="glyph === 'github'" class="fluent-icon" :width="size" :height="size" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false"><path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27s1.36.09 2 .27c1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0 0 16 8c0-4.42-3.58-8-8-8Z" /></svg>
  <span v-else class="fluent-icon" :style="{ fontSize: `${size}px` }" aria-hidden="true">{{ char }}</span>
</template>

<style scoped>
.fluent-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-family: 'Segoe Fluent Icons', 'Segoe MDL2 Assets', sans-serif;
  font-weight: 400;
  line-height: 1;
  flex: none;
}
</style>

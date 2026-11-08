<script setup lang="ts">
import FluentIcon from '../controls/FluentIcon.vue'
import type { GlyphName } from '../icons'

withDefaults(defineProps<{ name: string; glyph: GlyphName; as?: 'div' | 'li'; separateControls?: boolean }>(), { as: 'div' })
</script>

<template>
  <component :is="as" class="package-summary-item">
    <div class="package-summary-card">
    <span class="package-symbol"><FluentIcon :glyph="glyph" :size="24" /></span>
    <div class="package-copy"><strong>{{ name }}</strong><div v-if="$slots.meta" class="package-meta"><slot name="meta" /></div></div>
      <div v-if="$slots.default && !separateControls" class="package-controls"><slot /></div>
    </div>
    <div v-if="$slots.default && separateControls" class="package-external-controls"><slot /></div>
  </component>
</template>

<style scoped>
.package-summary-item { min-width: 0; }
.package-external-controls { min-width: 0; }
.package-external-controls:has(> *) { margin-top: 16px; }
.package-external-controls :deep(.task-progress) { margin-top: 0; }
.package-summary-card { display: grid; grid-template-columns: 46px minmax(0, 1fr); align-items: center; gap: 12px 14px; padding: 14px 16px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-overlay); background: var(--card-bg-default); }
.package-controls { grid-column: 2; min-width: 0; }
.package-symbol { display: grid; place-items: center; width: 46px; height: 46px; border: 1px solid color-mix(in srgb, var(--accent-default) 16%, transparent); border-radius: 10px; color: var(--accent-default); background: color-mix(in srgb, var(--accent-default) 8%, var(--card-bg-default)); }
.package-copy { min-width: 0; display: grid; gap: 5px; }
.package-copy strong { font-size: 14px; line-height: 20px; font-weight: 600; overflow-wrap: anywhere; }
.package-meta { display: flex; flex-wrap: wrap; gap: 4px 6px; color: var(--text-secondary); font-size: 12px; line-height: 17px; overflow-wrap: anywhere; }
</style>

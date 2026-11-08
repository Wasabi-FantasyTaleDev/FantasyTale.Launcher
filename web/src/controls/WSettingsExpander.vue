<script setup lang="ts">
// SettingsExpander (CommunityToolkit pattern): a SettingsCard header that expands to
// reveal child rows on a secondary card background.
import { useId } from 'vue'
import FluentIcon from './FluentIcon.vue'
import type { GlyphName } from '../icons'

withDefaults(defineProps<{ icon?: GlyphName; header: string; description?: string }>(), {
  icon: undefined,
  description: '',
})

const open = defineModel<boolean>('open', { default: false })
const bodyId = useId()
</script>

<template>
  <div class="w-expander" :class="{ open }">
    <button type="button" class="w-expander-header" :aria-expanded="open" :aria-controls="bodyId" @click="open = !open">
      <span v-if="icon" class="w-card-icon"><FluentIcon :glyph="icon" :size="20" /></span>
      <span class="w-card-text">
        <span class="w-card-header type-body-strong">{{ header }}</span>
        <span v-if="description" class="w-card-desc type-caption">{{ description }}</span>
      </span>
      <span class="w-expander-chevron"><FluentIcon glyph="chevronDown" :size="12" /></span>
    </button>
    <div :id="bodyId" class="w-expander-collapse" role="region" :aria-label="header" :aria-hidden="!open" :inert="!open">
      <div class="w-expander-clip"><div class="w-expander-body"><slot /></div></div>
    </div>
  </div>
</template>

<style scoped>
.w-expander {
  border-radius: var(--radius-overlay);
  background: var(--card-bg-default);
  border: 1px solid var(--card-stroke-default);
  overflow: hidden;
}
.w-expander-header {
  display: flex;
  align-items: center;
  gap: 16px;
  width: 100%;
  min-height: 68px;
  padding: 16px 24px;
  color: var(--text-primary);
  font-size: 14px;
  text-align: left;
  transition: background-color var(--duration-faster) linear;
}
.w-expander-header:hover {
  background: var(--ctrl-fill-secondary);
}
.w-expander-header:active {
  background: var(--ctrl-fill-tertiary);
}
.w-card-icon {
  flex: none;
  width: 32px;
  height: 32px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--text-secondary);
}
.w-card-text {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.w-card-header {
  color: var(--text-primary);
}
.w-card-desc {
  color: var(--text-secondary);
}
.w-expander-chevron {
  color: var(--text-secondary);
  transition: transform 240ms var(--ease-fast-out-slow-in);
}
.w-expander.open > .w-expander-header > .w-expander-chevron { transform: rotate(180deg); }
.w-expander-collapse {
  display: grid;
  grid-template-rows: 0fr;
  opacity: 0;
  transition: grid-template-rows 240ms var(--ease-fast-out-slow-in), opacity 180ms linear;
}
.open > .w-expander-collapse { grid-template-rows: 1fr; opacity: 1; }
.w-expander-clip {
  min-height: 0;
  overflow: hidden;
}
.w-expander-body {
  padding: 0 24px 16px 72px;
  border-top: 1px solid var(--stroke-divider);
  background: var(--card-bg-secondary);
}
@media (prefers-reduced-motion: reduce) {
  .w-expander-collapse, .w-expander-chevron { transition: none; }
}
</style>

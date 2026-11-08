<script setup lang="ts">
import { useId } from 'vue'
import FluentIcon from './FluentIcon.vue'
import type { GlyphName } from '../icons'

defineProps<{ header: string; description?: string; icon?: GlyphName; count?: number; settings?: boolean }>()
const open = defineModel<boolean>('open', { default: false })
const regionId = useId()
</script>

<template>
  <section class="w-content-expander" :class="{ expanded: open, 'settings-expander': settings }">
    <div class="expander-heading">
    <button type="button" class="heading-toggle" :aria-expanded="open" :aria-controls="regionId" @click="open = !open">
      <FluentIcon v-if="icon" :glyph="icon" :size="settings ? 20 : 18" class="heading-icon" />
      <span class="heading-text"><span>{{ header }}</span><small v-if="description">{{ description }}</small></span>
      <span v-if="count != null" class="heading-count">{{ count }}</span>
    </button>
      <span v-if="$slots.action" class="heading-action"><slot name="action" /></span>
      <button type="button" class="expander-arrow" :aria-label="header" :aria-expanded="open" :aria-controls="regionId" @click="open = !open"><FluentIcon glyph="chevronDown" :size="12" /></button>
    </div>
    <div :id="regionId" class="expander-reveal" role="region" :aria-label="header" :aria-hidden="!open" :inert="!open">
      <div class="expander-clip"><div class="expander-content"><slot /></div></div>
    </div>
  </section>
</template>

<style scoped>
.w-content-expander { min-width: 0; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-control); background: var(--card-bg-default); }
.expander-heading { width: 100%; min-height: 48px; display: flex; align-items: center; gap: 12px; padding: 7px 8px 7px 16px; border-radius: inherit; text-align: start; font-size: 14px; line-height: 20px; }
.heading-toggle { display: flex; align-items: center; gap: 12px; flex: 1; min-width: 0; text-align: start; font-weight: 400; }
.heading-action { display: flex; align-items: center; flex: none; }
.heading-text { display: grid; gap: 2px; }
.heading-text small { color: var(--text-secondary); font-size: 12px; font-weight: 400; }
.heading-icon { flex: none; color: var(--text-secondary); }
.heading-text { flex: 1; min-width: 0; overflow-wrap: anywhere; }
.heading-count { color: var(--text-secondary); font-size: 12px; font-variant-numeric: tabular-nums; }
.expander-arrow { display: grid; place-items: center; flex: none; width: 32px; height: 32px; margin-left: 4px; border-radius: var(--radius-control); transition: background-color 120ms linear; }
.expander-arrow :deep(.fluent-icon) { transition: transform 220ms var(--ease-fast-out-slow-in); }
.expander-heading:hover .expander-arrow { background: var(--subtle-secondary); }
.expander-heading:active .expander-arrow { background: var(--subtle-tertiary); }
.expanded .expander-arrow :deep(.fluent-icon) { transform: rotate(180deg); }
.expander-heading:focus-visible { outline-offset: -3px; }
.expander-reveal { display: grid; grid-template-rows: 0fr; opacity: 0; transition: grid-template-rows 240ms var(--ease-fast-out-slow-in), opacity 180ms linear; }
.expanded .expander-reveal { grid-template-rows: 1fr; opacity: 1; }
.expander-clip { min-height: 0; overflow: hidden; border-radius: 0 0 var(--radius-control) var(--radius-control); }
.expander-content { border-top: 1px solid var(--card-stroke-default); padding: 4px 16px; background: var(--card-bg-secondary); }
.settings-expander { border-radius: var(--radius-overlay); }
.settings-expander .expander-heading { min-height: 68px; padding: 16px 16px 16px 24px; gap: 8px; }
.settings-expander .heading-toggle { gap: 16px; }
.settings-expander .heading-icon { width: 32px; height: 32px; padding: 6px; box-sizing: border-box; }
.settings-expander .expander-content { padding: 0 16px 0 72px; }
.settings-expander .expander-content :deep(.w-settings-card) { padding: 16px 0; border: 0; background: transparent; }
@media(prefers-reduced-motion:reduce) { .expander-reveal, .expander-arrow :deep(.fluent-icon) { transition: none; } }
</style>

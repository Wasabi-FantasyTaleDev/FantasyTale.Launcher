<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import WButton from '../controls/WButton.vue'
import WProgressBar from '../controls/WProgressBar.vue'
import WProgressRing from '../controls/WProgressRing.vue'
import FluentIcon from '../controls/FluentIcon.vue'

defineProps<{ progress: { path: string; total: number; completedArchives: number; totalArchives: number; elapsedSeconds: number; percentage: number } }>()
defineEmits<{ cancel: [] }>()
const { t } = useI18n()
const expanded = ref(false)
</script>

<template>
  <section class="home-resource-progress" :aria-label="t('resourcePreparation.title')">
    <div class="resource-heading">
      <button type="button" class="resource-expander" :aria-expanded="expanded" aria-controls="resource-preparation-details" @click="expanded = !expanded">
        <WProgressRing :size="16" />
        <span>{{ t('resourcePreparation.title') }}</span>
        <small>{{ Math.floor(progress.percentage) }}%</small>
        <span class="expander-affordance">{{ t(expanded ? 'updates.collapseDetails' : 'updates.expandDetails') }}<FluentIcon :glyph="expanded ? 'chevronUp' : 'chevronDown'" :size="12" /></span>
      </button>
      <WButton variant="subtle" @click="$emit('cancel')">{{ t('dialog.cancel') }}</WButton>
    </div>
    <WProgressBar class="resource-bar" :value="progress.percentage" :indeterminate="!progress.total" :aria-label="t('resourcePreparation.title')" />
    <p class="resource-metrics">{{ t('resourcePreparation.progress', { done: progress.completedArchives, total: progress.totalArchives, seconds: Math.floor(progress.elapsedSeconds) }) }}</p>
    <div id="resource-preparation-details" class="resource-collapse" :class="{ expanded }" :inert="!expanded" :aria-hidden="!expanded"><div><p>{{ progress.path }}</p></div></div>
  </section>
</template>

<style scoped>
.home-resource-progress { border-top: 1px solid var(--card-stroke-default); padding: 12px 24px; }
.resource-heading { display: flex; align-items: center; gap: 10px; }
.resource-expander { min-width: 0; min-height: 32px; display: flex; align-items: center; gap: 10px; padding: 0 4px; text-align: left; border-radius: 4px; }
.resource-expander > span:first-of-type { font-weight: 600; }
.resource-expander > small, .resource-metrics { font-size: 12px; color: var(--text-secondary); font-variant-numeric: tabular-nums; }
.resource-expander:hover { background: var(--subtle-secondary); }
.resource-expander:active { background: var(--subtle-tertiary); }
.expander-affordance { display: inline-flex; align-items: center; white-space: nowrap; gap: 8px; margin-left: 8px; font-size: 12px; color: var(--accent-text-primary); }
.resource-heading > .w-btn { flex: none; margin-left: auto; font-size: 12px; }
.resource-bar { margin-top: 8px; }
.resource-metrics { margin: 8px 0 0; }
.resource-collapse { display: grid; grid-template-rows: 0fr; opacity: 0; transition: grid-template-rows 180ms var(--ease-fast-out-slow-in), opacity 140ms ease; }
.resource-collapse.expanded { grid-template-rows: 1fr; opacity: 1; }
.resource-collapse > div { min-height: 0; overflow: hidden; }
.resource-collapse p { overflow-wrap: anywhere; color: var(--text-secondary); font-size: 12px; margin: 8px 0 0; }
@media (prefers-reduced-motion: reduce) { .resource-collapse { transition: none; } }
</style>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import { selectedBuild, selectedInstance, state } from '../state'
import { downloads, downloadTasks, sessionDownloadIds } from '../downloads/live'
import { dismissedCompletionIds } from '../downloads/completions'
import { formatBytes, matchesInstanceUpdate, transferMetrics } from '../downloads/model'
import UpdateActions from './UpdateActions.vue'
import UpdateProgress from './UpdateProgress.vue'
import WButton from '../controls/WButton.vue'
import WComboBox from '../controls/WComboBox.vue'
import WProgressBar from '../controls/WProgressBar.vue'
import FluentIcon from '../controls/FluentIcon.vue'
const { t, locale } = useI18n()
const router = useRouter()
const tasks = computed(() => downloadTasks.value.filter(task => task.stage !== 'canceled' &&
  (task.stage !== 'completed' || !dismissedCompletionIds.has(task.id)) &&
  (task.stage !== 'completed' || task.mode === 'preview' || sessionDownloadIds.has(task.id)) &&
  (task.target.kind === 'launcher' ? task.stage !== 'queued' && task.stage !== 'completed' : matchesInstanceUpdate(task, state.selectedGameId, selectedInstance.value?.id, selectedBuild.value?.id)))
  .sort((a, b) => Number(a.stage === 'completed') - Number(b.stage === 'completed')))
const selectedId = ref('')
const expanded = ref(false)
const task = computed(() => tasks.value.find(item => item.id === selectedId.value) ?? tasks.value[0])
watch(() => task.value?.id, () => { expanded.value = false }, { flush: 'sync' })
const metrics = computed(() => task.value ? transferMetrics(task.value) : null)
const taskOptions = computed(() => tasks.value.map(item => ({ value: item.id, label: localized(item.target.nameLocalized ?? undefined) || item.target.name })))
function details(id: string) { downloads.selectedId = id; void router.push({ path: '/downloads', query: { task: id } }) }
</script>
<template>
  <Transition name="home-completion"><div v-if="task" class="home-updates"><div class="home-updates-content">
    <section class="home-update" :aria-label="t(`updates.${task.target.kind}`)">
      <div class="update-heading">
        <button type="button" class="update-expander" :aria-expanded="expanded" aria-controls="home-update-details" @click="expanded = !expanded">
          <FluentIcon :glyph="task.stage === 'completed' ? 'completed' : task.target.kind === 'launcher' ? 'refresh' : 'download'" :size="18" />
          <span>{{ t(`updates.${task.stage}`) }}</span>
          <small v-if="metrics?.percent != null">{{ Math.floor(metrics.percent) }}%</small>
          <small v-if="metrics?.speed">{{ formatBytes(metrics.speed, locale) }}/s</small>
          <span class="expander-affordance">{{ t(expanded ? 'updates.collapseDetails' : 'updates.expandDetails') }}<FluentIcon :glyph="expanded ? 'chevronUp' : 'chevronDown'" :size="12" /></span>
        </button>
        <WComboBox v-if="tasks.length > 1" :model-value="task.id" :options="taskOptions" :aria-label="t('updates.all')" class="task-choice" @update:model-value="selectedId = $event" />
        <UpdateActions :task="task" :hide-retry="task.stage === 'failed'"><template #before-actions><WButton :variant="task.stage === 'failed' ? 'accent' : 'subtle'" @click="details(task.id)">{{ t('updates.openDownloads') }}<FluentIcon glyph="chevronRight" :size="10" /></WButton></template></UpdateActions>
      </div>
      <WProgressBar v-if="!expanded && !['ready', 'completed', 'failed', 'canceled'].includes(task.stage)" class="collapsed-progress" :value="metrics?.percent ?? 0" :aria-label="t('updates.total')" />
      <div id="home-update-details" class="update-collapse" :class="{ expanded }" :inert="!expanded" :aria-hidden="!expanded"><div><UpdateProgress :task="task" compact /></div></div>
    </section>
  </div></div></Transition>
</template>
<style scoped>
.home-updates { display: grid; grid-template-rows: 1fr; opacity: 1; }
.home-updates-content { min-height: 0; overflow: hidden; }
.home-update { border-top: 1px solid var(--card-stroke-default); }
.home-completion-enter-active, .home-completion-leave-active { transition: opacity 200ms ease, grid-template-rows 260ms var(--ease-fast-out-slow-in); }
.home-completion-enter-from, .home-completion-leave-to { opacity: 0; grid-template-rows: 0fr; }
.home-update { padding: 12px 24px; }
.update-heading { display: flex; align-items: center; gap: 10px; }
.update-expander { flex: none; min-width: 0; min-height: 32px; display: flex; align-items: center; gap: 10px; padding: 0 4px; text-align: left; border-radius: 4px; }
.update-expander > .fluent-icon:first-child { color: var(--accent-text-primary); flex: none; }
.update-expander > span:first-of-type { font-weight: 600; }
.update-expander > small { font-size: 12px; color: var(--text-secondary); font-variant-numeric: tabular-nums; }
.update-expander:hover { background: var(--subtle-secondary); }
.update-expander:active { background: var(--subtle-tertiary); }
.expander-affordance { display: inline-flex; align-items: center; flex: none; white-space: nowrap; gap: 8px; margin-left: 8px; font-size: 12px; color: var(--accent-text-primary); }
.update-heading > .update-actions { flex: none; margin-left: auto; }
.collapsed-progress { margin-top: 8px; }
.update-collapse { display: grid; grid-template-rows: 0fr; opacity: 0; transition: grid-template-rows 180ms var(--ease-fast-out-slow-in), opacity 140ms ease; }
.update-collapse.expanded { grid-template-rows: 1fr; opacity: 1; }
.update-collapse > div { min-height: 0; overflow: hidden; }
.update-collapse :deep(.update-progress) { padding-top: 12px; }
.update-heading :deep(.w-btn) { font-size: 12px; }
.task-choice { width: 220px; flex: none; }
@media (prefers-reduced-motion: reduce) { .update-collapse, .home-completion-enter-active, .home-completion-leave-active { transition: none; } }
</style>

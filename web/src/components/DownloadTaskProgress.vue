<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { downloadTasks } from '../downloads/live'
import { downloadStageKey, formatBytes, transferMetrics, type DownloadTask } from '../downloads/model'
import WProgressBar from '../controls/WProgressBar.vue'

const props = defineProps<{ task?: DownloadTask; taskId?: string; pending?: boolean; compact?: boolean }>()
const { t, locale } = useI18n()
const current = computed(() => downloadTasks.value.find(task => task.id === (props.taskId ?? props.task?.id)) ?? props.task)
const metrics = computed(() => current.value ? transferMetrics(current.value) : undefined)
const value = computed(() => {
  const task = current.value
  if (!task) return null
  if (task.stage === 'completed') return 100
  if (['checking', 'verifying', 'installing'].includes(task.stage)) return task.stageProgress
  return metrics.value?.percent ?? null
})
const eta = computed(() => {
  const seconds = metrics.value?.remainingSeconds
  if (seconds == null) return t('updates.estimating')
  return seconds < 60 ? t('updates.seconds', { n: seconds }) : seconds < 3600 ? t('updates.minutes', { n: Math.ceil(seconds / 60) }) : t('updates.hours', { n: Math.ceil(seconds / 3600) })
})
</script>

<template>
  <div v-if="pending && !current" class="task-progress" :class="{ compact }" role="status"><WProgressBar indeterminate :aria-label="t('updates.checking')" /><span class="task-caption">{{ t('updates.checking') }}</span></div>
  <div v-else-if="current" class="task-progress" :class="{ compact }">
    <WProgressBar :value="value ?? 0" :indeterminate="value == null" :aria-label="current.target.name" />
    <div v-if="compact" class="task-caption compact-caption" role="status">
      <span>{{ t(downloadStageKey(current)) }}</span>
      <span v-if="metrics && current.stage === 'downloading'">{{ formatBytes(metrics.received, locale) }} / {{ formatBytes(metrics.total, locale) }}</span>
      <span v-if="metrics && current.stage === 'downloading' && metrics.speed > 0">{{ formatBytes(metrics.speed, locale) }}/s</span>
      <span v-if="current.stage === 'downloading'">{{ eta }}</span>
      <span v-if="value != null">{{ Math.floor(value) }}%</span>
    </div>
    <template v-else>
    <div class="task-caption" role="status"><span>{{ t(downloadStageKey(current)) }}</span><span v-if="value != null">{{ Math.floor(value) }}%</span></div>
    <div v-if="metrics && current.stage === 'downloading'" class="task-caption">
      <span>{{ formatBytes(metrics.received, locale) }} / {{ formatBytes(metrics.total, locale) }}</span>
      <span v-if="metrics.speed > 0">{{ formatBytes(metrics.speed, locale) }}/s</span>
    </div>
    </template>
    <p v-if="current.failure" class="task-error" role="alert">{{ t(current.failure === 'source' ? 'clientUpdates.sourceError' : current.failure === 'busy' ? 'updates.fileBusyError' : current.failure === 'access' ? 'updates.fileAccessError' : current.failure === 'archive-password' ? 'updates.archivePassword' : current.failure === 'startup' ? 'launcherUpdates.startup' : `updates.${current.failure}Error`) }}</p>
  </div>
</template>

<style scoped>
.task-progress { display: grid; gap: 8px; width: 100%; margin-top: 12px; }
.task-caption { display: flex; justify-content: space-between; gap: 12px; color: var(--text-secondary); font-size: 12px; font-variant-numeric: tabular-nums; }
.task-progress.compact { gap: 6px; margin: 0; padding: 0 20px 12px; }
.compact-caption { justify-content: flex-start; gap: 14px; white-space: nowrap; overflow: hidden; }
.compact-caption span:first-child { color: var(--text-primary); }
.compact-caption span:nth-last-child(1) { margin-left: auto; }
.task-error { margin: 0; color: var(--text-critical); font-size: 12px; }
@media (max-width: 760px) { .task-progress.compact { padding-inline: 16px; }.compact-caption { gap: 8px; font-size: 11px; flex-wrap: wrap; } }
</style>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { downloadStageKey, fileBytes, formatBytes, percent, transferMetrics, type DownloadTask } from '../downloads/model'
import WProgressBar from '../controls/WProgressBar.vue'
import FluentIcon from '../controls/FluentIcon.vue'

const props = defineProps<{ task: DownloadTask; selectedPath?: string; compact?: boolean }>()
const { t, locale } = useI18n()
const metrics = computed(() => transferMetrics(props.task))
const bytes = (value: number | null) => formatBytes(value, locale.value)
const file = computed(() => props.task.files.find(f => f.path === props.selectedPath) ?? metrics.value.current)
const filePercent = computed(() => file.value ? percent(fileBytes(file.value), file.value.size) : null)
const percentText = (value: number | null) => value == null ? '—' : `${Math.floor(value)}%`
const eta = computed(() => {
  const seconds = metrics.value.remainingSeconds
  if (seconds == null) return props.task.stage === 'downloading' ? t('updates.estimating') : '—'
  return seconds < 60 ? t('updates.seconds', { n: seconds }) : seconds < 3600 ? t('updates.minutes', { n: Math.ceil(seconds / 60) }) : t('updates.hours', { n: Math.ceil(seconds / 3600) })
})
const note = computed(() => {
  const stage = props.task.stage
  if (stage === 'queued' && props.task.mode === 'live') return t('clientUpdates.queueNote')
  if (stage === 'failed' && props.task.failure === 'source') return t('clientUpdates.sourceError')
  if (stage === 'failed' && props.task.failure === 'startup') return t('launcherUpdates.startup')
  if (stage === 'failed' && props.task.failure === 'archive-password') return t('updates.archivePassword')
  if (stage === 'failed' && props.task.failure === 'space') return t('updates.spaceError')
  if (stage === 'failed' && props.task.failure === 'busy') return t('updates.fileBusyError')
  if (stage === 'failed' && props.task.failure === 'access') return t('updates.fileAccessError')
  if (stage === 'failed') return t(`updates.${props.task.failure === 'integrity' ? 'integrityError' : props.task.failure === 'disk' ? 'diskError' : 'networkError'}`)
  return stage === 'paused' ? t('updates.pauseNote') : stage === 'installing' ? t('updates.installNote') : stage === 'ready' ? t('updates.readyNote') : stage === 'waiting' ? t('updates.waitingNote') : stage === 'completed' ? t('updates.completeNote') : ''
})
const processing = computed(() => ['checking', 'verifying', 'installing'].includes(props.task.stage))
</script>
<template>
  <div class="update-progress" :class="[task.stage, { compact, 'has-file': !!file }]">
    <div class="total-progress">
    <div class="progress-caption"><span>{{ t('updates.total') }}</span><span class="byte-count">{{ bytes(metrics.received) }} / {{ bytes(metrics.total) }}</span><strong>{{ percentText(metrics.percent) }}</strong></div>
    <WProgressBar class="total-track" :value="metrics.percent ?? 0" :indeterminate="metrics.percent == null && task.stage === 'downloading'" :aria-label="t('updates.total')" />
    <div class="transfer-stats"><span>{{ t('updates.transferred', { done: metrics.transferred, total: task.files.length }) }}</span><span>{{ t('updates.speed') }} <strong>{{ metrics.speed ? `${bytes(metrics.speed)}/s` : '—' }}</strong></span><span>{{ t('updates.remaining') }} <strong>{{ eta }}</strong></span></div>
    </div>
    <Transition name="file-progress">
    <div v-if="file" class="file-progress">
      <div class="progress-caption"><span>{{ t(selectedPath ? 'updates.selectedFile' : 'updates.currentFile') }}</span><span class="file-path" :title="file.path">{{ file.path }}</span><strong>{{ percentText(filePercent) }}</strong></div>
      <WProgressBar :value="filePercent ?? 0" :indeterminate="filePercent == null && task.stage === 'downloading'" :aria-label="`${t('updates.currentFile')}: ${file.path}`" />
      <div class="file-size"><span>{{ bytes(fileBytes(file)) }} / {{ bytes(file.size) }}</span><span>{{ t(`updates.file_${file.stage}`) }}</span></div>
    </div>
    </Transition>
    <div v-if="processing" class="processing-stage"><div><FluentIcon :glyph="task.stage === 'installing' ? 'hardDrive' : 'refresh'" :size="14" /><span role="status">{{ t(downloadStageKey(task)) }}</span><strong v-if="task.stageProgress != null">{{ percentText(task.stageProgress) }}</strong></div><WProgressBar :value="task.stageProgress ?? 0" :indeterminate="task.stageProgress == null" :aria-label="t(downloadStageKey(task))" /></div>
    <p v-if="note" class="update-note" :class="{ error: task.stage === 'failed' }" role="status"><FluentIcon :glyph="task.stage === 'failed' ? 'warning' : task.stage === 'completed' ? 'completed' : 'info'" :size="14" />{{ note }}</p>
  </div>
</template>
<style scoped>
.update-progress { min-width: 0; font-variant-numeric: tabular-nums; }
.total-progress { min-width: 0; }
.progress-caption { display: flex; align-items: center; gap: 12px; margin-bottom: 9px; font-size: 12px; }
.progress-caption > span:first-child { flex: none; color: var(--text-secondary); }
.progress-caption strong { font-size: 13px; font-weight: 600; min-width: 36px; text-align: right; }
.byte-count, .file-path { flex: 1; min-width: 0; text-align: right; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
.total-track { height: 5px; border-radius: 3px; }
.transfer-stats { display: flex; flex-wrap: wrap; gap: 6px 24px; margin: 10px 0 20px; font-size: 12px; color: var(--text-secondary); }
.transfer-stats > span:first-child { margin-right: auto; }
.transfer-stats strong { color: var(--text-primary); font-weight: 500; margin-left: 6px; }
.file-progress { padding: 13px 16px 10px; border-radius: 5px; background: var(--subtle-secondary); }
.file-size { display: flex; justify-content: space-between; margin-top: 7px; font-size: 11px; color: var(--text-secondary); }
.paused :deep(.w-progress-fill), .canceled :deep(.w-progress-fill) { background: var(--text-tertiary); }
.failed :deep(.w-progress-fill) { background: var(--system-critical, #c42b1c); }
.processing-stage { margin-top: 16px; }
.processing-stage > div:first-child { display: flex; align-items: center; gap: 8px; font-size: 12px; margin-bottom: 8px; }
.processing-stage strong { margin-left: auto; }
.update-note { display: flex; align-items: flex-start; gap: 8px; margin: 14px 0 0; color: var(--text-secondary); font-size: 12px; line-height: 19px; }
.update-note .fluent-icon { flex: none; margin-top: 2px; }
.update-note.error { color: var(--system-critical, #c42b1c); }
.compact { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 0fr); column-gap: 0; transition: grid-template-columns 320ms cubic-bezier(.2,.8,.2,1), column-gap 320ms cubic-bezier(.2,.8,.2,1); }
.compact.has-file { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); column-gap: 32px; }
.compact > .total-progress { grid-column: 1; grid-row: 1; }
.compact .transfer-stats { margin: 8px 0 0; gap: 6px 16px; }
.compact > .file-progress { grid-column: 2; grid-row: 1; min-width: 0; overflow: hidden; background: transparent; padding: 0; }
.compact .file-progress > .w-progress { height: 5px; }
.file-progress-enter-active, .file-progress-leave-active { transition: opacity 240ms ease, transform 320ms cubic-bezier(.2,.8,.2,1); }
.file-progress-enter-from, .file-progress-leave-to { opacity: 0; transform: translateX(8px); }
.compact > .processing-stage, .compact > .update-note { grid-column: 1 / -1; }
.compact > .processing-stage { margin-top: 10px; }
.compact > .update-note { margin-top: 8px; }
@media (prefers-reduced-motion: reduce) { .compact, .file-progress-enter-active, .file-progress-leave-active { transition: none; } .file-progress-enter-from, .file-progress-leave-to { transform: none; } }
@media (max-width: 600px) { .transfer-stats { gap: 6px 16px; } .transfer-stats > span:first-child { flex-basis: 100%; } .file-progress { padding: 12px; } }
</style>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, inHost, on } from '../bridge'
import { state } from '../state'
import { launcherUpdate, launcherUpdatePreview } from '../downloads/launcherUpdate'
import { formatBytes, transferMetrics } from '../downloads/model'
import FluentIcon from '../controls/FluentIcon.vue'
import WDialog from '../controls/WDialog.vue'
import WProgressBar from '../controls/WProgressBar.vue'
import WProgressRing from '../controls/WProgressRing.vue'
import WTooltip from '../controls/WTooltip.vue'
import WButton from '../controls/WButton.vue'

defineProps<{ compact: boolean }>()
const emit = defineEmits<{ layout: [] }>()
const { t, locale } = useI18n()
const { state: flow, active, visible, canInstall, mandatory, show, dismiss, accept, install } = launcherUpdate
const currentVersion = ref<string | null>(launcherUpdatePreview ? '0.1.12' : null)
const requiredUpgrade = ref<{ minimumLauncherVersion: string; currentLauncherVersion: string; subject: string } | null>(null)
const checkingRequirement = ref(false), requirementError = ref('')
async function openManualUpdate() {
  try {
    const manual = await call('launcherUpdates.manualDownload')
    if (manual.url) await call('shell.openExternal', { url: manual.url })
    else requirementError.value = t('launcherUpdates.requiredUnavailable')
  } catch { requirementError.value = t('launcherUpdates.checkNetworkFailed') }
}
const removeRequirementListener = on('launcher.upgradeRequired', data => {
  if (flow.restarting || !data || typeof data !== 'object' ||
    typeof (data as Record<string, unknown>).minimumLauncherVersion !== 'string' ||
    typeof (data as Record<string, unknown>).currentLauncherVersion !== 'string' ||
    typeof (data as Record<string, unknown>).subject !== 'string') return
  requiredUpgrade.value = data as typeof requiredUpgrade.value
  requirementError.value = ''
})
async function checkRequiredUpdate() {
  const selected = requiredUpgrade.value
  if (checkingRequirement.value || !selected) return
  checkingRequirement.value = true; requirementError.value = ''
  try {
    await call('launcherUpdates.check'); await launcherUpdate.refresh()
    if (stopped || requiredUpgrade.value !== selected) return
    if (['available', 'downloading', 'ready'].includes(flow.update.status)) { requiredUpgrade.value = null; show() }
    else requirementError.value = t('launcherUpdates.requiredUnavailable')
  } catch { if (!stopped && requiredUpgrade.value === selected) requirementError.value = t('launcherUpdates.checkNetworkFailed') }
  finally { if (!stopped) checkingRequirement.value = false }
}
const metrics = computed(() => flow.task ? transferMetrics(flow.task) : null)
const spinning = computed(() => flow.restarting || (flow.update.status !== 'ready' &&
  (active.value || flow.update.status === 'downloading')))
// Individual files can be verified before the remaining files finish downloading.
const ringProgress = computed(() => flow.restarting || metrics.value?.percent === 100 ||
  (flow.task && ['installing', 'ready', 'waiting', 'completed'].includes(flow.task.stage)) ? null : metrics.value?.percent ?? 0)
const status = computed(() => flow.restarting ? 'restartingNow' : flow.busy || !flow.task || ['checking', 'queued'].includes(flow.task.stage) ? 'preparing' :
  flow.task.stage === 'verifying' ? 'verifyingNow' : flow.task.stage === 'paused' ? 'pausedNow' : flow.task.stage === 'ready' ? 'preparing' : 'downloadingNow')
const progress = computed(() => flow.task?.stage === 'verifying' ? flow.task.stageProgress : metrics.value?.percent)
const eta = computed(() => {
  const seconds = metrics.value?.remainingSeconds
  if (seconds == null) return ''
  return seconds < 60 ? t('updates.seconds', { n: seconds }) : seconds < 3600 ? t('updates.minutes', { n: Math.ceil(seconds / 60) }) : t('updates.hours', { n: Math.ceil(seconds / 3600) })
})
const tooltip = computed(() => !spinning.value ? t(flow.update.status === 'ready' ? 'launcherUpdates.ready' : 'launcherUpdates.updateAvailable') :
  ringProgress.value != null ? `${t('launcherUpdates.downloadingNow')} · ${Math.floor(ringProgress.value)}%` :
  t(flow.restarting ? 'launcherUpdates.installingTitle' : flow.update.status === 'ready' ? 'launcherUpdates.ready' : 'launcherUpdates.verifyingNow'))
let stopped = false, timer: ReturnType<typeof setTimeout> | undefined
async function poll() {
  await launcherUpdate.refresh()
  if (!stopped) timer = setTimeout(poll, 500)
}
onMounted(async () => {
  if (inHost) {
    try { const info = await call('app.info'); if (!stopped) currentVersion.value = info.version }
    catch (error) { if (!stopped) state.error = (error as Error).message }
  }
  if (!stopped && (inHost || launcherUpdatePreview)) void poll()
})
onBeforeUnmount(() => { stopped = true; if (timer) clearTimeout(timer); removeRequirementListener(); launcherUpdate.stop() })
</script>

<template>
  <Transition name="update-entry" @after-enter="emit('layout')" @after-leave="emit('layout')">
    <div v-if="visible" class="launcher-update-entry" :class="{ compact }">
      <WTooltip :text="tooltip" placement="right">
        <button type="button" class="launcher-update-button" :aria-label="tooltip" aria-haspopup="dialog" @click="show">
          <Transition name="update-symbol" mode="out-in">
            <WProgressRing v-if="spinning" :key="ringProgress == null ? 'working' : 'progress'" :size="16" class="update-ring"
              :value="ringProgress ?? 0" :indeterminate="ringProgress == null" :aria-label="tooltip" />
            <FluentIcon v-else key="download" glyph="download" :size="15" />
          </Transition>
        </button>
      </WTooltip>
      <button v-if="!compact" type="button" class="update-entry-label" @click="show">{{ tooltip }}</button>
    </div>
  </Transition>

  <WDialog :open="requiredUpgrade === null && flow.dialog === 'download'" :title="t('launcherUpdates.confirmTitle')"
    :dismiss-on-backdrop="!mandatory" :close-disabled="mandatory"
    :primary-text="t('launcherUpdates.download')" :secondary-text="mandatory ? '' : t('dialog.cancel')"
    @primary="accept" @secondary="dismiss" @close="dismiss">
    <p class="update-dialog-body">{{ t(mandatory ? 'launcherUpdates.deprecatedBody' : 'launcherUpdates.downloadBody', { version: flow.update.version || '—', current: currentVersion || '—' }) }}</p>
    <WButton v-if="mandatory && flow.update.failure" @click="openManualUpdate">{{ t('launcherUpdates.openDownload') }}</WButton>
    <p v-if="mandatory && requirementError" class="update-inline-error" role="alert">{{ requirementError }}</p>
  </WDialog>

  <WDialog :open="requiredUpgrade === null && flow.dialog === 'confirm'" :title="t('launcherUpdates.readyTitle')"
    :dismiss-on-backdrop="!mandatory" :close-disabled="mandatory"
    :primary-text="t('launcherUpdates.updateNow')" :secondary-text="mandatory ? '' : t('launcherUpdates.later')"
    :primary-disabled="!canInstall"
    @primary="install" @secondary="dismiss" @close="dismiss">
    <p class="update-dialog-body">{{ t('launcherUpdates.confirmBody') }}</p>
  </WDialog>

  <WDialog :open="requiredUpgrade === null && flow.dialog === 'progress'" :title="t(flow.restarting ? 'launcherUpdates.installingTitle' : 'launcherUpdates.downloadingNow')"
    focus-panel
    :close-text="flow.restarting || mandatory ? '' : t(flow.error ? 'updates.close' : 'launcherUpdates.background')"
    :primary-text="mandatory && !flow.restarting && (flow.error || flow.task?.stage === 'failed' || flow.task?.stage === 'paused') ? t('updates.retry') : ''"
    :primary-disabled="flow.busy" @primary="canInstall ? install() : accept()"
    :dismiss-on-backdrop="!flow.restarting && !mandatory" :show-dismiss-button="!flow.restarting && !mandatory"
    :close-disabled="flow.restarting || mandatory" :dismiss-label="t('updates.close')" @close="dismiss">
    <div class="update-dialog-progress" aria-live="polite">
      <p class="update-dialog-body">{{ t(flow.restarting ? 'launcherUpdates.installingBody' : 'launcherUpdates.downloadProgressBody') }}</p>
      <div v-if="!flow.error" class="update-progress-row">
        <WProgressBar :value="progress ?? 0" :indeterminate="flow.busy || flow.restarting || !flow.task || flow.task.stage === 'checking' || progress == null" :aria-label="t('updates.total')" />
        <span class="progress-percent">{{ !flow.busy && !flow.restarting && progress != null ? `${Math.floor(progress)}%` : t('launcherUpdates.preparing') }}</span>
      </div>
      <div v-if="!flow.error" class="update-progress-status">
        <span>{{ t(`launcherUpdates.${status}`) }}</span>
        <span v-if="flow.task?.stage === 'downloading' && metrics?.speed">{{ formatBytes(metrics.speed, locale) }}/s<span v-if="eta"> · {{ eta }}</span></span>
      </div>
      <p v-if="flow.error" class="update-inline-error" role="alert">{{ t('launcherUpdates.restartFailed') }}</p>
      <p v-else-if="mandatory && flow.task?.stage === 'failed'" class="update-inline-error" role="alert">{{ t('launcherUpdates.requiredUnavailable') }}</p>
      <WButton v-if="mandatory && (flow.error || flow.task?.stage === 'failed')" @click="openManualUpdate">{{ t('launcherUpdates.openDownload') }}</WButton>
      <p v-if="mandatory && requirementError" class="update-inline-error" role="alert">{{ requirementError }}</p>
      <!-- <p class="update-background-note">{{ t('launcherUpdates.backgroundHint') }}</p> -->
    </div>
  </WDialog>

  <WDialog :open="requiredUpgrade !== null" :title="t('launcherUpdates.requiredTitle')"
    :primary-text="t('clientUpdates.check')" :secondary-text="t('dialog.cancel')" :primary-disabled="checkingRequirement"
    @primary="checkRequiredUpdate" @secondary="requiredUpgrade = null" @close="requiredUpgrade = null">
    <p class="update-dialog-body">{{ t('launcherUpdates.requiredBody', { minimum: requiredUpgrade?.minimumLauncherVersion, current: requiredUpgrade?.currentLauncherVersion }) }}</p>
    <p v-if="requirementError" class="update-inline-error" role="alert">{{ requirementError }}</p>
  </WDialog>

  <WDialog :open="requiredUpgrade === null && flow.dialog === 'complete'" :title="t('launcherUpdates.completeTitle')" :close-text="t('updates.close')" @close="dismiss">
    <p class="update-dialog-body">{{ t('launcherUpdates.completeBody') }}</p>
  </WDialog>
</template>

<style scoped>
.launcher-update-entry { display: flex; align-items: center; gap: 10px; height: 38px; padding-left: 4px; margin-bottom: 2px; }
.launcher-update-entry.compact { justify-content: center; padding-left: 0; }
.launcher-update-button { display: grid; place-items: center; width: 28px; height: 28px; border-radius: 50%; color: var(--accent-text-primary); background: color-mix(in srgb, var(--accent-default) 12%, var(--card-bg-default)); transition: background 167ms linear, color 167ms linear, transform 167ms var(--ease-fast-out-slow-in); }
.launcher-update-button:hover { color: var(--accent-default); background: color-mix(in srgb, var(--accent-default) 19%, var(--card-bg-default)); }
.launcher-update-button:focus-visible { outline: 2px solid var(--accent-default); outline-offset: 2px; }
.launcher-update-button:active { transform: scale(.94); }
.update-ring :deep(.w-ring-arc) { stroke: currentColor; stroke-width: 2; }
.update-ring :deep(.w-ring-track) { stroke: currentColor; stroke-width: 2; opacity: .25; }
.update-entry-label { min-width: 0; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; text-align: left; font-size: 13px; color: var(--text-primary); padding: 6px 0; }
.update-dialog-body { margin: 0; line-height: 1.7; color: var(--text-secondary); }
.update-dialog-progress { display: grid; gap: 18px; }
.update-progress-row { display: flex; gap: 16px; align-items: center; }
.update-progress-row .w-progress { flex: 1; height: 4px; }
.progress-percent { min-width: 44px; text-align: right; font-variant-numeric: tabular-nums; font-size: 13px; color: var(--text-secondary); }
.update-progress-status { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-top: -8px; font-size: 12px; color: var(--text-secondary); font-variant-numeric: tabular-nums; }
.update-background-note { margin: 0; font-size: 12px; line-height: 1.6; color: var(--text-tertiary); }
.update-inline-error { margin: 0; color: var(--fill-critical); font-size: 13px; line-height: 1.6; }
.update-entry-enter-active, .update-entry-leave-active { transition: height 300ms var(--ease-fast-out-slow-in), opacity 220ms linear, transform 300ms var(--ease-fast-out-slow-in), margin-bottom 300ms var(--ease-fast-out-slow-in); overflow: hidden; }
.update-entry-enter-from, .update-entry-leave-to { height: 0; opacity: 0; margin-bottom: 0; transform: translateY(8px); }
.update-symbol-enter-active, .update-symbol-leave-active { transition: opacity 120ms linear, transform 180ms var(--ease-fast-out-slow-in); }
.update-symbol-enter-from, .update-symbol-leave-to { opacity: 0; transform: scale(.8); }
@media (prefers-reduced-motion: reduce) { .update-entry-enter-active, .update-entry-leave-active, .update-symbol-enter-active, .update-symbol-leave-active { transition: opacity 180ms linear; } .launcher-update-button:active { transform: none; } }
</style>

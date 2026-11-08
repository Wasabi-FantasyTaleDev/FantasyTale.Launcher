<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, type LauncherUpdateAttempt, type LauncherUpdateState, type LauncherUpdatePreferences } from '../bridge'
import { launcherUpdate } from '../downloads/launcherUpdate'
import { state } from '../state'
import { updateAttemptDetails, updateFailureKey, updateFailureReason } from '../downloads/updateFailure'
import WSettingsCard from '../controls/WSettingsCard.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WButton from '../controls/WButton.vue'
import WDialog from '../controls/WDialog.vue'

const { t } = useI18n()
const update = ref<LauncherUpdateState>({ status: 'unconfigured', version: null, revision: null, manifestSha256: null, failure: null })
const preferences = ref<LauncherUpdatePreferences>({ autoCheck: true, autoDownload: true })
const busy = ref(false)
const sourceResults = ref<{ name: string; state: LauncherUpdateState }[]>([])
const lastAttempt = ref<LauncherUpdateAttempt | null>(null)
const detailsOpen = ref(false)
const checkFailure = computed(() => update.value.status === 'failed' &&
  (!update.value.version || !/^[a-f0-9]{64}$/i.test(update.value.manifestSha256 ?? '')))
const hasUsableSource = computed(() => sourceResults.value.some(source => ['current', 'available'].includes(source.state.status)))
const failedSources = computed(() => hasUsableSource.value ? [] : sourceResults.value.filter(source => source.state.status === 'failed'))
const attemptDetails = computed(() => updateAttemptDetails(lastAttempt.value, t))
const hasDiagnostics = computed(() => checkFailure.value || failedSources.value.length > 0 || !!update.value.failure || attemptDetails.value.length > 0)
const diagnosticText = computed(() => {
  const lines: string[] = []
  if (checkFailure.value || update.value.failure) lines.push(`${t('launcherUpdates.failureCode')}: ${updateFailureReason(update.value)}${update.value.httpStatus ? ` · HTTP ${update.value.httpStatus}` : ''}`)
  for (const source of failedSources.value) lines.push(`${source.name}: ${t(updateFailureKey(source.state))}${source.state.httpStatus ? ` · HTTP ${source.state.httpStatus}` : ''}`)
  lines.push(...attemptDetails.value)
  return lines.join('\n')
})
let stopped = false, timer: ReturnType<typeof setTimeout> | undefined
async function refresh() {
  try {
    const [result, results, attempt] = await Promise.all([call('launcherUpdates.state'), call('launcherUpdates.sourceResults'), call('launcherUpdates.lastAttempt')])
    if (!stopped) { if (!busy.value) { update.value = result; lastAttempt.value = attempt } sourceResults.value = results }
  }
  catch (error) { if (!stopped) state.error = (error as Error).message }
  if (!stopped) timer = setTimeout(refresh, 2000)
}
async function save(key: keyof LauncherUpdatePreferences, value: boolean) {
  busy.value = true
  try { preferences.value = await call('launcherUpdates.setPreferences', { ...preferences.value, [key]: value }) }
  catch (error) { state.error = (error as Error).message }
  finally { busy.value = false }
}
async function act() {
  if (busy.value) return
  busy.value = true
  try {
    if (['available', 'ready', 'downloading'].includes(update.value.status)) {
      await launcherUpdate.refresh(); launcherUpdate.show()
    }
    else {
      update.value = await call('launcherUpdates.check')
      await launcherUpdate.refresh()
      if (['available', 'ready', 'downloading'].includes(launcherUpdate.state.update.status)) launcherUpdate.show()
    }
  } catch (error) { state.error = (error as Error).message }
  finally { busy.value = false }
}
async function copyDiagnostics() {
  if (!diagnosticText.value) return
  await navigator.clipboard.writeText(diagnosticText.value)
}
async function openLog() { await call('launcherUpdates.openLog') }
onMounted(async () => {
  try { preferences.value = await call('launcherUpdates.preferences') }
  catch (error) { state.error = (error as Error).message }
  if (!stopped) void refresh()
})
onBeforeUnmount(() => { stopped = true; if (timer) clearTimeout(timer) })
</script>

<template>
  <h2 class="section-title">{{ t('launcherUpdates.title') }}</h2>
  <div class="launcher-update-settings">
    <WSettingsCard icon="refresh" :header="t('launcherUpdates.autoCheck')" :description="t('launcherUpdates.autoCheckDesc')">
      <WToggleSwitch :model-value="preferences.autoCheck" :disabled="busy" :aria-label="t('launcherUpdates.autoCheck')" @update:model-value="save('autoCheck', $event)" />
    </WSettingsCard>
    <WSettingsCard icon="download" :header="t('launcherUpdates.autoDownload')" :description="t('launcherUpdates.autoDownloadDesc')">
      <WToggleSwitch :model-value="preferences.autoDownload" :disabled="busy" :aria-label="t('launcherUpdates.autoDownload')" @update:model-value="save('autoDownload', $event)" />
    </WSettingsCard>
    <WSettingsCard icon="info" :header="t(checkFailure ? 'launcherUpdates.checkFailed' : `launcherUpdates.${update.status}`)" :description="checkFailure ? t(updateFailureKey(update)) : update.failure ? t(update.failure === 'startup' ? 'launcherUpdates.startup' : `clientUpdates.${update.failure}`) : update.version || ''">
      <WButton v-if="hasDiagnostics" variant="standard" :disabled="busy" @click="detailsOpen = true">{{ t('launcherUpdates.failureDetails') }}</WButton>
      <WButton :disabled="busy || update.status === 'checking'" :variant="update.status === 'available' ? 'accent' : 'standard'" @click="act">
        {{ t(busy || update.status === 'checking' ? 'clientUpdates.checking' : update.status === 'available' || update.status === 'ready' ? 'launcherUpdates.updateNow' : update.status === 'downloading' ? 'launcherUpdates.view' : 'clientUpdates.check') }}
      </WButton>
    </WSettingsCard>
  </div>
  <WDialog :open="detailsOpen" :title="t('launcherUpdates.failureDetails')" :close-text="t('titlebar.close')" @close="detailsOpen = false">
    <pre class="diagnostic-log">{{ diagnosticText }}</pre>
    <WButton variant="standard" :disabled="!diagnosticText" @click="copyDiagnostics">{{ t('launcherUpdates.copyDetails') }}</WButton>
    <WButton variant="standard" @click="openLog">{{ t('launcherUpdates.openLog') }}</WButton>
  </WDialog>
</template>
<style scoped>
.launcher-update-settings { display: flex; flex-direction: column; gap: 4px; margin-bottom: 24px; }
.diagnostic-log { white-space: pre-wrap; overflow-wrap: anywhere; color: var(--text-secondary); font: 12px/1.5 ui-monospace, monospace; }
</style>

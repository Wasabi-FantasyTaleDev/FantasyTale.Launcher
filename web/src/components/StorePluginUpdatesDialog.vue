<script setup lang="ts">
import { computed, ref, watch, onScopeDispose } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost } from '../bridge'
import { state } from '../state'
import { downloadTasks, trackDownload, downloadAction } from '../downloads/live'
import { pluginSourceChoices, pluginInstallationTargets, pluginSelectionStamp, type StorePluginUpdate } from '../store/updates'
import WDialog from '../controls/WDialog.vue'
import WCheckBox from '../controls/WCheckBox.vue'
import WComboBox from '../controls/WComboBox.vue'
import DownloadTaskProgress from './DownloadTaskProgress.vue'
import StorePluginInstallList from './StorePluginInstallList.vue'

const props = defineProps<{ open: boolean; updates: readonly StorePluginUpdate[] }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
interface UpdatePlan extends StorePluginUpdate { sourceId: string; taskId: string; error: string; targets: string[]; selections: Record<string, string>; serverId?: string }
let revision = 0
onScopeDispose(() => { revision++ })
function close() { revision++; busy.value = false; emit('close') }
const plans = ref<UpdatePlan[]>([]), activateAfterInstall = ref(true), busy = ref(false)
const installationItems = computed(() => plans.value.map(plan => {
  const offer = plan.offers.find(offer => offer.source.id === plan.sourceId)!
  const item = offer.releases.find(item => item.version === plan.item.version
    && (plan.sourceId !== plan.source.id || item.manifestSha256 === plan.item.manifestSha256))!
  return { item, source: offer.source, installedVersion: plan.installedVersion }
}))
const sourceOptions = computed(() => plans.value.map(plan => pluginSourceChoices(plan.offers, plan.item.version, true)
  .map(option => ({ value: option.value, disabled: option.disabled,
    label: `${option.name} · ${option.version}` }))))
const completed = computed(() => plans.value.length > 0 && plans.value.every(plan => downloadTasks.value.some(task => task.id === plan.taskId && task.stage === 'completed')))
const actionable = computed(() => plans.value.some(plan => !plan.taskId || downloadTasks.value.some(task => task.id === plan.taskId && ['failed', 'canceled', 'paused'].includes(task.stage))))
const queued = computed(() => plans.value.filter(plan => plan.taskId).length)
watch(() => props.open, open => {
  revision++; busy.value = false
  if (!open) return
  // A confirmation owns its exact offers even if a catalog refresh finishes behind it.
  plans.value = props.updates.map(update => ({ ...update, item: { ...update.item, builds: [...update.item.builds] }, source: { ...update.source }, offers: update.offers.map(offer => ({ source: { ...offer.source },
    releases: offer.releases.map(item => ({ ...item, builds: [...item.builds] })) })),
    sourceId: update.source.id, taskId: '', error: '', targets: pluginInstallationTargets(update.item, state.instances),
    selections: Object.fromEntries(state.instances.map(client => [client.id, pluginSelectionStamp(update.item, client)])),
    serverId: state.selectedGameId === update.item.game ? state.selectedServerId || undefined : undefined }))
  activateAfterInstall.value = true
}, { immediate: true })
function chooseSource(index: number, sourceId: string) {
  const plan = plans.value[index], offer = plan?.offers.find(offer => offer.source.id === sourceId)
  if (!plan || busy.value || plan.taskId || !offer || sourceOptions.value[index]?.find(option => option.value === sourceId)?.disabled) return
  plan.sourceId = sourceId; plan.error = ''
  const item = installationItems.value[index]!.item
  plan.targets = pluginInstallationTargets(item, state.instances)
  plan.selections = Object.fromEntries(state.instances.map(client => [client.id, pluginSelectionStamp(item, client)]))
}
async function install() {
  if (completed.value) { close(); return }
  if (busy.value || !(inHost || !!demoMode?.enabled) || !plans.value.length) return
  const current = revision, capturedPlans = [...plans.value], capturedItems = [...installationItems.value], activate = activateAfterInstall.value
  busy.value = true
  try {
    // Queue admission is sequential; the persistent download service owns transfer concurrency.
    for (let index = 0; index < capturedPlans.length; index++) {
      if (current !== revision) break
      const plan = capturedPlans[index]!
      if (plan.taskId) {
        const task = downloadTasks.value.find(task => task.id === plan.taskId)
        if (task && ['failed', 'canceled', 'paused'].includes(task.stage)) {
          try { await downloadAction(task, task.stage === 'paused' ? 'resume' : 'retry'); plan.error = '' }
          catch (error) { plan.error = (error as Error).message }
        }
        continue
      }
      plan.error = ''
      const { item, source } = capturedItems[index]!
      const expectedSource = { kind: source.kind, baseUrl: source.baseUrl, catalogPath: source.catalogPath }
      // Never re-enable a client that changed its selection after confirmation opened.
      const currentTargets = new Set(pluginInstallationTargets(item, state.instances))
      const instanceIds = activate ? plan.targets.filter(id => currentTargets.has(id)
        && state.instances.some(client => client.id === id && pluginSelectionStamp(item, client) === plan.selections[id])) : []
      try {
        const params = { sourceId: source.id, expectedSource, packageId: item.id, version: item.version, manifestSha256: item.manifestSha256 }
        const task = instanceIds.length
          ? await call('store.installPluginTargets', { ...params, instanceIds, serverId: plan.serverId })
          : await call('store.downloadPlugin', params)
        plan.taskId = task.id; trackDownload(task)
      } catch (error) { plan.error = (error as Error).message }
    }
  } finally { if (current === revision) busy.value = false }
}
</script>

<template>
  <WDialog :open="open" :title="t('store.updateAll')" :primary-text="t(completed ? 'updates.close' : busy ? 'install.starting' : queued ? 'updates.retry' : 'store.updateAll')" :close-text="completed ? undefined : t('updates.close')"
    :primary-disabled="busy || !(inHost || !!demoMode?.enabled) || !plans.length || !completed && !actionable" :primary-busy="busy" @close="close" @primary="install">
    <div class="form-stack">
      <StorePluginInstallList class="update-install-list" :items="installationItems">
        <template #default="{ index }">
          <label v-if="plans[index]!.offers.length > 1" class="source-field"><span>{{ t('store.downloadSource') }}</span>
            <WComboBox :model-value="plans[index]!.sourceId" :disabled="busy || !!plans[index]!.taskId"
              :options="sourceOptions[index]!"
              @update:model-value="chooseSource(index, $event)" />
          </label>
          <p v-if="plans[index]!.error" class="update-error" role="alert">{{ plans[index]!.error }}</p>
          <DownloadTaskProgress :task-id="plans[index]!.taskId" :pending="busy && !plans[index]!.error" />
        </template>
      </StorePluginInstallList>
      <WCheckBox v-model="activateAfterInstall" :disabled="busy || queued > 0">{{ t('store.activateAfterInstall') }}</WCheckBox>
    </div>
  </WDialog>
</template>

<style scoped>
.update-install-list { max-height: min(420px, 52vh); }
.source-field { display: flex; align-items: center; gap: 12px; font-size: 12px; }
.source-field > span { flex: none; white-space: nowrap; }
.source-field > .w-flyout-anchor { flex: 1; min-width: 0; }
.update-error, .update-queued, .update-progress { margin: 8px 0 0; font-size: 12px; line-height: 18px; overflow-wrap: anywhere; }
.update-error { color: var(--fill-critical); }.update-queued, .update-progress { color: var(--text-secondary); }
</style>

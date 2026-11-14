<script setup lang="ts">
import { computed, ref, watch, onScopeDispose } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost } from '../bridge'
import { pluginSourceChoices, pluginUpdateFrom, pluginInstallationTargets, type LocalPlugin } from '../store/updates'
import { state, prepareStorePluginInstallation } from '../state'
import type { StorePackage, StoreSource } from '../store/types'
import type { StorePackageOffer } from '../store/groups'
import { downloadTasks, trackDownload, downloadAction } from '../downloads/live'
import WDialog from '../controls/WDialog.vue'
import WCheckBox from '../controls/WCheckBox.vue'
import WComboBox from '../controls/WComboBox.vue'
import DownloadTaskProgress from './DownloadTaskProgress.vue'
import StorePluginInstallList from './StorePluginInstallList.vue'

const props = defineProps<{ open: boolean; item: StorePackage; source: StoreSource; offers?: StorePackageOffer[]; serverId?: string; localPlugins?: readonly LocalPlugin[] }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
let revision = 0
onScopeDispose(() => { revision++ })
function close() { revision++; busy.value = false; emit('close') }
const taskId = ref('')
const task = computed(() => downloadTasks.value.find(task => task.id === taskId.value))
const finished = computed(() => task.value?.stage === 'completed')
const retryable = computed(() => task.value && ['failed', 'canceled', 'paused'].includes(task.value.stage))
const locked = computed(() => busy.value || !!taskId.value)
const activateAfterInstall = ref(true), busy = ref(false), error = ref('')
const choices = ref<StorePackageOffer[]>([]), sourceId = ref(''), releaseHash = ref('')
const updating = ref(false)
const offer = computed(() => choices.value.find(value => value.source.id === sourceId.value))
const item = computed(() => offer.value?.releases.find(value => value.manifestSha256 === releaseHash.value))
const source = computed(() => offer.value?.source)
const updateFrom = computed(() => item.value && pluginUpdateFrom(item.value, props.localPlugins ?? state.plugins ?? []))
const primaryText = computed(() => t(finished.value ? 'updates.close' : retryable.value ? 'updates.retry' : busy.value ? 'install.starting' : updateFrom.value ? 'store.update' : activateAfterInstall.value ? 'install.install' : 'store.download'))
const installationItems = computed(() => item.value && source.value ? [{ item: item.value, source: source.value, installedVersion: updateFrom.value }] : [])
const sourceOptions = computed(() => {
  const options = pluginSourceChoices(choices.value, item.value?.version ?? '', updating.value)
  const differingVersions = new Set(options.map(value => value.version)).size > 1
  return options.map(value => ({ value: value.value, disabled: value.disabled,
    label: updating.value || differingVersions ? `${value.name} · ${value.version}` : value.name }))
})
const targets = computed(() => item.value ? pluginInstallationTargets(item.value, state.instances) : [])
watch(() => props.open, open => {
  revision++; busy.value = false
  if (!open) return
  // Keep the confirmed candidates stable while background catalogs continue refreshing.
  choices.value = (props.offers ?? [{ source: props.source, releases: [props.item] }])
    .filter(value => value.source.enabled).map(value => ({ source: { ...value.source },
      releases: value.releases.filter(release => release.kind === 'plugin' && release.game === props.item.game && release.id === props.item.id)
        .map(release => ({ ...release, builds: [...release.builds] })) })).filter(value => value.releases.length)
  sourceId.value = choices.value.find(value => value.source.id === props.source.id)?.source.id ?? choices.value[0]?.source.id ?? ''
  const first = offer.value?.releases
  releaseHash.value = first?.find(value => value.manifestSha256 === props.item.manifestSha256)?.manifestSha256 ?? first?.[0]?.manifestSha256 ?? ''
  updating.value = !!updateFrom.value
  activateAfterInstall.value = true; error.value = ''; taskId.value = ''
}, { immediate: true })
function chooseSource(id: string) {
  if (locked.value) return
  const next = choices.value.find(value => value.source.id === id)
  if (!next || sourceOptions.value.find(option => option.value === id)?.disabled) return
  const version = item.value?.version
  sourceId.value = id
  releaseHash.value = next.releases.find(value => value.version === version)?.manifestSha256 ?? next.releases[0]?.manifestSha256 ?? ''
  retainTargets()
}
function chooseRelease(hash: string) {
  if (locked.value || !offer.value?.releases.some(value => value.manifestSha256 === hash)) return
  releaseHash.value = hash; retainTargets()
}
function retainTargets() { error.value = '' }
async function install() {
  if (finished.value) { close(); return }
  const current = revision
  if (task.value) {
    if (retryable.value && !busy.value) {
      busy.value = true
      try { await downloadAction(task.value, task.value.stage === 'paused' ? 'resume' : 'retry') }
      catch (e) { if (current === revision) error.value = (e as Error).message }
      finally { if (current === revision) busy.value = false }
    }
    return
  }
  if (busy.value || !(inHost || !!demoMode?.enabled) || !item.value || !source.value || item.value.launcherCompatible === false) return
  const selectedItem = item.value, selectedSource = source.value, instanceIds = activateAfterInstall.value ? [...targets.value] : []
  const expectedSource = { kind: selectedSource.kind, baseUrl: selectedSource.baseUrl, catalogPath: selectedSource.catalogPath }
  const serverId = props.serverId ?? (state.selectedGameId === selectedItem.game ? state.selectedServerId || undefined : undefined)
  const remember = activateAfterInstall.value ? prepareStorePluginInstallation(selectedItem, serverId ?? '') : undefined
  busy.value = true; error.value = ''
  try {
    const task = remember
      ? await call('store.installPluginTargets', { sourceId: selectedSource.id, expectedSource, packageId: selectedItem.id, version: selectedItem.version,
        manifestSha256: selectedItem.manifestSha256, game: selectedItem.game, instanceIds, serverId })
      : await call('store.downloadPlugin', { sourceId: selectedSource.id, expectedSource, packageId: selectedItem.id, version: selectedItem.version, manifestSha256: selectedItem.manifestSha256 })
    remember?.(task.id)
    trackDownload(task); if (current === revision) taskId.value = task.id
  } catch (e) { if (current === revision) error.value = (e as Error).message }
  finally { if (current === revision) busy.value = false }
}
</script>
<template>
    <WDialog :open="open" :title="t(updateFrom ? 'store.updateTitle' : 'install.pluginTitle')" :primary-text="primaryText" :close-text="finished ? undefined : t('updates.close')"
    :primary-disabled="busy || !!taskId && !finished && !retryable || !(inHost || !!demoMode?.enabled) || !item || item.launcherCompatible === false" :primary-busy="busy" @close="close" @primary="install">
    <div v-if="item && source" class="form-stack">
      <StorePluginInstallList :items="installationItems"><DownloadTaskProgress :task-id="taskId" :pending="busy" /></StorePluginInstallList>
      <label v-if="choices.length > 1" class="field"><span>{{ t('store.downloadSource') }}</span><WComboBox :model-value="sourceId" :options="sourceOptions" :disabled="locked" @update:model-value="chooseSource" /></label>
      <label v-if="(offer?.releases.length ?? 0) > 1" class="field"><span>{{ t('store.version') }}</span><WComboBox :model-value="releaseHash" :options="offer!.releases.map(value => ({ value: value.manifestSha256, label: value.version }))" :disabled="locked" @update:model-value="chooseRelease" /></label>
      <p v-if="item.minimumLauncherVersion && item.launcherCompatible === false" class="install-error" role="alert">{{ t('launcherUpdates.pluginMinimum', { version: item.minimumLauncherVersion }) }}</p>
      <WCheckBox :model-value="activateAfterInstall" :disabled="locked" @update:model-value="activateAfterInstall = $event">{{ t('store.activateAfterInstall') }}</WCheckBox>
      <p v-if="error" class="install-error" role="alert">{{ error }}</p>
    </div>
  </WDialog>
</template>
<style scoped>
.activation-note, .install-error { color: var(--text-secondary); font-size: 12px; }
.activation-note, .install-error { margin: 0; overflow-wrap: anywhere; }
</style>

<script setup lang="ts">
import { computed, ref, watch, onScopeDispose } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { call, demoMode, inHost } from '../bridge'
import { localized } from '../i18n'
import { isRunning, refreshInstances, selectGame, state } from '../state'
import type { StorePackage, StoreSource } from '../store/types'
import { trackDownload } from '../downloads/live'
import type { DownloadTask } from '../downloads/model'
import DownloadTaskProgress from './DownloadTaskProgress.vue'
import InstallationTaskList from './InstallationTaskList.vue'
import WDialog from '../controls/WDialog.vue'
import WComboBox from '../controls/WComboBox.vue'
import WTextBox from '../controls/WTextBox.vue'
import WButton from '../controls/WButton.vue'
import WContentTransition from '../controls/WContentTransition.vue'
import ClientInstallComponents from './ClientInstallComponents.vue'
import { useClientInstallComponents } from '../plugins/clientInstallComponents'
import StorePluginInstallList from './StorePluginInstallList.vue'
import { previewInstallationBuild } from '../plugins/clientInstallPreview'

const props = defineProps<{ open: boolean; item: StorePackage; source: StoreSource }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n(), route = useRoute()
const componentPreview = computed(() => import.meta.env.DEV && !inHost && route.query.clientInstallPreview === '1')
let revision = 0
onScopeDispose(() => { revision++ })
function close() { revision++; busy.value = false; pending.value = undefined; emit('close') }
function cancelOverwrite() { revision++; busy.value = false; pending.value = undefined }
const target = ref(''), name = ref(''), path = ref(''), busy = ref(false), error = ref(''), pending = ref<{ id: string; path: string }>()
const tasks = ref<DownloadTask[]>([]), componentsAdmitted = ref(false), admissionDone = ref(false)
const clients = computed(() => state.instances.filter(item => item.game === props.item.game && item.build === props.item.builds[0]))
const options = computed(() => [...clients.value.map(item => ({ value: item.id, label: item.name, caption: item.installationPath || undefined, disabled: isRunning(item.id) })), { value: '', label: t('install.newClient') }])
const game = computed(() => state.games.find(item => item.id === props.item.game))
const build = computed(() => {
  const value = game.value?.builds.find(item => item.id === props.item.builds[0])
  return value && componentPreview.value ? previewInstallationBuild(value) : value
})
const selected = computed(() => clients.value.find(item => item.id === target.value))
const components = useClientInstallComponents(() => ({ open: props.open, game: props.item.game, build: build.value,
  nativeLanguage: game.value?.clientLanguages[0], serverId: state.selectedGameId === props.item.game ? state.selectedServerId : undefined, preview: componentPreview.value }))
watch(() => props.open, open => { revision++; busy.value = false; if (open) { target.value = clients.value.find(item => !isRunning(item.id))?.id ?? ''; name.value = ''; path.value = ''; error.value = ''; pending.value = undefined; tasks.value = []; componentsAdmitted.value = false; admissionDone.value = false } })
async function browse() {
  const current = revision
  try { const chosen = await call('shell.pickFolder', { title: t('live.source') }); if (current === revision) path.value = chosen ?? path.value }
  catch (e) { if (current === revision) error.value = (e as Error).message }
}
async function install(overwrite = false) {
  if (admissionDone.value) { close(); return }
  if (busy.value || !(inHost || !!demoMode?.enabled) || !build.value) return
  const current = revision, item = { ...props.item }, source = { ...props.source }, selectedBuild = build.value
  const selectedPath = path.value, selectedName = name.value, selectedPending = pending.value
  busy.value = true; error.value = ''
  try {
    const installComponents = components.confirm()
    let instanceId = target.value
    if (!instanceId) {
      const id = selectedPending?.id ?? `${selectedBuild.id}-${crypto.randomUUID().slice(0, 8)}`
      const destination = await call('instances.inspectInstallation', { id, installationPath: selectedPending?.path ?? (selectedPath.trim() || undefined) })
      if (current !== revision) return
      if (destination.containsFiles && !overwrite) { pending.value = { id, path: destination.path }; return }
      const created = await call('instances.create', { id, game: item.game, build: selectedBuild.id, name: selectedName.trim() || localized(selectedBuild.name), installationPath: destination.path, overwrite })
      if (current !== revision) return
      instanceId = created.id; target.value = created.id; pending.value = undefined
      await refreshInstances()
    }
    if (current !== revision) return
    if (!componentsAdmitted.value) {
      const admitted = await installComponents(instanceId)
      if (current !== revision) return
      tasks.value = admitted; componentsAdmitted.value = true
    }
    const task = await call('store.installClient', { sourceId: source.id, packageId: item.id, version: item.version, manifestSha256: item.manifestSha256, instanceId })
    trackDownload(task); if (current !== revision) return
    admissionDone.value = true; tasks.value = [...tasks.value, task]; selectGame(item.game); state.selectedInstanceId = instanceId

  } catch (e) { if (current === revision) { error.value = (e as Error).message; pending.value = undefined } }
  finally { if (current === revision) busy.value = false }
}
</script>
<template>
  <WDialog :open="open" :title="t('install.title')" :primary-text="t(admissionDone ? 'updates.close' : busy ? 'install.starting' : 'install.install')" :close-text="t('dialog.cancel')" :primary-disabled="busy || !admissionDone && (components.loading.value || !!components.error.value || !(inHost || !!demoMode?.enabled) || !build || !!pending || !!target && isRunning(target))" :primary-busy="busy" @close="close" @primary="install()">
    <div class="form-stack">
      <InstallationTaskList v-if="tasks.length" :tasks="tasks" />
      <template v-else>
      <StorePluginInstallList :items="[{ item, source }]"><DownloadTaskProgress v-if="busy" pending /></StorePluginInstallList>
      <label class="field"><span>{{ t('install.target') }}</span><WComboBox v-model="target" :options="options" :disabled="busy" /></label>
      <WContentTransition><div :key="target ? 'existing' : 'new'" class="form-stack">
        <template v-if="!target">
          <label class="field"><span>{{ t('live.name') }}</span><WTextBox v-model="name" :placeholder="localized(build?.name)" :maxlength="128" :disabled="busy" /></label>
          <label class="field"><span>{{ t('live.source') }}</span><div class="input-action"><WTextBox v-model="path" :placeholder="t('live.installPathDefault')" :disabled="busy" /><WButton :disabled="busy || !(inHost || !!demoMode?.enabled)" @click="browse">{{ t('live.browse') }}</WButton></div></label>
        </template>
        <template v-else><p class="muted">{{ t('install.replace') }}</p><p class="install-path">{{ selected?.installationPath }}</p></template>
      </div></WContentTransition>
      <ClientInstallComponents :items="components.items.value" :selected="components.selected.value" :languages="components.languages.value" :native-language="components.nativeLanguage.value" :additional-languages="components.additionalLanguages.value" :loading="components.loading.value" :error="components.error.value" :disabled="busy" @plugin="components.selectPlugin" @language="components.selectLanguage" @retry="components.refresh" />
      </template>
      <p v-if="error" class="install-error" role="alert">{{ error }}</p>
    </div>
    <WDialog :open="!!pending" :title="t('live.overwriteTitle')" :primary-text="t('live.overwrite')" :close-text="t('dialog.cancel')" :primary-disabled="busy" :primary-busy="busy" @close="cancelOverwrite" @primary="install(true)">
      <p>{{ t('live.overwriteBody') }}</p><p class="install-path">{{ pending?.path }}</p>
    </WDialog>
  </WDialog>
</template>
<style scoped>
.install-path { color: var(--text-secondary); font-size: 12px; }.install-path, .install-error { overflow-wrap: anywhere; margin: 0; }.install-error { color: var(--text-secondary); font-size: 12px; }
</style>

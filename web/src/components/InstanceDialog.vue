<script setup lang="ts">
import { computed, ref, watch, onScopeDispose } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, inHost, type Instance, type ServerClientRelease, type ServerPreset } from '../bridge'
import { localized, clientRegion } from '../i18n'
import { clientVersionCaption } from '../clientVersion'
import { perform, selectedGame, state } from '../state'
import { supportedClientLanguages } from '../clientLanguages'
import WDialog from '../controls/WDialog.vue'
import WTextBox from '../controls/WTextBox.vue'
import WComboBox from '../controls/WComboBox.vue'
import WButton from '../controls/WButton.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WCheckBox from '../controls/WCheckBox.vue'
import { trackDownload } from '../downloads/live'
import { useClientInstallComponents } from '../plugins/clientInstallComponents'
import { ServerRequestError } from '../serverFailures'
import ServerFailureNotice from './ServerFailureNotice.vue'
import type { DownloadTask } from '../downloads/model'
import DownloadTaskProgress from './DownloadTaskProgress.vue'
import InstallationTaskList from './InstallationTaskList.vue'
import ClientInstallComponents from './ClientInstallComponents.vue'

const props = defineProps<{ open: boolean; mode?: 'create' | 'edit' | 'clone'; instance?: Instance; preset?: ServerPreset; initialBuild?: string }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
let dialogRevision = 0
onScopeDispose(() => { dialogRevision++ })
function close() { dialogRevision++; identificationRevision++; discoveryRevision++; checking.value = false; emit('close') }
function cancelCreate() { dialogRevision++; pendingCreate.value = undefined; checking.value = false }
const tasks = ref<DownloadTask[]>([]), componentsAdmitted = ref(false), admissionDone = ref(false)
const name = ref(''), build = ref(''), installationPath = ref(''), language = ref(''), checking = ref(false)
const checkIntegrity = ref(true)
type CreateRequest = { id: string; name: string; game: string; build: string; installationPath?: string; language: string | null; checkIntegrity: boolean; serverId?: string; presetId?: string; download?: ServerClientRelease }
const pendingCreate = ref<CreateRequest>()
const createdRequest = ref<CreateRequest>()
const recommendation = ref<ServerClientRelease>()
const downloadClient = ref(false), discovering = ref(false), downloadError = ref<Error>(), detectedExisting = ref(false)
const downloadFailure = computed(() => new ServerRequestError(t('serverClient.unavailable'), {
  ...(downloadError.value instanceof ServerRequestError ? downloadError.value.details : {}),
  code: 'client_download_unavailable', reason: downloadError.value?.message,
}))
let discoveryRevision = 0
const createMode = computed(() => !props.mode || props.mode === 'create')
const preset = computed(() => props.preset?.build === build.value ? props.preset : state.catalog?.instances.find(p => p.build === build.value))
async function discover() {
  const revision = ++discoveryRevision
  recommendation.value = undefined; downloadClient.value = false; downloadError.value = undefined; discovering.value = false
  if (!props.open || !createMode.value || !inHost || (!createdRequest.value && !preset.value?.downloadSource)) return
  const serverId = createdRequest.value?.serverId ?? state.selectedServerId, presetId = createdRequest.value?.presetId ?? preset.value?.id
  if (!presetId) return
  discovering.value = true
  try {
    const release = await call('servers.clientRelease', { serverId, presetId })
    if (revision === discoveryRevision) { recommendation.value = release ?? undefined; downloadClient.value = !!release && !detectedExisting.value }
  } catch (error) { if (revision === discoveryRevision) downloadError.value = error as Error }
  finally { if (revision === discoveryRevision) discovering.value = false }
}
watch([() => props.open, build, () => state.selectedServerId, preset], discover, { flush: 'post' })
const chosenBuild = computed(() => selectedGame.value?.builds.find(b => b.id === build.value))
const components = useClientInstallComponents(() => ({ open: props.open && createMode.value, game: selectedGame.value?.id ?? '',
  build: chosenBuild.value, nativeLanguage: selectedGame.value?.clientLanguages[0], serverId: state.selectedServerId }))
const defaultName = computed(() => !props.mode || props.mode === 'create' ? localized(chosenBuild.value?.name) || build.value : '')
const resolvedName = computed(() => name.value.trim() || defaultName.value)
const languages = computed(() => supportedClientLanguages(chosenBuild.value, selectedGame.value?.clientLanguages, props.preset?.clientLanguages))
watch(languages, values => { if (!values.includes(language.value)) language.value = values[0] ?? '' })
const builds = computed(() => selectedGame.value?.builds.map(b => ({ value: b.id, label: localized(b.name, { dates: true }), group: clientRegion(b.branch), caption: clientVersionCaption(b) })) ?? [])
const title = computed(() => t(props.mode === 'edit' ? 'live.editTitle' : props.mode === 'clone' ? 'live.cloneTitle' : 'live.add'))
watch(() => props.open, open => {
  dialogRevision++; ++identificationRevision; checking.value = false
  tasks.value = []; componentsAdmitted.value = false; admissionDone.value = false; pendingCreate.value = undefined; createdRequest.value = undefined
  detectedExisting.value = false
  if (!open) return
  checkIntegrity.value = props.instance?.checkIntegrity ?? true
  // Leave new-client names empty so auto-detection can update the default name
  // with the selected build without replacing a user-entered custom name.
  name.value = props.instance ? props.instance.name + (props.mode === 'clone' ? ` — ${t('live.cloneSuffix')}` : '') : ''
  build.value = props.instance?.build ?? props.preset?.build ?? props.initialBuild ?? builds.value[0]?.value ?? ''
  installationPath.value = ''; language.value = props.instance?.language ?? (createMode.value ? chosenBuild.value?.nativeLanguage || languages.value[0] || '' : languages.value.includes(state.clientLanguage) ? state.clientLanguage : languages.value[0] ?? '')
})
async function browse() {
  const current = dialogRevision
  try {
    const path = await call('shell.pickFolder', { title: t('live.source') })
    if (current !== dialogRevision) return
    installationPath.value = path ?? installationPath.value; await identifyPath()
  } catch (error) { if (current === dialogRevision) state.error = (error as Error).message }
}
let identificationRevision = 0
watch(installationPath, () => {
  ++identificationRevision
  detectedExisting.value = false
  downloadClient.value = !!recommendation.value
}, { flush: 'sync' })
async function identifyPath() {
  if (!props.open || !createMode.value || !inHost || !installationPath.value.trim() || createdRequest.value) return
  const revision = ++identificationRevision, path = installationPath.value.trim(), gameId = selectedGame.value!.id
  detectedExisting.value = false
  checking.value = true
  try {
    const destination = await call('instances.inspectInstallation', { id: `inspect-${crypto.randomUUID().slice(0, 8)}`, installationPath: path, game: gameId })
    if (revision !== identificationRevision || !props.open || installationPath.value.trim() !== path || selectedGame.value?.id !== gameId) return
    checking.value = false
    if (destination.detectedBuildIds.length === 1 && destination.detectedBuildIds[0] !== build.value) build.value = destination.detectedBuildIds[0]!
    if (destination.detectedBuildIds.length) { installationPath.value = destination.path; detectedExisting.value = true; downloadClient.value = false }
    else downloadClient.value = !!recommendation.value
  } catch (error) { if (revision === identificationRevision && props.open) state.error = (error as Error).message }
  finally { if (revision === identificationRevision) checking.value = false }
}
async function createClient(request: CreateRequest, overwrite = false) {
  const current = dialogRevision
  const saved = await perform(async () => {
    const installComponents = components.confirm()
    const { download, ...creation } = request
    if (!createdRequest.value) {
      const created = await call('instances.create', { ...creation, overwrite, existingClient: !download })
      if (current !== dialogRevision) return
      request = { ...request, id: created.id }
      createdRequest.value = request
    }
    state.selectedInstanceId = request.id
    if (current !== dialogRevision) return
    if (!componentsAdmitted.value) {
      const admitted = await installComponents(request.id)
      if (current !== dialogRevision) return
      tasks.value = admitted; componentsAdmitted.value = true
    }
    if (download) {
      const task = await call('servers.installClient', { serverId: request.serverId, presetId: request.presetId,
        sourceId: download.source.id, manifestSha256: download.package.manifestSha256, instanceId: request.id })
      trackDownload(task); if (current !== dialogRevision) return
      tasks.value = [...tasks.value, task]
    }
    admissionDone.value = true
  })
  if (current !== dialogRevision) return
  pendingCreate.value = undefined
  if (saved && !tasks.value.length) {
    emit('close')
  }
  else if (createdRequest.value?.download) await discover()
}
async function save() {
  const current = dialogRevision
  if (admissionDone.value) { emit('close'); return }
  if (!props.mode || props.mode === 'create') {
    if (createdRequest.value) {
      if (createdRequest.value.download && !recommendation.value) return
      await createClient({ ...createdRequest.value, download: createdRequest.value.download ? recommendation.value : undefined }); return
    }
    const request: CreateRequest = { id: `${build.value}-${crypto.randomUUID().slice(0, 8)}`, name: resolvedName.value, game: selectedGame.value!.id, build: build.value,
      installationPath: installationPath.value.trim() || undefined, language: language.value || null, checkIntegrity: checkIntegrity.value, serverId: state.selectedServerId || undefined, presetId: preset.value?.id, download: downloadClient.value ? recommendation.value : undefined }
    checking.value = true
    try {
      const destination = await call('instances.inspectInstallation', { id: request.id, installationPath: request.installationPath, game: request.game })
      if (current !== dialogRevision) return
      if (!destination.detectedBuildIds.length) {
        if (!recommendation.value) throw new ServerRequestError(t('serverClient.unavailable'), {
          ...(downloadError.value instanceof ServerRequestError ? downloadError.value.details : {}),
          code: 'client_download_unavailable', reason: downloadError.value?.message ?? t('serverClient.noSource'),
        })
        request.download = recommendation.value
      }
      if (!request.download && destination.detectedBuildIds.length === 1 && destination.detectedBuildIds[0] !== request.build) {
        build.value = destination.detectedBuildIds[0]!; return
      }
      request.installationPath = destination.path
      if (destination.containsFiles) pendingCreate.value = request
      else await createClient(request)
    } catch (error) { if (current === dialogRevision) { state.requestFailure = error as Error; state.error = (error as Error).message } }
    finally { if (current === dialogRevision) checking.value = false }
    return
  }
  const saved = await perform(async () => {
    if (props.mode === 'edit' && props.instance) {
      await call('instances.update', { id: props.instance.id, name: name.value.trim(), endpoint: props.instance.endpoint, language: language.value || null, checkIntegrity: checkIntegrity.value })
    } else {
      const id = `${build.value}-${crypto.randomUUID().slice(0, 8)}`
      if (props.mode === 'clone' && props.instance) await call('instances.clone', { sourceId: props.instance.id, id, name: name.value.trim() })
      state.selectedInstanceId = id
    }
  })
  if (saved && current === dialogRevision) emit('close')
}
</script>

<template>
  <WDialog :open="open" :title="title" :primary-text="admissionDone ? t('updates.close') : state.busy || checking || discovering ? t('live.working') : createdRequest ? t('serverClient.retry') : createMode && (!detectedExisting || downloadClient) ? t('serverClient.install') : t('live.save')" :close-text="t('dialog.cancel')"
    :primary-disabled="state.busy || checking || !admissionDone && (discovering || createMode && (components.loading.value || !!components.error.value) || !!pendingCreate || (!!createdRequest?.download && !recommendation) || !inHost || !resolvedName || !build)" :primary-busy="state.busy || checking" @close="close" @primary="save">
    <div class="form-stack">
      <DownloadTaskProgress v-if="state.busy || checking" pending />
      <InstallationTaskList v-if="tasks.length" :tasks="tasks" />
      <template v-else>
      <label class="field"><span>{{ t('live.name') }}</span><WTextBox v-model="name" :placeholder="defaultName" :maxlength="128" :disabled="state.busy || !!createdRequest" /></label>
      <label class="field"><span>{{ t('live.build') }}</span><WComboBox v-model="build" :options="builds" :disabled="state.busy || !!createdRequest || mode === 'edit' || mode === 'clone'" /></label>
      <p v-if="!builds.length" class="muted">{{ t('live.clientUnavailable') }}</p>
      <template v-if="!mode || mode === 'create'">
        <label class="field"><span>{{ t('live.source') }}</span><div class="input-action"><WTextBox v-model="installationPath" :placeholder="t('live.installPathDefault')" :disabled="state.busy || checking || !!createdRequest" @change="identifyPath" /><WButton :disabled="state.busy || checking || !!createdRequest || !inHost" @click="browse">{{ t('live.browse') }}</WButton></div></label>
      </template>
      <div v-if="recommendation" class="server-download">
        <div class="server-download-heading"><span id="server-download-label">{{ t('serverClient.download') }}</span><WToggleSwitch v-model="downloadClient" aria-labelledby="server-download-label" :disabled="state.busy || checking || !!createdRequest" /></div>
        <p>{{ localized(recommendation.package.name) }} · {{ recommendation.source.name }}</p>
        <p class="server-download-address">{{ recommendation.source.baseUrl }}</p>
      </div>
      <div v-if="downloadError" class="server-download-error"><ServerFailureNotice :error="downloadFailure" /><WButton :disabled="state.busy || discovering" @click="discover">{{ t('serverClient.retry') }}</WButton></div>
      <p v-if="createdRequest" class="muted">{{ t('serverClient.created') }}</p>
      <div v-if="mode !== 'clone'" class="client-update-preference">
        <WCheckBox v-model="checkIntegrity" :disabled="state.busy || checking || !!createdRequest">{{ t('clientInstall.checkIntegrity') }}</WCheckBox>
        <p class="muted">{{ t('clientInstall.checkIntegrityHint') }}</p>
      </div>
      <ClientInstallComponents v-if="createMode" :items="components.items.value" :selected="components.selected.value" :languages="components.languages.value" :native-language="components.nativeLanguage.value" :additional-languages="components.additionalLanguages.value" :loading="components.loading.value" :error="components.error.value" :disabled="state.busy || checking" @plugin="components.selectPlugin" @language="components.selectLanguage" @retry="components.refresh" />
      <!-- <p v-if="!inHost" class="muted">{{ t('live.browserOnly') }}</p> -->
      </template>
    </div>
    <WDialog :open="!!pendingCreate" :title="t(pendingCreate?.download ? 'live.overwriteTitle' : 'live.add')" :primary-text="t(pendingCreate?.download ? 'live.overwrite' : 'live.save')" :close-text="t('dialog.cancel')" :primary-disabled="state.busy" :primary-busy="state.busy" @primary="pendingCreate && createClient(pendingCreate, true)" @close="cancelCreate">
      <p>{{ t(pendingCreate?.download ? 'live.overwriteBody' : 'clientLibrary.useExisting') }}</p><p class="installation-confirm-path">{{ pendingCreate?.installationPath }}</p>
    </WDialog>
  </WDialog>
</template>

<style scoped>
.server-download { padding: 12px; border: 1px solid var(--card-stroke-default); border-radius: 8px; }
.server-download-heading, .server-download-error { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.server-download p { margin: 6px 0 0; color: var(--text-secondary); font-size: 12px; }
.server-download-address { overflow-wrap: anywhere; }
.installation-confirm-path { overflow-wrap: anywhere; color: var(--text-secondary); user-select: text; font-size: 12px; }
.client-update-preference p { margin: 4px 0 0 30px; font-size: 12px; }
</style>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, inHost, hostingPreview, type HostingProfile, type HostingSession, type InstalledServerPackage } from '../bridge'
import { selectedGame, state, refreshServers, selectedInstance } from '../state'
import { localized, clientRegion } from '../i18n'
import { clientVersionCaption } from '../clientVersion'
import { trackDownload } from '../downloads/live'
import type { DownloadTask } from '../downloads/model'
import type { StorePackage, StoreSource } from '../store/types'
import WDialog from '../controls/WDialog.vue'
import WTextBox from '../controls/WTextBox.vue'
import WComboBox from '../controls/WComboBox.vue'
import WButton from '../controls/WButton.vue'
import WExpander from '../controls/WExpander.vue'
import DownloadTaskProgress from './DownloadTaskProgress.vue'
import InstallationTaskList from './InstallationTaskList.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WProgressRing from '../controls/WProgressRing.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import WAnimatedSize from '../controls/WAnimatedSize.vue'
import HostingLogDialog from './HostingLogDialog.vue'

const props = defineProps<{ open: boolean; profile?: HostingProfile; initialBuild?: string; defaultScope?: HostingProfile['mode']; selectAfterSave?: boolean }>()
const emit = defineEmits<{ close: []; saved: [HostingProfile]; connected: [] }>()
const { t } = useI18n()
const draft = ref<HostingProfile>(), profiles = ref<HostingProfile[]>([]), sessions = ref<HostingSession[]>([])
const installed = ref<InstalledServerPackage[]>([]), addresses = ref<string[]>([])
const loading = ref(false), busy = ref(false), error = ref(''), networkOpen = ref(false), profileId = ref('')
const releases = ref<{ source: StoreSource; item: StorePackage }[]>([]), releaseKey = ref(''), catalogLoaded = ref(false)
const activeTask = ref(''), taskSnapshot = ref<DownloadTask>()
const advancedOpen = ref(false), detailsOpen = ref(false), restartOpen = ref(false)
const nickname = ref(localStorage.getItem('localPlay.playerName') || '')
const nicknameValid = computed(() => /^[A-Za-z0-9_.@-]{1,32}$/.test(nickname.value.trim()))
let revision = 0, disposed = false, timer: ReturnType<typeof setTimeout> | undefined
const builds = computed(() => (selectedGame.value?.builds ?? []).filter(b => b.supportsHosting).map(b => ({ value: b.id, label: localized(b.name, { dates: true }), group: clientRegion(b.branch), caption: clientVersionCaption(b) })))
const currentSession = computed(() => sessions.value.find(s => s.profileId === draft.value?.id))
const running = computed(() => !!currentSession.value && ['starting', 'ready', 'stopping'].includes(currentSession.value.state))
const locked = computed(() => loading.value || busy.value)
const compatible = computed(() => installed.value.filter(p => p.package.game === draft.value?.game && p.package.builds.includes(draft.value?.build ?? '')))
const managedPackage = computed({ get: () => compatible.value.find(p => p.directory === draft.value?.packagePath)?.directory ?? '', set: (value: string) => { if (draft.value) draft.value.packagePath = value } })
const packageOptions = computed(() => [{ value: '', label: t('serverPackages.folder') }, ...compatible.value.map(p => ({ value: p.directory, label: `${localized(p.package.name)} · ${p.package.version}` }))])
const profileOptions = computed(() => [{ value: '', label: t('localPlay.newProfile') }, ...profiles.value.map(p => ({ value: p.id, label: p.name }))])
const scopeOptions = computed(() => [
  { value: 'private', label: t('localPlay.private') },
  { value: 'lan', label: t('localPlay.lan') },
  { value: 'room', label: t(selectedGame.value?.roomsEnabled === true ? 'localPlay.room' : 'localPlay.roomFuture'), disabled: selectedGame.value?.roomsEnabled !== true },
])
const storageChoice = computed({ get: () => draft.value?.storageMode ?? (draft.value?.temporaryData ? 'temporary' : 'local'), set: (value: string) => {
  if (draft.value && (value === 'local' || value === 'package' || value === 'temporary')) { draft.value.storageMode = value; draft.value.temporaryData = value === 'temporary' }
} })
const valid = computed(() => !!draft.value?.name.trim() && !!draft.value?.build && !!draft.value?.packagePath.trim() &&
  (draft.value.mode === 'private' || draft.value.mode === 'lan' && addresses.value.includes(draft.value.advertisedHost) || draft.value.mode === 'room' && selectedGame.value?.roomsEnabled === true && !!draft.value.advertisedHost.trim()) &&
  [draft.value.gamePort, draft.value.relayPort ?? draft.value.gamePort + 1, draft.value.apiPort, draft.value.adminPort].every(p => Number.isInteger(p) && p >= 1024 && p <= 65535) &&
  new Set([draft.value.gamePort, draft.value.relayPort ?? draft.value.gamePort + 1, draft.value.apiPort, draft.value.adminPort]).size === 4)
const title = computed(() => props.selectAfterSave ? t('localPlay.title') : t(props.profile ? 'hosting.edit' : props.defaultScope === 'room' ? 'multiplayer.create' : 'hosting.add'))
const primaryText = computed(() => t('hosting.save'))
const availableReleases = computed(() => releases.value.filter(r => r.item.game === draft.value?.game && r.item.builds.includes(draft.value?.build ?? '') && r.item.launcherCompatible !== false))
const releaseOptions = computed(() => availableReleases.value.map(r => ({ value: key(r), label: `${localized(r.item.name)} · ${r.item.version} · ${r.source.name}` })))
function key(release: { source: StoreSource; item: StorePackage }) { return JSON.stringify([release.source.id, release.item.id, release.item.version, release.item.manifestSha256]) }
const current = (expected: number) => !disposed && props.open && expected === revision && draft.value?.game === state.selectedGameId
function close() {
  ++revision; clearTimeout(timer)
  busy.value = false; loading.value = false; detailsOpen.value = false
  emit('close')
}
function chooseProfile(id: string) {
  const profile = profiles.value.find(p => p.id === id)
  const build = builds.value.some(b => b.value === props.initialBuild) ? props.initialBuild! : builds.value[0]?.value ?? ''
  const scope = props.defaultScope === 'room' && selectedGame.value?.roomsEnabled !== true ? 'lan' : props.defaultScope ?? 'private'
  draft.value = profile ? { ...profile } : {
    id: `host-${crypto.randomUUID().slice(0, 8)}`, game: state.selectedGameId, build,
    name: localized(selectedGame.value?.name), packagePath: '', mode: scope, requireAuthentication: false, clientInstanceId: selectedInstance.value?.build === build ? selectedInstance.value.id : state.instances.find(i => i.game === state.selectedGameId && i.build === build && i.clientInstalled)?.id,
    advertisedHost: scope === 'private' ? '127.0.0.1' : scope === 'lan' ? addresses.value[0] ?? '' : '',
    gamePort: 17230, apiPort: 18090, adminPort: 18094, temporaryData: false, storageMode: 'local',
  }
  profileId.value = id; error.value = ''
  if (!profile) draft.value.packagePath = compatible.value[0]?.directory ?? ''
}
async function initialize() {
  const expected = ++revision
  clearTimeout(timer); detailsOpen.value = false; loading.value = true; busy.value = false; error.value = ''; draft.value = undefined; networkOpen.value = false; advancedOpen.value = false
  releases.value = []; catalogLoaded.value = false; activeTask.value = ''; taskSnapshot.value = undefined
  try {
    const [overview, packages, lan, ports] = await Promise.all([call('hosting.list'), call('hosting.packages'), call('hosting.localAddresses'), call('hosting.availablePorts')])
    if (disposed || !props.open || expected !== revision) return
    profiles.value = overview.profiles.filter(p => p.game === state.selectedGameId)
    sessions.value = overview.sessions; installed.value = packages; addresses.value = lan
    const existing = props.profile ?? (props.selectAfterSave ? profiles.value.find(p => p.build === props.initialBuild && p.mode === 'private') : undefined)
    chooseProfile(existing?.id ?? '')
    const chosen = draft.value as HostingProfile | undefined
    if (!existing && chosen) [chosen.gamePort, chosen.relayPort, chosen.apiPort, chosen.adminPort] = ports as [number, number, number, number]
    if (current(expected)) timer = setTimeout(() => void poll(expected), 800)
  } catch (e) { if (expected === revision) error.value = (e as Error).message }
  finally { if (expected === revision) loading.value = false }
  if (!current(expected) || error.value) return
  if (props.selectAfterSave && !(draft.value as HostingProfile | undefined)?.packagePath) await findPackages()
}
async function poll(expected: number) {
  if (!current(expected)) return
  try {
    const overview = await call('hosting.list')
    if (!current(expected)) return
    sessions.value = overview.sessions
    if (activeTask.value) {
      const tasks = await call('downloads.list')
      if (!current(expected)) return
      const task = tasks.find(task => task.id === activeTask.value)
      if (task) taskSnapshot.value = task
      if (!task) error.value = t('localPlay.downloadFailed')
      else if (task.stage === 'completed') {
        activeTask.value = ''
        const packages = await call('hosting.packages')
        if (!current(expected)) return
        installed.value = packages
        if (draft.value && !draft.value.packagePath) draft.value.packagePath = compatible.value[0]?.directory ?? ''

      }
    }
  } catch (e) { if (current(expected)) error.value = (e as Error).message }
  finally { if (current(expected)) timer = setTimeout(() => void poll(expected), 800) }
}
async function browse() {
  const expected = revision; busy.value = true
  try { const path = await call('shell.pickFolder'); if (current(expected) && path && draft.value) draft.value.packagePath = path }
  catch (e) { if (current(expected)) error.value = (e as Error).message }
  finally { if (current(expected)) busy.value = false }
}
async function findPackages() {
  const expected = revision; busy.value = true; error.value = ''
  try {
    const sources = (await call('store.sources')).filter(s => s.enabled)
    const results = await Promise.allSettled(sources.map(async source => ({ source, catalog: await call('store.catalog', { id: source.id }) })))
    if (!current(expected)) return
    releases.value = results.flatMap(result => result.status === 'fulfilled' ? result.value.catalog.packages.filter(p => p.kind === 'server').map(item => ({ source: result.value.source, item })) : [])
    releaseKey.value = availableReleases.value[0] ? key(availableReleases.value[0]) : ''
    catalogLoaded.value = true
    const failure = results.find(result => result.status === 'rejected')
    if (failure?.status === 'rejected') error.value = String(failure.reason?.message ?? failure.reason)
  } catch (e) { if (current(expected)) error.value = (e as Error).message }
  finally { if (current(expected)) busy.value = false }
}
async function install() {
  const release = availableReleases.value.find(r => key(r) === releaseKey.value)
  if (!release || busy.value || activeTask.value) return
  const expected = revision; busy.value = true; error.value = ''
  try {
    const task = await call('store.installServer', { sourceId: release.source.id, packageId: release.item.id, version: release.item.version, manifestSha256: release.item.manifestSha256 })
    trackDownload(task)
    if (current(expected)) { activeTask.value = task.id; taskSnapshot.value = task }
  } catch (e) { if (current(expected)) error.value = (e as Error).message }
  finally { if (current(expected)) busy.value = false }
}
async function stop() {
  const session = currentSession.value; if (!session || busy.value) return
  const expected = revision; busy.value = true; error.value = ''
  try { const result = await call('hosting.stop', { id: session.profileId, runId: session.runId }); if (current(expected)) sessions.value = sessions.value.map(s => s.runId === result.runId ? result : s) }
  catch (e) { if (current(expected)) error.value = (e as Error).message }
  finally { if (current(expected)) busy.value = false }
}
async function save() {
  if (!draft.value || !valid.value || busy.value || loading.value || !(inHost || hostingPreview)) return
  const expected = revision, request = { ...draft.value, name: draft.value.name.trim(), packagePath: draft.value.packagePath.trim(), advertisedHost: draft.value.mode === 'private' ? '127.0.0.1' : draft.value.advertisedHost.trim(), temporaryData: storageChoice.value === 'temporary' }
  busy.value = true; error.value = ''
  try {
    const wasRunning = running.value
    const previous = profiles.value.find(p => p.id === request.id)
    const changed = !previous || JSON.stringify(previous) !== JSON.stringify(request)
    const saved = await call('hosting.save', request)
    if (!current(expected)) return
    draft.value = saved
    localStorage.setItem('localPlay.playerName', nickname.value.trim())
    if (props.selectAfterSave) {
      await refreshServers({ refreshSelectedServices: false })
      if (!current(expected)) return
      state.selectedServerId = `local-${saved.id}`; state.selectedBuildId = saved.build
      state.selectedInstanceId = saved.clientInstanceId ?? state.instances.find(i => i.game === saved.game && i.build === saved.build && i.clientInstalled)?.id ?? ''
    }
    emit('saved', saved)
    if (wasRunning && changed) restartOpen.value = true
    else emit('close')
  } catch (e) { if (current(expected)) error.value = (e as Error).message === 'local_client_data_missing' ? t('localPlay.clientDataMissing') : (e as Error).message }
  finally { if (current(expected)) busy.value = false }
}
async function restart() {
  const session = currentSession.value, id = draft.value?.id
  if (!session || !id || busy.value) return
  busy.value = true; error.value = ''
  try {
    await call('hosting.stop', { id, runId: session.runId, allowConnected: true })
    await call('hosting.start', { id })
    restartOpen.value = false; emit('close')
  } catch (e) { error.value = (e as Error).message }
  finally { busy.value = false }
}
watch(() => draft.value?.mode, (mode, previous) => {
  if (!draft.value || mode === previous) return
  if (mode === 'private') draft.value.advertisedHost = '127.0.0.1'
  else if (mode === 'lan' && !addresses.value.includes(draft.value.advertisedHost)) draft.value.advertisedHost = addresses.value[0] ?? ''
  else if (mode === 'room' && draft.value.advertisedHost === '127.0.0.1') draft.value.advertisedHost = ''
})
watch(() => draft.value?.build, build => {
  if (draft.value && !state.instances.some(i => i.id === draft.value?.clientInstanceId && i.build === build))
    draft.value.clientInstanceId = state.instances.find(i => i.game === draft.value?.game && i.build === build && i.clientInstalled)?.id
  const existing = installed.value.find(p => p.directory === draft.value?.packagePath)
  if (draft.value && existing && !existing.package.builds.includes(build ?? '')) draft.value.packagePath = compatible.value[0]?.directory ?? ''
  releaseKey.value = availableReleases.value[0] ? key(availableReleases.value[0]) : ''
})
watch(() => [props.open, state.selectedGameId] as const, ([openValue], previous) => {
  ++revision; clearTimeout(timer)
  if (openValue && previous && state.selectedGameId !== previous[1]) { emit('close'); return }
  if (openValue) void initialize()
}, { immediate: true, flush: 'sync' })
onBeforeUnmount(() => { disposed = true; ++revision; clearTimeout(timer) })
</script>

<template>
  <WDialog dismiss-on-backdrop :open="open" :title="title" wide :primary-text="primaryText" :close-text="t('dialog.cancel')" :primary-disabled="busy || loading || !valid || !(inHost || hostingPreview) || !!activeTask || (draft?.requireAuthentication === false && !nicknameValid)" @primary="save" @close="close">
    <WAnimatedSize class="setup-size"><div class="setup-form">
      <p v-if="loading" role="status">{{ t('hosting.starting') }}</p>
      <template v-if="draft">
        <label v-if="selectAfterSave && profiles.length"><span>{{ t('localPlay.profile') }}</span><WComboBox :model-value="profileId" :options="profileOptions" :disabled="busy" @update:model-value="chooseProfile" /></label>

        <div v-if="selectAfterSave" class="quick-start">
          <div class="quick-heading"><WProgressRing v-if="busy" :size="20" /><span>{{ localized(selectedGame?.builds.find(build => build.id === draft?.build)?.name, { dates: true }) }}</span></div>

          <div v-if="!draft.packagePath" class="package-download">
            <template v-if="releaseOptions.length"><WComboBox v-model="releaseKey" :options="releaseOptions" :disabled="busy || !!activeTask" /><WButton :disabled="busy || !!activeTask || !releaseKey" @click="install">{{ t('install.install') }}</WButton></template>
            <WButton v-else :disabled="busy" @click="findPackages">{{ t('localPlay.findPackage') }}</WButton>
            <p v-if="catalogLoaded && !releaseOptions.length" class="hint">{{ t('localPlay.noPackage') }}</p>
          </div>
        </div>
          <div class="setup-section">
            <label><span>{{ t('hosting.name') }}</span><WTextBox v-model="draft.name" :maxlength="128" :disabled="locked" /></label>
            <label><span>{{ t('hosting.version') }}</span><WComboBox v-model="draft.build" :options="builds" :disabled="locked" /></label>
            <div class="scope-section">
              <label><span>{{ t('localPlay.scope') }}</span><WComboBox v-model="draft.mode" :options="scopeOptions" :disabled="locked" /></label>
              <p class="hint">{{ t(`localPlay.${draft.mode}Hint`) }}</p>
              <label v-if="draft.mode === 'lan'"><span>{{ t('localPlay.interface') }}</span><WComboBox v-model="draft.advertisedHost" :options="addresses.map(value => ({ value, label: value }))" :disabled="locked" /><small v-if="!addresses.length">{{ t('localPlay.noInterface') }}</small></label>
              <label v-if="draft.mode === 'room'"><span>{{ t('hosting.address') }}</span><WTextBox v-model="draft.advertisedHost" :disabled="locked" /></label>
            </div>
          </div>
        <label class="authentication-choice"><span>{{ t('localPlay.requireAuthentication') }}</span><WToggleSwitch v-model="draft.requireAuthentication" :disabled="locked" /></label>
        <Transition name="nickname"><label v-if="!draft.requireAuthentication"><span>{{ t('localPlay.playerName') }}</span><WTextBox v-model="nickname" :maxlength="32" :disabled="locked" /><small v-if="nickname && !nicknameValid">{{ t('localPlay.playerNameInvalid') }}</small></label></Transition>
        <WExpander v-model:open="advancedOpen" :header="t('hosting.edit')">
          <div class="setup-section advanced-settings">
            <div class="package-section">
              <label><span>{{ t('serverPackages.installed') }}</span><WComboBox v-model="managedPackage" :options="packageOptions" :disabled="locked" /></label>
              <label><span>{{ t('hosting.package') }}</span><span class="folder-field"><WTextBox v-model="draft.packagePath" :disabled="locked || !!managedPackage" /><WButton :disabled="locked || !(inHost || hostingPreview)" @click="browse">{{ t('hosting.browse') }}</WButton></span></label>
              <div v-if="!compatible.length && !draft.packagePath" class="package-download">
                <p class="hint">{{ t('localPlay.missingPackage') }}</p><WButton v-if="!catalogLoaded" :disabled="busy || !(inHost || hostingPreview)" @click="findPackages">{{ t('localPlay.findPackage') }}</WButton>
                <template v-else-if="releaseOptions.length"><WComboBox v-model="releaseKey" :options="releaseOptions" :disabled="busy || !!activeTask" /><WButton :disabled="busy || !!activeTask || !releaseKey" @click="install">{{ t('install.install') }}</WButton></template>
                <p v-else class="hint">{{ t('localPlay.noPackage') }}</p>
              </div>
            </div>
            <div class="storage-section">
              <label><span>{{ t('hosting.storage') }}</span><WComboBox v-model="storageChoice" :options="(['local', 'package', 'temporary'] as const).map(value => ({ value, label: t(`hosting.storage${value}`) }))" :disabled="locked" /></label>
              <p class="hint">{{ t(`hosting.storage${storageChoice}Hint`) }}</p>
            </div>
            <WExpander v-model:open="networkOpen" class="network-settings" :header="t('hosting.network')"><div class="port-fields"><label v-for="field in (['gamePort', 'relayPort', 'apiPort', 'adminPort'] as const)" :key="field"><span>{{ t(`hosting.${field}`) }}</span><WTextBox :model-value="String(draft[field] ?? draft.gamePort + 1)" type="number" min="1024" max="65535" :disabled="locked" @update:model-value="draft[field] = Number($event)" /></label></div></WExpander>
          </div>
        </WExpander>
      </template>
      <DownloadTaskProgress v-if="busy && !taskSnapshot" pending />
      <InstallationTaskList v-if="taskSnapshot" :tasks="[taskSnapshot]" />
      <WInfoBar v-if="error" severity="error" @close="error = ''">{{ error }}<template #action><WButton @click="detailsOpen = true">{{ t('serverFailure.details') }}</WButton></template></WInfoBar>
    </div></WAnimatedSize>
  </WDialog>
  <WDialog :open="restartOpen" :title="t('hosting.edit')" :primary-text="t('localHosting.restartNow')" :close-text="t('localHosting.restartLater')" :primary-disabled="busy" :close-disabled="busy" @primary="restart" @close="restartOpen = false; emit('close')"><p>{{ t('localHosting.restartPrompt') }}</p><WInfoBar v-if="error" severity="error" :closable="false">{{ error }}</WInfoBar></WDialog>
  <HostingLogDialog :open="detailsOpen" :session="currentSession" :error="error" @close="detailsOpen = false" />
</template>

<style scoped>
.setup-size { overflow: clip; }
.nickname-enter-active { transition: opacity 180ms ease; }
.nickname-leave-active { position: absolute; left: 0; right: 0; transition: opacity 120ms ease; }
.nickname-enter-from, .nickname-leave-to { opacity: 0; }
@media (prefers-reduced-motion: reduce) { .nickname-enter-active, .nickname-leave-active { transition: none; } }
.quick-start { display: grid; gap: 14px; padding: 16px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-control); }
.advanced-settings { padding: 12px; }
.quick-heading { display: flex; align-items: center; gap: 12px; font-size: 15px; }
.setup-form .authentication-choice { display: flex; align-items: center; justify-content: space-between; }
.setup-form, .setup-section, .setup-form label, .package-section, .storage-section, .scope-section { display: grid; gap: 16px; }
.setup-form label { gap: 7px; font-size: 13px; }
.setup-section { align-content: start; }
.scope-section { padding-top: 16px; border-top: 1px solid var(--stroke-divider); gap: 10px; }
.storage-section { gap: 7px; }
.folder-field { display: flex; gap: 8px; }.folder-field .w-textbox { flex: 1; min-width: 0; }
.hint, small { margin: 0; color: var(--text-secondary); font-size: 12px; line-height: 1.6; }
.port-fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; padding-block: 12px; }
.package-download { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; padding: 12px; border: 1px solid var(--card-stroke-default); border-radius: 6px; background: var(--card-bg-secondary); }
.package-download > .hint { flex: 1; min-width: 220px; }.package-download > .w-combo { flex: 1; }
.network-settings :deep(.expander-heading) { min-height: 42px; padding: 4px 8px 4px 14px; font-size: 13px; font-weight: 400; }
@media (max-width: 500px) { .port-fields { grid-template-columns: 1fr; } }
</style>

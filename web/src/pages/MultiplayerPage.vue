<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { mergeServerPresentation } from '../serverDescription'
import { useRoute, useRouter } from 'vue-router'
import { call, demoMode, inHost, on, type DirectoryEntry, type DirectorySource, type Server, type ServerPreset, type ServerCatalog, type ServerProfileImport } from '../bridge'
import { policyPlugins, policySources } from '../clientPolicy'
import type { StoreSource } from '../store/types'
import { localized } from '../i18n'
import { gameServers, selectedGame, state, refreshServers } from '../state'
import PageBody from '../components/PageBody.vue'
import { multiplayerPreview } from '../multiplayerPreview'
import DirectorySourceDialog from '../components/DirectorySourceDialog.vue'
import StoreSourcesDialog from '../components/StoreSourcesDialog.vue'
import ServerDialog from '../components/ServerDialog.vue'
import InstanceDialog from '../components/InstanceDialog.vue'
import ServerDetailsDialog from '../components/ServerDetailsDialog.vue'
import ServerCard from '../components/ServerCard.vue'
import LocalServerSetupDialog from '../components/LocalServerSetupDialog.vue'
import WButton from '../controls/WButton.vue'
import WComboBox from '../controls/WComboBox.vue'
import WTextBox from '../controls/WTextBox.vue'
import WDialog from '../controls/WDialog.vue'
import WSelectorBar from '../controls/WSelectorBar.vue'
import WAnimatedSize from '../controls/WAnimatedSize.vue'
import FluentIcon from '../controls/FluentIcon.vue'
const { t } = useI18n(), router = useRouter(), route = useRoute()
const sources = ref<DirectorySource[]>([]), sourceId = ref(''), entries = ref<DirectoryEntry[]>([])
const storeSources = ref<StoreSource[]>([])
const roomsEnabled = computed(() => multiplayerPreview || selectedGame.value?.roomsEnabled !== false)
const hostingEnabled = computed(() => selectedGame.value?.localHostingEnabled !== false)
function readLastTab(): 'room' | 'server' {
  try {
    const value = typeof localStorage === 'undefined' ? null : localStorage.getItem('multiplayerTab')
    if (value === 'room' || value === 'server') return value
  } catch { /* Browsing remains available when preference storage is unavailable. */ }
  return roomsEnabled.value ? 'room' : 'server'
}
const lastTab = ref(readLastTab())
const kind = computed<'room' | 'server'>({ get: () => route.query.tab === 'server' || route.query.tab === 'room' ? route.query.tab : lastTab.value, set: tab => { void router.replace({ query: { ...route.query, tab } }) } })
watch(kind, value => {
  lastTab.value = value
  try { localStorage.setItem('multiplayerTab', value) } catch { /* Preference persistence is optional. */ }
}, { immediate: true })
const directoryTabs = computed(() => [
  { key: 'room', label: t('multiplayer.rooms') },
  { key: 'server', label: t('multiplayer.servers') },
])
function selectKind(value: string) { if (value === 'room' || value === 'server') kind.value = value }
const query = ref(''), selectedId = ref(''), buildId = ref('')
const busy = ref(false), loading = ref(false), error = ref(''), sourceDialog = ref(false), submitDialog = ref(false), submitted = ref(false)
const hostingDialog = ref(false), detailsOpen = ref(false)
const favorites = ref<string[]>(readFavorites())
function readFavorites(): string[] {
  try { const value: unknown = typeof localStorage === 'undefined' ? [] : JSON.parse(localStorage.getItem('serverFavorites') ?? '[]'); return Array.isArray(value) ? value.filter((key): key is string => typeof key === 'string') : [] } catch { return [] }
}
function favoriteKey(game: string, address: string) { return `${game}:${address.replace(/\/$/, '').toLocaleLowerCase()}` }
function toggleFavorite(key: string) {
  favorites.value = favorites.value.includes(key) ? favorites.value.filter(value => value !== key) : [...favorites.value, key]
  localStorage.setItem('serverFavorites', JSON.stringify(favorites.value))
}
const profileConflict = ref<{ text: string; inspection: ServerProfileImport; build: string; game: string; revision: number }>()
const savedCatalogs = ref<Record<string, ServerCatalog>>({})
const savedCatalogPending = ref<Record<string, boolean>>({})
let catalogRevision = 0
async function loadSavedCatalogs() {
  const revision = ++catalogRevision, game = state.selectedGameId
  const queue = gameServers.value.filter(s => !s.transient && (s.apiUrl || multiplayerPreview))
  savedCatalogPending.value = Object.fromEntries(queue.map(server => [server.id, true]))
  const next: Record<string, ServerCatalog> = {}
  await Promise.all(Array.from({ length: Math.min(4, queue.length) }, async () => {
    while (queue.length && !disposed && revision === catalogRevision) {
      const server = queue.shift()!
      try { next[server.id] = await call('servers.catalog', { id: server.id }) } catch { /* Detailed failures are shown when opening this server. */ }
    }
  }))
  if (!disposed && revision === catalogRevision && state.selectedGameId === game) { savedCatalogs.value = next; savedCatalogPending.value = {} }
}
function serverPresentation(server: Server) {
  return mergeServerPresentation(server.presentation, savedCatalogs.value[server.id]?.presentation)
}
function openDetails(id: string) { selectedId.value = id; detailsOpen.value = true }
const serverDialog = ref(false), editing = ref<Server>(), deleting = ref<Server>(), preset = ref<ServerPreset>()
const draft = ref({ name: '', apiUrl: '', description: '', builds: [] as string[] })
const source = computed(() => sources.value.find(s => s.id === sourceId.value))
const sourceOptions = computed(() => sources.value.filter(s => s.enabled).map(s => ({ value: s.id, label: s.storeSourceId === '*' ? t('store.allSources') : s.name })))
const visible = computed(() => entries.value.filter(e => e.kind === kind.value && e.details.game === state.selectedGameId && e.details.name.toLocaleLowerCase().includes(query.value.toLocaleLowerCase())))
const savedServers = computed(() => kind.value === 'server' ? gameServers.value.filter(s => !s.transient && s.name.toLocaleLowerCase().includes(query.value.toLocaleLowerCase())) : [])
const cards = computed(() => {
  const activeServer = state.servers.find(server => server.id === state.selectedServerId && server.game === state.selectedGameId)
  const saved = savedServers.value.map(server => ({
    key: `saved:${server.id}`, server, entry: undefined as DirectoryEntry | undefined,
    name: server.name, game: server.game, builds: [...new Set(savedCatalogs.value[server.id]?.instances.map(item => item.build) ?? server.builds ?? [])],
    buildsLoading: savedCatalogPending.value[server.id] === true,
    address: server.apiUrl, presentation: serverPresentation(server), recommendations: policyPlugins(savedCatalogs.value[server.id]?.clientPolicy ?? server.clientPolicy),
    sources: policySources(savedCatalogs.value[server.id]?.clientPolicy ?? server.clientPolicy), serverId: server.id, sourceId: undefined as string | undefined,
    entryId: undefined as string | undefined, description: undefined as string | undefined, room: false,
    favorite: favorites.value.includes(favoriteKey(server.game, server.apiUrl || server.id)), selected: state.selectedServerId === server.id,
  }))
  const discovered = visible.value.filter(entry => entry.kind === 'room' || !savedServers.value.some(server =>
    server.apiUrl && favoriteKey(server.game, server.apiUrl) === favoriteKey(entry.details.game, entry.details.apiUrl)
    || server.endpoint && entry.details.endpoint && server.endpoint.port === entry.details.endpoint.port && server.endpoint.host.toLowerCase() === entry.details.endpoint.host.toLowerCase())).map(entry => ({
    key: `directory:${entry.id}`, server: undefined as Server | undefined, entry,
    name: entry.details.name, game: entry.details.game, builds: entry.details.builds,
    buildsLoading: false,
    address: entry.details.apiUrl, presentation: entry.details.presentation, recommendations: policyPlugins(entry.details.clientPolicy),
    sources: policySources(entry.details.clientPolicy), serverId: undefined as string | undefined, sourceId: sourceId.value,
    entryId: entry.id, description: entry.details.description, room: entry.kind === 'room',
    favorite: favorites.value.includes(favoriteKey(entry.details.game, entry.details.apiUrl)),
    selected: !!activeServer?.apiUrl && favoriteKey(activeServer.game, activeServer.apiUrl) === favoriteKey(entry.details.game, entry.details.apiUrl),
  }))
  return [...saved, ...discovered].sort((a,b) => Number(b.selected) - Number(a.selected) || Number(b.favorite) - Number(a.favorite))
})
function connectCard(card: typeof cards.value[number]) {
  if (card.server) connectSaved(card.server)
  else if (card.entry) connectEntry(card.entry)
}
const selectedKey = computed(() => {
  if (savedServers.value.some(s => `saved:${s.id}` === selectedId.value) || visible.value.some(e => `directory:${e.id}` === selectedId.value)) return selectedId.value
  const saved = savedServers.value.find(s => s.id === state.selectedServerId) ?? savedServers.value[0]
  return saved ? `saved:${saved.id}` : visible.value[0] ? `directory:${visible.value[0].id}` : ''
})
const selectedSaved = computed(() => savedServers.value.find(s => `saved:${s.id}` === selectedKey.value))
const selected = computed(() => visible.value.find(e => `directory:${e.id}` === selectedKey.value))
const buildOptions = computed(() => (selectedGame.value?.builds ?? []).filter(b => selected.value?.details.builds.includes(b.id)).map(b => ({ value: b.id, label: localized(b.name, { dates: true }), caption: b.releaseVersion })))
let sourceRevision = 0, generation = 0, contextRevision = 0, timer: ReturnType<typeof setTimeout> | undefined, disposed = false
watch([sourceId, selectedKey, buildId, kind, () => state.selectedGameId], () => { ++contextRevision; profileConflict.value = undefined }, { flush: 'sync' })
async function refresh() {
  if (disposed) return
  const id = sourceId.value, version = ++generation
  if (!source.value?.baseUrl || !source.value.enabled) { entries.value = []; loading.value = false; return }
  loading.value = true
  try { const list = await call('directory.list', { sourceId: id }); if (!disposed && version === generation) { entries.value = list.entries; error.value = '' } }
  catch (e) { if (!disposed && version === generation) { entries.value = []; const message = (e as Error).message; error.value = message === 'directory_timeout' ? t('multiplayer.timeout') : message } }
  finally { if (version === generation) loading.value = false }
}
async function loadSources(id?: string) {
  const request = ++sourceRevision, nextSources = await call('directory.sources')
  if (disposed || request !== sourceRevision) return
  sources.value = nextSources
  const next = sources.value.some(s => s.id === (id || sourceId.value) && s.enabled) ? (id || sourceId.value) : sources.value.find(s => s.enabled)?.id ?? ''
  if (sourceId.value === next) await refresh(); else sourceId.value = next
}
async function manageSources() {
  if (source.value?.storeSourceId != null) storeSources.value = await call('store.sources')
  sourceDialog.value = true
}
const stopStoreUpdates = on('store.changed', () => { if (!disposed) void loadSources().catch(e => { error.value = (e as Error).message }) })
async function execute(action: () => Promise<void>) {
  if (disposed || busy.value || state.busy) return; busy.value = true; error.value = ''
  try { await action() } catch (e) { if (!disposed) error.value = (e as Error).message } finally { busy.value = false }
}
async function join() {
  const entry = selected.value, build = buildId.value, id = sourceId.value, game = state.selectedGameId, revision = contextRevision
  if (!entry || !build || entry.kind === 'room' && !roomsEnabled.value) return
  const entryId = entry.id
  const current = () => !disposed && revision === contextRevision && state.selectedGameId === game
  await execute(async () => {
    try {
      let server: Server
      if (entry.kind === 'server') {
        const profile = await call('directory.profile', { sourceId: id, entryId })
        if (!current()) return
        if (profile.inspection.exists && !profile.inspection.identical) {
          profileConflict.value = { ...profile, build, game, revision }
          return
        }
        server = await call('servers.importProfile', { text: profile.text, game })
      } else server = await call('directory.join', { sourceId: id, entryId, build })
      if (!current()) return
      await refreshServers()
      if (!current()) return
      if (server.game !== game || !state.servers.some(s => s.id === server.id && s.game === game)) throw new Error(t('multiplayer.connectionChanged'))
      state.selectedServerId = server.id; state.selectedBuildId = build
      const instance = state.instances.find(i => i.game === game && i.build === build)
      state.selectedInstanceId = instance?.id ?? ''
      await router.push('/home')
    } catch (error) { if (current()) throw error }
  })
}
async function overwriteDiscoveredProfile() {
  const candidate = profileConflict.value
  if (!candidate) return
  await execute(async () => {
    const server = await call('servers.importProfile', { text: candidate.text, game: candidate.game, expectedFingerprint: candidate.inspection.fingerprint })
    profileConflict.value = undefined
    if (disposed || candidate.revision !== contextRevision || state.selectedGameId !== candidate.game) return
    await refreshServers()
    if (disposed || candidate.revision !== contextRevision || state.selectedGameId !== candidate.game) return
    if (server.game !== candidate.game || !state.servers.some(saved => saved.id === server.id && saved.game === candidate.game)) throw new Error(t('multiplayer.connectionChanged'))
    state.selectedServerId = server.id; state.selectedBuildId = candidate.build
    state.selectedInstanceId = state.instances.find(instance => instance.game === candidate.game && instance.build === candidate.build)?.id ?? ''
    await router.push('/home')
  })
}
function connectSaved(server: Server) { selectedId.value = `saved:${server.id}`; chooseSaved() }
function connectEntry(entry: DirectoryEntry) {
  selectedId.value = `directory:${entry.id}`
  const compatible = (selectedGame.value?.builds ?? []).filter(build => entry.details.builds.includes(build.id))
  const build = compatible.find(build => build.id === state.selectedBuildId) ?? (compatible.length === 1 ? compatible[0] : undefined)
  if (!build) { detailsOpen.value = true; return }
  buildId.value = build.id
  if (inHost || !!demoMode?.enabled) void join(); else detailsOpen.value = true
}
function edit(server?: Server) { editing.value = server; serverDialog.value = true }
function chooseDetails(build: string) {
  if (selectedSaved.value) {
    if (build) { state.selectedBuildId = build; state.selectedInstanceId = state.instances.find(i => i.game === state.selectedGameId && i.build === build)?.id ?? '' }
    chooseSaved()
  } else { buildId.value = build; void join() }
}
function chooseSaved() {
  if (!selectedSaved.value) return
  state.selectedServerId = selectedSaved.value.id
  void router.push('/home')
}
function importPreset(value: ServerPreset) {
  if (!selectedSaved.value) return
  state.selectedServerId = selectedSaved.value.id
  preset.value = value
}
async function remove() {
  const server = deleting.value
  if (!server) return
  await execute(async () => { await call('servers.delete', { id: server.id }); await refreshServers(); deleting.value = undefined })
}
function submit() { draft.value = { name: '', apiUrl: '', description: '', builds: [] }; submitted.value = false; error.value = ''; submitDialog.value = true }
async function sendSubmission() {
  const details = { ...draft.value, game: state.selectedGameId }, id = sourceId.value
  await execute(async () => { await call('directory.submit', { sourceId: id, details }); submitted.value = true })
}
async function poll() { if (!busy.value && !loading.value && !submitDialog.value && !sourceDialog.value && !serverDialog.value && !preset.value && !deleting.value) await refresh(); if (!disposed) timer = setTimeout(poll, 15000) }
watch(sourceId, () => { entries.value = []; if (selectedId.value.startsWith('directory:')) selectedId.value = ''; void refresh() })
watch(kind, () => { selectedId.value = ''; query.value = ''; detailsOpen.value = false })
watch(() => selected.value?.id, () => { buildId.value = buildOptions.value[0]?.value ?? '' })
watch(() => state.selectedGameId, () => { detailsOpen.value = false; savedCatalogs.value = {}; selectedId.value = ''; query.value = ''; submitDialog.value = false; serverDialog.value = false; deleting.value = undefined; preset.value = undefined })
watch(() => gameServers.value.map(s => `${s.id}:${s.apiUrl}`).join('|'), () => void loadSavedCatalogs(), { immediate: true })
onMounted(async () => { try { await loadSources() } catch (e) { error.value = (e as Error).message }; if (!disposed) timer = setTimeout(poll, 15000) })
onBeforeUnmount(() => { disposed = true; generation++; contextRevision++; catalogRevision++; clearTimeout(timer); stopStoreUpdates() })
</script>
<template>
  <section class="page">
    <header class="page-header"><div><h1 class="page-title">{{ t('multiplayer.title') }}</h1><p class="page-desc">{{ t('multiplayer.description') }}</p></div><div class="page-actions"><Transition name="submit-action"><WButton v-if="kind === 'server'" :disabled="busy || !(inHost || !!demoMode?.enabled) || !source?.baseUrl || source.storeSourceId != null" @click="submit">{{ t('multiplayer.submit') }}</WButton></Transition><WButton variant="accent" :disabled="busy || state.busy || kind === 'room' && !hostingEnabled" @click="kind === 'server' ? edit() : hostingDialog = true"><FluentIcon glyph="add" /><WAnimatedSize axis="width" class="primary-label"><Transition name="action-label"><span :key="kind" class="action-text">{{ t(kind === 'server' ? 'service.addServer' : 'multiplayer.create') }}</span></Transition></WAnimatedSize></WButton></div></header>
    <PageBody>
      <p v-if="multiplayerPreview" class="development-notice">{{ t('serverInfo.preview') }}</p>
      <div class="directory-toolbar">
        <div class="directory-tools-left"><WSelectorBar class="directory-tabs" :model-value="kind" :items="directoryTabs" compact-indicator :aria-label="t('multiplayer.title')" @update:model-value="selectKind" /><div class="directory-search"><WTextBox v-model="query" :placeholder="t('multiplayer.search')" :aria-label="t('multiplayer.search')" /></div></div>
        <div class="directory-tools-right"><WComboBox v-model="sourceId" class="directory-source" :options="sourceOptions" :aria-label="t('multiplayer.source')" :disabled="busy" /><WButton variant="subtle" :disabled="busy" :aria-label="t('multiplayer.sources')" @click="execute(manageSources)"><FluentIcon glyph="settings" /></WButton><WButton :disabled="loading || busy || !source?.baseUrl" @click="refresh">{{ t('live.refresh') }}</WButton></div>
      </div>
      <!-- <p v-if="!roomsEnabled" class="development-notice" role="status">{{ t('multiplayer.rooms') }} · {{ t('featureAvailability.development') }}</p> -->
      <p v-if="error && !submitDialog" class="directory-error" role="alert">{{ error }}</p>
      <div class="directory-content"><Transition name="directory-page" mode="out-in"><div :key="kind" class="directory-panel">
      <div v-if="kind === 'room' && !source?.baseUrl" class="empty-state"><FluentIcon glyph="globe" :size="42" /><h2>{{ t('multiplayer.source') }}</h2><p>{{ t('multiplayer.sourceEmpty') }}</p><WButton @click="execute(manageSources)">{{ t('multiplayer.sources') }}</WButton></div>
      <div v-else class="server-gallery">
        <TransitionGroup v-if="cards.length" name="server-cards" tag="div" class="server-grid">
          <ServerCard v-for="card in cards" :key="card.key" :name="card.name" :game="card.game" :builds="card.builds" :builds-loading="card.buildsLoading" :room="card.room" :address="card.address" :description="card.description" :presentation="card.presentation" :recommendations="card.recommendations" :sources="card.sources" :server-id="card.serverId" :icon-asset="card.server?.iconAsset" :source-id="card.sourceId" :entry-id="card.entryId" :favorite="card.favorite" :selected="card.selected" :busy="busy || state.busy" @favorite="toggleFavorite(favoriteKey(card.game, card.address || card.serverId || card.key))" @details="openDetails(card.key)" @connect="connectCard(card)" />
        </TransitionGroup>
        <p v-else class="muted">{{ t(!source?.baseUrl ? 'multiplayer.sourceEmpty' : loading ? 'live.working' : 'multiplayer.empty') }}</p>
      </div>
      </div></Transition></div>
    </PageBody>
    <ServerDetailsDialog :open="detailsOpen && !!(selectedSaved || selected)" :server="selectedSaved" :entry="selected" :catalog="selectedSaved ? savedCatalogs[selectedSaved.id] : undefined" :source-id="sourceId" :source-name="source?.name" :busy="busy || state.busy" @close="detailsOpen = false" @edit="edit(selectedSaved)" @remove="deleting = selectedSaved" @preset="importPreset" @choose="chooseDetails" />
    <LocalServerSetupDialog :open="hostingDialog" default-scope="room" @close="hostingDialog = false" />
    <WDialog :open="!!profileConflict" :title="t('serverInfo.overwriteProfileTitle')" :primary-text="t('serverInfo.overwriteProfile')" :close-text="t('dialog.cancel')" :primary-disabled="busy" :close-disabled="busy" dismiss-on-backdrop @primary="overwriteDiscoveredProfile" @close="profileConflict = undefined"><p>{{ t('serverInfo.overwriteProfileBody', { name: profileConflict?.inspection.server.name }) }}</p></WDialog>
    <ServerDialog :open="serverDialog" :server="editing" @close="serverDialog = false" @saved="server => selectedId = `saved:${server.id}`" />
    <InstanceDialog :open="!!preset" :preset="preset" @close="preset = undefined" />
    <WDialog :open="!!deleting" :title="t('service.removeServerTitle')" :primary-text="t('service.remove')" :close-text="t('dialog.cancel')" :primary-disabled="busy || state.busy" :close-disabled="busy" @primary="remove" @close="deleting = undefined"><p>{{ t('service.removeServerBody', { name: deleting?.name }) }}</p><p v-if="error" class="directory-error" role="alert">{{ error }}</p></WDialog>
    <DirectorySourceDialog :open="sourceDialog && source?.storeSourceId == null" :sources="sources.filter(s => s.storeSourceId == null)" :selected-id="sourceId" @close="sourceDialog = false" @saved="id => execute(() => loadSources(id))" />
    <StoreSourcesDialog :open="sourceDialog && source?.storeSourceId != null" :sources="storeSources" @close="sourceDialog = false" @changed="execute(async () => { storeSources = await call('store.sources'); await loadSources() })" />
    <WDialog :open="submitDialog" :title="t(submitted ? 'multiplayer.submitted' : 'multiplayer.submit')" :primary-text="submitted ? '' : t('multiplayer.submit')" :close-text="t(submitted ? 'titlebar.close' : 'dialog.cancel')" :primary-disabled="busy || !draft.name.trim() || !draft.apiUrl.trim() || !draft.builds.length" :close-disabled="busy" @primary="sendSubmission" @close="submitDialog = false">
      <p v-if="submitted">{{ t('multiplayer.submitHint') }}</p><div v-else class="submission-form"><label><span>{{ t('hosting.name') }}</span><WTextBox v-model="draft.name" :maxlength="128" :disabled="busy" /></label><label><span>{{ t('multiplayer.address') }}</span><WTextBox v-model="draft.apiUrl" :maxlength="2048" :disabled="busy" placeholder="https://server.example/" /></label><label><span>{{ t('multiplayer.introduction') }}</span><WTextBox v-model="draft.description" :maxlength="512" :disabled="busy" /></label><fieldset><legend>{{ t('multiplayer.builds') }}</legend><div class="build-checks"><label v-for="build in selectedGame?.builds" :key="build.id"><input v-model="draft.builds" type="checkbox" :value="build.id" :disabled="busy" /><span>{{ localized(build.name, { dates: true }) }}</span><small>{{ build.releaseVersion }}</small></label></div></fieldset><p class="muted">{{ t('multiplayer.submitHint') }}</p><p v-if="error" class="directory-error" role="alert">{{ error }}</p></div>
    </WDialog>
  </section>
</template>
<style scoped>
.development-notice { margin: 0 0 14px; color: var(--text-secondary); font-size: 12px; }
.directory-toolbar { display: flex; align-items: center; flex-wrap: wrap; gap: 12px 20px; padding-bottom: 20px; }
.directory-tools-left, .directory-tools-right { display: flex; align-items: center; gap: 10px; min-width: 0; }
.directory-tools-left { flex: 1 1 auto; }.directory-tools-right { margin-inline-start: auto; flex: none; }
.directory-search { width: clamp(150px,18vw,250px); min-width: 120px; }
.directory-tabs { flex: none; }.directory-tabs :deep(.w-selbar-item) { padding: 10px 14px; }.directory-tabs :deep(.w-selbar-item.selected) { font-weight: 400; }.directory-source { width: 220px; }
@media (max-width: 850px) { .directory-tools-left { flex: 1 1 100%; }.directory-search { flex: 1; max-width: 280px; }.directory-tools-right { margin-inline-start: auto; } }
.directory-content { min-height: 260px; }.directory-panel { min-width: 0; }
.directory-page-enter-active { transition: opacity 220ms linear, transform 260ms cubic-bezier(0.1,0.9,0.2,1); }
.directory-page-leave-active { transition: opacity 100ms linear, transform 100ms ease-in; pointer-events: none; }
.directory-page-enter-from { opacity: 0; transform: translateY(8px); }
.directory-page-leave-to { opacity: 0; transform: translateY(-4px); }
@media (prefers-reduced-motion: reduce) { .directory-page-enter-active, .directory-page-leave-active { transition: opacity 100ms linear; }.directory-page-enter-from, .directory-page-leave-to { transform: none; } }
.gallery-search { display: flex; gap: 12px; margin-bottom: 20px; }.gallery-search .w-textbox { flex: 1; max-width: 360px; }.section-heading { font-size: 14px; font-weight: 600; margin: 24px 0 12px; }.section-heading:first-of-type { margin-top: 0; }
.server-grid { position: relative; display: grid; grid-template-columns: repeat(auto-fill,minmax(min(380px,100%),1fr)); gap: 16px; align-items: stretch; }
.server-dialog-body { height: min(600px,65vh); display: flex; min-height: 0; }.entry-detail { display: flex; flex-direction: column; width: 100%; min-height: 0; }.detail-heading { flex: none; padding: 12px 0 20px; }.detail-heading h2 { font-size: 22px; margin: 0 0 8px; }.detail-heading p,.server-address { font-size: 12px; color: var(--text-secondary); overflow-wrap: anywhere; margin: 0; }.description-scroll { margin-inline: 0; --page-inset-inline: 0px; }.join-footer { display: flex; flex-wrap: wrap; align-items: end; gap: 12px; padding-top: 20px; }.join-footer label { display: grid; gap: 8px; flex: 1; min-width: 200px; font-size: 12px; }
.directory-error { color: var(--fill-critical); font-size: 13px; overflow-wrap: anywhere; }.submission-form,.submission-form label { display: grid; gap: 14px; }.submission-form label { gap: 7px; font-size: 13px; }.build-checks { display: grid; gap: 10px; }.build-checks label { display: flex; gap: 8px; }.build-checks small { color: var(--text-secondary); }fieldset { border: 1px solid var(--card-stroke-default); padding: 14px; border-radius: 6px; }legend { font-size: 13px; }.empty-state { text-align: center; padding: 64px 24px; color: var(--text-secondary); }

.page-actions { display: flex; align-items: center; gap: 8px; }
.action-text { display: inline-block; white-space: nowrap; }
.action-label-enter-active { transition: opacity 180ms linear, transform 220ms cubic-bezier(0.1,0.9,0.2,1); }
.action-label-leave-active { position: absolute; inset: 0 auto auto 0; transition: opacity 100ms linear; }
.action-label-enter-from { opacity: 0; transform: translateY(4px); }.action-label-leave-to { opacity: 0; }
.submit-action-enter-active, .submit-action-leave-active { transition: opacity 180ms linear, max-width 240ms ease, padding 240ms ease, margin 240ms ease; max-width: 260px; overflow: hidden; }
.submit-action-enter-from, .submit-action-leave-to { opacity: 0; max-width: 0; min-width: 0; padding-inline: 0; margin-inline-end: -8px; }
@media (prefers-reduced-motion: reduce) { .action-label-enter-active, .action-label-leave-active, .submit-action-enter-active, .submit-action-leave-active { transition: none; }.action-label-enter-from { transform: none; } }

.server-grid > .server-cards-move { transition: transform 360ms cubic-bezier(0.2,0.8,0.2,1), border-color 140ms linear, box-shadow 140ms linear; }
.server-grid > .server-cards-enter-active { transition: opacity 180ms linear, transform 220ms ease-out; }
.server-cards-enter-from { opacity: 0; transform: translateY(8px); }
@media (prefers-reduced-motion: reduce) { .server-cards-move, .server-grid > .server-cards-enter-active { transition: none; }.server-cards-enter-from { transform: none; } }
</style>

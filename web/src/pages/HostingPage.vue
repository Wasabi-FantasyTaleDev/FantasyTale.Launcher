<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { call, demoMode, inHost, hostingPreview, type HostingOverview, type HostingProfile, type HostingSession, type DirectorySource, type RoomPublication } from '../bridge'
import { selectedGame, state, refreshServers } from '../state'
import { localized } from '../i18n'
import PageBody from '../components/PageBody.vue'
import HostingLogDialog from '../components/HostingLogDialog.vue'
import DirectorySourceDialog from '../components/DirectorySourceDialog.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import WButton from '../controls/WButton.vue'
import WDialog from '../controls/WDialog.vue'
import WTextBox from '../controls/WTextBox.vue'
import WComboBox from '../controls/WComboBox.vue'
import HostingTerminal from '../components/HostingTerminal.vue'
import WContentTransition from '../controls/WContentTransition.vue'
import WSelectorBar from '../controls/WSelectorBar.vue'
import LocalServerSetupDialog from '../components/LocalServerSetupDialog.vue'

const { t } = useI18n()
const detailsOpen = ref(false), router = useRouter()
const hostingEnabled = computed(() => selectedGame.value?.localHostingEnabled !== false)
const overview = ref<HostingOverview>({ profiles: [], sessions: [] }), selectedId = ref(''), busy = ref(false), error = ref('')
const dialog = ref(false), editing = ref<HostingProfile>(), confirm = ref<'stop' | 'remove' | null>(null)
const confirmationTarget = ref<{ profile: HostingProfile; session?: HostingSession }>()
const detailTab = ref<'information' | 'terminal'>('information')
const detailTabs = computed(() => ['information', 'terminal'].map(key => ({ key, label: t(`localHosting.${key}`) })))
const sources = ref<DirectorySource[]>([]), publications = ref<RoomPublication[]>([]), publishDialog = ref(false), sourceDialog = ref(false), publishSourceId = ref(''), description = ref('')
const publishTarget = ref<{ id: string; runId: string }>()
const publication = computed(() => publications.value.find(p => p.profileId === selected.value?.id))
const published = computed(() => !!publication.value && !['expired', 'withdrawn'].includes(publication.value.state))
const directoryOptions = computed(() => sources.value.filter(s => s.enabled && s.baseUrl && s.storeSourceId == null).map(s => ({ value: s.id, label: s.name })))
const profiles = computed(() => overview.value.profiles.filter(p => p.game === state.selectedGameId))
const selected = computed(() => profiles.value.find(p => p.id === selectedId.value))
const selectedBuildName = computed(() => builds.value.find(b => b.value === selected.value?.build)?.label ?? selected.value?.build)
const session = computed(() => overview.value.sessions.find(s => s.profileId === selected.value?.id))
const running = (s?: HostingSession) => !!s && ['starting', 'ready', 'stopping'].includes(s.state)
const builds = computed(() => (selectedGame.value?.builds ?? []).filter(b => b.supportsHosting).map(b => ({ value: b.id, label: localized(b.name, { dates: true }), caption: b.releaseVersion })))
let timer: ReturnType<typeof setTimeout> | undefined, disposed = false, refreshRevision = 0, contextRevision = 0
watch([selectedId, () => state.selectedGameId], () => { ++contextRevision }, { flush: 'sync' })
async function refresh() {
  if (disposed || !hostingEnabled.value) return
  const revision = ++refreshRevision
  try {
    const [next, rooms] = await Promise.all([call('hosting.list'), call('directory.publications')]); if (disposed || revision !== refreshRevision) return
    overview.value = next
    publications.value = rooms
    if (!profiles.value.some(p => p.id === selectedId.value)) selectedId.value = profiles.value[0]?.id ?? ''
  } catch (error) {
    if (!disposed && revision === refreshRevision) throw error
  }
}
async function poll() {
  try { await refresh() } catch (e) { error.value = (e as Error).message }
  finally { if (!disposed && hostingEnabled.value) timer = setTimeout(poll, 1500) }
}
async function execute(action: () => Promise<void>) {
  if (disposed || busy.value || !hostingEnabled.value) return
  busy.value = true; error.value = ''
  try { await action(); await refresh() } catch (e) { if (!disposed) error.value = (e as Error).message } finally { busy.value = false }
}
function edit(profile?: HostingProfile) {
  if (!hostingEnabled.value) return
  editing.value = profile; dialog.value = true
}
async function saved(profile: HostingProfile) { selectedId.value = profile.id; await execute(async () => {}) }
async function start() { const id = selected.value?.id; if (id) await execute(async () => { await call('hosting.start', { id }); await refreshServers({ refreshSelectedServices: false }) }) }
async function applyConfirmation() {
  const profile = confirmationTarget.value?.profile, current = confirmationTarget.value?.session, action = confirm.value
  if (!profile) return
  await execute(async () => {
    if (action === 'stop' && current) await call('hosting.stop', { id: profile.id, runId: current.runId })
    else if (action === 'remove') await call('hosting.delete', { id: profile.id })
    confirm.value = null; await refreshServers({ refreshSelectedServices: false })
  })
}
function ask(action: 'stop' | 'remove') {
  if (!selected.value) return
  confirmationTarget.value = { profile: selected.value, session: session.value }; confirm.value = action
}
async function connect() {
  if (!selected.value || session.value?.state !== 'ready') return
  const profile = { ...selected.value }, runId = session.value.runId, revision = contextRevision
  const current = () => !disposed && revision === contextRevision && selectedId.value === profile.id &&
    state.selectedGameId === profile.game && session.value?.runId === runId && session.value.state === 'ready'
  await execute(async () => {
    await refreshServers({ refreshSelectedServices: false })
    if (!current()) return
    const destination = await call('hosting.destination', { id: profile.id, runId })
    if (!current()) return
    if (destination.game !== profile.game || !state.servers.some(s => s.id === destination.id)) throw new Error(t('hosting.stopped'))
    state.selectedServerId = destination.id
    const instance = state.instances.find(i => i.game === profile.game && i.build === profile.build)
    state.selectedInstanceId = instance?.id ?? ''
    state.selectedBuildId = profile.build
    await router.push('/home')
  })
}
async function admin() { const url = session.value?.adminUrl; if (url) await execute(async () => { await call('shell.openExternal', { url }) }) }
async function loadDirectories(id?: string) {
  sources.value = await call('directory.sources')
  publishSourceId.value = directoryOptions.value.some(s => s.value === (id || publishSourceId.value)) ? (id || publishSourceId.value) : directoryOptions.value[0]?.value ?? ''
}
function showPublish() {
  if (!selected.value || session.value?.state !== 'ready') return
  publishTarget.value = { id: selected.value.id, runId: session.value.runId }; description.value = ''; publishDialog.value = true
  void execute(() => loadDirectories())
}
async function publish() {
  const target = publishTarget.value; if (!target) return
  await execute(async () => { await call('directory.publish', { ...target, sourceId: publishSourceId.value, description: description.value }); publishDialog.value = false })
}
async function withdraw() {
  const current = publication.value; if (!current) return
  await execute(async () => { await call('directory.withdraw', { id: current.profileId, runId: current.runId }) })
}
watch(() => state.selectedGameId, () => { selectedId.value = profiles.value[0]?.id ?? ''; dialog.value = false; confirm.value = null })
onMounted(() => { void poll() })
onBeforeUnmount(() => { disposed = true; ++refreshRevision; ++contextRevision; clearTimeout(timer) })
</script>

<template>
  <section class="page">
    <header class="page-header"><div><h1 class="page-title">{{ t('hosting.title') }}</h1><p class="page-desc">{{ t('hosting.description') }}</p></div><WButton variant="accent" :disabled="!hostingEnabled || busy || !(inHost || !!demoMode?.enabled || hostingPreview) || !builds.length" @click="edit()"><FluentIcon glyph="add" />{{ t('hosting.add') }}</WButton></header>
    <p v-if="error && !dialog" class="hosting-error" role="alert">{{ error }}</p>
    <PageBody contained>
      <div v-if="!hostingEnabled" class="empty-state" role="status"><FluentIcon glyph="hardDrive" :size="42" /><h2>{{ t('hosting.title') }}</h2><p>{{ t('featureAvailability.development') }}</p><WButton disabled>{{ t('hosting.start') }}</WButton></div>
      <div v-else-if="!profiles.length" class="empty-state"><FluentIcon glyph="hardDrive" :size="42" /><h2>{{ t(builds.length ? 'hosting.empty' : 'hosting.unsupported') }}</h2><p>{{ t('hosting.emptyBody') }}</p><WButton v-if="builds.length" :disabled="!(inHost || !!demoMode?.enabled || hostingPreview)" @click="edit()">{{ t('hosting.add') }}</WButton></div>
      <div v-else class="hosting-workspace">
        <PageBody class="profile-list"><button v-for="profile in profiles" :key="profile.id" class="profile-row surface" :class="{ selected: selectedId === profile.id }" @click="selectedId = profile.id"><FluentIcon :glyph="profile.mode === 'private' ? 'hardDrive' : 'connect'" :size="22" /><span class="profile-copy"><strong>{{ profile.name }}</strong><small>{{ t(`hosting.${profile.mode}`) }}</small></span><span class="state-dot" :class="overview.sessions.find(s => s.profileId === profile.id)?.state" /></button></PageBody>
        <section v-if="selected" class="host-detail surface">
          <div class="hosting-tabs"><WSelectorBar :model-value="detailTab" :items="detailTabs" @update:model-value="detailTab = $event as typeof detailTab" /></div>
          <WContentTransition>
          <HostingTerminal v-if="detailTab === 'terminal'" :key="`terminal:${selected.id}`" :profile="selected" :session="session" @changed="refresh" />
          <div v-else key="information" class="information-panel">
          <div class="detail-heading"><div class="host-mark"><FluentIcon :glyph="selected.mode === 'private' ? 'hardDrive' : 'connect'" :size="28" /></div><div><h2>{{ selected.name }}</h2><p>{{ t(`hosting.${selected.mode}`) }} · {{ selectedBuildName }}</p></div><WButton variant="subtle" :disabled="busy" :aria-label="t('hosting.edit')" @click="edit(selected)"><FluentIcon glyph="settings" /></WButton></div>
          <div class="host-state"><span class="state-dot" :class="session?.state" /><strong>{{ t(`hosting.${session?.state ?? 'stopped'}`) }}</strong><span v-if="session?.forcedStop">{{ t('hosting.forced') }}</span><span v-else-if="session?.failure">{{ t(`hosting.${session.failure}`) }}</span><WButton v-if="session?.failure" variant="subtle" @click="detailsOpen = true">{{ t('serverFailure.details') }}</WButton></div>
          <div class="host-actions"><WButton v-if="!running(session)" variant="accent" :disabled="busy || !(inHost || !!demoMode?.enabled || hostingPreview)" @click="start"><FluentIcon glyph="play" />{{ t('hosting.start') }}</WButton><WButton v-else variant="accent" :disabled="busy || session?.state !== 'ready'" @click="connect"><FluentIcon glyph="play" />{{ t('hosting.connect') }}</WButton><WButton v-if="running(session)" :disabled="busy || session?.state === 'stopping'" @click="ask('stop')">{{ t('hosting.stop') }}</WButton><WButton :disabled="busy || session?.state !== 'ready'" @click="admin"><FluentIcon glyph="openInNewWindow" />{{ t('hosting.admin') }}</WButton></div>
          <PageBody class="detail-body"><dl class="host-facts"><div><dt>{{ t('hosting.package') }}</dt><dd>{{ selected.packagePath }}</dd></div><div><dt>{{ t('hosting.address') }}</dt><dd>{{ selected.mode === 'private' ? t('hosting.localOnly') : `${selected.advertisedHost}:${selected.apiPort}` }}</dd></div><div><dt>{{ t('hosting.storage') }}</dt><dd>{{ t(`hosting.storage${selected.storageMode ?? (selected.temporaryData ? 'temporary' : 'package')}`) }}</dd></div></dl>
            <div v-if="selected.mode === 'room'" class="publication-row"><FluentIcon glyph="globe" :size="22" /><div><strong>{{ t(`multiplayer.${publication?.state ?? 'withdrawn'}`) }}</strong><small>{{ publication?.error || t('multiplayer.publishHint') }}</small></div><WButton v-if="published" :disabled="busy" @click="withdraw">{{ t('multiplayer.withdraw') }}</WButton><WButton v-else :disabled="busy || session?.state !== 'ready'" @click="showPublish">{{ t('multiplayer.publish') }}</WButton></div>

            <WButton class="remove" variant="subtle" :disabled="busy || running(session)" @click="ask('remove')"><FluentIcon glyph="delete" />{{ t('hosting.remove') }}</WButton>
          </PageBody>
          </div>
          </WContentTransition>
        </section>
      </div>
    </PageBody>
    <WDialog :open="publishDialog" :title="t('multiplayer.publish')" :primary-text="t('multiplayer.publish')" :close-text="t('dialog.cancel')" :primary-disabled="busy || !publishSourceId" :close-disabled="busy" @primary="publish" @close="publishDialog = false"><div class="host-form"><label><span>{{ t('multiplayer.source') }}</span><WComboBox v-model="publishSourceId" :options="directoryOptions" :disabled="busy" /></label><WButton :disabled="busy" @click="sourceDialog = true">{{ t('multiplayer.sources') }}</WButton><label><span>{{ t('multiplayer.introduction') }}</span><WTextBox v-model="description" :maxlength="512" :disabled="busy" /></label><p class="muted">{{ t('multiplayer.publishHint') }}</p><p v-if="error" class="hosting-error" role="alert">{{ error }}</p></div><DirectorySourceDialog :open="sourceDialog" :sources="sources.filter(s => s.storeSourceId == null)" :selected-id="publishSourceId" @close="sourceDialog = false" @saved="id => execute(() => loadDirectories(id))" /></WDialog>
    <LocalServerSetupDialog :open="dialog" :profile="editing" default-scope="room" @close="dialog = false" @saved="saved" />
    <WDialog :open="!!confirm" :title="t(confirm === 'stop' ? 'hosting.stopTitle' : 'hosting.removeTitle')" :primary-text="t(confirm === 'stop' ? 'hosting.stop' : 'hosting.remove')" :close-text="t('dialog.cancel')" :primary-disabled="busy" :close-disabled="busy" @primary="applyConfirmation" @close="confirm = null"><p>{{ t(confirm === 'stop' ? 'hosting.stopBody' : 'hosting.removeBody') }}</p></WDialog>
    <HostingLogDialog :open="detailsOpen" :session="session" :error="error" @close="detailsOpen = false" />
  </section>
</template>

<style scoped>
.hosting-tabs { display: flex; align-items: center; padding: 8px 16px; min-height: 52px; border-bottom: 1px solid var(--card-stroke-default); flex: none; }
.hosting-tabs :deep(.w-selbar-item) { padding: 8px 12px; font-weight: 400; }
.information-panel { display: flex; flex-direction: column; flex: 1; min-height: 0; }

.hosting-workspace { flex: 1; min-height: 0; display: grid; grid-template-columns: 290px minmax(0, 1fr); gap: 20px; }
.profile-list { --page-inset-inline: 0px; --page-inset-bottom: 0px; }
.profile-row { width: 100%; display: flex; align-items: center; gap: 14px; padding: 18px 16px; margin-bottom: 8px; text-align: left; position: relative; transition: background 160ms; }
.profile-row.selected { background: var(--ctrl-fill-secondary); }.profile-row.selected::before { content: ''; position: absolute; width: 3px; height: 26px; left: 0; border-radius: 3px; background: var(--accent-default); }
.profile-row > .profile-copy { flex: 1; min-width: 0; display: grid; gap: 5px; }.profile-row strong { overflow: hidden; text-overflow: ellipsis; }.profile-row small { color: var(--text-secondary); }
.host-detail { min-height: 0; overflow: hidden; display: flex; flex-direction: column; }.detail-heading { display: flex; align-items: center; gap: 16px; padding: 26px 28px 18px; }.host-mark { width: 60px; height: 60px; display: grid; place-items: center; background: var(--ctrl-fill-secondary); border-radius: 12px; }.detail-heading > div:nth-child(2) { flex: 1; min-width: 0; }.detail-heading h2 { margin: 0; font-size: 22px; font-weight: 600; overflow-wrap: anywhere; }.detail-heading p { margin: 7px 0 0; color: var(--text-secondary); font-size: 13px; }
.host-state { padding: 0 28px 18px; display: flex; gap: 9px; align-items: center; flex-wrap: wrap; font-size: 13px; }.host-state > span:last-child:not(.state-dot) { width: 100%; color: var(--text-secondary); }.state-dot { width: 8px; height: 8px; flex: none; border-radius: 50%; background: var(--text-tertiary); }.state-dot.ready { background: #298348; }.state-dot.starting, .state-dot.stopping { background: var(--accent-default); animation: hosting-pulse 1.2s ease-in-out infinite alternate; }.state-dot.failed { background: #bd3434; }
.host-actions { display: flex; gap: 9px; padding: 0 28px 24px; flex-wrap: wrap; border-bottom: 1px solid var(--card-stroke-default); }.detail-body { --page-inset-inline: 28px; --page-inset-bottom: 20px; margin-inline: 0; }.host-facts { display: grid; gap: 20px; margin: 22px 0; font-size: 13px; }.host-facts > div { display: grid; grid-template-columns: 110px minmax(0, 1fr); gap: 16px; }.host-facts dt { color: var(--text-secondary); }.host-facts dd { margin: 0; overflow-wrap: anywhere; user-select: text; }.host-log { max-height: 220px; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; font-size: 12px; user-select: text; margin: 0; }.log-path { font-size: 11px; color: var(--text-secondary); overflow-wrap: anywhere; user-select: text; }.remove { margin-top: 20px; }
.host-form { display: grid; gap: 16px; }.host-form label { display: grid; gap: 7px; font-size: 13px; }.folder-field { display: flex; gap: 8px; }.folder-field .w-textbox { flex: 1; min-width: 0; }.hint { color: var(--text-secondary); font-size: 12px; line-height: 1.6; margin: 0 0 10px; }.temporary-field { display: flex; gap: 20px; align-items: center; }.temporary-field > span { flex: 1; display: grid; gap: 5px; }.temporary-field strong { font-weight: 400; font-size: 13px; }.temporary-field small { color: var(--text-secondary); font-size: 12px; line-height: 1.5; }.port-fields { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }.hosting-error { color: #bd3434; font-size: 13px; overflow-wrap: anywhere; }
.host-form :deep(.w-expander-body) { padding: 16px 24px; }
.publication-row { display: flex; align-items: center; gap: 16px; padding: 18px 0; margin-bottom: 12px; border-top: 1px solid var(--card-stroke-default); }.publication-row > div { flex: 1; min-width: 0; display: grid; gap: 7px; }.publication-row strong { font-size: 13px; font-weight: 600; }.publication-row small { color: var(--text-secondary); font-size: 12px; line-height: 1.6; }.publication-row > .fluent-icon { color: var(--text-secondary); flex: none; }
@keyframes hosting-pulse { from { opacity: .35; } to { opacity: 1; } }@media (prefers-reduced-motion: reduce) { .state-dot { animation: none; } }
</style>

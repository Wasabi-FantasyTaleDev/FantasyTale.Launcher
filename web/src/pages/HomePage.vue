<script setup lang="ts">
import PageBody from '../components/PageBody.vue'
import { computed, nextTick, onActivated, onDeactivated, onMounted, onScopeDispose, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost, on, type HostingProfile } from '../bridge'
import { localized, clientRegion } from '../i18n'
import { clientVersionCaption } from '../clientVersion'
import { gameInstances, gamePreparing, gameReady, homeGames, isRunning, perform, selectGame, selectedBuild, selectedGame, gameServers, selectedServer, buildNotServed, loginRequired, loginPolicy, navigationEntries, clientLanguages, isClientLanguageInstalled, languageNames, setClientLanguage, refreshServices, refreshServers, selectedInstance, state } from '../state'
import PluginSchemePicker from '../components/PluginSchemePicker.vue'
import { ensurePluginScheme, pluginSchemesBusy } from '../state'
import AccountPicker from '../components/AccountPicker.vue'
import InstanceDialog from '../components/InstanceDialog.vue'
import InstallationTaskList from '../components/InstallationTaskList.vue'
import LocalServerSetupDialog from '../components/LocalServerSetupDialog.vue'
import GameHeroMedia from '../components/GameHeroMedia.vue'
import { prefs } from '../theme'
import ClientUpdateStatus from '../components/ClientUpdateStatus.vue'
import HomeUpdate from '../components/HomeUpdate.vue'
import HomeResourceProgress from '../components/HomeResourceProgress.vue'
import { downloadTasks, trackDownload } from '../downloads/live'
import { isTerminal, matchesInstanceUpdate, type DownloadTask } from '../downloads/model'
import WButton from '../controls/WButton.vue'
import { availableStoreUpdates } from '../store/background'
import WComboBox from '../controls/WComboBox.vue'
import WDialog from '../controls/WDialog.vue'
import WTextBox from '../controls/WTextBox.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import WProgressRing from '../controls/WProgressRing.vue'
import { requestStop } from '../state'
import { requestRecommendations, dismissRecommendations } from '../plugins/recommendations'
import { requestPluginPolicy } from '../plugins/pluginPolicy'
import { requestLaunchWarnings, finishLaunchWarnings } from '../plugins/launchWarnings'
import { launchWithResourceChoices, finishResourceConflicts } from '../plugins/resourceConflicts'
import { requestRequiredUpdates, finishRequiredUpdates } from '../plugins/launchUpdates'
import ServerMaintenanceDialog from '../components/ServerMaintenanceDialog.vue'
import ServerNoticeDialog from '../components/ServerNoticeDialog.vue'
import ServerNoticeListDialog from '../components/ServerNoticeListDialog.vue'
import MarkdownText from '../components/MarkdownText.vue'
import { accountSection, accountSectionsShown, defaultSection, loadServerStatus, pendingAuthSection, serverStatus } from '../servers/sections'
import { selectAccount } from '../state'
import type { ServerNotice, ServerStatus } from '../bridge'

const { t, locale } = useI18n()
const router = useRouter()
const localPlayOpen = ref(false), choosingLocal = ref(false), localProfile = ref<HostingProfile>(), localStarting = ref(false)
const localPlayerName = ref(localStorage.getItem('localPlay.playerName') || ''), nicknameOpen = ref(false)
const validNickname = computed(() => /^[A-Za-z0-9_.@-]{1,32}$/.test(localPlayerName.value.trim()))
watch(localPlayerName, name => localStorage.setItem('localPlay.playerName', name))
const nameOnly = computed(() => selectedServer.value?.kind === 'room' && loginPolicy.value === 'none')
const addOpen = ref(false), loginOpen = ref(false), account = ref('')
const hero = ref<HTMLElement>()
const controls = ref<HTMLElement>()
const controlsHeight = ref<number>()
const regularControlsHeight = ref(142)
const titleOffset = computed(() => gamePreparing.value ? Math.max(0, regularControlsHeight.value - (controlsHeight.value ?? 0)) : 0)
let controlsObserver: ResizeObserver | undefined
let pageActive = true
onActivated(() => { pageActive = true })
onDeactivated(() => { pageActive = false })
onMounted(() => {
  controlsObserver = new ResizeObserver(() => {
    if (!pageActive || !controls.value) return
    const height = controls.value.getBoundingClientRect().height
    controlsHeight.value = height
    if (!gamePreparing.value) regularControlsHeight.value = height
  })
  if (controls.value) controlsObserver.observe(controls.value)
})
onScopeDispose(() => controlsObserver?.disconnect())
const gameIndex = computed(() => homeGames.value.findIndex(game => game.id === state.selectedGameId))
const previousGame = computed(() => homeGames.value[gameIndex.value - 1])
const nextGame = computed(() => homeGames.value[gameIndex.value + 1])
const videos = computed(() => selectedGame.value?.backgroundVideos ?? (selectedGame.value?.backgroundVideo ? [selectedGame.value.backgroundVideo] : []))
const englishName = computed(() => selectedGame.value?.name.en ?? localized(selectedGame.value?.name))
const displayName = computed(() => {
  const name = localized(selectedGame.value?.name)
  return name === englishName.value ? selectedGame.value?.name.ko || name : name
})
watch(() => selectedGame.value?.id, () => { nicknameOpen.value = false; addOpen.value = false; loginOpen.value = false; account.value = '' })
async function switchGame(direction: -1 | 1) {
  const game = direction === -1 ? previousGame.value : nextGame.value
  if (!game || state.busy) return
  selectGame(game.id)
  await nextTick()
  hero.value?.focus({ preventScroll: true })
}
const running = computed(() => !!selectedInstance.value && isRunning(selectedInstance.value.id))
const hookFailed = computed(() => state.running.find(r => r.instanceId === selectedInstance.value?.id)?.hook?.state === 'failed')
const failedPluginNames = computed(() => state.running.find(r => r.instanceId === selectedInstance.value?.id)?.hook?.plugins
  .filter(plugin => !['installed', 'already-present', 'skipped-option', 'unavailable-build'].includes(plugin.outcome))
  .map(plugin => localized(state.plugins.find(item => item.id === plugin.id)?.name) || plugin.id).join(', ') ?? '')
const clientUpdate = computed(() => downloadTasks.value.find(task => !isTerminal(task) &&
  (task.target.kind === 'client' && matchesInstanceUpdate(task, state.selectedGameId, selectedInstance.value?.id, selectedBuild.value?.id) || pendingLaunch.value?.tasks.includes(task.id))))
const resourceProgress = ref<{ path: string; completed: number; total: number; phase: string; elapsedSeconds: number; completedArchives: number; totalArchives: number; percentage: number }>()
const resourceVisible = ref(false)
const resourceInstance = ref('')
let cancelledResourceInstance = ''
onScopeDispose(on('instances.resourceProgress', value => {
  const message = value as { instanceId: string; progress: typeof resourceProgress.value | null }
  if (message.instanceId !== selectedInstance.value?.id || message.progress && message.instanceId === cancelledResourceInstance) return
  resourceProgress.value = message.progress ?? undefined
  if (message.progress) { resourceVisible.value = true; resourceInstance.value = message.instanceId }
  else { resourceVisible.value = false; cancelledResourceInstance = '' }
}))
async function cancelResources() {
  cancelledResourceInstance = resourceInstance.value
  resourceVisible.value = false
  await call('instances.cancelResourcePreparation', { id: resourceInstance.value })
}
const launchPreparing = ref(false)
let launchTimeout: ReturnType<typeof setTimeout> | undefined
let launchAttempt = 0
let stopLaunchAttempt: ((timedOut: boolean) => void) | undefined
let launchCancellation: Promise<void> | undefined
const launchCancelling = ref(false)
let localAttempt = 0
const launchStarting = computed(() => localStarting.value || launchPreparing.value || state.running.find(r => r.instanceId === selectedInstance.value?.id)?.hook?.state === 'starting')
const pendingLaunch = ref<{ instanceId: string; serverId: string; username: string | undefined; tasks: string[] }>()
const pluginTransfers = ref<DownloadTask[]>([])
// Start first checks the servers: while any is under maintenance the player sees every server as a card and continues
// only when one of their section can be entered. A status that cannot be read never blocks the launch; the game server
// itself refuses entry during maintenance.
const maintenanceStatus = ref<ServerStatus | null>(null)
// Servers that cannot be entered when the launch continues; the gateway leaves them out of the server list.
let unavailableServers: number[] = []
let finishMaintenance: ((proceed: boolean) => void) | undefined
function answerMaintenance(proceed: boolean) { const resolve = finishMaintenance; finishMaintenance = undefined; maintenanceStatus.value = null; resolve?.(proceed) }
// The section the player starts in. It follows the signed-in account, except that sections sharing the default
// section's sign-in can all be played with the same account.
const playSection = ref<string | null>(null)
const currentAccountSection = () => accountSectionsShown() ? state.account?.section ?? null : defaultSection(serverStatus.value)
// Play here only selects another, still open section and the dialog stays open, so the launch goes on only when the
// player presses Continue. A section with the same sign-in keeps the account; a separate one switches to the saved
// account there (the same username first), or closes the dialog and opens the sign-in when none is saved.
const switchingSection = ref(false)
async function playInSection(section: string) {
  if (switchingSection.value) return
  const target = accountSection(section)
  if (target === currentAccountSection()) { playSection.value = section; return }
  const saved = state.accounts.find(entry => entry.account.section === target && entry.account.username === state.account?.username)
    ?? state.accounts.find(entry => entry.account.section === target)
  if (!saved) { answerMaintenance(false); pendingAuthSection.value = section; state.authOpen = true; return }
  switchingSection.value = true
  try { await selectAccount(saved.account.username, target); playSection.value = section }
  catch (error) { state.requestFailure = error as Error; state.error = (error as Error).message }
  finally { switchingSection.value = false }
}
async function checkServers(current: () => boolean): Promise<boolean> {
  if (!(inHost || !!demoMode?.enabled)) return true
  let status: ServerStatus
  unavailableServers = []
  try { status = await loadServerStatus() } catch { return current() }
  if (!current()) return false
  unavailableServers = status.servers.filter(server => server.state !== 'online').map(server => server.id)
  if (!status.sections.some(section => section.id === playSection.value) || accountSection(playSection.value, status) !== currentAccountSection())
    playSection.value = currentAccountSection()
  if (!status.servers.some(server => server.state === 'maintenance')) return true
  maintenanceStatus.value = status
  return await new Promise<boolean>(resolve => { finishMaintenance = resolve }) && current()
}
// Then the server's notices the player has not seen yet; each is marked seen once shown, so it appears only once.
const startNotices = ref<ServerNotice[]>([])
let finishNotices: (() => void) | undefined
function noticesDone() { const resolve = finishNotices; finishNotices = undefined; startNotices.value = []; resolve?.() }
async function showNotices(serverId: string, current: () => boolean): Promise<boolean> {
  if (!(inHost || !!demoMode?.enabled)) return true
  const section = playSection.value ?? state.account?.section ?? undefined
  let notices: ServerNotice[]
  try { notices = await call('servers.notices', { serverId, section }) } catch { return current() }
  if (!current() || !notices.length) return current()
  startNotices.value = notices
  await new Promise<void>(resolve => { finishNotices = resolve })
  try { await call('servers.markNoticesSeen', { serverId, section, ids: notices.map(notice => notice.id) }) } catch { /* shown again next time */ }
  return current()
}
// Show All lists every published notice as cards, from the notice popup or the button beside the server picker.
const allNotices = ref<{ open: boolean; loading: boolean; error: string; notices: ServerNotice[] }>({ open: false, loading: false, error: '', notices: [] })
let allNoticesRequest = 0
async function openAllNotices() {
  const serverId = state.selectedServerId, request = ++allNoticesRequest
  const section = playSection.value ?? state.account?.section ?? undefined
  allNotices.value = { open: true, loading: true, error: '', notices: [] }
  try {
    const notices = await call('servers.notices', { serverId, section, all: true })
    if (request !== allNoticesRequest) return
    allNotices.value = { ...allNotices.value, loading: false, notices }
    if (notices.length) void call('servers.markNoticesSeen', { serverId, section, ids: notices.map(notice => notice.id) }).catch(() => {})
  } catch (error) { if (request === allNoticesRequest) allNotices.value = { ...allNotices.value, loading: false, error: (error as Error).message } }
}
function closeAllNotices() { ++allNoticesRequest; allNotices.value = { ...allNotices.value, open: false } }
let finishTransfers: ((answer: 'wait' | 'now' | 'cancel') => void) | undefined, alive = true
function finishPluginTransfers(answer: 'wait' | 'now' | 'cancel') {
  const resolve = finishTransfers; finishTransfers = undefined; pluginTransfers.value = []; resolve?.(answer)
}
onScopeDispose(() => { alive = false; finishPluginTransfers('cancel'); finishRequiredUpdates(false); finishResourceConflicts(false); if (launchTimeout) clearTimeout(launchTimeout) })
watch(() => [selectedInstance.value?.id, state.selectedServerId, state.account?.username, account.value], () => { finishPluginTransfers('cancel'); finishRequiredUpdates(false) })
function waitForPlugins(tasks: DownloadTask[], instanceId: string, serverId: string) {
  if (tasks.length) pendingLaunch.value = { instanceId, serverId, username: state.account?.username, tasks: tasks.map(task => task.id) }
}
watch(() => [downloadTasks.value, selectedInstance.value?.id, state.selectedServerId, state.account?.username, launchPreparing.value] as const, () => {
  const pending = pendingLaunch.value
  if (!pending) return
  if (selectedInstance.value?.id !== pending.instanceId || state.selectedServerId !== pending.serverId || state.account?.username !== pending.username) {
    pendingLaunch.value = undefined; return
  }
  const tasks = pending.tasks.map(id => downloadTasks.value.find(task => task.id === id))
  if (tasks.some(task => !task || ['failed', 'canceled'].includes(task.stage))) { pendingLaunch.value = undefined; return }
  if (!launchPreparing.value && tasks.every(task => task?.stage === 'completed')) { pendingLaunch.value = undefined; void launch() }
})
const choices = computed(() => (selectedGame.value?.builds ?? []).flatMap(build => {
  const installed = gameInstances.value.filter(i => i.build === build.id)
  return installed.length ? installed.map(i => ({ value: `instance:${i.id}`, label: localized(build.name, { dates: true }), group: clientRegion(build.branch), caption: [clientVersionCaption(build), i.name].filter(Boolean).join(' · ') }))
    : [{ value: `build:${build.id}`, label: localized(build.name, { dates: true }), group: clientRegion(build.branch), caption: clientVersionCaption(build) }]
}))
const versionChoice = computed({ get: () => selectedInstance.value ? `instance:${selectedInstance.value.id}` : `build:${selectedBuild.value?.id ?? ''}`,
  set: (value: string) => { if (value.startsWith('instance:')) { state.selectedInstanceId = value.slice(9); state.selectedBuildId = selectedInstance.value?.build ?? '' } else { state.selectedInstanceId = ''; state.selectedBuildId = value.slice(6) } } })
const languageChoices = computed(() => clientLanguages.value.length ? clientLanguages.value.map(value => ({ value, label: isClientLanguageInstalled(value) ? languageNames[value] ?? value : t('clientLanguage.notInstalled', { language: languageNames[value] ?? value }) })) : [{ value: '', label: t('service.defaultLanguage') }])
const servers = computed(() => [...gameServers.value.filter(s => !s.localOnly).map(s => ({ value: s.id, label: s.name })),
  { value: 'action:local-play', label: t('localPlay.title'), disabled: selectedGame.value?.localHostingEnabled !== true || !(inHost || !!demoMode?.enabled) },
  { value: 'action:add-server', label: t('service.addServer') },
])
async function chooseServer(value: string) {
  if (value === 'action:local-play') {
    if (selectedGame.value?.localHostingEnabled !== true || !(inHost || !!demoMode?.enabled) || choosingLocal.value) return
    const game = state.selectedGameId, build = selectedBuild.value?.id
    choosingLocal.value = true
    try {
      const overview = await call('hosting.list')
      if (!alive || state.selectedGameId !== game || selectedBuild.value?.id !== build) return
      const profile = overview.profiles.find(p => p.game === game && p.build === build && p.mode === 'private')
      localProfile.value = profile
      const session = overview.sessions.find(s => s.profileId === profile?.id && s.state === 'ready')
      if (!profile || !session) { localPlayOpen.value = true; return }
      const destination = await call('hosting.destination', { id: profile.id, runId: session.runId })
      if (!alive || state.selectedGameId !== game || selectedBuild.value?.id !== build) return
      await refreshServers({ refreshSelectedServices: false })
      if (!alive || state.selectedGameId !== game || selectedBuild.value?.id !== build) return
      if (!state.servers.some(s => s.id === destination.id)) throw new Error(t('hosting.stopped'))
      state.selectedServerId = destination.id
    } catch (error) { state.requestFailure = error as Error; state.error = (error as Error).message }
    finally { choosingLocal.value = false }
  }
  else if (value === 'action:add-server') void router.push({ name: 'multiplayer', query: { tab: 'server' } })
  else {
    if (inHost || !!demoMode?.enabled) await call('hosting.releasePrivate', { keepServerId: value })
    state.selectedServerId = value
  }
}
const hasClient = computed(() => selectedInstance.value?.clientInstalled === true)
const clientPending = computed(() => !state.instancesLoaded && !state.clientWorkspaceCached)
const needLogin = computed(() => loginRequired.value && !state.account)
const loginConnecting = computed(() => hasClient.value && !running.value && loginPolicy.value !== 'none' && !state.account && (state.accountRestoring || state.serverLoading))
const hasPluginUpdates = computed(() => availableStoreUpdates.value.some(update => update.item.game === selectedGame.value?.id))
function openPluginUpdates() { void router.push({ name: 'store', query: { kind: 'plugin', updates: '1' } }) }
const pluginCount = computed(() => selectedInstance.value?.plugins.filter(p => p.enabled).length ?? 0)
async function chooseClientLanguage(language: string) {
  const plugin = selectedBuild.value?.additionalLanguages?.[language]
  if (plugin && !isClientLanguageInstalled(language)) {
    await router.push({ name: 'store', query: { kind: 'plugin', package: plugin } })
    return
  }
  await setClientLanguage(language)
}
async function openLocalSettings() {
  const overview = await call('hosting.list')
  localProfile.value = overview.profiles.find(p => `local-${p.id}` === state.selectedServerId)
  localPlayOpen.value = true
}
async function localSaved() {
  localPlayerName.value = localStorage.getItem('localPlay.playerName') || ''
  await refreshServices()
}
async function ensureLocalStarted(): Promise<boolean> {
  if (!selectedServer.value?.localOnly) return true
  const serverId = state.selectedServerId, instanceId = state.selectedInstanceId, id = serverId.slice(6)
  localStarting.value = true
  const attempt = ++localAttempt
  try {
    let overview = await call('hosting.list')
    if (attempt !== localAttempt) return false
    let session = overview.sessions.find(s => s.profileId === id && ['ready', 'starting'].includes(s.state))
    if (!session) session = await call('hosting.start', { id })
    const runId = session.runId, deadline = Date.now() + 60000
    while (session.state === 'starting') {
      if (Date.now() >= deadline) throw new Error(t('hosting.startup-timeout'))
      await new Promise(resolve => setTimeout(resolve, 300))
      if (!alive || attempt !== localAttempt || serverId !== state.selectedServerId || instanceId !== state.selectedInstanceId) { await call('hosting.releasePrivate', { keepServerId: state.selectedServerId }); return false }
      overview = await call('hosting.list')
      const exact = overview.sessions.find(s => s.profileId === id && s.runId === runId)
      if (!exact) throw new Error(t('hosting.stopped'))
      session = exact
    }
    if (attempt !== localAttempt) { await call('hosting.releasePrivate', {}); return false }
    if (session.state !== 'ready') throw new Error(t(`hosting.${session.failure ?? 'stopped'}`))
    await refreshServices()
    return alive && serverId === state.selectedServerId && instanceId === state.selectedInstanceId && !state.serverError
  } catch (e) { state.requestFailure = e as Error; state.error = (e as Error).message; return false }
  finally { localStarting.value = false }
}
function playerNameForLaunch() {
  return selectedServer.value?.kind === 'room' && loginPolicy.value === 'none'
    ? localPlayerName.value.trim() : account.value
}
async function launch() {
  if (!gameReady.value || !selectedInstance.value || launchPreparing.value) return
  cancelledResourceInstance = ''
  launchPreparing.value = true
  const attempt = ++launchAttempt
  // The start clock runs only while the client is really starting. Maintenance, notices, plugin recommendations,
  // warnings and conflict choices wait for the player, so they never count towards the start timeout.
  let deadline: number | undefined
  let stopped = false
  const valid = () => !stopped && alive && launchAttempt === attempt && launchPreparing.value && (deadline === undefined || Date.now() < deadline)
  const instanceId = selectedInstance.value.id, serverId = state.selectedServerId
  if (launchTimeout) { clearTimeout(launchTimeout); launchTimeout = undefined }
  const clock: LaunchClock = {
    start() {
      if (launchTimeout) clearTimeout(launchTimeout)
      deadline = Date.now() + 60000
      launchTimeout = setTimeout(() => stopLaunchAttempt?.(true), 60000)
      return deadline
    },
    pause() {
      if (launchTimeout) { clearTimeout(launchTimeout); launchTimeout = undefined }
      deadline = undefined
    },
  }
  const timeout = new Promise<void>(resolve => {
    stopLaunchAttempt = timedOut => {
      if (launchAttempt !== attempt || stopped) return
      stopped = true
      pendingLaunch.value = undefined
      finishPluginTransfers('cancel')
      finishRequiredUpdates(false)
      finishLaunchWarnings(false)
      finishResourceConflicts(false)
      dismissRecommendations()
      if (timedOut) state.error = t('launch.timeout')
      // Before the clock starts nothing is running in the host yet; stopping here only closes the open prompt.
      if ((inHost || !!demoMode?.enabled) && deadline !== undefined) {
        launchCancelling.value = true
        launchCancellation = call('instances.cancelLaunch', { id: instanceId, deadlineUnixMilliseconds: deadline }).then(() => {}).catch(error => {
          if (launchAttempt === attempt && !timedOut) { state.requestFailure = error as Error; state.error = (error as Error).message }
        }).finally(() => { launchCancelling.value = false })
      }
      resolve()
    }
  })
  try {
    const prepare = async () => {
      await ensurePluginScheme()
      if (valid() && instanceId === selectedInstance.value?.id && serverId === state.selectedServerId) await prepareLaunch(valid, clock, timeout)
    }
    await Promise.race([prepare(), timeout])
  } catch (error) { if (launchAttempt === attempt) { state.requestFailure = error as Error; state.error = (error as Error).message } } finally {
    if (launchAttempt === attempt) {
      launchPreparing.value = false
      stopLaunchAttempt = undefined
      if (launchTimeout) { clearTimeout(launchTimeout); launchTimeout = undefined }
    }
  }
}
interface LaunchClock { start(): number; pause(): void }
async function prepareLaunch(valid: () => boolean, clock: LaunchClock, timeout: Promise<void>) {
  if (!selectedInstance.value || !valid()) return
  const instance = selectedInstance.value, serverId = state.selectedServerId
  // Play here in the maintenance dialog may switch the account to another section, so the account this launch uses
  // is fixed only once that dialog is answered.
  if (!await checkServers(() => valid() && selectedInstance.value?.id === instance.id && state.selectedServerId === serverId)) return
  const launchAccount = playerNameForLaunch(), username = state.account?.username, section = state.account?.section
  const current = () => valid() && selectedInstance.value?.id === instance.id && state.selectedServerId === serverId && state.account?.username === username && state.account?.section === section && playerNameForLaunch() === launchAccount
  if (!await showNotices(serverId, current)) return
  try {
    if (!await requestRecommendations(instance.id, instance.name, serverId, true, current)) {
      if (current()) waitForPlugins(downloadTasks.value.filter(task => task.target.kind === 'plugin' && task.target.instanceId === instance.id && !isTerminal(task) && task.stage !== 'failed'), instance.id, serverId)
      return
    }
    if (!current()) return
    // An unmet plugin requirement or an enabled conflict blocks the launch until the user resolves it in the prompt.
    if (!await requestPluginPolicy(instance.id, { launching: true }, current)) return
    if (!current()) return
  } catch (error) { if (current()) { state.requestFailure = error as Error; state.error = (error as Error).message }; return }
  let confirmedWarnings: string[] | null
  try { confirmedWarnings = await requestLaunchWarnings(instance.id, serverId, current) }
  catch (error) { if (current()) { state.requestFailure = error as Error; state.error = (error as Error).message }; return }
  if (confirmedWarnings === null) return
  if (!current()) return
  let confirmedRequiredPluginUpdates: string[] = [], requiredTaskIds: string[] = []
  try {
    const answer = await requestRequiredUpdates(instance.id, serverId)
    if (!answer || !current()) return
    confirmedRequiredPluginUpdates = answer.keys; requiredTaskIds = answer.tasks
  } catch (error) { if (current()) { state.requestFailure = error as Error; state.error = (error as Error).message }; return }
  let confirmedPendingPlugins: string[] = []
  try {
    if (inHost || !!demoMode?.enabled) {
      const tasks = await call('plugins.launchTransfers', { instanceId: instance.id })
      if (!current()) return
      confirmedPendingPlugins = tasks.filter(task => requiredTaskIds.includes(task.id)).map(task => task.id)
      const remaining = tasks.filter(task => !requiredTaskIds.includes(task.id))
      if (remaining.length) {
        remaining.forEach(trackDownload); pluginTransfers.value = remaining
        const answer = await new Promise<'wait' | 'now' | 'cancel'>(resolve => { finishTransfers = resolve })
        if (!current() || answer === 'cancel') return
        if (answer === 'wait') { waitForPlugins(remaining, instance.id, serverId); return }
        confirmedPendingPlugins.push(...remaining.map(task => task.id))
      }
    }
  } catch (error) { if (current()) { state.requestFailure = error as Error; state.error = (error as Error).message }; return }
  const language = state.clientLanguage
  let cancelled = false
  if (!current()) return
  const success = await perform(() => Promise.race([(async () => {
    if (!current()) return
    clock.start()
    if (state.instancesLoaded && language && language !== instance.language) await call('instances.update', { id: instance.id, name: instance.name, endpoint: instance.endpoint, language, serverId: serverId || undefined })
    if (!current()) return
    // Each attempt gets a fresh minute; the clock stops while the player answers a resource conflict prompt.
    const result = await launchWithResourceChoices(resourceConflictChoices => {
      const deadline = clock.start()
      return call('instances.launch', { id: instance.id, serverId, section: state.account?.section ?? undefined, unavailableServers, account: launchAccount || undefined, confirmedWarnings, confirmedPendingPlugins, confirmedRequiredPluginUpdates, resourceConflictChoices, launcherLanguage: locale.value, deadlineUnixMilliseconds: deadline })
        .catch(error => { clock.pause(); throw error })
    }, current)
    if (!current()) return
    cancelled = 'cancelled' in result || 'timeout' in result
    if ('timeout' in result) state.error = t('launch.timeout')
  })(), timeout]))
  if (success && !cancelled && current()) { loginOpen.value = false; account.value = '' }
}
async function cancelStarting() {
  if (launchCancelling.value) return
  launchCancelling.value = true
  try {
    stopLaunchAttempt?.(false)
    await launchCancellation
    if (localStarting.value) {
      localAttempt++
      await call('hosting.releasePrivate', {})
      localStarting.value = false
    }
    const client = state.running.find(item => item.instanceId === selectedInstance.value?.id && item.hook?.state === 'starting')
    if (client) await call('instances.cancelLaunch', { id: client.instanceId, processId: client.processId })
  } catch (error) { state.requestFailure = error as Error; state.error = (error as Error).message }
  finally { launchCancelling.value = false }
}
async function requestLaunch() {
  if (launchCancelling.value) return
  if (launchStarting.value) { await cancelStarting(); return }
  if (localStarting.value || launchPreparing.value || pendingLaunch.value || loginConnecting.value) return
  if (running.value && selectedInstance.value) { requestStop(selectedInstance.value.id); return }
  if (nameOnly.value && !validNickname.value) { nicknameOpen.value = true; return }
  if (selectedServer.value?.localOnly && !await ensureLocalStarted()) return
  if (needLogin.value) { state.authOpen = true; return }
  if (nameOnly.value) { if (!validNickname.value) { nicknameOpen.value = true; return }; void launch(); return }
  if (!state.account && selectedBuild.value?.requiresAccount) { account.value = ''; loginOpen.value = true }
  else void launch()
}
</script>

<template>
  <section class="page home-page">
    <header class="page-header"><div><h1 class="page-title">{{ t('nav.home') }}</h1><p class="page-desc">{{ t('live.subtitle') }}</p></div>
      <!-- <span class="library-label"><span class="status-dot" />{{ t('live.local') }}</span> -->
    </header>
    <PageBody contained class="home-workspace">

    <div class="game-surface" :style="{ '--title-offset': `${titleOffset}px` }">
      <div ref="hero" class="game-hero" :class="{ 'has-navigation': homeGames.length > 1, nsfw: prefs.nsfw }" :style="{ '--hero-accent': selectedGame?.accent || '#265c78' }"
        role="group" :aria-label="t('game.switch')" tabindex="0" @keydown.left.prevent="switchGame(-1)" @keydown.right.prevent="switchGame(1)">
        <div v-if="!prefs.nsfw" class="hero-media-slot"><Transition name="hero-media" mode="out-in"><GameHeroMedia :key="`${selectedGame?.id}:${videos.join('|')}`" :sources="videos" :image="selectedGame?.backgroundImage" :audio-tracks="selectedGame?.backgroundAudioTracks" :accent="selectedGame?.accent || '#265c78'" :disabled="prefs.nsfw" /></Transition></div>
        <div class="hero-wash" />
        <div class="hero-copy" aria-live="polite" aria-atomic="true"><Transition name="hero-title" mode="out-in"><div :key="selectedGame?.id"><span class="hero-eyebrow" lang="en">{{ selectedGame?.heroTitle || englishName }}</span><h2 :lang="displayName === selectedGame?.name.ko ? 'ko' : undefined">{{ displayName }}</h2><p>{{ gamePreparing ? t('live.gameComingSoon') : t('live.subtitle') }}</p></div></Transition></div>
        <button v-if="previousGame" type="button" class="hero-arrow previous" :disabled="state.busy"
          :aria-label="t('live.previousGame', { name: localized(previousGame.name) })" :title="localized(previousGame.name)" @click="switchGame(-1)"><FluentIcon glyph="chevronLeft" :size="16" /></button>
        <button v-if="nextGame" type="button" class="hero-arrow next" :disabled="state.busy"
          :aria-label="t('live.nextGame', { name: localized(nextGame.name) })" :title="localized(nextGame.name)" @click="switchGame(1)"><FluentIcon glyph="chevronRight" :size="16" /></button>
      </div>
      <div class="home-controls" :style="{ height: controlsHeight === undefined ? undefined : `${controlsHeight}px` }"><div ref="controls">
      <div v-if="gamePreparing" class="first-launch"><div><h3>{{ t('live.gameComingSoon') }}</h3><p>{{ t('live.gamePending') }}</p></div><FluentIcon glyph="clock" :size="22" /></div>
      <div v-else class="home-connection">
        <div class="connection-selectors">
          <div class="connection-row"><FluentIcon glyph="gamepad" :size="20" /><label class="connection-field"><span class="field-label">{{ t('live.build') }}</span><WComboBox subtle-selection class="client-version-picker" v-model="versionChoice" :options="choices" :placeholder="t(clientPending ? 'live.clientLoading' : 'live.selectInstance')" :disabled="state.busy || !choices.length" :aria-label="t('live.selectInstance')" /></label></div>
        </div>
        <div class="launch-action">
          <WButton class="launch-button" :class="{ 'is-starting': launchStarting, 'is-connecting': loginConnecting }" variant="accent" :aria-busy="launchStarting || loginConnecting"
            :disabled="launchCancelling || (!launchStarting && (clientPending || loginConnecting || state.busy || pluginSchemesBusy.value || !!pendingLaunch || (!running && !!clientUpdate) || (!running && buildNotServed)))"
            @click="running ? requestLaunch() : !hasClient ? (addOpen = true) : requestLaunch()">
            <WProgressRing v-if="launchStarting || loginConnecting || (clientPending && state.instancesLoading)" class="launch-spinner" :size="16" />
            <FluentIcon v-else :glyph="running ? 'completed' : clientUpdate ? 'download' : !hasClient ? 'add' : needLogin ? 'contact' : 'play'" :size="16" />
            {{ launchStarting ? t('launch.starting') : running ? t('live.running') : loginConnecting ? t('service.connecting') : clientUpdate ? t(`updates.${clientUpdate.stage}`) : !hasClient ? t(clientPending ? 'live.clientLoading' : 'live.add') : needLogin ? t('service.login') : t('home.launch') }}
          </WButton>
        </div>
      </div>
      <p v-if="buildNotServed && !running" class="build-not-served" role="status">{{ t('live.buildNotServed') }}</p>
      <ClientUpdateStatus v-if="selectedInstance && !clientUpdate" :instance-id="selectedInstance.id" compact />
      <Transition name="resource-progress"><div v-if="resourceVisible && resourceProgress" class="resource-progress-slot"><div><HomeResourceProgress :progress="resourceProgress" @cancel="cancelResources" /></div></div></Transition>
      <HomeUpdate />
      <div v-if="hookFailed" class="server-error" role="alert"><span>{{ failedPluginNames ? t('launch.pluginsFailed', { plugins: failedPluginNames }) : t(selectedInstance?.diagnosticsEnabled ? 'launch.hookFailed' : 'launch.failed') }}</span><WButton v-if="selectedInstance?.diagnosticsEnabled" variant="subtle" @click="call('instances.openDiagnostics', { id: selectedInstance!.id })">{{ t('launch.diagnostics') }}</WButton></div>
      <div v-if="!gamePreparing" class="home-service-strips">
        <div class="service-strip server-account-strip">
          <div class="server-group"><FluentIcon glyph="globe" :size="18" /><span class="strip-label">{{ t('home.server') }}</span><WComboBox fit-content class="strip-picker" :model-value="selectedServer?.localOnly ? 'action:local-play' : state.selectedServerId" :options="servers" @update:model-value="chooseServer" :disabled="state.busy" :placeholder="t('service.selectServer')" :aria-label="t('service.selectServer')" /><WButton v-if="selectedServer?.localOnly" variant="subtle" :disabled="state.busy || localStarting" :aria-label="t('hosting.edit')" @click="openLocalSettings"><FluentIcon glyph="settings" /></WButton><WButton v-else-if="selectedServer?.apiUrl" variant="subtle" :title="t('notices.allTitle')" :aria-label="t('notices.allTitle')" @click="openAllNotices"><FluentIcon glyph="info" /></WButton></div>

          <div class="language-group"><FluentIcon glyph="font" :size="18" /><span class="strip-label">{{ t('service.clientLanguage') }}</span><WComboBox fit-content class="strip-picker" :model-value="state.clientLanguage" :options="languageChoices" :disabled="!state.instancesLoaded || state.busy || running || !clientLanguages.length" :aria-label="t('service.clientLanguage')" @update:model-value="chooseClientLanguage" /></div>
          <div v-if="nameOnly" class="account-group local-name"><span class="strip-label">{{ t('localPlay.playerName') }}</span><WTextBox v-model="localPlayerName" :maxlength="32" :aria-label="t('localPlay.playerName')" :disabled="state.busy || running" /></div>
          <div v-else-if="loginPolicy !== 'none'" class="account-group"><AccountPicker /></div>
        </div>
        <div v-if="state.serverError" class="server-error" role="status"><span>{{ state.serverError }}</span><WButton variant="subtle" :disabled="state.serverLoading" @click="refreshServices">{{ t('live.refresh') }}</WButton></div>
      </div>
      </div></div>
    </div>
    <div class="home-secondary">
    <div v-if="selectedGame?.serviceNotice" class="service-notice" role="note"><FluentIcon glyph="info" :size="16" /><MarkdownText :source="localized(selectedGame.serviceNotice)" /></div>


    <div v-if="!gamePreparing && (navigationEntries.includes('instances') || navigationEntries.includes('plugins'))" class="section-heading"><h2>{{ t('live.library') }}</h2><WButton v-if="navigationEntries.includes('instances')" variant="subtle" @click="router.push('/instances')">{{ t('live.manage') }}<FluentIcon glyph="chevronRight" :size="10" /></WButton></div>
    <div v-if="!gamePreparing && (navigationEntries.includes('instances') || navigationEntries.includes('plugins'))" class="home-links">
      <button v-if="navigationEntries.includes('instances')" class="shortcut-card" @click="router.push('/instances')"><span class="shortcut-icon"><FluentIcon glyph="gamepad" :size="22" /></span><div><h3>{{ t('nav.instances') }}<span class="count">{{ gameInstances.length }}</span></h3><p>{{ t('live.manageBody') }}</p></div><FluentIcon glyph="chevronRight" :size="12" /></button>
      <div v-if="navigationEntries.includes('plugins')" class="shortcut-card plugin-shortcut" @click="router.push('/plugins')">
        <button type="button" class="plugin-shortcut-main"><span class="shortcut-icon"><FluentIcon glyph="puzzle" :size="22" /></span><div><h3>{{ t('nav.plugins') }}<span class="count">{{ pluginCount }}</span><span v-if="hasPluginUpdates" class="plugin-update-badge">{{ t('store.updateAvailable') }}</span></h3><p>{{ t('live.pluginBody') }}</p></div></button>
        <PluginSchemePicker compact :disabled="launchPreparing" @click.stop /><WButton v-if="hasPluginUpdates" class="plugin-update-action" @click.stop="openPluginUpdates">{{ t('store.goToUpdates') }}</WButton>
        <FluentIcon glyph="chevronRight" :size="12" />
      </div>
    </div>
    <!-- <div class="home-footer"><FluentIcon glyph="info" :size="14" /><span>{{ inHost ? t('live.settingsNote') : t('live.browserOnly') }}</span></div> -->
    <!-- <div v-if="inHost" class="home-footer"><FluentIcon glyph="info" :size="14" /><span>{{ t('live.settingsNote') }}</span></div> -->
    </div>
    <WDialog :open="nicknameOpen" :title="t('localPlay.enterNickname')" :primary-text="t('home.launch')" :close-text="t('dialog.cancel')" :primary-disabled="!validNickname || state.busy" @primary="nicknameOpen = false; requestLaunch()" @close="nicknameOpen = false">
      <WTextBox v-model="localPlayerName" :maxlength="32" :placeholder="t('localPlay.enterNickname')" :aria-label="t('localPlay.playerName')" @keydown.enter="validNickname && (nicknameOpen = false, requestLaunch())" />
      <p v-if="localPlayerName && !validNickname">{{ t('localPlay.playerNameInvalid') }}</p>
    </WDialog>
    <LocalServerSetupDialog :open="localPlayOpen" :initial-build="selectedBuild?.id" :profile="localProfile" default-scope="private" select-after-save @saved="localSaved" @close="localPlayOpen = false" />
    <InstanceDialog :open="addOpen" :initial-build="selectedBuild?.id" @close="addOpen = false" />
    <ServerMaintenanceDialog :status="maintenanceStatus" :section="playSection" :switching="switchingSection" @continue="answerMaintenance(true)" @close="answerMaintenance(false)" @switch="playInSection" />
    <ServerNoticeDialog :notices="startNotices" @done="noticesDone" @show-all="openAllNotices" />
    <ServerNoticeListDialog :open="allNotices.open" :notices="allNotices.notices" :loading="allNotices.loading" :error="allNotices.error" @close="closeAllNotices" />
    <WDialog :open="pluginTransfers.length > 0" :title="t('pluginUpdates.launchPendingTitle')"
      :primary-text="t('pluginUpdates.waitAndLaunch')" :secondary-text="t('pluginUpdates.launchNow')" :close-text="t('dialog.cancel')" primary-first
      @primary="finishPluginTransfers('wait')" @secondary="finishPluginTransfers('now')" @close="finishPluginTransfers('cancel')">
      <p>{{ t('pluginUpdates.launchPendingBody') }}</p>
      <InstallationTaskList :tasks="pluginTransfers" />
      <p class="muted">{{ t('pluginUpdates.launchPendingNote') }}</p>
    </WDialog>
    <WDialog :open="loginOpen" :title="t('home.launch')" :primary-text="t('home.launch')" :close-text="t('dialog.cancel')" :primary-disabled="!account.trim() || state.busy" :close-disabled="state.busy" @primary="launch" @close="loginOpen = false; account = ''">
      <div class="form-stack"><p class="muted">{{ t('live.accountHint') }}</p><label class="field"><span>{{ t('accounts.username') }}</span><WTextBox v-model="account" autocomplete="off" :disabled="state.busy" /></label></div>
    </WDialog>
  </PageBody>
  </section>
</template>

<style scoped>
.resource-progress-slot { display: grid; grid-template-rows: 1fr; opacity: 1; }
.resource-progress-slot > div { min-height: 0; overflow: hidden; }
.resource-progress-enter-active, .resource-progress-leave-active { transition: opacity 200ms ease, grid-template-rows 260ms var(--ease-fast-out-slow-in); }
.resource-progress-enter-from, .resource-progress-leave-to { opacity: 0; grid-template-rows: 0fr; }
@media (prefers-reduced-motion: reduce) { .resource-progress-enter-active, .resource-progress-leave-active { transition: none; } }
.local-name { display: flex; align-items: center; gap: 10px; }.local-name :deep(.w-textbox) { min-width: 0; max-width: 200px; }
.home-page { max-width: 1440px; margin: 0 auto; }
.home-workspace :deep(.page-scroll) { display: grid; grid-template-rows: minmax(0, 1fr) 156px; gap: 12px; overflow: hidden; }
.home-secondary { min-height: 0; overflow: auto; }
.home-secondary .section-heading { margin-top: 0; }
.home-secondary .service-notice { margin-top: 0; }
.home-secondary .home-footer { padding-top: 16px; }
.library-label { display: flex; align-items: center; gap: 7px; color: var(--text-secondary); font-size: 12px; padding-bottom: 5px; }
.status-dot { width: 6px; height: 6px; background: var(--accent-default); border-radius: 50%; }
.game-surface { flex: 1; min-height: 0; display: flex; flex-direction: column; border: 1px solid var(--card-stroke-default); border-radius: 8px; overflow: hidden; background: var(--card-bg-default); }
.game-surface > :not(.game-hero) { flex: none; }
/* The update panel owns its reveal animation. Let the controls container follow
   that layout change immediately so the rows below move down in one direction
   instead of being animated a second time by the parent. */
.home-controls { transition: none; }
.game-hero { flex: 1; min-height: 0; position: relative; overflow: hidden; background: radial-gradient(ellipse at 95% 0%, color-mix(in srgb, var(--hero-accent) 65%, white) 0, transparent 65%), linear-gradient(110deg, color-mix(in srgb, var(--hero-accent) 28%, #10263b), var(--hero-accent)); }
.game-hero:focus-visible { outline-color: var(--accent-default); outline-offset: -3px; }
.hero-media-slot { position: absolute; inset: 0; }
.game-hero.nsfw { background: var(--solid-bg-tertiary); }
.nsfw .hero-wash { display: none; }
.nsfw .hero-copy { color: var(--text-primary); }
.nsfw .hero-eyebrow, .nsfw .hero-copy p { color: var(--text-secondary); }
.nsfw .hero-arrow { color: var(--text-primary); border-color: var(--card-stroke-default); background: var(--subtle-secondary); backdrop-filter: none; }
.nsfw .hero-arrow:hover:not(:disabled) { background: var(--subtle-tertiary); }
.nsfw .hero-arrow:focus-visible { outline-color: var(--accent-default); }
.hero-media-enter-active, .hero-media-leave-active { transition: opacity 320ms cubic-bezier(0.2, 0, 0, 1); }
.hero-media-enter-from, .hero-media-leave-to { opacity: 0; }
.hero-title-enter-active, .hero-title-leave-active { transition: opacity 140ms ease; }
.hero-title-enter-from, .hero-title-leave-to { opacity: 0; }
.hero-wash { position: absolute; inset: 0; background: linear-gradient(90deg, #112d49ad, #112d4900 85%); pointer-events: none; }
.hero-copy { position: absolute; inset: auto 32px calc(58px + var(--title-offset, 0px)); transition: bottom 320ms cubic-bezier(0.2, 0, 0, 1); color: #fff; pointer-events: none; }
.has-navigation .hero-copy { left: 72px; right: 72px; }
.hero-arrow { position: absolute; top: 50%; transform: translateY(-50%); width: 36px; height: 44px; border: 1px solid #ffffff40; border-radius: 6px; display: flex; align-items: center; justify-content: center; background: #15233166; color: white; backdrop-filter: blur(12px); transition: background-color var(--duration-faster) linear; }
.hero-arrow.previous { left: 16px; }
.hero-arrow.next { right: 16px; }
.hero-arrow:hover:not(:disabled) { background: #152331a6; }
.hero-arrow:active:not(:disabled) { background: #152331cc; }
.hero-arrow:focus-visible { outline: 2px solid white; outline-offset: 2px; }
.hero-arrow:disabled { opacity: 0.45; }
.hero-eyebrow { font-size: 13px; color: #ffffffb8; letter-spacing: 1.2px; }
.hero-copy h2 { margin: 8px 0; font-size: 36px; font-weight: 600; line-height: 1.3; }
.hero-copy p { margin: 0; color: #ffffffcc; }
.launch-bar { display: flex; align-items: end; gap: 24px; padding: 20px 24px; }
.launch-picker { flex: 1; max-width: 290px; min-width: 170px; }
.field-label { display: block; color: var(--text-secondary); font-size: 12px; margin-bottom: 6px; }
.launch-details { min-width: 0; flex: 1; }
.endpoint-link { height: 32px; display: flex; align-items: center; gap: 8px; max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.endpoint-link:hover { color: var(--accent-text-primary); }
.launch-button { min-width: 148px; min-height: 40px; align-self: center; }
.launch-button.is-starting:disabled, .launch-button.is-connecting:disabled { --w-btn-border: var(--ctrl-elevation-border-accent); background-color: var(--accent-default); color: var(--text-on-accent-primary); }
.launch-spinner :deep(.w-ring-arc) { stroke: currentColor; }
.first-launch { box-sizing: border-box; min-height: 48px; display: flex; align-items: center; justify-content: space-between; gap: 24px; padding: 12px 24px; }
.first-launch > div { display: flex; align-items: center; gap: 16px; min-width: 0; }
.first-launch h3 { margin: 0; font-size: 14px; font-weight: 600; flex: none; }
.first-launch p { color: var(--text-secondary); margin: 0; font-size: 12px; }
.home-server { display: flex; align-items: center; flex-wrap: wrap; gap: 10px; padding: 14px 24px; border-top: 1px solid var(--card-stroke-default); color: var(--text-secondary); font-size: 12px; }
.home-server button { display: inline-flex; align-items: center; gap: 8px; color: var(--text-primary); }
.home-server button:hover { color: var(--accent-text-primary); }
.service-notice { display: flex; align-items: center; gap: 10px; padding: 16px; margin-top: 16px; border-radius: 8px; border: 1px solid var(--card-stroke-default); background: var(--card-bg-default); color: var(--text-secondary); }
.service-notice p { margin: 0; }
.service-notice > .fluent-icon { flex: none; }
.section-heading { flex: none; display: flex; align-items: center; justify-content: space-between; margin: 12px 0 8px; }
.section-heading h2 { font-size: 16px; font-weight: 600; margin: 0; }
.home-links { flex: none; display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
.shortcut-card { display: flex; align-items: center; gap: 16px; padding: 14px 20px; text-align: left; background: var(--card-bg-default); border: 1px solid var(--card-stroke-default); border-radius: 8px; }
.plugin-shortcut { cursor: pointer; }
.plugin-shortcut-main { flex: 1; min-width: 0; display: flex; align-items: center; gap: 16px; text-align: left; padding: 0; color: inherit; }
.plugin-shortcut-main h3 { flex-wrap: wrap; }
.plugin-update-badge { padding: 1px 7px; border-radius: 4px; background: color-mix(in srgb, var(--accent-default) 10%, var(--card-bg-default)); color: var(--accent-default); font-size: 11px; line-height: 18px; font-weight: 400; }
.plugin-update-action { flex: none; }
.shortcut-card:hover { background: var(--ctrl-fill-secondary); }
.shortcut-card:active { background: var(--ctrl-fill-tertiary); }
.shortcut-icon { color: var(--text-secondary); }
.shortcut-card > div { flex: 1; }
.shortcut-card h3 { font-size: 14px; font-weight: 600; margin: 0 0 6px; display: flex; align-items: center; gap: 8px; }
.shortcut-card p { color: var(--text-secondary); font-size: 12px; line-height: 18px; margin: 0; }
.count { font-size: 11px; font-weight: 400; border-radius: 4px; min-width: 18px; text-align: center; padding: 0 4px; background: var(--subtle-secondary); color: var(--text-secondary); }
.home-footer { display: flex; align-items: center; gap: 8px; color: var(--text-tertiary); font-size: 12px; padding: 22px 2px 0; }
@media (max-width: 1000px) { .launch-bar { gap: 16px; } .launch-details { max-width: 220px; } }
@media (max-width: 640px) {
  .library-label { display: none; }
  .hero-copy { inset: auto 20px calc(58px + var(--title-offset, 0px)); }
  .has-navigation .hero-copy { left: 58px; right: 58px; }
  .hero-arrow.previous { left: 10px; }
  .hero-arrow.next { right: 10px; }
  .hero-copy h2 { font-size: 28px; }
  .first-launch, .launch-bar { flex-direction: column; align-items: stretch; padding: 20px; }
  .first-launch { flex-direction: row; padding: 12px 20px; }
  .first-launch > div { align-items: flex-start; flex-direction: column; gap: 4px; }
  .launch-picker, .launch-details { max-width: none; }
  .launch-button { align-self: stretch; }
  .home-links { grid-template-columns: 1fr; }
  .section-heading { flex-wrap: wrap; gap: 8px; }
  .home-footer { align-items: start; }
}
.home-connection { display: flex; align-items: center; gap: 24px; padding: 14px 24px; }
.connection-selectors { flex: 1; min-width: 0; }
.connection-row { display: flex; align-items: center; gap: 16px; min-height: 36px; min-width: 0; }
.connection-row > .fluent-icon { color: var(--text-secondary); flex: none; }
.connection-field { display: grid; grid-template-columns: auto minmax(0, 1fr); align-items: center; gap: 16px; flex: 1; min-width: 0; }
.connection-field .field-label { margin: 0; font-size: 13px; }
.home-connection .connection-field { max-width: 460px; }
.client-version-picker :deep(.w-combobox) { height: 48px; }
.client-version-picker :deep(.w-combobox-caption) { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.launch-action { display: flex; align-items: center; justify-content: flex-end; flex: none; }
.service-strip { display: flex; align-items: center; gap: 10px; min-height: 48px; padding: 8px 24px; border-top: 1px solid var(--card-stroke-default); font-size: 12px; }
.service-strip > .fluent-icon, .strip-label { color: var(--text-secondary); flex: none; }
.service-strip :deep(.w-combobox) { width: auto; max-width: 100%; min-width: 0; background: transparent; font-size: 12px; padding: 4px 6px; }
.service-strip :deep(.w-combobox::before) { display: none; }
.service-strip :deep(.w-combobox:hover:not(:disabled)) { background: var(--subtle-secondary); }
.service-strip :deep(.w-flyout-anchor) { min-width: 0; }
.service-strip :deep(.w-combobox-chevron) { font-size: 10px !important; }
.server-account-strip { gap: 12px 24px; flex-wrap: wrap; }
.server-group { display: flex; align-items: center; gap: 10px; min-width: 0; }
.server-group > .fluent-icon { color: var(--text-secondary); }
.account-group, .language-group { min-width: 0; padding-left: 24px; border-left: 1px solid var(--card-stroke-default); }
.language-group { display: flex; align-items: center; gap: 10px; }
.language-group > .fluent-icon { color: var(--text-secondary); }
.account-trigger { display: block; max-width: 100%; padding: 4px 6px; min-height: 32px; border-radius: var(--radius-control); color: var(--text-primary); text-align: left; }
.account-trigger:hover:not(:disabled) { background: var(--subtle-secondary); }
.account-trigger:active:not(:disabled) { background: var(--subtle-tertiary); }
.account-trigger:disabled { opacity: 0.5; }
.account-trigger :deep(.account-summary) > .fluent-icon { color: var(--text-secondary); margin-left: 2px; }
@media (max-width: 700px) {
  .server-account-strip { gap: 12px; }
  .server-group { flex: 1; gap: 6px; }
  .server-group :deep(.w-flyout-anchor) { flex: 1; min-width: 0; }
  .server-group :deep(.w-combobox) { width: 100%; }
  .account-group, .language-group { flex: none; padding-left: 12px; }
  .account-group :deep(.account-summary), .account-group :deep(.account-identity), .account-group :deep(.account-identity > div) { gap: 6px; }
}
.server-error { display: flex; align-items: center; gap: 16px; padding: 12px 24px; border-top: 1px solid var(--card-stroke-default); font-size: 12px; color: var(--text-secondary); }
.server-error > span { flex: 1; overflow-wrap: anywhere; }
.build-not-served { margin: 0; padding: 8px 24px 0; font-size: 12px; color: var(--text-secondary); text-align: right; }
@media (max-width: 700px) {
  .home-connection { gap: 16px; padding: 18px 20px; }
  .home-connection .connection-row { gap: 10px; }
  .home-connection .connection-row > .fluent-icon { display: none; }
  .home-connection .connection-field { grid-template-columns: minmax(0, 1fr); gap: 6px; }
  .launch-action .launch-button { min-width: 120px; }
  .service-strip { padding: 8px 20px; }
  .connection-field { gap: 12px; }
}
/* Compact selectors retain readable labels before the connection row wraps. */
.server-group { flex: 0 1 280px; min-width: min(100%, 240px); }
.server-group :deep(.w-flyout-anchor) { flex: 1; min-width: 150px; }
.service-strip .server-group :deep(.w-combobox) { width: 100%; }
.language-group { flex: 0 0 auto; }
.language-group :deep(.w-flyout-anchor) { flex: none; min-width: 112px; }
.service-strip .language-group :deep(.w-combobox) { width: 100%; }
.account-group { flex: 1 1 300px; }
.account-group :deep(.w-flyout-anchor) { display: block; width: 100%; }
.account-group :deep(.account-summary.compact) { flex-wrap: nowrap; }
.account-group :deep(.account-identity) { flex: 1 1 auto; min-width: 0; }
.account-group :deep(.account-identity > div) { min-width: 0; }
.account-group :deep(.account-label) { flex: none; }
.account-group :deep(.account-identity strong) { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.account-group :deep(.account-stats) { flex: none; flex-wrap: nowrap; }
@media (max-width: 760px) {
  .server-group { flex: 1 1 100%; max-width: none; }
  .language-group { padding-left: 0; border-left: 0; }
  .account-group { padding-left: 16px; }
}
@media (max-width: 440px) {
  .language-group { padding-left: 0; border-left: 0; }
}
</style>

<style scoped>
.resource-preparation { display: grid; gap: 12px; }.resource-preparation p { margin: 0; color: var(--text-secondary); font-size: 12px; }.resource-path { overflow-wrap: anywhere; }
.local-name { display: flex; align-items: center; gap: 10px; }.local-name :deep(.w-textbox) { min-width: 0; max-width: 200px; }
@media (prefers-reduced-motion: reduce) { .home-controls, .hero-copy { transition: none; } }
</style>

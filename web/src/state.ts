// Shared state contains only host data and user preferences.
//
// State Ownership & Lifetime:
// 1. Host snapshots: `state` holds installed games, server lists, account display data
//    and client execution states; downloads and store browsing have separate owners.
// 2. Optimistic Workspace Restoration: `readClientWorkspace()` hydrates instances and games
//    from localStorage so views can render a display-only snapshot before the
//    native bridge completes its initial discovery scan.
// 3. Selection Synchronization: Switching games automatically switches the persisted instance
//    and server selection keys (`clientSelection.<game>` and `server.<game>`), preserving
//    user choices across games without leaking cross-game references.
// 4. Context changes clear displayed account state and invalidate pending results.
//    Authenticated transport and renewal ownership remain in the host.
import { computed, reactive, watch } from 'vue'
import { call, demoMode, inHost, on, type PluginRecoveryNotice, type LaunchCleanupFailure, type IpPreference, type Server, type ServerCatalog, type AccountProfile, type AccountSessionInfo, type GameSummary, type BuildSummary, type Instance, type PluginSummary, type RunningClient } from './bridge'
import { prefs } from './theme'
import { supportedClientLanguages, clientLanguageInstalled } from './clientLanguages'
import { accountExpiryDelay, liveAccountSessions } from './accounts/expiry'
import { resolveLoginPolicy } from './accounts/login-policy'
import { i18n } from './i18n'
import { clientFailureCode } from './launchFailure'
import { captureSchemeInstallation, planStoreInstallation, rememberStoreInstallation, cancelStoreInstallation, cloneSelection, defaultScheme, emptyLibrary, readLibrary, removeRecoveredRelease, schemeContext, type PluginScheme, type SchemeLibrary } from './plugins/schemes'
import type { StorePackage } from './store/types'

type CachedBuilds = { game: string; builds: BuildSummary[] }
function cachedBuild(build: BuildSummary): boolean {
  return !!build && typeof build.id === 'string' && typeof build.branch === 'string' &&
    (build.buildDate == null || typeof build.buildDate === 'string') && !!build.name &&
    typeof build.name === 'object' && Object.values(build.name).every(value => typeof value === 'string')
}
function readClientWorkspace(): { instances: Instance[]; builds: CachedBuilds[]; games: GameSummary[]; available: boolean } {
  try {
    const saved = JSON.parse(localStorage.getItem('clientWorkspace') ?? '{}')
    const validInstances = Array.isArray(saved.instances) && saved.instances.every((item: Instance) => item &&
      [item.id, item.game, item.build, item.name].every(value => typeof value === 'string') && typeof item.clientInstalled === 'boolean')
    const games: GameSummary[] = Array.isArray(saved.games) ? saved.games.filter((game: GameSummary) => game &&
      typeof game.id === 'string' && ['enabled', 'placeholder'].includes(game.status) && game.name &&
      typeof game.name === 'object' && Object.values(game.name).every(value => typeof value === 'string') &&
      Array.isArray(game.clientLanguages) && game.clientLanguages.every(value => typeof value === 'string') &&
      Array.isArray(game.builds) && game.builds.every(cachedBuild)) : []
    const builds = Array.isArray(saved.builds) ? saved.builds.filter((group: CachedBuilds) => group &&
      typeof group.game === 'string' && Array.isArray(group.builds) && group.builds.every(cachedBuild)) : games.map(game => ({ game: game.id, builds: game.builds }))
    return {
      instances: validInstances ? saved.instances.map((item: Instance) => ({ id: item.id, game: item.game, build: item.build, name: item.name,
        clientInstalled: item.clientInstalled, language: typeof item.language === 'string' ? item.language : null, endpoint: null, plugins: [] })) : [],
      games, builds, available: validInstances && builds.length > 0,
    }
  } catch { return { instances: [], builds: [], games: [], available: false } }
}
function readClientSelection(game: string, fallback = ''): { instanceId: string; buildId: string } {
  try {
    const saved = JSON.parse(localStorage.getItem(`clientSelection.${game}`) ?? '{}')
    return { instanceId: typeof saved.instanceId === 'string' ? saved.instanceId : fallback, buildId: typeof saved.buildId === 'string' ? saved.buildId : '' }
  } catch { return { instanceId: fallback, buildId: '' } }
}
const cachedWorkspace = readClientWorkspace()
const initialGame = localStorage.getItem('game') ?? 'bubblefighter'
const initialSelection = readClientSelection(initialGame, localStorage.getItem('instance') ?? '')
const initialServer = localStorage.getItem(`server.${initialGame}`) ?? ''

export const state = reactive({
  games: cachedWorkspace.games, instances: cachedWorkspace.instances, plugins: [] as PluginSummary[], running: [] as RunningClient[],
  selectedGameId: initialGame,
  selectedInstanceId: initialSelection.instanceId,
  selectedBuildId: initialSelection.buildId || cachedWorkspace.instances.find(item => item.id === initialSelection.instanceId)?.build || '',
  instancesLoaded: false, instancesLoading: true, clientWorkspaceCached: cachedWorkspace.available,
  servers: [] as Server[], catalog: null as ServerCatalog | null, account: null as AccountProfile | null,
  accounts: [] as AccountSessionInfo[],
  accountsLoading: false, accountRestoring: true,
  selectedServerId: initialServer, serverLoading: false, serverError: '', authOpen: false,
  clientLanguage: '', ipPreference: 'china' as IpPreference,
  loaded: false, busy: false, error: '', requestFailure: undefined as Error | undefined,
  launchFailure: undefined as LaunchCleanupFailure | undefined,
  launchFailureMessage: '',
  stopTarget: null as RunningClient | null,
})
let accountExpiryTimer: ReturnType<typeof setTimeout> | undefined
function expireDisplayedAccounts() {
  const sessions = liveAccountSessions({ accounts: state.accounts, current: state.account })
  state.accounts = sessions.accounts; state.account = sessions.current
}
watch(() => state.accounts, accounts => {
  clearTimeout(accountExpiryTimer)
  const delay = accountExpiryDelay(accounts)
  if (delay !== null) accountExpiryTimer = setTimeout(expireDisplayedAccounts, delay)
}, { flush: 'sync' })
window.addEventListener('focus', expireDisplayedAccounts)
export const selectedGame = computed(() => state.games.find(g => g.id === state.selectedGameId
  && !g.unsupportedRegions?.some(tag => tag.toLowerCase() === (i18n.global.locale?.value ?? 'en').toLowerCase())))
export const homeGames = computed(() => state.games.filter(g => (g.status === 'enabled' || g.homeOrder != null)
    && !g.unsupportedRegions?.some(tag => tag.toLowerCase() === (i18n.global.locale?.value ?? 'en').toLowerCase()))
  .sort((a, b) => (a.homeOrder ?? 0) - (b.homeOrder ?? 0) || a.id.localeCompare(b.id)))
export const gameReady = computed(() => selectedGame.value?.status === 'enabled')
export const gamePreparing = computed(() => selectedGame.value?.preparing ?? false)
export const gameInstances = computed(() => gameReady.value ? state.instances.filter(i => i.game === state.selectedGameId) : [])
export const selectedInstance = computed(() => gameInstances.value.find(i => i.id === state.selectedInstanceId))
export const selectedBuild = computed(() => selectedGame.value?.builds.find(b => b.id === (selectedInstance.value?.build ?? state.selectedBuildId)))
export const gameServers = computed(() => state.servers.filter(s => s.game === state.selectedGameId))
export const selectedServer = computed(() => gameServers.value.find(s => s.id === state.selectedServerId))
const schemeLibraries = reactive<Record<string, SchemeLibrary>>({})
const appliedSchemes = new Map<string, { game: string; build: string; scheme: string; server: string; resolved: string[] }>()
const applyingSchemes = new Set<string>()
const pendingSchemes = new Map<string, number>()
let applyingSchemeRecovery: { game: string; releases: Map<string, { id: string; version: string }> } | undefined
export const pluginSchemesBusy = reactive({ value: false })
function schemeLibraryForGame(game: string): SchemeLibrary {
  if (!schemeLibraries[game]) {
    const saved = localStorage.getItem(`pluginSchemes.${game}`)
    try { schemeLibraries[game] = readLibrary(saved) }
    catch {
      if (saved) localStorage.setItem(`pluginSchemes.${game}.invalid`, saved)
      schemeLibraries[game] = emptyLibrary()
      state.error = i18n.global.t('pluginSchemes.recovered')
    }
  }
  return schemeLibraries[game]!
}
export const pluginSchemeLibrary = computed(() => schemeLibraryForGame(state.selectedGameId))
export const selectedPluginScheme = computed(() => pluginSchemeLibrary.value.selections[schemeContext(state.selectedServerId, selectedBuild.value?.id ?? '')] ?? defaultScheme)
function persistSchemes(game: string) { localStorage.setItem(`pluginSchemes.${game}`, JSON.stringify(schemeLibraries[game])) }
/** Carries the checked store action into each build's saved scheme before any version switch can restore an older choice. */
export function prepareStorePluginInstallation(item: StorePackage, server: string): (task: string) => void {
  const library = schemeLibraryForGame(item.game)
  const builds = item.builds.length ? item.builds : [...new Set([
    ...(state.games.find(game => game.id === item.game)?.builds.map(build => build.id) ?? []),
    ...state.instances.filter(instance => instance.game === item.game).map(instance => instance.build),
  ])]
  const plans = planStoreInstallation(library, item, builds, server, state.instances.filter(instance => instance.game === item.game))
  return task => { rememberStoreInstallation(library, plans, task); persistSchemes(item.game) }
}
/** Drops canceled store choices even when their dialog has closed or the application has restarted. */
export function cancelStorePluginInstallation(task: string): void {
  for (const game of state.games) {
    const library = schemeLibraryForGame(game.id)
    if (!library.installations?.some(record => record.task === task)) continue
    cancelStoreInstallation(library, task); persistSchemes(game.id)
  }
}
/** Retirement removes every saved selection of this identity, including schemes not currently selected. */
export function forgetDeprecatedPlugin(game: string, pluginId: string): void {
  const library = schemeLibraries[game] ?? readLibrary(localStorage.getItem(`pluginSchemes.${game}`))
  for (const scheme of library.schemes) for (const build of Object.keys(scheme.builds)) {
    scheme.builds[build] = scheme.builds[build]!.filter(plugin => plugin.id !== pluginId)
  }
  library.installations = library.installations?.filter(record => record.plugin.id !== pluginId)
  schemeLibraries[game] = library
  for (const instance of state.instances.filter(item => item.game === game)) instance.plugins = instance.plugins.filter(plugin => plugin.id !== pluginId)
  persistSchemes(game)
}
/** Removes only the failed installed release; unrelated unresolved imports remain pinned. */
export function forgetRecoveredPlugins(notice: PluginRecoveryNotice): void {
  const library = schemeLibraryForGame(notice.game)
  if (applyingSchemeRecovery?.game === notice.game) applyingSchemeRecovery.releases.set(
    JSON.stringify([notice.pluginId, notice.version]), { id: notice.pluginId, version: notice.version })
  removeRecoveredRelease(library, notice.pluginId, notice.version)
  schemeLibraries[notice.game] = library
  for (const instance of state.instances.filter(item => item.game === notice.game)) {
    instance.plugins = instance.plugins.filter(plugin => plugin.id !== notice.pluginId || plugin.version !== notice.version)
  }
  persistSchemes(notice.game)
}

export function capturePluginScheme(instance: Instance): void {
  if (applyingSchemes.has(instance.id)) return
  const active = appliedSchemes.get(instance.id), library = schemeLibraries[instance.game]
  const scheme = library?.schemes.find(s => s.id === active?.scheme)
  if (scheme && active?.build === instance.build) {
    // Packages skipped during import remain pinned until a later application resolves them.
    const captured = captureSchemeInstallation(library!, scheme, instance.build, instance.plugins, active.resolved, state.plugins.filter(plugin => plugin.game === instance.game))
    scheme.builds[instance.build] = captured.plugins
    active.resolved = captured.resolved
    persistSchemes(instance.game)
  }
}
let schemeQueue = Promise.resolve()
export function ensurePluginScheme(force = false): Promise<void> {
  const requestedInstance = selectedInstance.value?.id, requestedGame = state.selectedGameId, server = state.selectedServerId
  if (force && requestedInstance) pendingSchemes.set(requestedInstance, (pendingSchemes.get(requestedInstance) ?? 0) + 1)
  const job = schemeQueue.then(async () => {
    const instance = selectedInstance.value
    if (!instance || instance.id !== requestedInstance || state.selectedGameId !== requestedGame || state.selectedServerId !== server
      || !state.instancesLoaded || isRunning(instance.id)) return
    const library = pluginSchemeLibrary.value, id = selectedPluginScheme.value
    const target = library.schemes.find(s => s.id === id) ?? library.schemes[0]!
    const active = appliedSchemes.get(instance.id)
    const pending = pendingSchemes.get(instance.id)
    if (!force && !pendingSchemes.has(instance.id) && active?.scheme === target.id && active.build === instance.build && active.server === server) return
    if (!force) capturePluginScheme(instance)
    const baseline = library.schemes.find(s => s.id === defaultScheme)!
    baseline.builds[instance.build] ??= cloneSelection(instance.plugins)
    target.builds[instance.build] ??= cloneSelection(baseline.builds[instance.build]!)
    pluginSchemesBusy.value = true
    applyingSchemes.add(instance.id)
    applyingSchemeRecovery = { game: instance.game, releases: new Map() }
    try {
      const migrated = await call('plugins.resolveScheme', { instanceId: instance.id, plugins: cloneSelection(target.builds[instance.build]!) })
      const changed = await call('plugins.applyScheme', { instanceId: instance.id, plugins: migrated, serverId: server || undefined })
      target.builds[instance.build] = migrated
      // Recovery can arrive while applyScheme is awaiting the host. Its result
      // must not reintroduce a release that the recovery event just removed.
      for (const release of applyingSchemeRecovery.releases.values()) {
        removeRecoveredRelease(library, release.id, release.version)
      }
      state.instances = state.instances.map(item => item.id === changed.id ? { ...item, ...changed } : item)
      appliedSchemes.set(instance.id, { game: instance.game, build: instance.build, scheme: target.id, server,
        resolved: changed.plugins.map(p => JSON.stringify([p.id, p.version])) })
      if (pendingSchemes.get(instance.id) === pending) pendingSchemes.delete(instance.id)
      persistSchemes(instance.game)
    } finally { applyingSchemeRecovery = undefined; applyingSchemes.delete(instance.id); pluginSchemesBusy.value = false }
  })
  schemeQueue = job.catch(() => {})
  return job
}
export async function choosePluginScheme(id: string): Promise<void> {
  if (state.busy || pluginSchemesBusy.value || selectedInstance.value && isRunning(selectedInstance.value.id)) return
  if (!pluginSchemeLibrary.value.schemes.some(s => s.id === id)) return
  const key = schemeContext(state.selectedServerId, selectedBuild.value?.id ?? '')
  const library = pluginSchemeLibrary.value, game = state.selectedGameId, previous = library.selections[key]
  library.selections[key] = id
  try { await ensurePluginScheme(); persistSchemes(game); await refreshPlugins() }
  catch (error) { if (previous) library.selections[key] = previous; else delete library.selections[key]; persistSchemes(game); throw error }
}
export async function addPluginScheme(name: string, imported?: PluginScheme): Promise<void> {
  const trimmed = name.trim()
  if (!trimmed || trimmed.length > 64 || pluginSchemeLibrary.value.schemes.some(s => s.name.toLowerCase() === trimmed.toLowerCase())) throw new Error(i18n.global.t('pluginSchemes.invalidName'))
  if (selectedInstance.value) capturePluginScheme(selectedInstance.value)
  const scheme = imported ?? { id: crypto.randomUUID(), name: trimmed, builds: JSON.parse(JSON.stringify(pluginSchemeLibrary.value.schemes.find(s => s.id === selectedPluginScheme.value)?.builds ?? {})) }
  pluginSchemeLibrary.value.schemes.push(scheme); persistSchemes(state.selectedGameId)
  await choosePluginScheme(scheme.id)
}
export async function deletePluginScheme(id: string): Promise<void> {
  if (id === defaultScheme) return
  if (selectedPluginScheme.value === id) await choosePluginScheme(defaultScheme)
  pluginSchemeLibrary.value.schemes = pluginSchemeLibrary.value.schemes.filter(s => s.id !== id)
  pluginSchemeLibrary.value.installations = pluginSchemeLibrary.value.installations?.filter(record => record.scheme !== id)
  for (const [key, selected] of Object.entries(pluginSchemeLibrary.value.selections)) if (selected === id) delete pluginSchemeLibrary.value.selections[key]
  persistSchemes(state.selectedGameId)
}
export const serverPreset = computed(() => state.catalog?.instances.find(p => p.build === selectedBuild.value?.id))
// The server lists every build it knows, but a cluster serves only the union of its nodes' builds; the rest stay visible, disabled.
export const buildNotServed = computed(() => serverPreset.value?.clusterServed === false)
export const supportsPlugins = computed(() => state.catalog?.supportsPlugins ?? selectedGame.value?.supportsPlugins ?? true)
export const navigationEntries = computed(() => selectedGame.value?.navigationEntries ?? ['home', 'downloads', 'accounts'])
export const supportsInstances = computed(() => state.catalog?.supportsInstances ?? selectedGame.value?.supportsInstances ?? false)
export const loginPolicy = computed(() => resolveLoginPolicy(selectedBuild.value?.launcherLogin, selectedGame.value?.launcherLogin,
  serverPreset.value?.launcherLogin ?? state.catalog?.launcherLogin, selectedServer.value?.kind === 'room'))
export const loginRequired = computed(() => loginPolicy.value === 'required')
export const clientLanguages = computed(() => supportedClientLanguages(selectedBuild.value, selectedGame.value?.clientLanguages,
  serverPreset.value?.clientLanguages ?? state.catalog?.clientLanguages))
export const isClientLanguageInstalled = (code: string) => clientLanguageInstalled(selectedBuild.value, code, state.plugins)
export const languageNames: Record<string, string> = { 'zh-CN': '简体中文', 'zh-TW': '繁體中文', ko: '한국어', en: 'English', id: 'Bahasa Indonesia', vi: 'Tiếng Việt' }
let serviceRequest = 0
export async function refreshServices(): Promise<void> {
  const request = ++serviceRequest, id = state.selectedServerId
  state.catalog = null; state.serverError = ''
  if (!id) { state.serverLoading = false; state.accountRestoring = false; return }
  state.serverLoading = true
  state.accountRestoring = !state.account
  try {
    const catalog = await call('servers.catalog', { id })
    if (request !== serviceRequest || id !== state.selectedServerId) return
    state.catalog = catalog
    if (loginPolicy.value === 'none') {
      ++accountRequest; state.accounts = []; state.account = null; state.accountsLoading = false; state.accountRestoring = false; state.authOpen = false
    } else await refreshAccounts()
  } catch (error) { if (request === serviceRequest) state.serverError = (error as Error).message }
  finally { if (request === serviceRequest) { state.serverLoading = false; if (!state.accountsLoading) state.accountRestoring = false } }
}
let accountRequest = 0
export async function refreshAccounts(): Promise<void> {
  const request = ++accountRequest, serverId = state.selectedServerId
  if (!serverId) { state.accounts = []; state.account = null; state.accountsLoading = false; state.accountRestoring = false; return }
  state.accountsLoading = true
  state.accountRestoring = !state.account
  try {
    const result = await call('accounts.list', { serverId })
    if (request === accountRequest && serverId === state.selectedServerId) {
      const live = liveAccountSessions(result); state.accounts = live.accounts; state.account = live.current
    }
  } finally {
    if (request === accountRequest && serverId === state.selectedServerId) { state.accountsLoading = false; state.accountRestoring = false }
  }
}
export async function selectAccount(username: string, section?: string | null): Promise<void> {
  const serverId = state.selectedServerId
  await perform(async () => {
    ++accountRequest
    try { await call('accounts.select', { serverId, username, section: section ?? undefined }) }
    finally { if (serverId === state.selectedServerId) await refreshAccounts() }
  })
}
export async function refreshServers({ refreshSelectedServices = true }: { refreshSelectedServices?: boolean } = {}): Promise<void> {
  const previousId = state.selectedServerId
  try { state.servers = await call('servers.list') }
  catch (error) { if (!state.accountsLoading) state.accountRestoring = false; throw error }
  if (!gameServers.value.some(s => s.id === state.selectedServerId)) state.selectedServerId = localStorage.getItem(`server.${state.selectedGameId}`) ?? gameServers.value[0]?.id ?? ''
  if (!gameServers.value.some(s => s.id === state.selectedServerId)) state.selectedServerId = gameServers.value[0]?.id ?? ''
  if (refreshSelectedServices && state.loaded && previousId === state.selectedServerId) await refreshServices()
}
export async function setClientLanguage(language: string): Promise<void> {
  if (!clientLanguages.value.includes(language) || !isClientLanguageInstalled(language)) return
  const instance = selectedInstance.value
  if (instance && (inHost || !!demoMode?.enabled)) {
    if (!await perform(() => call('instances.update', { id: instance.id, name: instance.name, endpoint: instance.endpoint, language, serverId: state.selectedServerId || undefined }))) return
  }
  state.clientLanguage = language; localStorage.setItem(`clientLanguage.${state.selectedGameId}`, language)
}
export const isRunning = (id: string) => state.running.some(r => r.instanceId === id)
export const runningStatusKey = (id: string) => {
  const hook = state.running.find(r => r.instanceId === id)?.hook
  return hook?.state === 'starting' ? 'launch.starting' : hook?.state === 'failed' ? 'launch.failed' : 'live.running'
}
export function requestStop(id: string): void { state.stopTarget = state.running.find(r => r.instanceId === id) ?? null }

let runningRefresh: Promise<void> | undefined, runningRevision = 0
/** Rechecks process ownership independently of slower client and plugin inventory work. */
export function refreshRunning(invalidate = false): Promise<void> {
  if (invalidate) runningRevision++
  runningRefresh ??= readRunning().finally(() => { runningRefresh = undefined })
  return runningRefresh
}
async function readRunning(): Promise<void> {
  while (true) {
    const expected = runningRevision
    try {
      const running = await call('instances.running')
      // An exit or readiness event invalidates any snapshot already in flight.
      if (expected === runningRevision) { state.running = running; return }
    } catch (error) { if (expected === runningRevision) throw error }
  }
}
/** Maintains one process-status request at a time for the lifetime of the application shell. */
export function pollRunning(): () => void {
  let stopped = false, timer: ReturnType<typeof setTimeout> | undefined
  const active = () => inHost || !!demoMode?.enabled
  const focus = () => {
    if (!stopped && active()) void refreshRunning(true).catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
  }
  async function poll() {
    if (!active()) { timer = undefined; return }
    try { await refreshRunning() }
    catch (error) { if (!stopped) { state.requestFailure = error as Error; state.error = (error as Error).message } }
    if (!stopped && active()) timer = setTimeout(poll, 2000)
  }
  const stopModeWatch = demoMode
    ? watch(() => demoMode.enabled, enabled => {
        if (stopped) return
        if (enabled && !timer) void poll()
        else if (!enabled && !inHost) {
          clearTimeout(timer)
          timer = undefined
          state.running = []
        }
      })
    : () => {}
  window.addEventListener('focus', focus)
  if (active()) void poll()
  return () => { stopped = true; clearTimeout(timer); stopModeWatch(); window.removeEventListener('focus', focus) }
}

/** Refreshes persisted instances and process status. */
let instanceRefresh: Promise<void> | undefined
export async function refreshInstances(includePlugins = true): Promise<void> {
  instanceRefresh ??= readInstances().finally(() => { instanceRefresh = undefined })
  await instanceRefresh
  if (includePlugins) await refreshPlugins()
}
/** Reconciles completed package downloads with the selected scheme and authoritative client inventory. */
export async function refreshPluginSelectionsAfterDownload(): Promise<void> {
  // Installation can activate a newer package before the shell receives its completion event.
  // Finish any pre-installation snapshot before requesting authoritative selections again.
  if (instanceRefresh) await instanceRefresh.catch(() => {})
  await refreshInstances(false)
  await ensurePluginScheme(true)
  await refreshInstances()
}
async function readInstances(): Promise<void> {
  state.instancesLoading = true
  const results = await Promise.allSettled([
      call('instances.list', { preferredId: state.selectedInstanceId || undefined }).then(instances => {
        for (const instance of instances) capturePluginScheme(instance)
        state.instances = instances
        if (!gameInstances.value.some(i => i.id === state.selectedInstanceId)) {
          const matching = gameInstances.value.find(instance => instance.build === state.selectedBuildId)
          if (matching || state.selectedInstanceId || !state.selectedBuildId) state.selectedInstanceId = (matching ?? gameInstances.value[0])?.id ?? ''
        }
        if (!state.selectedBuildId) state.selectedBuildId = selectedInstance.value?.build ?? selectedGame.value?.builds[0]?.id ?? ''
        state.instancesLoaded = true
      }).finally(() => { state.instancesLoading = false }),
      refreshRunning(),
    ])
  const failed = results.find(result => result.status === 'rejected')
  if (failed?.status === 'rejected') throw failed.reason
}
let pluginRequest = 0
watch(supportsPlugins, () => { void refreshPlugins().catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message }) })
export async function refreshPlugins(): Promise<void> {
  try { await ensurePluginScheme() }
  catch (error) {
    // Inventory remains readable while a mutation owns the client; launch still awaits its scheme.
    if (!error || typeof error !== 'object' || !('code' in error) || error.code !== 'instance_in_use') throw error
  }
  const request = ++pluginRequest
  const id = selectedInstance.value?.id
  const plugins = await call('plugins.list', { instanceId: id, serverId: state.selectedServerId || undefined, language: i18n.global?.locale ? String(i18n.global.locale.value) : undefined })
  if (request === pluginRequest) state.plugins = plugins
}
export async function loadGames(): Promise<void> {
  if (state.games.length) {
    state.loaded = true
    void loadWorkspace()
    return
  }
  await loadWorkspace()
}
async function loadWorkspace(): Promise<void> {
  try {
    state.games = (await call('games.list')).map(game => {
      const previous = cachedWorkspace.builds.find(group => group.game === game.id)?.builds ?? []
      const remembered = previous.filter(build => !game.builds.some(item => item.id === build.id))
      return { ...game, builds: [...game.builds, ...remembered].sort((a, b) => a.branch.localeCompare(b.branch) || (a.buildDate ?? '').localeCompare(b.buildDate ?? '') || a.id.localeCompare(b.id)) }
    })
    if (!homeGames.value.some(g => g.id === state.selectedGameId)) state.selectedGameId = homeGames.value[0]?.id ?? ''

    // The first paint must not wait for filesystem inspection, account refreshes,
    // store synchronization or executable signature parsing. Those requests are
    // independent and update the already visible shell as they complete.
    void refreshInstances(false).catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
    void refreshServers().catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
    void call('settings.network').then(network => { state.ipPreference = network.ipPreference })
      .catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })

    void call('store.sync').then(async () => {
      const games = (await call('games.list')).map(game => ({ ...game, builds: [...game.builds].sort((a, b) => a.branch.localeCompare(b.branch) || (a.buildDate ?? "").localeCompare(b.buildDate ?? "") || a.id.localeCompare(b.id)) }))
      await refreshInstances(false)
      state.games = games
      if (!selectedBuild.value) {
        state.selectedInstanceId = gameInstances.value.find(instance => selectedGame.value?.builds.some(build => build.id === instance.build))?.id ?? ''
        state.selectedBuildId = selectedInstance.value?.build ?? selectedGame.value?.builds[0]?.id ?? ''
      }
      await refreshServers()
    }).catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
  } catch (error) { if (!state.accountsLoading) state.accountRestoring = false; state.requestFailure = error as Error; state.error = (error as Error).message }
  finally {
    state.loaded = true
    void refreshPlugins().catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
    if (inHost || !!demoMode?.enabled) void call('instances.recoverStopped').catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
  }
}
export async function perform(action: () => Promise<unknown>): Promise<boolean> {
  if (state.busy) return false
  state.busy = true; state.error = ''; state.requestFailure = undefined
  try { await action(); await refreshInstances(); return true }
  catch (error) { state.requestFailure = error as Error; state.error = (error as Error).message; return false }
  finally { state.busy = false }
}
export function selectGame(id: string): void {
  if (state.busy || !homeGames.value.some(g => g.id === id)) return
  state.authOpen = false
  state.selectedGameId = id
  const previous = readClientSelection(id)
  state.selectedInstanceId = gameInstances.value.find(instance => instance.id === previous.instanceId)?.id ??
    (previous.buildId && !previous.instanceId ? '' : gameInstances.value[0]?.id ?? '')
  state.selectedBuildId = selectedInstance.value?.build ?? (selectedGame.value?.builds.some(build => build.id === previous.buildId) ? previous.buildId : selectedGame.value?.builds[0]?.id ?? '')
  state.selectedServerId = localStorage.getItem(`server.${id}`) ?? gameServers.value[0]?.id ?? ''
  if (!gameServers.value.some(s => s.id === state.selectedServerId)) state.selectedServerId = gameServers.value[0]?.id ?? ''
  state.clientLanguage = localStorage.getItem(`clientLanguage.${id}`) ?? selectedGame.value?.clientLanguages[0] ?? ''
  localStorage.setItem('game', id)
}
watch([homeGames, () => state.busy], ([available, busy]) => {
  if (busy || !available.length || available.some(game => game.id === state.selectedGameId)) return
  selectGame(available[0]!.id)
})
watch([selectedInstance, clientLanguages, () => state.plugins], () => { const preferred = selectedInstance.value?.language ?? localStorage.getItem(`clientLanguage.${state.selectedGameId}`); state.clientLanguage = preferred && clientLanguages.value.includes(preferred) ? preferred : clientLanguages.value.find(isClientLanguageInstalled) ?? '' })
watch(() => state.selectedServerId, id => {
  state.authOpen = false; state.account = null; state.accounts = []; ++accountRequest; state.catalog = null; state.serverError = ''
  state.accountsLoading = false; state.accountRestoring = !!id
  localStorage.setItem(`server.${state.selectedGameId}`, id)
  if (state.loaded) void refreshServices()
  if (state.loaded) void refreshPlugins().catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
}, { flush: 'sync' })
watch(() => state.selectedInstanceId, id => {
  localStorage.setItem('instance', id)
  void refreshPlugins().catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
})
watch([() => state.instances, () => state.games, () => state.selectedInstanceId, () => state.selectedBuildId], () => {
  try {
    // Display-only snapshots omit plugin options, installation paths and account/session data.
    localStorage.setItem('clientWorkspace', JSON.stringify({
      instances: state.instances.map(({ id, game, build, name, clientInstalled, language }) => ({ id, game, build, name, clientInstalled, language })),
      games: state.games,
    }))
    if ((!state.selectedInstanceId || selectedInstance.value) && (selectedInstance.value || state.selectedBuildId))
      localStorage.setItem(`clientSelection.${state.selectedGameId}`, JSON.stringify({ instanceId: state.selectedInstanceId, buildId: selectedInstance.value?.build ?? state.selectedBuildId }))
  } catch { /* An unavailable display cache must not interrupt the authoritative host state. */ }
})
function refreshClientState(): void {
  void refreshRunning(true).catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
  void refreshInstances().catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
}
on('instances.changed', refreshClientState)
on('instances.exited', refreshClientState)
on('instances.readiness', data => {
  const readiness = data as { hook?: { state?: string; error?: string } } | null
  if (readiness?.hook?.state === 'failed' && readiness.hook.error === 'startup-timeout') state.error = i18n.global.t('launch.timeout')
  refreshClientState()
})
on('instances.error', data => {
  const failure = data as LaunchCleanupFailure
  const key = failure.phase === 'bootstrap-dispose' ? 'bootstrap' : failure.phase === 'runtime-restore' ? 'restore'
    : failure.phase === 'observe-exit' ? 'observe' : 'other'
  const code = clientFailureCode(failure)
  state.error = failure.error === 'launch_cleanup_failed'
    ? code ? i18n.global.t('launchCleanup.clientExited', { code }) : i18n.global.t(`launchCleanup.${key}`)
    : failure.error
  state.launchFailure = failure.error === 'launch_cleanup_failed' ? failure : undefined
  state.launchFailureMessage = state.launchFailure ? state.error : ''
  void refreshRunning(true).catch(error => { state.requestFailure = error as Error; state.error = (error as Error).message })
})

const root = document.documentElement.style
watch([selectedGame, () => prefs.systemAccent], () => {
  const color = prefs.systemAccent ? '' : selectedGame.value?.accent || ''
  for (const key of ['--accent', '--accent-default', '--accent-secondary', '--accent-tertiary', '--fill-attention']) root.removeProperty(key)
  if (color) {
    root.setProperty('--accent', color); root.setProperty('--accent-default', color)
    root.setProperty('--accent-secondary', `color-mix(in srgb, ${color} 90%, transparent)`)
    root.setProperty('--accent-tertiary', `color-mix(in srgb, ${color} 80%, transparent)`)
  }
}, { immediate: true })

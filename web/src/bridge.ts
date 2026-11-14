import previewMirrors from './preview-github-mirrors.json'
import type { NativeUiCapabilities, NativeUiResult } from './nativeUi'
import type { ClientUpdateOverview, ClientUpdatePreferences } from './downloads/clientUpdates'
import previewBuilds from "./preview-builds.json"
import installationPreview from './store/installation-preview.json'
import { i18n } from './i18n'
import { resourceConflictError, type ResourceConflictReport } from './plugins/resourceConflicts'
import { pluginPolicyError } from './pluginPolicyErrors'
import { serverFailure, type ServerFailureDetails } from './serverFailures'
import { instanceFailure, type InstanceFailureDetails } from './instanceFailures'
import previewLevels from "./preview-levels.json"
import type { StoreCatalog, StoreSource, StorePackage } from './store/types'
import type { DownloadTask } from './downloads/model'
import { previewSources, savePreviewSource, removePreviewSource } from './store/preview'
import { multiplayerPreview, previewDirectory, previewInstances, previewRoster, previewServerCatalog } from './multiplayerPreview'
import { checkPreviewPolicy, configurePreviewPolicyPlugin, installPreviewPolicyPackage, isPolicyPreview, previewPolicyCatalog, previewPolicyInstances, previewPolicyPlugins } from './plugins/policyPreview'
import { hostingPreview, previewHosting, previewHostingLog, savePreviewHosting, changePreviewHosting, deletePreviewHosting } from './hostingPreview'
export { hostingPreview } from './hostingPreview'
// Typed client for the host bridge (src/FantasyTale.Launcher.Host/Bridge/BridgeRouter.cs).
//
// Architecture & Invariants:
// 1. Host Isolation: In standalone development (`npm run dev`), inHost is false and fallback
//    handlers mock host operations to allow UI development without running the desktop host.
// 2. Bounded Concurrency: At most 32 asynchronous bridge requests can be pending concurrently.
//    Excess requests are immediately rejected to enforce bounded resources and prevent leaks.
// 3. Stale-Result Guards: Callers track monotonic revision counters (e.g., requestRevision)
//    so that delayed or out-of-order RPC responses arriving after a user navigates or switches
//    context are discarded rather than mutating inactive state.
// 4. Localization Boundary: The backend emits machine-readable error codes. This bridge acts
//    as the translation barrier, mapping codes (e.g. client_remove_unsafe, instance_in_use)
//    to localized strings before surfacing them to views.

export type LocalizedText = Record<string, string>
export interface LaunchCleanupFailure {
  instanceId: string
  processId?: number
  error: string
  phase?: string
  exitConfirmed?: boolean
  exitCode?: number | null
  failureType?: string
  utc?: string
}
export interface PluginSettingsPage { id: string; kind: 'native' | 'html'; title: LocalizedText; options: string[] }
export interface ClientComponent { id: string; name: LocalizedText; description?: LocalizedText | null; required: boolean; defaultSelected?: boolean }
export interface InstalledServerPackage { id: string; sourceId: string; package: StorePackage; directory: string }

export interface BuildSummary {
  supportsHosting?: boolean
  id: string
  branch: string
  buildDate?: string | null
  releaseDate?: string | null
  name: LocalizedText
  login: string
  defaultPort: number | null
  releaseVersion: string
  patchVersion?: string
  protocolVersion?: number
  region?: string
  description: LocalizedText | null
  launcherLogin: string | null
  nativeLanguage?: string
  additionalLanguages?: Record<string, string>
  requiresAccount: boolean
}

export type OptionValue = boolean | number | string
export interface Endpoint { host: string; port: number }
export interface InstancePlugin { id: string; version: string; enabled: boolean; required: boolean; options: Record<string, OptionValue> }
export interface Instance { id: string; name: string; game: string; build: string; endpoint: Endpoint | null; plugins: InstancePlugin[]; language?: string | null; checkIntegrity?: boolean; clientInstalled?: boolean; installationPath?: string | null; externalInstallation?: boolean; recoveryRequired?: boolean; diagnosticsEnabled?: boolean }
export interface HookStatus { state: 'starting' | 'ready' | 'failed' | 'not-required'; error: string; plugins: { id: string; version: string; outcome: string; required: boolean }[] }
export interface RunningClient { instanceId: string; processId: number; hook?: HookStatus }
export interface PluginOption {
  key: string; type: 'bool' | 'int' | 'string' | 'enum'; default: OptionValue
  minimum: number; maximum: number; maxLength: number; values: string[]; valueLabels?: Record<string, LocalizedText> | null; label: LocalizedText; description: LocalizedText | null
}
export interface PluginSummary {
  aliases?: string[]
  /** Packages this one needs, as `id` or `id@constraint`; shared data packages are used without being enabled. */
  requires?: string[]
  /** Plugin policy rules that apply to the selected client and launcher language. */
  policy?: { plugin: string; type: 'require' | 'recommend' | 'conflict'; range: string; builds: string[]; languages: string[]; defaultSelected: boolean; satisfied?: boolean | null }[]
  builtIn?: boolean
  languagePackCompatible?: boolean
  authors?: string[]
  minimumLauncherVersion?: string | null; launcherCompatible?: boolean
  id: string; game: string; version: string; kind: 'patch' | 'native' | 'files' | 'overlay' | 'service' | 'resources' | 'assets'; name: LocalizedText; description: LocalizedText | null
  canUninstall?: boolean
  warningRequired?: boolean
  resourceSettings?: boolean; configurationPage?: boolean; configurationPages?: PluginSettingsPage[] | null; available: boolean; required: boolean; selection: InstancePlugin | null; options: PluginOption[]
}
export interface ResourceOverrideChange {
  pluginId: string; version: string; name: LocalizedText; member: string
  action: 'add' | 'replace' | 'remove' | 'file'; originalSha256: string | null; sha256: string | null
}
export interface ResourceOverrideFile {
  target: string; size: number; sha256: string; originalSha256: string | null
  cacheReused: boolean; restoreOnExit: boolean; changes: ResourceOverrideChange[]
  state: 'original' | 'overridden' | 'modified' | 'unreadable'
}
export interface ResourceOverrideSnapshot { preparedAt: string; files: ResourceOverrideFile[] }
export interface PluginLoadIssue { folder: string; game: string | null; id: string | null; version: string | null; message: string; sourceId?: string | null }
export interface PluginRecoveryNotice { instanceId: string; game: string; pluginId: string; version: string; reason: 'missing' | 'corrupt' | 'unavailable' }
export interface PluginDeprecationNotice { game: string; pluginId: string; versions: string[]; name: LocalizedText; reason: LocalizedText }
export interface PluginImportPreview {
  token: string; id: string; version: string; kind: 'patch' | 'native' | 'files' | 'overlay' | 'service' | 'resources' | 'assets'; name: LocalizedText
  description: LocalizedText | null; fileCount: number; size: number
}
export interface PluginUpdateState {
  id: string; installedVersion: string; status: 'current' | 'available' | 'installed' | 'unmanaged' | 'source' | 'integrity' | 'network' | 'disk'
  version: string | null; manifestSha256: string | null; sourceName: string | null; targetId?: string | null
}
export interface PluginUpdateSnapshot { checking: boolean; checkedAt: string | null; updates: PluginUpdateState[]; error: string | null }

export type IpPreference = 'china' | 'all' | 'disabled'
export interface NetworkPreferences { ipPreference: IpPreference; gitHubMirror: boolean; gitHubMirrorSource: string; defaultPending: boolean; suggestionPending: boolean }
export interface MirrorResult { id: string; state: 'pending' | 'testing' | 'available' | 'failed'; milliseconds: number | null; bytesPerSecond: number | null }
let previewMirrorResults: MirrorResult[] = []
let previewMirrorSuggestionAcknowledged = false
const previewMirrorSuggestion = () => import.meta.env.DEV && !previewMirrorSuggestionAcknowledged &&
  new URLSearchParams(location.hash.split('?')[1] ?? '').get('mirrorSuggestionPreview') === '1'
export interface MirrorOverview { providers: { id: string; name: string }[]; results: MirrorResult[] }
export interface Server {
  builds?: string[]
  clientPolicy?: ClientPolicy
  presentation?: ServerPresentation | null; iconAsset?: string | null; kind?: 'server' | 'room'; localOnly?: boolean; transient?: boolean; preferIp?: boolean | null; useHttps?: boolean; id: string; game: string; name: string; apiUrl: string | null; endpoint: Endpoint | null; bundledInstances: boolean; builtIn: boolean }
export interface ServerClientSource { kind: 'http' | 'alist' | 'github'; baseUrl: string; catalogPath: string; packageId: string }
export interface ServerClientRelease { source: StoreSource; package: StorePackage }
export interface ServerPreset { downloadSource?: ServerClientSource | null; id: string; build: string; name: LocalizedText; endpoint: Endpoint | null; launcherLogin?: string | null; clientLanguages?: string[] | null; releaseVersion?: string; protocolVersion?: number | null; patchVersion?: string | null; clusterServed?: boolean | null }
export interface RegistrationField { key: string; label: LocalizedText; type: 'text' | 'password' | 'email' | 'number' | 'select' | 'checkbox'; required: boolean; maxLength: number; choices: string[] }
export interface ServerPresentation { images?: string[]; imagesTranslations?: Record<string, string[]>; motd?: string; motdTranslations?: Record<string, string>; summary?: string; summaryTranslations?: Record<string, string>; iconUrl?: string | null; iconUrlTranslations?: Record<string, string>; onlinePlayers?: number | null; presenceGroup?: string | null; presenceComplete?: boolean | null; peakOnlinePlayers?: number | null; playerLimit?: number | null; state?: 'online' | 'offline' | 'maintenance' | null; whitelist?: boolean; rosterEnabled?: boolean }
export interface OnlinePlayerList { schema: number; enabled: boolean; complete?: boolean | null; truncated?: boolean; players: { name: string; level?: number | null; buildId?: string | null }[] }
/** One typed rule of a server's client policy. */
export interface ClientPolicyRule { id: string; type: string; builds: string[]; version?: string; sources?: string[] }
/** A server's client policy: store sources and per-plugin rules; later sections are added beside plugins. */
export interface ClientPolicy { version: number; sources: ServerStoreSource[]; plugins: { id: string; rules: ClientPolicyRule[] }[] }
/** One line of a server's plugin list: a plugin it requires, recommends or disables on the listed builds. */
export interface PolicyPluginEntry { id: string; builds: string[]; required: boolean; disabled?: boolean }
export interface ServerStoreSource { name: string; kind: 'http' | 'alist' | 'github'; baseUrl: string; catalogPath: string }
export interface ServerCatalog { schema: number; game: string; name: string; registrationEnabled: boolean; registrationFields: RegistrationField[]; instances: ServerPreset[]; hostPolicy?: 'auto' | 'ip' | 'domain' | null; region?: string | null; launcherLogin?: string | null; supportsInstances?: boolean | null; supportsPlugins?: boolean | null; clientLanguages?: string[] | null; profileFields?: string[] | null; levelIcon?: boolean | null; presentation?: ServerPresentation | null; clientPolicy?: ClientPolicy; clusterBuilds?: string[] | null }
/** A server section: servers that share one account and character data. A cluster whose servers all share data has one. */
/** A group of servers. A separate section signs players in through its own API and keeps its own accounts; others share the default's sign-in. */
export interface ServerSection { id: string; name: string; default?: boolean; separate?: boolean }
export interface ServerMaintenance { start?: string | null; end?: string | null; description?: string | null }
/** One selectable server of the selected server profile, with its section and live state. */
export interface ServerStatusEntry { id: number; name: string; section: string; automatic?: boolean; state: 'online' | 'maintenance' | 'offline'; maintenance?: ServerMaintenance | null; players?: number | null; capacity?: number | null }
export interface ServerStatus { sections: ServerSection[]; servers: ServerStatusEntry[] }
/** An operator notice shown once when the player starts the game on this server. */
export interface ServerNotice { id: string; title: string; body: string; level: 'info' | 'warning' | 'important'; publishedAt?: string | null }
export interface AccountProfile { section?: string | null; username: string; displayName: string; stats: Record<string, number | null>; levelIconUrl: string | null; rankBadge?: string | null; levelIconId?: number | null; rankVariant?: string | null; hasCharacter?: boolean | null }
export interface AccountSessionInfo { account: AccountProfile; expiresAt: string; refreshExpiresAt?: string | null }
export interface AccountSessionList { current: AccountProfile | null; accounts: AccountSessionInfo[] }
export interface LevelIcon { path: string; x: number; y: number; width: number; height: number; sheetWidth: number; sheetHeight: number }
export interface LevelEntry { level: number; minimumExperience: string; name: string | null; icons: Record<string, LevelIcon> }
export interface LevelCatalog { entries: LevelEntry[]; badges: Record<string, { iconId: number; name: string; icon: LevelIcon }>; displayOffset: number; namesPending: boolean }
export interface GameSummary {
  unsupportedRegions?: string[]
  navigationEntries: string[]
  roomsEnabled?: boolean
  localHostingEnabled?: boolean
  levels?: LevelCatalog | null
  assetBase?: string
  supportsInstances: boolean
  supportsPlugins: boolean
  launcherLogin: string
  clientLanguages: string[]
  profileFields: string[]
  levelIcon: boolean
  bundledInstances: boolean
  backgroundAudioTracks: (string | null)[]
  id: string
  status: 'enabled' | 'placeholder'
  homeOrder: number | null
  heroTitle?: string | null
  preparing: boolean
  serviceNotice: LocalizedText | null
  name: LocalizedText
  accent: string
  backgroundVideo: string | null
  backgroundImage?: string | null
  backgroundVideos: string[]
  builds: BuildSummary[]
}

export interface AppInfo {
  version: string
  language: string
  os: string
}

export interface LauncherUpdateState { mandatory?: boolean; deprecatedBeforeVersion?: string | null; status: 'unconfigured' | 'current' | 'checking' | 'available' | 'downloading' | 'ready' | 'failed'; version: string | null; revision: number | null; manifestSha256: string | null; failure: 'integrity' | 'network' | 'disk' | 'startup' | null; failureReason?: string | null; httpStatus?: number | null }
export interface LauncherUpdateAttempt { status: 'failed' | 'rolled-back' | 'recovery-required'; failure: 'integrity' | 'network' | 'disk' | 'startup' | null; phase: string | null; errorCode: number | null }
export interface LauncherUpdatePreferences { autoCheck: boolean; autoDownload: boolean }

export interface HostingProfile { relayPort?: number; requireAuthentication?: boolean; clientInstanceId?: string | null; id: string; game: string; build: string; name: string; packagePath: string; mode: 'private' | 'lan' | 'room'; advertisedHost: string; gamePort: number; apiPort: number; adminPort: number; temporaryData: boolean; storageMode?: 'package' | 'local' | 'temporary' | null; effectiveStorageMode?: 'package' | 'local' | 'temporary' }
export interface HostingSession { profileId: string; runId: string; state: 'starting' | 'ready' | 'stopping' | 'stopped' | 'failed'; processId: number; startedAt: string; exitCode: number | null; failure: string | null; forcedStop: boolean; apiUrl: string | null; adminUrl: string | null; logPath: string }
export interface HostingOverview { profiles: HostingProfile[]; sessions: HostingSession[] }
export interface DirectorySource { id: string; name: string; baseUrl: string | null; enabled: boolean; builtIn: boolean; storeSourceId?: string | null }
export interface DirectoryAdvertisement { game: string; name: string; apiUrl: string; builds: string[]; description: string; endpoint?: Endpoint | null; presentation?: ServerPresentation | null; clientPolicy?: ClientPolicy }
export interface ServerProfileImport { server: Server; exists: boolean; identical: boolean; fingerprint: string | null }
export interface DirectoryEntry { profilePath?: string | null; storeSourceId?: string | null; id: string; kind: 'server' | 'room'; details: DirectoryAdvertisement; expiresAt: string | null }
export interface RoomPublication { profileId: string; runId: string; sourceId: string; roomId: string; state: 'published' | 'reconnecting' | 'withdrawing' | 'expired' | 'withdrawn'; expiresAt: string; error: string | null }

/** Bridge methods and their result types. Keep in sync with the host's registrations. */
export interface LauncherUpdateSourceSettings { name: string; kind: string; baseUrl: string; feedPath: string; minimumRevision: number; preferIp?: boolean; signaturePath?: string | null; signaturePublicKey?: string | null }

export interface BridgeMethods {
  'servers.onlinePlayers': OnlinePlayerList
  'servers.status': ServerStatus
  'servers.notices': ServerNotice[]
  'servers.markNoticesSeen': null
  'directory.onlinePlayers': OnlinePlayerList
  'hosting.availablePorts': number[]
  'hosting.localAddresses': string[]
  'hosting.packages': InstalledServerPackage[]
  'store.installServer': DownloadTask
  'directory.sources': DirectorySource[]
  'directory.saveSource': DirectorySource
  'directory.deleteSource': null
  'directory.list': { schema: number; entries: DirectoryEntry[] }
  'directory.submit': { id: string; details: DirectoryAdvertisement; submittedAt: string }
  'directory.join': Server
  'directory.profile': { text: string; inspection: ServerProfileImport }
  'servers.inspectProfile': ServerProfileImport
  'servers.importProfile': Server
  'servers.exportProfile': { fileName: string; text: string }
  'servers.refreshProfile': Server
  'directory.publications': RoomPublication[]
  'directory.publish': RoomPublication
  'directory.withdraw': null
  'hosting.list': HostingOverview
  'hosting.save': HostingProfile
  'hosting.delete': null
  'hosting.start': HostingSession
  'hosting.stop': HostingSession
  'hosting.log': string[]
  'hosting.destination': Server
  'launcherUpdates.state': LauncherUpdateState
  'launcherUpdates.lastAttempt': LauncherUpdateAttempt | null
  'launcherUpdates.manualDownload': { url: string | null; version: string | null }
  'launcherUpdates.check': LauncherUpdateState
  'launcherUpdates.download': DownloadTask
  'launcherUpdates.restart': null
  'launcherUpdates.sourceResults': { name: string; state: LauncherUpdateState }[]
  'launcherUpdates.sources': LauncherUpdateSourceSettings[]
  'launcherUpdates.setSources': LauncherUpdateSourceSettings[]
  'launcherUpdates.preferences': LauncherUpdatePreferences
  'launcherUpdates.setPreferences': LauncherUpdatePreferences
  'launcherUpdates.openLog': null
  'updates.list': ClientUpdateOverview
  'updates.check': ClientUpdateOverview
  'updates.install': DownloadTask
  'settings.updates': ClientUpdatePreferences
  'settings.setUpdates': ClientUpdatePreferences
  'downloads.list': DownloadTask[]
  'downloads.action': DownloadTask[]
  'downloads.clearFinished': DownloadTask[]
  'store.installClient': DownloadTask
  'store.installPlugin': DownloadTask
  'store.installPluginTargets': DownloadTask
  'store.sources': StoreSource[]
  'store.saveSource': StoreSource
  'store.removeSource': null
  'store.catalog': StoreCatalog
  'store.sync': { sourceId: string; status: string; checkedAt: string | null; failure: string | null }[]
  'store.syncState': { sourceId: string; status: string; checkedAt: string | null; failure: string | null }[]
  'app.info': AppInfo
  'games.list': GameSummary[]
  'window.minimize': null
  'window.toggleMaximize': boolean
  'window.close': null
  'window.ready': null
  'window.exit': null
  'window.startDrag': null
  'shell.openExternal': null
  'shell.pickFolder': string | null
  'shell.pickPluginZip': string | null
  'shell.importServerImages': string[]
  'shell.pickServerProfile': string | null
  'shell.saveServerProfile': boolean
  'window.theme': null
  'window.state': { maximized: boolean }
  'window.language': null
  'instances.list': Instance[]
  'instances.running': RunningClient[]
  'nativeUi.capabilities': NativeUiCapabilities
  'nativeUi.invoke': NativeUiResult
  'nativeUi.result': NativeUiResult
  'instances.services': Record<string, string>
  'instances.create': Instance
  'store.downloadPlugin': DownloadTask
  'instances.inspectInstallation': { path: string; containsFiles: boolean; detectedBuildIds: string[] }
  'instances.clone': Instance
  'instances.delete': { deletedFiles: number; preservedFiles: number }
  'instances.update': Instance
  'instances.openFolder': null
  'instances.cancelResourcePreparation': null
  'instances.resourceOverrides': ResourceOverrideSnapshot | null
  'instances.cancelLaunch': null
  'instances.launch': RunningClient | { cancelled: true } | { timeout: true }
  'instances.stop': { instanceId: string; exitCode: number } | null
  'instances.hookPing': string
  'instances.openDiagnostics': null
  'instances.launchDiagnostics': { lifecycle: string }
  'instances.recover': null
  'plugins.list': PluginSummary[]
  'instances.recoverStopped': null
  'plugins.ensureLatest': DownloadTask[]
  'plugins.launchWarnings': { key: string; id: string; version: string; installed: boolean; name: LocalizedText; description?: LocalizedText | null; warning?: LocalizedText | null }[]
  'plugins.installationPlan': ClientComponent[]
  'plugins.installationComponents': DownloadTask[]
  'clientPolicy.claimPlugins': ClientComponent[]
  'clientPolicy.acceptPlugins': DownloadTask[]
  'plugins.configurationContext': Record<string, unknown>
  'plugins.configurationPage': string
  'plugins.configure': Instance
  'plugins.applyScheme': Instance
  'plugins.scheme.parse': { game: string; name: string; builds: { id: string; plugins: { id: string; version: string; enabled: boolean; options: Record<string, OptionValue> }[] }[] }
  'plugins.resolveScheme': InstancePlugin[]
  'plugins.scheme.format': string
  'shell.pickPluginScheme': string | null
  'shell.savePluginScheme': boolean
  'hosting.releasePrivate': null
  'plugins.loadIssues': PluginLoadIssue[]
  'plugins.recoveryNotices': PluginRecoveryNotice[]
  'plugins.deprecationNotices': PluginDeprecationNotice[]
  'plugins.dismissRecoveryNotice': null
  'plugins.uninstall': null
  'plugins.uninstallRejected': null
  'plugins.prepareImport': PluginImportPreview
  'plugins.installImport': { id: string; version: string; skipped: string[] }
  'pluginPolicy.check': unknown[]
  'settings.launcherLanguage': null
  'pluginPolicy.supportOffers': unknown[]
  'pluginPolicy.answerSupportOffer': { skipped: string[] }
  'plugins.discardImport': null
  'plugins.checkUpdates': PluginUpdateState[]
  'plugins.updateSnapshot': PluginUpdateSnapshot
  'plugins.pendingUpgrades': { count: number; waitingForExit: number; failure: string | null }
  'plugins.launchTransfers': DownloadTask[]
  'plugins.launchUpdates': { checking: boolean; items: { key: string; id: string; name: LocalizedText; installedVersion: string; version: string; task: DownloadTask | null }[] }
  'plugins.installUpdate': DownloadTask
  'servers.list': Server[]
  'servers.save': Server
  'servers.delete': null
  'servers.clientRelease': ServerClientRelease | null
  'servers.installClient': DownloadTask
  'servers.catalog': ServerCatalog
  'accounts.list': AccountSessionList
  'accounts.select': AccountSessionList
  'accounts.current': AccountProfile | null
  'accounts.rememberedLogin': { username: string; password: string } | null
  'accounts.refresh': AccountProfile | null
  'accounts.login': AccountProfile
  'accounts.register': null
  'accounts.logout': null
  'settings.network': NetworkPreferences
  'settings.showGitHubMirrorSuggestion': NetworkPreferences
  'settings.githubMirrors': MirrorOverview
  'settings.testGitHubMirrors': MirrorResult[]
  'settings.overlayTest': { enabled: boolean; passThroughOutside: boolean }
  'settings.setOverlayTest': { enabled: boolean; passThroughOutside: boolean }
  'settings.setNetwork': NetworkPreferences
}

type Pending = { resolve: (value: unknown) => void; reject: (reason: Error) => void }

const pending = new Map<number, Pending>()
const listeners = new Map<string, Set<(data: unknown) => void>>()
let nextId = 1

const external = (window.external ?? {}) as Partial<PhotinoExternal>
export const inHost = typeof external.sendMessage === 'function'

if (inHost) {
  external.receiveMessage!((raw) => {
    const message = JSON.parse(raw) as ServerFailureDetails & { id?: number; result?: unknown; error?: string; event?: string; data?: unknown; code?: string; instanceFailure?: InstanceFailureDetails; conflictReport?: ResourceConflictReport; minimumLauncherVersion?: string; currentLauncherVersion?: string; subject?: string }
    if (message.event) {
      listeners.get(message.event)?.forEach((listener) => listener(message.data))
      return
    }
    const entry = message.id === undefined ? undefined : pending.get(message.id)
    if (!entry) return
    pending.delete(message.id!)
    if (message.error !== undefined) {
      const conflict = resourceConflictError(message)
      if (conflict) { entry.reject(conflict); return }
      const busy = instanceFailure(message, key => i18n.global.t(key))
      if (busy) { entry.reject(busy); return }
      if (message.code === 'launcher_update_required') listeners.get('launcher.upgradeRequired')?.forEach(listener => listener(message))
      const removalErrors: Record<string, string> = { client_remove_unsafe: 'unsafe', client_remove_inventory: 'inventory', client_remove_pending: 'pending', client_remove_running: 'running' }
      if (removalErrors[message.error]) { entry.reject(new Error(i18n.global.t(`clientRemoval.${removalErrors[message.error]}`))); return }
      const clientErrors: Record<string, string> = { client_search_limit: 'searchLimit', client_search_multiple: 'multipleClients', client_executable_missing: 'executableMissing', client_installation_overlap: 'overlap' }
      entry.reject(serverFailure({ ...message, error: message.error }, key => i18n.global.t(key)) ?? new Error(message.error === 'plugin_updates_changed' ? i18n.global.t('pluginUpdates.pendingChanged') : clientErrors[message.error] ? i18n.global.t(`clientLibrary.${clientErrors[message.error]}`) : message.error === 'instance_in_use' ? i18n.global.t('updates.fileBusyError') : message.error === 'instance_lock_unavailable' ? i18n.global.t('updates.diskError') : message.error === 'build_disabled' ? i18n.global.t('buildAvailability.disabled') : message.error === 'feature_in_development' ? i18n.global.t('featureAvailability.development') : pluginPolicyError(message.error, (key, values) => i18n.global.t(key, values)) ?? message.error))
    }
    else entry.resolve(message.result)
  })
}

const previewServers: Server[] = __DEV_SERVERS__
const fallback: { [K in keyof BridgeMethods]?: (params?: unknown) => BridgeMethods[K] | Promise<BridgeMethods[K]> } = {
  'hosting.packages': () => [],
  'hosting.availablePorts': () => [17230, 17231, 18090, 18094],
  'hosting.localAddresses': () => [],
  'directory.sources': () => [{ id: 'fantasy', name: 'Bubble Fighter Fantasy', baseUrl: multiplayerPreview ? 'https://directory.example/' : null, enabled: true, builtIn: true }],
  'directory.list': () => ({ schema: 1, entries: multiplayerPreview ? previewDirectory() : [] }),
  'servers.onlinePlayers': () => multiplayerPreview ? previewRoster() : { schema: 1, enabled: false, players: [] },
  'directory.onlinePlayers': () => multiplayerPreview ? previewRoster() : { schema: 1, enabled: false, players: [] },
  'directory.publications': () => [],
  'hosting.list': () => hostingPreview ? previewHosting() : { profiles: [], sessions: [] },
  'hosting.log': () => hostingPreview ? previewHostingLog() : [],
  'hosting.save': p => { if (!hostingPreview) throw new Error('This operation requires the desktop launcher.'); return savePreviewHosting(p as HostingProfile) },
  'hosting.start': p => { if (!hostingPreview) throw new Error('This operation requires the desktop launcher.'); return changePreviewHosting((p as { id: string }).id, 'ready') },
  'hosting.stop': p => { if (!hostingPreview) throw new Error('This operation requires the desktop launcher.'); return changePreviewHosting((p as { id: string }).id, 'stopped') },
  'hosting.delete': p => { if (!hostingPreview) throw new Error('This operation requires the desktop launcher.'); return deletePreviewHosting((p as { id: string }).id) },
  'updates.list': () => ({ checking: false, clients: [], error: null }),
  'launcherUpdates.state': () => ({ status: 'unconfigured', version: null, revision: null, manifestSha256: null, failure: null }),
  'launcherUpdates.lastAttempt': () => null,
  'launcherUpdates.manualDownload': () => ({ url: 'http://archive-cn.600819.xyz:5244/FantasyTale/launcher/win-x64/portable', version: null }),
  'launcherUpdates.check': () => ({ status: 'unconfigured', version: null, revision: null, manifestSha256: null, failure: null }),
  'launcherUpdates.sourceResults': () => [],
  'launcherUpdates.sources': () => JSON.parse(localStorage.getItem('launcherUpdateSources') || '[]') as LauncherUpdateSourceSettings[],
  'launcherUpdates.setSources': p => { const sources = (p as { sources: LauncherUpdateSourceSettings[] }).sources; localStorage.setItem('launcherUpdateSources', JSON.stringify(sources)); return sources },
  'launcherUpdates.preferences': () => JSON.parse(localStorage.getItem('launcherUpdatePreferences') || '{"autoCheck":true,"autoDownload":true}') as LauncherUpdatePreferences,
  'launcherUpdates.setPreferences': p => { localStorage.setItem('launcherUpdatePreferences', JSON.stringify(p)); return p as LauncherUpdatePreferences },
  'updates.check': () => ({ checking: false, clients: [], error: null }),
  'settings.updates': () => ({ autoCheckPlugins: true, autoUpdatePlugins: true, removeOldPluginVersions: true, ...JSON.parse(localStorage.getItem('clientUpdatePreferences') || '{"autoCheck":true,"autoDownload":false}') }) as ClientUpdatePreferences,
  'plugins.updateSnapshot': () => ({ checking: false, checkedAt: null, updates: [], error: null }),
  'plugins.deprecationNotices': () => [],
  'plugins.resolveScheme': p => (p as { plugins: InstancePlugin[] }).plugins,
  'settings.setUpdates': p => { localStorage.setItem('clientUpdatePreferences', JSON.stringify(p)); return p as ClientUpdatePreferences },
  'store.sync': () => [],
  'store.sources': () => previewSources(),
  'store.catalog': p => { if (!isPolicyPreview()) throw new Error('This operation requires the desktop launcher.'); return previewPolicyCatalog((p as { id?: string } | undefined)?.id) },
  'store.installPluginTargets': p => { if (!isPolicyPreview()) throw new Error('This operation requires the desktop launcher.'); return installPreviewPolicyPackage(p) },
  'store.saveSource': p => savePreviewSource(p as StoreSource),
  'store.removeSource': p => removePreviewSource((p as { id: string }).id),
  'app.info': () => ({ version: 'dev', language: navigator.language, os: 'browser' }),
  'instances.list': () => isPolicyPreview() ? previewPolicyInstances() : multiplayerPreview ? previewInstances() : [],
  'pluginPolicy.check': p => { if (!isPolicyPreview()) throw new Error('This operation requires the desktop launcher.'); return checkPreviewPolicy(p) },
  'plugins.configure': p => { if (!isPolicyPreview()) throw new Error('This operation requires the desktop launcher.'); return configurePreviewPolicyPlugin(p) },
  'plugins.applyScheme': () => { if (!isPolicyPreview()) throw new Error('This operation requires the desktop launcher.'); return previewPolicyInstances()[0]! },
  'plugins.pendingUpgrades': () => { if (!isPolicyPreview()) throw new Error('This operation requires the desktop launcher.'); return { count: 0, waitingForExit: 0, failure: null } },
  'instances.recoverStopped': () => null,
  'servers.list': () => previewServers,
  'servers.catalog': (p) => {
    const server = previewServers.find(s => s.id === (p as { id: string }).id)!
    const catalog: ServerCatalog = { schema: 1, game: server.game, name: server.name, registrationEnabled: false, registrationFields: [], instances: server.game === 'bubblefighter' ? previewBuilds.filter(b => b.enabled !== false && !b.hidden).map(b => ({ id: b.id, build: b.id, name: b.name, endpoint: null, launcherLogin: b.launcherLogin ?? 'required', releaseVersion: b.releaseVersion })) : [] }
    return multiplayerPreview ? previewServerCatalog(catalog) : catalog
  },
  'servers.status': () => ({ sections: [], servers: [] }),
  'servers.notices': () => [],
  'servers.markNoticesSeen': () => null,
  'accounts.list': () => ({ current: null, accounts: [] }),
  'accounts.current': () => null,
  'accounts.rememberedLogin': () => null,
  'accounts.refresh': () => null,
  'settings.network': () => ({ ipPreference: (localStorage.getItem('ipPreference') as IpPreference) || 'china', gitHubMirror: localStorage.getItem('githubMirror') === 'on', gitHubMirrorSource: localStorage.getItem('githubMirrorSource') || 'auto', defaultPending: false, suggestionPending: previewMirrorSuggestion() }),
  'settings.showGitHubMirrorSuggestion': () => {
    previewMirrorSuggestionAcknowledged = true
    return fallback['settings.network']!()
  },
  'settings.githubMirrors': () => ({ providers: previewMirrors, results: previewMirrorResults }),
  'settings.testGitHubMirrors': async () => {
    previewMirrorResults = previewMirrors.map(provider => ({ id: provider.id, state: 'testing', milliseconds: null, bytesPerSecond: null }))
    await Promise.all(previewMirrors.map(async (provider, index) => {
      await new Promise(resolve => setTimeout(resolve, 350 + index % 12 * 120))
      previewMirrorResults[index] = { id: provider.id, state: index % 5 === 4 ? 'failed' : 'available', milliseconds: 120 + index * 17, bytesPerSecond: index % 5 === 4 ? null : 1048576 * (2.5 - index % 7 * 0.3) }
    }))
    return previewMirrorResults
  },
  'settings.overlayTest': () => ({ enabled: localStorage.getItem('overlayTest') === 'on', passThroughOutside: localStorage.getItem('overlayPassThroughOutside') !== 'off' }),
  'settings.setOverlayTest': (p) => {
    const settings = p as { enabled: boolean; passThroughOutside?: boolean }
    const passThroughOutside = settings.passThroughOutside ?? localStorage.getItem('overlayPassThroughOutside') !== 'off'
    localStorage.setItem('overlayTest', settings.enabled ? 'on' : 'off')
    localStorage.setItem('overlayPassThroughOutside', passThroughOutside ? 'on' : 'off')
    return { enabled: settings.enabled, passThroughOutside }
  },
  'settings.setNetwork': (p) => {
    const value = p as { ipPreference?: IpPreference; githubMirror?: boolean; githubMirrorSource?: string }
    if (value.ipPreference != null) localStorage.setItem('ipPreference', value.ipPreference)
    if (value.githubMirror != null) localStorage.setItem('githubMirror', value.githubMirror ? 'on' : 'off')
    if (value.githubMirror != null) previewMirrorSuggestionAcknowledged = true
    if (value.githubMirrorSource != null) localStorage.setItem('githubMirrorSource', value.githubMirrorSource)
    return { ipPreference: (localStorage.getItem('ipPreference') as IpPreference) || 'china', gitHubMirror: localStorage.getItem('githubMirror') === 'on', gitHubMirrorSource: localStorage.getItem('githubMirrorSource') || 'auto', defaultPending: false, suggestionPending: previewMirrorSuggestion() }
  },
  'instances.running': () => [],
  'nativeUi.capabilities': () => ({ build: '', operations: [] }),
  'nativeUi.invoke': (p) => ({ operationId: String((p as { request?: { operationId?: string } } | undefined)?.request?.operationId ?? ''), status: 'unsupported', reason: 'providerUnavailable' }),
  'nativeUi.result': (p) => ({ operationId: String((p as { operationId?: string } | undefined)?.operationId ?? ''), status: 'unsupported', reason: 'providerUnavailable' }),
  'instances.services': () => ({}),
  'plugins.list': () => isPolicyPreview() ? previewPolicyPlugins(__DEV_PLUGINS__) : __DEV_PLUGINS__,
  'plugins.loadIssues': () => [],
  'plugins.recoveryNotices': () => [],
  'plugins.dismissRecoveryNotice': () => null,
  'plugins.uninstall': () => null,
  'plugins.uninstallRejected': () => null,
  'plugins.installationPlan': params => {
    const { game, build } = params as { game: string; build: string }
    return (installationPreview as Record<string, Record<string, ClientComponent[]>>)[game]?.[build] ?? []
  },
  'window.theme': () => null,
  'shell.openExternal': (params) => {
    const url = (params as { url?: string } | undefined)?.url
    if (url) window.open(url, '_blank', 'noopener')
    return null
  },
  'games.list': (): GameSummary[] => ([
    {
      id: 'bubblefighter', supportsInstances: true, supportsPlugins: true, launcherLogin: 'required', clientLanguages: [], profileFields: ['level', 'lucci', 'cash'], levelIcon: true, bundledInstances: true, backgroundAudioTracks: [],
      status: 'enabled',
      homeOrder: 0,
      heroTitle: 'Crazy Shooting Bubble Fighter',
      preparing: false, serviceNotice: null,
      name: { en: 'Bubble Fighter', 'zh-CN': '泡泡战士', 'zh-TW': '泡泡大亂鬥', ko: '버블파이터', id: 'Crazy Shooter', vi: 'BF Online' },
      accent: '#2D8CFF',
      backgroundVideo: import.meta.env.DEV ? `${__DEV_GAMES_URL__}/BubbleFighter/media/background.mp4` : null,
      backgroundVideos: import.meta.env.DEV ? [`${__DEV_GAMES_URL__}/BubbleFighter/media/background.mp4`] : [],
      builds: previewBuilds.filter(b => b.enabled !== false && !b.hidden),
    },
    { id: 'talesrunner', supportsInstances: false, supportsPlugins: false, launcherLogin: 'required', clientLanguages: [], profileFields: ['level', 'exp', 'tr', 'cash'], levelIcon: true, bundledInstances: false, backgroundAudioTracks: [], status: 'placeholder', homeOrder: 20, preparing: true,
      name: { en: 'Tales Runner', 'zh-CN': '超级跑跑', 'zh-TW': '跑Online', ko: '테일즈런너', id: 'Tales Runner', vi: 'Tales Runner' },
      serviceNotice: { en: 'Fantasy Runner is currently not available in mainland China.', 'zh-CN': 'Fantasy Runner 目前不在中国大陆地区提供服务。',
        'zh-TW': 'Fantasy Runner 目前不在中國大陸地區提供服務。', ko: 'Fantasy Runner는 현재 중국 본토에서 서비스를 제공하지 않습니다.',
        id: 'Fantasy Runner saat ini tidak menyediakan layanan di Tiongkok daratan.', vi: 'Fantasy Runner hiện không cung cấp dịch vụ tại Trung Quốc đại lục.' },
      accent: '#9C78B6', backgroundImage: import.meta.env.DEV ? `${__DEV_GAMES_URL__}/TalesRunner/media/cover.jpg` : null, backgroundVideo: null, backgroundVideos: [], builds: [] },
    { id: 'airrider', supportsInstances: false, supportsPlugins: true, launcherLogin: 'required', clientLanguages: ['zh-CN', 'zh-TW', 'ko', 'en'], profileFields: ['level', 'lucci', 'cash'], levelIcon: true, bundledInstances: true, backgroundAudioTracks: import.meta.env.DEV ? [`${__DEV_GAMES_URL__}/AirRider/media/background.mp3`] : [], status: 'placeholder', homeOrder: 10,
      preparing: false, serviceNotice: null, heroTitle: 'Crazy Racing Air Rider',
      name: { en: 'Air Rider', 'zh-CN': '跑跑飞空艇', 'zh-TW': '跑跑飛空艇', ko: '에어라이더', id: 'Air Rider', vi: 'Air Rider' },
      accent: '#169DAE', backgroundVideo: import.meta.env.DEV ? `${__DEV_GAMES_URL__}/AirRider/media/background.mp4` : null,
      backgroundVideos: import.meta.env.DEV ? [`${__DEV_GAMES_URL__}/AirRider/media/background.mp4`] : [], builds: [] },
  ] as Omit<GameSummary, 'navigationEntries'>[]).map(game => { const data = (previewLevels as Record<string, { folder: string; catalog: LevelCatalog }>)[game.id]; return { ...game, ...__DEV_GAME_AVAILABILITY__[game.id], navigationEntries: __DEV_GAME_NAVIGATION__[game.id] ?? ['home', 'downloads', 'accounts'], levels: data?.catalog, assetBase: data && import.meta.env.DEV ? `${__DEV_GAMES_URL__}/${data.folder}/` : undefined } }),
}

import { demoMode, setDemoMode, onDemoModeChange, triggerDemoReset } from './demo/mode'
import { demoHostCall } from './demo/host'

export { demoMode, setDemoMode, onDemoModeChange, triggerDemoReset }

/** Dispatches synthetic host events into registered bridge listeners. */
export function emitBridgeEvent(event: string, data?: unknown): void {
  const handlers = listeners.get(event)
  if (!handlers) return
  for (const listener of [...handlers]) {
    listener(data)
  }
}

/** Calls a host method. Rejects with the host's error message. */
export function call<K extends keyof BridgeMethods>(method: K, params?: unknown): Promise<BridgeMethods[K]> {
  if (demoMode.enabled && !(inHost && String(method).startsWith('window.'))) {
    return demoHostCall(method, params, emitBridgeEvent)
  }
  if (!inHost) {
    const answer = fallback[method]
    return answer ? Promise.resolve(answer(params)) : Promise.reject(new Error('This operation requires the desktop launcher.'))
  }
  if (pending.size >= 32) return Promise.reject(new Error('Too many pending launcher operations.'))
  const id = nextId++
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve: resolve as (value: unknown) => void, reject })
    external.sendMessage!(JSON.stringify({ id, method, params }))
  })
}

/** Subscribes to a host event; returns the unsubscribe function. */
export function on(event: string, listener: (data: unknown) => void): () => void {
  if (!listeners.has(event)) listeners.set(event, new Set())
  listeners.get(event)!.add(listener)
  return () => listeners.get(event)?.delete(listener)
}

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import {
  call, demoMode, emitBridgeEvent, setDemoMode, triggerDemoReset,
  type DirectoryEntry, type DirectorySource, type HostingSession, type Instance,
  type PluginDeprecationNotice, type PolicyPluginEntry, type Server, type ServerCatalog,
} from '../bridge'
import { demoController } from '../demo/host'
import { demoScenarioPayloads } from '../demo/fixtures'
import { loadGames, refreshInstances, refreshRunning, refreshServers, state } from '../state'
import { prefs } from '../theme'
import type { StorePackage, StoreSource } from '../store/types'
import { availableStoreUpdates, startStoreUpdateChecks } from '../store/background'
import { downloadAction, downloadTasks, trackDownload } from '../downloads/live'
import { isTerminal } from '../downloads/model'
import { launcherUpdate } from '../downloads/launcherUpdate'
import { pluginNotices, showPluginNotice } from '../plugins/notices'
import { loadSupportOffers } from '../plugins/supportOffers'
import { policyPrompt, requestPluginPolicy } from '../plugins/pluginPolicy'
import { launchWarningPrompt } from '../plugins/launchWarnings'
import { requiredUpdatePrompt } from '../plugins/launchUpdates'
import { recommendationPrompt } from '../plugins/recommendations'
import { resourceConflictPrompt } from '../plugins/resourceConflicts'
import { ServerRequestError } from '../serverFailures'
import { InstanceRequestError } from '../instanceFailures'
import WSettingsCard from '../controls/WSettingsCard.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WExpander from '../controls/WExpander.vue'
import WButton from '../controls/WButton.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import DownloadTaskProgress from './DownloadTaskProgress.vue'
import PackageSummaryCard from './PackageSummaryCard.vue'
import StoreInstallDialog from './StoreInstallDialog.vue'
import StorePluginDialog from './StorePluginDialog.vue'
import StorePluginUpdatesDialog from './StorePluginUpdatesDialog.vue'
import StoreServerDialog from './StoreServerDialog.vue'
import StoreSourcesDialog from './StoreSourcesDialog.vue'
import InstanceDialog from './InstanceDialog.vue'
import ServerDetailsDialog from './ServerDetailsDialog.vue'
import ServerDialog from './ServerDialog.vue'
import OnlinePlayersDialog from './OnlinePlayersDialog.vue'
import ServerPluginListDialog from './ServerPluginListDialog.vue'
import DirectorySourceDialog from './DirectorySourceDialog.vue'
import LocalServerSetupDialog from './LocalServerSetupDialog.vue'
import HostingLogDialog from './HostingLogDialog.vue'
import PluginImportDialog from './PluginImportDialog.vue'
import PluginSchemeShareDialog from './PluginSchemeShareDialog.vue'
import PluginDeprecationDialog from './PluginDeprecationDialog.vue'
import LauncherUpdateFailureDialog from './LauncherUpdateFailureDialog.vue'

const { t } = useI18n()

const storeInstallTarget = ref<{ item: StorePackage; source: StoreSource }>()
const storePluginTarget = ref<{ item: StorePackage; source: StoreSource }>()
const storeServerTarget = ref<{ item: StorePackage; source: StoreSource }>()
const storeUpdatesOpen = ref(false)
const storeSourcesOpen = ref(false)
const storeSourcesList = ref<StoreSource[]>([])

const instanceDialogOpen = ref(false)
const instanceDialogTarget = ref<Instance>()
const serverDetailsOpen = ref(false)
const sampleServerCatalog = ref<ServerCatalog>()
const sampleDirectorySourceId = ref<string>('')
const sampleDirectorySourceName = ref<string>('')
const serverDialogOpen = ref(false)
const serverDialogTarget = ref<Server>()
const onlinePlayersOpen = ref(false)
const serverPluginsOpen = ref(false)
const directorySourcesOpen = ref(false)
const directorySourcesList = ref<DirectorySource[]>([])
const hostingSetupOpen = ref(false)
const hostingLogOpen = ref(false)
const hostingLogSession = ref<HostingSession>()

const pluginImportOpen = ref(false)
const pluginShareOpen = ref(false)
const deprecationNotices = ref<PluginDeprecationNotice[]>([])
const updateFailureKey = ref(0)
const openGroups = reactive({
  store: true,
  instances: true,
  plugins: true,
  updates: true,
  notices: true,
})

const activeDemoDownloads = computed(() =>
  downloadTasks.value.filter(task => !isTerminal(task) && task.target.kind !== 'launcher').slice(0, 3),
)

const sampleRecommendations = computed<PolicyPluginEntry[]>(() => {
  const plugins = sampleServerCatalog.value?.clientPolicy?.plugins ?? []
  const mapped: PolicyPluginEntry[] = plugins.flatMap(plugin =>
    plugin.rules.map(rule => ({
      id: plugin.id,
      builds: rule.builds,
      required: rule.type === 'require',
      disabled: rule.type === 'disable',
    })),
  )
  if (mapped.length) return mapped
  return [
    { id: 'bubblefighter.fantasy.kr-auth', builds: ['kr-812', 'cn-279'], required: true },
    { id: 'bubblefighter.fantasy.hud', builds: ['kr-812', 'cn-279'], required: false },
  ]
})

const demoEnabled = computed({
  get: () => demoMode.enabled,
  set: (value: boolean) => {
    setDemoMode(value)
    if (!value) {
      if (!prefs.developerMode) prefs.developerMode = true
      closeLocalDialogs()
      clearAllBanners()
    }
  },
})

function closeLocalDialogs() {
  storeInstallTarget.value = undefined
  storePluginTarget.value = undefined
  storeServerTarget.value = undefined
  storeUpdatesOpen.value = false
  storeSourcesOpen.value = false
  instanceDialogOpen.value = false
  serverDetailsOpen.value = false
  serverDialogOpen.value = false
  onlinePlayersOpen.value = false
  serverPluginsOpen.value = false
  directorySourcesOpen.value = false
  hostingSetupOpen.value = false
  hostingLogOpen.value = false
  pluginImportOpen.value = false
  pluginShareOpen.value = false
  deprecationNotices.value = []
}

function clearAllBanners() {
  state.error = ''
  state.requestFailure = undefined
  state.launchFailure = undefined
  state.launchFailureMessage = ''
  pluginNotices.splice(0)
}

async function resetMockWorkspace() {
  closeLocalDialogs()
  clearAllBanners()
  policyPrompt.open = false
  launchWarningPrompt.open = false
  requiredUpdatePrompt.open = false
  recommendationPrompt.open = false
  resourceConflictPrompt.open = false
  state.authOpen = false
  state.stopTarget = null
  launcherUpdate.state.dialog = null
  launcherUpdate.state.error = ''
  launcherUpdate.state.consent = ''
  triggerDemoReset()
  await Promise.all([loadGames(), refreshInstances(), refreshServers(), refreshRunning(), launcherUpdate.refresh()])
  showPluginNotice('demo-reset', 'success', t('developerMode.demo.resetDone'))
}

async function firstStorePackage(kind: 'client' | 'plugin' | 'server'): Promise<{ item: StorePackage; source: StoreSource } | undefined> {
  const sources = (await call('store.sources')).filter(s => s.enabled && s.baseUrl)
  for (const source of sources) {
    const catalog = await call('store.catalog', { id: source.id })
    const item = catalog.packages.find(pkg => pkg.kind === kind)
    if (item) return { item, source }
  }
  return undefined
}

async function openStoreDialog(kind: 'client' | 'plugin' | 'server') {
  const found = await firstStorePackage(kind)
  if (!found) return
  if (kind === 'client') storeInstallTarget.value = found
  else if (kind === 'plugin') storePluginTarget.value = found
  else storeServerTarget.value = found
}

async function openStoreUpdates() {
  if (!availableStoreUpdates.value.length) await startStoreUpdateChecks()
  storeUpdatesOpen.value = true
}

async function openStoreSources() {
  storeSourcesList.value = await call('store.sources')
  storeSourcesOpen.value = true
}

function triggerAnimatedDownload(kind: 'client' | 'plugin' | 'server') {
  const task = demoController.triggerInstallDemo(kind)
  trackDownload(task)
}

function demoSampleInstance(): Instance | undefined {
  return state.instances[0] ?? demoController.getWorkspace().instances[0]
}

function demoSampleServer(): Server | undefined {
  return state.servers[0] ?? demoController.getWorkspace().servers[0]
}

function demoSampleDirectoryEntry(): DirectoryEntry | undefined {
  return demoController.getWorkspace().directoryEntries[0]
}

async function loadSampleServerMetadata() {
  const server = demoSampleServer()
  if (server && !sampleServerCatalog.value) {
    try {
      sampleServerCatalog.value = await call('servers.catalog', { id: server.id })
    } catch { /* Handled gracefully if server has no live catalog. */ }
  }
  if (!sampleDirectorySourceId.value) {
    try {
      const sources = await call('directory.sources')
      sampleDirectorySourceId.value = sources[0]?.id ?? ''
      sampleDirectorySourceName.value = sources[0]?.name ?? ''
    } catch { /* No directory sources available. */ }
  }
}

function openInstanceDialog(edit: boolean) {
  instanceDialogTarget.value = edit ? demoSampleInstance() : undefined
  instanceDialogOpen.value = true
}

function openStopGameDialog() {
  const running = state.running[0] ?? demoController.getWorkspace().running[0]
  if (running) {
    state.stopTarget = { instanceId: running.instanceId, processId: running.processId }
    return
  }
  const instance = demoSampleInstance()
  if (instance) {
    state.stopTarget = { instanceId: instance.id, processId: 4096 }
  }
}

function openAuthDialog() {
  state.authOpen = true
}

async function openServerDetails() {
  await loadSampleServerMetadata()
  serverDetailsOpen.value = true
}

function openServerDialog(edit: boolean) {
  serverDialogTarget.value = edit ? demoSampleServer() : undefined
  serverDialogOpen.value = true
}

async function openOnlinePlayers() {
  await loadSampleServerMetadata()
  onlinePlayersOpen.value = true
}

async function openServerPlugins() {
  await loadSampleServerMetadata()
  serverPluginsOpen.value = true
}

async function openDirectorySources() {
  directorySourcesList.value = await call('directory.sources')
  directorySourcesOpen.value = true
}

async function openHostingLog() {
  const overview = await call('hosting.list')
  const running = overview.sessions.find(s => s.state === 'ready' || s.state === 'starting') ?? overview.sessions[0]
  if (running) {
    hostingLogSession.value = running
    hostingLogOpen.value = true
    return
  }
  const profile = overview.profiles[0]
  if (!profile) return
  const session = await call('hosting.start', { id: profile.id })
  hostingLogSession.value = session
  hostingLogOpen.value = true
}

async function triggerPluginPolicyDialog() {
  const instance = demoSampleInstance()
  if (!instance) return
  demoController.preparePluginPolicyDemo(instance.id)
  await refreshInstances()
  try {
    await requestPluginPolicy(instance.id, { launching: true })
  } finally {
    demoController.clearPluginPolicyDemo()
  }
}

function triggerRecommendationsDialog() {
  const instance = demoSampleInstance()
  if (!instance) return
  recommendationPrompt.open = true
  recommendationPrompt.instanceId = instance.id
  recommendationPrompt.name = instance.name
  recommendationPrompt.serverId = demoSampleServer()?.id ?? 'official-kr-812'
  recommendationPrompt.busy = false
  recommendationPrompt.error = ''
  recommendationPrompt.tasks = []
  recommendationPrompt.selected = ['bubblefighter.fantasy.hud']
  recommendationPrompt.items = [
    {
      id: 'bubblefighter.fantasy.kr-auth',
      required: true,
      name: { en: 'KR Auth Helper', 'zh-CN': '韩服认证助手' },
      description: { en: 'Required by the selected server for encrypted session tokens.', 'zh-CN': '所选服务器要求启用以提供加密登录令牌。' },
    },
    {
      id: 'bubblefighter.fantasy.hud',
      required: false,
      name: { en: 'HUD Overlay', 'zh-CN': 'HUD 界面增强' },
      description: { en: 'Displays frame timing, latency and match telemetry in-game.', 'zh-CN': '在游戏内显示延迟、帧率与对局遥测信息。' },
    },
  ]
}

function triggerLaunchWarningDialog() {
  const instance = demoSampleInstance()
  if (!instance) return
  launchWarningPrompt.open = true
  launchWarningPrompt.instanceId = instance.id
  launchWarningPrompt.busy = false
  launchWarningPrompt.error = ''
  launchWarningPrompt.tasks = []
  launchWarningPrompt.items = [
    {
      key: 'bubblefighter.fantasy.hud@1.4.0',
      id: 'bubblefighter.fantasy.hud',
      name: { en: 'HUD Overlay', 'zh-CN': 'HUD 界面增强' },
      version: '1.4.0',
      installed: true,
      warning: {
        en: 'Recommended by the active server and currently disabled on this client.',
        'zh-CN': '当前服务器推荐启用此插件，但它在所选客户端中尚未开启。',
      },
    },
  ]
}

function triggerResourceConflictDialog() {
  const instance = demoSampleInstance()
  resourceConflictPrompt.open = true
  resourceConflictPrompt.selected = {}
  resourceConflictPrompt.report = {
    snapshot: `demo-snapshot-${Date.now()}`,
    build: instance?.build ?? 'kr-812',
    language: instance?.language ?? 'zh-CN',
    conflicts: [
      {
        id: 'conflict-crosshair-color',
        code: 'asset_override',
        archive: 'ui_hud.bfl',
        member: 'ui/hud/crosshair.xml',
        target: 'CrosshairStyle',
        key: 'PrimaryColor',
        field: 'value',
        original: '#FFFFFF',
        parties: [
          {
            pluginId: 'bubblefighter.fantasy.hud',
            version: '1.4.0',
            name: { values: { en: 'HUD Overlay', 'zh-CN': 'HUD 界面增强' } },
            action: 'override',
            value: '#00B7C3',
          },
          {
            pluginId: 'bubblefighter.fantasy.theme',
            version: '2.0.1',
            name: { values: { en: 'Classic Blue Theme', 'zh-CN': '经典蔚蓝界面包' } },
            action: 'override',
            value: '#FFB900',
          },
        ],
      },
    ],
  }
}

function triggerRequiredUpdateDialog() {
  const task = demoController.triggerInstallDemo('plugin')
  trackDownload(task)
  requiredUpdatePrompt.open = true
  requiredUpdatePrompt.items = [
    {
      key: 'bubblefighter.fantasy.kr-auth@1.2.0',
      id: 'bubblefighter.fantasy.kr-auth',
      name: { en: 'KR Auth Helper', 'zh-CN': '韩服认证助手' },
      installedVersion: '1.1.0',
      version: '1.2.0',
      task,
    },
  ]
}

async function triggerSupportOfferDialog() {
  demoController.triggerSupportOffer()
  await loadSupportOffers()
}

function triggerRecoveryNotice() {
  const instance = demoSampleInstance()
  emitBridgeEvent('plugins.recovered', [
    {
      instanceId: instance?.id ?? 'demo-kr-812',
      game: instance?.game ?? 'bubblefighter',
      pluginId: 'bubblefighter.fantasy.hud',
      version: '1.4.0',
    },
  ])
}

function triggerDeprecationNotice() {
  deprecationNotices.value = [
    {
      game: 'bubblefighter',
      pluginId: 'bubblefighter.fantasy.legacy-chat',
      name: { en: 'Legacy Chat Bridge', 'zh-CN': '旧版聊天桥接插件' },
      versions: ['0.8.2', '0.8.3'],
      reason: {
        en: 'Replaced by the built-in native chat renderer in recent builds.',
        'zh-CN': '近期版本已内置原生聊天渲染能力，该插件已停止维护并下线。',
      },
    },
  ]
}

async function triggerAvailableUpdate() {
  demoController.setLauncherUpdateState('available')
  launcherUpdate.state.consent = ''
  launcherUpdate.state.error = ''
  await launcherUpdate.refresh()
  launcherUpdate.show()
}

async function triggerAnimatedLauncherUpdate() {
  demoController.triggerLauncherDownload()
  launcherUpdate.state.error = ''
  await launcherUpdate.refresh()
  launcherUpdate.state.consent = launcherUpdate.state.update.manifestSha256 ?? ''
  launcherUpdate.state.dialog = 'progress'
}

function triggerReadyRestartPrompt() {
  demoController.setLauncherUpdateState('ready')
  const hash = demoController.getWorkspace().launcherUpdate.manifestSha256 ?? 'c'.repeat(64)
  const version = demoController.getWorkspace().launcherUpdate.version ?? '0.9.5'
  const readyTask = {
    id: launcherUpdate.state.task?.id || 'demo-launcher-ready',
    mode: 'live' as const,
    target: {
      kind: 'launcher' as const,
      name: 'Fantasy Tale Launcher',
      fromVersion: '0.9.0',
      toVersion: version,
      sourceName: 'Official GitHub Releases',
      sourceKind: 'github' as const,
      directory: '',
    },
    stage: 'ready' as const,
    files: [{ path: 'FantasyTale.Launcher-setup.exe', size: 68_157_440, received: 68_157_440, stage: 'verified' as const }],
    currentPath: 'FantasyTale.Launcher-setup.exe',
    bytesPerSecond: 0,
    speedSampleCount: 1,
    stageProgress: 100,
    failure: null,
    resumeStage: 'downloading' as const,
  }
  launcherUpdate.state.update = {
    status: 'ready',
    version,
    revision: 3,
    manifestSha256: hash,
    mandatory: false,
    failure: null,
  }
  launcherUpdate.state.task = readyTask
  launcherUpdate.state.error = ''
  launcherUpdate.state.restarting = false
  launcherUpdate.state.dialog = 'confirm'
}

function triggerMandatoryUpgrade() {
  demoController.triggerUpgradeRequired()
}

function triggerUpdateFailureDialog() {
  demoController.setLauncherUpdateState('failed', 'network')
  updateFailureKey.value++
}

async function triggerMirrorSuggestion() {
  demoController.setMirrorSuggestion(true)
  const net = await call('settings.network')
  emitBridgeEvent('settings.network', net)
}

function triggerSuccessNotice() {
  showPluginNotice('demo-success', 'success', t('pluginUpdates.finished', { count: 2 }))
}

function triggerWarningNotice() {
  showPluginNotice('demo-warning', 'warning', t('pluginLoadIssues.summary', { count: 1 }), { persistent: true })
}

function triggerServerError() {
  const payload = demoScenarioPayloads().serverFailure
  const failure = new ServerRequestError(t(`serverFailure.${payload.code}`), {
    code: payload.code,
    httpStatus: payload.httpStatus,
    transportCode: payload.transportCode,
    reason: payload.reason,
    maintenanceStart: payload.maintenanceStart,
    maintenanceEnd: payload.maintenanceEnd,
    maintenanceDescription: payload.maintenanceDescription,
  })
  state.launchFailure = undefined
  state.requestFailure = failure
  state.error = failure.message
}

function triggerLaunchFailureBanner() {
  const payload = demoScenarioPayloads().launchFailure
  const msg = t('launchCleanup.banner', { name: demoSampleInstance()?.name ?? 'KR-812' })
  state.requestFailure = undefined
  state.launchFailure = payload
  state.launchFailureMessage = msg
  state.error = msg
}

function triggerInstanceBusyBanner() {
  const payload = demoScenarioPayloads().instanceFailure
  const err = new InstanceRequestError(t('instanceBusy.message'), payload)
  state.launchFailure = undefined
  state.requestFailure = err
  state.error = err.message
}
</script>

<template>
  <div class="demo-mode-tab">
    <div class="settings-group">
      <WSettingsCard icon="developerTools" :header="t('developerMode.demo.title')" :description="t('developerMode.demo.description')">
        <div class="demo-header-actions">
          <WButton :disabled="!demoMode.enabled" @click="resetMockWorkspace">
            <FluentIcon glyph="refresh" :size="14" />{{ t('developerMode.demo.reset') }}
          </WButton>
          <WToggleSwitch v-model="demoEnabled" :aria-label="t('developerMode.demo.title')" />
        </div>
      </WSettingsCard>
    </div>

    <WInfoBar v-if="!demoMode.enabled" severity="warning" :closable="false">
      {{ t('developerMode.demo.disabledNotice') }}
    </WInfoBar>

    <WExpander v-model:open="openGroups.store" settings icon="download" :header="t('developerMode.demo.groups.store')" :description="t('developerMode.demo.groups.storeDesc')">
      <div class="demo-button-grid">
        <WButton :disabled="!demoMode.enabled" @click="openStoreDialog('client')">{{ t('developerMode.demo.actions.storeClient') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openStoreDialog('plugin')">{{ t('developerMode.demo.actions.storePlugin') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openStoreDialog('server')">{{ t('developerMode.demo.actions.storeServer') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openStoreUpdates">{{ t('developerMode.demo.actions.storePluginUpdates') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openStoreSources">{{ t('developerMode.demo.actions.storeSources') }}</WButton>
        <WButton variant="accent" :disabled="!demoMode.enabled" @click="triggerAnimatedDownload('client')">{{ t('developerMode.demo.actions.animateClient') }}</WButton>
        <WButton variant="accent" :disabled="!demoMode.enabled" @click="triggerAnimatedDownload('plugin')">{{ t('developerMode.demo.actions.animatePlugin') }}</WButton>
        <WButton variant="accent" :disabled="!demoMode.enabled" @click="triggerAnimatedDownload('server')">{{ t('developerMode.demo.actions.animateServer') }}</WButton>
      </div>
      <div v-if="activeDemoDownloads.length" class="demo-live-transfers">
        <span class="demo-live-heading">{{ t('developerMode.demo.activeTransfers') }}</span>
        <PackageSummaryCard
          v-for="task in activeDemoDownloads"
          :key="task.id"
          :glyph="task.target.kind === 'plugin' ? 'puzzle' : task.target.kind === 'server' ? 'hardDrive' : 'gamepad'"
          :name="localized(task.target.nameLocalized ?? undefined) || task.target.name"
        >
          <template #meta>{{ task.target.toVersion }}</template>
          <DownloadTaskProgress :task="task" />
          <div class="demo-transfer-controls">
            <WButton v-if="task.stage === 'downloading' || task.stage === 'queued'" variant="subtle" @click="downloadAction(task, 'pause')">
              <FluentIcon glyph="pause" :size="12" />{{ t('updates.pause') }}
            </WButton>
            <WButton v-else-if="task.stage === 'paused'" variant="subtle" @click="downloadAction(task, 'resume')">
              <FluentIcon glyph="play" :size="12" />{{ t('updates.resume') }}
            </WButton>
            <WButton variant="subtle" @click="downloadAction(task, 'cancel')">
              <FluentIcon glyph="cancel" :size="12" />{{ t('dialog.cancel') }}
            </WButton>
          </div>
        </PackageSummaryCard>
      </div>
    </WExpander>

    <WExpander v-model:open="openGroups.instances" settings icon="gamepad" :header="t('developerMode.demo.groups.instances')" :description="t('developerMode.demo.groups.instancesDesc')">
      <div class="demo-button-grid">
        <WButton :disabled="!demoMode.enabled" @click="openInstanceDialog(false)">{{ t('developerMode.demo.actions.instanceAdd') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openInstanceDialog(true)">{{ t('developerMode.demo.actions.instanceEdit') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openStopGameDialog">{{ t('developerMode.demo.actions.stopGame') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openAuthDialog">{{ t('developerMode.demo.actions.auth') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openServerDetails">{{ t('developerMode.demo.actions.serverDetails') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openServerDialog(true)">{{ t('developerMode.demo.actions.serverEdit') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openOnlinePlayers">{{ t('developerMode.demo.actions.onlinePlayers') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openServerPlugins">{{ t('developerMode.demo.actions.serverPlugins') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openDirectorySources">{{ t('developerMode.demo.actions.directorySources') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="hostingSetupOpen = true">{{ t('developerMode.demo.actions.hostingSetup') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="openHostingLog">{{ t('developerMode.demo.actions.hostingLog') }}</WButton>
      </div>
    </WExpander>

    <WExpander v-model:open="openGroups.plugins" settings icon="puzzle" :header="t('developerMode.demo.groups.plugins')" :description="t('developerMode.demo.groups.pluginsDesc')">
      <div class="demo-button-grid">
        <WButton :disabled="!demoMode.enabled" @click="triggerPluginPolicyDialog">{{ t('developerMode.demo.actions.pluginPolicy') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerRecommendationsDialog">{{ t('developerMode.demo.actions.pluginRecommend') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerLaunchWarningDialog">{{ t('developerMode.demo.actions.pluginLaunchWarning') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerResourceConflictDialog">{{ t('developerMode.demo.actions.pluginConflict') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerRequiredUpdateDialog">{{ t('developerMode.demo.actions.pluginRequiredUpdate') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerSupportOfferDialog">{{ t('developerMode.demo.actions.pluginSupport') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="pluginImportOpen = true">{{ t('developerMode.demo.actions.pluginImport') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="pluginShareOpen = true">{{ t('developerMode.demo.actions.pluginSchemeShare') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerRecoveryNotice">{{ t('developerMode.demo.actions.pluginRecovery') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerDeprecationNotice">{{ t('developerMode.demo.actions.pluginDeprecation') }}</WButton>
      </div>
    </WExpander>

    <WExpander v-model:open="openGroups.updates" settings icon="refresh" :header="t('developerMode.demo.groups.updates')" :description="t('developerMode.demo.groups.updatesDesc')">
      <div class="demo-button-grid">
        <WButton :disabled="!demoMode.enabled" @click="triggerAvailableUpdate">{{ t('developerMode.demo.actions.updateAvailable') }}</WButton>
        <WButton variant="accent" :disabled="!demoMode.enabled" @click="triggerAnimatedLauncherUpdate">{{ t('developerMode.demo.actions.updateProgress') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerReadyRestartPrompt">{{ t('developerMode.demo.actions.updateReady') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerMandatoryUpgrade">{{ t('developerMode.demo.actions.updateMandatory') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerUpdateFailureDialog">{{ t('developerMode.demo.actions.updateFailure') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerMirrorSuggestion">{{ t('developerMode.demo.actions.githubMirror') }}</WButton>
      </div>
    </WExpander>

    <WExpander v-model:open="openGroups.notices" settings icon="info" :header="t('developerMode.demo.groups.notices')" :description="t('developerMode.demo.groups.noticesDesc')">
      <div class="demo-button-grid">
        <WButton :disabled="!demoMode.enabled" @click="triggerSuccessNotice">{{ t('developerMode.demo.actions.noticeSuccess') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerWarningNotice">{{ t('developerMode.demo.actions.noticeWarning') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerServerError">{{ t('developerMode.demo.actions.serverError') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerLaunchFailureBanner">{{ t('developerMode.demo.actions.launchFailure') }}</WButton>
        <WButton :disabled="!demoMode.enabled" @click="triggerInstanceBusyBanner">{{ t('developerMode.demo.actions.instanceBusy') }}</WButton>
        <WButton @click="clearAllBanners">{{ t('developerMode.demo.actions.clearNotices') }}</WButton>
      </div>
    </WExpander>

    <StoreInstallDialog v-if="storeInstallTarget" :open="!!storeInstallTarget" :item="storeInstallTarget.item" :source="storeInstallTarget.source" @close="storeInstallTarget = undefined" />
    <StorePluginDialog v-if="storePluginTarget" :open="!!storePluginTarget" :item="storePluginTarget.item" :source="storePluginTarget.source" @close="storePluginTarget = undefined" />
    <StoreServerDialog v-if="storeServerTarget" :open="!!storeServerTarget" :item="storeServerTarget.item" :source="storeServerTarget.source" @close="storeServerTarget = undefined" />
    <StorePluginUpdatesDialog :open="storeUpdatesOpen" :updates="availableStoreUpdates" @close="storeUpdatesOpen = false" />
    <StoreSourcesDialog :open="storeSourcesOpen" :sources="storeSourcesList" @close="storeSourcesOpen = false" />
    <InstanceDialog :open="instanceDialogOpen" :instance="instanceDialogTarget" @close="instanceDialogOpen = false" />
    <ServerDetailsDialog
      :open="serverDetailsOpen"
      :server="demoSampleServer()"
      :entry="demoSampleDirectoryEntry()"
      :catalog="sampleServerCatalog"
      :source-id="sampleDirectorySourceId"
      :source-name="sampleDirectorySourceName"
      :busy="false"
      @close="serverDetailsOpen = false"
    />
    <ServerDialog :open="serverDialogOpen" :server="serverDialogTarget" @close="serverDialogOpen = false" />
    <OnlinePlayersDialog
      :open="onlinePlayersOpen"
      :game="demoSampleServer()?.game"
      :server-id="demoSampleServer()?.id"
      :source-id="sampleDirectorySourceId"
      :entry-id="demoSampleDirectoryEntry()?.id"
      @close="onlinePlayersOpen = false"
    />
    <ServerPluginListDialog
      :open="serverPluginsOpen"
      :game="demoSampleServer()?.game ?? 'bubblefighter'"
      :builds="demoSampleServer()?.builds ?? ['kr-812']"
      :recommendations="sampleRecommendations"
      :sources="sampleServerCatalog?.clientPolicy?.sources ?? []"
      :server-id="demoSampleServer()?.id"
      @close="serverPluginsOpen = false"
    />
    <DirectorySourceDialog :open="directorySourcesOpen" :sources="directorySourcesList" :selected-id="directorySourcesList[0]?.id" @close="directorySourcesOpen = false" />
    <LocalServerSetupDialog :open="hostingSetupOpen" default-scope="room" @close="hostingSetupOpen = false" />
    <HostingLogDialog :open="hostingLogOpen" :session="hostingLogSession" @close="hostingLogOpen = false" />
    <PluginImportDialog :open="pluginImportOpen" @close="pluginImportOpen = false" />
    <PluginSchemeShareDialog :open="pluginShareOpen" @close="pluginShareOpen = false" />
    <PluginDeprecationDialog :notices="deprecationNotices" @close="deprecationNotices = []" />
    <LauncherUpdateFailureDialog v-if="updateFailureKey > 0" :key="updateFailureKey" />
  </div>
</template>

<style scoped>
.demo-mode-tab { display: grid; gap: 8px; width: 100%; }
.demo-mode-tab :deep(.settings-expander .expander-content) { padding: 16px 24px 16px 72px; }
.settings-group { display: flex; flex-direction: column; gap: 4px; width: 100%; }
.demo-header-actions { display: flex; align-items: center; gap: 12px; }
.demo-button-grid { display: flex; flex-wrap: wrap; gap: 8px; }
.demo-live-transfers { display: grid; gap: 8px; margin-top: 14px; padding-top: 14px; border-top: 1px solid var(--card-stroke-default); }
.demo-live-heading { font-size: 12px; font-weight: 600; color: var(--text-secondary); }
.demo-transfer-controls { display: flex; gap: 8px; margin-top: 8px; }
</style>

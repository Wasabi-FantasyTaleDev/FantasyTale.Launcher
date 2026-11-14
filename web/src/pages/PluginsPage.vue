<script setup lang="ts">
import PageBody from '../components/PageBody.vue'
import MarkdownText from '../components/MarkdownText.vue'
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { call, demoMode, inHost, type OptionValue, type PluginSummary, type PluginUpdateState, type PluginLoadIssue } from '../bridge'
import { downloadAction, downloads, trackDownload } from '../downloads/live'
import { downloadStageKey, isTerminal } from '../downloads/model'
import { localized } from '../i18n'
import PluginSchemePicker from '../components/PluginSchemePicker.vue'
import PluginSchemeShareDialog from '../components/PluginSchemeShareDialog.vue'
import { capturePluginScheme, selectedGame, selectedPluginScheme, pluginSchemesBusy } from '../state'
import { libraryCards, libraryRelease, libraryUpdate, pluginId } from '../plugins/library'
import { settingsPages } from '../plugins/configuration'
import { showPluginNotice } from '../plugins/notices'
import { newestFirst, satisfiesRange } from '../store/versions'
import { matchesPluginIdentity } from '../store/updates'
import { gameInstances, supportsPlugins, isRunning, perform, refreshPlugins, selectedInstance, state } from '../state'
import WButton from '../controls/WButton.vue'
import WComboBox from '../controls/WComboBox.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WDialog from '../controls/WDialog.vue'
import WTextBox from '../controls/WTextBox.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import { requestPluginPolicy } from '../plugins/pluginPolicy'
import PluginImportDialog from '../components/PluginImportDialog.vue'
import PluginConfigurationPage from '../components/PluginConfigurationPage.vue'
import WAnimatedSize from '../controls/WAnimatedSize.vue'
import WProgressRing from '../controls/WProgressRing.vue'
import WProgressBar from '../controls/WProgressBar.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import ShellNotification from '../components/ShellNotification.vue'
import PackageSummaryCard from '../components/PackageSummaryCard.vue'
import DownloadTaskProgress from '../components/DownloadTaskProgress.vue'
import { transferMetrics } from '../downloads/model'
import { createPluginUpdatePreview, pluginUpdatePreviewAvailable } from '../plugins/updatePreview'

const { t } = useI18n()
const route = useRoute()
const preview = computed(() => pluginUpdatePreviewAvailable && !inHost && route.query.pluginUpdatePreview === '1')
const updatePreview = createPluginUpdatePreview()
const sharingScheme = ref(false)
onMounted(() => { if (!inHost && route.query.schemeSharePreview === '1') sharingScheme.value = true })
const search = ref(''), editing = ref<PluginSummary>()
const pluginListReady = ref(true)
const loadedPluginInstances = new Set<string>()
let pluginListRevision = 0
async function loadPluginList() {
  const revision = ++pluginListRevision
  const instance = selectedInstance.value
  const firstLoad = !!instance && !loadedPluginInstances.has(instance.id)
    && !state.plugins.some(plugin => plugin.game === instance.game)
  pluginListReady.value = !firstLoad
  try { await refreshPlugins() }
  catch (error) { if (!stopped && revision === pluginListRevision && instance?.id === selectedInstance.value?.id) { state.requestFailure = error as Error; state.error = (error as Error).message } }
  finally {
    if (instance) loadedPluginInstances.add(instance.id)
    if (revision === pluginListRevision) pluginListReady.value = true
  }
}
onMounted(() => { void loadPluginList() })
onMounted(() => {
  if (preview.value && route.query.pluginNoticePreview === '1') {
    checkNotice.value = true
    pendingUpgrades.value = 4
    discoveryError.value = 'integrity'
  }
})
const configurationContext = ref<Record<string, unknown>>({})
const configurationHtml = reactive<Record<string, string>>({}), configurationErrors = reactive<Record<string, string>>({}), activeConfiguration = ref(0)
const configurationPages = computed(() => settingsPages(editing.value, t('pluginConfiguration.standard'), t('pluginConfiguration.custom')))
const pageOptions = (page: { options: string[] }) => editing.value?.options.filter(option => page.options.includes(option.key)) ?? []
const pageValues = (page: { options: string[] }) => Object.fromEntries(page.options.map(key => [key, values[key]!]))
const configurationPending = computed(() => configurationPages.value.some(page => page.kind === 'html' && configurationHtml[page.id] === undefined))
let editorRevision = 0
const importing = ref(false), importedMessage = ref(''), switching = ref<{ plugin: PluginSummary; from: string; instanceId: string }>()
const preserveOptions = ref(true)
const updates = ref<PluginUpdateState[]>([]), checking = ref(false), checked = ref(false)
const discoveryError = ref('')
const checkNotice = ref(false)
const dismissedNotices = reactive(new Set<string>())
const loadIssues = ref<PluginLoadIssue[]>([]), issueDetails = ref<PluginLoadIssue>()
const uninstallingIssue = ref<PluginLoadIssue>(), uninstalling = ref<PluginSummary>(), uninstallBusy = ref(false)
const issueChecks = reactive<Record<string, { instanceId: string; update: PluginUpdateState }>>({})
const issueUpdate = (issue: PluginLoadIssue) => issueChecks[issue.folder]?.update ?? updates.value.find(item => item.id === issue.id && item.installedVersion === issue.version)
const issueTask = (issue: PluginLoadIssue) => downloads.tasks.find(task => task.id === updateTasks[`${issue.id}:${issue.version}`])
const issueProgress = (issue: PluginLoadIssue) => { const task = issueTask(issue); return task ? task.stage === 'downloading' ? transferMetrics(task).percent : task.stageProgress : null }
const pendingUpgrades = ref(0), waitingForExit = ref(0), activationFailure = ref('')
let snapshotTimer: ReturnType<typeof setTimeout> | undefined, stopped = false
const updateTasks = reactive<Record<string, string>>({})
const startingUpdates = reactive(new Set<string>())
let checkRevision = 0, contextRevision = 0
const key = (plugin: PluginSummary) => `${pluginId(plugin)}:${plugin.version}`
const update = (plugin: PluginSummary) => preview.value ? updatePreview.update(plugin) : libraryUpdate(plugin, selectedInstance.value?.game, updates.value)
const updateTask = (plugin: PluginSummary) => preview.value ? updatePreview.task(plugin) : downloads.tasks.find(task => task.id === updateTasks[key(plugin)])
  ?? downloads.tasks.find(task => task.target.kind === 'plugin' && task.target.gameId === plugin.game
    && task.target.packageId === (update(plugin)?.targetId ?? plugin.id) && !isTerminal(task))
const updateTaskActive = (plugin: PluginSummary) => {
  const task = updateTask(plugin)
  return startingUpdates.has(key(plugin)) || !!task && !['completed', 'canceled', 'failed', 'paused'].includes(task.stage)
}
const hasUpdate = (plugin: PluginSummary) => {
  const item = update(plugin), installed = selectedInstance.value?.plugins.find(entry => entry.id === plugin.id)?.version
  return item?.status === 'available' || item?.status === 'installed' && installed !== item.version
}
const updateVersion = (plugin: PluginSummary) => hasUpdate(plugin) ? `${plugin.version} → ${update(plugin)?.version}` : plugin.version
const availableCount = computed(() => new Set(updates.value.filter(u => u.status === 'available').map(u => u.id)).size)
const notices = computed(() => {
  const items: { key: string; severity: 'info' | 'success' | 'warning' | 'error'; title: string; message: string }[] = []
  if (checkNotice.value) items.push({ key: 'check', severity: 'success', title: t('pluginUpdates.checked'),
    message: t(availableCount.value ? 'pluginUpdates.found' : 'pluginUpdates.current', { count: availableCount.value }) })
  if (pendingUpgrades.value && !activationFailure.value) items.push({ key: `pending:${pendingUpgrades.value}`, severity: 'info', title: '',
    message: t(waitingForExit.value ? 'pluginUpdates.waitingForExit' : 'pluginUpdates.pendingActivation', { count: pendingUpgrades.value }) })
  if (activationFailure.value) items.push({ key: 'activation-failed', severity: 'warning', title: '', message: t('pluginUpdates.activationFailed') })
  const errors = new Set([discoveryError.value,
    ...updates.value.map(update => update.status)].filter(status => ['integrity', 'network', 'disk'].includes(status)))
  for (const error of errors) items.push({ key: `error:${error}`, severity: 'warning', title: '', message: t(`pluginUpdates.${error}`) })
  return items.filter(item => !dismissedNotices.has(item.key))
})
const activeDownload = computed(() => downloads.tasks.some(task => task.target.instanceId === selectedInstance.value?.id && !isTerminal(task)))
const values = reactive<Record<string, OptionValue>>({})
const choices = computed(() => gameInstances.value.map(i => ({ value: i.id, label: localized(selectedGame.value?.builds.find(b => b.id === i.build)?.name, { dates: true }) || i.name })))
const libraryVersions = reactive<Record<string, string>>({})
const versions = (plugin: PluginSummary) => preview.value ? [plugin] : newestFirst(state.plugins.filter(p => pluginId(p) === pluginId(plugin)))
const plugins = computed(() => libraryCards(preview.value ? updatePreview.plugins : state.plugins, libraryVersions)
  .filter(p => `${localized(p.name)} ${p.id}`.toLowerCase().includes(search.value.toLowerCase()))
  // Keep packages usable for the selected client at the top while preserving
  // the catalog order within each group.
  .sort((left, right) => Number(!left.available) - Number(!right.available)))
const locked = computed(() => !(inHost || demoMode?.enabled) || !supportsPlugins.value || state.busy || pluginSchemesBusy.value || activeDownload.value || !selectedInstance.value)
function nextLaunchNotice(instanceId = selectedInstance.value?.id) {
  if (instanceId && isRunning(instanceId)) showPluginNotice('next-launch', 'info', t('pluginUpdates.nextLaunch'))
}
watch(() => state.plugins.filter(plugin => plugin.selection).map(plugin => [pluginId(plugin), plugin.version] as const), (current, previous) => {
  for (const [id, version] of current) {
    const before = previous.find(([identity]) => identity === id)?.[1]
    if (before && before !== version && libraryVersions[id] === before) delete libraryVersions[id]
  }
})
watch([() => state.selectedInstanceId, () => state.selectedServerId, selectedPluginScheme], () => {
  checkRevision++; contextRevision++; checking.value = false
  updates.value = []; checked.value = false; importedMessage.value = ''; switching.value = undefined; closeEditor()
  checkNotice.value = false; dismissedNotices.clear()
  pendingUpgrades.value = 0; waitingForExit.value = 0; activationFailure.value = ''
  discoveryError.value = ''; loadIssues.value = []; issueDetails.value = undefined; uninstallingIssue.value = undefined; uninstalling.value = undefined
  for (const id of Object.keys(libraryVersions)) delete libraryVersions[id]
  for (const id of Object.keys(updateTasks)) delete updateTasks[id]
  for (const folder of Object.keys(issueChecks)) delete issueChecks[folder]
}, { flush: 'sync' })
watch([() => selectedInstance.value?.id, supportsPlugins], () => {
  if (selectedInstance.value && supportsPlugins.value) void loadPluginList()
}, { flush: 'post' })
onMounted(() => { if (inHost || demoMode?.enabled) void pollSnapshot() })
watch(() => Boolean(demoMode?.enabled), enabled => {
  if (enabled) {
    if (!snapshotTimer) void pollSnapshot()
    if (selectedInstance.value && supportsPlugins.value) void loadPluginList()
  }
})
onBeforeUnmount(() => { checkRevision++; contextRevision++; closeEditor(); updatePreview.stop(); stopped = true; if (snapshotTimer) clearTimeout(snapshotTimer) })
async function pollSnapshot() {
  const game = state.selectedGameId
  const pendingInstance = selectedInstance.value?.id
  try {
    const issues = await call('plugins.loadIssues', { game })
    if (!stopped && game === state.selectedGameId) loadIssues.value = issues
    const pending = pendingInstance ? await call('plugins.pendingUpgrades', { instanceId: pendingInstance }) : undefined
    if (!stopped && pendingInstance === selectedInstance.value?.id) {
      pendingUpgrades.value = pending?.count ?? 0; waitingForExit.value = pending?.waitingForExit ?? 0; activationFailure.value = pending?.failure ?? ''
    }
  } catch { if (!stopped && pendingInstance === selectedInstance.value?.id) activationFailure.value = 'disk' }
  const id = selectedInstance.value?.id, revision = checkRevision
  if (id && supportsPlugins.value && !checking.value) {
    try {
      const snapshot = await call('plugins.updateSnapshot', { instanceId: id })
      if (!stopped && revision === checkRevision && !checking.value) {
        if (snapshot.checkedAt) { updates.value = snapshot.updates; checked.value = true }
        discoveryError.value = snapshot.error ?? ''
      }
    } catch (error) { if (!stopped && revision === checkRevision) discoveryError.value = 'network' }
  }
  if (!stopped) snapshotTimer = setTimeout(pollSnapshot, 2000)
}
async function checkUpdates() {
  const id = selectedInstance.value?.id
  if (!id || checking.value || !(inHost || demoMode?.enabled) || stopped) return
  const revision = ++checkRevision; checking.value = true
  checkNotice.value = false; dismissedNotices.clear()
  try {
    const result = await call('plugins.checkUpdates', { instanceId: id, serverId: state.selectedServerId || undefined })
    if (!stopped && revision === checkRevision) { updates.value = result; checked.value = true; checkNotice.value = true; discoveryError.value = '' }
  } catch (error) { if (!stopped && revision === checkRevision) { state.requestFailure = error as Error; state.error = (error as Error).message } }
  finally { if (!stopped && revision === checkRevision) checking.value = false }
}
async function downloadUpdate(plugin: PluginSummary) {
  const item = update(plugin), instanceId = selectedInstance.value?.id
  if (!item || (item.status !== 'available' && item.status !== 'installed') || !instanceId) return
  const revision = contextRevision, serverId = state.selectedServerId || undefined
  await perform(async () => {
    const task = await call('plugins.installUpdate', { instanceId, id: item.id, installedVersion: item.installedVersion,
      version: item.version, manifestSha256: item.manifestSha256, serverId })
    trackDownload(task)
    if (!stopped && revision === contextRevision) updateTasks[key(plugin)] = task.id
  })
}
async function repairIssue(issue: PluginLoadIssue) {
  const item = issueUpdate(issue), instanceId = issueChecks[issue.folder]?.instanceId ?? selectedInstance.value?.id
  if (!item || !['available', 'installed'].includes(item.status) || !instanceId) return
  const revision = contextRevision
  await perform(async () => {
    const task = await call('plugins.installUpdate', { instanceId, id: item.id, installedVersion: item.installedVersion,
      version: item.version, manifestSha256: item.manifestSha256 })
    trackDownload(task)
    if (!stopped && revision === contextRevision) updateTasks[`${issue.id}:${issue.version}`] = task.id
  })
}
async function updateIssue(issue: PluginLoadIssue) {
  if (!issue.id || !issue.version || checking.value || stopped) return
  if (['available', 'installed'].includes(issueUpdate(issue)?.status ?? '')) { await repairIssue(issue); return }
  const game = issue.game ?? selectedInstance.value?.game
  const candidates = [selectedInstance.value, ...gameInstances.value].filter(instance => instance?.game === game)
  const revision = ++checkRevision; checking.value = true
  try {
    for (const instanceId of new Set(candidates.map(instance => instance!.id))) {
      const result = await call('plugins.checkUpdates', { instanceId })
      if (stopped || revision !== checkRevision) return
      const update = result.find(item => item.id === issue.id && item.installedVersion === issue.version)
      if (!update) continue
      issueChecks[issue.folder] = { instanceId, update }
      if (['available', 'installed'].includes(update.status)) break
    }
  } catch (error) { if (!stopped && revision === checkRevision) { state.requestFailure = error as Error; state.error = (error as Error).message } }
  finally { if (!stopped && revision === checkRevision) checking.value = false }
}
async function uninstallPlugin() {
  const plugin = uninstalling.value
  if (!plugin || uninstallBusy.value) return
  uninstallBusy.value = true
  const running = state.running?.some(client => state.instances.some(instance => instance.id === client.instanceId && instance.game === plugin.game))
  try {
    await call('plugins.uninstall', { game: plugin.game, id: plugin.id })
    if (uninstalling.value === plugin) uninstalling.value = undefined
    if (!stopped) await refreshPlugins()
    if (running) showPluginNotice('next-launch', 'info', t('pluginUpdates.nextLaunch'))
  } catch (error) { if (!stopped) { state.requestFailure = error as Error; state.error = (error as Error).message } }
  finally { uninstallBusy.value = false }
}
async function uninstallIssue() {
  const issue = uninstallingIssue.value, game = state.selectedGameId
  if (!issue || uninstallBusy.value) return
  const running = state.running?.some(client => state.instances.some(instance => instance.id === client.instanceId && instance.game === game))
  uninstallBusy.value = true
  try {
    if (issue.id) await call('plugins.uninstall', { game, id: issue.id })
    else await call('plugins.uninstallRejected', { game, folder: issue.folder })
    if (!stopped && game === state.selectedGameId) {
      loadIssues.value = loadIssues.value.filter(item => issue.id ? item.id !== issue.id : item.folder !== issue.folder)
      if (uninstallingIssue.value === issue) uninstallingIssue.value = undefined; uninstalling.value = undefined
      await refreshPlugins()
      if (running) showPluginNotice('next-launch', 'info', t('pluginUpdates.nextLaunch'))
    }
  } catch (error) { if (!stopped) { state.requestFailure = error as Error; state.error = (error as Error).message } }
  finally { uninstallBusy.value = false }
}
async function activateUpdate(plugin: PluginSummary) {
  const offer = update(plugin)
  if (offer?.targetId && offer.targetId !== plugin.id) { await downloadUpdate(plugin); return }
  const target = libraryRelease(state.plugins, plugin, offer?.version)
  if (target) {
    const previous = selectedInstance.value?.plugins.find(entry => entry.id === plugin.id)
    if (previous?.enabled && selectedInstance.value && !await requestPluginPolicy(selectedInstance.value.id, { enabling: { id: target.id, version: target.version } })) return
    await configure(target, previous?.enabled ?? false, previous?.options, true)
  }
}
async function applyUpdate(plugin: PluginSummary) {
  if (preview.value) { updatePreview.start(plugin); return }
  if (startingUpdates.has(key(plugin))) return
  startingUpdates.add(key(plugin))
  try {
    const task = updateTask(plugin)
    if (task?.stage === 'failed' || task?.stage === 'paused') {
      await downloadAction(task, task.stage === 'failed' ? 'retry' : 'resume')
    } else if (update(plugin)?.status === 'installed') await activateUpdate(plugin)
    else await downloadUpdate(plugin)
  } catch (error) { state.requestFailure = error as Error; state.error = (error as Error).message }
  finally { startingUpdates.delete(key(plugin)) }
}
watch(() => downloads.tasks.filter(task => Object.values(updateTasks).includes(task.id) && task.stage === 'completed').map(task => task.id).join(','), async (done, before) => {
  if (done && done !== before) {
    const revision = contextRevision
    try {
      for (const [entry, taskId] of Object.entries(updateTasks)) {
        if (!downloads.tasks.some(task => task.id === taskId && task.stage === 'completed')) continue;
        const split = entry.lastIndexOf(':'), identity = entry.slice(0, split), baseline = entry.slice(split + 1)
        if (libraryVersions[identity] === baseline) delete libraryVersions[identity]
      }
      await refreshPlugins()
      if (stopped || revision !== contextRevision) return
      if (!stopped && revision === contextRevision) await checkUpdates()
    } catch (error) { if (!stopped && revision === contextRevision) { state.requestFailure = error as Error; state.error = (error as Error).message } }
  }
})
function showUpdate(plugin: PluginSummary) { return update(plugin)?.status === 'installed' }
function closeEditor() {
  editorRevision++; editing.value = undefined; activeConfiguration.value = 0
  for (const key of Object.keys(configurationHtml)) delete configurationHtml[key]
  for (const key of Object.keys(configurationErrors)) delete configurationErrors[key]
}
function configurationKeydown(event: KeyboardEvent) {
  if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return
  event.preventDefault()
  const count = configurationPages.value.length
  activeConfiguration.value = event.key === 'Home' ? 0 : event.key === 'End' ? count - 1
    : (activeConfiguration.value + (event.key === 'ArrowRight' ? 1 : -1) + count) % count
  const tabs = (event.currentTarget as HTMLElement).querySelectorAll<HTMLButtonElement>('[role=tab]')
  tabs[activeConfiguration.value]?.focus()
}
async function edit(plugin: PluginSummary) {
  closeEditor()
  const revision = ++editorRevision
  for (const key of Object.keys(values)) delete values[key]
  for (const option of plugin.options) values[option.key] = plugin.selection?.options[option.key] ?? option.default
  editing.value = plugin
  configurationContext.value = {}
  try {
    const context = plugin.resourceSettings ? await call('plugins.configurationContext', { instanceId: selectedInstance.value!.id, id: plugin.id, version: plugin.version }) : {}
    if (!stopped && revision === editorRevision && editing.value === plugin) configurationContext.value = context
  } catch (error) {
    if (!stopped && revision === editorRevision && editing.value === plugin) {
      for (const page of configurationPages.value.filter(page => page.kind === 'html')) configurationErrors[page.id] = String(error)
      return
    }
  }
  await Promise.all(configurationPages.value.filter(page => page.kind === 'html').map(async page => {
    try {
      const html = await call('plugins.configurationPage', { instanceId: selectedInstance.value!.id, id: plugin.id, version: plugin.version,
        ...(plugin.configurationPages ? { pageId: page.id } : {}) })
      if (!stopped && revision === editorRevision && editing.value === plugin) configurationHtml[page.id] = html
    } catch (error) {
      if (!stopped && revision === editorRevision && editing.value === plugin) configurationErrors[page.id] = (error as Error).message
    }
  }))
}
async function configure(plugin: PluginSummary, enabled: boolean, options = plugin.selection?.options, selectVersion = false) {
  const instance = selectedInstance.value!
  const changed = await perform(() => call('plugins.configure', { instanceId: instance.id, id: plugin.id, version: plugin.version, enabled, options,
    ...(selectVersion ? { selectVersion: true, expectedVersion: instance.plugins.find(p => p.id === plugin.id)?.version ?? null } : {}),
    serverId: state.selectedServerId || undefined }))
  if (changed) nextLaunchNotice(instance.id)
  return changed
}
async function save() {
  const plugin = editing.value, revision = editorRevision
  const instance = selectedInstance.value
  const previous = instance?.plugins.find(p => p.id === plugin?.id)
  if (!plugin || !instance || state.busy) return
  state.busy = true; state.error = ''; state.requestFailure = undefined
  try {
    const changed = await call('plugins.configure', { instanceId: instance.id, id: plugin.id, version: plugin.version,
      enabled: previous?.enabled ?? false, options: { ...values }, selectVersion: true,
      expectedVersion: instance.plugins.find(p => p.id === plugin.id)?.version ?? null,
      serverId: state.selectedServerId || undefined, deferRefresh: true })
    if (stopped || revision !== editorRevision) return
    // Configuration returns persisted settings, without the installation inventory.
    state.instances = state.instances.map(item => item.id === changed.id ? { ...item, ...changed } : item)
    capturePluginScheme(changed)
    const selection = changed.plugins.find(item => item.id === plugin.id && item.version === plugin.version) ?? null
    state.plugins = state.plugins.map(item => item.id === plugin.id && item.version === plugin.version
      ? { ...item, selection } : item)
    closeEditor()
    nextLaunchNotice(instance.id)
  } catch (error) {
    state.requestFailure = error as Error; state.error = (error as Error).message
  } finally { state.busy = false }
}
async function selectVersion(plugin: PluginSummary, version: string) {
  const target = libraryRelease(state.plugins, plugin, version), revision = contextRevision, instance = selectedInstance.value
  if (!target) return
  if (!instance || !supportsPlugins.value || !target.available) {
    libraryVersions[pluginId(plugin)] = version; return
  }
  if (locked.value) return
  const previous = instance.plugins.find(p => p.id === plugin.id)
  if (previous?.enabled && !await requestPluginPolicy(instance.id, { enabling: { id: target.id, version: target.version } },
    () => !stopped && revision === contextRevision && selectedInstance.value?.id === instance.id)) return
  if (await configure(target, previous?.enabled ?? false, undefined, true)
    && !stopped && revision === contextRevision) libraryVersions[pluginId(plugin)] = version
}
async function toggle(plugin: PluginSummary, enabled: boolean) {
  const instance = selectedInstance.value, revision = contextRevision
  const previous = instance?.plugins.find(p => p.id === plugin.id)
  if (enabled && previous && previous.version !== plugin.version) {
    preserveOptions.value = true
    switching.value = { plugin, from: previous.version, instanceId: instance!.id }; return
  }
  // Turning a plugin on checks its unmet requirements, recommendations and conflicts; turning it off checks active dependents.
  if (instance && !await requestPluginPolicy(instance.id, enabled
    ? { enabling: { id: plugin.id, version: plugin.version } }
    : { disabling: { id: plugin.id, version: plugin.version } },
    () => !stopped && revision === contextRevision && selectedInstance.value?.id === instance.id)) return
  await configure(plugin, enabled)
}
/** Whether a policy rule chip is currently satisfied on the selected client or library. */
function ruleMet(rule: NonNullable<PluginSummary['policy']>[number]) {
  if (typeof rule.satisfied === 'boolean') return rule.satisfied
  const targetPackages = state.plugins.filter(item => matchesPluginIdentity(item, rule.plugin) && (!rule.range || satisfiesRange(item.version, rule.range)))
  const enabled = !!selectedInstance.value?.plugins.some(selection => selection.enabled
    && (!rule.range || satisfiesRange(selection.version, rule.range))
    && (selection.id === rule.plugin || targetPackages.some(item => item.id === selection.id && item.version === selection.version)))
  if (rule.type === 'conflict') return !enabled
  if (targetPackages.some(item => item.kind === 'assets' && (!selectedInstance.value || item.available))) return true
  return enabled
}
function policyName(other: string) {
  const found = state.plugins.find(item => matchesPluginIdentity(item, other))
  return found ? localized(found.name) : other
}
async function switchVersion() {
  const change = switching.value, revision = contextRevision
  if (!change || change.instanceId !== selectedInstance.value?.id) { switching.value = undefined; return }
  if (!await requestPluginPolicy(change.instanceId, { enabling: { id: change.plugin.id, version: change.plugin.version } },
    () => !stopped && revision === contextRevision && switching.value === change && selectedInstance.value?.id === change.instanceId)) {
    if (switching.value === change) switching.value = undefined
    return
  }
  if (await configure(change.plugin, true, preserveOptions.value ? undefined : {})
    && !stopped && revision === contextRevision && switching.value === change) switching.value = undefined
}
function imported(name: string, version: string) { importedMessage.value = t('pluginImport.success', { name, version }) }
</script>

<template>
  <section class="page plugin-page">
    <header class="page-header"><div><h1 class="page-title">{{ t('plugins.title') }}</h1><p class="page-desc">{{ t('pluginImport.library') }}</p></div><div class="plugin-context"><PluginSchemePicker /><WComboBox v-if="choices.length" v-model="state.selectedInstanceId" :options="choices" :placeholder="t('live.selectInstance')" :aria-label="t('live.selectInstance')" :disabled="state.busy" class="instance-picker" /></div></header>
    <div class="plugin-toolbar"><WTextBox v-model="search" :placeholder="t('plugins.search')" :aria-label="t('plugins.search')" /><span v-if="importedMessage" class="import-result" role="status">{{ importedMessage }}</span><div class="toolbar-actions"><WButton :disabled="!selectedInstance || state.busy" @click="sharingScheme = true"><FluentIcon glyph="share" />{{ t('pluginSchemes.share') }}</WButton><WButton :disabled="!selectedInstance || !supportsPlugins || state.busy" @click="$router.push({ path: '/store', query: { kind: 'plugin' } })"><FluentIcon glyph="download" />{{ t('pluginImport.download') }}</WButton><WButton :disabled="!selectedInstance || !supportsPlugins || !(inHost || !!demoMode?.enabled) || checking || state.busy" @click="checkUpdates"><FluentIcon glyph="refresh" :class="{ spinning: checking }" />{{ t(checking ? 'pluginUpdates.checking' : 'pluginUpdates.check') }}</WButton><WButton :disabled="!(inHost || !!demoMode?.enabled) || state.busy || !state.selectedGameId" @click="importing = true; importedMessage = ''"><FluentIcon glyph="add" />{{ t('pluginImport.title') }}</WButton></div></div>
    <PageBody>
    <WInfoBar v-if="preview" class="plugin-preview-notice" severity="info" :closable="false" :title="t('updates.preview')">{{ t('updates.previewBody') }}</WInfoBar>
    <p v-else-if="!preview && !selectedInstance" class="muted">{{ t('pluginImport.chooseClient') }}</p>
    <p v-else-if="!supportsPlugins" class="muted">{{ t('service.pluginsUnsupported') }}</p>

      <TransitionGroup name="plugin-notice" tag="div" class="plugin-notices">
        <ShellNotification v-for="notice in notices" :key="notice.key" :severity="notice.severity" :title="notice.title" :message="notice.message" :persistent="notice.key.startsWith('pending:') ? true : undefined" @close="dismissedNotices.add(notice.key)" />
      </TransitionGroup>
      <div v-if="loadIssues.length" class="load-issues" role="status">
        <WInfoBar severity="warning" :closable="false">{{ t('pluginLoadIssues.summary', { count: loadIssues.length }) }}</WInfoBar>
        <WInfoBar v-for="issue in loadIssues" :key="issue.folder" severity="warning" :closable="false" :title="`${issue.id ?? t('pluginLoadIssues.unknown')} ${issue.version ?? ''}`">
          {{ issueUpdate(issue)?.status === 'available' ? t('pluginUpdates.available', { version: issueUpdate(issue)?.version }) : t('pluginLoadIssues.skipped') }}
          <template #action>
            <div v-if="issueTask(issue)" class="issue-progress"><WProgressBar :value="issueProgress(issue) ?? 0" :indeterminate="issueProgress(issue) == null" /><span>{{ t(`updates.${issueTask(issue)!.stage}`) }}<template v-if="issueProgress(issue) != null"> · {{ Math.floor(issueProgress(issue)!) }}%</template></span></div>
            <div class="issue-actions">
              <WButton v-if="issue.id && issue.version" :disabled="state.busy || checking || !selectedInstance || !!issueTask(issue) && !isTerminal(issueTask(issue)!)" @click="updateIssue(issue)">{{ checking ? t('pluginUpdates.checking') : ['available', 'installed'].includes(issueUpdate(issue)?.status ?? '') ? t('pluginLoadIssues.repair') : t('pluginUpdates.check') }}</WButton>
              <WButton :disabled="state.busy || uninstallBusy || !!issueTask(issue) && !isTerminal(issueTask(issue)!)" @click="uninstallingIssue = issue">{{ t('pluginLoadIssues.uninstall') }}</WButton>
              <WButton variant="subtle" @click="issueDetails = issue">{{ t('pluginLoadIssues.details') }}</WButton>
            </div>
          </template>
        </WInfoBar>
      </div>
      <TransitionGroup name="plugin-reorder" tag="div" class="plugin-list">
        <article v-for="plugin in plugins" :key="pluginId(plugin)" class="plugin-row surface" :class="{ 'plugin-unavailable': !plugin.available }">
          <WAnimatedSize>
          <div class="plugin-main">
          <span class="plugin-icon"><FluentIcon :glyph="plugin.kind === 'native' ? 'code' : 'puzzle'" :size="22" /></span>
          <div class="plugin-text">
            <TransitionGroup name="plugin-meta" tag="div" class="plugin-heading">
              <h2 key="name">{{ localized(plugin.name) }}</h2>
              <WAnimatedSize key="version" axis="width"><Transition name="version-swap"><span :key="updateVersion(plugin)" class="version">{{ updateVersion(plugin) }}</span></Transition></WAnimatedSize>
              <span v-if="plugin.builtIn" key="built-in" class="builtin-badge">{{ t('plugins.builtIn') }}</span>
              <span v-if="hasUpdate(plugin)" key="update" class="update-badge">{{ t('pluginUpdates.badge') }}</span>
              <span v-if="selectedInstance && !plugin.available" key="unavailable" class="unavailable-badge">{{ t('live.unavailable') }}</span>
              <span v-if="plugin.required || plugin.warningRequired" key="required" class="required">{{ t('live.required') }}</span>
            </TransitionGroup>
            <MarkdownText v-if="localized(plugin.description ?? undefined)" :source="localized(plugin.description ?? undefined)" />
            <p v-if="plugin.authors?.length" class="muted">{{ t('store.authors') }}: {{ plugin.authors.join(', ') }}</p>
            <ul v-if="plugin.policy?.length" class="policy-rules">
              <li v-for="rule in plugin.policy" :key="rule.type + rule.plugin" class="policy-rule" :class="[rule.type, { met: ruleMet(rule) }]">
                <FluentIcon :glyph="ruleMet(rule) ? 'completed' : rule.type === 'conflict' ? 'errorBadge' : rule.type === 'require' ? 'warning' : 'info'" :size="14" />
                {{ t(`pluginPolicy.chip.${rule.type}`, { name: policyName(rule.plugin) }) }}
              </li>
            </ul>
            <span v-if="plugin.minimumLauncherVersion && plugin.launcherCompatible === false" class="unavailable">{{ t('launcherUpdates.pluginMinimum', { version: plugin.minimumLauncherVersion }) }}</span>
          </div>
          <div class="plugin-release-actions">
          <WComboBox v-if="versions(plugin).length > 1" :model-value="plugin.version" :options="versions(plugin).map(p => ({ value: p.version, label: p.version }))" :aria-label="t('pluginImport.packageVersion')" :disabled="state.busy" class="package-version" @update:model-value="selectVersion(plugin, $event)" />
          <Transition name="update-action"><WButton v-if="hasUpdate(plugin) || updateTaskActive(plugin)" class="plugin-update-action" variant="accent" :disabled="!preview && (!(inHost || !!demoMode?.enabled) || !supportsPlugins || state.busy || checking || !selectedInstance) || updateTaskActive(plugin)" :aria-busy="updateTaskActive(plugin)" @click="applyUpdate(plugin)"><Transition name="version-swap" mode="out-in"><span :key="String(updateTaskActive(plugin))" class="update-button-content"><WProgressRing v-if="updateTaskActive(plugin)" :size="16" /><FluentIcon v-else glyph="download" :size="14" />{{ t(updateTaskActive(plugin) ? updateTask(plugin)?.target.fromVersion === '' ? 'pluginUpdates.installing' : 'pluginUpdates.updating' : 'pluginUpdates.updateNow') }}</span></Transition></WButton></Transition>
          </div>
          <WToggleSwitch :model-value="plugin.selection?.enabled ?? false" :disabled="locked || plugin.required && !!plugin.selection?.enabled || !plugin.available" :aria-label="`${localized(plugin.name)} ${plugin.version}`" @update:model-value="toggle(plugin, $event)" />
          <WButton class="plugin-delete" variant="subtle" :disabled="!plugin.canUninstall || state.busy || uninstallBusy || activeDownload" :aria-label="`${t('pluginLoadIssues.uninstall')} ${localized(plugin.name)}`" :title="t('pluginLoadIssues.uninstall')" @click="uninstalling = plugin"><FluentIcon glyph="delete" /></WButton>
          <WButton class="plugin-options" variant="subtle" :aria-label="`${localized(plugin.name)} ${t('live.options')}`" :title="t('live.options')" :disabled="!(plugin.options.length || plugin.configurationPage) || locked" @click="edit(plugin)"><FluentIcon glyph="settings" /></WButton>
          </div>
          <Transition name="plugin-update"><div v-if="plugins.length > 1 && (startingUpdates.has(key(plugin)) || updateTask(plugin) && !isTerminal(updateTask(plugin)!))" class="plugin-update"><div class="plugin-progress-inner"><DownloadTaskProgress compact :pending="startingUpdates.has(key(plugin))" :task="updateTask(plugin)" /></div></div></Transition>
          <Transition name="plugin-update"><div v-if="showUpdate(plugin) && !hasUpdate(plugin)" class="plugin-update"><div class="plugin-update-inner">
            <FluentIcon :glyph="update(plugin)?.status === 'available' || update(plugin)?.status === 'installed' ? 'download' : 'info'" :size="18" />
            <div class="update-copy"><span>{{ updateTask(plugin) && !isTerminal(updateTask(plugin)!) ? t(downloadStageKey(updateTask(plugin)!)) : t(`pluginUpdates.${update(plugin)?.status}`, { version: update(plugin)?.version }) }}</span><small v-if="['available', 'installed'].includes(update(plugin)!.status)">{{ update(plugin)?.sourceName }}</small></div>
            <WButton v-if="updateTask(plugin) && !isTerminal(updateTask(plugin)!)" variant="subtle" @click="downloads.selectedId = updateTask(plugin)!.id; $router.push({ path: '/downloads', query: { task: updateTask(plugin)!.id } })">{{ t('pluginUpdates.progress') }}</WButton>
            <WButton v-else-if="update(plugin)?.status === 'available'" :disabled="!(inHost || !!demoMode?.enabled) || !supportsPlugins || state.busy || checking || !selectedInstance" @click="downloadUpdate(plugin)">{{ t('pluginUpdates.download', { version: update(plugin)?.version }) }}</WButton>
            <WButton v-else-if="update(plugin)?.status === 'installed' && selectedInstance?.plugins.find(p => p.id === plugin.id)?.version !== update(plugin)?.version" :disabled="locked || checking" @click="activateUpdate(plugin)">{{ t('pluginUpdates.activate', { version: update(plugin)?.version }) }}</WButton>
          </div></div></Transition>
          </WAnimatedSize>
        </article>
      </TransitionGroup>
      <DownloadTaskProgress v-if="plugins.length === 1 && (startingUpdates.has(key(plugins[0]!)) || updateTask(plugins[0]!) && !isTerminal(updateTask(plugins[0]!)!))"
        :pending="startingUpdates.has(key(plugins[0]!))" :task="updateTask(plugins[0]!)" />
      <p v-if="!plugins.length" class="muted">{{ t('plugins.noneFound') }}</p>
    <WDialog :open="!!editing" :title="localized(editing?.name)" :primary-text="t('live.save')" :close-text="t('dialog.cancel')" :primary-disabled="locked || configurationPending" :primary-busy="state.busy" @primary="save" @close="closeEditor">
      <div v-if="configurationPages.length > 1" class="configuration-tabs" role="tablist" :aria-label="t('live.options')" @keydown="configurationKeydown">
        <button v-for="(page, index) in configurationPages" :id="'plugin-tab-' + index" :key="page.id" type="button" role="tab" :aria-controls="'plugin-panel-' + index" :aria-selected="activeConfiguration === index" :tabindex="activeConfiguration === index ? 0 : -1" @click="activeConfiguration = index">{{ localized(page.title) }}</button>
      </div>
      <WAnimatedSize class="configuration-size"><div class="configuration-pages">
        <div v-for="(page, index) in configurationPages" :id="'plugin-panel-' + index" :key="page.id" class="configuration-panel" :class="{ active: activeConfiguration === index }" :inert="activeConfiguration !== index" :aria-hidden="activeConfiguration !== index" :role="configurationPages.length > 1 ? 'tabpanel' : undefined" :aria-labelledby="configurationPages.length > 1 ? 'plugin-tab-' + index : undefined">
          <div v-if="page.kind === 'native'" class="form-stack"><label v-for="option in pageOptions(page)" :key="option.key" class="field"><span>{{ localized(option.label) }}</span>
            <WToggleSwitch v-if="option.type === 'bool'" :model-value="values[option.key] === true" :aria-label="localized(option.label)" @update:model-value="values[option.key] = $event" />
            <WComboBox v-else-if="option.type === 'enum'" :model-value="String(values[option.key])" :options="option.values.map(v => ({ value: v, label: localized(option.valueLabels?.[v]) || v }))" :aria-label="localized(option.label)" @update:model-value="values[option.key] = $event" />
            <WTextBox v-else :model-value="String(values[option.key])" :type="option.type === 'int' ? 'number' : 'text'" :min="option.minimum" :max="option.maximum" :step="1" @update:model-value="values[option.key] = option.type === 'int' ? Number($event) : $event" />
            <small v-if="option.description" class="muted">{{ localized(option.description) }}</small>
          </label></div>
          <PluginConfigurationPage v-else-if="configurationHtml[page.id] !== undefined" :html="configurationHtml[page.id]!" :context="configurationContext" :options="pageOptions(page)" :values="pageValues(page)" @change="Object.assign(values, $event)" />
          <p v-else-if="configurationErrors[page.id]" role="alert">{{ configurationErrors[page.id] }}</p>
          <div v-else class="configuration-loading" role="status" :aria-label="localized(page.title)"><WProgressRing :size="24" /></div>
        </div>
      </div></WAnimatedSize>
    </WDialog>
    <WDialog :open="!!issueDetails" :title="t('pluginLoadIssues.details')" :close-text="t('titlebar.close')" @close="issueDetails = undefined"><pre class="issue-details">{{ issueDetails?.message }}</pre></WDialog>
    <WDialog :open="!!uninstallingIssue" :title="t('pluginLoadIssues.uninstall')" :primary-text="t('pluginLoadIssues.uninstall')" :close-text="t('dialog.cancel')" :primary-disabled="uninstallBusy" @primary="uninstallIssue" @close="uninstallingIssue = undefined"><div class="form-stack"><PackageSummaryCard glyph="puzzle" :name="uninstallingIssue?.id ?? t('pluginLoadIssues.unknown')" /><p>{{ t('pluginLoadIssues.uninstallConfirm', { name: uninstallingIssue?.id ?? t('pluginLoadIssues.unknown') }) }}</p></div></WDialog>
    <WDialog :open="!!uninstalling" :title="t('pluginLoadIssues.uninstall')" :primary-text="t('pluginLoadIssues.uninstall')" :close-text="t('dialog.cancel')" :primary-disabled="uninstallBusy" :primary-busy="uninstallBusy" @primary="uninstallPlugin" @close="uninstalling = undefined">
      <div class="form-stack"><PackageSummaryCard glyph="puzzle" :name="localized(uninstalling?.name)"><template #meta>{{ localized(uninstalling?.description ?? undefined) }}</template></PackageSummaryCard><p>{{ t('pluginLoadIssues.uninstallConfirm', { name: localized(uninstalling?.name) }) }}</p></div>
    </WDialog>
    <PluginImportDialog :open="importing" @close="importing = false" @imported="imported" />
    <WDialog :open="!!switching" :title="t('pluginImport.switchTitle')" :primary-text="t('pluginImport.switch')" :close-text="t('dialog.cancel')" :primary-disabled="locked" :primary-busy="state.busy" @primary="switchVersion" @close="switching = undefined">
      <p>{{ t('pluginImport.switchBody', { name: localized(switching?.plugin.name), from: switching?.from, to: switching?.plugin.version }) }}</p>
      <label class="version-options"><span>{{ t('pluginImport.preserveOptions') }}</span><WToggleSwitch v-model="preserveOptions" :disabled="locked" :aria-label="t('pluginImport.preserveOptions')" /></label>
    </WDialog>
    </PageBody>
    <div v-if="!preview && selectedInstance && supportsPlugins && !pluginListReady" class="plugin-loading-overlay" role="status" aria-live="polite">
      <div class="plugin-loading-content"><WProgressRing :size="28" /><span>{{ t('recommendations.loading') }}</span></div>
    </div>
    <PluginSchemeShareDialog :open="sharingScheme" @close="sharingScheme = false" />
  </section>
</template>

<style scoped>
/* Caption-size status lines with Fluent signal glyphs, as Windows Settings lists conditions; no chip outlines. */
.policy-rules { display: flex; flex-wrap: wrap; gap: 4px 16px; margin: 6px 0 0; padding: 0; list-style: none; }
.policy-rule { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; line-height: 16px; color: var(--text-secondary); }
.policy-rule.met > :first-child { color: var(--fill-success); }
.policy-rule.require:not(.met) > :first-child { color: var(--fill-caution); }
.policy-rule.recommend:not(.met) > :first-child { color: var(--fill-attention); }
.policy-rule.conflict:not(.met) > :first-child { color: var(--fill-critical); }
.policy-rule:not(.met) { color: var(--text-primary); }
.plugin-preview-notice { margin-bottom: 12px; }
.plugin-notices { display: grid; gap: 8px; }
.plugin-notices:not(:empty) { margin-bottom: 14px; }
.plugin-notice-enter-active, .plugin-notice-leave-active { transition: opacity 180ms ease, transform 200ms ease; }
.plugin-notice-enter-from, .plugin-notice-leave-to { opacity: 0; transform: translateY(-6px); }
.plugin-notice-move { transition: transform 200ms ease; }
.plugin-page { position: relative; }
.plugin-loading-overlay { position: absolute; z-index: 20; inset: 0; display: grid; place-items: center; background: color-mix(in srgb, var(--mica-base) 72%, transparent); backdrop-filter: blur(2px); }
.plugin-loading-content { display: flex; align-items: center; gap: 10px; padding: 14px 18px; border: 1px solid var(--card-stroke-default); border-radius: 8px; background: var(--layer-alt); color: var(--text-secondary); box-shadow: var(--shadow-flyout); }
.load-issues { display: grid; gap: 8px; margin-bottom: 16px; }.load-issues > p { margin: 0; }.issue-actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 12px; }.issue-progress { display: grid; gap: 6px; margin-top: 12px; }.issue-details { white-space: pre-wrap; overflow-wrap: anywhere; user-select: text; font-size: 12px; }
.configuration-tabs { display: flex; gap: 24px; margin-bottom: 20px; border-bottom: 1px solid var(--stroke-divider); }
.configuration-tabs button { position: relative; padding: 8px 2px 12px; color: var(--text-secondary); transition: color 160ms ease; }
.configuration-tabs button[aria-selected=true] { color: var(--text-primary); }
.configuration-tabs button::after { content: ''; position: absolute; left: 0; right: 0; bottom: -1px; height: 3px; border-radius: 2px; background: var(--accent-default); opacity: 0; transform: scaleX(.6); transition: opacity 180ms ease, transform 220ms cubic-bezier(.2,.65,.25,1); }
.configuration-tabs button[aria-selected=true]::after { opacity: 1; transform: scaleX(1); }
.configuration-tabs button:focus-visible { outline: 2px solid var(--accent-default); outline-offset: 2px; border-radius: 3px; }
.configuration-size { overflow: clip; }
.configuration-loading { display: grid; place-items: center; min-height: 160px; }
.configuration-pages { position: relative; }
.configuration-panel { position: absolute; inset: 0 0 auto; opacity: 0; visibility: hidden; pointer-events: none; transition: opacity 180ms ease, visibility 0s 180ms; }
.configuration-panel.active { position: relative; opacity: 1; visibility: visible; pointer-events: auto; transition: opacity 180ms ease; }
@media (prefers-reduced-motion: reduce) { .configuration-panel, .configuration-panel.active, .configuration-tabs button, .configuration-tabs button::after { transition: none; } }
.version-options { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin-top: 16px; }
.plugin-context { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }.instance-picker { width: 210px; }
.package-version { width: 120px; flex: none; }
.plugin-toolbar { display: flex; gap: 16px; align-items: center; margin-bottom: 20px; }.plugin-toolbar > :first-child { width: 280px; }.toolbar-actions { display: flex; gap: 8px; margin-left: auto; flex: none; }.import-result { color: var(--text-secondary); font-size: 12px; }
.plugin-list { display: flex; flex-direction: column; gap: 6px; }
.plugin-row { overflow: hidden; }
.plugin-unavailable { opacity: .56; filter: saturate(.7); }
.plugin-reorder-move { transition: transform 260ms cubic-bezier(.2,.65,.25,1); }
.plugin-reorder-enter-active, .plugin-reorder-leave-active { transition: opacity 180ms ease, transform 220ms ease; }
.plugin-reorder-enter-from, .plugin-reorder-leave-to { opacity: 0; transform: translateY(8px); }
.plugin-reorder-leave-active { position: absolute; width: 100%; }
.plugin-main { display: grid; grid-template-columns: 32px minmax(0, 1fr) auto 40px 32px 32px; align-items: center; gap: 18px; padding: 20px; }
.plugin-release-actions { grid-column: 3; display: flex; align-items: center; justify-content: flex-end; gap: 12px; }
.plugin-update-action { min-width: 106px; }
.update-button-content { display: inline-flex; align-items: center; gap: 8px; }
.update-button-content :deep(.w-ring-arc) { stroke: currentColor; }
.plugin-main > .w-switch { grid-column: 4; }
.plugin-main > .plugin-delete { grid-column: 5; width: 32px; height: 32px; padding: 0; }
.plugin-main > .plugin-options { grid-column: 6; width: 32px; height: 32px; padding: 0; }
.plugin-progress-inner { min-height: 0; overflow: hidden; }
.plugin-icon { color: var(--text-secondary); width: 32px; flex: none; text-align: center; }
.plugin-text { flex: 1; min-width: 0; }
.plugin-heading { position: relative; display: flex; flex-wrap: wrap; align-items: center; gap: 10px; }
.plugin-heading h2 { font-size: 14px; margin: 0; font-weight: 600; }
.version { font-size: 12px; color: var(--text-tertiary); }
.builtin-badge { font-size: 11px; font-weight: 400; color: var(--fill-attention); background: color-mix(in srgb, var(--fill-attention) 12%, transparent); padding: 1px 6px; border-radius: 3px; white-space: nowrap; }
.update-badge { font-size: 11px; font-weight: 500; color: var(--fill-success); background: color-mix(in srgb, var(--fill-success) 14%, transparent); padding: 1px 6px; border-radius: 3px; white-space: nowrap; }
.unavailable-badge { font-size: 11px; font-weight: 400; color: var(--text-secondary); background: var(--subtle-secondary); padding: 1px 6px; border-radius: 3px; white-space: nowrap; }
.required { font-size: 11px; color: var(--fill-caution); background: color-mix(in srgb, var(--fill-caution) 12%, transparent); padding: 1px 6px; border-radius: 3px; white-space: nowrap; }
.plugin-text p { font-size: 12px; line-height: 18px; color: var(--text-secondary); margin: 6px 0 0; }
.unavailable { display: block; color: var(--text-tertiary); font-size: 12px; margin-top: 4px; }
.plugin-update { display: grid; grid-template-rows: 1fr; transition: grid-template-rows 240ms cubic-bezier(.2,.8,.2,1), opacity 180ms ease; }
.plugin-update-inner { min-height: 0; overflow: hidden; display: flex; align-items: center; gap: 12px; background: var(--subtle-secondary); }
.plugin-update-inner > :first-child { margin-left: 24px; color: var(--accent-text-primary); flex: none; }
.update-copy { padding: 12px 0; flex: 1; min-width: 0; font-size: 13px; }
.update-copy small { display: block; font-size: 12px; margin-top: 3px; color: var(--text-secondary); }
.plugin-update-inner > .w-btn { margin-right: 20px; flex: none; }
.plugin-update-enter-from, .plugin-update-leave-to { grid-template-rows: 0fr; opacity: 0; }
.version-swap-enter-active, .version-swap-leave-active { transition: opacity 160ms ease, transform 180ms ease; }
.version-swap-enter-from { opacity: 0; transform: translateY(-4px); }
.version-swap-leave-to { opacity: 0; transform: translateY(4px); }
.plugin-heading .version-swap-leave-active { position: absolute; }
.plugin-meta-move { transition: transform 220ms ease; }
.plugin-meta-enter-active, .plugin-meta-leave-active, .update-action-enter-active, .update-action-leave-active { transition: opacity 180ms ease, transform 220ms ease; }
.plugin-meta-enter-from, .plugin-meta-leave-to, .update-action-enter-from, .update-action-leave-to { opacity: 0; transform: translateY(4px); }
.plugin-meta-leave-active { position: absolute; }
.spinning { animation: update-spin 1s linear infinite; }
@keyframes update-spin { to { transform: rotate(360deg); } }
@media (max-width: 760px) {
  .plugin-main { grid-template-columns: 32px minmax(0, 1fr) 40px 32px 32px; gap: 12px; padding: 16px; }
  .plugin-main > .plugin-text { grid-column: 2 / -1; }
  .plugin-release-actions { grid-column: 2; grid-row: 2; flex-wrap: wrap; }
  .plugin-main > .w-switch { grid-column: 3; grid-row: 2; }
  .plugin-main > .plugin-delete { grid-column: 4; grid-row: 2; }
  .plugin-main > .plugin-options { grid-column: 5; grid-row: 2; }
  .plugin-toolbar { flex-wrap: wrap; }
  .plugin-toolbar > :first-child { flex: 1; min-width: 160px; }
  .toolbar-actions { flex-wrap: wrap; }
}
@media (prefers-reduced-motion: reduce) { .plugin-update, .plugin-reorder-move, .plugin-reorder-enter-active, .plugin-reorder-leave-active, .version-swap-enter-active, .version-swap-leave-active, .plugin-meta-move, .plugin-meta-enter-active, .plugin-meta-leave-active, .update-action-enter-active, .update-action-leave-active { transition: none; }.spinning { animation: none; } }
</style>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost } from '../bridge'
import { downloadAction, downloadTasks, trackDownload } from '../downloads/live'
import type { DownloadTask } from '../downloads/model'
import { localized } from '../i18n'
import { prepareStorePluginInstallation, refreshInstances, state } from '../state'
import { newestFirst, satisfiesRange } from '../store/versions'
import { matchesPluginIdentity } from '../store/updates'
import type { StorePackage, StoreSource } from '../store/types'
import { cancelPolicy, continuePolicy, disableTarget, enableTarget, findingKey, policyAction, policyPrompt as prompt, policyResolved,
  refreshPolicy, switchDependent, type PolicyFinding } from '../plugins/pluginPolicy'
import { isPolicyPreview } from '../plugins/policyPreview'
import WDialog from '../controls/WDialog.vue'
import WButton from '../controls/WButton.vue'
import WCheckBox from '../controls/WCheckBox.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import DownloadTaskProgress from './DownloadTaskProgress.vue'

interface StoreMatch { item: StorePackage; source: StoreSource }
interface CardAction {
  kind: 'install' | 'update' | 'installing' | 'updating' | 'retry' | 'resume' | 'enable' | 'enabled' | 'disable' | 'disabled' | 'notInstalled' | 'notEnabled'
  accent: boolean; disabled: boolean; live?: PolicyFinding
}

const { t } = useI18n()
const storeMatches = ref<Record<string, StoreMatch>>({})
const searchingKeys = ref<Record<string, boolean>>({})
const cardTaskIds = ref<Record<string, string>>({})
const settledSnapshots = ref<Record<string, DownloadTask>>({})
const settledTasks = new Set<string>()
const canUseStore = computed(() => inHost || !!demoMode?.enabled || isPolicyPreview())
const cards = computed(() => prompt.cards.length ? prompt.cards : prompt.findings)
const blocking = computed(() => cards.value.filter(item => item.blocks))
const recommended = computed(() => cards.value.filter(item => item.type === 'recommend'))
const instance = computed(() => state.instances.find(item => item.id === prompt.instanceId))

const targetKey = (card: PolicyFinding) => `${card.type}:${card.target?.id ?? card.other}`
const liveFinding = (card: PolicyFinding) => prompt.findings.find(item => findingKey(item) === findingKey(card))
const localMatch = (card: PolicyFinding) => state.plugins.find(item => item.id === card.other || item.aliases?.includes(card.other))
const storeMatch = (card: PolicyFinding) => storeMatches.value[findingKey(card)]?.item
const isCardSearching = (card: PolicyFinding) => !!searchingKeys.value[targetKey(card)]
const cardTaskId = (card: PolicyFinding) => cardTaskIds.value[targetKey(card)] ?? ''
const cardTask = (card: PolicyFinding): DownloadTask | undefined => {
  const id = cardTaskId(card)
  return id ? downloadTasks.value.find(task => task.id === id) ?? settledSnapshots.value[targetKey(card)] : settledSnapshots.value[targetKey(card)]
}
const isRetryableTask = (task: DownloadTask) => ['failed', 'canceled', 'paused'].includes(task.stage)

const dependentOn = (finding: PolicyFinding) => prompt.enabling?.id === finding.plugin ? prompt.keepEnabling
  : !!instance.value?.plugins.find(item => item.id === finding.plugin && item.enabled)

const blockingGroups = computed(() => {
  const groups: {
    plugin: string; pluginName: PolicyFinding['pluginName']; hasRequire: boolean; hasConflict: boolean; items: PolicyFinding[]
  }[] = []
  const byPlugin = new Map<string, (typeof groups)[number]>()
  for (const card of blocking.value) {
    let group = byPlugin.get(card.plugin)
    if (!group) {
      group = {
        plugin: card.plugin,
        pluginName: card.pluginName,
        hasRequire: card.type === 'require',
        hasConflict: card.type === 'conflict',
        items: [],
      }
      byPlugin.set(card.plugin, group)
      groups.push(group)
    } else {
      group.hasRequire ||= card.type === 'require'
      group.hasConflict ||= card.type === 'conflict'
    }
    group.items.push(card)
  }
  return groups
})

const cardName = (card: PolicyFinding) => {
  if (prompt.disabling) return localized(card.pluginName) || card.plugin
  return localized(card.target?.name ?? localMatch(card)?.name ?? storeMatch(card)?.name) || card.other
}
const cardVersion = (card: PolicyFinding) => {
  if (prompt.disabling) return card.pluginVersion
  const storeVersion = storeMatch(card)?.version
  if (liveFinding(card)?.status === 'version' && card.target?.version && storeVersion && storeVersion !== card.target.version) {
    return `${card.target.version} → ${storeVersion}`
  }
  return card.target?.version || localMatch(card)?.version || storeVersion || card.range || ''
}
const cardDescription = (card: PolicyFinding) => {
  if (prompt.disabling) {
    const local = state.plugins.find(item => item.id === card.plugin && item.version === card.pluginVersion)
    return localized(card.pluginDescription ?? local?.description ?? undefined)
  }
  const desc = localized(card.target?.description ?? localMatch(card)?.description ?? storeMatch(card)?.description ?? undefined)
  if (desc) return desc
  const name = cardName(card)
  return name && name !== card.other ? card.other : ''
}
const cardGlyph = (card: PolicyFinding) => {
  const kind = prompt.disabling ? card.pluginKind : (card.target?.kind ?? localMatch(card)?.kind)
  return kind === 'native' ? 'code' : 'puzzle'
}

function showCardProgress(card: PolicyFinding): boolean {
  if (prompt.disabling) return false
  if (isCardSearching(card)) return true
  const task = cardTask(card)
  if (!task) return false
  if (task.stage !== 'completed') return true
  const live = liveFinding(card)
  return !!live && (live.status === 'missing' || live.status === 'unavailable' || live.status === 'version')
}

function hasProgressSlot(card: PolicyFinding): boolean {
  if (prompt.disabling) return false
  return showCardProgress(card) || !!settledSnapshots.value[targetKey(card)]
    || card.status === 'missing' || card.status === 'unavailable' || card.status === 'version'
}

function cardAction(card: PolicyFinding): CardAction {
  const live = liveFinding(card)
  if (prompt.disabling) {
    if (live && dependentOn(card)) return { kind: 'disable', accent: false, disabled: prompt.busy || card.pluginRequired, live }
    return { kind: 'disabled', accent: false, disabled: true }
  }
  if (card.type === 'conflict') {
    if (live?.status === 'conflict') return { kind: 'disable', accent: false, disabled: prompt.busy, live }
    return { kind: 'disabled', accent: false, disabled: true }
  }
  if (!dependentOn(card)) {
    if (card.target) return { kind: 'notEnabled', accent: false, disabled: true }
    return { kind: 'notInstalled', accent: false, disabled: true }
  }
  if (live && (live.status === 'missing' || live.status === 'unavailable' || live.status === 'version')) {
    const updating = live.status === 'version'
    const task = cardTask(card)
    if (isCardSearching(card)) return { kind: updating ? 'updating' : 'installing', accent: false, disabled: true, live }
    if (task) {
      if (task.stage === 'paused') return { kind: 'resume', accent: true, disabled: prompt.busy, live }
      if (task.stage === 'failed' || task.stage === 'canceled') return { kind: 'retry', accent: true, disabled: prompt.busy, live }
      return { kind: updating || !!task.target.fromVersion ? 'updating' : 'installing', accent: false, disabled: true, live }
    }
    return { kind: updating ? 'update' : 'install', accent: true, disabled: prompt.busy, live }
  }
  if (live?.status === 'disabled') return { kind: 'enable', accent: true, disabled: prompt.busy, live }
  return { kind: 'enabled', accent: false, disabled: true }
}

const cardInactive = (card: PolicyFinding) => !prompt.disabling && card.type === 'require' && !dependentOn(card)

function actionLabel(action: CardAction): string {
  switch (action.kind) {
    case 'install': return t('pluginPolicy.install')
    case 'update': return t('pluginPolicy.update')
    case 'installing': return t('pluginPolicy.installing')
    case 'updating': return t('pluginPolicy.updating')
    case 'retry': return t('updates.retry')
    case 'resume': return t('updates.resume')
    case 'enable': return t('pluginPolicy.enable')
    case 'enabled': return t('pluginPolicy.enabledDone')
    case 'disable': return t('pluginPolicy.disableAction')
    case 'disabled': return t('pluginPolicy.disabledDone')
    case 'notInstalled': return t('pluginPolicy.notInstalled')
    case 'notEnabled': return t('pluginPolicy.notEnabled')
  }
}

async function triggerCardAction(card: PolicyFinding): Promise<void> {
  const action = cardAction(card)
  if (action.disabled) return
  if (action.kind === 'retry' || action.kind === 'resume') {
    const task = cardTask(card)
    if (!task) return
    prompt.error = ''
    try { await downloadAction(task, task.stage === 'paused' ? 'resume' : 'retry') }
    catch (error) { prompt.error = (error as Error).message }
    return
  }
  if (action.kind === 'install' || action.kind === 'update') { await install(action.live ?? card); return }
  if (action.kind === 'enable') { await enableTarget(action.live ?? card); return }
  if (action.kind === 'disable') {
    if (prompt.disabling) await switchDependent(action.live ?? card, false)
    else await disableTarget(action.live ?? card)
  }
}

async function resolveStorePackages(targets: readonly PolicyFinding[]): Promise<Record<string, StoreMatch>> {
  if (!canUseStore.value || !instance.value || !targets.length) return {}
  const sources = (await call('store.sources')).filter(source => source.enabled && source.baseUrl)
  const buckets = new Map<string, { item: StorePackage; source: StoreSource; version: string }[]>()
  for (const source of sources) {
    try {
      const catalog = await call('store.catalog', { id: source.id })
      for (const target of targets) {
        const key = findingKey(target)
        for (const item of catalog.packages) {
          if (item.kind === 'plugin' && item.game === instance.value.game && matchesPluginIdentity(item, target.other)
            && item.launcherCompatible !== false && (!item.builds.length || item.builds.includes('*') || item.builds.includes(instance.value.build))
            && (!target.range || satisfiesRange(item.version, target.range))) {
            const list = buckets.get(key) ?? []
            list.push({ item, source, version: item.version })
            buckets.set(key, list)
          }
        }
      }
    } catch { /* Another source may still offer it. */ }
  }
  const matched: Record<string, StoreMatch> = {}
  for (const [key, found] of buckets) {
    const newest = newestFirst(found)[0]
    if (newest) matched[key] = { item: newest.item, source: newest.source }
  }
  return matched
}

/** Downloads and enables the newest store release inline inside the dependency card. */
async function install(finding: PolicyFinding): Promise<void> {
  if (!canUseStore.value || !instance.value) return
  const key = findingKey(finding)
  const tKey = targetKey(finding)
  if (searchingKeys.value[tKey]) return
  const existingTask = cardTask(finding)
  if (existingTask && cardTaskIds.value[tKey]) {
    if (isRetryableTask(existingTask)) {
      prompt.error = ''
      try { await downloadAction(existingTask, existingTask.stage === 'paused' ? 'resume' : 'retry') }
      catch (error) { prompt.error = (error as Error).message }
    }
    return
  }

  searchingKeys.value = { ...searchingKeys.value, [tKey]: true }
  prompt.error = ''
  try {
    let match = storeMatches.value[key]
    if (!match) {
      const found = await resolveStorePackages([finding])
      match = found[key]
      if (!match) throw new Error(t('pluginPolicy.notInStore', { name: cardName(finding) }))
      storeMatches.value = { ...storeMatches.value, [key]: match }
    }
    const selectedItem = match.item
    const selectedSource = match.source
    const serverId = state.selectedGameId === selectedItem.game ? state.selectedServerId || undefined : undefined
    const expectedSource = { kind: selectedSource.kind, baseUrl: selectedSource.baseUrl, catalogPath: selectedSource.catalogPath }
    const remember = (inHost || !!demoMode?.enabled) ? prepareStorePluginInstallation(selectedItem, serverId ?? '') : undefined
    const task = await call('store.installPluginTargets', {
      sourceId: selectedSource.id,
      expectedSource,
      packageId: selectedItem.id,
      version: selectedItem.version,
      manifestSha256: selectedItem.manifestSha256,
      game: selectedItem.game,
      instanceIds: [instance.value.id],
      serverId,
    })
    remember?.(task.id)
    trackDownload(task)
    cardTaskIds.value = { ...cardTaskIds.value, [tKey]: task.id }
    settledSnapshots.value = { ...settledSnapshots.value, [tKey]: task }
  } catch (error) {
    prompt.error = (error as Error).message
  } finally {
    const next = { ...searchingKeys.value }
    delete next[tKey]
    searchingKeys.value = next
  }
}

watch(() => prompt.open, open => {
  if (!open) {
    searchingKeys.value = {}
    cardTaskIds.value = {}
    settledSnapshots.value = {}
    settledTasks.clear()
    storeMatches.value = {}
  }
})
watch(() => [prompt.open, cards.value.map(findingKey).join('|')] as const, async ([open]) => {
  if (!open || !canUseStore.value || !instance.value) return
  const pending = cards.value.filter(card => (card.status === 'missing' || card.status === 'version' || card.status === 'unavailable') && !storeMatches.value[findingKey(card)])
  if (!pending.length) return
  try {
    const resolved = await resolveStorePackages(pending)
    if (prompt.open && Object.keys(resolved).length) storeMatches.value = { ...storeMatches.value, ...resolved }
  } catch { /* Missing store metadata only keeps ID fallback until the user invokes Install. */ }
}, { immediate: true })
watch(() => downloadTasks.value.map(task => `${task.id}:${task.stage}`).join('|'), async () => {
  if (!prompt.open) return
  const ids = Object.values(cardTaskIds.value)
  const completed = downloadTasks.value.filter(task => ids.includes(task.id) && task.stage === 'completed' && !settledTasks.has(task.id))
  if (!completed.length) return
  const snapshots = { ...settledSnapshots.value }
  for (const task of completed) {
    settledTasks.add(task.id)
    for (const [tKey, id] of Object.entries(cardTaskIds.value)) {
      if (id === task.id) snapshots[tKey] = { ...task, files: task.files.map(file => ({ ...file })) }
    }
  }
  settledSnapshots.value = snapshots
  prompt.error = ''
  try {
    await refreshInstances()
    if (canUseStore.value) {
      const plugins = await call('plugins.list', instance.value ? { instanceId: instance.value.id } : undefined)
      if (Array.isArray(plugins)) state.plugins = plugins
    }
    await refreshPolicy()
  } catch (error) {
    prompt.error = (error as Error).message
  }
})
watch([() => state.plugins, () => state.instances], () => {
  if (prompt.open && !prompt.busy) void refreshPolicy().catch(error => { prompt.error = (error as Error).message })
})
</script>

<template>
  <WDialog wide :auto-resize="false" :open="prompt.open" :title="t(prompt.launching ? 'pluginPolicy.launchTitle' : prompt.disabling ? 'pluginPolicy.disableTitle' : 'pluginPolicy.title')"
    :primary-text="t('pluginPolicy.confirm')" :close-text="t('dialog.cancel')"
    :primary-disabled="prompt.busy || !policyResolved()" :primary-busy="prompt.busy" @primary="continuePolicy" @close="cancelPolicy">
    <div class="policy-content">
      <p class="policy-lead">{{ t(prompt.launching ? 'pluginPolicy.launchBody' : prompt.disabling ? 'pluginPolicy.disableBody' : 'pluginPolicy.body') }}</p>

      <template v-if="prompt.disabling">
        <div class="policy-list">
          <section v-for="card in blocking" :key="findingKey(card)" class="policy-card" :class="{ resolved: !liveFinding(card) }">
            <span class="policy-symbol"><FluentIcon :glyph="cardGlyph(card)" :size="20" /></span>
            <div class="policy-info">
              <div class="policy-heading">
                <strong>{{ cardName(card) }}</strong>
                <span v-if="cardVersion(card)" class="policy-version">{{ cardVersion(card) }}</span>
              </div>
              <p v-if="cardDescription(card)" class="policy-desc">{{ cardDescription(card) }}</p>
            </div>
            <div class="policy-action">
              <WButton :variant="cardAction(card).accent ? 'accent' : 'standard'" :disabled="cardAction(card).disabled" @click="triggerCardAction(card)">
                <Transition name="policy-btn-text" mode="out-in">
                  <span :key="cardAction(card).kind" class="policy-btn-label">{{ actionLabel(cardAction(card)) }}</span>
                </Transition>
              </WButton>
            </div>
          </section>
        </div>
      </template>

      <template v-else>
        <section v-for="group in blockingGroups" :key="group.plugin" class="policy-group">
          <div class="policy-group-head">
            <span class="policy-group-title">{{ t(group.hasConflict && !group.hasRequire ? 'pluginPolicy.group.conflict' : 'pluginPolicy.group.require', { name: localized(group.pluginName) }) }}</span>
          </div>
          <div class="policy-list">
            <section v-for="card in group.items" :key="findingKey(card)" class="policy-card" :class="[card.type, { resolved: cardAction(card).kind === 'enabled' || cardAction(card).kind === 'disabled', inactive: cardInactive(card) }]">
              <span class="policy-symbol"><FluentIcon :glyph="cardGlyph(card)" :size="20" /></span>
              <div class="policy-info">
                <div class="policy-heading">
                  <strong>{{ cardName(card) }}</strong>
                  <span v-if="cardVersion(card)" class="policy-version">{{ cardVersion(card) }}</span>
                </div>
                <p v-if="cardDescription(card)" class="policy-desc">{{ cardDescription(card) }}</p>
              </div>
              <div class="policy-action">
                <WButton :variant="cardAction(card).accent ? 'accent' : 'standard'" :disabled="cardAction(card).disabled" @click="triggerCardAction(card)">
                  <Transition name="policy-btn-text" mode="out-in">
                    <span :key="cardAction(card).kind" class="policy-btn-label">{{ actionLabel(cardAction(card)) }}</span>
                  </Transition>
                </WButton>
              </div>
              <div v-if="hasProgressSlot(card)" class="policy-progress-reveal" :class="{ open: showCardProgress(card) }" :aria-hidden="!showCardProgress(card)">
                <div class="policy-progress-clip">
                  <div class="policy-progress-inner">
                    <DownloadTaskProgress v-if="showCardProgress(card) || cardTask(card)" :task="cardTask(card)" :task-id="cardTaskId(card)" :pending="isCardSearching(card) && !cardTask(card)" />
                  </div>
                </div>
              </div>
            </section>
          </div>
        </section>
      </template>

      <template v-if="recommended.length">
        <span class="policy-label">{{ t('pluginPolicy.recommendedLabel') }}</span>
        <div class="policy-list">
          <div v-for="card in recommended" :key="findingKey(card)" class="policy-card recommend" :class="{ resolved: !liveFinding(card) }">
            <WCheckBox :model-value="!liveFinding(card) || prompt.selected.includes(findingKey(card))" :disabled="prompt.busy || liveFinding(card)?.status !== 'disabled'"
              :aria-label="cardName(card)"
              @update:model-value="checked => { prompt.selected = checked ? [...new Set([...prompt.selected, findingKey(card)])] : prompt.selected.filter(key => key !== findingKey(card)) }" />
            <div class="policy-info">
              <div class="policy-heading">
                <strong>{{ cardName(card) }}</strong>
                <span v-if="cardVersion(card)" class="policy-version">{{ cardVersion(card) }}</span>
              </div>
              <p v-if="cardDescription(card)" class="policy-desc">{{ cardDescription(card) }}</p>
            </div>
            <div class="policy-action">
              <WButton v-if="isCardSearching(card) || (cardTask(card) && cardTaskIds[targetKey(card)] && !isRetryableTask(cardTask(card)!) && cardTask(card)!.stage !== 'completed')" variant="standard" disabled>
                {{ t(liveFinding(card)?.status === 'version' ? 'pluginPolicy.updating' : 'pluginPolicy.installing') }}
              </WButton>
              <WButton v-else-if="cardTask(card)?.stage === 'paused' && cardTaskIds[targetKey(card)]" variant="accent" :disabled="prompt.busy" @click="install(liveFinding(card) ?? card)">
                {{ t('updates.resume') }}
              </WButton>
              <WButton v-else-if="(cardTask(card)?.stage === 'failed' || cardTask(card)?.stage === 'canceled') && cardTaskIds[targetKey(card)]" variant="accent" :disabled="prompt.busy" @click="install(liveFinding(card) ?? card)">
                {{ t('updates.retry') }}
              </WButton>
              <WButton v-else-if="liveFinding(card)?.status === 'missing' || liveFinding(card)?.status === 'unavailable'" variant="accent" :disabled="prompt.busy" @click="install(liveFinding(card)!)">
                {{ t('pluginPolicy.install') }}
              </WButton>
              <WButton v-else-if="liveFinding(card)?.status === 'version'" variant="accent" :disabled="prompt.busy" @click="install(liveFinding(card)!)">
                {{ t('pluginPolicy.update') }}
              </WButton>
              <WButton v-else-if="!liveFinding(card)" variant="standard" disabled>
                {{ t('pluginPolicy.enabledDone') }}
              </WButton>
            </div>
            <div v-if="hasProgressSlot(card)" class="policy-progress-reveal" :class="{ open: showCardProgress(card) }" :aria-hidden="!showCardProgress(card)">
              <div class="policy-progress-clip">
                <div class="policy-progress-inner">
                  <DownloadTaskProgress v-if="showCardProgress(card) || cardTask(card)" :task="cardTask(card)" :task-id="cardTaskId(card)" :pending="isCardSearching(card) && !cardTask(card)" />
                </div>
              </div>
            </div>
          </div>
        </div>
      </template>

      <div class="policy-error-reveal" :class="{ open: !!prompt.error }" :aria-hidden="!prompt.error">
        <div class="policy-progress-clip">
          <div class="policy-error-inner">
            <WInfoBar v-if="prompt.error" severity="error" :closable="false">{{ prompt.error }}</WInfoBar>
          </div>
        </div>
      </div>
    </div>
  </WDialog>
</template>

<style scoped>
.policy-content { display: grid; gap: 14px; }
.policy-lead { margin: 0; color: var(--text-secondary); line-height: 1.5; }
.policy-group { display: grid; gap: 8px; }
.policy-group-head { display: flex; align-items: center; gap: 12px; padding: 0 2px; }
.policy-group-title { font-size: 12px; font-weight: 600; color: var(--text-secondary); }
.policy-label { color: var(--text-secondary); font-size: 12px; font-weight: 600; padding: 0 2px; }
.policy-list { display: grid; gap: 8px; }
.policy-card {
  display: grid; grid-template-columns: 40px minmax(0, 1fr) auto; align-items: center; gap: 0 14px;
  padding: 14px 16px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-overlay);
  background: var(--card-bg-default);
  transition:
    opacity var(--duration-fast) linear,
    border-color var(--duration-normal) var(--ease-fast-out-slow-in),
    background-color var(--duration-normal) var(--ease-fast-out-slow-in);
}
.policy-card.inactive { opacity: 0.56; }
.policy-card.recommend { grid-template-columns: auto minmax(0, 1fr) auto; }
.policy-symbol {
  display: grid; place-items: center; width: 40px; height: 40px; border-radius: 10px;
  border: 1px solid color-mix(in srgb, var(--accent-default) 16%, transparent);
  color: var(--accent-default); background: color-mix(in srgb, var(--accent-default) 8%, var(--card-bg-default));
  transition:
    color var(--duration-normal) var(--ease-fast-out-slow-in),
    border-color var(--duration-normal) var(--ease-fast-out-slow-in),
    background-color var(--duration-normal) var(--ease-fast-out-slow-in);
}
.policy-card.conflict .policy-symbol {
  border-color: color-mix(in srgb, var(--fill-critical, var(--accent-default)) 22%, transparent);
  color: var(--fill-critical, var(--accent-default));
  background: color-mix(in srgb, var(--fill-critical, var(--accent-default)) 10%, var(--card-bg-default));
}
.policy-card.resolved .policy-symbol {
  border-color: color-mix(in srgb, var(--fill-success, var(--accent-default)) 20%, transparent);
  color: var(--fill-success, var(--accent-default));
  background: color-mix(in srgb, var(--fill-success, var(--accent-default)) 10%, var(--card-bg-default));
}
.policy-info { display: grid; gap: 3px; min-width: 0; }
.policy-heading { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; min-width: 0; }
.policy-heading strong { font-size: 14px; line-height: 20px; font-weight: 600; overflow-wrap: anywhere; }
.policy-version {
  padding: 1px 7px; border-radius: 999px; font-size: 11px; line-height: 16px;
  color: var(--text-secondary); background: color-mix(in srgb, var(--text-primary) 7%, transparent);
}
.policy-desc {
  margin: 0; color: var(--text-secondary); font-size: 12px; line-height: 18px;
  display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;
}
.policy-action { display: flex; align-items: center; justify-content: flex-end; flex: none; }
.policy-action :deep(.w-btn) {
  min-width: 96px;
  min-height: 32px;
  transition:
    background-color var(--duration-fast) var(--ease-fast-out-slow-in),
    border-color var(--duration-fast) var(--ease-fast-out-slow-in),
    color var(--duration-fast) linear;
}
.policy-btn-label { display: inline-block; line-height: 20px; min-height: 20px; }
.policy-btn-text-enter-active {
  transition:
    opacity var(--duration-faster) linear,
    transform var(--duration-fast) var(--ease-fast-out-slow-in);
}
.policy-btn-text-leave-active { transition: opacity var(--duration-faster) linear; }
.policy-btn-text-enter-from { opacity: 0; transform: translateY(3px); }
.policy-btn-text-leave-to { opacity: 0; }

.policy-progress-reveal {
  grid-column: 2 / -1;
  display: grid;
  grid-template-rows: 0fr;
  opacity: 0;
  transform: translateY(-4px);
  transition:
    grid-template-rows var(--duration-normal) var(--ease-fast-out-slow-in),
    opacity var(--duration-fast) linear,
    transform var(--duration-normal) var(--ease-fast-out-slow-in);
}
.policy-progress-reveal.open {
  grid-template-rows: 1fr;
  opacity: 1;
  transform: translateY(0);
}
.policy-progress-clip { min-height: 0; overflow: hidden; }
.policy-progress-inner { display: grid; align-content: start; min-height: 62px; padding-top: 10px; }
.policy-progress-inner :deep(.task-progress) { gap: 6px; margin-top: 0; }
.policy-progress-inner :deep(.task-caption) { line-height: 18px; min-height: 18px; }

.policy-error-reveal {
  display: grid;
  grid-template-rows: 0fr;
  opacity: 0;
  margin-top: -14px;
  transition:
    grid-template-rows var(--duration-normal) var(--ease-fast-out-slow-in),
    opacity var(--duration-fast) linear,
    margin-top var(--duration-normal) var(--ease-fast-out-slow-in);
}
.policy-error-reveal.open {
  grid-template-rows: 1fr;
  opacity: 1;
  margin-top: 0;
}
.policy-error-inner { min-height: 0; }

@media (prefers-reduced-motion: reduce) {
  .policy-card,
  .policy-symbol,
  .policy-action :deep(.w-btn),
  .policy-btn-text-enter-active,
  .policy-btn-text-leave-active,
  .policy-progress-reveal,
  .policy-error-reveal {
    transition: none;
  }
}
</style>

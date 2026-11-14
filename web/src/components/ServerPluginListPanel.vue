<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost, type PluginSummary, type PolicyPluginEntry, type ServerStoreSource } from '../bridge'
import { state } from '../state'
import { localized } from '../i18n'
import { newestFirst } from '../store/versions'
import type { StorePackage, StoreSource } from '../store/types'
import StorePluginDialog from './StorePluginDialog.vue'
import WButton from '../controls/WButton.vue'
import WComboBox from '../controls/WComboBox.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import FluentIcon from '../controls/FluentIcon.vue'
const props = defineProps<{ active: boolean; game: string; builds: string[]; recommendations: PolicyPluginEntry[]; sources: ServerStoreSource[]; serverId?: string }>()
const { t } = useI18n()
const plugins = ref<PluginSummary[]>([]), sources = ref<StoreSource[]>([]), instanceId = ref(''), busy = ref(false), error = ref('')
const installTarget = ref<{ item: StorePackage; source: StoreSource }>()
let revision = 0
const clients = computed(() => state.instances.filter(i => i.game === props.game && props.builds.includes(i.build)))
const build = computed(() => clients.value.find(i => i.id === instanceId.value)?.build)
const recommendations = computed(() => {
  const grouped = new Map<string, PolicyPluginEntry>()
  for (const item of props.recommendations.filter(item => !build.value || !item.builds.length || item.builds.includes(build.value))) {
    const previous = grouped.get(item.id)
    grouped.set(item.id, { ...item, required: item.required || previous?.required === true, disabled: item.disabled || previous?.disabled === true })
  }
  return [...grouped.values()]
})
const plugin = (id: string) => plugins.value.find(p => p.id === id && p.game === props.game)
const enabled = (id: string) => !!instanceId.value && plugins.value.some(p => p.id === id && p.game === props.game && p.selection?.enabled && p.available)
function matchingSource(item: ServerStoreSource) { return sources.value.find(s => s.kind === item.kind && s.baseUrl?.replace(/\/$/, '') === item.baseUrl.replace(/\/$/, '') && s.catalogPath === item.catalogPath) }
function sourceHost(item: ServerStoreSource) { try { return new URL(item.baseUrl).host } catch { return item.baseUrl } }
async function refresh() {
  const expected = ++revision; error.value = ''
  if (!props.active) return
  try {
    const [items, stores] = await Promise.all([call('plugins.list', { instanceId: instanceId.value || undefined, serverId: props.serverId }), call('store.sources')])
    if (props.active && expected === revision) { plugins.value = items; sources.value = stores }
  } catch (e) { if (props.active && expected === revision) error.value = (e as Error).message }
}
async function addSource(item: ServerStoreSource) {
  if (busy.value || !(inHost || demoMode?.enabled)) return
  const expected = revision; busy.value = true; error.value = ''
  try {
    const existing = matchingSource(item)
    await call('store.saveSource', { ...item, id: existing?.id ?? `server-${crypto.randomUUID().slice(0, 12)}`, enabled: true })
    const stores = await call('store.sources')
    if (props.active && expected === revision) sources.value = stores
  } catch (e) { if (props.active && expected === revision) error.value = (e as Error).message }
  finally { if (expected === revision) busy.value = false }
}
async function install(id: string) {
  if (busy.value || !(inHost || demoMode?.enabled)) return
  const expected = revision; busy.value = true; error.value = ''
  try {
    const queue = sources.value.filter(s => s.enabled && s.baseUrl)
    const found: { item: StorePackage; source: StoreSource }[] = [], failures: string[] = []
    await Promise.all(Array.from({ length: Math.min(4, queue.length) }, async () => {
      while (queue.length && expected === revision) {
        const source = queue.shift()!
        try {
          const catalog = await call('store.catalog', { id: source.id })
          for (const item of catalog.packages) if (item.kind === 'plugin' && item.game === props.game && item.id === id && item.launcherCompatible !== false &&
            (!item.builds.length || (build.value ? item.builds.includes(build.value) : props.builds.some(b => item.builds.includes(b))))) found.push({ item, source })
        } catch (e) { failures.push((e as Error).message) }
      }
    }))
    if (!props.active || expected !== revision) return
    const newest = newestFirst(found.map(release => ({ ...release, version: release.item.version })))[0]
    if (!newest) throw new Error([t('serverInfo.pluginUnavailable'), ...failures].join('\n'))
    installTarget.value = newest
  } catch (e) { if (props.active && expected === revision) error.value = (e as Error).message }
  finally { if (expected === revision) busy.value = false }
}
watch(() => JSON.stringify([props.active, props.game, props.serverId, props.builds, props.recommendations, props.sources]), () => {
  busy.value = false; installTarget.value = undefined
  instanceId.value = clients.value.find(i => i.id === state.selectedInstanceId)?.id ?? clients.value[0]?.id ?? ''
  void refresh()
}, { immediate: true })
watch(instanceId, () => void refresh())
onBeforeUnmount(() => { ++revision })
</script>
<template>
  <section class="server-plugins-panel" :class="{ 'empty-panel': !recommendations.length && !props.sources.length }">
    <div class="plugin-list">
      <label v-if="clients.length"><span>{{ t('hosting.version') }}</span><WComboBox v-model="instanceId" :options="clients.map(i => ({ value: i.id, label: i.name }))" :disabled="busy" /></label>
      <div v-if="!recommendations.length" class="plugin-empty"><FluentIcon glyph="puzzle" :size="36" /><p>{{ t('serverInfo.noPlugins') }}</p></div>
      <article v-for="item in recommendations" :key="item.id" class="plugin-row" :class="{ 'disabled-rule': item.disabled }"><span class="plugin-icon"><FluentIcon :glyph="item.disabled ? 'blocked' : 'puzzle'" :size="20" /></span><div><strong>{{ localized(plugin(item.id)?.name) || item.id }}</strong><small class="rule-badge" :class="{ required: item.required, disabled: item.disabled }">{{ t(item.disabled ? 'serverInfo.disabled' : item.required ? 'serverInfo.required' : 'serverInfo.recommended') }}</small></div><span v-if="item.disabled" class="plugin-result"><FluentIcon glyph="blocked" :size="14" />{{ t('serverInfo.temporarilyDisabled') }}</span><span v-else-if="enabled(item.id)" class="plugin-result installed"><FluentIcon glyph="checkMark" :size="14" />{{ t('serverInfo.installed') }}</span><WButton v-else :disabled="busy || !(inHost || demoMode?.enabled)" @click="install(item.id)">{{ t('plugins.install') }}</WButton></article>
      <template v-if="props.sources.length"><h3>{{ t('serverInfo.sources') }}</h3><article v-for="item in props.sources" :key="`${item.kind}:${item.baseUrl}:${item.catalogPath}`" class="plugin-row source-row"><span class="plugin-icon"><FluentIcon glyph="shop" :size="20" /></span><div><strong>{{ item.name }}</strong><small :title="`${item.baseUrl} · ${item.catalogPath}`">{{ sourceHost(item) }}</small></div><WButton :disabled="busy || !(inHost || demoMode?.enabled) || matchingSource(item)?.enabled" @click="addSource(item)">{{ t(matchingSource(item)?.enabled ? 'serverInfo.sourceAdded' : 'serverInfo.addSource') }}</WButton></article></template>
      <WInfoBar v-if="error" severity="error" @close="error = ''">{{ error }}</WInfoBar>
    </div>
    <StorePluginDialog v-if="installTarget" :open="!!installTarget" :item="installTarget.item" :source="installTarget.source" :server-id="serverId" @close="installTarget = undefined" />
  </section>
</template>
<style scoped>
.plugin-list { display: grid; gap: 10px; }label { display: flex; align-items: center; gap: 12px; font-size: 13px; margin-bottom: 10px; }label > span { color: var(--text-secondary); flex: none; }label :deep(.w-flyout-anchor) { min-width: 0; width: 280px; }
.plugin-empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 18px; padding: 36px 24px; color: var(--text-secondary); text-align: center; }.plugin-empty > :deep(.fluent-icon) { color: var(--text-tertiary); }.plugin-empty p { max-width: 340px; font-size: 14px; line-height: 1.8; margin: 0; }.empty-panel { height: 100%; }.empty-panel .plugin-list { min-height: 100%; grid-template-rows: auto 1fr; }.empty-panel .plugin-empty { grid-row: 2; }
.plugin-row { display: flex; gap: 14px; align-items: center; padding: 15px; border-radius: 7px; border: 1px solid var(--card-stroke-default); background: var(--card-bg-default); }.plugin-row > div { display: grid; gap: 7px; flex: 1; min-width: 0; }.plugin-row strong { font-weight: 600; overflow-wrap: anywhere; font-size: 13px; }.plugin-row small, .plugin-result { font-size: 12px; color: var(--text-secondary); overflow-wrap: anywhere; }
.plugin-icon { width: 38px; height: 38px; display: grid; place-items: center; border-radius: 8px; flex: none; color: var(--accent-default); background: var(--subtle-secondary); }.disabled-rule .plugin-icon { color: var(--text-secondary); }.rule-badge { justify-self: start; padding: 1px 6px; line-height: 18px; border-radius: 4px; background: var(--subtle-secondary); }.rule-badge.required { color: var(--accent-default); background: color-mix(in srgb,var(--accent-default) 8%,transparent); }.rule-badge.disabled { color: var(--text-secondary); }.plugin-result { max-width: 160px; display: flex; align-items: center; gap: 6px; }.plugin-result.installed { color: var(--fill-success); }
.source-row { background: var(--card-bg-secondary); }.source-row .plugin-icon { color: var(--text-secondary); }.source-row .w-btn { font-size: 12px; }.source-row small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }h3 { font-size: 14px; margin: 20px 0 2px; font-weight: 600; }.plugin-row > .w-btn { flex: none; }.plugin-result { flex: none; }
@media(max-width:720px) { .plugin-row { flex-wrap: wrap; }.plugin-result { max-width: none; margin-left: 52px; } }
</style>

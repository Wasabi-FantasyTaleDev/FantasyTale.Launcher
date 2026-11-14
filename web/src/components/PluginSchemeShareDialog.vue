<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost, type InstancePlugin } from '../bridge'
import { addPluginScheme, capturePluginScheme, pluginSchemeLibrary, selectedInstance, selectedPluginScheme, state } from '../state'
import { defaultScheme, type PluginScheme } from '../plugins/schemes'
import { liveStoreSession } from '../store/background'
import { matchesPluginIdentity, isPluginUpgrade } from '../store/updates'
import { compareVersions } from '../store/versions'
import { trackDownload } from '../downloads/live'
import { localized } from '../i18n'
import WDialog from '../controls/WDialog.vue'
import WButton from '../controls/WButton.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import ShellNotification from './ShellNotification.vue'
import WCheckBox from '../controls/WCheckBox.vue'
import PackageSummaryCard from './PackageSummaryCard.vue'
import type { StorePackage, StoreSource } from '../store/types'
const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
const route = useRoute()
const missingPreview = computed(() => !inHost && route.query.schemeMissingPreview === '1')
type Missing = { key: string; plugin: InstancePlugin; builds: string[]; offer?: { source: StoreSource; item: StorePackage }; selected: boolean }
const pending = ref<PluginScheme>(), missing = ref<Missing[]>([]), busy = ref(false), error = ref(''), notice = ref(''), file = ref<HTMLInputElement>()
const context = () => JSON.stringify([state.selectedGameId, state.selectedServerId, selectedInstance.value?.id])
let revision = 0
watch(() => [props.open, state.selectedGameId, state.selectedServerId], () => { revision++; pending.value = undefined; missing.value = []; error.value = ''; notice.value = ''; initializeMissingPreview() })
const scheme = computed(() => pluginSchemeLibrary.value.schemes.find(s => s.id === selectedPluginScheme.value))
onMounted(initializeMissingPreview)
function initializeMissingPreview() {
  if (!missingPreview.value) return
  const source: StoreSource = { id: 'preview', name: 'Fantasy Tale Archive', kind: 'http', baseUrl: 'http://preview.invalid/', catalogPath: 'source.toml', enabled: true, builtIn: false }
  const definitions = __DEV_PLUGINS__.filter(item => item.game === state.selectedGameId).slice(0, 2)
  missing.value = definitions.map((definition, index) => {
    const plugin: InstancePlugin = { id: definition.id, version: definition.version, enabled: true, required: false, options: {} }
    const item: StorePackage = { id: plugin.id, version: plugin.version, kind: 'plugin', game: state.selectedGameId, name: definition.name, description: definition.description ?? {}, publisher: 'Fantasy Tale Studio', size: 0, builds: [], manifest: 'manifest.toml', manifestSha256: '0'.repeat(64) }
    return { key: plugin.id, plugin, builds: [state.selectedBuildId], offer: { source, item }, selected: index === 0 }
  })
  missing.value.push({ key: 'preview.unavailable', plugin: { id: 'preview.unavailable', version: '1.0.0', enabled: true, required: false, options: {} }, builds: [state.selectedBuildId], selected: false })
  pending.value = { id: 'preview-import', name: t('pluginSchemes.default'), builds: {} }
}
async function exportFile() {
  if (!scheme.value || busy.value) return
  busy.value = true; error.value = ''
  try {
  if (selectedInstance.value) capturePluginScheme(selectedInstance.value)
  const name = scheme.value.id === defaultScheme ? t('pluginSchemes.default') : scheme.value.name
  const text = await call('plugins.scheme.format', { document: { schema: 1, game: state.selectedGameId, name, builds: Object.entries(scheme.value.builds).map(([id, plugins]) => ({ id, plugins })) } })
  const fileName = `${name.replace(/[<>:"/\\|?*\x00-\x1f]/g, '-') || 'plugin-scheme'}.toml`
  if (inHost || !!demoMode?.enabled) { if (!await call('shell.savePluginScheme', { text, fileName, title: t('pluginSchemes.export') })) return }
  else {
    const url = URL.createObjectURL(new Blob([text], { type: 'application/toml' }))
    const anchor = document.createElement('a'); anchor.href = url; anchor.download = fileName; anchor.click(); setTimeout(() => URL.revokeObjectURL(url), 1000)
  }
  notice.value = t('pluginSchemes.exported')
  } catch (e) { error.value = (e as Error).message }
  finally { busy.value = false }
}
async function chooseFile() {
  if (!(inHost || !!demoMode?.enabled)) { file.value?.click(); return }
  try { const text = await call('shell.pickPluginScheme', { title: t('pluginSchemes.import') }); if (text !== null) await inspect(text) }
  catch (e) { error.value = (e as Error).message }
}
async function fileChosen(event: Event) {
  const input = event.target as HTMLInputElement, selected = input.files?.[0]
  try { if (selected) await inspect(await selected.text()) } catch { error.value = t('pluginSchemes.importError') }
  finally { input.value = '' }
}
async function inspect(text: string) {
  const expected = ++revision, target = context(), game = state.selectedGameId
  busy.value = true; error.value = ''; notice.value = ''
  try {
    const parsed = await call('plugins.scheme.parse', { text })
    if (parsed.game !== game) throw new Error(t('pluginSchemes.importError'))
    const imported: PluginScheme = { id: crypto.randomUUID(), name: parsed.name, builds: Object.fromEntries(parsed.builds.map(build => [build.id, build.plugins.map(plugin => ({ ...plugin, required: false }))])) }
    await liveStoreSession.refresh()
    const inventory = await call('plugins.list')
    if (expected !== revision || target !== context() || !props.open) return
    const entries = new Map<string, Missing>()
    for (const [build, plugins] of Object.entries(imported.builds)) for (const plugin of plugins) {
      if (inventory.some(item => item.game === game && (item.id === plugin.id && item.version === plugin.version
        || item.aliases?.includes(plugin.id) && isPluginUpgrade(item.version, plugin.version)))) continue
      const key = JSON.stringify([plugin.id, plugin.version]), existing = entries.get(key)
      if (existing) { existing.builds.push(build); continue }
      const offer = liveStoreSession.state.sources.filter(source => source.enabled).flatMap(source => (liveStoreSession.state.catalogs[source.id]?.packages ?? [])
        .filter(item => item.kind === 'plugin' && item.game === game && (!item.builds.length || item.builds.includes(build))
          && item.launcherCompatible !== false && (item.id === plugin.id && item.version === plugin.version
            || item.id !== plugin.id && matchesPluginIdentity(item, plugin.id) && isPluginUpgrade(item.version, plugin.version)))
        .map(item => ({ source, item }))).sort((left, right) => -(compareVersions(left.item.version, right.item.version) ?? 0))[0]
      entries.set(key, { key, plugin, builds: [build], offer, selected: !!offer })
    }
    pending.value = imported; missing.value = [...entries.values()]
    if (!missing.value.length) await finish(false)
  } catch { if (expected === revision) error.value = t('pluginSchemes.importError') }
  finally { busy.value = false }
}
async function finish(install: boolean) {
  if (missingPreview.value) { pending.value = undefined; missing.value = []; return }
  const imported = pending.value, instance = selectedInstance.value, target = context(), expected = revision
  if (!imported) return
  busy.value = true; error.value = ''
  try {
    let downloading = false
    // Preserve unresolved entries in the saved scheme. Unchecked packages are not downloaded.
    if (install && instance) for (const entry of missing.value.filter(item => item.selected && item.offer)) {
      const { source, item } = entry.offer!
      const task = await call('store.downloadPlugin', { sourceId: source.id, packageId: item.id, version: item.version, manifestSha256: item.manifestSha256 })
      trackDownload(task); downloading = true
      if (expected !== revision || target !== context()) return
    }
    await addPluginScheme(imported.name, imported)
    if (expected !== revision || target !== context()) return
    pending.value = undefined; missing.value = []; notice.value = t(downloading ? 'pluginSchemes.pending' : 'pluginSchemes.imported')
  } catch (e) { error.value = (e as Error).message }
  finally { busy.value = false }
}
</script>
<template>
  <WDialog :open="open" :title="t('pluginSchemes.share')" :close-text="t('updates.close')" :close-disabled="busy" @close="emit('close')">
    <div class="share-body"><p>{{ t('pluginSchemes.shareBody') }}</p><div class="share-actions"><WButton :disabled="busy || !scheme" @click="exportFile">{{ t('pluginSchemes.export') }}</WButton><WButton variant="accent" :disabled="busy || !selectedInstance" @click="chooseFile">{{ t('pluginSchemes.import') }}</WButton></div><input ref="file" type="file" accept=".toml" hidden @change="fileChosen" /><ShellNotification v-if="notice" severity="success" :message="notice" @close="notice = ''" /><WInfoBar v-if="error" severity="error" @close="error = ''">{{ error }}</WInfoBar></div>
  </WDialog>
  <WDialog :open="!!pending && missing.length > 0" wide primary-first :title="t('pluginSchemes.import')" :primary-text="t('pluginSchemes.install')" :secondary-text="t('pluginSchemes.importOnly')" :close-text="t('dialog.cancel')" :primary-disabled="busy || !missing.some(item => item.selected && item.offer)" :secondary-disabled="busy" :close-disabled="busy" @primary="finish(true)" @secondary="finish(false)" @close="pending = undefined">
    <div class="share-body"><p>{{ t('pluginSchemes.missing') }}</p><PackageSummaryCard v-for="entry in missing" :key="entry.key" :name="entry.offer ? localized(entry.offer.item.name) : entry.plugin.id" glyph="puzzle"><template #meta><span>{{ entry.plugin.version }}</span><span>{{ entry.builds.join(' · ') }}</span></template><WCheckBox v-model="entry.selected" :disabled="!entry.offer || busy">{{ t('pluginSchemes.installPlugin') }}</WCheckBox><WInfoBar v-if="!entry.offer" severity="warning" :closable="false">{{ t('pluginSchemes.unavailable') }}</WInfoBar></PackageSummaryCard><WInfoBar v-if="error" severity="error" :closable="false">{{ error }}</WInfoBar></div>
  </WDialog>
</template>
<style scoped>
.share-body { display: grid; gap: 16px; }.share-body p { margin: 0; line-height: 1.6; }.share-actions { display: flex; gap: 12px; }.share-body :deep(.w-infobar) { margin-top: 10px; }
</style>

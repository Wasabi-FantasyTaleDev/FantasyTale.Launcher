<script setup lang="ts">
import { ref, watch, onScopeDispose } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost, on, type PluginImportPreview } from '../bridge'
import { localized } from '../i18n'
import { refreshInstances, state } from '../state'
import WProgressBar from '../controls/WProgressBar.vue'
import WDialog from '../controls/WDialog.vue'
import WButton from '../controls/WButton.vue'
import WContentTransition from '../controls/WContentTransition.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import WCheckBox from '../controls/WCheckBox.vue'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ close: []; imported: [name: string, version: string] }>()
const { t } = useI18n()
const game = ref(''), path = ref(''), busy = ref(false), error = ref(''), preview = ref<PluginImportPreview>(), enableAfterInstall = ref(true)
let revision = 0, installingToken = ''
const operationId = ref(''), progress = ref<{ stage: string; percent: number | null }>()
const unsubscribe = on('plugins.importProgress', value => {
  const event = value as { operationId: string; stage: string; percent: number | null }
  if (props.open && event.operationId === operationId.value) progress.value = event
})
onScopeDispose(() => { revision++; unsubscribe() })
function beginOperation() { operationId.value = crypto.randomUUID().replaceAll('-', ''); progress.value = { stage: 'checking', percent: null } }
watch(() => props.open, open => {
  revision++; busy.value = false
  if (open) { game.value = state.selectedGameId; path.value = ''; preview.value = undefined; error.value = ''; progress.value = undefined; operationId.value = ''; enableAfterInstall.value = true }
})
async function discard(token = preview.value?.token) {
  if (!token || token === installingToken) return
  await call('plugins.discardImport', { token })
  if (preview.value?.token === token) preview.value = undefined
}
function close() {
  revision++; const token = preview.value?.token; preview.value = undefined; busy.value = false; emit('close')
  void discard(token).catch(e => { state.error = (e as Error).message })
}
async function browse(kind: 'zip' | 'folder') {
  if (busy.value || !(inHost || !!demoMode?.enabled)) return
  const current = revision, selectedGame = game.value
  busy.value = true; error.value = ''
  try {
    const selected = await call(kind === 'zip' ? 'shell.pickPluginZip' : 'shell.pickFolder', { title: t('pluginImport.title') })
    if (!selected || current !== revision) return
    await discard()
    if (current !== revision) return
    path.value = selected; beginOperation()
    const prepared = await call('plugins.prepareImport', { game: selectedGame, path: selected, operationId: operationId.value })
    if (current !== revision) { await discard(prepared.token); return }
    preview.value = prepared
  } catch (e) { if (current === revision) error.value = (e as Error).message }
  finally { if (current === revision) busy.value = false }
}
async function install() {
  if (busy.value || !preview.value) return
  const current = revision, imported = preview.value, selectedGame = game.value
  busy.value = true; error.value = ''; installingToken = imported.token
  try {
    beginOperation()
    await call('plugins.installImport', { game: selectedGame, token: imported.token, operationId: operationId.value, enable: enableAfterInstall.value })
    await refreshInstances()
    if (current !== revision) return
    preview.value = undefined; emit('imported', localized(imported.name), imported.version); emit('close')
  } catch (e) { if (current === revision) error.value = (e as Error).message }
  finally { if (installingToken === imported.token) installingToken = ''; if (current === revision) busy.value = false }
}
</script>

<template>
  <WDialog :open="open" :title="t('pluginImport.title')" :primary-text="t(busy && preview ? 'pluginImport.installing' : 'pluginImport.install')" :close-text="t('dialog.cancel')" :primary-disabled="busy || !preview || !(inHost || !!demoMode?.enabled)" :primary-busy="busy" @primary="install" @close="close">
    <div class="form-stack">
      <div class="package-choices"><WButton :disabled="busy || !(inHost || !!demoMode?.enabled) || !game" @click="browse('zip')"><FluentIcon glyph="upload" />{{ t('pluginImport.zip') }}</WButton><WButton :disabled="busy || !(inHost || !!demoMode?.enabled) || !game" @click="browse('folder')"><FluentIcon glyph="folderOpen" />{{ t('pluginImport.folder') }}</WButton></div>
      <p v-if="path" class="package-path">{{ path }}</p>
      <WContentTransition><div :key="preview?.token ?? (busy ? 'checking' : 'empty')" class="package-preview">
        <template v-if="preview"><div class="package-identity"><FluentIcon :glyph="preview.kind === 'native' ? 'code' : 'puzzle'" :size="24" /><div><strong>{{ localized(preview.name) }}</strong><span>{{ preview.version }} · {{ preview.id }}</span></div></div><p v-if="preview.description">{{ localized(preview.description) }}</p><div class="package-facts"><span>{{ t('pluginImport.ready') }}</span><span>{{ t('pluginImport.files', { count: preview.fileCount, size: (preview.size / 1048576).toLocaleString(undefined, { maximumFractionDigits: 2 }) }) }}</span></div></template>
        <p v-else role="status">{{ t(busy ? 'pluginImport.checking' : 'pluginImport.choose') }}</p>
      </div></WContentTransition>
      <WCheckBox v-if="preview" :model-value="enableAfterInstall" :disabled="busy" @update:model-value="enableAfterInstall = $event">{{ t('pluginPolicy.importEnable') }}</WCheckBox>
      <div v-if="busy && progress" class="import-progress" role="status">
        <WProgressBar :indeterminate="progress.percent == null" :value="progress.percent ?? 0" />
        <span>{{ t(progress.stage === 'copying' ? 'updates.installing' : `updates.${progress.stage}`) }}</span>
        <span v-if="progress.percent != null">{{ Math.floor(progress.percent) }}%</span>
      </div>
      <p v-if="error" class="package-error" role="alert">{{ error }}</p>
    </div>
  </WDialog>
</template>

<style scoped>
.import-progress { display: grid; grid-template-columns: 1fr auto; gap: 8px; font-size: 12px; color: var(--text-secondary); }.import-progress > :first-child { grid-column: 1 / -1; }
.package-choices { display: flex; gap: 8px; }.package-choices > * { flex: 1; }
.package-preview { padding: 16px; border: 1px solid var(--stroke-divider); border-radius: var(--radius-control); background: var(--layer-default); }
.package-identity { display: flex; gap: 12px; align-items: center; }.package-identity > div { display: grid; gap: 4px; min-width: 0; }.package-identity strong { font-size: 14px; font-weight: 600; }.package-identity span { font-size: 12px; color: var(--text-secondary); overflow-wrap: anywhere; }
.package-preview p { font-size: 13px; line-height: 1.6; margin: 12px 0 0; color: var(--text-secondary); }.package-preview > p:first-child { margin: 0; }
.package-facts { display: flex; flex-wrap: wrap; gap: 8px 16px; margin-top: 14px; font-size: 12px; color: var(--text-secondary); }.package-facts > :first-child { color: var(--accent-text-primary); }
.package-path, .package-hint, .package-error { margin: 0; font-size: 12px; line-height: 1.6; overflow-wrap: anywhere; color: var(--text-secondary); }.package-error { color: var(--text-primary); }
</style>

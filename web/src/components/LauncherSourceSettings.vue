<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, type LauncherUpdateSourceSettings } from '../bridge'
import WSettingsCard from '../controls/WSettingsCard.vue'
import WTextBox from '../controls/WTextBox.vue'
import WButton from '../controls/WButton.vue'
import WComboBox from '../controls/WComboBox.vue'
import WDialog from '../controls/WDialog.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'

const { t } = useI18n()
const sources = ref<LauncherUpdateSourceSettings[]>([]), busy = ref(false), error = ref('')
const open = ref(false), editor = ref(false), editing = ref(-1)
const draft = ref<LauncherUpdateSourceSettings>()
async function load() { busy.value = true; try { sources.value = await call('launcherUpdates.sources') } catch (e) { error.value = (e as Error).message } finally { busy.value = false } }
function edit(index = -1) { editing.value = index; error.value = ''; draft.value = index < 0 ? { name: '', kind: 'http', baseUrl: '', feedPath: 'FantasyTale/launcher/win-x64/current.toml', minimumRevision: 0 } : { ...sources.value[index] }; editor.value = true }
async function commit() {
  if (!draft.value) return
  const previous = sources.value
  sources.value = [...previous]
  if (editing.value < 0) sources.value.push({ ...draft.value }); else sources.value[editing.value] = { ...draft.value }
  await save()
  if (error.value) sources.value = previous; else { editor.value = false; draft.value = undefined }
}
function remove(index: number) { if (sources.value.length > 1) sources.value.splice(index, 1) }
function move(index: number, delta: number) { const target = index + delta; if (target < 0 || target >= sources.value.length) return; const [item] = sources.value.splice(index, 1); sources.value.splice(target, 0, item) }
async function save() { busy.value = true; error.value = ''; try { sources.value = await call('launcherUpdates.setSources', { sources: sources.value }) } catch (e) { error.value = (e as Error).message } finally { busy.value = false } }
onMounted(load)
</script>
<template>
  <h2 class="section-title">{{ t('launcherUpdates.sourcesTitle') }}</h2>
  <WSettingsCard icon="globe" clickable :header="t('store.manage')" :description="sources.map(source => source.name).join(' · ')" @click="open = true" />
  <WDialog :open="open" :title="t('launcherUpdates.sourcesTitle')" :primary-text="t('launcherUpdates.saveSources')" :close-text="t('titlebar.close')" :primary-disabled="busy || !sources.length" :close-disabled="busy" @primary="save" @close="open = false; load()">
    <div class="source-heading"><span>{{ t('store.sourceList') }}</span><WButton variant="subtle" :disabled="busy" @click="edit()"><FluentIcon glyph="add" />{{ t('store.add') }}</WButton></div>
    <div class="source-list">
      <article v-for="(source, index) in sources" :key="index" class="source-row surface">
        <FluentIcon glyph="globe" :size="20" />
        <button type="button" class="source-copy" :disabled="busy" @click="edit(index)"><strong>{{ source.name }}</strong><small>{{ source.baseUrl }}</small></button>
        <WButton variant="subtle" :aria-label="t('launcherUpdates.moveUp')" :disabled="busy || index === 0" @click="move(index, -1)">↑</WButton>
        <WButton variant="subtle" :aria-label="t('launcherUpdates.moveDown')" :disabled="busy || index === sources.length - 1" @click="move(index, 1)">↓</WButton>
        <WButton variant="subtle" :aria-label="t('store.edit') + ' · ' + source.name" :disabled="busy" @click="edit(index)"><FluentIcon glyph="edit" /></WButton>
        <WButton variant="subtle" :aria-label="t('store.remove') + ' · ' + source.name" :disabled="busy || sources.length < 2" @click="remove(index)"><FluentIcon glyph="delete" /></WButton>
      </article>
    </div>
    <p v-if="error && !editor" role="alert" class="settings-footnote">{{ error }}</p>
    <WDialog :open="editor" :title="t(editing < 0 ? 'store.add' : 'store.edit')" :primary-text="t('store.save')" :close-text="t('dialog.cancel')" :primary-disabled="busy || !draft?.name.trim() || !draft?.baseUrl.trim() || !draft?.feedPath.trim()" :close-disabled="busy" @primary="commit" @close="editor = false; draft = undefined">
      <div v-if="draft" class="form-stack">
        <label class="field"><span>{{ t('launcherUpdates.sourceName') }}</span><WTextBox v-model="draft.name" :maxlength="128" :disabled="busy" /></label>
        <label class="field"><span>{{ t('launcherUpdates.sourceTransport') }}</span><WComboBox v-model="draft.kind" :options="[{ value: 'http', label: 'HTTP' }, { value: 'alist', label: 'AList' }, { value: 'github', label: 'GitHub' }]" :disabled="busy" /></label>
        <label class="field"><span>{{ t('launcherUpdates.sourceUrl') }}</span><WTextBox v-model="draft.baseUrl" :maxlength="2048" :disabled="busy" /></label>
        <div class="source-heading"><span>{{ t('service.ipPreference') }}</span><WToggleSwitch :model-value="draft.preferIp ?? false" :aria-label="t('service.ipPreference')" :disabled="busy" @update:model-value="draft.preferIp = $event" /></div>
        <label class="field"><span>{{ t('launcherUpdates.feedPath') }}</span><WTextBox v-model="draft.feedPath" :maxlength="1024" :disabled="busy" /></label>
        <p v-if="error" role="alert" class="settings-footnote">{{ error }}</p>
      </div>
    </WDialog>
  </WDialog>
</template>
<style scoped>
.source-list { display: grid; gap: 8px; max-height: 360px; overflow-y: auto; }
.source-heading { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
.source-row { display: flex; align-items: center; gap: 4px; padding: 12px; }
.source-row > .fluent-icon { flex: none; margin-right: 8px; color: var(--text-secondary); }
.source-copy { display: grid; gap: 4px; min-width: 0; flex: 1; padding: 4px; background: none; border: 0; color: var(--text-primary); text-align: left; cursor: pointer; }
.source-copy small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text-secondary); font-size: 12px; }
.settings-footnote { color: var(--text-secondary); font-size: 12px; overflow-wrap: anywhere; }
</style>

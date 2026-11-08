<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call } from '../bridge'
import { state } from '../state'
import type { StoreSource } from '../store/types'
import WDialog from '../controls/WDialog.vue'
import WButton from '../controls/WButton.vue'
import WTextBox from '../controls/WTextBox.vue'
import WComboBox from '../controls/WComboBox.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
import WSettingsExpander from '../controls/WSettingsExpander.vue'
import FluentIcon from '../controls/FluentIcon.vue'

const props = defineProps<{ open: boolean; sources: StoreSource[] }>()
const emit = defineEmits<{ close: []; changed: [] }>()
const { t } = useI18n()
const draft = ref<StoreSource>(), removing = ref<StoreSource>(), busy = ref(false), error = ref(''), advanced = ref(false)
watch(() => props.open, () => { draft.value = undefined; removing.value = undefined; error.value = '' })
const validation = computed(() => {
  if (!draft.value) return ''
  try {
    const raw = draft.value.baseUrl?.trim() ?? ''
    if (raw || !draft.value.builtIn) {
      const url = new URL(raw)
      if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash || raw.length > 2048 || /[\x00-\x1f]/.test(raw)) return t('store.invalidUrl')
    }
  } catch { return t('store.invalidUrl') }
  const path = draft.value.catalogPath
  if (!path || path.length > 1024 || /[\\:?#%\x00-\x1f]/.test(path) || path.split('/').some(p => !p || p === '.' || p === '..' || p.trim() !== p)) return t('store.invalidPath')
  return ''
})
function edit(source?: StoreSource) {
  error.value = ''; advanced.value = false
  draft.value = source ? { ...source } : { id: `source-${crypto.randomUUID().slice(0, 12)}`, name: '', kind: 'alist', baseUrl: '', catalogPath: 'FantasyTale/source.toml', enabled: true, builtIn: false, preferIp: null }
}
async function save() {
  if (!draft.value || validation.value) return
  busy.value = true; error.value = ''
  try {
    const { builtIn: _, ...source } = draft.value
    await call('store.saveSource', { ...source, name: source.name.trim(), baseUrl: source.baseUrl?.trim() || null })
    draft.value = undefined; emit('changed')
  } catch (e) { error.value = (e as Error).message }
  finally { busy.value = false }
}
async function remove() {
  if (!removing.value || removing.value.builtIn || busy.value) return
  busy.value = true; error.value = ''
  try { await call('store.removeSource', { id: removing.value.id }); removing.value = undefined; emit('changed') }
  catch (e) { error.value = (e as Error).message }
  finally { busy.value = false }
}
</script>

<template>
  <WDialog :open="open" :title="t('store.manage')" :close-text="t('titlebar.close')" :close-disabled="busy" @close="emit('close')">
    <div class="sources-heading"><span class="muted">{{ t('store.sourceList') }}</span><WButton variant="subtle" :disabled="busy" @click="edit()"><FluentIcon glyph="add" />{{ t('store.add') }}</WButton></div>
    <div class="sources-list">
      <article v-for="source in sources" :key="source.id" class="source-row surface">
        <FluentIcon glyph="globe" :size="20" />
        <div class="source-copy"><strong>{{ source.name }}</strong><small>{{ source.baseUrl || t('store.missing') }}</small><span>{{ source.kind === 'alist' ? 'AList' : source.kind === 'github' ? 'GitHub' : 'HTTP' }}<template v-if="source.builtIn"> · {{ t('store.bundled') }}</template><template v-if="!source.enabled"> · {{ t('store.disabled') }}</template></span></div>
        <div class="source-actions">
          <WButton variant="subtle" :aria-label="`${t('store.edit')} · ${source.name}`" :title="t('store.edit')" :disabled="busy" @click="edit(source)"><FluentIcon glyph="edit" /></WButton>
          <WButton v-if="!source.builtIn" variant="subtle" :aria-label="`${t('store.remove')} · ${source.name}`" :title="t('store.remove')" :disabled="busy" @click="removing = source"><FluentIcon glyph="delete" /></WButton>
        </div>
      </article>
    </div>
    <p v-if="error && !draft && !removing" class="source-error" role="alert">{{ error }}</p>
    <WDialog :open="!!draft" :title="t(sources.some(s => s.id === draft?.id) ? 'store.edit' : 'store.add')" :primary-text="t(busy ? 'store.working' : 'store.save')" :close-text="t('dialog.cancel')" :primary-disabled="busy || !draft?.name.trim() || !!validation" :close-disabled="busy" @primary="save" @close="draft = undefined">
      <div v-if="draft" class="form-stack">
        <label class="field"><span>{{ t('store.name') }}</span><WTextBox v-model="draft.name" :maxlength="128" :disabled="busy" /></label>
        <label class="field"><span>{{ t('store.kind') }}</span><WComboBox :model-value="draft.kind" :options="[{ value: 'alist', label: 'AList' }, { value: 'http', label: 'HTTP' }, { value: 'github', label: 'GitHub' }]" :disabled="busy" @update:model-value="draft.kind = $event === 'github' ? 'github' : $event === 'http' ? 'http' : 'alist'" /></label>
        <label class="field"><span>{{ t('store.url') }}</span><WTextBox :model-value="draft.baseUrl ?? ''" placeholder="https://files.example.org" :maxlength="2048" :disabled="busy" @update:model-value="draft.baseUrl = $event" /></label>
        <div class="source-toggle"><span>{{ t('store.enabled') }}</span><WToggleSwitch v-model="draft.enabled" :aria-label="t('store.enabled')" :disabled="busy" /></div>
        <div class="source-toggle"><span>{{ t('service.ipPreference') }}</span><WToggleSwitch :model-value="draft.preferIp ?? state.ipPreference !== 'disabled'" :aria-label="t('service.ipPreference')" :disabled="busy" @update:model-value="draft.preferIp = $event" /></div>
        <WSettingsExpander v-model:open="advanced" class="catalog-location" :header="t('store.advanced')"><label class="field catalog-field"><span>{{ t('store.path') }}</span><WTextBox v-model="draft.catalogPath" :maxlength="1024" :disabled="busy" /></label></WSettingsExpander>
        <p v-if="error || (draft.baseUrl && validation)" class="source-error" role="alert">{{ error || validation }}</p>
      </div>
    </WDialog>
    <WDialog :open="!!removing" :title="t('store.removeTitle')" :primary-text="t('store.remove')" :close-text="t('dialog.cancel')" :primary-disabled="busy" :close-disabled="busy" @primary="remove" @close="removing = undefined">
      <p>{{ t('store.removeBody', { name: removing?.name }) }}</p><p v-if="error" class="source-error" role="alert">{{ error }}</p>
    </WDialog>
  </WDialog>
</template>

<style scoped>
.sources-heading, .source-toggle { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
.sources-heading { margin-bottom: 12px; }
.sources-list { display: grid; gap: 8px; max-height: 360px; overflow-y: auto; }
.source-row { display: flex; align-items: center; gap: 12px; padding: 14px; }
.source-copy { display: grid; gap: 4px; min-width: 0; flex: 1; }
.source-actions { display: flex; align-items: center; gap: 4px; flex: none; }
.source-actions > .w-btn { width: 32px; height: 32px; padding: 0; }
.source-copy strong { font-weight: 600; }.source-copy small, .source-copy > span { color: var(--text-secondary); font-size: 12px; overflow-wrap: anywhere; }
.source-copy > span { color: var(--text-tertiary); }.source-row > .fluent-icon { color: var(--text-secondary); }
.source-toggle { padding: 4px 0; }.catalog-field { padding: 12px 14px 16px; }.source-error { color: var(--text-secondary); font-size: 12px; overflow-wrap: anywhere; margin: 0; }
/* This form has no leading icon column, so its nested field aligns with the expander title. */
.catalog-location :deep(.w-expander-header) { min-height: 44px; padding: 10px 14px; }
.catalog-location :deep(.w-card-header) { font-weight: 400; }
.catalog-location :deep(.w-expander-body) { padding: 0; }
</style>

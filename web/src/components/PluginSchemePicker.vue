<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { addPluginScheme, choosePluginScheme, deletePluginScheme, isRunning, pluginSchemeLibrary, pluginSchemesBusy, selectedInstance, selectedPluginScheme, state } from '../state'
import { defaultScheme, type PluginScheme } from '../plugins/schemes'
import WComboBox from '../controls/WComboBox.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import WDialog from '../controls/WDialog.vue'
import WTextBox from '../controls/WTextBox.vue'
import WInfoBar from '../controls/WInfoBar.vue'
const props = defineProps<{ compact?: boolean; disabled?: boolean }>()
const { t } = useI18n()
const adding = ref(false), deleting = ref<PluginScheme>(), name = ref(''), error = ref(''), busy = ref(false)
const locked = computed(() => props.disabled || state.busy || busy.value || pluginSchemesBusy.value || !!selectedInstance.value && isRunning(selectedInstance.value.id))
const options = computed(() => [...pluginSchemeLibrary.value.schemes.map(s => ({ value: s.id, label: s.id === defaultScheme ? t('pluginSchemes.default') : s.name })), { value: 'action:add', label: t('pluginSchemes.add') }])
async function action(run: () => Promise<void>) {
  busy.value = true; error.value = ''
  try { await run() } catch (e) { error.value = (e as Error).message } finally { busy.value = false }
}
async function select(id: string) {
  if (id === 'action:add') { name.value = ''; error.value = ''; adding.value = true; return }
  await action(() => choosePluginScheme(id))
}
async function add() { await action(async () => { await addPluginScheme(name.value); adding.value = false }) }
function requestRemoval(id: string, close: () => void) {
  close(); error.value = ''
  deleting.value = pluginSchemeLibrary.value.schemes.find(s => s.id === id && s.id !== defaultScheme)
}
async function remove() { const target = deleting.value; if (target) await action(async () => { await deletePluginScheme(target.id); deleting.value = undefined }) }
watch(() => [state.selectedGameId, state.selectedServerId], () => { adding.value = false; deleting.value = undefined; error.value = '' })
</script>
<template>
  <div class="scheme-picker" @click.stop>
    <WComboBox :model-value="selectedPluginScheme" :options="options" :disabled="locked" :aria-label="t('pluginSchemes.title')" @update:model-value="select">
      <template #option-action="{ option, close }">
        <button v-if="option.value !== defaultScheme && option.value !== 'action:add'" type="button" class="scheme-remove" :disabled="locked" :aria-label="t('pluginSchemes.deleteNamed', { name: option.label })" :title="t('pluginSchemes.delete')" @click.stop="requestRemoval(option.value, close)"><FluentIcon glyph="cancel" :size="12" /></button>
      </template>
    </WComboBox>
    <WDialog :open="adding" :title="t('pluginSchemes.add')" :primary-text="t('pluginSchemes.addConfirm')" :close-text="t('dialog.cancel')" :primary-disabled="busy || !name.trim()" :close-disabled="busy" @primary="add" @close="adding = false">
      <WTextBox v-model="name" :maxlength="64" :placeholder="t('pluginSchemes.name')" :aria-label="t('pluginSchemes.name')" @keydown.enter="name.trim() && !busy && add()" />
      <WInfoBar v-if="error" severity="error" :closable="false">{{ error }}</WInfoBar>
    </WDialog>
    <WDialog :open="!!deleting" :title="t('pluginSchemes.deleteTitle')" :primary-text="t('pluginSchemes.delete')" :close-text="t('dialog.cancel')" :primary-disabled="busy" :close-disabled="busy" focus-panel @primary="remove" @close="deleting = undefined">
      <p>{{ t('pluginSchemes.deleteBody', { name: deleting?.name }) }}</p><WInfoBar v-if="error" severity="error" :closable="false">{{ error }}</WInfoBar>
    </WDialog>
  </div>
  <WInfoBar v-if="error && !adding && !deleting" severity="error" @close="error = ''">{{ error }}</WInfoBar>
</template>
<style scoped>
.scheme-picker { display: flex; align-items: center; gap: 8px; min-width: 0; flex: none; }
.scheme-picker > :first-child { width: 174px; max-width: 220px; }
.scheme-remove { display: flex; align-items: center; justify-content: center; flex: none; width: 30px; height: 30px; margin: 0 5px; border-radius: var(--radius-control); color: var(--text-secondary); }
.scheme-remove:hover:not(:disabled) { background: var(--subtle-secondary); color: var(--text-primary); }
.scheme-remove:disabled { opacity: 0.5; }
</style>

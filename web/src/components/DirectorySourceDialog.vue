<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, inHost, type DirectorySource } from '../bridge'
import WDialog from '../controls/WDialog.vue'
import WComboBox from '../controls/WComboBox.vue'
import WTextBox from '../controls/WTextBox.vue'
import WButton from '../controls/WButton.vue'
import WToggleSwitch from '../controls/WToggleSwitch.vue'
const props = defineProps<{ open: boolean; sources: DirectorySource[]; selectedId?: string }>()
const emit = defineEmits<{ close: []; saved: [id: string] }>()
const { t } = useI18n(), draft = ref<DirectorySource>(), busy = ref(false), error = ref(''), removing = ref(false)
const valid = computed(() => !!draft.value?.name.trim() && !!draft.value.baseUrl?.trim())
function select(id: string) { const source = props.sources.find(s => s.id === id); if (source) draft.value = { ...source }; error.value = '' }
function add() { draft.value = { id: `source-${crypto.randomUUID().slice(0, 12)}`, name: '', baseUrl: '', enabled: true, builtIn: false }; error.value = '' }
watch(() => props.open, open => { if (open) { draft.value = undefined; removing.value = false; select(props.selectedId ?? props.sources[0]?.id ?? ''); if (!draft.value) add() } })
async function execute(remove = false) {
  if (!draft.value || busy.value) return
  busy.value = true; error.value = ''
  try {
    if (remove) await call('directory.deleteSource', { id: draft.value.id })
    else await call('directory.saveSource', { id: draft.value.id, name: draft.value.name.trim(), baseUrl: draft.value.baseUrl?.trim(), enabled: draft.value.enabled })
    removing.value = false; emit('saved', remove ? '' : draft.value.id); emit('close')
  } catch (e) { error.value = (e as Error).message } finally { busy.value = false }
}
</script>
<template>
  <WDialog :open="open" :title="t('multiplayer.sources')" :primary-text="t('hosting.save')" :close-text="t('dialog.cancel')" :primary-disabled="!valid || busy || !inHost" :close-disabled="busy" @primary="execute()" @close="emit('close')">
    <div v-if="draft" class="source-form">
      <div class="source-chooser"><WComboBox :model-value="draft.id" :options="sources.map(s => ({ value: s.id, label: s.name }))" :disabled="busy" :aria-label="t('multiplayer.source')" @update:model-value="select" /><WButton :disabled="busy" @click="add">{{ t('multiplayer.addSource') }}</WButton></div>
      <label><span>{{ t('hosting.name') }}</span><WTextBox v-model="draft.name" :disabled="busy" :maxlength="128" /></label>
      <label><span>{{ t('multiplayer.sourceUrl') }}</span><WTextBox v-model="draft.baseUrl!" :disabled="busy" placeholder="https://directory.example/" :maxlength="2048" /></label>
      <div class="source-enabled"><span>{{ t('multiplayer.enabled') }}</span><WToggleSwitch v-model="draft.enabled" :disabled="busy" /></div>
      <WButton v-if="!draft.builtIn && sources.some(s => s.id === draft?.id)" variant="subtle" :disabled="busy" @click="removing = true">{{ t('multiplayer.removeSource') }}</WButton>
      <p v-if="error" class="source-error" role="alert">{{ error }}</p>
    </div>
    <WDialog :open="removing" :title="t('multiplayer.removeSource')" :primary-text="t('service.remove')" :close-text="t('dialog.cancel')" :primary-disabled="busy" :close-disabled="busy" @primary="execute(true)" @close="removing = false"><p>{{ draft?.name }}</p></WDialog>
  </WDialog>
</template>
<style scoped>
.source-form { display: grid; gap: 20px; }.source-form label { display: grid; gap: 8px; }.source-form label > span,.source-enabled { font-size: 13px; }.source-chooser { display: flex; gap: 10px; }.source-chooser > :first-child { flex: 1; min-width: 0; }.source-enabled { display: flex; align-items: center; justify-content: space-between; }.source-error { color: #bd3434; overflow-wrap: anywhere; margin: 0; }
</style>

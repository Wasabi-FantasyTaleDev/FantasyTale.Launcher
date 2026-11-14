<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call } from '../bridge'
import type { NativeUiCapabilities, NativeUiControl, NativeUiControlTarget, NativeUiKind, NativeUiRequest, NativeUiResult } from '../nativeUi'
import { nativeControlMessages } from './nativeControlMessages'
import WExpander from '../controls/WExpander.vue'
import WTextBox from '../controls/WTextBox.vue'
import WButton from '../controls/WButton.vue'

const props = defineProps<{ client: { id: string; processId: number } | null; capabilities: NativeUiCapabilities | null }>()
const { t } = useI18n({ useScope: 'local', messages: nativeControlMessages })
const form = ref(''), name = ref(''), id = ref(''), index = ref(''), match = ref(false), current = ref(''), text = ref('')
const buttonName = ref('developer-button'), x = ref(12), y = ref(12), width = ref(120), height = ref(27)
const result = ref<NativeUiResult>(), error = ref(''), pending = ref(false), cursor = ref(0)
let generation = 0, stopped = false
const selector = computed<NativeUiControlTarget>(() => ({
  ...(form.value ? { form: form.value } : {}), ...(name.value ? { name: name.value } : {}),
  ...(id.value ? { id: id.value } : {}), ...(index.value !== '' ? { arrayIndex: Number(index.value) } : {}),
  ...(match.value ? { text: current.value } : {}),
}))
function enabled(kind: NativeUiKind) {
  return !!props.client && !pending.value && !!props.capabilities?.operations.some(operation => operation.kind === kind && operation.supported && operation.ready)
}
function use(control: NativeUiControl) {
  form.value = control.form; id.value = control.id; name.value = ''; index.value = ''; match.value = false
  current.value = control.text; text.value = control.text
}
async function invoke(kind: NativeUiKind) {
  const client = props.client
  if (!client || !enabled(kind)) return
  const epoch = generation, operationId = crypto.randomUUID()
  const request: NativeUiRequest = { operationId, kind, text: '', target: selector.value }
  if (kind === 'control.setText' || kind === 'control.addButton') request.text = text.value
  if (kind === 'control.addButton') request.definition = {name:buttonName.value,x:x.value,y:y.value,width:width.value,height:height.value}
  if (kind === 'control.events') { request.afterSequence = cursor.value; delete request.target }
  pending.value = true; error.value = ''
  try {
    let response = await call('nativeUi.invoke', {...client, request})
    for (let attempt = 0; response.status === 'queued' && attempt < 40; attempt++) {
      await new Promise(resolve => setTimeout(resolve, 250))
      if (stopped || epoch !== generation) return
      response = await call('nativeUi.result', {...client, operationId})
    }
    if (stopped || epoch !== generation) return
    result.value = response
    if (response.cursor != null) cursor.value = response.cursor
  } catch (failure) { if (!stopped && epoch === generation) error.value = (failure as Error).message }
  finally { if (!stopped && epoch === generation) pending.value = false }
}
watch(() => `${props.client?.id}:${props.client?.processId}`, () => {
  generation++; pending.value = false; result.value = undefined; cursor.value = 0; id.value = ''; error.value = ''
})
onBeforeUnmount(() => { stopped = true; generation++ })
</script>

<template>
  <WExpander settings :header="t('title')" :description="t('help')">
    <div class="control-form">
      <label><span>{{ t('form') }}</span><WTextBox v-model="form" :placeholder="t('auto')" /></label>
      <label><span>{{ t('name') }}</span><WTextBox v-model="name" /></label>
      <label><span>{{ t('index') }}</span><input v-model="index" type="number" min="0" /></label>
      <label><span>{{ t('id') }}</span><WTextBox v-model="id" /></label>
      <label class="match"><input v-model="match" type="checkbox" /><span>{{ t('filter') }}</span></label>
      <label v-if="match"><span>{{ t('current') }}</span><WTextBox v-model="current" /></label>
      <label><span>{{ t('text') }}</span><textarea v-model="text" rows="3" /></label>
      <div class="actions">
        <WButton :disabled="!enabled('control.inspect')" @click="invoke('control.inspect')">{{ t('inspect') }}</WButton>
        <WButton :disabled="!enabled('control.setText') || !Object.keys(selector).length" @click="invoke('control.setText')">{{ t('set') }}</WButton>
        <WButton :disabled="!enabled('control.activate') || !Object.keys(selector).length" @click="invoke('control.activate')">{{ t('activate') }}</WButton>
      </div>
      <label><span>{{ t('buttonName') }}</span><WTextBox v-model="buttonName" /></label>
      <div class="geometry">
        <label>X<input v-model.number="x" type="number" min="0" /></label>
        <label>Y<input v-model.number="y" type="number" min="0" /></label>
        <label>W<input v-model.number="width" type="number" min="1" /></label>
        <label>H<input v-model.number="height" type="number" min="1" /></label>
      </div>
      <div class="actions">
        <WButton :disabled="!enabled('control.addButton') || !Object.keys(selector).length" @click="invoke('control.addButton')">{{ t('add') }}</WButton>
        <WButton :disabled="!enabled('control.remove') || !id" @click="invoke('control.remove')">{{ t('remove') }}</WButton>
        <WButton :disabled="!enabled('control.events')" @click="invoke('control.events')">{{ t('events') }}</WButton>
      </div>
      <p v-if="error" role="alert">{{ error }}</p>
      <div v-if="result" role="status">{{ t('result') }}: {{ result.status }} {{ result.reason }}</div>
      <div v-for="control in result?.controls" :key="control.id" class="control-result">
        <code>{{ control.form }} / {{ control.name }} {{ control.arrayIndex == null ? '' : `[${control.arrayIndex}]` }} ({{ control.kind }})</code>
        <details><summary>{{ control.id }}</summary><pre>{{ JSON.stringify({ ...control, form: undefined, name: undefined, text: undefined }, null, 2) }}</pre></details>
        <pre>{{ control.text }}</pre><WButton @click="use(control)">{{ t('use') }}</WButton>
      </div>
      <pre v-if="result?.events?.length">{{ JSON.stringify(result.events, null, 2) }}</pre>
    </div>
  </WExpander>
</template>

<style scoped>
.control-form { display:grid; gap:16px; padding:20px 0; min-width:0; }
.control-form > label { display:grid; grid-template-columns:160px minmax(0,1fr); gap:16px; align-items:center; }
.control-form .match { display:flex; gap:8px; }
.actions,.geometry { display:flex; gap:8px; flex-wrap:wrap; }
.geometry label { display:grid; gap:4px; width:85px; }
textarea,input[type=number] { width:100%; box-sizing:border-box; color:var(--text-primary); background:var(--ctrl-fill-default); border:1px solid var(--ctrl-stroke-default); border-radius:var(--radius-control); padding:6px; }
.control-result { padding:12px; border:1px solid var(--ctrl-stroke-default); border-radius:var(--radius-control); }
pre,code { white-space:pre-wrap; overflow-wrap:anywhere; }
[role=alert] { color:var(--system-critical); }
@media(max-width:640px) { .control-form > label { grid-template-columns:minmax(0,1fr); gap:6px; } }
</style>

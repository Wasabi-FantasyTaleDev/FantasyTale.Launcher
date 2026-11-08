<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, type RunningClient } from '../bridge'
import { state } from '../state'
import type { NativeUiCapabilities, NativeUiKind, NativeUiRequest, NativeUiResult } from '../nativeUi'
import WSettingsCard from '../controls/WSettingsCard.vue'
import WExpander from '../controls/WExpander.vue'
import WComboBox from '../controls/WComboBox.vue'
import WTextBox from '../controls/WTextBox.vue'
import WButton from '../controls/WButton.vue'
import NativeChatColorPicker from './NativeChatColorPicker.vue'
import { hexColor } from '../colors'

const { t, te } = useI18n()
const clients = ref<RunningClient[]>([])
const selected = ref('')
const capabilities = ref<NativeUiCapabilities | null>(null)
const loading = ref(false)
const error = ref('')
const results = ref<Partial<Record<NativeUiKind, NativeUiResult>>>({})
const pending = ref(new Set<NativeUiKind>())
const title = ref('Fantasy Tale')
const text = ref('')
const icon = ref('none')
const buttons = ref([{ id: 'ok', label: '' }, { id: 'cancel', label: '' }])
const notice = ref('')
const announcement = ref('')
const announcementId = ref('developer.banner')
const style = ref('system')
const chatCaption = ref('Fantasy Tale')
const runs = ref([{ text: '', color: '#FFFFFF' }])
let generation = 0
let capabilityGeneration = 0
let stopped = false
let refreshing = false
let timer: ReturnType<typeof setInterval> | undefined
const pollTimers = new Set<ReturnType<typeof setTimeout>>()
const clientKey = (client: RunningClient) => `${client.instanceId}:${client.processId}`
const options = computed(() => clients.value.map(client => {
  const instance = state.instances.find(item => item.id === client.instanceId)
  return { value: clientKey(client), label: instance?.name ?? client.instanceId, group: instance?.build }
}))
const target = computed(() => clients.value.find(client => clientKey(client) === selected.value))
function selectedTarget() {
  const client = target.value
  if (!client) return null
  return { id: client.instanceId, processId: client.processId }
}
function clearPolls() {
  for (const timer of pollTimers) clearTimeout(timer)
  pollTimers.clear()
}
async function refreshClients() {
  if (refreshing) return
  refreshing = true
  try {
    const running = await call('instances.running')
    if (stopped) return
    clients.value = running
    if (!running.some(client => clientKey(client) === selected.value)) selected.value = running[0] ? clientKey(running[0]) : ''
  } catch (failure) { if (!stopped) error.value = (failure as Error).message }
  finally { refreshing = false }
}
async function refreshCapabilities(silent = false) {
  const epoch = generation
  const refresh = ++capabilityGeneration
  error.value = ''
  const client = selectedTarget()
  if (!client) { loading.value = false; return }
  if (!silent) loading.value = true
  try {
    const current = await call('nativeUi.capabilities', client)
    if (!stopped && epoch === generation && refresh === capabilityGeneration) capabilities.value = current
  } catch (failure) {
    if (!stopped && epoch === generation && refresh === capabilityGeneration) {
      capabilities.value = null
      error.value = (failure as Error).message
    }
  }
  finally { if (!stopped && epoch === generation && refresh === capabilityGeneration) loading.value = false }
}
function changeTarget() {
  generation++
  clearPolls()
  pending.value.clear()
  results.value = {}
  capabilities.value = null
  return refreshCapabilities()
}
function capability(kind: NativeUiKind) { return capabilities.value?.operations.find(item => item.kind === kind) }
const iconOptions = computed(() => ['none', 'notice', 'warning', 'question', 'error']
  .filter(value => capability('messageBox')?.features.includes(`icon:${value}`))
  .map(value => ({ value, label: t(`nativeUi.icons.${value}`) })))
const buttonRange = computed(() => {
  const range = capability('messageBox')?.features.map(feature => /^buttons:(\d+)(?:\.\.(\d+))?$/.exec(feature)).find(Boolean)
  return range ? { min: Number(range[1]), max: Number(range[2] ?? range[1]) } : { min: 1, max: 1 }
})
const richChat = computed(() => !!capability('chat')?.features.includes('color:runs'))
const ownedAnnouncements = computed(() => !!capability('announcement.close')?.supported)
function addButton() {
  if (buttons.value.length < buttonRange.value.max) buttons.value.push({ id: `button-${crypto.randomUUID()}`, label: '' })
}
function parametersSupported(kind: NativeUiKind) {
  if (kind === 'messageBox') return buttons.value.length >= buttonRange.value.min && buttons.value.length <= buttonRange.value.max
    && iconOptions.value.some(option => option.value === icon.value)
  if (kind === 'chat') return runs.value.every(run => hexColor(run.color) !== null)
    && (richChat.value || runs.value.every(run => hexColor(run.color) === hexColor(runs.value[0]?.color ?? '')))
  return true
}
function availability(kind: NativeUiKind) {
  return t(`nativeUi.${loading.value ? 'loading' : !capability(kind)?.supported ? 'unsupported' : capability(kind)?.ready ? 'ready' : 'notReady'}`)
}
function enabled(kind: NativeUiKind) { return !loading.value && !!target.value && !!capability(kind)?.ready && !!capability(kind)?.supported && !pending.value.has(kind) && parametersSupported(kind) }
function resultText(kind: NativeUiKind) {
  const result = results.value[kind]
  if (!result) return ''
  const key = `nativeUi.statuses.${result.status}`
  const label = te(key) ? t(key) : result.status
  const reason = result.reason && te(`nativeUi.reasons.${result.reason}`) ? t(`nativeUi.reasons.${result.reason}`) : result.reason
  return result.buttonId ? t('nativeUi.selected', { button: result.buttonId }) : [label, reason].filter(Boolean).join(' · ')
}
function stillPending(kind: NativeUiKind, result: NativeUiResult) { return result.status === 'queued' || kind === 'messageBox' && result.status === 'shown' }
function poll(kind: NativeUiKind, operationId: string, client: { id: string; processId: number }, epoch: number) {
  const timer = setTimeout(async () => {
    pollTimers.delete(timer)
    if (stopped || epoch !== generation) return
    try {
      const result = await call('nativeUi.result', { ...client, operationId })
      if (stopped || epoch !== generation) return
      results.value[kind] = result
      if (stillPending(kind, result)) poll(kind, operationId, client, epoch)
      else pending.value.delete(kind)
    } catch (failure) {
      if (stopped || epoch !== generation) return
      results.value[kind] = { operationId, status: 'outcomeUnknown', reason: 'channelInterrupted' }
      pending.value.delete(kind)
      error.value = (failure as Error).message
    }
  }, 600)
  pollTimers.add(timer)
}
async function invoke(kind: NativeUiKind) {
  const client = selectedTarget()
  if (!client || !enabled(kind)) return
  const epoch = generation
  const operationId = crypto.randomUUID()
  const request: NativeUiRequest = { operationId, kind, text: '' }
  if (kind === 'messageBox') Object.assign(request, { title: title.value, text: text.value, icon: icon.value, buttons: buttons.value.map(button => ({ ...button })) })
  if (kind === 'notification') request.text = notice.value
  if (kind.startsWith('announcement.')) Object.assign(request, { announcementId: ownedAnnouncements.value ? announcementId.value : undefined, text: kind === 'announcement.show' ? announcement.value : '' })
  if (kind === 'chat') Object.assign(request, { chatStyle: style.value, chatCaption: chatCaption.value, runs: runs.value.map(run => ({ ...run, color: hexColor(run.color)! })) })
  pending.value.add(kind)
  error.value = ''
  try {
    const result = await call('nativeUi.invoke', { ...client, request })
    if (stopped || epoch !== generation) return
    results.value[kind] = result
    if (stillPending(kind, result)) poll(kind, operationId, client, epoch)
    else pending.value.delete(kind)
  } catch (failure) {
    if (stopped || epoch !== generation) return
    pending.value.delete(kind)
    error.value = (failure as Error).message
  }
}
watch(selected, changeTarget)
watch(iconOptions, options => { if (!options.some(option => option.value === icon.value)) icon.value = options[0]?.value ?? 'none' })
onMounted(async () => {
  buttons.value = [{ id: 'ok', label: t('nativeUi.ok') }, { id: 'cancel', label: t('nativeUi.cancel') }]
  await refreshClients()
  if (!stopped) timer = setInterval(async () => {
    await refreshClients()
    if (!stopped && selectedTarget()) await refreshCapabilities(true)
  }, 2000)
})
onUnmounted(() => { stopped = true; generation++; clearInterval(timer); clearPolls() })
</script>

<template>
  <div class="native-tests">
    <WSettingsCard :header="t('nativeUi.client')" :description="!clients.length ? t('nativeUi.noClient') : undefined">
      <div class="client-actions"><WComboBox v-model="selected" :options="options" :disabled="!clients.length" :aria-label="t('nativeUi.client')" /><WButton @click="refreshCapabilities()">{{ t('nativeUi.refresh') }}</WButton></div>
    </WSettingsCard>
    <p v-if="error" class="test-error" role="alert">{{ error }}</p>
    <WExpander settings :header="t('nativeUi.messageBox')" :description="availability('messageBox')">
      <div class="test-form">
        <label><span>{{ t('nativeUi.heading') }}</span><WTextBox v-model="title" /></label>
        <label><span>{{ t('nativeUi.text') }}</span><textarea v-model="text" rows="3" /></label>
        <label><span>{{ t('nativeUi.icon') }}</span><WComboBox v-model="icon" :options="iconOptions" :disabled="!iconOptions.length" /></label>
        <div class="repeated-fields">
          <div v-for="(button, index) in buttons" :key="index" class="button-fields">
            <WTextBox v-model="button.id" :aria-label="t('nativeUi.buttonId')" :placeholder="t('nativeUi.buttonId')" />
            <WTextBox v-model="button.label" :aria-label="t('nativeUi.buttonLabel')" :placeholder="t('nativeUi.buttonLabel')" />
            <WButton :disabled="buttons.length <= buttonRange.min" @click="buttons.splice(index, 1)">{{ t('nativeUi.remove') }}</WButton>
          </div>
          <WButton :disabled="buttons.length >= buttonRange.max" @click="addButton">{{ t('nativeUi.addButton') }}</WButton>
        </div>
        <WButton variant="accent" :disabled="!enabled('messageBox')" @click="invoke('messageBox')">{{ t('nativeUi.show') }}</WButton>
        <p role="status">{{ resultText('messageBox') }}</p>
      </div>
    </WExpander>
    <WExpander settings :header="t('nativeUi.notification')" :description="availability('notification')">
      <div class="test-form">
        <label><span>{{ t('nativeUi.text') }}</span><textarea v-model="notice" rows="3" /></label>
        <WButton variant="accent" :disabled="!enabled('notification')" @click="invoke('notification')">{{ t('nativeUi.show') }}</WButton>
        <p role="status">{{ resultText('notification') }}</p>
      </div>
    </WExpander>
    <WExpander settings :header="t('nativeUi.announcement')" :description="availability('announcement.show')">
      <div class="test-form">
        <label v-if="ownedAnnouncements"><span>{{ t('nativeUi.announcementId') }}</span><WTextBox v-model="announcementId" /></label>
        <label><span>{{ t('nativeUi.text') }}</span><textarea v-model="announcement" rows="3" /></label>
        <p>{{ t('nativeUi.autoExpire') }}</p>
        <div class="actions">
          <WButton variant="accent" :disabled="!enabled('announcement.show')" @click="invoke('announcement.show')">{{ t('nativeUi.show') }}</WButton>
          <WButton v-if="ownedAnnouncements" :disabled="!enabled('announcement.close')" @click="invoke('announcement.close')">{{ t('nativeUi.close') }}</WButton>
        </div>
        <p role="status">{{ resultText('announcement.show') }}</p><p role="status">{{ resultText('announcement.close') }}</p>
      </div>
    </WExpander>
    <WExpander settings :header="t('nativeUi.chat')" :description="availability('chat')">
      <div class="test-form">
        <label><span>{{ t('nativeUi.style') }}</span><WComboBox v-model="style" :options="[{ value: 'normal', label: t('nativeUi.normal') }, { value: 'system', label: t('nativeUi.system') }]" /></label>
        <label><span>{{ t('nativeUi.chatCaption') }}</span><WTextBox v-model="chatCaption" /></label>
        <div class="repeated-fields">
          <div v-for="(run, index) in runs" :key="index" class="run-fields">
            <WTextBox v-model="run.text" :aria-label="t('nativeUi.text')" :placeholder="t('nativeUi.text')" />
            <NativeChatColorPicker v-model="run.color" />
            <WButton :disabled="runs.length === 1" @click="runs.splice(index, 1)">{{ t('nativeUi.remove') }}</WButton>
          </div>
          <WButton v-if="richChat" @click="runs.push({ text: '', color: '#FFFFFF' })">{{ t('nativeUi.addRun') }}</WButton>
        </div>
        <p v-if="capability('chat')?.supported && !richChat">{{ t('nativeUi.uniformColor') }}</p>
        <WButton variant="accent" :disabled="!enabled('chat')" @click="invoke('chat')">{{ t('nativeUi.send') }}</WButton>
        <p role="status">{{ resultText('chat') }}</p>
      </div>
    </WExpander>
  </div>
</template>

<style scoped>
.native-tests { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
.client-actions { display: flex; align-items: center; gap: 8px; min-width: 0; }
.client-actions .w-flyout-anchor { min-width: 160px; max-width: 320px; }
.test-form { display: grid; gap: 16px; padding: 20px 0; min-width: 0; }
.test-form > label { display: grid; grid-template-columns: minmax(80px, 128px) minmax(0, 1fr); gap: 16px; align-items: center; font-size: 14px; }
.test-form > .w-btn { justify-self: start; }
.test-form p { margin: 0; color: var(--text-secondary); font-size: 12px; overflow-wrap: anywhere; }
.test-form p:empty { display: none; }
.test-error { color: var(--system-critical); font-size: 13px; margin: 8px 0; }
.repeated-fields { display: grid; gap: 8px; min-width: 0; }
.repeated-fields > .w-btn { justify-self: start; }
.button-fields, .run-fields { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) auto; gap: 8px; align-items: center; }
.run-fields { grid-template-columns: minmax(0, 1fr) auto; }
.run-fields > .chat-color-picker { grid-column: 1 / -1; grid-row: 2; }
.actions { display: flex; gap: 8px; flex-wrap: wrap; }
textarea { width: 100%; min-width: 0; box-sizing: border-box; resize: vertical; padding: 8px 12px; border: 1px solid var(--ctrl-stroke-default); border-radius: var(--radius-control); background: var(--ctrl-fill-default); color: var(--text-primary); font: inherit; }
textarea:focus-visible { outline-color: var(--accent); }
@media (max-width: 640px) {
  .client-actions { flex-wrap: wrap; }
  .test-form > label { grid-template-columns: minmax(0, 1fr); gap: 8px; }
  .button-fields, .run-fields { grid-template-columns: minmax(0, 1fr); }
  .button-fields .w-btn, .run-fields .w-btn { justify-self: start; }
}
</style>

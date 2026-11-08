<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, type LaunchCleanupFailure } from '../bridge'
import { state } from '../state'
import { clientFailureCode } from '../launchFailure'
import WInfoBar from '../controls/WInfoBar.vue'
import WDialog from '../controls/WDialog.vue'
import WButton from '../controls/WButton.vue'

const props = defineProps<{ failure: LaunchCleanupFailure; message: string }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
const open = ref(false), busy = ref(false), lifecycle = ref(''), detailError = ref('')
let revision = 0
const instance = computed(() => state.instances.find(item => item.id === props.failure.instanceId))
const details = computed(() => JSON.stringify({ ...props.failure, instanceName: instance.value?.name,
  build: instance.value?.build, recentLauncherEvents: lifecycle.value }, null, 2))
const stage = computed(() => props.failure.phase === 'bootstrap-dispose' ? 'bootstrap' : props.failure.phase === 'runtime-restore' ? 'restore'
  : props.failure.phase === 'observe-exit' ? 'observe' : 'other')
const clientCode = computed(() => clientFailureCode(props.failure))
watch(() => props.failure, () => { revision++; open.value = false; busy.value = false; lifecycle.value = ''; detailError.value = '' })
async function showDetails() {
  open.value = true; busy.value = true; detailError.value = ''
  const attempt = ++revision
  try {
    const result = await call('instances.launchDiagnostics', { id: props.failure.instanceId })
    if (attempt === revision) lifecycle.value = result.lifecycle
  } catch { if (attempt === revision) detailError.value = t('launchCleanup.readFailed') }
  finally { if (attempt === revision) busy.value = false }
}
async function copy() {
  try { await navigator.clipboard.writeText(details.value); detailError.value = '' }
  catch { detailError.value = t('serverFailure.copyFailed') }
}
async function openLog() {
  try { await call('launcherUpdates.openLog'); detailError.value = '' }
  catch { detailError.value = t('launchCleanup.readFailed') }
}
</script>

<template>
  <WInfoBar severity="error" @close="emit('close')">{{ message }}
    <template #action><WButton variant="subtle" @click="showDetails">{{ t('serverFailure.details') }}</WButton></template>
  </WInfoBar>
  <WDialog :open="open" :title="t('launchCleanup.title')" :close-text="t('titlebar.close')" @close="open = false">
    <div class="launch-failure-content">
      <p v-if="clientCode">{{ t('launchCleanup.clientExited', { code: clientCode }) }}</p>
      <p>{{ t(`launchCleanup.stages.${stage}`) }}</p>
      <p>{{ t('launchCleanup.share') }}</p>
      <p v-if="busy" role="status">{{ t('launchCleanup.loading') }}</p>
      <pre class="launch-failure-details">{{ details }}</pre>
      <WInfoBar v-if="detailError" severity="warning" :closable="false">{{ detailError }}</WInfoBar>
      <div class="launch-failure-actions"><WButton @click="copy">{{ t('launcherUpdates.copyDetails') }}</WButton><WButton @click="openLog">{{ t('launcherUpdates.openLog') }}</WButton></div>
    </div>
  </WDialog>
</template>

<style scoped>
.launch-failure-content { display: grid; gap: 12px; }
.launch-failure-content p { margin: 0; line-height: 1.6; }
.launch-failure-details { margin: 0; padding: 12px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-control); background: var(--fill-subtle); white-space: pre-wrap; overflow-wrap: anywhere; max-height: 320px; overflow-y: auto; user-select: text; font-size: 12px; }
.launch-failure-actions { display: flex; flex-wrap: wrap; gap: 8px; }
</style>

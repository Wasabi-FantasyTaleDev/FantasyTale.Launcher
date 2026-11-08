<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call } from '../bridge'
import type { InstanceRequestError } from '../instanceFailures'
import WInfoBar from '../controls/WInfoBar.vue'
import WDialog from '../controls/WDialog.vue'
import WButton from '../controls/WButton.vue'

const props = defineProps<{ error: InstanceRequestError }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
const open = ref(false), lifecycle = ref(''), detailError = ref('')
watch(() => props.error, () => { open.value = false; lifecycle.value = ''; detailError.value = '' })
const details = computed(() => JSON.stringify({ ...props.error.details, recentLauncherEvents: lifecycle.value }, null, 2))
async function showDetails() {
  open.value = true; detailError.value = ''; lifecycle.value = ''
  const error = props.error
  if (!error.details.instanceId) return
  try {
    const result = await call('instances.launchDiagnostics', { id: error.details.instanceId })
    if (props.error === error) lifecycle.value = result.lifecycle
  } catch { if (props.error === error) detailError.value = t('launchCleanup.readFailed') }
}
async function copy() {
  try { await navigator.clipboard.writeText(details.value); detailError.value = '' }
  catch { detailError.value = t('serverFailure.copyFailed') }
}
</script>

<template>
  <WInfoBar severity="error" @close="emit('close')">{{ error.message }}
    <template #action><WButton variant="subtle" @click="showDetails">{{ t('serverFailure.details') }}</WButton></template>
  </WInfoBar>
  <WDialog :open="open" :title="t('instanceBusy.title')" :close-text="t('titlebar.close')" @close="open = false">
    <div class="instance-failure-content">
      <p>{{ t('instanceBusy.explanation') }}</p>
      <pre>{{ details }}</pre>
      <WInfoBar v-if="detailError" severity="warning" :closable="false">{{ detailError }}</WInfoBar>
      <WButton @click="copy">{{ t('launcherUpdates.copyDetails') }}</WButton>
    </div>
  </WDialog>
</template>

<style scoped>
.instance-failure-content { display: grid; gap: 12px; }
.instance-failure-content p { margin: 0; line-height: 1.6; }
.instance-failure-content pre { margin: 0; padding: 12px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-control); background: var(--fill-subtle); white-space: pre-wrap; overflow-wrap: anywhere; max-height: 320px; overflow-y: auto; user-select: text; font-size: 12px; }
</style>

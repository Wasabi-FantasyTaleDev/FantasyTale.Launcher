<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, type HostingSession } from '../bridge'
import WDialog from '../controls/WDialog.vue'
import WProgressRing from '../controls/WProgressRing.vue'

const props = defineProps<{ open: boolean; session?: HostingSession; error?: string }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
const text = ref(''), loading = ref(false)
const logView = ref<HTMLElement>()
let revision = 0, disposed = false, timer: ReturnType<typeof setTimeout> | undefined
const current = (expected: number) => !disposed && props.open && expected === revision
async function refresh(expected: number, session: HostingSession) {
  try {
    const lines = await call('hosting.log', { id: session.profileId, runId: session.runId })
    if (current(expected)) text.value = lines.join('\n')
  } catch (error) { if (current(expected)) text.value = (error as Error).message }
  finally { if (current(expected)) loading.value = false }
  await nextTick()
  if (!current(expected)) return
  if (logView.value) logView.value.scrollTop = logView.value.scrollHeight
  timer = setTimeout(() => void refresh(expected, session), 1000)
}
function close() {
  ++revision; clearTimeout(timer)
  emit('close')
}
watch([() => props.open, () => props.session?.profileId, () => props.session?.runId], ([open]) => {
  const expected = ++revision
  clearTimeout(timer)
  text.value = ''; loading.value = false
  if (!open || !props.session) return
  loading.value = true
  void refresh(expected, props.session)
}, { immediate: true, flush: 'sync' })
onBeforeUnmount(() => { disposed = true; ++revision; clearTimeout(timer) })
</script>

<template>
  <WDialog :open="open" :title="t('serverFailure.details')" :close-text="t('titlebar.close')" @close="close">
    <WProgressRing v-if="loading" :size="24" />
    <p v-if="error">{{ error }}</p>
    <pre v-if="text" ref="logView" class="hosting-details">{{ text }}</pre>
    <p v-else-if="!loading && session">{{ t('hosting.noLogs') }}</p>
    <p v-if="session" class="hosting-log-path">{{ session.logPath }}</p>
  </WDialog>
</template>

<style scoped>
.hosting-details { max-height: 340px; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; user-select: text; font-size: 12px; line-height: 1.6; }
.hosting-log-path { overflow-wrap: anywhere; user-select: text; color: var(--text-secondary); font-size: 12px; }
</style>

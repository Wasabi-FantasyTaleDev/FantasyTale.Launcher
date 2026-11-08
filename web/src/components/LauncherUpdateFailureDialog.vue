<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, inHost } from '../bridge'
import { state } from '../state'
import { updateFailureKey } from '../downloads/updateFailure'
import WDialog from '../controls/WDialog.vue'

const { t } = useI18n()
const open = ref(false), busy = ref(false), version = ref<string | null>(null), url = ref<string | null>(null)
let stopped = false, lastFailure = '', checkFailureMessage = '', timer: ReturnType<typeof setTimeout> | undefined
async function refresh() {
  try {
    const update = await call('launcherUpdates.state')
    if (stopped) return
    if (update.status === 'failed') {
      const key = `${update.manifestSha256 ?? ''}:${update.failure ?? ''}:${update.failureReason ?? ''}:${update.httpStatus ?? ''}`
      if (key !== lastFailure) {
        lastFailure = key
        // Discovery failures have no verified newer selection; they cannot imply an update failed.
        if (!update.version || !/^[a-f0-9]{64}$/i.test(update.manifestSha256 ?? '')) {
          open.value = false
          checkFailureMessage = t(updateFailureKey(update))
          state.error = checkFailureMessage
          return
        }
        const manual = await call('launcherUpdates.manualDownload')
        if (stopped || manual.version !== update.version) return
        version.value = update.version; url.value = manual.url
        open.value = true
      }
    } else if (update.status !== 'checking') {
      lastFailure = ''; open.value = false
      if (checkFailureMessage && state.error === checkFailureMessage) state.error = ''
      checkFailureMessage = ''
    }
  } catch (error) { if (!stopped) state.error = (error as Error).message }
  finally { if (!stopped) timer = setTimeout(refresh, 2000) }
}
async function openDownload() {
  if (!url.value || busy.value) return
  busy.value = true
  try { await call('shell.openExternal', { url: url.value }); if (!stopped) open.value = false }
  catch (error) { if (!stopped) state.error = (error as Error).message }
  finally { busy.value = false }
}
onMounted(() => { if (inHost) void refresh() })
onBeforeUnmount(() => { stopped = true; if (timer) clearTimeout(timer) })
</script>

<template>
  <WDialog :open="open" :title="t('launcherUpdates.manualTitle')" :primary-text="t('launcherUpdates.openDownload')"
    :secondary-text="t('launcherUpdates.later')" :primary-disabled="busy || !url" @primary="openDownload" @secondary="open = false" @close="open = false">
    <p>{{ t('launcherUpdates.manualBody', { version }) }}</p>
  </WDialog>
</template>

<style scoped>
p { margin: 0; line-height: 1.65; }
</style>

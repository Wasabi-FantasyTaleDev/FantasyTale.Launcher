<script setup lang="ts">
import { computed, ref, watch, onScopeDispose } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, inHost } from '../bridge'
import { downloadTasks, downloadAction, trackDownload } from '../downloads/live'
import type { StorePackage, StoreSource } from '../store/types'
import DownloadTaskProgress from './DownloadTaskProgress.vue'
import WDialog from '../controls/WDialog.vue'
import StorePluginInstallList from './StorePluginInstallList.vue'

const props = defineProps<{ open: boolean; item: StorePackage; source: StoreSource }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
const busy = ref(false), error = ref(''), taskId = ref('')
let revision = 0
onScopeDispose(() => { revision++ })
const task = computed(() => downloadTasks.value.find(task => task.id === taskId.value))
const finished = computed(() => task.value?.stage === 'completed')
const retryable = computed(() => task.value && ['failed', 'canceled', 'paused'].includes(task.value.stage))
watch(() => props.open, () => { revision++; busy.value = false; error.value = ''; taskId.value = '' })
function close() { revision++; busy.value = false; emit('close') }
async function install() {
  if (finished.value) { close(); return }
  if (busy.value || !inHost || task.value && !retryable.value) return
  const current = revision, item = props.item, source = props.source, original = task.value
  busy.value = true; error.value = ''
  try {
    if (original) await downloadAction(original, original.stage === 'paused' ? 'resume' : 'retry')
    else {
      const task = await call('store.installServer', { sourceId: source.id, packageId: item.id, version: item.version, manifestSha256: item.manifestSha256 })
      trackDownload(task); if (current === revision) taskId.value = task.id
    }
  } catch (e) { if (current === revision) error.value = (e as Error).message }
  finally { if (current === revision) busy.value = false }
}
</script>
<template>
  <WDialog :open="open" :title="t('serverPackages.install')" :primary-text="t(finished ? 'updates.close' : retryable ? 'updates.retry' : busy ? 'install.starting' : 'install.install')" :close-text="t('dialog.cancel')"
    :primary-disabled="busy || !inHost || !!taskId && !finished && !retryable" :primary-busy="busy" @primary="install" @close="close">
    <div class="form-stack">
      <StorePluginInstallList :items="[{ item, source }]"><DownloadTaskProgress :task-id="taskId" :pending="busy" /></StorePluginInstallList>
      <p class="package-note">{{ t('serverPackages.note') }}</p>
      <p v-if="error" class="install-error" role="alert">{{ error }}</p>
    </div>
  </WDialog>
</template>
<style scoped>
.package-note { color: var(--text-secondary); margin: 0; line-height: 1.6; }
.install-error { color: var(--status-error); margin: 0; }
</style>

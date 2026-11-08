<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import type { DownloadTask } from '../downloads/model'
import { downloadTasks, downloadAction } from '../downloads/live'
import DownloadTaskProgress from './DownloadTaskProgress.vue'
import PackageSummaryCard from './PackageSummaryCard.vue'
import WButton from '../controls/WButton.vue'
const props = defineProps<{ tasks: DownloadTask[] }>()
const { t } = useI18n()
const busy = ref(''), error = ref('')
const current = computed(() => props.tasks.map(original => downloadTasks.value.find(task => task.id === original.id) ?? original))
async function retry(task: DownloadTask) {
  if (busy.value) return
  busy.value = task.id; error.value = ''
  try { await downloadAction(task, task.stage === 'paused' ? 'resume' : 'retry') }
  catch (cause) { error.value = (cause as Error).message }
  finally { busy.value = '' }
}
</script>
<template>
  <div v-if="tasks.length" class="installation-tasks">
    <PackageSummaryCard v-for="task in current" :key="task.id" :separate-controls="current.length === 1"
      :name="localized(task.target.nameLocalized ?? undefined) || task.target.name"
      :glyph="task.target.kind === 'client' ? 'gamepad' : task.target.kind === 'server' ? 'globe' : task.target.kind === 'launcher' ? 'refresh' : 'puzzle'">
      <template #meta>{{ task.target.toVersion }}</template>
      <DownloadTaskProgress :task="task" />
      <WButton v-if="['failed', 'canceled', 'paused'].includes(task.stage)" :disabled="!!busy" @click="retry(task)">{{ t(task.stage === 'paused' ? 'updates.resume' : 'updates.retry') }}</WButton>
    </PackageSummaryCard>
    <p v-if="error" role="alert">{{ error }}</p>
  </div>
</template>
<style scoped>
.installation-tasks { display: grid; gap: 12px; }
.installation-tasks :deep(.w-button) { margin-top: 10px; }
p { margin: 0; font-size: 12px; overflow-wrap: anywhere; }
</style>

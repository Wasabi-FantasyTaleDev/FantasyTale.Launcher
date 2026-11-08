<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import { requiredUpdatePrompt as prompt, finishRequiredUpdates } from '../plugins/launchUpdates'
import { transferMetrics, formatBytes, type DownloadTask } from '../downloads/model'
import WDialog from '../controls/WDialog.vue'
import WProgressBar from '../controls/WProgressBar.vue'
import PackageSummaryCard from './PackageSummaryCard.vue'
const { t, locale } = useI18n()
function progress(task: DownloadTask) {
  const processing = ['checking', 'verifying', 'installing'].includes(task.stage)
  const value = processing ? task.stageProgress : transferMetrics(task).percent
  return { value: value ?? 0, indeterminate: task.stage === 'queued' || value == null && (processing || task.stage === 'downloading'),
    text: value == null ? '' : `${Math.floor(value)}%` }
}
</script>
<template>
  <WDialog :open="prompt.open" :title="t('requiredUpdates.title')" :primary-text="t('dialog.cancel')"
    :secondary-text="t('pluginUpdates.launchNow')" @primary="finishRequiredUpdates(false)" @secondary="finishRequiredUpdates(true)" @close="finishRequiredUpdates(false)">
    <p class="update-warning">{{ prompt.items.length === 1 ? t('requiredUpdates.single', { name: localized(prompt.items[0]!.name) }) : t('requiredUpdates.multiple') }}</p>
    <ul class="required-plugin-list">
      <PackageSummaryCard v-for="item in prompt.items" :key="item.id" as="li" glyph="puzzle" :separate-controls="prompt.items.length === 1" :name="localized(item.name)">
        <template #meta><span>{{ item.installedVersion }} → {{ item.version }}</span></template>
        <div v-if="item.task" class="plugin-transfer">
          <WProgressBar :value="progress(item.task).value" :indeterminate="progress(item.task).indeterminate" :aria-label="localized(item.name)" />
          <div class="transfer-caption" role="status"><span>{{ t(`updates.${item.task.stage}`) }}</span><span>{{ progress(item.task).text }}</span></div>
          <div v-if="item.task.stage === 'downloading'" class="transfer-caption"><span>{{ formatBytes(transferMetrics(item.task).received, locale) }} / {{ formatBytes(transferMetrics(item.task).total, locale) }}</span><span v-if="item.task.bytesPerSecond > 0">{{ formatBytes(item.task.bytesPerSecond, locale) }}/s</span></div>
        </div>
      </PackageSummaryCard>
    </ul>
  </WDialog>
</template>
<style scoped>
.update-warning { margin: 0 0 16px; line-height: 1.65; }
.required-plugin-list { margin: 0; padding: 0; list-style: none; display: grid; gap: 8px; max-height: min(380px, 48vh); overflow-y: auto; overscroll-behavior: contain; }
.plugin-transfer { display: grid; gap: 8px; }
.transfer-caption { display: flex; justify-content: space-between; gap: 12px; color: var(--text-secondary); font-size: 12px; font-variant-numeric: tabular-nums; }
</style>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import { computed } from 'vue'
import { enableLaunchWarnings, finishLaunchWarnings, launchWarningPrompt as prompt } from '../plugins/launchWarnings'
import { downloadTasks } from '../downloads/live'
import PackageSummaryCard from './PackageSummaryCard.vue'
import DownloadTaskProgress from './DownloadTaskProgress.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import WDialog from '../controls/WDialog.vue'
const { t } = useI18n()
const retryable = computed(() => prompt.tasks.some(original => downloadTasks.value.some(task => task.id === original.id && ['failed', 'canceled', 'paused'].includes(task.stage))))
</script>
<template>
  <WDialog :open="prompt.open" :title="t('pluginWarnings.title')" :primary-text="t(retryable ? 'updates.retry' : 'pluginWarnings.enableContinue')" primary-first
    :close-text="t('dialog.cancel')" :primary-busy="prompt.busy" :primary-disabled="prompt.tasks.length > 0 && !retryable && !prompt.error"
    :secondary-text="t('pluginWarnings.continue')" :secondary-disabled="prompt.busy || prompt.tasks.length > 0" @primary="enableLaunchWarnings"
    @secondary="finishLaunchWarnings(true)" @close="finishLaunchWarnings(false)">
    <div class="component-warnings">
      <PackageSummaryCard v-for="item in prompt.tasks.length ? [] : prompt.items" :key="item.key" glyph="puzzle" :name="localized(item.name)">
        <template #meta>{{ item.version }}</template>
      </PackageSummaryCard>
      <PackageSummaryCard v-for="task in prompt.tasks" :key="task.id" glyph="puzzle" :separate-controls="prompt.tasks.length === 1" :name="localized(task.target.nameLocalized ?? undefined) || task.target.name"><template #meta>{{ task.target.toVersion }}</template><DownloadTaskProgress :task="downloadTasks.find(item => item.id === task.id) ?? task" /></PackageSummaryCard>
      <p>{{ t('pluginWarnings.body') }}</p>
      <WInfoBar v-if="prompt.error" severity="error" :closable="false">{{ prompt.error }}</WInfoBar>
    </div>
  </WDialog>
</template>
<style scoped>
.component-warnings { display: grid; gap: 12px; }
.component-warnings p { margin: 0; line-height: 1.65; }
</style>

<script setup lang="ts">
import { computed } from 'vue'
import { downloadTasks } from '../downloads/live'
import DownloadTaskProgress from './DownloadTaskProgress.vue'
import PackageSummaryCard from './PackageSummaryCard.vue'
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import { acceptRecommendations, dismissRecommendations, recommendationPrompt as prompt } from '../plugins/recommendations'
import WDialog from '../controls/WDialog.vue'
import WCheckBox from '../controls/WCheckBox.vue'
const { t } = useI18n()
const retryable = computed(() => prompt.tasks.some(original => downloadTasks.value.some(task => task.id === original.id && ['failed', 'canceled', 'paused'].includes(task.stage))))
</script>
<template>
  <WDialog :open="prompt.open" :title="t('recommendations.title')" :primary-text="t(prompt.busy ? 'live.working' : prompt.tasks.length ? 'updates.retry' : 'recommendations.accept')"
    :close-text="t('dialog.cancel')" :primary-disabled="prompt.busy || prompt.tasks.length > 0 && !retryable && !prompt.error" :primary-busy="prompt.busy" @primary="acceptRecommendations" @close="dismissRecommendations">
    <div class="recommendation-content">
      <p>{{ t('recommendations.body') }}</p>
      <span class="client-label">{{ prompt.name }}</span>
      <WCheckBox v-for="item in prompt.tasks.length ? [] : prompt.items" :key="item.id" class="recommendation-card"
        :model-value="item.required || prompt.selected.includes(item.id)" :disabled="prompt.busy || item.required"
        @update:model-value="checked => { prompt.selected = checked ? [...new Set([...prompt.selected, item.id])] : prompt.selected.filter(id => id !== item.id) }">
        <span><strong>{{ localized(item.name) }}</strong><small v-if="item.description">{{ localized(item.description) }}</small></span>
      </WCheckBox>
      <PackageSummaryCard v-for="task in prompt.tasks" :key="task.id" glyph="puzzle" :separate-controls="prompt.tasks.length === 1" :name="localized(task.target.nameLocalized ?? undefined) || task.target.name">
        <template #meta>{{ task.target.toVersion }}</template><DownloadTaskProgress :task="task" />
      </PackageSummaryCard>
      <DownloadTaskProgress v-if="prompt.busy && !prompt.tasks.length" pending />
      <p v-if="prompt.error" class="recommendation-error" role="alert">{{ prompt.error }}</p>
    </div>
  </WDialog>
</template>
<style scoped>
.recommendation-content { display: grid; gap: 12px; }
.recommendation-content p { margin: 0; }
.client-label { color: var(--text-secondary); font-size: 12px; }
.recommendation-card { display: flex; align-items: center; gap: 14px; padding: 16px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-control); background: var(--card-bg-default); cursor: pointer; }
.recommendation-card span { display: grid; gap: 5px; }
.recommendation-card strong { font-weight: 600; }
.recommendation-card small { color: var(--text-secondary); line-height: 1.5; }
.recommendation-error { color: var(--text-critical); }
</style>

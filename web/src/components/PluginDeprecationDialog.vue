<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import WDialog from '../controls/WDialog.vue'
import PackageSummaryCard from './PackageSummaryCard.vue'
import { localized } from '../i18n'
import type { PluginDeprecationNotice } from '../bridge'

defineProps<{ notices: PluginDeprecationNotice[] }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
</script>

<template>
  <WDialog :open="notices.length > 0" :title="t('pluginDeprecation.title')" :close-text="t('pluginRecovery.close')" @close="emit('close')">
    <div class="retired-content">
      <div v-for="notice in notices" :key="`${notice.game}:${notice.pluginId}`" class="retired-item">
        <PackageSummaryCard glyph="puzzle" :name="localized(notice.name) || notice.pluginId">
          <template #meta>{{ notice.versions.join(', ') }}</template>
        </PackageSummaryCard>
        <p v-if="localized(notice.reason)">{{ localized(notice.reason) }}</p>
      </div>
      <p>{{ t('pluginDeprecation.body') }}</p>
    </div>
  </WDialog>
</template>

<style scoped>
.retired-content, .retired-item { display: grid; gap: 16px; }
.retired-content p { margin: 0; line-height: 1.65; }
</style>

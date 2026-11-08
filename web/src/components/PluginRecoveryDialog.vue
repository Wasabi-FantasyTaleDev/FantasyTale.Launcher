<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import WDialog from '../controls/WDialog.vue'
import PackageSummaryCard from './PackageSummaryCard.vue'
import type { PluginRecoveryNotice } from '../bridge'

const props = defineProps<{ notice: PluginRecoveryNotice | null }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
const title = computed(() => props.notice?.pluginId ?? t('pluginRecovery.unknown'))
</script>

<template>
  <WDialog :open="!!notice" :title="t('pluginRecovery.title')" :close-text="t('pluginRecovery.close')" @close="emit('close')">
    <div v-if="notice" class="recovery-content">
      <PackageSummaryCard glyph="puzzle" :name="title">
        <template #meta>{{ notice.version }}</template>
      </PackageSummaryCard>
      <p>{{ t('pluginRecovery.body') }}</p>
    </div>
  </WDialog>
</template>

<style scoped>
.recovery-content { display: grid; gap: 16px; }
.recovery-content p { margin: 0; line-height: 1.65; }
</style>

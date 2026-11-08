<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import { resourceConflictPrompt as prompt, finishResourceConflicts } from '../plugins/resourceConflicts'
import WDialog from '../controls/WDialog.vue'
import PackageSummaryCard from './PackageSummaryCard.vue'

const { t } = useI18n()
const complete = computed(() => !!prompt.report && prompt.report.conflicts.every(conflict => conflict.parties.some(party => party.pluginId === prompt.selected[conflict.id])))
</script>

<template>
  <WDialog :open="prompt.open" :title="t('resourceConflicts.title')" :primary-text="t('resourceConflicts.retry')"
    :primary-disabled="!complete" primary-first :close-text="t('dialog.cancel')" @primary="finishResourceConflicts(true)" @close="finishResourceConflicts(false)">
    <div class="resource-conflicts">
      <p>{{ t('resourceConflicts.body') }}</p>
      <fieldset v-for="conflict in prompt.report?.conflicts" :key="conflict.id">
        <legend>{{ t('resourceConflicts.choose') }}</legend>
        <div v-for="party in conflict.parties" :key="party.pluginId" class="conflict-party">
          <label>
            <input v-model="prompt.selected[conflict.id]" type="radio" :name="conflict.id" :value="party.pluginId" :aria-label="t('resourceConflicts.keep', { name: localized(party.name.values) })">
            <PackageSummaryCard :name="localized(party.name.values)" glyph="puzzle"><template #meta>{{ party.version }}</template></PackageSummaryCard>
          </label>
          <p v-if="party.value !== null" class="conflict-value">{{ t('resourceConflicts.value', { value: party.value }) }}</p>
        </div>
        <p class="conflict-location">{{ conflict.member }}<br>{{ [conflict.target, conflict.key, conflict.field].filter(Boolean).join(' · ') }}</p>
        <p v-if="conflict.original !== null" class="conflict-value">{{ t('resourceConflicts.original', { value: conflict.original }) }}</p>
      </fieldset>
      <p>{{ t('resourceConflicts.scope') }}</p>
    </div>
  </WDialog>
</template>

<style scoped>
.resource-conflicts { display: grid; gap: 16px; }
.resource-conflicts p { margin: 0; line-height: 1.6; overflow-wrap: anywhere; }
.resource-conflicts fieldset { min-width: 0; margin: 0; padding: 0; border: 0; display: grid; gap: 10px; }
.resource-conflicts legend { margin-bottom: 10px; font-weight: 600; }
.conflict-party label { display: grid; grid-template-columns: 20px minmax(0, 1fr); gap: 12px; align-items: center; cursor: pointer; }
.conflict-party input { width: 20px; height: 20px; margin: 0; accent-color: var(--accent-default); }
.conflict-party .conflict-value { margin: 6px 0 0 32px; }
.conflict-location, .conflict-value { color: var(--text-secondary); font-size: 12px; }
</style>

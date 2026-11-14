<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { localized } from '../i18n'
import { resourceConflictPrompt as prompt, finishResourceConflicts } from '../plugins/resourceConflicts'
import WDialog from '../controls/WDialog.vue'
import FluentIcon from '../controls/FluentIcon.vue'

const { t } = useI18n()
const complete = computed(() => !!prompt.report && prompt.report.conflicts.every(conflict => conflict.parties.some(party => party.pluginId === prompt.selected[conflict.id])))
const conflictTargetDetails = (conflict: NonNullable<typeof prompt.report>['conflicts'][number]) =>
  [conflict.target, conflict.key, conflict.field].filter(Boolean).join(' · ')
</script>

<template>
  <WDialog wide :open="prompt.open" :title="t('resourceConflicts.title')" :primary-text="t('resourceConflicts.retry')"
    :primary-disabled="!complete" primary-first :close-text="t('dialog.cancel')" @primary="finishResourceConflicts(true)" @close="finishResourceConflicts(false)">
    <div class="resource-conflicts">
      <p class="conflict-lead">{{ t('resourceConflicts.body') }}</p>
      <section v-for="conflict in prompt.report?.conflicts" :key="conflict.id" class="conflict-group"
        role="radiogroup" :aria-labelledby="`conflict-choose-${conflict.id}`">
        <div class="conflict-group-head">
          <div class="conflict-target">
            <span class="conflict-target-icon"><FluentIcon glyph="openFile" :size="15" /></span>
            <div class="conflict-target-text">
              <strong class="conflict-member">{{ conflict.member }}</strong>
              <span v-if="conflictTargetDetails(conflict)" class="conflict-detail">{{ conflictTargetDetails(conflict) }}</span>
            </div>
            <span v-if="conflict.original !== null" class="conflict-original">{{ t('resourceConflicts.original', { value: conflict.original }) }}</span>
          </div>
          <span :id="`conflict-choose-${conflict.id}`" class="conflict-choose">{{ t('resourceConflicts.choose') }}</span>
        </div>
        <div class="conflict-list">
          <label v-for="party in conflict.parties" :key="party.pluginId" class="conflict-card"
            :class="{ selected: prompt.selected[conflict.id] === party.pluginId }">
            <span class="conflict-symbol"><FluentIcon glyph="puzzle" :size="20" /></span>
            <div class="conflict-info">
              <div class="conflict-heading">
                <strong>{{ localized(party.name.values) }}</strong>
                <span v-if="party.version" class="conflict-version">{{ party.version }}</span>
              </div>
              <p v-if="party.value !== null" class="conflict-value">{{ t('resourceConflicts.value', { value: party.value }) }}</p>
            </div>
            <span class="conflict-radio-slot">
              <input v-model="prompt.selected[conflict.id]" class="conflict-radio-input" type="radio"
                :name="conflict.id" :value="party.pluginId" :aria-label="t('resourceConflicts.keep', { name: localized(party.name.values) })">
              <span class="conflict-radio" aria-hidden="true" />
            </span>
          </label>
        </div>
      </section>
      <p class="conflict-scope">{{ t('resourceConflicts.scope') }}</p>
    </div>
  </WDialog>
</template>

<style scoped>
.resource-conflicts { display: grid; gap: 14px; }
.conflict-lead { margin: 0; color: var(--text-secondary); line-height: 1.5; }
.conflict-group { display: grid; gap: 8px; min-width: 0; }
.conflict-group-head { display: grid; gap: 8px; }
.conflict-target {
  display: flex; align-items: center; gap: 10px; flex-wrap: wrap;
  padding: 10px 12px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-control);
  background: var(--subtle-secondary);
}
.conflict-target-icon { display: grid; place-items: center; color: var(--text-secondary); flex: none; }
.conflict-target-text { display: grid; gap: 2px; min-width: 0; flex: 1; }
.conflict-member { font-size: 13px; line-height: 18px; font-weight: 600; color: var(--text-primary); overflow-wrap: anywhere; }
.conflict-detail { font-size: 12px; line-height: 16px; color: var(--text-secondary); overflow-wrap: anywhere; }
.conflict-original {
  padding: 2px 8px; border-radius: 999px; font-size: 11px; line-height: 16px;
  color: var(--text-secondary); background: color-mix(in srgb, var(--text-primary) 7%, transparent);
  overflow-wrap: anywhere;
}
.conflict-choose { color: var(--text-secondary); font-size: 12px; font-weight: 600; padding: 0 2px; }
.conflict-list { display: grid; gap: 8px; }
.conflict-card {
  display: grid; grid-template-columns: 40px minmax(0, 1fr) auto; align-items: center; gap: 0 14px;
  padding: 14px 16px; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-overlay);
  background: var(--card-bg-default); cursor: pointer;
  transition:
    border-color var(--duration-fast) var(--ease-fast-out-slow-in),
    background-color var(--duration-fast) var(--ease-fast-out-slow-in);
}
.conflict-card:hover {
  border-color: color-mix(in srgb, var(--accent-default) 36%, var(--card-stroke-default));
  background: var(--subtle-secondary);
}
.conflict-card.selected {
  border-color: var(--accent-default);
  background: color-mix(in srgb, var(--accent-default) 7%, var(--card-bg-default));
}
.conflict-card:has(.conflict-radio-input:focus-visible) {
  outline: 2px solid var(--text-primary);
  outline-offset: 2px;
}
.conflict-symbol {
  display: grid; place-items: center; width: 40px; height: 40px; border-radius: 10px;
  border: 1px solid color-mix(in srgb, var(--accent-default) 16%, transparent);
  color: var(--accent-default); background: color-mix(in srgb, var(--accent-default) 8%, var(--card-bg-default));
  transition:
    color var(--duration-normal) var(--ease-fast-out-slow-in),
    border-color var(--duration-normal) var(--ease-fast-out-slow-in),
    background-color var(--duration-normal) var(--ease-fast-out-slow-in);
}
.conflict-card.selected .conflict-symbol {
  border-color: color-mix(in srgb, var(--accent-default) 32%, transparent);
  background: color-mix(in srgb, var(--accent-default) 14%, var(--card-bg-default));
}
.conflict-info { display: grid; gap: 3px; min-width: 0; }
.conflict-heading { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; min-width: 0; }
.conflict-heading strong { font-size: 14px; line-height: 20px; font-weight: 600; overflow-wrap: anywhere; }
.conflict-version {
  padding: 1px 7px; border-radius: 999px; font-size: 11px; line-height: 16px;
  color: var(--text-secondary); background: color-mix(in srgb, var(--text-primary) 7%, transparent);
}
.conflict-value { margin: 0; color: var(--text-secondary); font-size: 12px; line-height: 18px; overflow-wrap: anywhere; }
.conflict-radio-slot {
  position: relative; display: grid; place-items: center; width: 20px; height: 20px; flex: none; margin-left: 6px;
}
.conflict-radio-input { position: absolute; inset: 0; margin: 0; opacity: 0; cursor: pointer; }
.conflict-radio {
  position: relative; display: grid; place-items: center; width: 20px; height: 20px; border-radius: 50%;
  border: 1.5px solid var(--ctrl-stroke-strong); background: var(--ctrl-fill-default);
  transition:
    background-color var(--duration-fast) linear,
    border-color var(--duration-fast) linear,
    transform var(--duration-faster) var(--ease-fast-out-slow-in);
}
.conflict-radio::after {
  content: ''; width: 8px; height: 8px; border-radius: 50%;
  background: var(--text-on-accent-primary); transform: scale(0);
  transition: transform var(--duration-fast) var(--ease-fast-out-slow-in);
}
.conflict-card:hover .conflict-radio { border-color: var(--text-primary); background: var(--ctrl-fill-secondary); }
.conflict-card.selected .conflict-radio { border-color: var(--accent-default); background: var(--accent-default); }
.conflict-card.selected:hover .conflict-radio { border-color: var(--accent-secondary); background: var(--accent-secondary); }
.conflict-card:active .conflict-radio { transform: scale(0.92); }
.conflict-card.selected .conflict-radio::after { transform: scale(1); }
.conflict-scope { margin: 0; color: var(--text-secondary); font-size: 12px; line-height: 1.5; }
@media (prefers-reduced-motion: reduce) {
  .conflict-card, .conflict-symbol, .conflict-radio, .conflict-radio::after { transition: none; }
}
</style>

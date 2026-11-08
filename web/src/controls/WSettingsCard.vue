<script setup lang="ts">
// SettingsCard (Windows Community Toolkit pattern): icon, header, description and a
// right-side control. `clickable` turns the whole row into a button with a chevron.
import FluentIcon from './FluentIcon.vue'
import type { GlyphName } from '../icons'

withDefaults(
  defineProps<{ icon?: GlyphName; header: string; description?: string; clickable?: boolean }>(),
  { icon: undefined, description: '', clickable: false },
)
const emit = defineEmits<{ click: [] }>()
</script>

<template>
  <button v-if="clickable" type="button" class="w-settings-card interactive" @click="emit('click')">
    <span v-if="icon" class="w-card-icon"><FluentIcon :glyph="icon" :size="20" /></span>
    <span class="w-card-text">
      <span class="w-card-header type-body">{{ header }}</span>
      <span v-if="description" class="w-card-desc type-caption">{{ description }}</span>
    </span>
    <span class="w-card-action"><slot /></span>
    <span class="w-card-chevron"><FluentIcon glyph="chevronRight" :size="12" /></span>
  </button>
  <div v-else class="w-settings-card">
    <span v-if="icon" class="w-card-icon"><FluentIcon :glyph="icon" :size="20" /></span>
    <span class="w-card-text">
      <span class="w-card-header type-body">{{ header }}</span>
      <span v-if="description" class="w-card-desc type-caption">{{ description }}</span>
    </span>
    <span class="w-card-action"><slot /></span>
    <span class="w-card-chevron reserved-chevron" aria-hidden="true"></span>
  </div>
</template>

<style scoped>
.w-settings-card {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  min-height: 68px;
  padding: 16px 16px 16px 24px;
  border-radius: var(--radius-overlay);
  background: var(--card-bg-default);
  border: 1px solid var(--card-stroke-default);
  color: var(--text-primary);
  font-size: 14px;
  text-align: left;
  transition: background-color var(--duration-faster) linear;
}
.w-settings-card.interactive:hover {
  background: var(--ctrl-fill-secondary);
}
.w-settings-card.interactive:active {
  background: var(--ctrl-fill-tertiary);
}
.w-card-icon {
  margin-right: 8px;
  flex: none;
  width: 32px;
  height: 32px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--text-secondary);
}
.w-card-text {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.w-card-header {
  color: var(--text-primary);
}
.w-card-desc {
  color: var(--text-secondary);
}
.w-card-action {
  flex: none;
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.w-card-chevron {
  display: grid; place-items: center; flex: none; width: 32px; height: 32px; margin-left: 4px;
  color: var(--text-secondary);
}
</style>

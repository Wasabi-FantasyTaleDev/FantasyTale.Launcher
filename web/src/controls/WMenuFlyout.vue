<script setup lang="ts">
// MenuFlyout: an anchored list of MenuFlyoutItems, like the right-click menus in WinUI.
import WFlyout from './WFlyout.vue'
import FluentIcon from './FluentIcon.vue'
import type { GlyphName } from '../icons'

export interface MenuFlyoutItem {
  key: string
  label: string
  icon?: GlyphName
  disabled?: boolean
}

defineProps<{ open: boolean; items: MenuFlyoutItem[]; placement?: 'bottom-start' | 'bottom' | 'bottom-end' | 'top-start' }>()
const emit = defineEmits<{ close: []; select: [key: string] }>()

function pick(item: MenuFlyoutItem) {
  if (item.disabled) return
  emit('select', item.key)
  emit('close')
}
</script>

<template>
  <WFlyout :open="open" :placement="placement" :min-width="160" @close="emit('close')">
    <template #anchor><slot name="anchor" /></template>
    <ul class="w-menu" role="menu">
      <li v-for="item in items" :key="item.key">
        <button
          type="button"
          class="w-menu-item"
          role="menuitem"
          :disabled="item.disabled"
          @click="pick(item)">
          <FluentIcon v-if="item.icon" :glyph="item.icon" />
          <span class="w-menu-label">{{ item.label }}</span>
        </button>
      </li>
    </ul>
  </WFlyout>
</template>

<style scoped>
.w-menu {
  list-style: none;
  margin: 0;
  padding: 0;
}
.w-menu-item {
  display: flex;
  align-items: center;
  gap: 12px;
  width: 100%;
  min-height: 32px;
  padding: 4px 12px;
  border-radius: var(--radius-control);
  color: var(--text-primary);
  font-size: 14px;
  text-align: left;
}
.w-menu-item .fluent-icon {
  color: var(--text-secondary);
}
.w-menu-item:hover:not(:disabled) {
  background: var(--subtle-secondary);
}
.w-menu-item:active:not(:disabled) {
  background: var(--subtle-tertiary);
}
.w-menu-item:disabled {
  color: var(--text-disabled);
}
.w-menu-item:disabled .fluent-icon {
  color: var(--text-disabled);
}
</style>

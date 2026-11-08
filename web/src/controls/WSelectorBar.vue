<script setup lang="ts">
// WinUI SelectorBar (CommunityToolkit style): text items with a sliding accent pill
// under the selected one — like the Store app's pivot headers.
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import WInfoBadge from './WInfoBadge.vue'

export interface SelectorBarItem {
  key: string
  label: string
  icon?: string
  disabled?: boolean
  title?: string
  badge?: number
  badgeLabel?: string
}

const props = defineProps<{ modelValue: string; items: SelectorBarItem[]; compactIndicator?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [string] }>()

const bar = ref<HTMLElement>()
const indicator = ref({ left: 0, width: 0, ready: false })

function measure() {
  const el = bar.value?.querySelector<HTMLElement>(`.w-selbar-item[data-key="${CSS.escape(props.modelValue)}"]`)
  if (!el) {
    indicator.value = { left: 0, width: 0, ready: false }
    return
  }
  const width = props.compactIndicator ? 16 : el.offsetWidth - 16
  indicator.value = { left: el.offsetLeft + (el.offsetWidth - width) / 2, width, ready: true }
}

watch(() => props.modelValue, () => nextTick(measure))
watch(() => props.items, () => nextTick(measure), { deep: true })
onMounted(() => {
  measure()
  window.addEventListener('resize', measure)
})
onBeforeUnmount(() => window.removeEventListener('resize', measure))
</script>

<template>
  <div ref="bar" class="w-selbar" role="tablist">
    <button
      v-for="item in items"
      :key="item.key"
      type="button"
      class="w-selbar-item"
      :class="{ selected: item.key === modelValue }"
      :data-key="item.key"
      role="tab"
      :aria-selected="item.key === modelValue"
      :aria-label="item.badge ? `${item.label}, ${item.badgeLabel ?? item.badge}` : undefined"
      :disabled="item.disabled"
      :title="item.title"
      @click="emit('update:modelValue', item.key)">
      {{ item.label }}
      <WInfoBadge v-if="item.badge" :value="item.badge" :label="item.badgeLabel ?? String(item.badge)" />
    </button>
    <span
      class="w-selbar-pill"
      :class="{ ready: indicator.ready }"
      :style="{ transform: `translateX(${indicator.left}px)`, width: `${indicator.width}px` }" />
  </div>
</template>

<style scoped>
.w-selbar {
  position: relative;
  display: inline-flex;
  gap: 4px;
  padding-bottom: 2px;
}
.w-selbar-item {
  display: inline-flex; align-items: center; justify-content: center; gap: 6px;
  padding: 6px 12px;
  border-radius: var(--radius-control);
  color: var(--text-secondary);
  font-size: 14px;
  font-weight: 400;
  transition:
    color var(--duration-faster) linear,
    background-color var(--duration-faster) linear;
}
.w-selbar-item:hover {
  color: var(--text-primary);
  background: var(--subtle-secondary);
}
.w-selbar-item:active {
  background: var(--subtle-tertiary);
}
.w-selbar-item.selected {
  color: var(--text-primary);
  font-weight: 600;
}
.w-selbar-item:disabled { color: var(--text-tertiary); background: transparent; }
.w-selbar-pill {
  position: absolute;
  bottom: 0;
  left: 0;
  height: 3px;
  border-radius: 2px;
  background: var(--accent-default);
  opacity: 0;
  transition:
    transform var(--duration-fast) var(--ease-fast-out-slow-in),
    width var(--duration-fast) var(--ease-fast-out-slow-in),
    opacity var(--duration-faster) linear;
}
.w-selbar-pill.ready {
  opacity: 1;
}
@media (prefers-reduced-motion: reduce) {
  .w-selbar-pill, .w-selbar-item {
  display: inline-flex; align-items: center; justify-content: center; gap: 6px; transition: none; }
}
</style>

<script setup lang="ts">
// WinUI 3 ComboBox: a readonly box with a chevron that opens an item flyout.
import { computed, ref, useAttrs, type StyleValue } from 'vue'
import WFlyout from './WFlyout.vue'
import FluentIcon from './FluentIcon.vue'
defineOptions({ inheritAttrs: false })

export interface ComboBoxOption {
  value: string
  label: string
  caption?: string
  group?: string
  disabled?: boolean
}

const props = withDefaults(
  defineProps<{ modelValue?: string; options: ComboBoxOption[]; placeholder?: string; disabled?: boolean; fitContent?: boolean; subtleSelection?: boolean }>(),
  { modelValue: '', placeholder: '', disabled: false, fitContent: false },
)
const emit = defineEmits<{ 'update:modelValue': [string] }>()
const attrs = useAttrs()
// Layout belongs to the root anchor so a parent's scoped width also sizes the flyout.
function buttonAttrs() { return Object.fromEntries(Object.entries(attrs).filter(([key]) => key !== 'class' && key !== 'style')) }

const open = ref(false)
const current = computed(() => props.options.find((o) => o.value === props.modelValue))
const groups = computed(() => {
  const result: { label: string; options: ComboBoxOption[] }[] = []
  for (const option of props.options) {
    const label = option.group ?? ''
    let group = result.find(item => item.label === label)
    if (!group) { group = { label, options: [] }; result.push(group) }
    group.options.push(option)
  }
  return result
})

function choose(option: ComboBoxOption) {
  if (option.disabled) return
  emit('update:modelValue', option.value)
  open.value = false
}
function close() { open.value = false }
</script>

<template>
  <WFlyout :class="$attrs.class" :style="$attrs.style as StyleValue" :open="open" :match-anchor-width="!fitContent" :min-width="fitContent ? 200 : 0" @close="open = false">
    <template #anchor>
      <button
        v-bind="buttonAttrs()"
        type="button"
        class="w-combobox"
        :class="{ open, empty: !current }"
        :disabled="disabled"
        :title="current?.label ?? placeholder"
        :aria-expanded="open"
        @click="open = !open">
        <span class="w-combobox-text">
          <span class="w-combobox-value">{{ current?.label ?? placeholder }}</span>
          <span v-if="current?.caption" class="w-combobox-caption type-caption">{{ current.caption }}</span>
        </span>
        <FluentIcon glyph="chevronDown" :size="12" class="w-combobox-chevron" />
      </button>
    </template>
    <ul class="w-combobox-list" :class="{ 'subtle-selection': subtleSelection }" role="listbox">
      <li v-for="group in groups" :key="group.label" role="presentation" class="w-combobox-group">
        <div v-if="group.label" class="w-combobox-group-label">{{ group.label }}</div>
        <ul role="group" :aria-label="group.label || undefined">
        <li v-for="option in group.options" :key="option.value" role="presentation">
        <div class="w-combobox-row">
        <button
          type="button"
          class="w-combobox-item"
          :class="{ selected: option.value === modelValue }"
          :disabled="option.disabled"
          role="option"
          :aria-selected="option.value === modelValue"
          @click="choose(option)">
          <span class="w-combobox-value">{{ option.label }}</span>
          <span v-if="option.caption" class="w-combobox-caption type-caption">{{ option.caption }}</span>
        </button>
        <slot name="option-action" :option="option" :close="close" />
        </div>
        </li>
        </ul>
      </li>
    </ul>
  </WFlyout>
</template>

<style scoped>
.w-combobox {
  position: relative;
  display: inline-flex;
  align-items: center;
  width: 100%;
  min-width: 64px;
  min-height: 32px;
  padding: 4px 8px 6px 12px;
  border-radius: var(--radius-control);
  background: var(--ctrl-fill-default);
  color: var(--text-primary);
  font-size: 14px;
  text-align: left;
  transition: background-color var(--duration-faster) linear;
}
.w-combobox::before {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: inherit;
  border: 1px solid transparent;
  background: var(--ctrl-elevation-border) border-box;
  -webkit-mask: linear-gradient(#fff 0 0) padding-box, linear-gradient(#fff 0 0);
  -webkit-mask-composite: xor;
  mask-composite: exclude;
  pointer-events: none;
}
.w-combobox:hover:not(:disabled) {
  background: var(--ctrl-fill-secondary);
}
.w-combobox:active:not(:disabled),
.w-combobox.open {
  background: var(--ctrl-fill-tertiary);
}
.w-combobox:disabled {
  background: var(--ctrl-fill-disabled);
  color: var(--text-disabled);
}
.w-combobox.empty .w-combobox-value {
  color: var(--text-secondary);
}
.w-combobox-text {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.w-combobox-value {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.w-combobox-caption {
  color: var(--text-secondary);
}
.w-combobox-chevron {
  flex: none;
  color: var(--text-secondary);
  margin-left: 8px;
  transition: transform var(--duration-fast) var(--ease-fast-out-slow-in);
}
.w-combobox.open .w-combobox-chevron { transform: rotate(180deg); }
.w-combobox-list {
  list-style: none;
  margin: 0;
  padding: 0;
  max-height: 336px;
  overflow-y: auto;
}
.w-combobox-item {
  position: relative;
  display: flex;
  flex-direction: column;
  width: 100%;
  min-height: 32px;
  justify-content: center;
  padding: 4px 12px 4px 16px;
  border-radius: var(--radius-control);
  color: var(--text-primary);
  font-size: 14px;
  text-align: left;
  transition: background-color var(--duration-faster) linear;
}
.w-combobox-row { display: flex; align-items: center; }
.w-combobox-row > .w-combobox-item { flex: 1; min-width: 0; }
.w-combobox-group > ul { list-style: none; margin: 0; padding: 0; }
.w-combobox-group-label { padding: 9px 12px 5px; color: var(--text-secondary); font-size: 12px; font-weight: 600; }
.w-combobox-group + .w-combobox-group { margin-top: 6px; padding-top: 4px; border-top: 1px solid var(--stroke-divider); }
.w-combobox-item:hover:not(:disabled) {
  background: var(--subtle-secondary);
}
.w-combobox-item:active:not(:disabled) {
  background: var(--subtle-tertiary);
}
.w-combobox-item.selected {
  background: var(--solid-bg-tertiary);
}
.subtle-selection .w-combobox-item.selected { background: var(--subtle-secondary); }
.subtle-selection .w-combobox-group > ul > li + li { margin-top: 3px; }
.w-combobox-item.selected::before {
  content: '';
  position: absolute;
  left: 6px;
  top: 50%;
  width: 3px;
  height: 16px;
  border-radius: 1.5px;
  background: var(--accent);
  transform: translateY(-50%);
  pointer-events: none;
}
.w-combobox-item.selected:active::before {
  transform: translateY(-50%) scaleY(0.625);
}
.w-combobox-item:disabled {
  color: var(--text-disabled);
}
.w-combobox-item:disabled .w-combobox-caption {
  color: var(--text-disabled);
}
.w-combobox-item .w-combobox-value { white-space: normal; overflow-wrap: anywhere; }
.w-combobox-item .w-combobox-caption { overflow-wrap: anywhere; }
</style>

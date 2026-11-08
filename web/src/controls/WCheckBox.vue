<script setup lang="ts">
const props = withDefaults(defineProps<{ modelValue?: boolean; disabled?: boolean }>(), { modelValue: false, disabled: false })
const emit = defineEmits<{ 'update:modelValue': [boolean] }>()
function change(event: Event) { if (!props.disabled) emit('update:modelValue', (event.target as HTMLInputElement).checked) }
</script>

<template>
  <label class="w-checkbox" :class="{ checked: modelValue, disabled }">
    <input type="checkbox" :checked="modelValue" :disabled="disabled" @change="change" />
    <span class="checkbox-box" aria-hidden="true"><svg viewBox="0 0 16 16"><path d="M3 8 6.5 11.5 13 5" /></svg></span>
    <span class="checkbox-label"><slot /></span>
  </label>
</template>

<style scoped>
.w-checkbox { position: relative; display: inline-flex; align-items: center; gap: 10px; min-height: 32px; cursor: pointer; font-size: 14px; color: var(--text-primary); }
.w-checkbox input { position: absolute; width: 20px; height: 20px; margin: 0; opacity: 0; }
.checkbox-box { display: grid; place-items: center; width: 20px; height: 20px; flex: none; border: 1px solid var(--ctrl-stroke-strong); border-radius: 4px; background: var(--ctrl-fill-default); transition: background-color var(--duration-fast) linear, border-color var(--duration-fast) linear, transform var(--duration-faster) var(--ease-fast-out-slow-in); }
.checkbox-box svg { width: 16px; height: 16px; fill: none; stroke: var(--text-on-accent-primary); stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
.checkbox-box path { stroke-dasharray: 16; stroke-dashoffset: 16; opacity: 0; transition: stroke-dashoffset var(--duration-fast) var(--ease-fast-out-slow-in), opacity var(--duration-faster) linear; }
.checked .checkbox-box { background: var(--accent-default); border-color: var(--accent-default); }
.checked .checkbox-box path { stroke-dashoffset: 0; opacity: 1; }
.w-checkbox:hover:not(.disabled) .checkbox-box { background: var(--ctrl-fill-secondary); border-color: var(--text-primary); }
.w-checkbox.checked:hover:not(.disabled) .checkbox-box { background: var(--accent-secondary); border-color: var(--accent-secondary); }
.w-checkbox:active:not(.disabled) .checkbox-box { transform: scale(.92); }
.w-checkbox.checked:active:not(.disabled) .checkbox-box { background: var(--accent-tertiary); }
.w-checkbox input:focus-visible + .checkbox-box { outline: 2px solid var(--text-primary); outline-offset: 3px; }
.disabled { cursor: default; color: var(--text-disabled); }
.disabled .checkbox-box { background: var(--ctrl-fill-disabled); border-color: var(--ctrl-stroke-strong-disabled); }
.disabled.checked .checkbox-box { background: var(--accent-disabled); border-color: transparent; }
.disabled.checked path { stroke: var(--text-on-accent-disabled); }
@media (prefers-reduced-motion: reduce) { .checkbox-box, .checkbox-box path { transition-duration: 1ms; } }
</style>

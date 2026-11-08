<script setup lang="ts">
// WinUI 3 ToggleSwitch: 40x20 pill with a growing knob, accent fill when on.
const props = withDefaults(defineProps<{ modelValue?: boolean; disabled?: boolean }>(), {
  modelValue: false,
  disabled: false,
})
const emit = defineEmits<{ 'update:modelValue': [boolean] }>()

function toggle() {
  if (!props.disabled) emit('update:modelValue', !props.modelValue)
}
</script>

<template>
  <button
    type="button"
    class="w-switch"
    :class="{ on: modelValue, disabled }"
    role="switch"
    :aria-checked="modelValue"
    :disabled="disabled"
    @click="toggle">
    <span class="w-switch-knob" />
  </button>
</template>

<style scoped>
.w-switch {
  position: relative;
  width: 40px;
  height: 20px;
  border-radius: 10px;
  border: 1px solid var(--ctrl-strong-fill);
  background: transparent;
  transition:
    background-color var(--duration-faster) linear,
    border-color var(--duration-faster) linear;
  flex: none;
}
.w-switch:hover:not(:disabled) {
  border-color: var(--ctrl-strong-fill);
  background: var(--subtle-secondary);
}
.w-switch-knob {
  position: absolute;
  top: 50%;
  left: 5px;
  width: 12px;
  height: 12px;
  border-radius: 7px;
  background: var(--ctrl-strong-fill);
  transform: translateY(-50%);
  transition:
    left var(--duration-fast) var(--ease-fast-out-slow-in),
    width var(--duration-fast) var(--ease-fast-out-slow-in),
    height var(--duration-fast) var(--ease-fast-out-slow-in),
    background-color var(--duration-faster) linear;
}
/* Hover grows the knob about its centre; pressed grows it toward the middle. */
.w-switch:hover:not(:disabled) .w-switch-knob {
  left: 4px;
  width: 14px;
  height: 14px;
  background: var(--text-primary);
}
.w-switch:active:not(:disabled) .w-switch-knob {
  left: 5px;
  width: 17px;
  height: 14px;
}
.w-switch.on {
  border-color: var(--accent-default);
  background: var(--accent-default);
}
.w-switch.on:hover:not(:disabled) {
  border-color: var(--accent-secondary);
  background: var(--accent-secondary);
}
.w-switch.on:active:not(:disabled) {
  border-color: var(--accent-tertiary);
  background: var(--accent-tertiary);
}
.w-switch.on .w-switch-knob {
  left: 23px;
  background: var(--text-on-accent-primary);
}
.w-switch.on:hover:not(:disabled) .w-switch-knob {
  left: 21px;
  background: var(--text-on-accent-primary);
}
.w-switch.on:active:not(:disabled) .w-switch-knob {
  left: 18px;
}
.w-switch:disabled {
  border-color: var(--ctrl-strong-fill-disabled);
}
.w-switch:disabled .w-switch-knob {
  background: var(--ctrl-strong-fill-disabled);
}
.w-switch.on:disabled {
  border-color: transparent;
  background: var(--accent-disabled);
}
.w-switch.on:disabled .w-switch-knob {
  background: var(--text-on-accent-disabled);
}
</style>

<script setup lang="ts">
// WinUI 3 TextBox: control fill, elevation border, and the accent underline on focus.
defineOptions({ inheritAttrs: false })
withDefaults(
  defineProps<{ modelValue?: string; placeholder?: string; type?: string; disabled?: boolean }>(),
  { modelValue: '', placeholder: '', type: 'text', disabled: false },
)
const emit = defineEmits<{ 'update:modelValue': [string] }>()

function onInput(e: Event) {
  emit('update:modelValue', (e.target as HTMLInputElement).value)
}
</script>

<template>
  <span class="w-textbox" :class="{ disabled }">
    <input
      v-bind="$attrs"
      :type="type"
      :value="modelValue"
      :placeholder="placeholder"
      :disabled="disabled"
      @input="onInput" />
  </span>
</template>

<style scoped>
.w-textbox {
  position: relative;
  display: inline-flex;
  width: 100%;
  min-width: 64px;
  border-radius: var(--radius-control);
  background: var(--ctrl-fill-default);
}
.w-textbox::before {
  /* Control elevation border: dark only along the bottom edge. */
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
.w-textbox::after {
  /* Accent underline revealed on focus. */
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  height: 2px;
  border-radius: var(--radius-control);
  background: var(--accent);
  opacity: 0;
  transition: opacity var(--duration-faster) linear;
  pointer-events: none;
}
.w-textbox:hover {
  background: var(--ctrl-fill-secondary);
}
.w-textbox:focus-within {
  background: var(--ctrl-fill-input-active);
}
.w-textbox:focus-within::after {
  opacity: 1;
}
.w-textbox.disabled {
  background: var(--ctrl-fill-disabled);
}
input {
  width: 100%;
  min-height: 30px;
  padding: 5px 10px;
  border: 0;
  background: transparent;
  color: var(--text-primary);
  font-size: 14px;
  line-height: 20px;
}
input:focus {
  outline: none;
}
input:disabled {
  color: var(--text-disabled);
}
</style>

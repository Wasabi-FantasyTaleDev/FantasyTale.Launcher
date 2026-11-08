<script setup lang="ts">
// WinUI 3 Button: standard, accent and subtle styles. No press scale — WinUI
// buttons change fill and border, never transform.
withDefaults(
  defineProps<{ variant?: 'standard' | 'accent' | 'subtle'; disabled?: boolean }>(),
  { variant: 'standard', disabled: false },
)
const emit = defineEmits<{ click: [MouseEvent] }>()
</script>

<template>
  <button
    type="button"
    class="w-btn"
    :class="`w-btn--${variant}`"
    :disabled="disabled"
    @click="emit('click', $event)">
    <slot />
  </button>
</template>

<style scoped>
.w-btn {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 32px;
  min-width: 32px;
  padding: 4px 11px 6px;
  border-radius: var(--radius-control);
  font-size: 14px;
  line-height: 20px;
  white-space: nowrap;
  transition: background-color var(--duration-faster) linear;
}
/* 1px border painted with the control elevation gradient so only the bottom edge
   is darker, like the real ControlElevationBorderBrush. */
.w-btn::before {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: inherit;
  border: 1px solid transparent;
  background: var(--w-btn-border) border-box;
  -webkit-mask: linear-gradient(#fff 0 0) padding-box, linear-gradient(#fff 0 0);
  -webkit-mask-composite: xor;
  mask-composite: exclude;
  pointer-events: none;
}
.w-btn:focus-visible {
  outline-offset: 0;
}
.w-btn--standard {
  --w-btn-border: var(--ctrl-elevation-border);
  background-color: var(--ctrl-fill-default);
  color: var(--text-primary);
}
.w-btn--standard:hover:not(:disabled) {
  background-color: var(--ctrl-fill-secondary);
}
.w-btn--standard:active:not(:disabled) {
  --w-btn-border: linear-gradient(var(--ctrl-stroke-default), var(--ctrl-stroke-default));
  background-color: var(--ctrl-fill-tertiary);
  color: var(--text-secondary);
}
.w-btn--accent {
  --w-btn-border: var(--ctrl-elevation-border-accent);
  background-color: var(--accent-default);
  color: var(--text-on-accent-primary);
}
.w-btn--accent:hover:not(:disabled) {
  background-color: var(--accent-secondary);
}
.w-btn--accent:active:not(:disabled) {
  --w-btn-border: none;
  background-color: var(--accent-secondary);
  color: var(--text-on-accent-secondary);
}
.w-btn--subtle {
  --w-btn-border: none;
  background-color: transparent;
  color: var(--text-primary);
}
.w-btn--subtle:hover:not(:disabled) {
  background-color: var(--subtle-secondary);
}
.w-btn--subtle:active:not(:disabled) {
  background-color: var(--subtle-tertiary);
  color: var(--text-secondary);
}
.w-btn:disabled {
  --w-btn-border: linear-gradient(var(--ctrl-stroke-default), var(--ctrl-stroke-default));
  background-color: var(--ctrl-fill-disabled);
  color: var(--text-disabled);
}
.w-btn--subtle:disabled {
  --w-btn-border: none;
  background-color: transparent;
}
</style>

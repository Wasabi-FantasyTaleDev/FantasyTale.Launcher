<script setup lang="ts">
// WinUI 3 ProgressRing: determinate arc or the spinning partial arc.
import { computed } from 'vue'

const props = withDefaults(defineProps<{ value?: number; indeterminate?: boolean; size?: number }>(), {
  value: 0,
  indeterminate: true,
  size: 32,
})

const r = computed(() => props.size / 2 - 1.5)
const circumference = computed(() => 2 * Math.PI * r.value)
const dash = computed(() => `${(Math.min(100, Math.max(0, props.value)) / 100) * circumference.value} ${circumference.value}`)
</script>

<template>
  <svg
    class="w-ring"
    :class="{ indeterminate }"
    :width="size"
    :height="size"
    :viewBox="`0 0 ${size} ${size}`"
    role="progressbar"
    :aria-valuenow="indeterminate ? undefined : Math.floor(Math.min(100, Math.max(0, value)))"
    aria-valuemin="0"
    aria-valuemax="100">
    <circle v-if="!indeterminate" class="w-ring-track" :cx="size / 2" :cy="size / 2" :r="r" />
    <circle
      class="w-ring-arc"
      :cx="size / 2"
      :cy="size / 2"
      :r="r"
      :stroke-dasharray="indeterminate ? `${circumference * 0.6} ${circumference}` : dash"
      :stroke-dashoffset="0" />
  </svg>
</template>

<style scoped>
.w-ring {
  transform: rotate(-90deg);
}
.w-ring circle {
  fill: none;
  stroke-width: 3;
  stroke-linecap: round;
}
.w-ring-track {
  stroke: var(--ctrl-fill-tertiary);
}
.w-ring-arc {
  stroke: var(--accent-default);
  transition: stroke-dasharray var(--duration-normal) var(--ease-fast-out-slow-in);
}
.w-ring.indeterminate {
  animation: w-ring-spin 1.6s cubic-bezier(0.53, 0.21, 0.29, 0.67) infinite;
}
@keyframes w-ring-spin {
  to {
    transform: rotate(630deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .w-ring.indeterminate { animation: none; }
  .w-ring-arc { transition: none; }
}
</style>

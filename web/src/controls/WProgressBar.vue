<script setup lang="ts">
// WinUI 3 ProgressBar: 3px track, accent fill, indeterminate slides a short segment.
withDefaults(defineProps<{ value?: number; indeterminate?: boolean }>(), { value: 0, indeterminate: false })
</script>

<template>
  <div
    class="w-progress"
    :class="{ indeterminate }"
    role="progressbar"
    :aria-valuenow="indeterminate ? undefined : Math.floor(Math.min(100, Math.max(0, value)))"
    aria-valuemin="0"
    aria-valuemax="100">
    <div v-if="!indeterminate" class="w-progress-fill" :style="{ width: `${Math.min(100, Math.max(0, value))}%` }" />
    <div v-else class="w-progress-track" />
  </div>
</template>

<style scoped>
.w-progress {
  position: relative;
  height: 3px;
  border-radius: 2px;
  background: var(--ctrl-fill-tertiary);
  overflow: hidden;
}
.w-progress-fill {
  height: 100%;
  border-radius: 2px;
  background: var(--accent-default);
  transition: width var(--duration-normal) var(--ease-fast-out-slow-in);
}
.w-progress-track {
  position: absolute;
  top: 0;
  bottom: 0;
  width: 40%;
  border-radius: 2px;
  background: var(--accent-default);
  animation: w-progress-slide 1.8s cubic-bezier(0.4, 0, 0.2, 1) infinite;
}
@keyframes w-progress-slide {
  0% {
    left: -40%;
  }
  50% {
    left: 100%;
    width: 40%;
  }
  50.1% {
    left: -40%;
  }
  100% {
    left: -40%;
  }
}
</style>

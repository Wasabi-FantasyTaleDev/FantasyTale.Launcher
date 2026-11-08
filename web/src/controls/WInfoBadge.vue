<script setup lang="ts">
defineProps<{ value: number; label: string }>()
</script>

<template>
  <span v-if="value > 0" class="w-info-badge" role="status" :aria-label="label">{{ value }}</span>
</template>

<style scoped>
.w-info-badge { display: inline-flex; flex: none; align-items: center; justify-content: center; box-sizing: border-box; min-width: 16px; height: 16px; padding: 0 4px; border-radius: 999px; background: #c42b1c; color: #fff; font-size: 11px; line-height: 16px; font-weight: 600; font-variant-numeric: tabular-nums; white-space: nowrap; }
@media (forced-colors: active) { .w-info-badge { background: Highlight; color: HighlightText; forced-color-adjust: none; } }
</style>

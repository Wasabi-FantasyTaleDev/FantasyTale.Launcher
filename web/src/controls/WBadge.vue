<script setup lang="ts">
// Small status badge/tag, aligned with WinUI InfoBadge severities.
withDefaults(
  defineProps<{ tone?: 'neutral' | 'accent' | 'success' | 'attention' | 'critical' }>(),
  { tone: 'neutral' },
)
</script>

<template>
  <span class="w-badge type-caption" :class="`w-badge--${tone}`"><slot /></span>
</template>

<style scoped>
.w-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 1px 8px;
  border-radius: var(--radius-control);
  font-weight: 400;
  white-space: nowrap;
}
.w-badge--neutral {
  background: var(--fill-neutral-bg);
  color: var(--text-secondary);
}
.w-badge--accent {
  background: var(--accent-default);
  color: var(--text-on-accent-primary);
}
.w-badge--success {
  background: var(--fill-success-bg);
  color: var(--fill-success);
}
.w-badge--attention {
  background: var(--fill-caution-bg);
  color: var(--fill-caution);
}
.w-badge--critical {
  background: var(--fill-critical-bg);
  color: var(--fill-critical);
}
</style>

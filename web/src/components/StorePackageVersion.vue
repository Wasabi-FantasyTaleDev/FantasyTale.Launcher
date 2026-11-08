<script setup lang="ts">
import { useI18n } from 'vue-i18n'

defineProps<{ version: string; installedVersion?: string }>()
const { t } = useI18n()
</script>

<template>
  <span class="package-version"><span class="version-numbers"><template v-if="installedVersion"><span class="previous-version">{{ installedVersion }}</span><span aria-hidden="true"> → </span></template>{{ version }}</span><span v-if="installedVersion" class="update-badge">{{ t('store.updateAvailable') }}</span></span>
</template>

<style scoped>
.package-version { display: inline-flex; align-items: center; flex-wrap: wrap; gap: 5px 8px; font-size: 12px; line-height: 18px; color: var(--text-secondary); }
.version-numbers { overflow-wrap: anywhere; font-variant-numeric: tabular-nums; }
.previous-version { color: var(--text-tertiary); }
.update-badge { flex: none; white-space: nowrap; padding: 1px 7px; border-radius: 4px; background: color-mix(in srgb, var(--fill-success) 14%, var(--card-bg-default)); color: var(--fill-success); font-size: 11px; line-height: 17px; font-weight: 400; }
</style>

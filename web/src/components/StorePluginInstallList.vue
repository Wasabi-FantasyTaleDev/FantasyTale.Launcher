<script setup lang="ts">
import { localized } from '../i18n'
import type { StorePackage, StoreSource } from '../store/types'
import PackageSummaryCard from './PackageSummaryCard.vue'
import StorePackageVersion from './StorePackageVersion.vue'

defineProps<{ items: readonly { item: StorePackage; source: StoreSource; installedVersion?: string }[] }>()
</script>

<template>
  <ul class="plugin-install-list">
    <PackageSummaryCard v-for="(entry, index) in items" :key="JSON.stringify([entry.item.game, entry.item.id])" as="li" class="plugin-install-card" :separate-controls="items.length === 1" :name="localized(entry.item.name)" :glyph="entry.item.kind === 'client' ? 'gamepad' : entry.item.kind === 'server' ? 'globe' : 'puzzle'">
      <template #meta><StorePackageVersion :version="entry.item.version" :installed-version="entry.installedVersion" /><span aria-hidden="true">·</span><span>{{ entry.source.name }}</span></template>
      <template v-if="$slots.default" #default><slot :entry="entry" :index="index" /></template>
    </PackageSummaryCard>
  </ul>
</template>

<style scoped>
.plugin-install-list { display: grid; gap: 8px; list-style: none; padding: 0; margin: 0; max-height: min(280px, 38vh); overflow-y: auto; overscroll-behavior: contain; }
</style>

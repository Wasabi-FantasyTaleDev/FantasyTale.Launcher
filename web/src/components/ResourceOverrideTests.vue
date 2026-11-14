<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost, type ResourceOverrideSnapshot, type ResourceOverrideFile } from '../bridge'
import { selectedInstance, gameInstances } from '../state'
import { localized } from '../i18n'
import { formatBytes } from '../downloads/model'
import WButton from '../controls/WButton.vue'
import WComboBox from '../controls/WComboBox.vue'
import WInfoBar from '../controls/WInfoBar.vue'
import WProgressRing from '../controls/WProgressRing.vue'

const { t, locale } = useI18n()
const selected = ref(selectedInstance.value?.id ?? '')
const snapshot = ref<ResourceOverrideSnapshot | null>(null)
const loading = ref(false)
const error = ref('')
const expanded = ref('')
const visible = ref(50)
const visibleMembers = ref(50)
let request = 0
let stopped = false
onBeforeUnmount(() => { stopped = true; request++ })
const clients = computed(() => gameInstances.value.map(client => ({ value: client.id, label: client.name, group: client.build })))
const files = computed(() => snapshot.value?.files ?? [])
function owners(file: ResourceOverrideFile) {
  return [...new Set(file.changes.map(change => `${localized(change.name)} ${change.version}`))].join(' · ')
}
async function refresh() {
  const revision = ++request
  error.value = ''; loading.value = true
  try {
    const value = selected.value && (inHost || !!demoMode?.enabled) ? await call('instances.resourceOverrides', { id: selected.value }) : null
    if (!stopped && revision === request) snapshot.value = value
  } catch (reason) { if (!stopped && revision === request) error.value = (reason as Error).message }
  finally { if (!stopped && revision === request) loading.value = false }
}
watch(selected, () => { snapshot.value = null; expanded.value = ''; visible.value = 50; void refresh() }, { immediate: true })
watch(clients, options => { if (!options.some(option => option.value === selected.value)) selected.value = options[0]?.value ?? '' }, { immediate: true })
function expand(target: string) { expanded.value = expanded.value === target ? '' : target; visibleMembers.value = 50 }
</script>

<template>
  <div class="resource-tests">
    <p class="description">{{ t('resourceOverrides.description') }}</p>
    <div class="toolbar">
      <WComboBox v-model="selected" :options="clients" :aria-label="t('resourceOverrides.client')" />
      <WButton :disabled="!selected || loading || !(inHost || !!demoMode?.enabled)" @click="refresh"><WProgressRing v-if="loading" :size="14" />{{ t('resourceOverrides.refresh') }}</WButton>
    </div>
    <WInfoBar v-if="error" severity="error" :title="error" :closable="false" />
    <p v-if="snapshot" class="description">{{ t('resourceOverrides.summary', { count: files.length, time: new Date(snapshot.preparedAt).toLocaleString(locale) }) }}</p>
    <p v-if="!loading && !files.length" class="empty">{{ t('resourceOverrides.empty') }}</p>
    <TransitionGroup name="override-list" tag="div" class="archive-list">
      <article v-for="file in files.slice(0, visible)" :key="file.target" class="archive">
        <button class="archive-header" :aria-expanded="expanded === file.target" @click="expand(file.target)">
          <span class="identity"><strong>{{ file.target }}</strong><span>{{ owners(file) }}</span></span>
          <span class="status"><span class="badge" :class="file.state">{{ t(`resourceOverrides.${file.state}`) }}</span><span>{{ formatBytes(file.size, locale) }} · {{ t(file.cacheReused ? 'resourceOverrides.reused' : 'resourceOverrides.built') }}</span></span>
          <span aria-hidden="true">{{ expanded === file.target ? '−' : '+' }}</span>
        </button>
        <div v-if="expanded === file.target" class="archive-detail">
          <p class="hash">{{ t('resourceOverrides.before') }} <code>{{ file.originalSha256 ?? '—' }}</code></p>
          <p class="hash">{{ t('resourceOverrides.after') }} <code>{{ file.sha256 }}</code></p>
          <p class="description">{{ t('resourceOverrides.edits', { count: file.changes.length }) }}</p>
          <div v-for="(change, index) in file.changes.slice(0, visibleMembers)" :key="index" class="edit">
            <span>{{ t(`resourceOverrides.${change.action}`) }}</span>
            <code>{{ change.member || file.target }}</code>
            <span>{{ localized(change.name) }} <small>{{ change.pluginId }}@{{ change.version }}</small></span>
          </div>
          <WButton v-if="file.changes.length > visibleMembers" @click="visibleMembers += 50">{{ t('resourceOverrides.more') }}</WButton>
        </div>
      </article>
    </TransitionGroup>
    <WButton v-if="files.length > visible" @click="visible += 50">{{ t('resourceOverrides.more') }}</WButton>
  </div>
</template>

<style scoped>
.resource-tests { display: grid; gap: 14px; }.toolbar { display: flex; gap: 10px; align-items: center; }.toolbar > :first-child { flex: 1; max-width: 440px; }.toolbar :deep(button) { display: inline-flex; align-items: center; gap: 8px; }
.description,.empty { margin: 0; color: var(--text-secondary); font-size: 13px; line-height: 1.6; }.empty { padding: 24px 0; }.archive-list { display: grid; gap: 8px; }.archive { border: 1px solid var(--card-stroke-default); background: var(--card-bg-default); border-radius: 8px; overflow: hidden; }
.archive-header { display: flex; align-items: center; width: 100%; gap: 16px; padding: 15px 16px; border: 0; color: var(--text-primary); background: transparent; text-align: left; font: inherit; cursor: pointer; }.archive-header:hover { background: var(--subtle-secondary); }.archive-header:focus-visible { outline: 2px solid var(--accent); outline-offset: -3px; }.identity { flex: 1; min-width: 0; }.identity strong { display: block; font-size: 13px; font-weight: 500; overflow-wrap: anywhere; }.identity > span,.status > span:last-child { display: block; color: var(--text-secondary); font-size: 12px; margin-top: 5px; }.status { flex: none; text-align: right; }.badge { font-size: 12px; background: var(--subtle-secondary); padding: 3px 8px; border-radius: 4px; }.badge.overridden { color: var(--accent); }.archive-detail { border-top: 1px solid var(--card-stroke-default); padding: 16px; display: grid; gap: 10px; }.hash { margin: 0; display: flex; gap: 12px; color: var(--text-secondary); font-size: 12px; flex-wrap: wrap; }.hash code { overflow-wrap: anywhere; }.edit { display: grid; grid-template-columns: 70px minmax(0, 1fr); gap: 5px 12px; padding: 8px 0; border-bottom: 1px solid var(--card-stroke-default); font-size: 12px; }.edit code { overflow-wrap: anywhere; }.edit > span:last-child { grid-column: 2; color: var(--text-secondary); }.edit small { margin-left: 8px; }.override-list-enter-active,.override-list-move { transition: opacity .18s, transform .18s; }.override-list-enter-from { opacity: 0; transform: translateY(8px); }@media(prefers-reduced-motion:reduce) { .override-list-enter-active,.override-list-move { transition: none; } }
</style>

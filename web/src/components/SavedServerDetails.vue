<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, type Server, type ServerCatalog, type ServerPreset } from '../bridge'
import { policyPlugins, policySources } from '../clientPolicy'
import { localized } from '../i18n'
import { mergeServerPresentation } from '../serverDescription'
import { selectedGame, state } from '../state'
import PageBody from './PageBody.vue'
import ServerIcon from './ServerIcon.vue'
import ServerInformation from './ServerInformation.vue'
import WButton from '../controls/WButton.vue'
import FluentIcon from '../controls/FluentIcon.vue'

const props = defineProps<{ server: Server; busy: boolean }>()
const emit = defineEmits<{ edit: []; remove: []; choose: []; preset: [value: ServerPreset] }>()
const { t } = useI18n()
const catalog = ref<ServerCatalog>(), loading = ref(false), error = ref('')
const presentation = computed(() => mergeServerPresentation(props.server.presentation, catalog.value?.presentation))
let generation = 0
// Browsing a server must not switch the Home destination or its account session.
async function refresh() {
  const id = props.server.id, request = ++generation
  catalog.value = undefined; error.value = ''; loading.value = true
  try {
    const result = await call('servers.catalog', { id })
    if (request === generation) catalog.value = result
  } catch (e) { if (request === generation) error.value = (e as Error).message }
  finally { if (request === generation) loading.value = false }
}
watch(() => props.server, () => void refresh(), { immediate: true })
onBeforeUnmount(() => { generation++ })
</script>
<template>
  <div class="saved-detail">
    <div class="detail-heading"><div class="server-mark"><ServerIcon :server-id="server.id" :url="presentation?.iconUrl" :translations="presentation?.iconUrlTranslations" :asset="server.iconAsset" :room="server.kind === 'room'" /></div><div class="server-title"><h2>{{ server.name }}</h2><p>{{ t('multiplayer.savedServers') }}<template v-if="server.id === state.selectedServerId"> · {{ t('live.selected') }}</template></p></div><WButton variant="subtle" :disabled="busy || state.busy" :aria-label="t('service.editServer')" @click="emit('edit')"><FluentIcon glyph="edit" /></WButton><WButton v-if="!server.builtIn" variant="subtle" :disabled="busy || state.busy" :aria-label="t('service.remove')" @click="emit('remove')"><FluentIcon glyph="delete" /></WButton></div>
    <PageBody class="server-content">
      <ServerInformation v-if="catalog" :game="server.game" :builds="[...new Set(catalog.instances.map(i => i.build))]" :presentation="presentation" :recommendations="policyPlugins(catalog.clientPolicy)" :sources="policySources(catalog.clientPolicy)" :server-id="server.id" />
      <dl><dt>{{ t('multiplayer.address') }}</dt><dd>{{ server.apiUrl || (server.endpoint ? `${server.endpoint.host}:${server.endpoint.port}` : t('service.noApi')) }}</dd></dl>
      <div class="presets-heading"><h3>{{ t('service.bundledPresets') }}</h3><WButton variant="subtle" :disabled="loading || busy || state.busy" @click="refresh">{{ t('live.refresh') }}</WButton></div>
      <p v-if="error" class="catalog-error" role="alert">{{ error }}</p>
      <p v-else-if="loading" class="muted" role="status">{{ t('live.working') }}</p>
      <p v-else-if="!catalog?.instances.length" class="muted">{{ t('live.clientUnavailable') }}</p>
      <div v-else class="preset-list"><article v-for="item in catalog.instances" :key="item.id" class="preset-card surface"><div><h4>{{ localized(item.name, { dates: true }) }}</h4><p v-if="item.releaseVersion">{{ item.releaseVersion }}</p><small>{{ item.build }}</small></div><WButton :disabled="busy || state.busy || !selectedGame?.builds.some(b => b.id === item.build)" @click="emit('preset', item)">{{ t(selectedGame?.builds.some(b => b.id === item.build) ? 'service.importPreset' : 'service.presetUnavailable') }}</WButton></article></div>
    </PageBody>
    <div class="server-footer"><WButton variant="accent" :disabled="busy || state.busy" @click="emit('choose')"><FluentIcon glyph="connect" />{{ t('multiplayer.connect') }}</WButton></div>
  </div>
</template>
<style scoped>
.saved-detail { flex: 1; min-height: 0; display: flex; flex-direction: column; }
.detail-heading { flex: none; display: flex; align-items: center; gap: 14px; padding: 30px; }
.server-mark { width: 66px; height: 66px; display: grid; place-items: center; flex: none; border: 1px solid var(--card-stroke-default); border-radius: 13px; background: var(--subtle-fill-secondary); color: var(--accent-default); }
.server-title { flex: 1; min-width: 0; }.server-title h2 { font-size: 23px; font-weight: 600; margin: 0 0 7px; overflow-wrap: anywhere; }.server-title p { margin: 0; color: var(--text-secondary); font-size: 12px; }
.server-content { --page-inset-inline: 30px; --page-inset-bottom: 20px; margin-inline: 0; }
dl { margin: 0; font-size: 13px; }dt { margin-bottom: 9px; color: var(--text-secondary); }dd { margin: 0; overflow-wrap: anywhere; }
.presets-heading { display: flex; align-items: center; justify-content: space-between; gap: 16px; margin: 24px 0 12px; }.presets-heading h3 { font-size: 14px; font-weight: 600; margin: 0; }
.preset-list { display: grid; gap: 8px; }.preset-card { display: flex; align-items: center; gap: 16px; padding: 16px; }.preset-card > div { flex: 1; min-width: 0; }.preset-card h4 { font-size: 14px; font-weight: 600; margin: 0; overflow-wrap: anywhere; }.preset-card p { margin: 6px 0; }.preset-card p,.preset-card small { color: var(--text-secondary); font-size: 12px; }
.catalog-error { color: var(--fill-critical); overflow-wrap: anywhere; font-size: 13px; }
.server-footer { flex: none; display: flex; justify-content: flex-end; padding: 24px 30px; border-top: 1px solid var(--card-stroke-default); background: var(--subtle-fill-secondary); }.server-footer > button { min-width: 140px; }
</style>

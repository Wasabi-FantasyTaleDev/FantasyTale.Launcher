<script setup lang="ts">
// deprecated: merged into Multiplayer > Servers; retained for reference, no longer maintained.
import PageBody from '../components/PageBody.vue'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, demoMode, inHost, type Server, type ServerPreset } from '../bridge'
import { gameServers, perform, refreshServers, refreshServices, selectedGame, state } from '../state'
import { localized } from '../i18n'
import ServerDialog from '../components/ServerDialog.vue'
import InstanceDialog from '../components/InstanceDialog.vue'
import WButton from '../controls/WButton.vue'
import WDialog from '../controls/WDialog.vue'
import FluentIcon from '../controls/FluentIcon.vue'
const { t } = useI18n()
const open = ref(false), editing = ref<Server>(), deleting = ref<Server>(), preset = ref<ServerPreset>()
function edit(server?: Server) { editing.value = server; open.value = true }
async function remove() {
  if (deleting.value && await perform(async () => { await call('servers.delete', { id: deleting.value!.id }); await refreshServers() })) deleting.value = undefined
}
</script>
<template>
  <section class="page"><header class="page-header"><div><h1 class="page-title">{{ t('servers.title') }}</h1><p class="page-desc">{{ t('service.serverIndependent') }}</p></div><WButton variant="accent" :disabled="state.busy" @click="edit()"><FluentIcon glyph="add" />{{ t('service.addServer') }}</WButton></header>
    <PageBody contained>
    <div class="server-workspace">
    <PageBody class="server-list-viewport">
    <div class="server-list"><article v-for="server in gameServers" :key="server.id" class="server-row surface" :class="{ selected: server.id === state.selectedServerId }">
      <button class="server-select" :disabled="state.busy" :aria-pressed="server.id === state.selectedServerId" @click="state.selectedServerId = server.id"><FluentIcon glyph="globe" :size="24" /><span><strong>{{ server.name }}</strong><small>{{ server.apiUrl || server.endpoint?.host || t('service.noApi') }}</small></span></button>
      <span v-if="server.id === state.selectedServerId" class="selected-label">{{ t('live.selected') }}</span><WButton variant="subtle" :disabled="state.busy" :aria-label="t('service.editServer')" @click="edit(server)"><FluentIcon glyph="edit" /></WButton><WButton v-if="!server.builtIn" variant="subtle" :disabled="state.busy || !(inHost || demoMode?.enabled)" :aria-label="t('service.remove')" @click="deleting = server"><FluentIcon glyph="delete" /></WButton>
    </article></div>
    </PageBody>
    <section class="server-presets surface">
    <div class="section-heading"><h2>{{ t('service.bundledPresets') }}</h2><WButton variant="subtle" :disabled="state.serverLoading || state.busy" @click="refreshServices">{{ t('live.refresh') }}</WButton></div>
    <PageBody class="preset-viewport">
    <p v-if="state.serverError" class="muted" role="status">{{ state.serverError }}</p><p v-if="state.serverLoading" class="muted">{{ t('live.working') }}</p>
    <p v-else-if="!state.catalog?.instances.length" class="muted">{{ t('live.clientUnavailable') }}</p>
    <template v-else><p class="muted">{{ t('service.presetNote') }}</p><div class="preset-grid"><article v-for="item in state.catalog.instances" :key="item.id" class="preset-card surface"><div><h3>{{ localized(item.name, { dates: true }) }}</h3><p v-if="item.releaseVersion">{{ item.releaseVersion }}</p><small>{{ item.build }}</small></div><WButton :disabled="state.busy || !selectedGame?.builds.some(b => b.id === item.build)" @click="preset = item">{{ t(selectedGame?.builds.some(b => b.id === item.build) ? 'service.importPreset' : 'service.presetUnavailable') }}</WButton></article></div></template>
    <p v-if="state.catalog?.hostPolicy === 'ip' || state.catalog?.hostPolicy === 'domain'" class="policy-note">{{ t('service.ipPreference') }}: {{ t(state.catalog.hostPolicy === 'ip' ? 'service.on' : 'service.off') }} · {{ t('service.serverManaged') }}</p>
    </PageBody>
    </section>
    </div>
    <ServerDialog :open="open" :server="editing" @close="open = false" /><InstanceDialog :open="!!preset" :preset="preset" @close="preset = undefined" />
    <WDialog :open="!!deleting" :title="t('service.removeServerTitle')" :primary-text="t('service.remove')" :close-text="t('dialog.cancel')" :primary-disabled="state.busy" :close-disabled="state.busy" @primary="remove" @close="deleting = undefined"><p>{{ t('service.removeServerBody', { name: deleting?.name }) }}</p></WDialog>
    <!-- <p v-if="!inHost" class="muted">{{ t('live.browserOnly') }}</p> -->
  </PageBody>
  </section>
</template>
<style scoped>
.server-list { display: flex; flex-direction: column; gap: 6px; }.server-row { display: flex; align-items: center; gap: 8px; padding: 12px; position: relative; }.server-row.selected::before { content: ''; position: absolute; left: 0; height: 24px; width: 3px; border-radius: 2px; background: var(--accent-default); }.server-select { display: flex; align-items: center; gap: 18px; padding: 8px; min-width: 0; flex: 1; text-align: left; }.server-select > span { display: flex; flex-direction: column; gap: 6px; min-width: 0; }.server-select strong { font-weight: 600; }.server-select small { overflow-wrap: anywhere; color: var(--text-secondary); }.server-select > .fluent-icon { flex: none; color: var(--text-secondary); }.selected-label { color: var(--text-secondary); font-size: 12px; }.section-heading { display: flex; justify-content: space-between; align-items: center; margin: 24px 0 8px; }.section-heading h2 { font-size: 16px; font-weight: 600; margin: 0; }.preset-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 300px), 1fr)); gap: 8px; }.preset-card { display: flex; align-items: center; gap: 16px; padding: 20px; }.preset-card > div { flex: 1; }.preset-card h3 { margin: 0; font-size: 14px; font-weight: 600; }.preset-card p, .preset-card small, .policy-note { color: var(--text-secondary); font-size: 12px; }.preset-card p { margin: 6px 0; }.policy-note { margin-top: 20px; }@media(max-width: 650px) { .selected-label { display: none; } }

.server-workspace { flex: 1; min-height: 0; display: grid; grid-template-columns: minmax(340px, 0.8fr) minmax(0, 1.2fr); gap: 20px; }
.server-list-viewport { --page-inset-inline: 0px; --page-inset-bottom: 0px; }
.server-presets { display: flex; flex-direction: column; min-height: 0; overflow: hidden; }
.server-presets .section-heading { flex: none; margin: 0; padding: 16px 20px; border-bottom: 1px solid var(--card-stroke-default); }
.preset-viewport { --page-inset-inline: 20px; --page-inset-bottom: 20px; margin-inline: 0; }
.preset-viewport :deep(.page-scroll) { padding-top: 16px; }
.preset-grid { grid-template-columns: minmax(0, 1fr); margin-top: 12px; }
.server-row { flex-wrap: wrap; }
.server-select { min-width: 200px; }
.selected-label { display: none; }
</style>

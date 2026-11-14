<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import MarkdownText from './MarkdownText.vue'
import type { ServerPresentation, PolicyPluginEntry, ServerStoreSource } from '../bridge'
import { state } from '../state'
import { localized } from '../i18n'
import { serverDescription } from '../serverDescription'
import { clientVersionCaption } from '../clientVersion'
import ServerPluginListDialog from './ServerPluginListDialog.vue'
import WButton from '../controls/WButton.vue'
import OnlinePlayersDialog from './OnlinePlayersDialog.vue'
const props = defineProps<{ game: string; builds: string[]; presentation?: ServerPresentation | null; description?: string; recommendations?: PolicyPluginEntry[]; sources?: ServerStoreSource[]; serverId?: string; sourceId?: string; entryId?: string }>()
const { t, locale } = useI18n(), pluginsOpen = ref(false), rosterOpen = ref(false), rosterUnavailable = ref(false)
const descriptionText = computed(() => serverDescription(props.presentation, locale.value, props.description))
const rosterEnabled = computed(() => props.presentation?.rosterEnabled === true && !rosterUnavailable.value)
watch([() => props.game, () => props.serverId, () => props.sourceId, () => props.entryId, () => props.presentation?.rosterEnabled], () => { rosterUnavailable.value = false })
watch(rosterEnabled, enabled => { if (!enabled) rosterOpen.value = false })
const supported = computed(() => props.builds.map(id => {
  const build = state.games.find(g => g.id === props.game)?.builds.find(b => b.id === id)
  return { id, name: build ? localized(build.name, { dates: true }) : id, caption: build ? clientVersionCaption(build) : '' }
}))
</script>
<template>
  <div class="server-information">
    <MarkdownText v-if="descriptionText" class="motd" :source="descriptionText" />
    <div class="status-badges"><span v-if="presentation?.state && presentation.state !== 'online'" :class="presentation.state">{{ t(`serverInfo.${presentation.state}`) }}</span><span v-if="presentation?.whitelist">{{ t('serverInfo.whitelist') }}</span></div>
    <dl class="player-counts">
      <div v-if="presentation?.onlinePlayers != null && presentation.onlinePlayers >= 0"><dt :title="presentation?.presenceComplete === false ? t('serverInfo.populationIncomplete') : undefined">{{ t('serverInfo.onlinePlayers') }}</dt><dd>{{ presentation.presenceComplete === false ? '≥ ' : '' }}{{ presentation.onlinePlayers }}</dd></div>
      <div v-if="presentation?.peakOnlinePlayers != null && presentation.peakOnlinePlayers >= 0"><dt>{{ t('serverInfo.peakOnlinePlayers') }}</dt><dd>{{ presentation.peakOnlinePlayers }}</dd></div>
      <div v-if="presentation?.playerLimit != null"><dt>{{ t('serverInfo.playerLimit') }}</dt><dd>{{ presentation.playerLimit }}</dd></div>
    </dl>
    <div class="supported"><h3>{{ t('serverInfo.builds') }}</h3><div v-for="build in supported" :key="build.id" class="supported-build" :title="build.caption"><span>{{ build.name }}</span></div><p v-if="!supported.length" class="muted">{{ t('serverInfo.unknownBuilds') }}</p></div>
    <div class="information-actions"><WButton v-if="recommendations?.length" @click="pluginsOpen = true">{{ t('serverInfo.plugins') }}</WButton><WButton v-if="rosterEnabled" @click="rosterOpen = true">{{ t('serverInfo.roster') }}</WButton></div>
    <OnlinePlayersDialog :open="rosterOpen" :game="game" :server-id="serverId" :source-id="sourceId" :entry-id="entryId" @close="rosterOpen = false" @availability="rosterUnavailable = !$event" />
    <ServerPluginListDialog :open="pluginsOpen" :game="game" :builds="builds" :recommendations="recommendations ?? []" :sources="sources ?? []" :server-id="serverId" @close="pluginsOpen = false" />
  </div>
</template>
<style scoped>
.server-information { display: grid; gap: 18px; margin-bottom: 24px; }.motd { margin: 0; overflow-wrap: anywhere; color: var(--text-secondary); line-height: 1.7; }
.status-badges { display: flex; flex-wrap: wrap; gap: 8px; }.status-badges:empty { display: none; }.status-badges span { padding: 4px 9px; border-radius: 4px; background: var(--subtle-secondary); font-size: 12px; }.status-badges .online { color: var(--status-success, #298348); }.status-badges .maintenance { color: var(--status-warning, #9d5d00); }
.player-counts { display: flex; flex-wrap: wrap; gap: 20px; margin: 0; font-size: 13px; }.player-counts div { display: grid; gap: 6px; }.player-counts dt, small { color: var(--text-secondary); }.player-counts dd { margin: 0; }
.supported h3 { margin: 0 0 10px; font-weight: 600; font-size: 13px; }.supported-build { display: inline-flex; margin: 0 6px 6px 0; padding: 4px 7px; border-radius: 4px; background: var(--subtle-fill-secondary); font-size: 11px; }.information-actions { display: flex; flex-wrap: wrap; gap: 10px; }
</style>

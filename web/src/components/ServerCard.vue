<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ServerPresentation, PolicyPluginEntry, ServerStoreSource } from '../bridge'
import { state } from '../state'
import { localized } from '../i18n'
import { serverDescription } from '../serverDescription'
import ServerIcon from './ServerIcon.vue'
import TrendIcon from './TrendIcon.vue'
import OnlinePlayersDialog from './OnlinePlayersDialog.vue'
import WButton from '../controls/WButton.vue'
import FluentIcon from '../controls/FluentIcon.vue'
const props = defineProps<{ name: string; game: string; builds: string[]; buildsLoading?: boolean; room?: boolean; address?: string | null; description?: string; presentation?: ServerPresentation | null; recommendations?: PolicyPluginEntry[]; sources?: ServerStoreSource[]; serverId?: string; iconAsset?: string | null; sourceId?: string; entryId?: string; selected?: boolean; busy?: boolean; favorite?: boolean }>()
const emit = defineEmits<{ details: []; connect: []; favorite: [] }>()
const { t, locale } = useI18n(), rosterOpen = ref(false), rosterUnavailable = ref(false)
const rosterEnabled = computed(() => props.presentation?.rosterEnabled === true && !rosterUnavailable.value)
const versionNames = computed(() => props.builds.map(id => {
  const build = state.games.find(game => game.id === props.game)?.builds.find(build => build.id === id)
  return build ? localized(build.name, { dates: true }) : t('serverInfo.otherBuild')
}))
const versions = computed(() => versionNames.value.slice(0, 2))
const motd = computed(() => serverDescription(props.presentation, locale.value, props.description, true).trim())
const hasCounts = computed(() => (props.presentation?.onlinePlayers ?? -1) >= 0 || (props.presentation?.peakOnlinePlayers ?? -1) >= 0 || props.presentation?.playerLimit != null)
watch([() => props.game, () => props.serverId, () => props.sourceId, () => props.entryId, () => props.presentation?.rosterEnabled], () => { rosterUnavailable.value = false })
watch(rosterEnabled, enabled => { if (!enabled) rosterOpen.value = false })
</script>
<template>
  <article class="server-card surface" :class="{ 'current-server': selected }" :style="{ order: favorite ? -1 : 0 }">
    <button type="button" class="card-open" :aria-label="`${t('serverInfo.details')}: ${name}`" @click="emit('details')" />
    <div class="card-overview">
      <div class="card-heading"><span class="card-icon"><ServerIcon :server-id="serverId" :url="presentation?.iconUrl" :translations="presentation?.iconUrlTranslations" :asset="iconAsset" :room="room" :size="28" /></span><div class="card-title"><h3 :title="name">{{ name }}</h3><div class="card-state"><span v-if="presentation?.state && presentation.state !== 'online'" :class="presentation.state">{{ t(`serverInfo.${presentation.state}`) }}</span><span v-if="presentation?.whitelist">{{ t('serverInfo.whitelist') }}</span><span v-if="selected" class="selection-label">{{ t('live.selected') }}</span></div></div></div>
      <p class="card-motd">{{ motd || t('serverInfo.noIntroduction') }}</p>
      <div v-if="hasCounts" class="card-counts">
        <component :is="rosterEnabled ? 'button' : 'span'" v-if="presentation?.onlinePlayers != null && presentation.onlinePlayers >= 0" :type="rosterEnabled ? 'button' : undefined" class="count-item" :title="t(presentation?.presenceComplete === false ? 'serverInfo.populationIncomplete' : 'serverInfo.onlinePlayers')" :aria-label="`${t('serverInfo.onlinePlayers')}: ${presentation.presenceComplete === false ? '≥ ' : ''}${presentation.onlinePlayers}`" @click="rosterEnabled && (rosterOpen = true)"><FluentIcon glyph="contact" :size="16" /><span>{{ presentation.presenceComplete === false ? '≥ ' : '' }}{{ presentation.onlinePlayers }}<template v-if="presentation.playerLimit != null && !presentation.presenceGroup"> / {{ presentation.playerLimit }}</template></span></component>
        <span v-if="presentation?.playerLimit != null && (presentation.presenceGroup || (presentation.onlinePlayers ?? -1) < 0)" class="count-item" :title="t('serverInfo.playerLimit')"><FluentIcon glyph="contact" :size="16" /><span>{{ presentation.playerLimit }}</span></span>
        <span v-if="presentation?.peakOnlinePlayers != null && presentation.peakOnlinePlayers >= 0" class="count-item" :title="t('serverInfo.peakOnlinePlayers')" :aria-label="`${t('serverInfo.peakOnlinePlayers')}: ${presentation.peakOnlinePlayers}`"><TrendIcon /><span>{{ presentation.peakOnlinePlayers }}</span></span>
      </div>
      <div v-if="buildsLoading || builds.length" class="card-builds" :aria-busy="buildsLoading" :title="versionNames.join('、')">
        <Transition name="build-fade" mode="out-in">
          <div v-if="buildsLoading && !versions.length" key="loading" class="build-placeholders" aria-hidden="true"><span /><span /></div>
          <div v-else :key="versions.join('|') || 'empty'" class="build-labels"><span v-for="(name,index) in versions" :key="index" class="build-label">{{ name }}</span><span v-if="builds.length > versions.length" class="build-more">+{{ builds.length - versions.length }}</span></div>
        </Transition>
      </div>
    </div>
    <div class="card-actions">
      <WButton variant="standard" :aria-pressed="favorite" :aria-label="t(favorite ? 'serverInfo.favorited' : 'serverInfo.favorite')" @click="emit('favorite')"><span class="action-content"><svg class="favorite-star" :class="{ active: favorite }" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round" aria-hidden="true"><path class="favorite-fill" fill="currentColor" d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9Z" /><path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-2.9-5.6 2.9 1.1-6.2L3 9.6l6.2-.9Z" /></svg><span class="action-label favorite-label" aria-hidden="true"><span :class="{ visible: !favorite }">{{ t('serverInfo.favorite') }}</span><span :class="{ visible: favorite }">{{ t('serverInfo.favorited') }}</span></span></span></WButton>
      <WButton variant="standard" @click="emit('details')"><span class="action-content"><FluentIcon glyph="info" :size="14" /><span class="action-label">{{ t('serverInfo.details') }}</span></span></WButton>
      <WButton variant="accent" :disabled="busy" @click="emit('connect')"><span class="action-content"><FluentIcon glyph="connect" :size="14" /><span class="action-label">{{ t(room ? 'multiplayer.join' : 'multiplayer.connect') }}</span></span></WButton>
    </div>
    <OnlinePlayersDialog :open="rosterOpen" :game="game" :server-id="serverId" :source-id="sourceId" :entry-id="entryId" @close="rosterOpen = false" @availability="rosterUnavailable = !$event" />
  </article>
</template>
<style scoped>
.server-card { position: relative; isolation: isolate; container-type: inline-size; min-width: 0; display: flex; flex-direction: column; border-radius: 8px; overflow: hidden; transition: border-color 140ms linear,box-shadow 140ms linear; }
.card-open { position: absolute; inset: 0; z-index: 1; width: 100%; border-radius: inherit; cursor: pointer; }
.card-open:focus-visible { outline-offset: -3px; }
.server-card:not(.current-server):has(.card-open:hover) { border-color: var(--ctrl-stroke-secondary); box-shadow: 0 2px 8px rgb(0 0 0 / 4%); }
.current-server { border-color: var(--accent-default); }
.card-overview { display: flex; flex-direction: column; gap: 14px; flex: none; height: 256px; box-sizing: border-box; text-align: start; padding: 20px; width: 100%; }

.card-heading { display: flex; gap: 12px; align-items: center; min-width: 0; width: 100%; text-align: start; border-radius: 6px; }
.card-icon { display: grid; place-items: center; width: 44px; height: 44px; flex: none; border-radius: 8px; background: var(--subtle-secondary); color: var(--accent-default); }
.card-title { display: flex; align-items: center; gap: 8px; min-width: 0; flex: 1; }.card-title h3 { min-width: 0; font-size: 15px; line-height: 1.4; font-weight: 600; margin: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.card-state { display: flex; flex: none; gap: 5px; font-size: 11px; color: var(--text-secondary); }.card-state:empty { display: none; }.card-state > span { padding: 2px 6px; line-height: 16px; border-radius: 4px; white-space: nowrap; background: var(--subtle-secondary); }.card-state .maintenance { color: var(--fill-caution); background: color-mix(in srgb,var(--fill-caution) 8%,transparent); }.card-state .selection-label { color: var(--accent-text-primary); background: color-mix(in srgb,var(--accent-default) 8%,transparent); }
.card-motd { flex: 1; min-height: 0; margin: 0; color: var(--text-secondary); font-size: 12px; line-height: 1.65; overflow: hidden; overflow-wrap: anywhere; white-space: pre-line; mask-image: linear-gradient(to bottom,#000 calc(100% - 10px),transparent); }
.card-counts { display: flex; align-items: center; gap: 20px; min-height: 24px; color: var(--text-secondary); font-size: 13px; line-height: 20px; font-variant-numeric: tabular-nums; }.count-item { display: inline-flex; align-items: center; gap: 6px; height: 24px; padding: 0; color: inherit; font: inherit; }.count-item span { line-height: 20px; }.count-item svg { display: block; flex: none; }button.count-item:hover { color: var(--accent-default); }button.count-item { position: relative; z-index: 2; border-radius: 4px; }
.card-builds { display: flex; align-items: center; flex: none; height: 26px; min-width: 0; overflow: hidden; white-space: nowrap; }
.build-labels, .build-placeholders { display: flex; align-items: center; gap: 5px; min-width: 0; width: 100%; }
.build-labels > span { padding: 3px 6px; border-radius: 4px; background: var(--subtle-secondary); font-size: 11px; color: var(--text-secondary); overflow: hidden; text-overflow: ellipsis; min-width: 0; }
.build-more { flex: none; }.build-label { flex: 0 1 auto; }
.build-placeholders > span { height: 22px; width: 104px; max-width: 40%; border-radius: 4px; background: var(--subtle-secondary); animation: build-pulse 1.4s ease-in-out infinite; }.build-placeholders > span + span { width: 92px; animation-delay: 160ms; }
.build-fade-enter-active, .build-fade-leave-active { transition: opacity 160ms ease; }.build-fade-enter-from, .build-fade-leave-to { opacity: 0; }
@keyframes build-pulse { 50% { opacity: .35; } }
.card-actions { display: grid; grid-template-columns: repeat(3, minmax(max-content, 1fr)); align-items: center; gap: 8px; border-top: 1px solid var(--card-stroke-default); padding: 12px; }
.card-actions .w-btn { z-index: 2; width: 100%; min-width: 0; height: 34px; font-size: 13px; line-height: 20px; padding: 6px 8px; }
.action-content { display: inline-flex; align-items: center; justify-content: center; gap: 5px; }.action-content > svg { flex: none; display: block; }.action-label { white-space: nowrap; }
.favorite-star { transition: color 180ms linear; }.favorite-star.active { color: #e4ac00; }.favorite-fill { opacity: 0; transition: opacity 180ms linear; }.favorite-star.active .favorite-fill { opacity: 1; }
.favorite-label { display: inline-grid; }.favorite-label > span { grid-area: 1 / 1; opacity: 0; transition: opacity 180ms linear; }.favorite-label > .visible { opacity: 1; }
@container (max-width: 369px) { .card-actions { grid-template-columns: repeat(2, minmax(0, 1fr)); }.card-actions .w-btn:last-child { grid-column: 1 / -1; } }
@media (prefers-reduced-motion: reduce) { .build-placeholders > span { animation: none; }.build-fade-enter-active, .build-fade-leave-active, .favorite-star, .favorite-fill, .favorite-label > span { transition: none; } }
</style>

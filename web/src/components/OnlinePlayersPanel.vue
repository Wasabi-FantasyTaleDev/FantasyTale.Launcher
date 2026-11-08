<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, type OnlinePlayerList } from '../bridge'
import { selectedGame, state } from '../state'
import { localized } from '../i18n'
import { accountLevel } from '../levels'
import WTextBox from '../controls/WTextBox.vue'
import WButton from '../controls/WButton.vue'
import ServerFailureNotice from './ServerFailureNotice.vue'
import RankIcon from './RankIcon.vue'
import FluentIcon from '../controls/FluentIcon.vue'
const props = defineProps<{ active: boolean; game?: string; serverId?: string; sourceId?: string; entryId?: string }>()
const emit = defineEmits<{ availability: [enabled: boolean] }>()
const { t } = useI18n()
const roster = ref<OnlinePlayerList>(), loading = ref(false), error = ref<Error>(), query = ref(''), scrollTop = ref(0), viewport = ref<HTMLElement>()
const columns = ref(3), viewportHeight = ref(336)
const game = computed(() => props.game ? state.games.find(item => item.id === props.game) : selectedGame.value)
let resize: ResizeObserver | undefined
let revision = 0
const entries = computed(() => (roster.value?.enabled ? roster.value.players : []).map((player, index) => ({ ...player, index,
  buildLabel: player.buildId ? localized(game.value?.builds.find(build => build.id === player.buildId)?.name, { dates: true }) || player.buildId : '',
  search: player.name.normalize('NFKC').toLocaleLowerCase() })))
const filtered = computed(() => { const text = query.value.trim().normalize('NFKC').toLocaleLowerCase(); return text ? entries.value.filter(p => p.search.includes(text)) : entries.value })
const rowStride = 86
const firstRow = computed(() => Math.max(0, Math.floor(scrollTop.value / rowStride) - 2))
const first = computed(() => firstRow.value * columns.value)
const visible = computed(() => filtered.value.slice(first.value, first.value + (Math.ceil(viewportHeight.value / rowStride) + 4) * columns.value).map(player => ({ ...player,
  rank: player.level == null ? null : accountLevel(game.value, { username: player.name, displayName: player.name, stats: { level: player.level }, levelIconUrl: null }),
})))
const contentHeight = computed(() => Math.max(0, Math.ceil(filtered.value.length / columns.value) * rowStride - 10))
watch(viewport, node => {
  resize?.disconnect()
  if (!node) return
  const measure = () => { columns.value = Math.max(1, Math.floor((node.clientWidth - 8 + 10) / 220)); viewportHeight.value = node.clientHeight }
  measure()
  if (typeof ResizeObserver !== 'undefined') { resize = new ResizeObserver(measure); resize.observe(node) }
})
watch(query, () => { scrollTop.value = 0; if (viewport.value) viewport.value.scrollTop = 0 })
async function refresh() {
  const expected = ++revision; error.value = undefined; loading.value = true
  try {
    const next = props.serverId ? await call('servers.onlinePlayers', { id: props.serverId }) : await call('directory.onlinePlayers', { sourceId: props.sourceId, entryId: props.entryId })
    if (props.active && expected === revision) { roster.value = next; emit('availability', next.enabled); scrollTop.value = 0; if (viewport.value) viewport.value.scrollTop = 0 }
  } catch (e) { if (props.active && expected === revision) { roster.value = undefined; error.value = e as Error } }
  finally { if (expected === revision) loading.value = false }
}
watch(() => [props.active, props.game, props.serverId, props.sourceId, props.entryId] as const, () => {
  ++revision; loading.value = false; error.value = undefined; roster.value = undefined; query.value = ''; scrollTop.value = 0
  if (props.active) void refresh()
}, { immediate: true })
onBeforeUnmount(() => { ++revision; resize?.disconnect() })
</script>
<template>
  <section class="online-players-panel">
    <div class="roster-toolbar"><WTextBox v-model="query" :placeholder="t('serverInfo.searchPlayers')" :aria-label="t('serverInfo.searchPlayers')" /><WButton :disabled="loading" @click="refresh">{{ t('live.refresh') }}</WButton></div>
    <p v-if="loading" role="status">{{ t('live.working') }}</p>
    <ServerFailureNotice v-else-if="error" :error="error" />
    <p v-else-if="roster && !roster.enabled">{{ t('serverInfo.rosterHidden') }}</p>
    <template v-else-if="roster"><p v-if="roster.complete === false" class="roster-count" role="status">{{ t('serverInfo.rosterIncomplete') }}</p><p v-if="roster.truncated" class="roster-count" role="status">{{ t('serverInfo.rosterTruncated') }}</p><p class="roster-count" role="status">{{ t('serverInfo.playerCount', { shown: filtered.length, total: entries.length }) }}</p><div ref="viewport" class="roster-viewport" role="list" :aria-label="t('serverInfo.roster')" @scroll="scrollTop = ($event.target as HTMLElement).scrollTop"><div class="roster-space" :style="{ height: `${contentHeight}px` }"><div class="roster-grid" :style="{ gridTemplateColumns: `repeat(${columns},minmax(0,1fr))`, transform: `translateY(${firstRow * rowStride}px)` }"><article v-for="(player, index) in visible" :key="player.index" class="roster-player" role="listitem" :aria-setsize="filtered.length" :aria-posinset="first + index + 1"><span class="player-rank"><RankIcon v-if="game?.levelIcon && game.assetBase && player.rank?.icon" :icon="player.rank.icon" :asset-base="game.assetBase" :size="32" :label="`${t('serverInfo.level')} ${player.rank.label}`" /><FluentIcon v-else glyph="contact" :size="24" /></span><div class="player-identity"><div class="player-title"><strong :title="player.name">{{ player.name }}</strong><span v-if="player.level != null" class="player-level" :title="player.rank?.name ?? undefined">{{ t('serverInfo.levelShort', { level: player.rank?.label ?? player.level }) }}</span></div><span v-if="player.buildLabel" class="player-build" :title="player.buildLabel">{{ player.buildLabel }}</span></div></article></div></div><p v-if="!filtered.length" class="muted">{{ t('serverInfo.noPlayers') }}</p></div></template>
  </section>
</template>
<style scoped>
.online-players-panel { height: 100%; min-height: 0; display: flex; flex-direction: column; }.roster-toolbar { display: flex; gap: 12px; flex: none; }.roster-toolbar .w-textbox { flex: 1; min-width: 0; }.roster-count { font-size: 12px; color: var(--text-secondary); margin-block: 14px 10px; flex: none; }
.roster-viewport { flex: 1; min-height: 180px; height: 336px; overflow-y: auto; scrollbar-gutter: stable; }.roster-space { position: relative; margin: 4px; }.roster-grid { position: absolute; inset: 0 0 auto; display: grid; gap: 10px; }
.roster-player { display: flex; gap: 12px; align-items: center; min-width: 0; height: 76px; box-sizing: border-box; padding: 12px; border: 1px solid var(--card-stroke-default); background: var(--card-bg-default); border-radius: 7px; transition: background-color 140ms linear; }.roster-player:hover { background: var(--subtle-secondary); }.player-rank { display: grid; place-items: center; width: 42px; height: 42px; flex: none; border-radius: 8px; background: var(--subtle-secondary); color: var(--text-secondary); }.player-identity { display: grid; flex: 1; gap: 5px; min-width: 0; }.player-title { display: flex; align-items: center; gap: 12px; min-width: 0; }.player-build { color: var(--text-secondary); font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }.player-identity strong { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 13px; font-weight: 500; }.player-level { flex: none; white-space: nowrap; color: var(--text-secondary); font-size: 12px; font-variant-numeric: tabular-nums; }
</style>

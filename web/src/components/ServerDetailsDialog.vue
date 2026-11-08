<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { call, inHost, type Server, type ServerCatalog, type DirectoryEntry, type ServerPreset } from '../bridge'
import { policyPlugins, policySources } from '../clientPolicy'
import { state } from '../state'
import { localized } from '../i18n'
import { mergeServerPresentation, serverDescription, serverGallery } from '../serverDescription'
import { serverImageUrl } from '../serverArtwork'
import ServerIcon from './ServerIcon.vue'
import TrendIcon from './TrendIcon.vue'
import ImageViewer from './ImageViewer.vue'
import ServerPluginListPanel from './ServerPluginListPanel.vue'
import OnlinePlayersPanel from './OnlinePlayersPanel.vue'
import ServerFailureNotice from './ServerFailureNotice.vue'
import WDialog from '../controls/WDialog.vue'
import WButton from '../controls/WButton.vue'
import WComboBox from '../controls/WComboBox.vue'
import FluentIcon from '../controls/FluentIcon.vue'
const props = defineProps<{ open: boolean; server?: Server; entry?: DirectoryEntry; catalog?: ServerCatalog; sourceId?: string; sourceName?: string; busy: boolean }>()
const emit = defineEmits<{ close: []; choose: [build: string]; edit: []; remove: []; preset: [value: ServerPreset] }>()
const { t, locale } = useI18n()
const tab = ref('information'), loaded = ref<ServerCatalog>(), loading = ref(false), failure = ref<Error>(), buildId = ref('')
const imageIndex = ref(0), failedImages = ref<string[]>([]), imageOpen = ref(false)
const rosterUnavailable = ref(false)
const navigation = ref<HTMLElement>(), navigationPill = ref({ top: 0, ready: false })
const introductionCopy = ref<HTMLElement>()
const introductionEdges = ref({ top: 1, bottom: 1 })
let navigationResize: ResizeObserver | undefined
let introductionResize: ResizeObserver | undefined
let revision = 0
const name = computed(() => props.server?.name ?? props.entry?.details.name ?? '')
const game = computed(() => props.server?.game ?? props.entry?.details.game ?? '')
const gameDefinition = computed(() => state.games.find(item => item.id === game.value))
const catalog = computed(() => loaded.value ?? props.catalog)
const presentation = computed(() => mergeServerPresentation(props.server?.presentation, catalog.value?.presentation) ?? props.entry?.details.presentation)
const rosterEnabled = computed(() => presentation.value?.rosterEnabled === true && !rosterUnavailable.value)
const builds = computed(() => [...new Set(catalog.value?.instances.map(p => p.build) ?? props.entry?.details.builds ?? props.server?.builds ?? [])])
const buildOptions = computed(() => builds.value.flatMap(id => {
  const build = gameDefinition.value?.builds.find(b => b.id === id)
  return build ? [{ value: id, label: localized(build.name, { dates: true }) }] : []
}))
const versions = computed(() => builds.value.map(id => {
  const build = state.games.find(g => g.id === game.value)?.builds.find(b => b.id === id)
  return { id, label: build ? localized(build.name, { dates: true }) : t('serverInfo.otherBuild'), available: !!build,
    installed: state.instances.some(instance => instance.game === game.value && instance.build === id && instance.clientInstalled === true),
    presets: catalog.value?.instances.filter(item => item.build === id) ?? [] }
}))
const motd = computed(() => serverDescription(presentation.value, locale.value, props.entry?.details.description))
const policy = computed(() => catalog.value?.clientPolicy ?? props.entry?.details.clientPolicy ?? props.server?.clientPolicy)
const recommendations = computed(() => policyPlugins(policy.value))
const hasPlugins = computed(() => recommendations.value.length > 0)
const pluginSources = computed(() => policySources(policy.value))
const pluginCounts = computed(() => {
  const rules = new Map<string, { required: boolean; disabled: boolean }>()
  for (const item of recommendations.value.filter(item => !buildId.value || !item.builds.length || item.builds.includes(buildId.value))) {
    const previous = rules.get(item.id)
    rules.set(item.id, { required: !!item.required || !!previous?.required, disabled: !!item.disabled || !!previous?.disabled })
  }
  return { total: rules.size, required: [...rules.values()].filter(item => item.required && !item.disabled).length,
    recommended: [...rules.values()].filter(item => !item.required && !item.disabled).length, disabled: [...rules.values()].filter(item => item.disabled).length }
})
const images = computed(() => serverGallery(presentation.value, locale.value).map(url => serverImageUrl(url, gameDefinition.value?.assetBase)).filter(url => !failedImages.value.includes(url)))
const image = computed(() => images.value[imageIndex.value % Math.max(1, images.value.length)])
const tabs = computed(() => [{ id: 'information', label: t('serverInfo.information'), icon: 'info' as const }, { id: 'versions', label: t('serverInfo.builds'), icon: 'gamepad' as const }, ...(hasPlugins.value ? [{ id: 'plugins', label: t('serverInfo.plugins'), icon: 'puzzle' as const }] : []), ...(rosterEnabled.value ? [{ id: 'players', label: t('serverInfo.roster'), icon: 'contact' as const }] : [])])
function moveImage(direction: number) { if (images.value.length) imageIndex.value = (imageIndex.value + direction + images.value.length) % images.value.length }
function imageFailed(event: Event) {
  const url = (event.target as HTMLImageElement).getAttribute('src')
  if (url) excludeImage(url)
}
function excludeImage(url: string) { if (!failedImages.value.includes(url)) failedImages.value = [...failedImages.value, url] }
function moveTab(event: KeyboardEvent, direction: number) {
  const index = (tabs.value.findIndex(item => item.id === tab.value) + direction + tabs.value.length) % tabs.value.length
  tab.value = tabs.value[index]!.id
  ;(event.currentTarget as HTMLElement).parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[index]?.focus()
}
function measureNavigation() {
  const selected = navigation.value?.querySelector<HTMLElement>('button[aria-selected="true"]')
  navigationPill.value = selected && props.open
    ? { top: selected.offsetTop + (selected.offsetHeight - 16) / 2, ready: true }
    : { top: navigationPill.value.top, ready: false }
}
function measureIntroductionEdges() {
  const element = introductionCopy.value
  if (!element) { introductionEdges.value = { top: 1, bottom: 1 }; return }
  const remaining = Math.max(0, element.scrollHeight - element.clientHeight - element.scrollTop)
  introductionEdges.value = {
    top: 1 - Math.min(1, Math.max(0, element.scrollTop) / 24),
    bottom: 1 - Math.min(1, remaining / 24),
  }
}
watch(introductionCopy, element => {
  introductionResize?.disconnect()
  measureIntroductionEdges()
  if (element && typeof ResizeObserver !== 'undefined') {
    introductionResize = new ResizeObserver(measureIntroductionEdges)
    introductionResize.observe(element)
  }
}, { flush: 'post' })
watch([motd, loading, tab, hasPlugins, rosterEnabled], measureIntroductionEdges, { flush: 'post' })
watch(navigation, element => {
  navigationResize?.disconnect()
  measureNavigation()
  if (element && typeof ResizeObserver !== 'undefined') {
    navigationResize = new ResizeObserver(measureNavigation)
    navigationResize.observe(element)
  }
}, { flush: 'post' })
watch([tab, tabs, () => props.open], measureNavigation, { flush: 'post' })
watch(images, value => { imageIndex.value = Math.min(imageIndex.value, Math.max(0, value.length - 1)); if (!value.length) imageOpen.value = false })
watch(tabs, items => { if (!items.some(item => item.id === tab.value)) tab.value = 'information' })
watch([() => props.server?.id, () => props.entry?.id, () => props.sourceId, game, () => presentation.value?.rosterEnabled], () => { rosterUnavailable.value = false })
watch(buildOptions, options => { if (!options.some(b => b.value === buildId.value)) buildId.value = options.find(b => b.value === state.selectedBuildId)?.value ?? options[0]?.value ?? '' })
watch([() => props.open, () => props.server?.id, () => props.entry?.id, () => props.sourceId, game], async () => {
  const expected = ++revision
  tab.value = 'information'; loaded.value = undefined; failure.value = undefined; loading.value = false; imageIndex.value = 0; failedImages.value = []; imageOpen.value = false
  buildId.value = buildOptions.value.find(b => b.value === state.selectedBuildId)?.value ?? buildOptions.value[0]?.value ?? ''
  if (!props.open || !props.server) return
  loading.value = true
  try { const value = await call('servers.catalog', { id: props.server.id }); if (expected === revision && props.open) loaded.value = value }
  catch (e) { if (expected === revision && props.open) failure.value = e as Error }
  finally { if (expected === revision) loading.value = false }
}, { immediate: true })
onBeforeUnmount(() => { ++revision; navigationResize?.disconnect(); introductionResize?.disconnect() })
</script>
<template>
  <WDialog :open="open" :title="name" navigation show-dismiss-button dismiss-on-backdrop :dismiss-label="t('titlebar.close')" :close-disabled="busy" @close="emit('close')">
    <div class="server-details-layout">
      <aside class="details-sidebar">
        <div class="sidebar-identity"><span class="sidebar-logo"><ServerIcon :server-id="server?.id" :url="presentation?.iconUrl" :translations="presentation?.iconUrlTranslations" :asset="server?.iconAsset" :room="entry?.kind === 'room' || server?.kind === 'room'" :size="24" /></span><strong :title="name">{{ name }}</strong></div>
        <div ref="navigation" class="details-navigation" role="tablist" aria-orientation="vertical" :aria-label="name">
          <button v-for="item in tabs" :id="`server-detail-tab-${item.id}`" :key="item.id" type="button" role="tab" :aria-selected="tab === item.id" :aria-controls="`server-detail-panel-${item.id}`" :tabindex="tab === item.id ? 0 : -1" :class="{ selected: tab === item.id }" @click="tab = item.id" @keydown.down.prevent="moveTab($event, 1)" @keydown.up.prevent="moveTab($event, -1)"><FluentIcon :glyph="item.icon" :size="18" />{{ item.label }}</button>
          <span class="details-navigation-pill" :class="{ ready: navigationPill.ready }" :style="{ transform: `translateY(${navigationPill.top}px)` }" aria-hidden="true" />
        </div>
        <!-- <WButton v-if="server" variant="subtle" :disabled="busy" @click="emit('edit')"><FluentIcon glyph="edit" />{{ t('service.editServer') }}</WButton> -->
        <div v-if="server && !server.builtIn" class="sidebar-manage"><WButton variant="subtle" :disabled="busy" @click="emit('remove')"><FluentIcon glyph="delete" />{{ t('service.remove') }}</WButton></div>
      </aside>
      <div class="details-main">
        <header class="details-header">
          <h2 :title="tab === 'information' ? name : undefined">{{ tab === 'information' ? name : tabs.find(item => item.id === tab)?.label }}</h2>
          <WButton v-if="server && tab === 'information'" variant="subtle" class="details-edit" :disabled="busy" :aria-label="t('service.editServer')" :title="t('service.editServer')" @click="emit('edit')"><FluentIcon glyph="edit" :size="18" /></WButton>
          <div class="server-status"><span v-if="presentation?.state && presentation.state !== 'online'" :class="presentation.state">{{ t(`serverInfo.${presentation.state}`) }}</span><span v-if="presentation?.whitelist"><FluentIcon glyph="blocked" :size="12" />{{ t('serverInfo.whitelist') }}</span></div>
        </header>
        <div class="details-body"><Transition name="detail-panel" mode="out-in"><div :id="`server-detail-panel-${tab}`" :key="tab" class="details-scroll" :class="{ information: tab === 'information', players: tab === 'players' }" role="tabpanel" :aria-labelledby="`server-detail-tab-${tab}`">
        <template v-if="tab === 'information'"><p v-if="loading && !catalog" class="muted">{{ t('live.working') }}</p><ServerFailureNotice v-if="failure" :error="failure" />
          <div class="server-overview" :class="{ 'has-gallery': image }">
          <div v-if="image" class="server-gallery">
            <button type="button" class="gallery-stage" :aria-label="t('serverInfo.enlargeImage')" @click="imageOpen = true"><Transition name="gallery-image" mode="out-in"><img :key="image" :src="image" :alt="t('serverInfo.imageAlt', { name, index: imageIndex + 1 })" referrerpolicy="no-referrer" @error="imageFailed" /></Transition><span class="gallery-zoom"><FluentIcon glyph="openInNewWindow" :size="14" /></span><span v-if="images.length > 1" class="gallery-position" aria-live="polite">{{ imageIndex + 1 }} / {{ images.length }}</span></button>
            <div v-if="images.length > 1" class="gallery-controls"><WButton variant="subtle" :aria-label="t('serverInfo.previousImage')" @click="moveImage(-1)"><FluentIcon glyph="chevronLeft" /></WButton><div class="gallery-thumbnails"><button v-for="(url,index) in images" :key="url" type="button" :aria-label="t('serverInfo.showImage', { index: index + 1 })" :aria-pressed="imageIndex === index" @click="imageIndex = index"><img :src="url" alt="" referrerpolicy="no-referrer" @error="imageFailed" /></button></div><WButton variant="subtle" :aria-label="t('serverInfo.nextImage')" @click="moveImage(1)"><FluentIcon glyph="chevronRight" /></WButton></div>
          </div>
          <section v-else class="server-identity-board" :style="{ '--server-accent': gameDefinition?.accent || 'var(--accent-default)' }">
            <!-- The server identity stands on its own without repeating the game name. -->
            <div class="identity-center"><span class="identity-emblem"><ServerIcon :server-id="server?.id" :url="presentation?.iconUrl" :translations="presentation?.iconUrlTranslations" :asset="server?.iconAsset" :room="entry?.kind === 'room' || server?.kind === 'room'" :size="64" /></span><div class="identity-title"><strong :title="name">{{ name }}</strong><span v-if="presentation?.state && presentation.state !== 'online'" class="identity-state" :class="presentation.state">{{ t(`serverInfo.${presentation.state}`) }}</span></div></div>
            <dl class="identity-facts"><div><dt>{{ t('serverInfo.builds') }}</dt><dd>{{ versions.length }}</dd></div><div v-if="hasPlugins"><dt>{{ t('serverInfo.plugins') }}</dt><dd>{{ pluginCounts.total }}</dd></div></dl>
          </section>
          <div class="overview-story"><dl class="server-metrics">
            <div v-if="presentation?.onlinePlayers != null && presentation.onlinePlayers >= 0"><dt :title="presentation?.presenceComplete === false ? t('serverInfo.populationIncomplete') : undefined">{{ t('serverInfo.onlinePlayers') }}</dt><dd><FluentIcon glyph="contact" :size="18" />{{ presentation.presenceComplete === false ? '≥ ' : '' }}{{ presentation.onlinePlayers }}</dd></div>
            <div v-if="presentation?.peakOnlinePlayers != null && presentation.peakOnlinePlayers >= 0"><dt>{{ t('serverInfo.peakOnlinePlayers') }}</dt><dd><TrendIcon :size="18" />{{ presentation.peakOnlinePlayers }}</dd></div>
            <div v-if="presentation?.playerLimit != null"><dt>{{ t('serverInfo.playerLimit') }}</dt><dd>{{ presentation.playerLimit }}</dd></div>
          </dl>
          <section class="introduction"><h3>{{ t('serverInfo.introduction') }}</h3><div ref="introductionCopy" class="introduction-copy" :style="{ '--introduction-top-opacity': introductionEdges.top, '--introduction-bottom-opacity': introductionEdges.bottom }" @scroll.passive="measureIntroductionEdges" tabindex="0" role="region" :aria-label="t('serverInfo.introduction')"><p v-if="motd" class="server-description">{{ motd }}</p><p v-else-if="!loading" class="muted">{{ t('serverInfo.noIntroduction') }}</p></div></section>
          <div class="overview-shortcuts">
            <button type="button" class="overview-shortcut" @click="tab = 'versions'"><span class="shortcut-icon"><FluentIcon glyph="gamepad" :size="20" /></span><span class="shortcut-text"><strong>{{ t('serverInfo.builds') }}</strong></span><span class="shortcut-count">{{ versions.length }}</span><FluentIcon glyph="chevronRight" :size="12" /></button>
            <button v-if="hasPlugins" type="button" class="overview-shortcut" @click="tab = 'plugins'"><span class="shortcut-icon"><FluentIcon glyph="puzzle" :size="20" /></span><span class="shortcut-text"><strong>{{ t('serverInfo.plugins') }}</strong></span><span class="shortcut-count">{{ pluginCounts.total }}</span><FluentIcon glyph="chevronRight" :size="12" /></button>
            <button v-if="rosterEnabled" type="button" class="overview-shortcut" @click="tab = 'players'"><span class="shortcut-icon"><FluentIcon glyph="contact" :size="20" /></span><span class="shortcut-text"><strong>{{ t('serverInfo.roster') }}</strong></span><span v-if="presentation?.onlinePlayers != null && presentation.onlinePlayers >= 0" class="shortcut-count">{{ presentation.presenceComplete === false ? '≥ ' : '' }}{{ presentation.onlinePlayers }}</span><FluentIcon glyph="chevronRight" :size="12" /></button>
          </div>
          </div>
          </div>
        </template>
        <section v-else-if="tab === 'versions'" class="supported-versions" :aria-label="t('serverInfo.builds')">
          <article v-for="version in versions" :key="version.id" class="supported-version" :class="{ selected: buildId === version.id }">
            <button type="button" class="version-selector" :aria-pressed="buildId === version.id" :disabled="busy || !version.available" @click="buildId = version.id"><span class="version-icon"><FluentIcon glyph="gamepad" :size="22" /></span><span class="version-label">{{ version.label }}</span><span class="version-selected" :aria-hidden="buildId !== version.id"><FluentIcon glyph="checkMark" :size="14" />{{ t('live.selected') }}</span></button>
            <div v-if="server && !version.installed && version.presets.length" class="version-actions"><div v-for="preset in version.presets" :key="preset.id"><span v-if="version.presets.length > 1" class="preset-name">{{ localized(preset.name, { dates: true }) }}</span><WButton variant="subtle" :disabled="busy || !inHost || !version.available" @click="emit('preset', preset)"><FluentIcon glyph="add" :size="12" />{{ t('service.importPreset') }}</WButton></div></div>
          </article>
          <div v-if="!versions.length" class="versions-empty"><FluentIcon glyph="gamepad" :size="36" /><p>{{ t(loading ? 'live.working' : 'serverInfo.unknownBuilds') }}</p></div>
        </section>
        <ServerPluginListPanel v-else-if="tab === 'plugins' && hasPlugins" :active="open" :game="game" :builds="builds" :recommendations="recommendations" :sources="pluginSources" :server-id="server?.id" />
        <OnlinePlayersPanel v-else-if="tab === 'players' && rosterEnabled" :active="open" :game="game" :server-id="server?.id" :source-id="sourceId" :entry-id="entry?.id" @availability="rosterUnavailable = !$event" />
      </div></Transition></div><footer class="details-footer"><div v-if="buildOptions.length" class="client-choice"><span class="client-choice-label">{{ t('hosting.version') }}</span><WComboBox v-model="buildId" :options="buildOptions" :aria-label="t('hosting.version')" :disabled="busy" /></div><WButton variant="accent" :disabled="busy || !inHost || !server && !buildId" @click="emit('choose', buildId)">{{ t(entry?.kind === 'room' || server?.kind === 'room' ? 'multiplayer.join' : 'multiplayer.connect') }}</WButton></footer></div>
    </div>
    <ImageViewer :open="open && imageOpen" v-model:index="imageIndex" :images="images" :title="name" @close="imageOpen = false" @failed="excludeImage" />
  </WDialog>
</template>
<style scoped>
.server-details-layout { display: grid; grid-template-columns: 184px minmax(0,1fr); height: min(640px,calc(100dvh - 80px)); min-height: 0; }
.details-sidebar { display: flex; flex-direction: column; gap: 24px; background: var(--solid-bg-base); border-right: 1px solid var(--card-stroke-default); padding: 28px 12px 16px; min-width: 0; }
.sidebar-identity { flex: none; display: flex; align-items: center; gap: 10px; padding: 0 10px; }
.sidebar-logo { display: grid; place-items: center; width: 38px; height: 38px; flex: none; border-radius: 8px; color: var(--accent-default); background: var(--subtle-secondary); }
.sidebar-identity strong { min-width: 0; flex: 1; font-size: 13px; line-height: 1.5; font-weight: 600; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow-wrap: anywhere; }
.details-navigation { position: relative; display: grid; gap: 4px; }
.details-navigation button { display: flex; align-items: center; gap: 12px; text-align: start; position: relative; min-height: 42px; padding: 10px 14px; border-radius: 5px; font-size: 13px; transition: background-color 140ms linear; }
.details-navigation button:hover, .details-navigation button.selected { background: var(--subtle-secondary); }
.details-navigation-pill { position: absolute; top: 0; left: 0; width: 3px; height: 16px; background: var(--accent-default); border-radius: 2px; pointer-events: none; opacity: 0; transition: transform var(--duration-fast) var(--ease-fast-out-slow-in),opacity var(--duration-faster) linear; }.details-navigation-pill.ready { opacity: 1; }
.sidebar-manage { margin-top: auto; display: grid; gap: 6px; }.sidebar-manage .w-btn { justify-content: flex-start; font-size: 12px; }
.details-main { display: flex; flex-direction: column; min-width: 0; min-height: 0; }
.details-body { flex: 1; min-height: 0; display: flex; overflow: hidden; }
.details-header { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; padding: 30px 52px 22px 30px; flex: none; }
.details-header h2 { margin: 0; min-width: 0; max-width: 100%; font-size: 23px; line-height: 32px; font-weight: 600; overflow-wrap: anywhere; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
.details-edit.w-btn { flex: none; width: 30px; min-width: 30px; height: 30px; padding: 5px; color: var(--text-secondary); }
.details-scroll { flex: 1; min-height: 0; overflow-y: auto; scrollbar-gutter: stable; padding: 0 30px 24px; }
.details-scroll.information { display: flex; flex-direction: column; overflow: hidden; scrollbar-gutter: auto; }
.details-scroll.players { overflow: hidden; }
.server-overview { display: grid; grid-template-columns: minmax(0,.9fr) minmax(0,1fr); grid-template-areas: 'gallery story'; grid-template-rows: minmax(0,1fr); column-gap: 28px; flex: 1; min-height: 0; }
.overview-story { grid-area: story; display: flex; flex-direction: column; min-width: 0; min-height: 0; overflow: hidden; }.introduction { display: flex; flex-direction: column; flex: 1; min-height: 0; }.introduction h3 { flex: none; margin: 0 0 12px; font-size: 14px; font-weight: 600; }.introduction-copy { flex: 1; min-height: 0; overflow-y: auto; scrollbar-gutter: stable; padding-right: 8px; }.server-description { white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.85; font-size: 14px; margin: 0; color: var(--text-secondary); }
.server-status { display: flex; flex-wrap: wrap; gap: 8px; }
.server-status > span { display: inline-flex; align-items: center; gap: 5px; padding: 2px 8px; border-radius: 4px; font-size: 12px; line-height: 18px; color: var(--text-secondary); background: var(--subtle-secondary); }
.server-status .online { color: var(--fill-success); background: color-mix(in srgb,var(--fill-success) 8%,transparent); }
.server-status .maintenance { color: var(--fill-caution); background: color-mix(in srgb,var(--fill-caution) 8%,transparent); }
.server-identity-board { grid-area: gallery; position: relative; isolation: isolate; overflow: hidden; min-width: 0; min-height: 0; display: flex; flex-direction: column; padding: 24px; border: 1px solid var(--card-stroke-default); border-radius: 8px; background: radial-gradient(ellipse at 50% 36%,color-mix(in srgb,var(--server-accent) 9%,transparent),transparent 72%),var(--card-bg-secondary); }
.server-identity-board::before, .server-identity-board::after { content: ''; position: absolute; z-index: -1; width: 280px; height: 280px; left: 50%; top: 42%; transform: translate(-50%,-50%); border-radius: 50%; border: 1px solid color-mix(in srgb,var(--server-accent) 9%,transparent); pointer-events: none; }
.server-identity-board::after { width: 380px; height: 380px; border-color: color-mix(in srgb,var(--server-accent) 5%,transparent); }
.identity-center { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 18px; text-align: center; flex: 1; min-height: 0; padding: 24px 0; }
.identity-emblem { display: grid; place-items: center; width: 104px; height: 104px; flex: none; border-radius: 24px; color: var(--server-accent); background: var(--card-bg-default); border: 1px solid var(--card-stroke-default); box-shadow: 0 6px 24px color-mix(in srgb,var(--server-accent) 8%,transparent); }
.identity-title { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 10px; max-width: 100%; min-width: 0; }.identity-title > strong { min-width: 0; max-width: 100%; font-size: 22px; line-height: 1.4; font-weight: 600; overflow-wrap: anywhere; display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden; }
.identity-state { flex: none; padding: 2px 6px; border-radius: 4px; font-size: 12px; line-height: 18px; color: var(--text-secondary); background: var(--subtle-secondary); }.identity-state.maintenance { color: var(--fill-caution); background: color-mix(in srgb,var(--fill-caution) 8%,transparent); }
.identity-facts { display: flex; margin: 0; padding-top: 20px; border-top: 1px solid var(--card-stroke-default); flex: none; }.identity-facts > div { display: flex; flex: 1; flex-direction: column; gap: 5px; align-items: center; }.identity-facts > div + div { border-left: 1px solid var(--card-stroke-default); }.identity-facts dt { font-size: 12px; color: var(--text-secondary); }.identity-facts dd { order: -1; margin: 0; font-size: 24px; line-height: 32px; font-weight: 500; font-variant-numeric: tabular-nums; }
.overview-shortcuts { display: grid; flex: none; gap: 10px; margin-top: 16px; border-top: 1px solid var(--stroke-divider); padding-top: 16px; }.overview-shortcuts:empty { display: none; }.overview-shortcut { display: flex; align-items: center; gap: 12px; min-width: 0; width: 100%; height: 54px; padding: 12px 14px; border: 1px solid var(--card-stroke-default); border-radius: 5px; background: var(--card-bg-default); text-align: start; transition: background-color 140ms linear; }.overview-shortcut:hover { background: var(--subtle-secondary); }.overview-shortcut:active { background: var(--subtle-tertiary); }.shortcut-icon { display: grid; place-items: center; width: 28px; flex: none; color: var(--text-secondary); }.shortcut-text { display: grid; gap: 4px; min-width: 0; flex: 1; }.shortcut-text strong { font-size: 13px; font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }.shortcut-count { font-size: 16px; font-variant-numeric: tabular-nums; color: var(--text-secondary); }.overview-shortcut > :deep(.fluent-icon) { flex: none; color: var(--text-secondary); }
.server-gallery { grid-area: gallery; align-self: stretch; display: flex; flex-direction: column; min-width: 0; min-height: 0; gap: 12px; }
.gallery-stage { position: relative; display: grid; place-items: center; flex: 1; min-height: 0; width: 100%; overflow: hidden; padding: 0; border-radius: 8px; background: var(--solid-bg-base); border: 1px solid var(--card-stroke-default); cursor: zoom-in; }.gallery-stage > img { display: block; position: absolute; inset: 0; width: 100%; height: 100%; min-height: 0; object-fit: contain; }.gallery-zoom { position: absolute; top: 12px; right: 12px; display: grid; place-items: center; width: 28px; height: 28px; border-radius: 5px; color: var(--text-primary); background: var(--card-bg-default); opacity: 0; transition: opacity 140ms linear; }.gallery-stage:hover .gallery-zoom, .gallery-stage:focus-visible .gallery-zoom { opacity: 1; }
.gallery-position { position: absolute; right: 12px; bottom: 12px; padding: 3px 8px; border-radius: 10px; background: rgb(0 0 0 / 48%); color: white; font-size: 11px; font-variant-numeric: tabular-nums; }
.gallery-controls { display: flex; flex: none; align-items: center; gap: 8px; color: var(--text-secondary); }.gallery-controls .w-btn { flex: none; min-width: 30px; padding-inline: 6px; }.gallery-thumbnails { display: flex; justify-content: safe center; gap: 8px; flex: 1; min-width: 0; overflow-x: auto; padding: 3px; scrollbar-width: thin; }.gallery-thumbnails button { flex: none; position: relative; width: 72px; height: 54px; border-radius: 5px; overflow: hidden; background: var(--solid-bg-base); opacity: .65; border: 2px solid transparent; transition: opacity 160ms linear,border-color 160ms linear; }.gallery-thumbnails button:hover, .gallery-thumbnails button[aria-pressed='true'] { opacity: 1; }.gallery-thumbnails button[aria-pressed='true'] { border-color: var(--accent-default); }.gallery-thumbnails img { display: block; width: 100%; height: 100%; object-fit: contain; }
.server-metrics { display: flex; flex: none; flex-wrap: wrap; gap: 20px 28px; margin: 0 0 26px; padding: 0 0 22px; border-bottom: 1px solid var(--stroke-divider); }
.server-metrics > div { display: flex; flex-direction: column; gap: 6px; }.server-metrics dt { font-size: 12px; color: var(--text-secondary); }.server-metrics dd { order: -1; display: flex; align-items: center; gap: 9px; margin: 0; font-size: 23px; line-height: 30px; font-weight: 500; font-variant-numeric: tabular-nums; }.server-metrics dd :deep(.fluent-icon) { color: var(--text-secondary); }.server-metrics:empty { display: none; }
.supported-versions { display: grid; gap: 10px; align-content: start; min-height: 100%; }.supported-version { display: flex; align-items: center; min-width: 0; border: 1px solid var(--card-stroke-default); border-radius: 7px; background: var(--card-bg-default); overflow: hidden; transition: background-color 200ms linear, border-color 200ms linear; }.supported-version.selected { background: var(--subtle-secondary); }.version-selector { display: flex; flex: 1; min-width: 0; align-items: center; gap: 16px; padding: 16px 18px; text-align: start; transition: background-color 140ms linear; }.version-selector:hover:not(:disabled) { background: var(--subtle-secondary); }.version-selector:disabled { color: var(--text-disabled); }.version-icon { display: grid; place-items: center; width: 38px; height: 38px; flex: none; border-radius: 8px; color: var(--accent-default); background: var(--subtle-secondary); }.version-label { min-width: 0; flex: 1; font-size: 14px; line-height: 22px; overflow-wrap: anywhere; }.version-selected { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; color: var(--accent-default); flex: none; opacity: 0; visibility: hidden; transition: opacity 180ms linear, visibility 0s 180ms; }.supported-version.selected .version-selected { opacity: 1; visibility: visible; transition-delay: 0s; }.version-actions { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 4px 16px; max-width: 45%; padding: 6px 12px; border-left: 1px solid var(--stroke-divider); }.version-actions > div { display: flex; align-items: center; gap: 8px; }.version-actions .w-btn { font-size: 12px; color: var(--accent-default); }.version-actions .w-btn:disabled { color: var(--text-disabled); }.preset-name { font-size: 12px; color: var(--text-secondary); }.versions-empty { display: flex; flex-direction: column; justify-content: center; align-items: center; align-self: stretch; min-height: 320px; padding: 24px; gap: 18px; color: var(--text-secondary); text-align: center; }.versions-empty p { margin: 0; line-height: 1.8; font-size: 14px; }
.details-footer { display: flex; gap: 20px; align-items: center; padding: 16px 30px; border-top: 1px solid var(--card-stroke-default); flex: none; background: var(--solid-bg-base); }.client-choice { display: flex; gap: 12px; align-items: center; min-width: 0; flex: 1; }.client-choice-label { font-size: 13px; color: var(--text-secondary); white-space: nowrap; flex: none; }.client-choice :deep(.w-flyout-anchor) { flex: 1; min-width: 0; max-width: 300px; }.details-footer > .w-btn { margin-left: auto; min-width: 112px; flex: none; }
.detail-panel-enter-active { transition: opacity 180ms ease-out,transform 220ms var(--ease-fast-out-slow-in); }.detail-panel-leave-active { transition: opacity 90ms ease-in; }.detail-panel-enter-from { opacity: 0; transform: translateY(7px); }.detail-panel-leave-to { opacity: 0; }.gallery-image-enter-active { transition: opacity 180ms linear; }.gallery-image-leave-active { transition: opacity 90ms linear; }.gallery-image-enter-from, .gallery-image-leave-to { opacity: 0; }
@media(max-width:900px) { .details-scroll.information { overflow-y: auto; display: block; scrollbar-gutter: stable; }.server-overview, .server-overview.has-gallery { grid-template-columns: 1fr; grid-template-rows: auto; }.server-overview { grid-template-areas: 'gallery' 'story'; }.overview-story { overflow: visible; }.introduction-copy { flex: auto; max-height: 320px; }.gallery-stage { flex: none; height: 240px; }.server-identity-board { min-height: 320px; margin-bottom: 24px; }.server-gallery { margin-bottom: 24px; } }
@media(max-width:720px) { .server-details-layout { grid-template-columns: 156px minmax(0,1fr); }.details-sidebar { padding-inline: 8px; }.sidebar-identity { padding-inline: 6px; flex-direction: column; align-items: flex-start; }.details-navigation button { gap: 8px; padding-inline: 10px; }.details-header { padding-left: 20px; }.details-scroll { padding-inline: 20px; }.details-footer { padding-inline: 20px; }.server-metrics { gap: 16px 24px; } }
@media(max-width:600px) { .details-footer { gap: 12px; padding: 12px 16px; }.client-choice-label { display: none; }.details-footer > .w-btn { min-width: 90px; } }
@media(max-width:520px) {
  .server-details-layout { grid-template-columns: 1fr; grid-template-rows: auto minmax(0,1fr); }
  .details-sidebar { padding: 14px 10px 8px; gap: 8px; border-right: 0; border-bottom: 1px solid var(--stroke-divider); }
  .sidebar-identity, .sidebar-manage { display: none; }
  .details-navigation { display: flex; overflow-x: auto; gap: 2px; }
  .details-navigation button { flex: 0 0 auto; min-height: 36px; gap: 6px; padding: 8px; font-size: 12px; }
  .details-navigation-pill { display: none; }
  .details-navigation button.selected { box-shadow: inset 0 -2px var(--accent-default); }
  .details-header { padding: 16px 42px 12px 16px; gap: 8px; }
  .details-header h2 { font-size: 18px; line-height: 26px; }
  .details-scroll { padding: 0 16px 16px; }
  .version-selector { gap: 10px; padding: 12px; }
  .version-selected { font-size: 11px; gap: 4px; }
  .version-actions { max-width: none; border-left: 0; border-top: 1px solid var(--stroke-divider); }
  .supported-version { flex-wrap: wrap; }
}
@media(max-height:720px) and (min-width:901px) { .identity-emblem { width: 80px; height: 80px; border-radius: 18px; }.identity-center { gap: 12px; padding-block: 16px; }.identity-title > strong { font-size: 19px; }.identity-facts { padding-top: 14px; } }
@media(prefers-reduced-motion:reduce) { .server-details-layout, .details-navigation-pill, .supported-version, .version-selector, .version-selected, .detail-panel-enter-active, .detail-panel-leave-active, .gallery-image-enter-active, .gallery-image-leave-active { transition: none; }.detail-panel-enter-from { transform: none; } }
.introduction-copy {
  -webkit-mask-image: linear-gradient(to bottom, rgb(0 0 0 / var(--introduction-top-opacity,1)) 0, #000 min(24px,25%), #000 calc(100% - min(24px,25%)), rgb(0 0 0 / var(--introduction-bottom-opacity,1)) 100%);
  mask-image: linear-gradient(to bottom, rgb(0 0 0 / var(--introduction-top-opacity,1)) 0, #000 min(24px,25%), #000 calc(100% - min(24px,25%)), rgb(0 0 0 / var(--introduction-bottom-opacity,1)) 100%);
}
@media (prefers-reduced-transparency: reduce), (forced-colors: active) { .introduction-copy { -webkit-mask-image: none; mask-image: none; } }
</style>

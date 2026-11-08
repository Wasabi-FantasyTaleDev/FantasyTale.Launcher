<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { call, inHost } from '../bridge'
import { localized } from '../i18n'
import { state } from '../state'
import { downloads } from '../downloads/live'
import { formatBytes } from '../downloads/model'
import type { StorePackage } from '../store/types'
import { previewStoreCatalog, previewStoreSources, previewStorePlugins, previewStoreUpdateCount } from '../store/preview'
import { storeSession } from '../store/session'
import { groupStorePackages } from '../store/groups'
import { pluginUpdateFrom, storePluginUpdates } from '../store/updates'
import { liveStoreSession, canInstallStorePlugin } from '../store/background'
import PageBody from '../components/PageBody.vue'
import WContentTransition from '../controls/WContentTransition.vue'
import StoreSourcesDialog from '../components/StoreSourcesDialog.vue'
import StoreInstallDialog from '../components/StoreInstallDialog.vue'
import StorePluginDialog from '../components/StorePluginDialog.vue'
import StorePluginUpdatesDialog from '../components/StorePluginUpdatesDialog.vue'
import StorePackageVersion from '../components/StorePackageVersion.vue'
import StoreServerDialog from '../components/StoreServerDialog.vue'
import WButton from '../controls/WButton.vue'
import WComboBox from '../controls/WComboBox.vue'
import WSelectorBar from '../controls/WSelectorBar.vue'
import WTextBox from '../controls/WTextBox.vue'
import FluentIcon from '../controls/FluentIcon.vue'
import WProgressRing from '../controls/WProgressRing.vue'

const { t, locale } = useI18n(), route = useRoute()
const manage = ref(false), loadError = computed(() => session.value.state.loadError), category = ref(route.query.kind === 'plugin' ? 'plugin' : 'client'), sourceFilter = ref(''), query = ref(''), selected = ref(''), version = ref('')
const refreshPreview = computed(() => import.meta.env.DEV && !inHost && route.query.storeRefreshPreview === '1')
const preview = computed(() => import.meta.env.DEV && !inHost && (route.query.storePreview === '1' || refreshPreview.value))
const session = computed(() => {
  const demo = preview.value, animated = refreshPreview.value, more = route.query.morePlugins === '1'
  if (!demo) return liveStoreSession
  let previewRevision = 0
  return storeSession(demo ? `preview:${animated}:${more}` : inHost ? 'desktop' : 'browser', {
    sources: async () => demo ? previewStoreSources() : await call('store.sources'),
    catalog: async (source, force) => {
      if (!demo) return await call('store.catalog', { id: source.id, force })
      if (animated) await new Promise(resolve => setTimeout(resolve, force ? 6000 : 1000))
      const catalog = previewStoreCatalog(source.id, more || animated && force)
      if (animated && force && source.builtIn) {
        previewRevision++
        const item = catalog.packages.find(item => item.kind === 'plugin')!
        catalog.packages.unshift({ ...item, id: 'preview.new-arrival',
          name: { en: 'New arrival', 'zh-CN': '新上架插件', 'zh-TW': '新上架外掛' },
          version: `1.${previewRevision}.0`, manifestSha256: 'a'.repeat(64) })
      }
      return catalog
    },
    plugins: async () => demo ? previewStorePlugins(more) : await call('plugins.list'),
    error: error => inHost ? (error as Error).message : t('store.browserOnly'),
  })
})
const sources = computed(() => session.value.state.sources), catalogs = computed(() => session.value.state.catalogs)
const errors = computed(() => session.value.state.errors), loading = computed(() => session.value.state.loading)
const localPlugins = computed(() => session.value.state.localPlugins), inventoryError = computed(() => session.value.state.inventoryError)
const inventoryLoading = computed(() => session.value.state.inventoryLoading && !session.value.state.inventoryLoaded)
const initialLoading = computed(() => loading.value && !session.value.state.loaded)
const refreshing = computed(() => session.value.state.loaded && (loading.value || session.value.state.inventoryLoading))
const installOpen = ref(false), updateFilter = ref(route.query.updates === '1' ? 'updates' : 'all'), updateAllOpen = ref(false)
const pendingPackage = ref('')
function refreshInventory() { return session.value.refreshInventory() }
function refresh(force = false) { return session.value.refresh(force) }
watch(session, () => { if (!session.value.state.loading) void refresh() }, { immediate: true })
const catalogGroups = computed(() => {
  const term = query.value.trim().toLocaleLowerCase()
  return groupStorePackages(sources.value, catalogs.value, category.value, sourceFilter.value).filter(group => !term || group.offers.some(offer =>
    offer.releases.some(item => [localized(item.name), item.id, gameName(item.game), ...item.builds.map(buildName)].join(' ').toLocaleLowerCase().includes(term))))
})
const canInstallPlugin = canInstallStorePlugin
const isInstalled = (item: StorePackage) => item.kind === 'plugin' && localPlugins.value.some(plugin => plugin.game === item.game && plugin.id === item.id)
const updates = computed(() => storePluginUpdates(catalogGroups.value, localPlugins.value, canInstallPlugin))
watch([preview, updates], () => { if (preview.value) previewStoreUpdateCount.value = updates.value.length }, { immediate: true })
onBeforeUnmount(() => { if (preview.value) previewStoreUpdateCount.value = undefined })
const groups = computed(() => category.value === 'plugin' ? catalogGroups.value.flatMap(group => {
  const update = updates.value.find(update => update.key === group.key)
  const offeredReleases = update ? update.offers.find(offer => offer.source.id === update.source.id)!.releases : group.releases
  const releases = update ? [update.item, ...offeredReleases.filter(item => item !== update.item)] : offeredReleases
  if (updateFilter.value === 'updates') return update ? [{ ...group, source: update.source,
    releases: releases.filter(item => item.launcherCompatible !== false && canInstallPlugin(item) && pluginUpdateFrom(item, localPlugins.value)) }] : []
  return [{ ...group, source: update?.source ?? group.source, releases }]
}).sort((left, right) => Number(!!pluginUpdateFrom(right.releases[0]!, localPlugins.value)) - Number(!!pluginUpdateFrom(left.releases[0]!, localPlugins.value))) : catalogGroups.value)
const currentGroup = computed(() => groups.value.find(group => group.key === selected.value))
const current = computed(() => currentGroup.value?.releases.find(item => item.manifestSha256 === version.value) ?? currentGroup.value?.releases[0])
const currentUpdateFrom = computed(() => current.value && pluginUpdateFrom(current.value, localPlugins.value))
const currentUpToDate = computed(() => !!current.value && isInstalled(current.value) && !currentUpdateFrom.value)
watch(() => downloads.tasks.filter(task => task.target.kind === 'plugin' && task.stage === 'completed').map(task => task.id).join(','),
  () => { void refreshInventory() })
watch(category, () => { updateFilter.value = 'all' })
const installable = computed(() => current.value?.kind === 'client'
  ? state.games.some(game => game.id === current.value?.game && game.status === 'enabled' && game.builds.some(build => build.id === current.value?.builds[0]))
  : current.value?.kind === 'server'
    ? state.games.some(game => game.id === current.value?.game && game.status === 'enabled' && game.builds.some(build => build.supportsHosting && current.value?.builds.includes(build.id)))
  : current.value?.kind === 'plugin' && state.games.some(game => game.id === current.value?.game && game.status === 'enabled' && game.supportsPlugins)
    && state.games.some(game => game.id === current.value?.game && game.builds.some(build => !current.value?.builds.length || current.value.builds.includes(build.id))))
const unavailableReason = computed(() => {
  if (!(inHost || preview.value)) return t('store.browserOnly')
  const game = state.games.find(item => item.id === current.value?.game)
  if (!game || game.status !== 'enabled') return t('clientLibrary.gameUnavailable')
  if (current.value?.kind === 'plugin' && !game.supportsPlugins) return t('clientLibrary.pluginsUnsupported')
  if (!installable.value) return t(current.value?.kind === 'server' ? 'featureAvailability.development' : 'clientLibrary.buildUnsupported')
  return ''
})
watch(() => route.query.package, target => {
  if (typeof target !== 'string' || !target) return
  pendingPackage.value = target
  category.value = 'plugin'; sourceFilter.value = ''; query.value = target; updateFilter.value = 'all'
}, { immediate: true })
watch(groups, values => {
  const requested = values.find(item => item.releases[0]?.id === pendingPackage.value)
  if (requested) { selected.value = requested.key; pendingPackage.value = '' }
  else if (!values.some(item => item.key === selected.value)) selected.value = values[0]?.key ?? ''
}, { immediate: true })
watch(selected, () => { version.value = '' })
const sourceOptions = computed(() => [{ value: '', label: t('store.allSources') }, ...sources.value.filter(s => s.enabled).map(s => ({ value: s.id, label: s.name }))])
const notices = computed(() => sources.value.filter(s => s.enabled && (!sourceFilter.value || s.id === sourceFilter.value)).flatMap(s => {
  if (errors.value[s.id]) return [{ source: s, message: errors.value[s.id]! }]
  if (!s.baseUrl && !(preview.value && s.builtIn)) return [{ source: s, message: t('store.missing') }]
  return []
}))
function packageAuthor(item: StorePackage) { return item.authors?.filter(name => name.trim()).join(', ') || item.publisher?.trim() || 'Fantasy Tale Studio' }
function gameName(id: string) { return localized(state.games.find(game => game.id === id)?.name) || id }
function buildName(id: string) { return localized(state.games.flatMap(game => game.builds).find(build => build.id === id)?.name, { dates: true }) || id }
function size(bytes: number) { return formatBytes(bytes, locale.value) }
function accent(game: string) { return state.games.find(item => item.id === game)?.accent ?? 'var(--accent-default)' }
</script>

<template>
  <section class="page store-page">
    <header class="page-header"><div><h1 class="page-title">{{ t('store.title') }}</h1><p class="page-desc">{{ t('store.desc') }}</p></div><div class="store-header-actions"><WButton variant="subtle" :disabled="loading" :aria-label="t('store.refresh')" @click="refresh(true)"><FluentIcon glyph="refresh" /></WButton><WButton @click="manage = true"><FluentIcon glyph="globe" />{{ t('store.manage') }}</WButton></div></header>
    <PageBody contained>
      <div class="store-toolbar"><WSelectorBar v-model="category" :items="[{ key: 'client', label: t('store.clients') }, { key: 'plugin', label: t('store.plugins') }, { key: 'server', label: t('serverPackages.title') }]" /><div class="store-filters"><WComboBox v-model="sourceFilter" :aria-label="t('store.source')" :options="sourceOptions" /><WTextBox v-model="query" :placeholder="t('store.search')" :aria-label="t('store.search')" :maxlength="128" /></div></div>
      <p v-if="loadError || inventoryError" class="catalog-error" role="alert">{{ loadError || inventoryError }}</p>
      <div class="store-workspace">
        <section class="store-browser surface" :aria-busy="initialLoading">
          <div class="store-list-heading">
            <Transition name="store-heading" mode="out-in">
              <div v-if="category === 'plugin'" key="plugin" class="store-heading-content">
                <WSelectorBar v-model="updateFilter" :items="[{ key: 'all', label: t('store.all') }, { key: 'updates', label: t('store.updates'), badge: updates.length, badgeLabel: t('store.updateCount', { count: updates.length }) }]" />
                <Transition name="store-update-action"><WButton v-if="updateFilter === 'updates'" variant="accent" :disabled="initialLoading || inventoryLoading || !updates.length" @click="updateAllOpen = true"><FluentIcon glyph="download" />{{ t('store.updateAll') }}</WButton></Transition>
              </div>
              <div v-else key="count" class="store-heading-content"><span v-if="!initialLoading && groups.length" class="store-status" role="status" aria-live="polite">{{ t('store.packageCount', { count: groups.length }) }}</span><span v-if="sourceFilter && !initialLoading && groups.length">{{ sources.find(s => s.id === sourceFilter)?.name }}</span></div>
            </Transition>
            <Transition name="store-refresh"><span v-if="refreshing" class="store-refresh-status" role="status"><WProgressRing :size="16" aria-hidden="true" /><span>{{ t('store.refreshing') }}</span></span></Transition>
          </div>
          <div v-if="notices.length" class="source-notices"><div v-for="notice in notices" :key="notice.source.id" class="source-notice"><FluentIcon glyph="info" /><div><strong>{{ notice.source.name }}</strong><span>{{ notice.message }}</span></div><WButton variant="subtle" @click="manage = true">{{ t('store.configure') }}</WButton></div></div>
          <WContentTransition><div :key="`${category}:${updateFilter}`" class="store-grid-viewport">
            <TransitionGroup name="store-card" tag="div" class="store-grid">
              <button v-for="group in groups" :key="group.key" type="button" class="package-card" :class="{ selected: group.key === selected }" :aria-pressed="group.key === selected" @click="selected = group.key">
                <span class="package-symbol" :style="{ '--package-accent': accent(group.releases[0]!.game) }"><FluentIcon :glyph="category === 'server' ? 'globe' : category === 'client' ? 'gamepad' : 'puzzle'" :size="25" /></span>
                <span class="package-copy"><small>{{ packageAuthor(group.releases[0]!) }}</small><span class="package-title"><strong>{{ localized(group.releases[0]!.name) }}</strong><span v-if="isInstalled(group.releases[0]!)" class="installed-badge">{{ t('store.installed') }}</span></span><Transition name="store-value" mode="out-in"><StorePackageVersion v-if="group.releases[0]!.version" :key="`${group.releases[0]!.manifestSha256}:${group.releases[0]!.version}:${pluginUpdateFrom(group.releases[0]!, localPlugins)}`" :version="group.releases[0]!.version" :installed-version="pluginUpdateFrom(group.releases[0]!, localPlugins)" /></Transition></span>
                <span class="package-bottom"><span>{{ group.offers.length > 1 ? t('store.sourceCount', { count: group.offers.length }) : group.source.name }}</span><span>{{ size(group.releases[0]!.size) }}</span></span>
              </button>
            </TransitionGroup>
            <template v-if="!groups.length"><div v-if="initialLoading || inventoryLoading && updateFilter === 'updates'" class="store-loading"><WProgressRing :size="32" :aria-label="t('store.loading')" /><span>{{ t('store.loading') }}</span></div>
            <div v-else-if="category === 'plugin' && updateFilter === 'updates'" class="store-empty"><FluentIcon glyph="checkMark" :size="36" /><h2>{{ t(query ? 'store.noMatches' : inventoryError ? 'store.updateCheckFailed' : 'store.noUpdates') }}</h2></div>
            <div v-else class="store-empty"><FluentIcon glyph="shop" :size="36" /><h2>{{ t(query ? 'store.noMatches' : 'store.empty') }}</h2><p v-if="!query">{{ t('store.emptyBody') }}</p><WButton v-if="!query" @click="manage = true">{{ t('store.manage') }}</WButton></div>
            </template>
          </div></WContentTransition>
        </section>
        <WContentTransition><aside :key="currentGroup?.key ?? ''" class="package-detail surface" :aria-label="t('store.overview')" :aria-busy="initialLoading">
          <template v-if="current && currentGroup">
            <div class="detail-heading"><span class="package-symbol detail-symbol" :style="{ '--package-accent': accent(current.game) }"><FluentIcon :glyph="current.kind === 'server' ? 'globe' : current.kind === 'client' ? 'gamepad' : 'puzzle'" :size="34" /></span><span class="detail-game">{{ packageAuthor(current) }}</span><div class="detail-title"><h2>{{ localized(current.name) }}</h2><span v-if="isInstalled(current)" class="installed-badge">{{ t('store.installed') }}</span></div><!-- <p>{{ currentGroup.source.name }}</p> --></div>
            <div class="detail-scroll"><p class="package-description">{{ localized(current.description) }}</p><label v-if="currentGroup.releases.length > 1 || current.version" class="field"><span>{{ t('store.version') }}</span><WComboBox :model-value="current.manifestSha256" :options="currentGroup.releases.map(item => ({ value: item.manifestSha256, label: item.version || t('clientUpdates.currentFiles') }))" @update:model-value="version = $event" /></label>
              <StorePackageVersion v-if="currentUpdateFrom" class="detail-update-version" :version="current.version" :installed-version="currentUpdateFrom" />
              <dl class="package-facts"><div v-if="current.authors?.length"><dt>{{ t('store.authors') }}</dt><dd>{{ current.authors.join(', ') }}</dd></div><div v-if="current.uploader"><dt>{{ t('store.uploader') }}</dt><dd>{{ current.uploader }}</dd></div><div><dt>{{ t('store.source') }}</dt><dd>{{ currentGroup.source.name }}</dd></div><div><dt>{{ t('store.size') }}</dt><dd>{{ size(current.size) }}</dd></div></dl>
              <h3>{{ t('store.builds') }}</h3><div class="build-tags"><span v-for="build in current.builds" :key="build">{{ buildName(build) }}</span><span v-if="!current.builds.length">{{ t('store.allBuilds') }}</span></div>
            </div>
            <div class="detail-actions"><p v-if="unavailableReason" role="status">{{ unavailableReason }}</p><WButton variant="accent" :disabled="!!unavailableReason || currentUpToDate" @click="installOpen = true"><FluentIcon :glyph="currentUpToDate ? 'checkMark' : currentUpdateFrom ? 'refresh' : 'download'" />{{ t(!installable ? 'store.unavailable' : currentUpToDate ? 'store.upToDate' : currentUpdateFrom ? 'store.update' : 'store.download') }}</WButton></div>
          </template>
          <div v-else-if="initialLoading" class="store-loading"><WProgressRing :size="32" :aria-label="t('store.loading')" /><span>{{ t('store.loading') }}</span></div>
          <div v-else class="store-empty"><FluentIcon glyph="openFile" :size="32" /><h2>{{ t('store.choose') }}</h2><p>{{ t('store.chooseBody') }}</p></div>
        </aside></WContentTransition>
      </div>
    </PageBody>
    <StoreSourcesDialog :open="manage" :sources="sources" @close="manage = false" @changed="refresh(true)" />
    <StoreInstallDialog v-if="current?.kind === 'client' && currentGroup" :open="installOpen" :item="current" :source="currentGroup.source" @close="installOpen = false" />
    <StorePluginDialog v-if="current?.kind === 'plugin' && currentGroup" :open="installOpen" :item="current" :source="currentGroup.source" :offers="currentGroup.offers" :local-plugins="localPlugins" @close="installOpen = false" />
    <StorePluginUpdatesDialog :open="updateAllOpen" :updates="updates" @close="updateAllOpen = false" />
    <StoreServerDialog v-if="current?.kind === 'server' && currentGroup" :open="installOpen" :item="current" :source="currentGroup.source" @close="installOpen = false" />
  </section>
</template>

<style scoped>
.store-header-actions, .store-toolbar, .store-filters { display: flex; align-items: center; gap: 12px; }
.store-toolbar { flex: none; justify-content: space-between; margin-bottom: 18px; }
.store-filters { min-width: 0; }.store-filters > .w-flyout-anchor { width: 210px; }.store-filters > .w-textbox { width: 260px; }
.store-workspace { flex: 1; min-height: 0; display: grid; grid-template-columns: minmax(0, 1.65fr) minmax(320px, 1fr); gap: 20px; }
.store-browser, .package-detail { min-height: 0; overflow: hidden; display: flex; flex-direction: column; }
.store-list-heading { display: flex; align-items: center; justify-content: space-between; gap: 8px; height: 64px; flex: none; padding: 12px 18px; color: var(--text-secondary); font-size: 12px; }
.store-refresh-status { display: inline-flex; align-items: center; gap: 7px; flex: none; white-space: nowrap; }
.store-value-enter-active, .store-value-leave-active, .store-refresh-enter-active, .store-refresh-leave-active { transition: opacity 160ms ease; }
.store-value-enter-from, .store-value-leave-to, .store-refresh-enter-from, .store-refresh-leave-to { opacity: 0; }
.package-card.store-card-move, .package-card.store-card-enter-active, .package-card.store-card-leave-active { transition: transform 320ms var(--ease-fast-out-slow-in), opacity 260ms ease, background 150ms, border-color 150ms; }
.package-card.store-card-enter-from, .package-card.store-card-leave-to { opacity: 0; transform: translateY(16px) scale(.98); }
.package-card.store-card-leave-active { position: absolute; pointer-events: none; }
.store-heading-content { display: flex; align-items: center; justify-content: space-between; gap: 8px; flex: 1; min-width: 0; }
.store-heading-enter-active, .store-heading-leave-active { transition: opacity 130ms linear, transform 130ms var(--ease-fast-out-slow-in); }
.store-heading-enter-from { opacity: 0; transform: translateY(3px); }
.store-heading-leave-to { opacity: 0; transform: translateY(-3px); }
.store-update-action-enter-active { transition: opacity 220ms ease-out, transform 220ms var(--ease-fast-out-slow-in); }
.store-update-action-leave-active { transition: opacity 160ms ease-in, transform 160ms ease-in; }
.store-update-action-enter-from, .store-update-action-leave-to { opacity: 0; transform: translateY(4px); }
@media (prefers-reduced-motion: reduce) {
  .package-card.store-card-move, .package-card.store-card-enter-active, .package-card.store-card-leave-active { transition: opacity 100ms linear; }
  .package-card.store-card-enter-from, .package-card.store-card-leave-to { transform: none; }
  .store-update-action-enter-active, .store-update-action-leave-active, .store-heading-enter-active, .store-heading-leave-active { transition: opacity 100ms linear; }
  .store-update-action-enter-from, .store-update-action-leave-to, .store-heading-enter-from, .store-heading-leave-to { transform: none; }
}
.detail-update-version { margin-top: 12px; }
.store-status { display: inline-flex; align-items: center; gap: 8px; min-height: 18px; }
.store-loading { min-height: 240px; height: 100%; display: flex; flex: 1; flex-direction: column; align-items: center; justify-content: center; gap: 16px; color: var(--text-secondary); font-size: 13px; }
.store-grid-viewport { flex: 1; min-height: 0; overflow-y: auto; padding: 0 14px 18px 18px; }
.store-grid { position: relative; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; align-content: start; }
.package-card { position: relative; display: grid; grid-template-columns: 52px minmax(0, 1fr); gap: 14px 12px; padding: 18px 16px 12px; text-align: left; border: 1px solid var(--card-stroke-default); border-radius: var(--radius-overlay); background: var(--card-bg-default); transition: background 150ms, border-color 150ms; }
.package-card:hover { background: var(--subtle-secondary); }.package-card.selected { border-color: color-mix(in srgb, var(--accent-default) 60%, var(--card-stroke-default)); background: color-mix(in srgb, var(--accent-default) 6%, var(--card-bg-default)); }
.package-symbol { display: grid; place-items: center; width: 52px; height: 56px; border-radius: 10px; background: color-mix(in srgb, var(--package-accent) 10%, var(--card-bg-default)); color: var(--package-accent); border: 1px solid color-mix(in srgb, var(--package-accent) 16%, transparent); }
.package-title, .detail-title { display: flex; align-items: center; flex-wrap: wrap; gap: 6px 8px; }
.detail-title { justify-content: center; }
.installed-badge { flex: none; white-space: nowrap; padding: 1px 7px; border-radius: 4px; background: color-mix(in srgb, var(--accent-default) 10%, var(--card-bg-default)); color: var(--accent-default); font-size: 11px; line-height: 17px; font-weight: 400; }
.package-copy { display: grid; align-content: center; gap: 4px; min-width: 0; }.package-copy strong { font-size: 15px; font-weight: 600; overflow-wrap: anywhere; }.package-copy small, .package-copy > .package-version { color: var(--text-secondary); font-size: 12px; line-height: 16px; }
.package-bottom { display: flex; justify-content: space-between; gap: 8px; grid-column: 1 / -1; padding-top: 10px; border-top: 1px solid var(--stroke-divider); color: var(--text-secondary); font-size: 11px; }.package-bottom > span:first-child { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }.package-bottom > span:last-child { flex: none; }
.detail-heading { display: flex; flex-direction: column; align-items: center; text-align: center; padding: 28px 24px 20px; border-bottom: 1px solid var(--stroke-divider); }.detail-symbol { width: 72px; height: 76px; border-radius: 14px; margin-bottom: 18px; }.detail-game { font-size: 12px; color: var(--text-secondary); }.detail-heading h2 { font-size: 23px; line-height: 30px; font-weight: 600; margin: 6px 0; }.detail-heading p { color: var(--text-secondary); font-size: 12px; margin: 0; }
.detail-scroll { flex: 1; min-height: 0; overflow-y: auto; padding: 20px 24px; }.package-description { font-size: 13px; color: var(--text-secondary); line-height: 22px; margin: 0 0 20px; white-space: pre-line; }.package-facts { display: grid; gap: 12px; margin: 20px 0; font-size: 12px; }.package-facts > div { display: flex; justify-content: space-between; gap: 16px; }.package-facts dt { color: var(--text-secondary); flex: none; }.package-facts dd { margin: 0; text-align: right; overflow-wrap: anywhere; }.detail-scroll h3 { font-size: 12px; font-weight: 600; margin: 0 0 10px; }
.build-tags { display: flex; flex-wrap: wrap; gap: 6px; }.build-tags span { font-size: 11px; padding: 2px 8px; border: 1px solid var(--card-stroke-default); border-radius: 4px; background: var(--subtle-secondary); }
.detail-actions { flex: none; padding: 16px 24px; border-top: 1px solid var(--stroke-divider); }.detail-actions > button { width: 100%; }
.store-empty { min-height: 100%; display: flex; flex-direction: column; align-items: center; justify-content: center; text-align: center; padding: 32px; gap: 12px; color: var(--text-secondary); }.store-empty h2 { font-size: 16px; font-weight: 600; color: var(--text-primary); margin: 4px 0 0; }.store-empty p { margin: 0; font-size: 13px; max-width: 280px; }.store-empty > button { margin-top: 6px; }
.source-notices { max-height: 128px; overflow-y: auto; flex: none; padding: 0 18px 12px; }.source-notice { display: flex; align-items: center; gap: 10px; padding: 10px 12px; border-radius: 6px; background: var(--subtle-secondary); }.source-notice + .source-notice { margin-top: 8px; }.source-notice > div { flex: 1; min-width: 0; display: grid; gap: 3px; font-size: 12px; }.source-notice strong { font-weight: 600; }.source-notice span { overflow-wrap: anywhere; color: var(--text-secondary); }.catalog-error { font-size: 12px; color: var(--text-secondary); }
</style>

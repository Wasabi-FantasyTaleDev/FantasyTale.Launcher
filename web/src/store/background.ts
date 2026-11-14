import { computed, watch } from 'vue'
import { call, demoMode, inHost, on } from '../bridge'
import { state } from '../state'
import { downloads } from '../downloads/live'
import { i18n } from '../i18n'
import { storeSession } from './session'
import { groupStorePackages } from './groups'
import { storePluginUpdates } from './updates'
import type { StorePackage } from './types'

export function canInstallStorePlugin(item: StorePackage): boolean {
  const game = state.games.find(game => game.id === item.game)
  return !!game && game.status === 'enabled' && game.supportsPlugins
    && game.builds.some(build => !item.builds.length || item.builds.includes(build.id))
}

/** The shell and store share one catalog and the complete local package inventory. */
export const liveStoreSession = storeSession(inHost || demoMode?.enabled ? 'desktop' : 'browser', {
  sources: () => call('store.sources'),
  catalog: (source, force) => call('store.catalog', { id: source.id, force }),
  plugins: () => call('plugins.list'),
  error: error => (inHost || !!demoMode?.enabled) ? (error as Error).message : i18n.global.t('store.browserOnly'),
})

export const availableStoreUpdates = computed(() => storePluginUpdates(
  groupStorePackages(liveStoreSession.state.sources, liveStoreSession.state.catalogs, 'plugin'),
  liveStoreSession.state.localPlugins, canInstallStorePlugin))

/** Checks off the navigation path; failures remain available in the store without blocking launch. */
export function startStoreUpdateChecks(): () => void {
  const active = () => inHost || !!demoMode?.enabled
  const refresh = () => { if (active() && !liveStoreSession.state.loading) void liveStoreSession.refresh() }
  const inventory = () => { if (active()) void liveStoreSession.refreshInventory() }
  if (active()) refresh()
  const timer = window.setInterval(refresh, 5 * 60_000)
  const stopDownloads = watch(() => downloads.tasks.filter(task => task.target.kind === 'plugin'
    && task.stage === 'completed').map(task => task.id).join(','), inventory)
  const stopInventory = watch(() => state.plugins.map(plugin => `${plugin.game}:${plugin.id}:${plugin.version}`).join(','), inventory)
  const stopChanges = on('plugins.changed', inventory)
  const stopModeWatch = demoMode
    ? watch(() => demoMode.enabled, enabled => {
        if (enabled) void liveStoreSession.refresh(true)
        else if (!inHost) liveStoreSession.state.localPlugins = []
      })
    : () => {}
  return () => { clearInterval(timer); stopDownloads(); stopInventory(); stopChanges(); stopModeWatch() }
}

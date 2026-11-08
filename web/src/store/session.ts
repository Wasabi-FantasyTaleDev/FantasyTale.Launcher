import { reactive } from 'vue'
import type { StoreCatalog, StoreSource } from './types'
import type { LocalPlugin } from './updates'

interface StoreLoader {
  sources(): Promise<StoreSource[]>
  catalog(source: StoreSource, force: boolean): Promise<StoreCatalog>
  plugins(): Promise<LocalPlugin[]>
  error(error: unknown): string
}

/**
 * Creates an isolated, non-persistent Store browsing session.
 *
 * Architecture & Invariants:
 * 1. Ephemeral View State: Catalogs, error notices, and inventory lists live strictly
 *    in frontend reactive memory and are never persisted to disk or user settings.
 * 2. Multi-Level Generation Counters: `generation` (for store catalogs) and
 *    `inventoryGeneration` (for local plugin inspections) prevent race conditions
 *    when users switch sources or trigger refreshes in rapid succession.
 * 3. Bounded Concurrency: Parallel catalog fetches from multiple sources are throttled
 *    to at most 4 simultaneous network requests.
 * 4. Structural Equality Check: If a fetched catalog matches the existing cached copy,
 *    assignment is skipped to prevent triggering unneeded Vue re-renders or restarting
 *    card entrance transitions.
 */
export function createStoreSession(loader: StoreLoader) {
  const state = reactive({
    sources: [] as StoreSource[], catalogs: {} as Record<string, StoreCatalog>,
    errors: {} as Record<string, string>, loadError: '', loading: false, loaded: false,
    localPlugins: [] as LocalPlugin[], inventoryLoading: false, inventoryLoaded: false, inventoryError: '',
  })
  let generation = 0, inventoryGeneration = 0
  async function refreshInventory() {
    const request = ++inventoryGeneration
    state.inventoryLoading = true; state.inventoryError = ''
    try {
      const plugins = await loader.plugins()
      if (request === inventoryGeneration) { state.localPlugins = plugins; state.inventoryLoaded = true }
    } catch (error) { if (request === inventoryGeneration) state.inventoryError = loader.error(error) }
    finally { if (request === inventoryGeneration) state.inventoryLoading = false }
  }
  async function refresh(force = false) {
    void refreshInventory()
    const request = ++generation
    state.loading = true; state.loadError = ''
    try {
      const entries = await loader.sources()
      if (request !== generation) return
      const previous = state.sources
      const retained = entries.filter(source => source.enabled && previous.some(old => old.id === source.id
        && old.kind === source.kind && old.baseUrl === source.baseUrl && old.catalogPath === source.catalogPath))
      // A changed address must never inherit packages from its former source identity.
      state.catalogs = Object.fromEntries(retained.filter(source => state.catalogs[source.id])
        .map(source => [source.id, state.catalogs[source.id]!]))
      state.errors = Object.fromEntries(retained.filter(source => state.errors[source.id])
        .map(source => [source.id, state.errors[source.id]!]))
      state.sources = entries
      const queue = entries.filter(source => source.enabled && source.baseUrl)
      let succeeded = queue.length === 0
      await Promise.all(Array.from({ length: Math.min(4, queue.length) }, async () => {
        while (queue.length && request === generation) {
          const source = queue.shift()!
          try {
            const catalog = await loader.catalog(source, force)
            if (request === generation) {
              // Preserve unchanged objects so a refresh does not restart card content animations.
              if (JSON.stringify(state.catalogs[source.id]) !== JSON.stringify(catalog)) state.catalogs[source.id] = catalog
              delete state.errors[source.id]; succeeded = true
            }
          } catch (error) { if (request === generation) state.errors[source.id] = loader.error(error) }
        }
      }))
      if (request === generation && succeeded) state.loaded = true
    } catch (error) { if (request === generation) state.loadError = loader.error(error) }
    finally { if (request === generation) state.loading = false }
  }
  return { state, refresh, refreshInventory }
}

const sessions = new Map<string, ReturnType<typeof createStoreSession>>()

/** Desktop and isolated preview catalogs cannot leak into one another. */
export function storeSession(key: string, loader: StoreLoader) {
  let session = sessions.get(key)
  if (!session) { session = createStoreSession(loader); sessions.set(key, session) }
  return session
}

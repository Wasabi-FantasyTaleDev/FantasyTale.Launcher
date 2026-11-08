import { reactive } from 'vue'
import { call, inHost } from '../bridge'
import { trackDownload } from './live'

export interface ClientUpdateState {
  instanceId: string
  status: 'current' | 'available' | 'unmanaged' | 'manual' | 'source' | 'integrity' | 'network' | 'disk' | 'busy'
  installedVersion: string
  version: string | null
  manifestSha256: string | null
  checkedAt: string
}
export interface ClientUpdateOverview { checking: boolean; clients: ClientUpdateState[]; error: string | null }
export interface ClientUpdatePreferences { autoCheck: boolean; autoDownload: boolean; autoCheckPlugins: boolean; autoUpdatePlugins: boolean; removeOldPluginVersions: boolean }
export const clientUpdates = reactive({ overview: { checking: false, clients: [], error: null } as ClientUpdateOverview,
  preferences: { autoCheck: true, autoDownload: false, autoCheckPlugins: true, autoUpdatePlugins: true, removeOldPluginVersions: true } as ClientUpdatePreferences, saving: false, requesting: false, installing: '', error: '' })
let revision = 0

export async function checkClientUpdates() {
  if (clientUpdates.requesting) return
  const expected = ++revision
  clientUpdates.requesting = true; clientUpdates.error = ''
  try { const result = await call('updates.check'); if (revision === expected) clientUpdates.overview = result }
  catch (error) { clientUpdates.error = (error as Error).message }
  finally { clientUpdates.requesting = false }
}
export async function installClientUpdate(update: ClientUpdateState) {
  if (!update.manifestSha256 || clientUpdates.installing) return
  revision++; clientUpdates.installing = update.instanceId
  try {
    trackDownload(await call('updates.install', { instanceId: update.instanceId, manifestSha256: update.manifestSha256 }))
    clientUpdates.overview = await call('updates.list')
  } finally { clientUpdates.installing = '' }
}
export async function saveUpdatePreferences(value: ClientUpdatePreferences) {
  clientUpdates.saving = true
  try { clientUpdates.preferences = await call('settings.setUpdates', value) }
  finally { clientUpdates.saving = false }
  if (value.autoCheck) void checkClientUpdates()
}
export function pollClientUpdates() {
  let stopped = false, timer: ReturnType<typeof setTimeout> | undefined
  async function poll() {
    const expected = revision
    try {
      const result = await call('updates.list')
      if (!stopped && expected === revision && !clientUpdates.requesting) clientUpdates.overview = result
    } catch (error) { if (!stopped) clientUpdates.error = (error as Error).message }
    if (!stopped) timer = setTimeout(poll, 2000)
  }
  void call('settings.updates').then(value => { if (!stopped) clientUpdates.preferences = value }).catch(error => { clientUpdates.error = (error as Error).message })
  if (inHost) void poll()
  return () => { stopped = true; if (timer) clearTimeout(timer) }
}

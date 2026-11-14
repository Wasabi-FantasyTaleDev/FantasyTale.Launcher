import { reactive } from 'vue'
import { call, demoMode, inHost, type LocalizedText } from '../bridge'
import type { DownloadTask } from '../downloads/model'

export interface RequiredPluginUpdate { key: string; id: string; name: LocalizedText; installedVersion: string; version: string; task: DownloadTask | null }

export const requiredUpdatePrompt = reactive({ open: false, items: [] as RequiredPluginUpdate[] })
let finish: ((value: { keys: string[]; tasks: string[] } | null) => void) | undefined
let timer: ReturnType<typeof setTimeout> | undefined
let generation = 0

/** Reads background discovery only; launching never requests a source refresh. */
export async function requestRequiredUpdates(instanceId: string, serverId: string) {
  if (!(inHost || !!demoMode?.enabled)) return { keys: [], tasks: [] }
  if (finish) return null
  const expected = ++generation
  const read = (ensure = false) => call('plugins.launchUpdates', { instanceId, serverId, ensure })
  // The first read lets the server's version rules start their switches or downloads; polling only reads progress.
  const first = await read(true)
  if (expected !== generation) return null
  const items = first.items
  if (!items.length) return { keys: [], tasks: [] }
  requiredUpdatePrompt.items = items; requiredUpdatePrompt.open = true
  const result = new Promise<{ keys: string[]; tasks: string[] } | null>(resolve => { finish = resolve })
  async function poll() {
    try {
      const latest = await read()
      if (expected !== generation || !finish) return
      requiredUpdatePrompt.items = latest.items
      if (!latest.items.length) { finishRequiredUpdates(true); return }
    } catch { /* Retain the displayed selection; backend validation rejects changed updates. */ }
    if (expected === generation && finish) timer = setTimeout(poll, 500)
  }
  timer = setTimeout(poll, 500)
  return result
}

export function finishRequiredUpdates(accepted: boolean) {
  generation++
  if (timer) clearTimeout(timer)
  requiredUpdatePrompt.open = false
  const resolve = finish; finish = undefined
  resolve?.(accepted ? { keys: requiredUpdatePrompt.items.map(item => item.key), tasks: requiredUpdatePrompt.items.flatMap(item => item.task ? [item.task.id] : []) } : null)
}

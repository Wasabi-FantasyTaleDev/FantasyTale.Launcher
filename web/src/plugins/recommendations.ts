import { reactive, watch } from 'vue'
import { call, inHost, type ClientComponent } from '../bridge'
import { downloadTasks, trackDownload, downloadAction } from '../downloads/live'
import type { DownloadTask } from '../downloads/model'
import { refreshInstances } from '../state'

export const recommendationPrompt = reactive({
  instanceId: '', serverId: '', name: '', open: false, busy: false, error: '', selected: [] as string[],
  tasks: [] as DownloadTask[],
  items: [] as ClientComponent[],
})
let tail: Promise<unknown> = Promise.resolve()
const pending = new Map<string, Promise<boolean>>()
let generation = 0
let completionAttempted = false
let finish: ((proceed: boolean) => void) | undefined

/** Serializes prompts for independent clients; the host persists the one-time acknowledgement. */
export function requestRecommendations(instanceId: string, name: string, serverId = '', launching = false, valid: () => boolean = () => true): Promise<boolean> {
  if (!inHost) return Promise.resolve(true)
  const existing = pending.get(instanceId)
  if (existing) return existing
  const request = tail.then(async () => {
    if (!valid()) return false
    const offered = await call('clientPolicy.claimPlugins', { instanceId, serverId: serverId || undefined })
    if (!valid()) return false
    const items = launching ? offered.filter(item => !item.required) : offered
    if (!items.length) return true
    generation++; completionAttempted = false
    Object.assign(recommendationPrompt, { instanceId, serverId, name, items,
      selected: items.filter(item => item.required || item.defaultSelected === true).map(item => item.id), error: '', busy: false, open: true, tasks: [] })
    return new Promise<boolean>(resolve => { finish = resolve })
  })
  pending.set(instanceId, request)
  tail = request.catch(() => {}).finally(() => { pending.delete(instanceId) })
  return request
}

export function dismissRecommendations(): void {
  const wasBusy = recommendationPrompt.busy
  generation++; recommendationPrompt.busy = false; recommendationPrompt.open = false
  const resolve = finish; finish = undefined; resolve?.(!wasBusy && recommendationPrompt.tasks.length === 0 && !recommendationPrompt.items.some(item => item.required))
}

export async function acceptRecommendations(): Promise<void> {
  if (recommendationPrompt.busy) return
  const current = generation
  completionAttempted = false
  recommendationPrompt.busy = true; recommendationPrompt.error = ''
  try {
    if (recommendationPrompt.tasks.length) {
      for (const original of [...recommendationPrompt.tasks]) {
        if (current !== generation) return
        const task = downloadTasks.value.find(task => task.id === original.id) ?? original
        if (['failed', 'canceled', 'paused'].includes(task.stage)) await downloadAction(task, task.stage === 'paused' ? 'resume' : 'retry')
      }
      return
    }
    const tasks = await call('clientPolicy.acceptPlugins', {
      instanceId: recommendationPrompt.instanceId, ids: [...new Set([...recommendationPrompt.selected, ...recommendationPrompt.items.filter(item => item.required).map(item => item.id)])], serverId: recommendationPrompt.serverId || undefined,
    })
    tasks.forEach(trackDownload)
    if (current !== generation) return
    recommendationPrompt.tasks = tasks
    if (tasks.length) return
    await refreshInstances()
    if (current !== generation) return
    recommendationPrompt.open = false
    const resolve = finish; finish = undefined
    resolve?.(true)
  } catch (error) { if (current === generation) recommendationPrompt.error = (error as Error).message }
  finally { if (current === generation) recommendationPrompt.busy = false }
}

// A queued transfer is not an installed plugin; only completed host snapshots release the launch.
watch(() => [downloadTasks.value, recommendationPrompt.busy, recommendationPrompt.tasks] as const, async ([tasks]) => {
  if (!recommendationPrompt.open || !recommendationPrompt.tasks.length || recommendationPrompt.busy || completionAttempted) return
  if (!recommendationPrompt.tasks.every(original => tasks.some(task => task.id === original.id && task.stage === 'completed'))) return
  const current = generation
  completionAttempted = true; recommendationPrompt.busy = true
  try {
    await refreshInstances()
    if (current !== generation) return
    recommendationPrompt.open = false
    const resolve = finish; finish = undefined; resolve?.(true)
  } catch (error) { if (current === generation) recommendationPrompt.error = (error as Error).message }
  finally { if (current === generation) recommendationPrompt.busy = false }
})

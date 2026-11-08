import { reactive, watch } from 'vue'
import { call, inHost, type LocalizedText } from '../bridge'
import { downloadTasks, trackDownload, downloadAction } from '../downloads/live'
import type { DownloadTask } from '../downloads/model'

export interface LaunchWarning {
  key: string; id: string; version: string; installed: boolean; name: LocalizedText
  description?: LocalizedText | null; warning?: LocalizedText | null
}
export const launchWarningPrompt = reactive({ open: false, instanceId: '', serverId: '', busy: false, error: '',
  items: [] as LaunchWarning[], tasks: [] as DownloadTask[] })
let finish: ((accepted: boolean) => void) | undefined
let generation = 0
let completionAttempted = false

/** Confirms disabled or missing startup components for one launch; dismissal never authorizes execution. */
export async function requestLaunchWarnings(instanceId: string, serverId = '', valid: () => boolean = () => true): Promise<string[] | null> {
  if (!inHost) return []
  if (finish) return null
  const items = await call('plugins.launchWarnings', { instanceId, serverId: serverId || undefined })
  if (!valid()) return null
  if (!items.length) return []
  if (finish) return null
  generation++
  Object.assign(launchWarningPrompt, { instanceId, serverId, items, open: true, busy: false, error: '', tasks: [] })
  const accepted = await new Promise<boolean>(resolve => { finish = resolve })
  return accepted ? items.map(item => item.key) : null
}

export function finishLaunchWarnings(accepted: boolean): void {
  generation++
  launchWarningPrompt.open = false
  const resolve = finish; finish = undefined
  resolve?.(accepted)
}

/** Installs and enables displayed components before releasing the pending launch. */
export async function enableLaunchWarnings(): Promise<void> {
  if (launchWarningPrompt.busy) return
  const current = generation
  completionAttempted = false
  launchWarningPrompt.busy = true; launchWarningPrompt.error = ''
  try {
    if (launchWarningPrompt.tasks.length) {
      for (const original of launchWarningPrompt.tasks) {
        const task = downloadTasks.value.find(item => item.id === original.id) ?? original
        if (task.stage === 'failed' || task.stage === 'canceled' || task.stage === 'paused')
          await downloadAction(task, task.stage === 'paused' ? 'resume' : 'retry')
      }
    } else {
      const tasks = await call('plugins.installationComponents', { instanceId: launchWarningPrompt.instanceId,
        ids: launchWarningPrompt.items.map(item => item.id), serverId: launchWarningPrompt.serverId || undefined })
      tasks.forEach(trackDownload)
      if (current !== generation) return
      launchWarningPrompt.tasks = tasks
    }
    if (!launchWarningPrompt.tasks.length && current === generation) await verifyEnabled(current)
  } catch (error) { if (current === generation) launchWarningPrompt.error = (error as Error).message }
  finally { if (current === generation) launchWarningPrompt.busy = false }
}

async function verifyEnabled(current: number) {
  const remaining = await call('plugins.launchWarnings', { instanceId: launchWarningPrompt.instanceId,
    serverId: launchWarningPrompt.serverId || undefined })
  if (current !== generation) return
  if (remaining.length) { launchWarningPrompt.items = remaining; launchWarningPrompt.tasks = []; return }
  finishLaunchWarnings(true)
}

watch(() => [downloadTasks.value, launchWarningPrompt.tasks, launchWarningPrompt.busy] as const, async () => {
  if (!launchWarningPrompt.open || launchWarningPrompt.busy || completionAttempted || !launchWarningPrompt.tasks.length
    || !launchWarningPrompt.tasks.every(original => downloadTasks.value.some(task => task.id === original.id && task.stage === 'completed'))) return
  const current = generation
  completionAttempted = true
  launchWarningPrompt.busy = true
  try { await verifyEnabled(current) }
  catch (error) { if (current === generation) launchWarningPrompt.error = (error as Error).message }
  finally { if (current === generation) launchWarningPrompt.busy = false }
})

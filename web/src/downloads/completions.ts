import { reactive } from 'vue'
import type { DownloadTask } from './model'

/** Home acknowledgements are scoped to task identities; download history remains available. */
export const dismissedCompletionIds = reactive(new Set<string>())

export function acknowledgeClientLaunch(tasks: readonly DownloadTask[], instanceId: string, gameId: string, buildId: string) {
  const retained = new Set(tasks.map(task => task.id))
  for (const id of dismissedCompletionIds) if (!retained.has(id)) dismissedCompletionIds.delete(id)
  for (const task of tasks) {
    if (task.stage !== 'completed' || task.target.gameId !== gameId || task.target.kind === 'launcher') continue
    if (task.target.instanceId ? task.target.instanceId === instanceId : task.target.kind === 'client' && task.target.buildId === buildId)
      dismissedCompletionIds.add(task.id)
  }
}

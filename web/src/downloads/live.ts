import { computed, reactive } from 'vue'
import { call, inHost } from '../bridge'
import type { DownloadTask } from './model'
import { isTerminal } from './model'
import { previewAction, updatePreview } from './preview'

export const downloads = reactive({ tasks: [] as DownloadTask[], selectedId: '', error: '' })
export const downloadTasks = computed(() => [...downloads.tasks, ...updatePreview.tasks])

/**
 * Tracks download task IDs observed during the current application lifetime.
 *
 * Invariant:
 * The Home page progress card only surfaces downloads actively observed during this
 * session, preventing completed or historical tasks restored from host disk storage
 * from showing redundant "Finished" banners on fresh launcher startup.
 */
export const sessionDownloadIds = reactive(new Set<string>())
let revision = 0

/** Registers and tracks a newly submitted download task in active memory. */
export function trackDownload(task: DownloadTask) {
  revision++
  sessionDownloadIds.add(task.id)
  downloads.tasks = [task, ...downloads.tasks.filter(item => item.id !== task.id)]
  downloads.selectedId = task.id
}

/** Dispatches a user action (pause, resume, cancel, retry, restart) to the host or preview engine. */
export async function downloadAction(task: DownloadTask, action: Parameters<typeof previewAction>[1], archivePassword?: string) {
  if (task.mode === 'preview') { previewAction(task, action); return }
  if (task.target.kind === 'launcher' && action === 'restart') { await call('launcherUpdates.restart'); return }
  if (task.target.kind === 'launcher' && task.failure === 'startup' && action === 'retry') {
    revision++
    const update = await call('launcherUpdates.check')
    if (update.status === 'available') trackDownload(await call('launcherUpdates.download', { manifestSha256: update.manifestSha256 }))
    else downloads.tasks = await call('downloads.list')
    return
  }
  revision++
  downloads.tasks = await call('downloads.action', { id: task.id, action, ...(archivePassword === undefined ? {} : { archivePassword }) })
}

/** Removes all completed and failed tasks from the active task list. */
export async function clearFinishedDownloads() {
  revision++
  if (inHost) downloads.tasks = await call('downloads.clearFinished')
  updatePreview.tasks = updatePreview.tasks.filter(task => !isTerminal(task))
}

/**
 * Periodically polls the host download manager for status updates (every 500ms).
 *
 * Invariants:
 * 1. Monotonic Revision Counter: Incremented on user actions to prevent an in-flight
 *    poll from overwriting immediate local state changes with older host snapshots.
 * 2. Unsubscribe Cleanup: Returns a teardown closure that cancels timers and flags
 *    the loop as stopped when components unmount.
 */
export function pollDownloads() {
  let stopped = false, timer: ReturnType<typeof setTimeout> | undefined
  async function poll() {
    const expected = revision
    try {
      const tasks = await call('downloads.list')
      if (!stopped && expected === revision) {
        for (const task of tasks) if (!isTerminal(task)) sessionDownloadIds.add(task.id)
        downloads.tasks = tasks; downloads.error = ''
      }
    } catch (error) { if (!stopped) downloads.error = (error as Error).message }
    if (!stopped) timer = setTimeout(poll, 500)
  }
  if (inHost) void poll()
  return () => { stopped = true; if (timer) clearTimeout(timer) }
}

import { call, inHost } from '../bridge'
import { downloads, trackDownload } from './live'
import { createLauncherUpdateFlow, type LauncherUpdateTransport } from './launcherUpdateFlow'
import type { DownloadTask } from './model'

/** The preview only runs on the development server, outside the native bridge. */
export const launcherUpdatePreview = import.meta.env.DEV && !inHost &&
  new URLSearchParams(window.location.hash.split('?')[1]).get('launcherUpdatePreview') === '1'

let startedAt = 0, completed = false
const hash = 'a'.repeat(64)
const previewTask = (): DownloadTask => {
  const elapsed = startedAt ? (Date.now() - startedAt) / 1000 : 0
  const fraction = Math.min(1, elapsed / 42), size = 96 * 1024 ** 2
  return { id: 'preview-launcher-update', mode: 'preview',
    target: { kind: 'launcher', name: 'Fantasy Tale Launcher', fromVersion: '0.1.12', toVersion: '0.1.13', sourceName: 'Fantasy Tale', sourceKind: 'alist', directory: '' },
    stage: elapsed < 1 ? 'checking' : fraction < 1 ? 'downloading' : elapsed < 45 ? 'verifying' : 'ready',
    files: [{ path: 'FantasyTale.Launcher.exe', size, received: size * fraction, stage: fraction < 1 ? 'downloading' : 'verified' }],
    currentPath: 'FantasyTale.Launcher.exe', bytesPerSecond: size / 42, speedSampleCount: 3,
    stageProgress: fraction === 1 ? Math.min(100, (elapsed - 42) / 3 * 100) : null,
    failure: null, resumeStage: 'downloading' }
}
const preview: LauncherUpdateTransport = {
  read: async () => ({ status: completed ? 'current' : !startedAt ? 'available' : previewTask().stage === 'ready' ? 'ready' : 'downloading',
    version: '0.1.13', revision: 2, manifestSha256: hash, failure: null }),
  download: async () => { startedAt ||= Date.now(); return previewTask() },
  task: async () => startedAt ? previewTask() : null,
  restart: async () => {
    await new Promise(resolve => setTimeout(resolve, 1600)); completed = true
    launcherUpdate.state.consent = ''; launcherUpdate.state.restarting = false
    launcherUpdate.state.update = await preview.read(); launcherUpdate.state.dialog = 'complete'
  },
}
export const launcherUpdate = createLauncherUpdateFlow(launcherUpdatePreview ? preview : {
  read: () => call('launcherUpdates.state'),
  download: async manifestSha256 => {
    let update = await call('launcherUpdates.state')
    if (update.status === 'failed') {
      const failed = (await call('downloads.list')).find(task => task.target.kind === 'launcher' && task.stage === 'failed' && task.failure !== 'startup')
      if (failed && update.manifestSha256 === manifestSha256) {
        const tasks = await call('downloads.action', { id: failed.id, action: 'retry' })
        downloads.tasks = tasks
        return tasks.find(task => task.id === failed.id) ?? failed
      }
    }
    if (update.status === 'failed') update = await call('launcherUpdates.check')
    if (update.manifestSha256 !== manifestSha256) throw new Error('The selected launcher update changed. Check for updates again.')
    if (update.status === 'ready' || update.status === 'downloading') {
      const task = (await call('downloads.list')).find(task => task.target.kind === 'launcher')
      if (!task) throw new Error('The launcher update task is unavailable.')
      if (task.stage === 'paused') {
        const tasks = await call('downloads.action', { id: task.id, action: 'resume' })
        downloads.tasks = tasks
        return tasks.find(item => item.id === task.id) ?? task
      }
      return task
    }
    const task = await call('launcherUpdates.download', { manifestSha256 })
    trackDownload(task); return task
  },
  task: async () => (await call('downloads.list')).find(task => task.target.kind === 'launcher') ?? null,
  restart: manifestSha256 => call('launcherUpdates.restart', { manifestSha256, stopRunningClients: true }),
})

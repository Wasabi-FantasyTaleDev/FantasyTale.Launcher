import { reactive } from 'vue'
import { canCancel, canPause, fileBytes, type DownloadTarget, type DownloadTask, type TaskStage } from './model'

/** Opt-in development fixtures never call the host or write client files. */
export const updatePreview = reactive({ enabled: false, tasks: [] as DownloadTask[], selectedId: '' })
export const previewAvailable = import.meta.env.DEV
let timer: ReturnType<typeof setInterval> | undefined
const mib = 1024 ** 2
const sizes = [220, 780, 640, 384, 12]
function makeTask(target: DownloadTarget, index: number): DownloadTask {
  const paths = target.kind === 'launcher' ? ['FantasyTale.Launcher.Host.exe', 'web.bundle', 'runtime.pack', 'locales.pack', 'release.manifest']
    : ['Data/characters.pack', 'Data/maps.pack', 'Data/textures.pack', 'Data/audio.pack', 'Config/client.manifest']
  return { id: `preview-${index}`, mode: 'preview', target, stage: index === 0 ? 'downloading' : 'queued',
    files: paths.map((path, i) => ({ path, size: sizes[i] * mib * (target.kind === 'launcher' ? 0.1 : 1), received: 0, stage: 'queued' })),
    currentPath: paths[0], bytesPerSecond: 0, speedSampleCount: 0, stageProgress: null, failure: null, resumeStage: 'downloading' }
}
export function startUpdatePreview(target: DownloadTarget) {
  if (!previewAvailable) return
  stopUpdatePreview()
  const client = makeTask(target, 0)
  client.files[0].received = client.files[0].size!; client.files[0].stage = 'downloaded'
  client.files[1].received = client.files[1].size! * 0.31; client.files[1].stage = 'downloading'
  client.currentPath = client.files[1].path
  client.bytesPerSecond = 8.4 * mib; client.speedSampleCount = 3
  updatePreview.tasks = [client, makeTask({ kind: 'launcher', name: 'Fantasy Tale Launcher', fromVersion: '0.1.0', toVersion: '0.2.0', sourceName: 'Fantasy Tale', sourceKind: 'alist', directory: 'C:\\Apps\\FantasyTale' }, 1)]
  updatePreview.selectedId = client.id; updatePreview.enabled = true
  timer = setInterval(tick, 500)
}
export function stopUpdatePreview() {
  if (timer) clearInterval(timer)
  timer = undefined; updatePreview.enabled = false; updatePreview.tasks = []; updatePreview.selectedId = ''
}
function tick() {
  for (const task of updatePreview.tasks) {
    if (task.stage === 'checking') { task.stage = 'downloading'; continue }
    if (task.stage === 'downloading') {
      task.bytesPerSecond = (8.4 + Math.sin(Date.now() / 2500) * 1.1) * mib
      task.speedSampleCount = Math.min(10, task.speedSampleCount + 1)
      const file = task.files.find(f => fileBytes(f) < (f.size ?? Infinity))
      if (!file) { task.stage = 'verifying'; task.stageProgress = 0; task.bytesPerSecond = 0; continue }
      task.currentPath = file.path; file.stage = 'downloading'
      file.received = Math.min(file.size!, file.received + task.bytesPerSecond * 0.5)
      if (file.received === file.size) file.stage = 'downloaded'
    } else if (task.stage === 'verifying' || task.stage === 'installing') {
      task.stageProgress = Math.min(100, (task.stageProgress ?? 0) + 2)
      if (task.stage === 'verifying') {
        const count = Math.floor(task.stageProgress / 100 * task.files.length)
        task.files.forEach((file, i) => { file.stage = i < count ? 'verified' : i === count ? 'verifying' : 'downloaded' })
        task.currentPath = task.files[Math.min(count, task.files.length - 1)].path
      }
      if (task.stageProgress === 100) {
        task.stage = task.stage === 'verifying' ? task.target.kind === 'launcher' ? 'ready' : 'installing' : 'completed'
        task.stageProgress = task.stage === 'installing' ? 0 : 100
      }
    }
  }
}
export function previewAction(task: DownloadTask, action: 'pause' | 'resume' | 'retry' | 'cancel' | 'restart' | 'start' | 'install') {
  if (!updatePreview.enabled || task.mode !== 'preview' || !updatePreview.tasks.includes(task)) return
  if (action === 'pause' && canPause(task)) task.stage = 'paused'
  if (action === 'resume' && task.stage === 'paused') { task.stage = 'downloading'; task.speedSampleCount = 0 }
  if (action === 'start' && task.stage === 'queued') task.stage = 'checking'
  if (action === 'retry' && task.stage === 'failed') { task.stage = task.resumeStage; task.failure = null; task.speedSampleCount = 0; task.stageProgress = 0 }
  if (action === 'cancel' && canCancel(task)) task.stage = 'canceled'
  if (action === 'restart' && task.stage === 'ready') task.stage = 'completed'
  if (action === 'install' && task.stage === 'waiting') { task.stage = 'installing'; task.stageProgress = 0 }
  if (task.stage !== 'downloading') task.bytesPerSecond = 0
}
/** Scenario selection resets only its fixture, never persisted account/client data. */
export function setPreviewStage(task: DownloadTask, stage: TaskStage) {
  if (!updatePreview.enabled || task.mode !== 'preview' || !updatePreview.tasks.includes(task)) return
  task.stage = stage; task.failure = stage === 'failed' ? 'network' : null; task.resumeStage = 'downloading'
  task.stageProgress = ['verifying', 'installing'].includes(stage) ? 35 : null
  task.bytesPerSecond = stage === 'downloading' ? 8.4 * mib : 0; task.speedSampleCount = 3
  const allTransferred = ['verifying', 'installing', 'waiting', 'ready', 'completed'].includes(stage)
  task.files.forEach((file, i) => {
    file.received = allTransferred || i === 0 ? file.size! : i === 1 ? file.size! * 0.31 : 0
    file.stage = allTransferred ? stage === 'verifying' ? 'downloaded' : 'verified' : i === 0 ? 'downloaded' : i === 1 ? stage === 'failed' ? 'failed' : 'downloading' : 'queued'
    if (stage === 'queued' || stage === 'checking') { file.received = 0; file.stage = 'queued' }
  })
  task.currentPath = task.files[stage === 'queued' || stage === 'checking' ? 0 : 1].path
}
if (import.meta.hot) import.meta.hot.dispose(stopUpdatePreview)

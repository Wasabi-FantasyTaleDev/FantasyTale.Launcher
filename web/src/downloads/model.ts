/** Download identity is independent of expiring source URLs and page selection. */
export interface DownloadTarget {
  kind: 'client' | 'plugin' | 'launcher' | 'server'
  packageId?: string
  gameId?: string
  buildId?: string
  instanceId?: string
  serverId?: string
  name: string
  nameLocalized?: Record<string, string> | null
  fromVersion: string
  toVersion: string
  sourceName: string
  sourceKind: 'alist' | 'http' | 'github'
  directory: string
}

export const taskStages = ['queued', 'checking', 'downloading', 'paused', 'verifying', 'installing', 'waiting', 'ready', 'completed', 'failed', 'canceled'] as const
export type TaskStage = typeof taskStages[number]
/** A new package has no previous version; Launcher self-updates always retain update wording. */
export function downloadStageKey(task: Pick<DownloadTask, 'target' | 'stage'>): string {
  return task.target.kind !== 'launcher' && !task.target.fromVersion && ['checking', 'downloading', 'installing', 'completed', 'failed'].includes(task.stage)
    ? `installation.${task.stage}` : `updates.${task.stage}`
}
export type FileStage = 'queued' | 'downloading' | 'downloaded' | 'verifying' | 'verified' | 'failed'
export interface DownloadFile {
  path: string
  size: number | null
  received: number
  stage: FileStage
}
/** A snapshot describes transfer and installation separately; 100% is not installed. */
export interface DownloadTask {
  id: string
  mode: 'preview' | 'live'
  target: DownloadTarget
  stage: TaskStage
  files: DownloadFile[]
  currentPath: string | null
  bytesPerSecond: number
  speedSampleCount: number
  stageProgress: number | null
  failure: 'network' | 'integrity' | 'disk' | 'space' | 'busy' | 'access' | 'source' | 'startup' | 'archive-password' | null
  resumeStage: 'downloading' | 'verifying'
}
const nonnegative = (value: number) => Number.isFinite(value) ? Math.max(0, value) : 0
export const fileBytes = (file: DownloadFile) => file.size == null ? nonnegative(file.received) : Math.min(nonnegative(file.size), nonnegative(file.received))
export const percent = (done: number, total: number | null): number | null => total == null ? null : total === 0 ? 100 : Math.min(100, Math.max(0, nonnegative(done) / total * 100))
export function transferMetrics(task: DownloadTask) {
  const total = task.files.length && task.files.every(file => file.size != null) ? task.files.reduce((sum, file) => sum + nonnegative(file.size!), 0) : null
  const received = task.files.reduce((sum, file) => sum + fileBytes(file), 0)
  const speed = task.stage === 'downloading' ? nonnegative(task.bytesPerSecond) : 0
  const current = task.files.find(file => file.path === task.currentPath) ?? null
  const remainingSeconds = task.stage === 'downloading' && task.speedSampleCount >= 3 && speed > 0 && total != null && received < total
    ? Math.ceil((total - received) / speed) : null
  return { total, received, speed, remainingSeconds, percent: percent(received, total), current,
    currentPercent: current ? percent(fileBytes(current), current.size) : null,
    transferred: task.files.filter(file => ['downloaded', 'verifying', 'verified'].includes(file.stage)).length }
}
export const isTerminal = (task: DownloadTask) => ['completed', 'canceled'].includes(task.stage)
export const canPause = (task: DownloadTask) => task.mode === 'preview' ? task.stage === 'downloading' : ['queued', 'checking', 'downloading', 'verifying'].includes(task.stage)
export const canCancel = (task: DownloadTask) => ['queued', 'checking', 'downloading', 'paused', 'verifying', 'waiting', 'failed'].includes(task.stage)
export function matchesClient(task: DownloadTask, gameId: string, instanceId: string | undefined, buildId: string | undefined) {
  return task.target.kind === 'client' && task.target.gameId === gameId &&
    (task.target.instanceId ? task.target.instanceId === instanceId : task.target.buildId === buildId)
}
/** Match instance-scoped prerequisites without showing another client's plugin transfers. */
export function matchesInstanceUpdate(task: DownloadTask, gameId: string, instanceId: string | undefined, buildId: string | undefined) {
  return matchesClient(task, gameId, instanceId, buildId) ||
    (task.target.kind === 'plugin' && !!instanceId && task.target.gameId === gameId && task.target.instanceId === instanceId)
}
export function formatBytes(bytes: number | null, locale: string): string {
  if (bytes == null) return '—'
  const amount = nonnegative(bytes), unit = amount === 0 ? 0 : Math.max(0, Math.min(3, Math.floor(Math.log(amount) / Math.log(1024))))
  return `${new Intl.NumberFormat(locale, { maximumFractionDigits: unit > 0 ? 1 : 0 }).format(amount / 1024 ** unit)} ${['B', 'KiB', 'MiB', 'GiB'][Math.max(0, unit)]}`
}

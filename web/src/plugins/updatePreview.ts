import { reactive } from 'vue'
import type { PluginSummary, PluginUpdateState } from '../bridge'
import type { DownloadTask } from '../downloads/model'

export const pluginUpdatePreviewAvailable = import.meta.env.DEV

/** Browser-only fixtures exercise the library controls without changing installed packages. */
export function createPluginUpdatePreview() {
  const plugins = reactive<PluginSummary[]>(import.meta.env.DEV ? __DEV_PLUGINS__.slice(0, 3).map(plugin => ({
    ...plugin, version: '0.2.0', available: true, canUninstall: false,
  })) : [])
  const tasks = reactive<Record<string, DownloadTask>>({})
  let timer: ReturnType<typeof setInterval> | undefined
  const update = (plugin: PluginSummary): PluginUpdateState => ({ id: plugin.id, installedVersion: plugin.version,
    status: plugin.version === '0.3.0' ? 'current' : 'available', version: '0.3.0', manifestSha256: null, sourceName: 'Fantasy Tale' })
  function start(plugin: PluginSummary) {
    if (!import.meta.env.DEV || tasks[plugin.id]) return
    const size = 96 * 1024 ** 2
    tasks[plugin.id] = { id: `plugin-preview-${plugin.id}`, mode: 'preview', target: {
      kind: 'plugin', gameId: plugin.game, packageId: plugin.id, name: plugin.name.en ?? plugin.id,
      fromVersion: plugin.version, toVersion: '0.3.0', sourceName: 'Fantasy Tale', sourceKind: 'http', directory: '',
    }, stage: 'checking', files: [{ path: 'plugin.zip', size, received: 0, stage: 'queued' }], currentPath: 'plugin.zip',
    bytesPerSecond: 0, speedSampleCount: 0, stageProgress: null, failure: null, resumeStage: 'downloading' }
    timer ??= setInterval(() => {
      for (const item of plugins) {
        const task = tasks[item.id]
        if (!task || task.stage === 'completed') continue
        if (task.stage === 'checking') { task.stage = 'downloading'; continue }
        if (task.stage === 'downloading') {
          task.bytesPerSecond = 2.4 * 1024 ** 2
          task.speedSampleCount++
          const file = task.files[0]!
          file.stage = 'downloading'; file.received = Math.min(file.size!, file.received + task.bytesPerSecond / 2)
          if (file.received === file.size) { file.stage = 'downloaded'; task.stage = 'verifying'; task.stageProgress = 0 }
        } else if (task.stage === 'verifying' || task.stage === 'installing') {
          task.stageProgress = Math.min(100, (task.stageProgress ?? 0) + 25)
          if (task.stageProgress === 100) {
            if (task.stage === 'verifying') { task.stage = 'installing'; task.stageProgress = 0 }
            else { task.stage = 'completed'; item.version = '0.3.0' }
          }
        }
      }
    }, 500)
  }
  function stop() { if (timer) clearInterval(timer); timer = undefined }
  return { plugins, update, task: (plugin: PluginSummary) => tasks[plugin.id], start, stop }
}

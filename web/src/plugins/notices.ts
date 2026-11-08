import { reactive } from 'vue'
import type { InfoBarSeverity } from '../controls/WInfoBar.vue'

export interface PluginNoticeOptions {
  persistent?: boolean
  durationMs?: number
}

export interface PluginNotice extends PluginNoticeOptions {
  id: number
  key: string
  severity: InfoBarSeverity
  message: string
}

export const pluginNotices = reactive<PluginNotice[]>([])
let nextId = 0

/** Keeps visible plugin messages bounded and replaces repeated notices from the same operation. */
export function showPluginNotice(key: string, severity: InfoBarSeverity, message: string, options: PluginNoticeOptions = {}): void {
  const prior = pluginNotices.findIndex(item => item.key === key)
  if (prior >= 0) pluginNotices.splice(prior, 1)
  pluginNotices.push({ id: ++nextId, key, severity, message, persistent: options.persistent, durationMs: options.durationMs })
  if (pluginNotices.length > 5) pluginNotices.splice(0, pluginNotices.length - 5)
}

/** A delayed close from an older operation must not dismiss its replacement. */
export function dismissPluginNotice(id: number): void {
  const index = pluginNotices.findIndex(notice => notice.id === id)
  if (index >= 0) pluginNotices.splice(index, 1)
}

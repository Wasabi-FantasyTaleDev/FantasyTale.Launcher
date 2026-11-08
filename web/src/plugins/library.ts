import type { PluginSummary, PluginUpdateState } from '../bridge'

export const pluginId = (plugin: Pick<PluginSummary, 'game' | 'id'>) => `${plugin.game}:${plugin.id}`

/** Keeps client-scoped update results off cards belonging to another game. */
export function libraryUpdate(plugin: Pick<PluginSummary, 'game' | 'id' | 'version'>,
  clientGame: string | undefined, updates: readonly PluginUpdateState[]): PluginUpdateState | undefined {
  return plugin.game === clientGame ? updates.find(update => update.id === plugin.id && update.installedVersion === plugin.version) : undefined
}

/** Resolves an exact library release without crossing game namespaces. */
export function libraryRelease<T extends Pick<PluginSummary, 'game' | 'id' | 'version'>>(
  packages: readonly T[], plugin: Pick<PluginSummary, 'game' | 'id'>, version: string | null | undefined,
): T | undefined {
  return packages.find(candidate => pluginId(candidate) === pluginId(plugin) && candidate.version === version)
}

/** Selects one release per plugin, retaining explicit browsing and client selections. */
export function libraryCards<T extends Pick<PluginSummary, 'game' | 'id' | 'version' | 'selection' | 'available'>>(
  packages: readonly T[], browsing: Readonly<Record<string, string>>,
): T[] {
  const groups = new Map<string, T[]>()
  for (const plugin of packages) {
    const id = pluginId(plugin)
    if (!groups.has(id)) groups.set(id, [])
    groups.get(id)!.push(plugin)
  }
  return [...groups.values()].map(releases => releases.find(p => p.version === browsing[pluginId(p)])
    ?? releases.find(p => p.selection) ?? releases.find(p => p.available) ?? releases[0]!)
}

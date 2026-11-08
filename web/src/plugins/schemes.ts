import type { InstancePlugin } from '../bridge'

export interface PluginScheme { id: string; name: string; builds: Record<string, InstancePlugin[]> }
export interface SchemeInstallation {
  scheme: string
  build: string
  task: string
  plugin: InstancePlugin
  previous?: InstancePlugin
  observed?: InstancePlugin
}
export interface SchemeLibrary { schemes: PluginScheme[]; selections: Record<string, string>; installations?: SchemeInstallation[] }
export const defaultScheme = 'default'
export function emptyLibrary(): SchemeLibrary { return { schemes: [{ id: defaultScheme, name: '', builds: {} }], selections: {} } }
export const schemeContext = (server: string, build: string) => JSON.stringify([server, build])
export const cloneSelection = (plugins: readonly InstancePlugin[] | undefined): InstancePlugin[] => (plugins ?? []).map(p => ({ ...p, options: { ...p.options } }))
const selectionStamp = (plugin: InstancePlugin | undefined) => plugin ? JSON.stringify([plugin.id, plugin.version, plugin.enabled,
  plugin.required ?? false, Object.entries(plugin.options ?? {}).sort(([a], [b]) => a.localeCompare(b))]) : ''

/** Snapshots the selected scheme of every compatible build, including builds without a registered client. */
export function planStoreInstallation(library: SchemeLibrary, item: { id: string; version: string; pluginDefinition?: {
  aliases: readonly string[]; options?: readonly { key: string }[] } | null },
  builds: readonly string[], server: string, clients: readonly { build: string; plugins: readonly InstancePlugin[] }[]): SchemeInstallation[] {
  const identities = [item.id, ...(item.pluginDefinition?.aliases ?? [])]
  return [...new Set(builds)].map(build => {
    const scheme = library.schemes.find(s => s.id === (library.selections[schemeContext(server, build)] ?? defaultScheme))!
    const saved = scheme.builds[build] ?? library.schemes.find(s => s.id === defaultScheme)!.builds[build] ?? []
    const observed = clients.find(client => client.build === build)?.plugins.find(plugin => identities.includes(plugin.id))
    const previous = saved.find(plugin => identities.includes(plugin.id))
    const options = { ...(previous?.options ?? observed?.options ?? {}) }
    if (item.pluginDefinition?.options) for (const key of Object.keys(options)) {
      if (!item.pluginDefinition.options.some(option => option.key === key)) delete options[key]
    }
    return { scheme: scheme.id, build, task: '', previous: cloneSelection(previous ? [previous] : [])[0],
      observed: cloneSelection(observed ? [observed] : [])[0], plugin: { id: item.id, version: item.version, enabled: true,
        required: previous?.required ?? observed?.required ?? false, options } }
  })
}

/** Saves accepted activation choices; a newer scheme edit supersedes the installation snapshot. */
export function rememberStoreInstallation(library: SchemeLibrary, plans: readonly SchemeInstallation[], task: string): void {
  for (const plan of plans) {
    const scheme = library.schemes.find(s => s.id === plan.scheme)
    if (!scheme) continue
    const saved = scheme.builds[plan.build] ?? library.schemes.find(s => s.id === defaultScheme)!.builds[plan.build] ?? []
    const current = saved.find(plugin => plugin.id === (plan.previous?.id ?? plan.plugin.id))
    if (selectionStamp(current) !== selectionStamp(plan.previous)) continue
    scheme.builds[plan.build] = [...cloneSelection(saved.filter(plugin => plugin.id !== current?.id && plugin.id !== plan.plugin.id)),
      ...cloneSelection([plan.plugin])]
    library.installations = (library.installations ?? []).filter(record => record.scheme !== plan.scheme || record.build !== plan.build
      || record.plugin.id !== plan.plugin.id)
    library.installations.push({ ...plan, task })
  }
}

/** Keeps an accepted choice pinned until the host enables it; subsequent client edits remain authoritative. */
export function captureSchemeInstallation(library: SchemeLibrary, scheme: PluginScheme, build: string,
  current: readonly InstancePlugin[], resolved: readonly string[], packages: readonly { id: string; version: string; aliases?: readonly string[] }[]) {
  let visible = [...current], known = [...resolved]
  const pending = []
  for (const record of library.installations ?? []) {
    if (record.scheme !== scheme.id || record.build !== build) { pending.push(record); continue }
    const saved = scheme.builds[build]?.find(plugin => plugin.id === record.plugin.id)
    if (selectionStamp(saved) !== selectionStamp(record.plugin)) continue
    const actual = current.find(plugin => plugin.id === record.plugin.id || plugin.id === record.observed?.id)
    if (actual?.id === record.plugin.id && actual.version === record.plugin.version && actual.enabled) continue
    if (selectionStamp(actual) !== selectionStamp(record.observed)) {
      scheme.builds[build] = (scheme.builds[build] ?? []).filter(plugin => plugin.id !== record.plugin.id)
      continue
    }
    pending.push(record)
    visible = visible.filter(plugin => plugin !== actual)
    known = known.filter(key => key !== JSON.stringify([record.plugin.id, record.plugin.version]))
  }
  library.installations = pending
  return captureSchemeSelection(scheme.builds[build] ?? [], visible, known, packages)
}

/** Cancellation restores only unchanged installation choices and preserves later manual edits. */
export function cancelStoreInstallation(library: SchemeLibrary, task: string): void {
  for (const record of library.installations ?? []) {
    if (record.task !== task) continue
    const scheme = library.schemes.find(s => s.id === record.scheme)
    const saved = scheme?.builds[record.build]
    if (!scheme || !saved || selectionStamp(saved.find(plugin => plugin.id === record.plugin.id)) !== selectionStamp(record.plugin)) continue
    scheme.builds[record.build] = [...cloneSelection(saved.filter(plugin => plugin.id !== record.plugin.id)),
      ...cloneSelection(record.previous ? [record.previous] : [])]
  }
  library.installations = library.installations?.filter(record => record.task !== task)
}

/** A failed installed release is shared by every build and scheme; other releases stay pinned. */
export function removeRecoveredRelease(library: SchemeLibrary, id: string, version: string): void {
  for (const scheme of library.schemes) for (const build of Object.keys(scheme.builds)) {
    scheme.builds[build] = scheme.builds[build]!.filter(plugin => plugin.id !== id || plugin.version !== version)
  }
  if (library.installations) library.installations = library.installations.filter(record => record.plugin.id !== id || record.plugin.version !== version)
}

/**
 * Captures plugin selections for a scheme while preserving imported/unresolved packages.
 *
 * Invariant:
 * When importing a scheme that references plugins not yet available in the local
 * inventory, those entries must remain pinned in the scheme data rather than being
 * discarded. When those packages are subsequently downloaded, they resolve seamlessly.
 */
export function captureSchemeSelection(saved: readonly InstancePlugin[], current: readonly InstancePlugin[], resolved: readonly string[],
  packages: readonly { id: string; version: string; aliases?: readonly string[] }[] = []) {
  const identity = (plugin: InstancePlugin) => JSON.stringify([plugin.id, plugin.version])
  const unresolved = saved.filter(plugin => !resolved.includes(identity(plugin)) && !current.some(item => identity(item) === identity(plugin)
    || packages.some(packageInfo => packageInfo.id === item.id && packageInfo.version === item.version && packageInfo.aliases?.includes(plugin.id))))
  const applied = current.filter(plugin => !unresolved.some(item => item.id === plugin.id))
  return { plugins: [...cloneSelection(applied), ...cloneSelection(unresolved)], resolved: applied.map(identity) }
}

/**
 * Parses and validates the serialized scheme library JSON from disk or localStorage.
 * Enforces schema integrity, ID uniqueness, and validates all nested plugin descriptors.
 * Throws an Error if the structure is corrupted, triggering graceful fallback to a default scheme.
 */
export function readLibrary(text: string | null): SchemeLibrary {
  if (!text) return emptyLibrary()
  const value = JSON.parse(text) as SchemeLibrary
  if (!value || !Array.isArray(value.schemes) || !value.schemes.some(s => s.id === defaultScheme)
    || new Set(value.schemes.map(s => s.id)).size !== value.schemes.length || !value.selections || typeof value.selections !== 'object'
    || Array.isArray(value.selections) || Object.values(value.selections).some(id => typeof id !== 'string'
      || !value.schemes.some(s => s.id === id))) throw new Error('Invalid plugin scheme library.')
  for (const scheme of value.schemes) {
    if (typeof scheme.id !== 'string' || !scheme.id || typeof scheme.name !== 'string' || !scheme.builds
      || typeof scheme.builds !== 'object' || Array.isArray(scheme.builds)) throw new Error('Invalid plugin scheme.')
    for (const plugins of Object.values(scheme.builds)) validatePlugins(plugins)
  }
  if (value.installations !== undefined) {
    if (!Array.isArray(value.installations)) throw new Error('Invalid scheme installations.')
    const seen = new Set<string>()
    for (const record of value.installations) {
      const key = JSON.stringify([record?.scheme, record?.build, record?.plugin?.id])
      if (!record || !value.schemes.some(scheme => scheme.id === record.scheme) || typeof record.build !== 'string' || !record.build
        || typeof record.task !== 'string' || !record.task || seen.has(key)) throw new Error('Invalid scheme installation.')
      validatePlugins([record.plugin]); if (record.previous) validatePlugins([record.previous]); if (record.observed) validatePlugins([record.observed])
      seen.add(key)
    }
  }
  return value
}
function validatePlugins(value: unknown): asserts value is InstancePlugin[] {
  if (!Array.isArray(value)) throw new Error('Invalid plugin selection.')
  const ids = new Set<string>()
  for (const p of value) {
    if (!p || typeof p.id !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(p.id) || ids.has(p.id)
      || typeof p.version !== 'string' || !p.version || typeof p.enabled !== 'boolean' || !p.options || typeof p.options !== 'object' || Array.isArray(p.options)
      || Object.values(p.options).some(v => !['string', 'boolean', 'number'].includes(typeof v) || typeof v === 'number' && !Number.isSafeInteger(v))) throw new Error('Invalid plugin selection.')
    ids.add(p.id)
  }
}
export type ImportedScheme = { game: string; name: string; builds: { id: string; plugins: InstancePlugin[] }[] }

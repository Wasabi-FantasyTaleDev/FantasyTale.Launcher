import type { Instance, PluginSummary } from '../bridge'
import type { StorePackage, StoreSource } from './types'
import type { StorePackageGroup, StorePackageOffer } from './groups'
import { compareVersions } from './versions'

export type LocalPlugin = Pick<PluginSummary, 'id' | 'game' | 'version'>

/** Former identifiers are declared by the successor package, never inferred from display names. */
export function matchesPluginIdentity(item: StorePackage, id: string): boolean {
  return item.id === id || !!item.pluginDefinition?.aliases.includes(id)
}

export interface StorePluginUpdate {
  key: string
  item: StorePackage
  source: StoreSource
  offers: StorePackageOffer[]
  installedVersion: string
}

/** Uses the newest local library release, including downloaded but disabled packages. */
export function installedPluginVersion(item: StorePackage, plugins: readonly LocalPlugin[]): string | undefined {
  if (item.kind !== 'plugin') return undefined
  const matching = plugins.filter(plugin => plugin.game === item.game && matchesPluginIdentity(item, plugin.id))
  const canonical = matching.filter(plugin => plugin.id === item.id)
  const versions = (canonical.length ? canonical : matching).map(plugin => plugin.version)
  // An incomparable local version cannot safely be treated as an older release.
  if (!versions.length || versions.some(version => compareVersions(version, version) === undefined)) return undefined
  return versions.reduce((newest, version) => compareVersions(version, newest)! > 0 ? version : newest)
}

/** Stable installations never silently opt into a prerelease channel. */
export function isPluginUpgrade(version: string, baseline: string): boolean {
  return (compareVersions(version, baseline) ?? 0) > 0 && (baseline.split('+')[0]!.includes('-') || !version.split('+')[0]!.includes('-'))
}

/** Returns the installed baseline only when this exact offer is a newer release. */
export function pluginUpdateFrom(item: StorePackage, plugins: readonly LocalPlugin[]): string | undefined {
  const baseline = installedPluginVersion(item, plugins)
  if (!baseline) return undefined
  const canonical = plugins.some(plugin => plugin.game === item.game && plugin.id === item.id)
  const migrated = !canonical && (baseline.split('+')[0]!.includes('-') || !item.version.split('+')[0]!.includes('-'))
  return migrated || isPluginUpgrade(item.version, baseline) ? baseline : undefined
}

/** Retains source priority among eligible upgrades and keeps every source's releases independent. */
export function storePluginUpdates(groups: readonly StorePackageGroup[], plugins: readonly LocalPlugin[],
  eligible: (item: StorePackage) => boolean): StorePluginUpdate[] {
  return groups.flatMap(group => {
    const offers = group.offers.map(offer => ({ source: offer.source,
      releases: offer.releases.filter(item => item.launcherCompatible !== false && eligible(item) && pluginUpdateFrom(item, plugins)) }))
      .filter(offer => offer.source.enabled && offer.releases.length)
    const first = offers[0]
    return first ? [{ key: group.key, item: first.releases[0]!, source: first.source, offers: group.offers,
      installedVersion: pluginUpdateFrom(first.releases[0]!, plugins)! }] : []
  })
}

/** Missing exact releases remain visible, disabled and stably ordered after usable sources. */
export function pluginSourceChoices(offers: readonly StorePackageOffer[], version: string, exact = false) {
  return offers.map(offer => {
    const match = offer.releases.find(release => release.version === version)
    return { value: offer.source.id, name: offer.source.name, version: match?.version ?? offer.releases[0]!.version,
      disabled: exact && (!match || match.launcherCompatible === false || !offer.source.enabled) }
  }).sort((left, right) => Number(left.disabled) - Number(right.disabled))
}

/** Updates transfer existing selections, including disabled ones; activation preserves their enabled state. */
export function pluginUpgradeTargets(item: StorePackage, instances: readonly Instance[]): string[] {
  return instances.filter(client => client.game === item.game && (!item.builds.length || item.builds.includes(client.build))
    && pluginUpdateFrom(item, client.plugins.map(plugin => ({ ...plugin, game: client.game }))))
    .map(client => client.id)
}

/** An explicit install-and-enable request includes compatible clients without an existing selection. */
export function pluginInstallationTargets(item: StorePackage, instances: readonly Instance[]): string[] {
  return instances.filter(client => client.game === item.game && (!item.builds.length || item.builds.includes(client.build)))
    .map(client => client.id)
}

/** Detects selection edits while an installation confirmation is open, including a removed selection. */
export function pluginSelectionStamp(item: StorePackage, instance: Instance): string {
  return JSON.stringify(instance.plugins.filter(plugin => matchesPluginIdentity(item, plugin.id)))
}

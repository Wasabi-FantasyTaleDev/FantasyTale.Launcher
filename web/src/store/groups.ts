import type { StoreCatalog, StorePackage, StoreSource } from './types'
import { newestFirst } from './versions'

export interface StorePackageOffer {
  source: StoreSource
  releases: StorePackage[]
}

export interface StorePackageGroup {
  key: string
  source: StoreSource
  releases: StorePackage[]
  offers: StorePackageOffer[]
}

/** Merges plugin display identities while retaining ordered, independent source offers. */
export function groupStorePackages(sources: readonly StoreSource[], catalogs: Readonly<Record<string, StoreCatalog>>,
  kind: string, sourceFilter = ''): StorePackageGroup[] {
  const groups = new Map<string, StorePackageGroup>()
  for (const source of sources) {
    if (!source.enabled || sourceFilter && source.id !== sourceFilter) continue
    for (const item of catalogs[source.id]?.packages ?? []) {
      if (item.kind !== kind) continue
      const key = JSON.stringify(kind === 'plugin' ? [kind, item.game, item.id] : [kind, source.id, item.game, item.id])
      let group = groups.get(key)
      if (!group) {
        group = { key, source, releases: [], offers: [] }
        groups.set(key, group)
      }
      let offer = group.offers.find(value => value.source.id === source.id)
      if (!offer) { offer = { source, releases: [] }; group.offers.push(offer) }
      offer.releases.push(item)
    }
  }
  for (const group of groups.values()) {
    for (const offer of group.offers) offer.releases = newestFirst(offer.releases)
    group.releases = group.offers[0]!.releases
  }
  return [...groups.values()]
}

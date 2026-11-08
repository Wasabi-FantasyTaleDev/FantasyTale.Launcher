import type { BuildSummary } from './bridge'

/** Formats descriptive version metadata without changing the client's title or wire identity. */
export function clientVersionCaption(build: Pick<BuildSummary, 'id' | 'branch' | 'region' | 'protocolVersion' | 'releaseVersion' | 'patchVersion'>): string | undefined {
  const region = (build.region || build.branch).toUpperCase()
  const wire = build.protocolVersion === undefined ? build.id : `${region.toLowerCase()}-${build.protocolVersion}`
  const release = build.releaseVersion.trim()
  const patch = build.patchVersion?.trim()
  if (release) return `${region} Ver.${release} (${wire})`
  if (patch) return `${region} Patch V${patch} (${wire})`
  return undefined
}

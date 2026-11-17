import type { BuildSummary, Instance } from './bridge'

/** Formats descriptive version metadata without changing the client's title or wire identity. */
export function clientVersionCaption(build: Pick<BuildSummary, 'id' | 'branch' | 'region' | 'protocolVersion' | 'releaseVersion' | 'patchVersion'>): string | undefined {
  const region = (build.region || build.branch).toUpperCase()
  const wire = clientWireId(build)
  const release = build.releaseVersion.trim()
  const patch = build.patchVersion?.trim()
  if (release) return `${region} Ver.${release} (${wire})`
  if (patch) return `${region} Patch V${patch} (${wire})`
  return undefined
}

/** The build's wire identity, such as kr-186: its region and protocol version, or its id without a protocol version. */
export function clientWireId(build: Pick<BuildSummary, 'id' | 'branch' | 'region' | 'protocolVersion'>): string {
  return build.protocolVersion === undefined ? build.id : `${(build.region || build.branch).toLowerCase()}-${build.protocolVersion}`
}

/** Whether a build belongs in version lists: hidden builds appear once a client of that build is registered. */
export function buildListed(game: string, build: Pick<BuildSummary, 'id' | 'hidden'>, instances: readonly Pick<Instance, 'game' | 'build'>[]): boolean {
  return !build.hidden || instances.some(instance => instance.game === game && instance.build === build.id)
}

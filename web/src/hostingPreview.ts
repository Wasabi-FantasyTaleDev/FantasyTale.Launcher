import type { HostingOverview, HostingProfile, HostingSession } from './bridge'
import builds from './preview-builds.json'

/** Read-only display fixtures are enabled only by an explicit browser preview URL. */
export const hostingPreview = import.meta.env.DEV && typeof window !== 'undefined'
  && new URLSearchParams(window.location.hash.split('?')[1] ?? '').get('hostingPreview') === '1'

function createHosting(): HostingOverview {
  const profile: HostingProfile = { id: 'preview-local', game: 'bubblefighter', build: builds.find(b => b.supportsHosting)?.id ?? builds[0]!.id,
    name: 'Fantasy Tale', packagePath: 'server-packages/fantasy-tale', mode: 'private', advertisedHost: '127.0.0.1',
    gamePort: 17230, relayPort: 17231, apiPort: 18090, adminPort: 18094, temporaryData: false, storageMode: 'local', requireAuthentication: false }
  const session: HostingSession = { profileId: profile.id, runId: 'preview-run', state: 'ready', processId: 0,
    startedAt: new Date().toISOString(), exitCode: null, failure: null, forcedStop: false,
    apiUrl: 'http://127.0.0.1:18090/', adminUrl: 'http://127.0.0.1:18094/', logPath: 'hosting/preview/server.log' }
  return { profiles: [profile], sessions: [session] }
}
let overview = createHosting()
export function previewHosting(): HostingOverview { return structuredClone(overview) }
export function savePreviewHosting(profile: HostingProfile): HostingProfile {
  overview.profiles = [...overview.profiles.filter(p => p.id !== profile.id), structuredClone(profile)]
  return structuredClone(profile)
}
export function changePreviewHosting(id: string, state: HostingSession['state']): HostingSession {
  const previous = overview.sessions.find(s => s.profileId === id) ?? { ...createHosting().sessions[0]!, profileId: id }
  const session = { ...previous, state }
  overview.sessions = [...overview.sessions.filter(s => s.profileId !== id), session]
  return structuredClone(session)
}
export function deletePreviewHosting(id: string): null {
  overview.profiles = overview.profiles.filter(p => p.id !== id)
  overview.sessions = overview.sessions.filter(s => s.profileId !== id)
  return null
}

export function previewHostingLog(): string[] {
  return ['[INFO] Starting local server', '[INFO] Loading profile and saved player data',
    '[INFO] Game listener ready on 127.0.0.1:17230', '[INFO] Launcher API ready on 127.0.0.1:18090',
    '[INFO] Administration ready on 127.0.0.1:18094', '[INFO] Server is ready; waiting for a player']
}

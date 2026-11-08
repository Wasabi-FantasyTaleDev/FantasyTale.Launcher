import type { DirectoryEntry, Instance, OnlinePlayerList, ServerCatalog } from './bridge'

/** Explicit browser-only fixture; it never enables desktop room publication or sends network requests. */
export const multiplayerPreview = import.meta.env.DEV && typeof window !== 'undefined' && window.location.hash.includes('multiplayerPreview=1')

export function previewInstances(): Instance[] {
  return ['cn-279', 'kr-812'].map(build => ({ id: `preview-installed-${build}`, name: `Installed ${build} client (preview)`,
    game: 'bubblefighter', build, endpoint: null, plugins: [], clientInstalled: true }))
}

export function previewDirectory(): DirectoryEntry[] {
  return [
    { id: 'preview-community', kind: 'server', expiresAt: null, details: {
      game: 'bubblefighter', name: 'Fantasy Tale Community', apiUrl: 'https://community.example/', builds: ['cn-406', 'kr-812'], description: '',
      presentation: { motd: 'Welcome to Fantasy Tale\n\nClassic matches, community events and a place to play together. Meet other players, revisit familiar maps and enjoy the game at your own pace.\n\nChoose a supported client below. Required and recommended plugins are listed in the sidebar, and the online player list is available before you join.',
        images: import.meta.env.DEV ? [new URL('./assets/preview/server-gallery-1.svg', import.meta.url).href, new URL('./assets/preview/server-gallery-2.svg', import.meta.url).href] : [],
        onlinePlayers: 128, peakOnlinePlayers: 512, state: 'online', rosterEnabled: true },
      clientPolicy: { version: 1, sources: [{ name: 'community', kind: 'http', baseUrl: 'https://plugins.example/', catalogPath: 'source.toml' }], plugins: [
        { id: 'bubblefighter.fantasy.login-fix-cn', rules: [{ id: 'needed', type: 'require', builds: ['cn-406'] }] },
        { id: 'bubblefighter.fantasy.auto-dash', rules: [{ id: 'offer', type: 'recommend', builds: [], sources: [] }] },
        { id: 'example.custom-effect', rules: [{ id: 'off', type: 'disable', builds: [] }] }] },
    } },
    { id: 'preview-room', kind: 'room', expiresAt: '2099-01-01T00:00:00Z', details: {
      game: 'bubblefighter', name: 'Classic Practice Room', apiUrl: 'https://room.example/', builds: ['cn-406'], description: 'A room for friends and practice matches.',
      presentation: { onlinePlayers: 6, peakOnlinePlayers: 12, playerLimit: 16, state: 'online', whitelist: true, rosterEnabled: true },
    } },
    { id: 'preview-maintenance', kind: 'server', expiresAt: null, details: {
      game: 'bubblefighter', name: 'Fantasy Tale Test Realm', apiUrl: 'https://test.example/', builds: ['kr-812'], description: 'Scheduled maintenance in progress.',
      presentation: { onlinePlayers: -1, peakOnlinePlayers: -1, state: 'maintenance', rosterEnabled: false },
    } },
    { id: 'preview-offline', kind: 'server', expiresAt: null, details: {
      game: 'bubblefighter', name: 'Classic Archive', apiUrl: 'https://archive.example/', builds: ['cn-279'], description: 'Offline preview.',
      presentation: { onlinePlayers: 0, peakOnlinePlayers: 24, playerLimit: 32, state: 'offline', rosterEnabled: false },
    } },
    ...previewExtraCards('server'),
    ...previewExtraCards('room'),
  ]
}

function previewExtraCards(kind: 'server' | 'room'): DirectoryEntry[] {
  const names = kind === 'server'
    ? ['Maple Grove', 'Blue Harbor', 'Weekend Arena', 'Cloud Garden', 'Moonlight Club', 'Practice Grounds', 'Rainbow Valley', 'Snow Village', 'Summer Coast', 'Classic League', 'Starry Night', 'Friendly Matches']
    : ['Evening Practice', 'Friends Only', 'Classic Maps', 'Casual Matches', 'Training Session', 'Weekend Party', 'New Players', 'Map Explorer', 'Team Practice', 'Quick Match', 'Late Night']
  return names.map((name, index) => ({
    id: `preview-extra-${kind}-${index}`, kind, expiresAt: kind === 'room' ? '2099-01-01T00:00:00Z' : null,
    details: {
      game: 'bubblefighter', name, apiUrl: `https://${kind}-${index}.example/`,
      builds: index % 2 ? ['kr-812'] : ['cn-279', 'cn-406'],
      description: index % 2 ? 'Classic maps and friendly matches. Everyone is welcome.' : 'A place to practice, meet friends and enjoy familiar maps.',
      presentation: {
        onlinePlayers: index % 6 === 4 ? -1 : index % 7 === 5 ? 0 : (index + 1) * 7,
        peakOnlinePlayers: index % 6 === 4 ? -1 : (index + 1) * 23,
        state: index % 6 === 4 ? 'maintenance' : index % 7 === 5 ? 'offline' : 'online',
        playerLimit: kind === 'room' ? 128 : undefined, whitelist: index % 5 === 2,
        rosterEnabled: index % 6 !== 4 && index % 7 !== 5,
        images: import.meta.env.DEV && index % 3 === 0 ? [new URL('./assets/preview/server-gallery-2.svg', import.meta.url).href] : [],
      },
    },
  }))
}
export function previewRoster(): OnlinePlayerList {
  return { schema: 1, enabled: true, players: Array.from({ length: 1250 }, (_, index) => ({ name: `Player${String(index + 1).padStart(4, '0')}`, level: index % 3 ? index % 100 + 1 : null })) }
}
export function previewServerCatalog(base: ServerCatalog): ServerCatalog {
  return { ...base, presentation: previewDirectory()[0]!.details.presentation, clientPolicy: previewDirectory()[0]!.details.clientPolicy }
}

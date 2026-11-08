import { fileURLToPath } from 'node:url'
import { existsSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { execFileSync } from 'node:child_process'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

const gamesRoot = fileURLToPath(new URL('../games', import.meta.url)).replace(/\\/g, '/')
const previewProject = fileURLToPath(new URL('../tools/web-preview/FantasyTale.WebPreview.csproj', import.meta.url))
const documents = JSON.parse(execFileSync('dotnet', ['run', '--project', previewProject, '-c', 'Release', '--verbosity', 'quiet', '--', gamesRoot], { encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 }))
const parseFile = (path: string) => documents[path.replace(/\\/g, '/')]
const privateIconFont = fileURLToPath(new URL('./private-assets/SEGOEICONS.TTF', import.meta.url))
const usePrivateIconFont = process.env.FT_SYSTEM_FONTS_ONLY !== '1' && existsSync(privateIconFont)
const definitions = readdirSync(gamesRoot, { withFileTypes: true })
  .filter(entry => entry.isDirectory() && existsSync(join(gamesRoot, entry.name, 'game.toml')))
  .map(entry => parseFile(join(gamesRoot, entry.name, 'game.toml')).game as {
    id: string; navigation_entries?: string[]; rooms_enabled?: boolean; local_hosting_enabled?: boolean
  })
const navigation = Object.fromEntries(definitions.map(game => [game.id, game.navigation_entries ?? ['home', 'downloads', 'accounts']]))
const availability = Object.fromEntries(definitions.map(game => [game.id, {
  roomsEnabled: game.rooms_enabled ?? true, localHostingEnabled: game.local_hosting_enabled ?? true,
}]))
const bundledPlugins = readdirSync(gamesRoot, { withFileTypes: true }).filter(entry => entry.isDirectory())
  .flatMap(entry => {
    const folder = join(gamesRoot, entry.name, 'plugins')
    if (!existsSync(folder)) return []
    return readdirSync(folder, { withFileTypes: true }).filter(plugin => plugin.isDirectory() && existsSync(join(folder, plugin.name, 'plugin.toml')))
      .map(plugin => {
        const definition = parseFile(join(folder, plugin.name, 'plugin.toml')).plugin as {
          id: string; game: string; version: string; kind: string; name: Record<string, string>; description?: Record<string, string>; authors?: string[]
        }
        return { ...definition, description: definition.description ?? null, builtIn: true, available: false, required: false, selection: null, options: [] }
      })
  })
const bundledServers = readdirSync(gamesRoot, { withFileTypes: true }).filter(entry => entry.isDirectory())
  .flatMap(entry => {
    const folder = join(gamesRoot, entry.name, 'servers')
    return existsSync(folder) ? readdirSync(folder).filter(file => file.endsWith('.toml')).map(file => {
      const profile = parseFile(join(folder, file)) as unknown as {
        server: { id: string; game: string; name: string; api_url?: string; host?: string; port?: number; bundled_instances?: boolean; icon_asset?: string }
        presentation?: { motd?: string; summary?: string; motd_translations?: Record<string, string>; summary_translations?: Record<string, string>; icon_url?: string; icon_url_translations?: Record<string, string>; images?: string[]; images_translations?: Record<string, string[]> }
      }
      const server = profile.server, presentation = profile.presentation
      return { id: server.id, game: server.game, name: server.name, apiUrl: server.api_url ?? null,
        endpoint: server.host ? { host: server.host, port: server.port } : null, bundledInstances: server.bundled_instances ?? false,
        builtIn: true, iconAsset: server.icon_asset ?? null, presentation: presentation ? {
          motd: presentation.motd ?? '', summary: presentation.summary ?? '', motdTranslations: presentation.motd_translations ?? {},
          summaryTranslations: presentation.summary_translations ?? {}, iconUrl: presentation.icon_url ?? null,
          iconUrlTranslations: presentation.icon_url_translations ?? {}, images: presentation.images ?? [], imagesTranslations: presentation.images_translations ?? {}, rosterEnabled: false,
        } : null }
    }) : []
  })

// The host serves dist/ through app://localhost/, so asset URLs stay relative.
export default defineConfig({
  base: './',
  plugins: [vue(), {
    name: 'private-icon-font',
    enforce: 'pre',
    transform(code, id) {
      if (!usePrivateIconFont || !id.replace(/\\/g, '/').endsWith('/src/styles/tokens.css')) return
      return `@font-face { font-family: 'Segoe Fluent Icons'; src: local('Segoe Fluent Icons'), url('../../private-assets/SEGOEICONS.TTF') format('truetype'); font-display: swap; }\n${code}`
    },
  }],
  // Lets the browser-only dev fallback show game media straight from the repository.
  define: { __PRIVATE_ICON_FONT__: JSON.stringify(usePrivateIconFont),
    __DEV_GAMES_URL__: JSON.stringify(`/@fs/${gamesRoot.replace(/^\//, '')}`),
    __DEV_GAME_NAVIGATION__: JSON.stringify(navigation), __DEV_GAME_AVAILABILITY__: JSON.stringify(availability), __DEV_SERVERS__: JSON.stringify(bundledServers),
    __DEV_PLUGINS__: JSON.stringify(bundledPlugins) },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    chunkSizeWarningLimit: 2048,
  },
  server: {
    port: 5317,
    strictPort: true,
    fs: { allow: ['..'] },
  },
})

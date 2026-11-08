import { createRouter, createWebHashHistory, type RouteRecordRaw } from 'vue-router'
import type { Component } from 'vue'
import HomePage from './pages/HomePage.vue'
import InstancesPage from './pages/InstancesPage.vue'
// deprecated: merged into Multiplayer > Servers; the legacy page is no longer maintained.
// import ServersPage from './pages/ServersPage.vue'
import DownloadsPage from './pages/DownloadsPage.vue'
import PluginsPage from './pages/PluginsPage.vue'
import AccountsPage from './pages/AccountsPage.vue'
import SettingsPage from './pages/SettingsPage.vue'
import StorePage from './pages/StorePage.vue'
import HostingPage from './pages/HostingPage.vue'
import MultiplayerPage from './pages/MultiplayerPage.vue'
import DeveloperPage from './pages/DeveloperPage.vue'
import type { GlyphName } from './icons'

/** One navigation entry: route, Fluent icon glyph name, label key and pane placement. */
export interface NavEntry {
  tag: string
  icon: GlyphName
  label: string
  footer?: boolean
}

export const navEntries: NavEntry[] = [
  { tag: 'home', icon: 'home', label: 'nav.home' },
  { tag: 'instances', icon: 'gamepad', label: 'nav.instances' },
  // deprecated: merged into Multiplayer > Servers; no further maintenance.
  // { tag: 'servers', icon: 'globe', label: 'nav.servers' },
  { tag: 'multiplayer', icon: 'globe', label: 'multiplayer.title' },
  { tag: 'hosting', icon: 'hardDrive', label: 'hosting.title' },
  { tag: 'store', icon: 'shop', label: 'store.title' },
  { tag: 'downloads', icon: 'download', label: 'nav.downloads' },
  { tag: 'plugins', icon: 'puzzle', label: 'nav.plugins' },
  { tag: 'accounts', icon: 'contact', label: 'nav.accounts' },
  { tag: 'settings', icon: 'settings', label: 'nav.settings', footer: true },
]

const pages: Record<string, Component> = {
  home: HomePage,
  instances: InstancesPage,
  // servers: ServersPage,
  downloads: DownloadsPage,
  plugins: PluginsPage,
  accounts: AccountsPage,
  settings: SettingsPage,
  store: StorePage,
  hosting: HostingPage,
  multiplayer: MultiplayerPage,
}

const routes: RouteRecordRaw[] = [
  { path: '/', redirect: '/home' },
  { path: '/developer', name: 'developer', component: DeveloperPage },
  // Keep existing bookmarks pointed at the maintained server workspace.
  { path: '/servers', name: 'servers', redirect: { name: 'multiplayer', query: { tab: 'server' } } },
  ...navEntries.map((entry) => ({ path: `/${entry.tag}`, name: entry.tag, component: pages[entry.tag], meta: { keepOnQueryChange: entry.tag === 'multiplayer' } })),
]

// Hash history: the host serves a static bundle from app://localhost/.
export const router = createRouter({ history: createWebHashHistory(), routes })

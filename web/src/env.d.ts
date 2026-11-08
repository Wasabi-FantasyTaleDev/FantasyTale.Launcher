/// <reference types="vite/client" />

/** Whether this build includes a locally supplied private icon font. */
declare const __PRIVATE_ICON_FONT__: boolean

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<object, object, unknown>
  export default component
}

/** Message channel Photino injects into the web view. */
interface PhotinoExternal {
  sendMessage(message: string): void
  receiveMessage(callback: (message: string) => void): void
}

interface External extends Partial<PhotinoExternal> {}

/** Repository games folder as a dev-server URL (vite.config.ts). */
declare const __DEV_GAMES_URL__: string
/** Menu declarations read from repository game.toml files for browser previews. */
declare const __DEV_GAME_NAVIGATION__: Record<string, string[]>

/** Reversible game feature switches from repository definitions. */
declare const __DEV_GAME_AVAILABILITY__: Record<string, { roomsEnabled: boolean; localHostingEnabled: boolean }>

/** Bundled server profiles read from each game's servers directory for browser previews. */
declare const __DEV_SERVERS__: import('./bridge').Server[]

/** Display metadata from bundled plugin definitions for browser previews. */
declare const __DEV_PLUGINS__: import('./bridge').PluginSummary[]

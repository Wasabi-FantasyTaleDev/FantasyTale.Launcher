import type { LocalizedText } from '../bridge'

export interface StoreSource {
  id: string
  name: string
  kind: 'alist' | 'http' | 'github'
  baseUrl: string | null
  catalogPath: string
  enabled: boolean
  builtIn: boolean
  preferIp?: boolean | null
}

export interface StorePackage {
  id: string
  kind: 'client' | 'plugin' | 'runtime' | 'launcher' | 'server'
  game: string
  builds: string[]
  version: string
  name: LocalizedText
  description: LocalizedText
  publisher: string
  authors?: string[]
  uploader?: string
  size: number
  manifest: string
  manifestSha256: string
  minimumLauncherVersion?: string
  launcherCompatible?: boolean
  pluginDefinition?: { id: string; aliases: string[]; options?: { key: string }[] } | null
}

export interface StoreCatalog {
  id: string
  name: LocalizedText
  packages: StorePackage[]
}

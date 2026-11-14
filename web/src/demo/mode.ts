import { reactive } from 'vue'

/** LocalStorage key storing explicit user preference for UI Demo Mode. */
export const DEMO_MODE_STORAGE_KEY = 'uiDemoMode'

type DemoModeListener = (enabled: boolean) => void
type DemoResetListener = () => void

const modeListeners = new Set<DemoModeListener>()
const resetListeners = new Set<DemoResetListener>()

/**
 * Resolves whether UI Demo Mode should be active on startup.
 * Explicit user choice ('on' / 'off') always wins; otherwise Vite dev in browser defaults to enabled.
 */
export function resolveDemoMode(
  stored: string | null | undefined,
  isDev: boolean,
  isNativeHost: boolean,
  isTest: boolean,
): boolean {
  if (stored === 'on') return true
  if (stored === 'off') return false
  if (isTest) return false
  return isDev && !isNativeHost
}

function readStoredDemoMode(): string | null {
  try {
    return globalThis.localStorage?.getItem(DEMO_MODE_STORAGE_KEY) ?? null
  } catch {
    return null
  }
}

function writeStoredDemoMode(value: 'on' | 'off'): void {
  try {
    globalThis.localStorage?.setItem(DEMO_MODE_STORAGE_KEY, value)
  } catch {
    // Ignore storage failures in restricted or non-browser environments.
  }
}

function readRuntimeFlags(): { isDev: boolean; isNativeHost: boolean; isTest: boolean } {
  const env = (import.meta as ImportMeta & { env?: { DEV?: boolean; MODE?: string } }).env
  const isDev = Boolean(env?.DEV)
  const isTest = env?.MODE === 'test'
  const ext = (globalThis as { external?: { sendMessage?: unknown } }).external
  const isNativeHost = typeof ext?.sendMessage === 'function'
  return { isDev, isNativeHost, isTest }
}

function resolveInitialDemoMode(): boolean {
  const { isDev, isNativeHost, isTest } = readRuntimeFlags()
  return resolveDemoMode(readStoredDemoMode(), isDev, isNativeHost, isTest)
}

/** Reactive UI Demo Mode state observed across pages and bridge calls. */
export const demoMode = reactive<{ enabled: boolean }>({
  enabled: resolveInitialDemoMode(),
})

/** Enables or disables UI Demo Mode and persists the explicit choice. */
export function setDemoMode(enabled: boolean): void {
  writeStoredDemoMode(enabled ? 'on' : 'off')
  if (demoMode.enabled === enabled) return
  demoMode.enabled = enabled
  for (const listener of [...modeListeners]) {
    listener(enabled)
  }
}

/** Subscribes to Demo Mode enable/disable transitions. */
export function onDemoModeChange(listener: DemoModeListener): () => void {
  modeListeners.add(listener)
  return () => modeListeners.delete(listener)
}

/** Subscribes to Demo Mode workspace reset requests. */
export function onDemoReset(listener: DemoResetListener): () => void {
  resetListeners.add(listener)
  return () => resetListeners.delete(listener)
}

/** Resets in-memory Demo Mode fixtures and notifies active mock hosts. */
export function triggerDemoReset(): void {
  for (const listener of [...resetListeners]) {
    listener()
  }
}

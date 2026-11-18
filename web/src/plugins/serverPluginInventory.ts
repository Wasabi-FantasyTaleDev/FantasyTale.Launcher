import type { PluginSummary } from '../bridge'

/**
 * Last installed-plugin inventory read per client, kept for the life of the page so reopening a server's plugin
 * list can draw the known state at once while a fresh read replaces it. The key is the client id, empty when no
 * client matches the server's builds.
 */
const inventories = new Map<string, readonly PluginSummary[]>()

/** The remembered inventory for a client, or undefined when none has been read yet. */
export function cachedInventory(instanceId: string): readonly PluginSummary[] | undefined { return inventories.get(instanceId) }

/** Remembers the inventory just read for a client. */
export function rememberInventory(instanceId: string, plugins: readonly PluginSummary[]): void { inventories.set(instanceId, plugins) }

/** Whether a plugin row is installed and enabled, missing, or not yet known because the inventory has not been read. */
export type InstallState = 'unknown' | 'installed' | 'missing'

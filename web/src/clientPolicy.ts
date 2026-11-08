import type { ClientPolicy, PolicyPluginEntry, ServerStoreSource } from './bridge'

/** The require, recommend and disable rules of a policy as the server pages list them. */
export function policyPlugins(policy?: ClientPolicy | null): PolicyPluginEntry[] {
  return (policy?.plugins ?? []).flatMap(plugin => plugin.rules.filter(rule => rule.type === 'require' || rule.type === 'recommend' || rule.type === 'disable')
    .map(rule => ({ id: plugin.id, builds: rule.builds, required: rule.type === 'require', disabled: rule.type === 'disable' })))
}
/** The store sources a policy offers for the player to add. */
export function policySources(policy?: ClientPolicy | null): ServerStoreSource[] { return policy?.sources ?? [] }

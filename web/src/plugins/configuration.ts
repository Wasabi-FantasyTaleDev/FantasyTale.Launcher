import type { PluginSummary, PluginSettingsPage } from '../bridge'

/** Explicit page declarations completely determine a package's settings navigation. */
export function settingsPages(plugin: Pick<PluginSummary, 'configurationPages' | 'configurationPage' | 'options'> | undefined, standard: string, custom: string): PluginSettingsPage[] {
  if (!plugin) return []
  if (plugin.configurationPages) return plugin.configurationPages
  const options = plugin.options.map(option => option.key)
  return [{ id: 'standard', kind: 'native', title: { en: standard }, options },
    ...(plugin.configurationPage ? [{ id: 'custom', kind: 'html' as const, title: { en: custom }, options }] : [])]
}

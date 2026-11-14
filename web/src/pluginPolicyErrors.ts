/** Host errors shaped `code:plugin-id` that name the plugin a client policy or server rule is about. */
export const pluginPolicyErrorCodes = ['server_plugin_required', 'server_plugin_version_low', 'server_plugin_version_high',
  'server_plugin_policy_conflict', 'client_policy_conflict', 'client_policy_source_conflict', 'client_policy_disabled',
  'plugin_policy_require_unmet', 'plugin_policy_required_by', 'plugin_policy_conflict_active'] as const

/** Translates a coded plugin policy error, or returns undefined for any other error text. */
export function pluginPolicyError(error: string, translate: (key: string, values: Record<string, string>) => string): string | undefined {
  const separator = error.indexOf(':')
  if (separator <= 0) return undefined
  const code = error.slice(0, separator), plugin = error.slice(separator + 1).trim()
  if (!plugin || !(pluginPolicyErrorCodes as readonly string[]).includes(code)) return undefined
  return translate(`pluginPolicyErrors.${code}`, { plugin })
}

import type { BuildSummary, PluginSummary } from './bridge'

/** Presents distribution and translation languages together, with server restrictions applied last. */
export function supportedClientLanguages(build: BuildSummary | undefined, fallback: string[] = [], allowed?: string[] | null): string[] {
  const native = build?.nativeLanguage || fallback[0] || ''
  return [...new Set([native, ...Object.keys(build?.additionalLanguages ?? {})])]
    .filter(code => code && (!allowed || allowed.includes(code)))
}

/** An installed, enabled, compatible file package is required before choosing its translation. */
export function clientLanguageInstalled(build: BuildSummary | undefined, code: string, plugins: PluginSummary[]): boolean {
  const id = build?.additionalLanguages?.[code]
  return !id || plugins.some(plugin => plugin.id === id && plugin.kind === 'files' && plugin.available
    && plugin.launcherCompatible !== false && plugin.languagePackCompatible !== false && plugin.selection?.enabled)
}

import type { ServerPresentation } from './bridge'

/** Selects plain-text server descriptions without requiring any particular translation. */
export function serverDescription(presentation: ServerPresentation | null | undefined, language: string, fallback = '', preferSummary = false): string {
  const full = Object.entries(presentation?.motdTranslations ?? {}).filter(([, text]) => text.trim())
  const summary = Object.entries(presentation?.summaryTranslations ?? {}).filter(([, text]) => text.trim())
  const entries = preferSummary ? [...summary, ...full] : [...full, ...summary]
  const find = (tag: string) => entries.find(([key]) => key.toLowerCase() === tag.toLowerCase())?.[1]
  const plain = preferSummary ? presentation?.summary || presentation?.motd : presentation?.motd || presentation?.summary
  return find(language) || find(language.split('-')[0]!) || find('en') || plain || entries[0]?.[1] || fallback
}

/** Keeps local translations and artwork when a catalog supplies only part of its display metadata. */
export function mergeServerPresentation(local?: ServerPresentation | null, remote?: ServerPresentation | null): ServerPresentation | null | undefined {
  if (!remote) return local
  return { ...local, ...remote, motd: remote.motd ?? local?.motd,
    summary: remote.summary ?? local?.summary,
    motdTranslations: mergeTranslations(local?.motdTranslations, remote.motdTranslations),
    summaryTranslations: mergeTranslations(local?.summaryTranslations, remote.summaryTranslations),
    iconUrlTranslations: mergeTranslations(local?.iconUrlTranslations, remote.iconUrlTranslations),
    imagesTranslations: mergeTranslations(local?.imagesTranslations, remote.imagesTranslations),
    iconUrl: remote.iconUrl === undefined ? local?.iconUrl : remote.iconUrl, images: remote.images ?? local?.images }
}

function translatedResource<T>(values: Record<string, T> | undefined, language: string, present: (value: T) => boolean, plain?: T): T | undefined {
  const entries = Object.entries(values ?? {}).filter(([, value]) => present(value))
  const find = (tag: string) => entries.find(([key]) => key.toLowerCase() === tag.toLowerCase())?.[1]
  return find(language) ?? find(language.split('-')[0]!) ?? find('en') ?? (plain !== undefined && present(plain) ? plain : undefined) ?? entries[0]?.[1]
}

/** Resolves the icon independently of the description and gallery language. */
export function serverIconReference(presentation: ServerPresentation | null | undefined, language: string): string | undefined {
  return translatedResource(presentation?.iconUrlTranslations, language, value => !!value.trim(), presentation?.iconUrl ?? undefined)
}

/** Empty localized galleries fall back without hiding screenshots provided in another language. */
export function serverGallery(presentation: ServerPresentation | null | undefined, language: string): string[] {
  return translatedResource(presentation?.imagesTranslations, language, value => value.length > 0, presentation?.images) ?? []
}

/** Native language names keep the translation selector understandable in every launcher locale. */
export const descriptionLanguages: Record<string, string> = {
  'zh-CN': '简体中文', 'zh-TW': '繁體中文', zh: '中文', en: 'English', ko: '한국어', id: 'Bahasa Indonesia', vi: 'Tiếng Việt', th: 'ไทย',
}

function mergeTranslations<T>(local: Record<string, T> | undefined, remote: Record<string, T> | undefined): Record<string, T> {
  return Object.fromEntries([...Object.entries(local ?? {}), ...Object.entries(remote ?? {})].map(([key, value]) => [key.toLowerCase(), value]))
}

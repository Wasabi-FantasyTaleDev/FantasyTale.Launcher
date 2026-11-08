import { createI18n } from 'vue-i18n'
import { calendarDateLabel } from './calendar'
import zhCN from '../locales/zh-CN.json'
import zhTW from '../locales/zh-TW.json'
import ko from '../locales/ko.json'
import id from '../locales/id.json'
import vi from '../locales/vi.json'
import th from '../locales/th.json'
import en from '../locales/en.json'

/**
 * UI locale selection is independent of the client game's supported languages.
 * Untranslated messages fall back to English.
 */
export const languages = ['zh-CN', 'zh-TW', 'ko', 'id', 'vi', 'th', 'en'] as const
export type Language = (typeof languages)[number]

/**
 * Maps a BCP 47 language tag from the operating system or browser environment
 * to one of the supported UI languages, falling back to English when no match exists.
 *
 * @param tag System language code (e.g., 'zh-CN', 'zh-HK', 'th-TH', 'en-US')
 * @returns The resolved canonical UI Language
 */
export function pickLanguage(tag: string): Language {
  const lower = tag.toLowerCase()
  if (lower.startsWith('zh')) return /-(tw|hk|mo|hant)/.test(lower) ? 'zh-TW' : 'zh-CN'
  if (lower.startsWith('ko')) return 'ko'
  if (lower.startsWith('id') || lower.startsWith('in')) return 'id'
  if (lower.startsWith('vi')) return 'vi'
  if (lower.startsWith('th')) return 'th'
  return 'en'
}

const stored = localStorage.getItem('language') as Language | null

/**
 * Central Vue I18n application instance configured in Composition API mode.
 * Loads comprehensive locale bundles from unified JSON definitions.
 */
export const i18n = createI18n({
  legacy: false,
  locale: stored && (languages as readonly string[]).includes(stored) ? stored : pickLanguage(navigator.language),
  fallbackLocale: 'en',
  messages: {
    'zh-CN': zhCN,
    'zh-TW': zhTW,
    ko,
    id,
    vi,
    th,
    en,
  },
})

/**
 * Resolves a definition's localized text record for the currently selected UI language.
 * Follows a cascade: exact locale -> base language code -> English ('en') -> first available value -> empty string.
 *
 * @param text Map of language tags to translated text (e.g., { en: 'Name', 'zh-CN': '名称' })
 * @returns Localized string corresponding to user preferences
 */
export function localized(text: Record<string, string> | undefined, options?: { dates?: boolean }): string {
  if (!text) return ''
  const locale = i18n.global.locale.value
  const value = text[locale] ?? text[locale.split('-')[0]] ?? text.en ?? Object.values(text)[0] ?? ''
  return options?.dates ? calendarDateLabel(value, locale) : value
}

/**
 * Localizes known game client distribution regions while preserving custom branch labels.
 *
 * @param branch Branch identifier (e.g., 'cn', 'kr', 'tw')
 * @returns Translated region label or original identifier if unknown
 */
export function clientRegion(branch: string): string {
  const key = `service.region_${branch}`
  return i18n.global.te(key) ? i18n.global.t(key) : branch
}

// Appearance preferences: theme override and accent source, persisted like a native app.
import { reactive, watch } from 'vue'
import { call, inHost } from './bridge'

export type ThemeSetting = 'system' | 'light' | 'dark'

const media = window.matchMedia('(prefers-color-scheme: dark)')

function readTheme(): ThemeSetting {
  // A #…/settings?theme=dark style override is handy for screenshots and debugging.
  const fromUrl = new URLSearchParams(location.search).get('theme')
  if (fromUrl === 'light' || fromUrl === 'dark') return fromUrl
  const stored = localStorage.getItem('theme')
  return stored === 'light' || stored === 'dark' ? stored : 'system'
}

export const prefs = reactive({
  theme: readTheme(),
  systemDark: media.matches,
  /** When true the Windows accent blue is used; when false the game's own accent. */
  systemAccent: localStorage.getItem('systemAccent') !== 'off',
  developerMode: localStorage.getItem('developerMode') === 'on',
  nsfw: localStorage.getItem('nsfw') === 'on',
})

// Keep media suppression independent of whether the developer workspace is visible.
watch(() => prefs.nsfw, value => localStorage.setItem('nsfw', value ? 'on' : 'off'), { flush: 'sync' })

media.addEventListener('change', (e) => {
  prefs.systemDark = e.matches
})

/** True while the UI renders with dark resources. */
export function isDark(): boolean {
  return prefs.theme === 'dark' || (prefs.theme === 'system' && prefs.systemDark)
}

export function applyTheme(): void {
  document.documentElement.dataset.theme = isDark() ? 'dark' : 'light'
}

watch(
  () => [prefs.theme, prefs.systemDark],
  () => {
    applyTheme()
    if (inHost) void call('window.theme', { dark: isDark() }).catch(() => { /* The next theme change retries the frame update. */ })
    localStorage.setItem('theme', prefs.theme)
  },
  { immediate: true },
)

import { reactive } from 'vue'
import { call, demoMode, inHost, on, type LocalizedText } from '../bridge'
import { refreshInstances } from '../state'

/** An upgrade that newly supports some clients, waiting for the user to choose where to enable it. */
export interface SupportOffer {
  game: string; plugin: string; version: string; name: LocalizedText; description: LocalizedText | null
  clients: { id: string; name: string; build: string }[]
}

export const supportPrompt = reactive({ offer: null as SupportOffer | null, selected: [] as string[], busy: false, error: '' })
let queue: SupportOffer[] = []

function next() {
  const offer = queue.shift() ?? null
  Object.assign(supportPrompt, { offer, selected: offer?.clients.map(client => client.id) ?? [], busy: false, error: '' })
}

/** Loads outstanding offers and shows the first; offers survive restarts until answered. */
export async function loadSupportOffers(): Promise<void> {
  if (!(inHost || !!demoMode?.enabled)) return
  queue = await call('pluginPolicy.supportOffers') as SupportOffer[]
  if (!supportPrompt.offer) next()
}

/** Enables the checked clients and dismisses the rest; each client is asked once per offer. */
export async function answerSupportOffer(enable: boolean): Promise<void> {
  const offer = supportPrompt.offer
  if (!offer || supportPrompt.busy) return
  const chosen = enable ? supportPrompt.selected : []
  supportPrompt.busy = true; supportPrompt.error = ''
  try {
    await call('pluginPolicy.answerSupportOffer', { game: offer.game, plugin: offer.plugin, version: offer.version,
      enable: chosen, dismiss: offer.clients.map(client => client.id).filter(id => !chosen.includes(id)) })
    await refreshInstances()
    next()
  } catch (error) { supportPrompt.error = (error as Error).message; supportPrompt.busy = false }
}

/** Starts listening for upgrades that add client support. */
export function watchSupportOffers(): () => void {
  void loadSupportOffers().catch(() => {})
  return on('pluginPolicy.supportOffers', () => { void loadSupportOffers().catch(() => {}) })
}

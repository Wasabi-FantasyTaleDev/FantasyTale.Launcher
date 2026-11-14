import { reactive } from 'vue'
import { call, type AccountProfile, type ServerStatus } from '../bridge'
import { selectedInstance, state } from '../state'

// A server profile's sections are groups of its servers, such as Main, Test Server and Oversea Server. By default they
// share the default section's sign-in: one login reaches every server through the in-game list. A separate section signs
// players in through its own API, so its accounts are its own and the same username there is another account.

/** The last status read for the selected server profile. */
export const serverStatus = reactive<{ serverId: string; value: ServerStatus | null }>({ serverId: '', value: null })

/** Reads the selected profile's sections and server states, keeping the result only while that profile stays selected. */
export async function loadServerStatus(): Promise<ServerStatus> {
  const serverId = state.selectedServerId
  const build = selectedInstance.value?.build ?? state.selectedBuildId
  const value = await call('servers.status', { serverId, build: build || undefined })
  if (serverId === state.selectedServerId) { serverStatus.serverId = serverId; serverStatus.value = value }
  return value
}

/** Sections are shown only when there is a choice: a standalone server or a cluster sharing one data set has none to show. */
export const sectionsShown = (status: ServerStatus | null | undefined = serverStatus.value) => (status?.sections.length ?? 0) > 1

/** Accounts are told apart by section only when some section signs players in separately. */
export const accountSectionsShown = (status: ServerStatus | null | undefined = serverStatus.value) => !!status?.sections.some(section => section.separate)

/** The sections a player signs in to: the default one, whose sign-in the shared sections use, and every separate one. */
export const signInSections = (status: ServerStatus | null | undefined = serverStatus.value) =>
  status?.sections.filter(section => section.separate || section.id === defaultSection(status)) ?? []

/** The section whose accounts play in a section: itself when separate, otherwise the default section. */
export function accountSection(id: string | null | undefined, status: ServerStatus | null | undefined = serverStatus.value): string | null {
  const section = status?.sections.find(entry => entry.id === id)
  return section?.separate ? section.id : defaultSection(status)
}

export function defaultSection(status: ServerStatus | null | undefined): string | null {
  return status?.sections.find(section => section.default)?.id ?? status?.sections[0]?.id ?? null
}

export function sectionName(id: string | null | undefined, status: ServerStatus | null | undefined = serverStatus.value): string {
  return status?.sections.find(section => section.id === id)?.name ?? id ?? ''
}

/** A section the next sign-in dialog opens with, set when the player chooses to play in a section without an account. */
export const pendingAuthSection = reactive<{ value: string | null }>({ value: null })

/** One saved account is one username in one section. */
export const accountKey = (account: Pick<AccountProfile, 'username' | 'section'> | null | undefined) =>
  account ? `${account.section ?? ''}\u0000${account.username}` : ''

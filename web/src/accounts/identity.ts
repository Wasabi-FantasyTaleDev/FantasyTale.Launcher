import type { AccountProfile } from '../bridge'

export function characterName(account: AccountProfile): string | null {
  const name = account.displayName.trim()
  if (account.hasCharacter === false || !name) return null
  // Some account responses substitute the login identifier before a player exists.
  if (account.hasCharacter == null && name === account.username) return null
  return name
}

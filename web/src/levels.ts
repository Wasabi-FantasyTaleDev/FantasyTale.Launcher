import type { AccountProfile, GameSummary } from './bridge'

/**
 * Resolves an account's level progression, rank badge, and experience bar.
 *
 * Invariants & Edge Cases:
 * 1. Authority: Explicit server-provided level/rank badges take precedence over experience.
 * 2. 64-bit Precision: Experience values use BigInt to prevent IEEE-754 precision loss
 *    when calculating large experience ladders.
 * 3. Authority Discrepancy: If the server reports a level that does not match the experience
 *    range, the server level is honored but the progress bar is hidden (`progress: null`)
 *    rather than displaying a misleading or negative percentage.
 */
export function accountLevel(game: GameSummary | undefined, account: AccountProfile | null) {
  const catalog = game?.levels
  if (!catalog || !account) return null
  const level = account.stats.level
  const legacyBadge = Object.values(catalog.badges).find(value => value.iconId === level)
  const badge = (account.rankBadge ? catalog.badges[account.rankBadge] : undefined)
    ?? Object.values(catalog.badges).find(value => value.iconId === account.levelIconId) ?? legacyBadge
  const expValue = account.stats.exp
  const exp = expValue != null && Number.isSafeInteger(expValue) && expValue >= 0 ? BigInt(expValue) : null
  const entry = level != null && !legacyBadge ? catalog.entries.find(value => value.level === level)
    : exp != null ? [...catalog.entries].reverse().find(value => BigInt(value.minimumExperience) <= exp) : undefined
  if (!entry) return badge ? { label: level != null && !legacyBadge ? String(level) : badge.name, name: null, badgeName: badge.name, icon: badge.icon, experience: exp, minimum: null, next: null, progress: null } : null
  const index = catalog.entries.indexOf(entry), next = catalog.entries[index + 1]
  const minimum = BigInt(entry.minimumExperience), maximum = next ? BigInt(next.minimumExperience) : null
  // A mismatched server rank stays authoritative; it must not show a misleading progress bar.
  const progress = exp != null && exp >= minimum && (maximum == null || exp < maximum)
    ? maximum == null ? 100 : Number((exp - minimum) * 10000n / (maximum - minimum)) / 100 : null
  return { label: String(entry.level + catalog.displayOffset), name: entry.name, badgeName: badge?.name, icon: badge?.icon ?? entry.icons[account.rankVariant ?? 'default'] ?? entry.icons.default,
    experience: exp, minimum, next: maximum, progress }
}

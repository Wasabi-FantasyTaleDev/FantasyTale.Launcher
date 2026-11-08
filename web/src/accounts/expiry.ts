import type { AccountSessionInfo, AccountSessionList } from '../bridge'

/**
 * Calculates the absolute unix timestamp after which this session cannot be used.
 * Considers both access token expiry and refresh token expiry.
 */
function sessionExpiry(entry: AccountSessionInfo): number {
  const access = Date.parse(entry.expiresAt), refresh = Date.parse(entry.refreshExpiresAt ?? '')
  return Math.max(Number.isFinite(access) ? access : 0, Number.isFinite(refresh) ? refresh : 0)
}

/**
 * Filters out expired sessions without accidentally promoting another session.
 * If the currently active account expired, it is cleared (`current: null`) rather than
 * guessing or auto-selecting an arbitrary alternative account.
 */
export function liveAccountSessions(sessions: AccountSessionList, now = Date.now()): AccountSessionList {
  const accounts = sessions.accounts.filter(entry => sessionExpiry(entry) > now)
  const current = sessions.current && accounts.some(entry => entry.account.username === sessions.current!.username) ? sessions.current : null
  return { accounts, current }
}

/**
 * Calculates milliseconds until the next session expires, capped at 60 seconds.
 *
 * Design Rationale:
 * A 60-second upper bound ensures that operating system sleep/wake events, wall-clock
 * changes are re-evaluated promptly without waiting for
 * an arbitrarily long timeout that might have slipped into the past.
 */
export function accountExpiryDelay(accounts: readonly AccountSessionInfo[], now = Date.now()): number | null {
  if (!accounts.length) return null
  let delay = 60_000
  for (const entry of accounts) {
    const expires = sessionExpiry(entry)
    delay = Math.min(delay, Number.isFinite(expires) ? expires - now : 1)
  }
  return Math.max(1, delay)
}

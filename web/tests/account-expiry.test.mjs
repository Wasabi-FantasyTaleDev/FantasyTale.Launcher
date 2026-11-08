import assert from 'node:assert/strict'
import test from 'node:test'
import { accountExpiryDelay, liveAccountSessions } from '../src/accounts/expiry.ts'

const now = Date.parse('2026-10-10T00:00:00Z')
const entry = (username, milliseconds) => ({ account: { username, displayName: username }, expiresAt: new Date(now + milliseconds).toISOString() })

test('an expired current account is cleared without selecting another live account', () => {
  const first = entry('first', 0), second = entry('second', 60_000)
  const original = { current: first.account, accounts: [first, second] }
  assert.deepEqual(liveAccountSessions(original, now), { current: null, accounts: [second] })
  assert.equal(original.accounts.length, 2)
  assert.equal(original.current.username, 'first')
})

test('valid selection survives other expiry and invalid or missing sessions cannot remain selected', () => {
  const live = entry('live', 1000), expired = entry('expired', -1)
  const invalid = { ...entry('invalid', 1), expiresAt: 'invalid' }
  assert.deepEqual(liveAccountSessions({ current: live.account, accounts: [expired, invalid, live] }, now), { current: live.account, accounts: [live] })
  assert.equal(liveAccountSessions({ current: invalid.account, accounts: [invalid, live] }, now).current, null)
  assert.equal(liveAccountSessions({ current: expired.account, accounts: [live] }, now).current, null)
})

test('expiry timing is bounded and recalculated after clock changes or account replacement', () => {
  assert.equal(accountExpiryDelay([], now), null)
  assert.equal(accountExpiryDelay([entry('long', 90 * 86400_000)], now), 60_000)
  assert.equal(accountExpiryDelay([entry('soon', 5000), entry('later', 8000)], now), 5000)
  assert.equal(accountExpiryDelay([entry('soon', 5000)], now + 10_000), 1)
  assert.equal(accountExpiryDelay([{ ...entry('invalid', 0), expiresAt: 'invalid' }], now), 1)
  assert.equal(liveAccountSessions({ current: null, accounts: [] }, now).current, null)
})

test('renewable accounts remain visible after access expiry and across restored inactive selections', () => {
  const active = entry('active', 30_000)
  const inactive = { ...entry('inactive', -60_000), refreshExpiresAt: new Date(now + 120_000).toISOString() }
  const restored = { ...entry('restored', 0), refreshExpiresAt: new Date(now + 90_000).toISOString() }
  const sessions = { current: restored.account, accounts: [active, inactive, restored] }
  assert.deepEqual(liveAccountSessions(sessions, now), sessions)
  assert.deepEqual(liveAccountSessions(sessions, now + 40_000), { current: restored.account, accounts: [inactive, restored] })
  assert.equal(accountExpiryDelay([inactive, restored], now), 60_000)
  assert.equal(accountExpiryDelay([inactive, restored], now + 80_000), 10_000)
  assert.deepEqual(liveAccountSessions(sessions, now + 120_000), { current: null, accounts: [] })
})

test('invalid or expired refresh deadlines cannot extend a session past its access expiry', () => {
  for (const refreshExpiresAt of [null, undefined, 'invalid', new Date(now - 1000).toISOString()]) {
    const expired = { ...entry('expired', 0), refreshExpiresAt }
    const live = { ...entry('live', 5000), refreshExpiresAt }
    assert.deepEqual(liveAccountSessions({ current: expired.account, accounts: [expired, live] }, now), { current: null, accounts: [live] })
    assert.equal(accountExpiryDelay([live], now), 5000)
  }
})

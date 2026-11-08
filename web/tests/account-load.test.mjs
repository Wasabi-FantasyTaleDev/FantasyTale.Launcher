import assert from 'node:assert/strict'
import test from 'node:test'
import { loadAccountGroups } from '../src/accounts/load.ts'

test('128 server groups fit the bridge budget and retain their order', async () => {
  let active = 0, peak = 0
  const servers = Array.from({ length: 128 }, (_, index) => index)
  const result = await loadAccountGroups(servers, async server => {
    active++; peak = Math.max(peak, active)
    await new Promise(resolve => setTimeout(resolve, server % 3))
    active--
    return { server, accounts: ['first', 'second'] }
  })
  assert.equal(peak, 4)
  assert.deepEqual(result.map(group => group.server), servers)
  assert.ok(result.every(group => group.accounts.length === 2))
})

test('a rejected group stops queuing further bridge requests', async () => {
  let called = 0
  await assert.rejects(loadAccountGroups(Array.from({ length: 128 }, (_, index) => index), async server => {
    called++
    if (server === 0) throw new Error('Server was removed')
    await new Promise(resolve => setTimeout(resolve, 5))
    return server
  }), /Server was removed/)
  await new Promise(resolve => setTimeout(resolve, 10))
  assert.equal(called, 4)
  assert.deepEqual(await loadAccountGroups([], async () => { throw new Error('Unexpected request') }), [])
})

test('a superseded refresh drains its current batch without queuing more requests', async () => {
  let current = true, called = 0
  const releases = []
  const loading = loadAccountGroups(Array.from({ length: 128 }, (_, index) => index), async server => {
    called++
    await new Promise(resolve => releases.push(resolve))
    return server
  }, () => current)
  assert.equal(called, 4)
  current = false
  releases.forEach(release => release())
  assert.equal(await loading, undefined)
  assert.equal(called, 4)
})

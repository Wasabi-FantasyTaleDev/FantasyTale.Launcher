import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { loadAccountGroups } from '../src/accounts/load.ts'
import * as expiry from '../src/accounts/expiry.ts'

function pending() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

const server = { id: 'one', game: 'fixture', name: 'Fixture' }
function setup(action = async () => {}, load = async () => ({ current: null, accounts: [] })) {
  const state = vue.reactive({ selectedGameId: 'fixture', selectedServerId: 'one',
    servers: [server], games: [], account: null, authOpen: false, busy: false, error: '' })
  const calls = [], unmount = [], scope = vue.effectScope()
  const dependencies = { state,
    selectGame: id => { state.selectedGameId = id },
    selectAccount: async username => { calls.push('select'); await action('select', username) },
    refreshAccounts: async () => { calls.push('refreshAccounts') },
    perform: async task => {
      try { await task(); return true }
      catch (error) { state.error = error.message; return false }
    },
  }
  const source = compileScript(parse(readFileSync(new URL('../src/pages/AccountsPage.vue', import.meta.url), 'utf8')).descriptor, { id: 'AccountsPage' }).content
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../state') return dependencies
    if (id === '../bridge') return { call: async (method, params) => {
      calls.push(method)
      if (method === 'accounts.list') return load(params)
      return action(method, params)
    } }
    if (id === '../i18n') return { localized: value => value }
    if (id === '../accounts/load') return { loadAccountGroups }
    if (id === '../accounts/expiry') return expiry
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, setTimeout, clearTimeout,
    window: { addEventListener() {}, removeEventListener() {} } })
  const bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  return { state, calls, bindings, dispose() { unmount.splice(0).forEach(callback => callback()); scope.stop() } }
}

const sessions = username => {
  const account = { username, displayName: username }
  return { current: account, accounts: [{ account, expiresAt: new Date(Date.now() + 60_000).toISOString() }] }
}

test('one failed server cannot hide successful account groups and retry clears only its notice', async () => {
  const unavailable = new Error('Server unavailable'), loaded = sessions('alice')
  let failing = true
  const fixture = setup(undefined, async ({ serverId }) => {
    if (serverId === 'one' && failing) throw unavailable
    return loaded
  })
  try {
    fixture.state.servers = [server, { ...server, id: 'two', name: 'Two' }]
    await vue.nextTick(); await fixture.bindings.refresh()
    const groups = fixture.bindings.groups.value
    assert.deepEqual(Array.from(groups, group => group.server.id), ['one', 'two'])
    assert.equal(groups[0].error, unavailable)
    assert.equal(groups[0].sessions.accounts.length, 0)
    assert.equal(groups[1].sessions.current.username, 'alice')
    assert.equal(groups[1].error, undefined)
    assert.equal(fixture.state.error, '')
    failing = false; await fixture.bindings.refresh()
    assert.ok(fixture.bindings.groups.value.every(group => !group.error && group.sessions.current.username === 'alice'))
  } finally { fixture.dispose() }
})

test('failed account refresh retains live fallback only for the exact saved endpoint', async () => {
  let failing = false
  const loaded = sessions('alice'), expired = { account: { username: 'expired' }, expiresAt: new Date(Date.now() - 1000).toISOString() }
  loaded.accounts.push(expired)
  const fixture = setup(undefined, async () => {
    if (failing) throw new Error('Offline')
    return loaded
  })
  try {
    fixture.state.servers = [{ ...server, apiUrl: 'https://one.example/' }]
    await vue.nextTick(); await fixture.bindings.refresh()
    failing = true; await fixture.bindings.refresh()
    assert.equal(fixture.bindings.groups.value[0].sessions.current.username, 'alice')
    assert.equal(fixture.bindings.groups.value[0].sessions.accounts.length, 1)
    fixture.state.servers = [{ ...server, apiUrl: 'https://replacement.example/' }]
    await vue.nextTick(); await fixture.bindings.refresh()
    assert.equal(fixture.bindings.groups.value[0].sessions.current, null)
    assert.equal(fixture.bindings.groups.value[0].sessions.accounts.length, 0)
    assert.equal(fixture.bindings.groups.value[0].error.message, 'Offline')
  } finally { fixture.dispose() }
})

test('a delayed failed account group cannot restore a removed server', async () => {
  const reply = pending(), fixture = setup(undefined, () => reply.promise)
  try {
    const loading = fixture.bindings.refresh()
    fixture.state.servers = []
    await vue.nextTick(); await fixture.bindings.refresh()
    reply.reject(new Error('Old server failed')); await loading
    assert.equal(fixture.bindings.groups.value.length, 0)
    assert.equal(fixture.state.error, '')
  } finally { fixture.dispose() }
})

test('adding an account cannot open a dialog after switching servers away and back', async () => {
  const fixture = setup()
  try {
    const choosing = fixture.bindings.choose(server)
    fixture.state.selectedServerId = 'two'
    fixture.state.selectedServerId = 'one'
    await choosing
    assert.equal(fixture.state.authOpen, false)
  } finally { fixture.dispose() }
})

test('a closed account page cannot open a delayed login dialog', async () => {
  const fixture = setup()
  const choosing = fixture.bindings.choose(server)
  fixture.dispose()
  await choosing
  assert.equal(fixture.state.authOpen, false)
})

test('finishing selection after leaving the page cannot queue another account load', async () => {
  const reply = pending(), fixture = setup(() => reply.promise)
  const choosing = fixture.bindings.choose(server, 'alice')
  await vue.nextTick()
  fixture.dispose()
  const count = fixture.calls.length
  reply.resolve()
  await choosing
  assert.equal(fixture.calls.length, count)
})

test('finishing a profile refresh after leaving the page cannot refresh another view', async () => {
  const reply = pending(), fixture = setup(() => reply.promise)
  const refreshing = fixture.bindings.refreshCurrent(server)
  await vue.nextTick()
  fixture.dispose()
  const count = fixture.calls.length
  reply.resolve()
  await refreshing
  assert.equal(fixture.calls.length, count)
})

test('a late logout does not close a newer confirmation', async () => {
  const reply = pending(), fixture = setup(() => reply.promise)
  try {
    fixture.bindings.remove.value = { server, username: 'alice' }
    const signingOut = fixture.bindings.signOut()
    fixture.bindings.remove.value = { server, username: 'bob' }
    reply.resolve()
    await signingOut
    assert.equal(fixture.bindings.remove.value?.username, 'bob')
  } finally { fixture.dispose() }
})

test('normal account addition still opens authentication on its selected server', async () => {
  const fixture = setup()
  try {
    await fixture.bindings.choose(server)
    assert.equal(fixture.state.selectedServerId, server.id)
    assert.equal(fixture.state.authOpen, true)
  } finally { fixture.dispose() }
})

test('normal account selection targets the requested game and username', async () => {
  const selected = [], fixture = setup(async (method, value) => { if (method === 'select') selected.push(value) })
  try {
    const target = { id: 'other', game: 'another-game', name: 'Other' }
    await fixture.bindings.choose(target, 'alice')
    assert.equal(fixture.state.selectedGameId, target.game)
    assert.equal(fixture.state.selectedServerId, target.id)
    assert.deepEqual(selected, ['alice'])
    assert.equal(fixture.state.authOpen, false)
  } finally { fixture.dispose() }
})

test('normal logout removes only the requested server account and closes its confirmation', async () => {
  const requests = [], fixture = setup(async (method, params) => { requests.push({ method, ...params }) })
  try {
    fixture.bindings.remove.value = { server, username: 'alice' }
    await fixture.bindings.signOut()
    assert.deepEqual(requests, [{ method: 'accounts.logout', serverId: server.id, username: 'alice' }])
    assert.equal(fixture.bindings.remove.value, null)
  } finally { fixture.dispose() }
})

for (const operation of ['refresh', 'logout']) {
  for (const change of ['server', 'unmount']) {
    test(`a delayed ${operation} failure stays out of the view after ${change}`, async () => {
      const reply = pending(), fixture = setup(() => reply.promise)
      fixture.bindings.remove.value = { server, username: 'alice' }
      const running = operation === 'refresh' ? fixture.bindings.refreshCurrent(server) : fixture.bindings.signOut()
      await vue.nextTick()
      if (change === 'unmount') fixture.dispose()
      else {
        fixture.state.selectedServerId = 'two'
        fixture.state.selectedServerId = 'one'
      }
      reply.reject(new Error('Old server unavailable'))
      await running
      assert.equal(fixture.state.error, '')
      if (change !== 'unmount') fixture.dispose()
    })
  }
  test(`a current ${operation} failure is still reported`, async () => {
    const fixture = setup(async () => { throw new Error('Current server unavailable') })
    try {
      fixture.bindings.remove.value = { server, username: 'alice' }
      if (operation === 'refresh') await fixture.bindings.refreshCurrent(server)
      else await fixture.bindings.signOut()
      assert.equal(fixture.state.error, 'Current server unavailable')
    } finally { fixture.dispose() }
  })
}

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const identitySource = readFileSync(new URL('../src/accounts/identity.ts', import.meta.url), 'utf8')
const identity = { exports: {} }
runInNewContext(ts.transpileModule(identitySource, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText, { module: identity, exports: identity.exports })

function pending() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

function setup(name, dependencies) {
  const source = readFileSync(new URL(`../src/components/${name}.vue`, import.meta.url), 'utf8')
  const script = compileScript(parse(source).descriptor, { id: name }).content
  const javascript = ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }, scope = vue.effectScope(), unmount = []
  const require = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../accounts/identity') return identity.exports
    if (id === '../state') return dependencies
    if (id === '../bridge') return { inHost: true, call: dependencies.call }
    if (id === '../i18n') return { localized: value => value }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require })
  const bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  return { bindings, dispose() { unmount.forEach(callback => callback()); scope.stop() } }
}

test('account menu does not reopen after switching servers, including switching back', async () => {
  const state = vue.reactive({ selectedServerId: 'one', error: '' }), refresh = pending()
  const component = setup('AccountPicker', { state, refreshAccounts: () => refresh.promise })
  try {
    const opening = component.bindings.toggle()
    state.selectedServerId = 'two'
    state.selectedServerId = 'one'
    refresh.resolve()
    await opening
    assert.equal(component.bindings.open.value, false)
  } finally { component.dispose() }
})

test('old menu errors cannot affect another server', async () => {
  const state = vue.reactive({ selectedServerId: 'one', error: '' }), refresh = pending()
  const component = setup('AccountPicker', { state, refreshAccounts: () => refresh.promise })
  try {
    const opening = component.bindings.toggle()
    state.selectedServerId = 'two'
    refresh.reject(new Error('Old server unavailable'))
    await opening
    assert.equal(state.error, '')
    assert.equal(component.bindings.open.value, false)
  } finally { component.dispose() }
})

function removalFixture() {
  const account = { username: 'private-login', displayName: 'Player', stats: {} }
  const state = vue.reactive({ selectedServerId: 'one', busy: false, accounts: [{ account }], account, error: '' })
  const calls = [], reply = pending()
  let refreshes = 0
  const component = setup('AccountPicker', { state, call(method, args) { calls.push({ method, args }); return reply.promise },
    async refreshAccounts() { refreshes++; state.accounts = []; state.account = null } })
  return { ...component, state, account, calls, reply, get refreshes() { return refreshes } }
}

test('removing an account requires confirmation and cancel does not send a request', async () => {
  const fixture = removalFixture()
  try {
    fixture.bindings.open.value = true
    fixture.bindings.requestRemoval(fixture.account)
    assert.equal(fixture.bindings.open.value, false)
    assert.equal(fixture.calls.length, 0)
    fixture.bindings.clearRemoval()
    await fixture.bindings.confirmRemoval()
    assert.equal(fixture.calls.length, 0)
    assert.equal(fixture.state.accounts.length, 1)
  } finally { fixture.dispose() }
})

test('confirming account removal targets only that record and refreshes the selected account', async () => {
  const fixture = removalFixture()
  try {
    fixture.bindings.requestRemoval(fixture.account)
    const deleting = fixture.bindings.confirmRemoval()
    await fixture.bindings.confirmRemoval()
    assert.equal(fixture.calls.length, 1)
    assert.equal(fixture.calls[0].method, 'accounts.logout')
    assert.equal(fixture.calls[0].args.serverId, 'one')
    assert.equal(fixture.calls[0].args.username, fixture.account.username)
    fixture.reply.resolve()
    await deleting
    assert.equal(fixture.refreshes, 1)
    assert.equal(fixture.state.account, null)
    assert.equal(fixture.bindings.removal.value, null)
    assert.equal(fixture.bindings.removing.value, false)
  } finally { fixture.dispose() }
})

test('server changes cancel a pending account removal confirmation', async () => {
  const fixture = removalFixture()
  try {
    fixture.bindings.requestRemoval(fixture.account)
    fixture.state.selectedServerId = 'two'
    await fixture.bindings.confirmRemoval()
    assert.equal(fixture.calls.length, 0)
    assert.equal(fixture.bindings.removal.value, null)
  } finally { fixture.dispose() }
})

test('a remote logout failure still refreshes a locally removed record', async () => {
  const fixture = removalFixture()
  try {
    fixture.bindings.requestRemoval(fixture.account)
    const deleting = fixture.bindings.confirmRemoval()
    fixture.reply.reject(new Error('Server offline'))
    await deleting
    assert.equal(fixture.refreshes, 1)
    assert.equal(fixture.state.accounts.length, 0)
    assert.equal(fixture.bindings.removal.value, null)
  } finally { fixture.dispose() }
})

function authFixture() {
  const state = vue.reactive({ selectedServerId: 'one', authOpen: false, serverLoading: false, catalog: { registrationEnabled: true, registrationFields: [] } })
  const reply = pending(), refresh = pending()
  const component = setup('AuthDialog', { state, selectedServer: vue.computed(() => ({ apiUrl: 'https://example.org/' })), call: method => method === 'accounts.rememberedLogin' ? Promise.resolve(null) : reply.promise, refreshAccounts: () => refresh.promise })
  state.authOpen = true
  component.bindings.username.value = 'alice'
  component.bindings.password.value = 'secret'
  return { ...component, state, reply, refresh }
}

test('late login completion cannot close a newly opened account dialog', async () => {
  const fixture = authFixture()
  try {
    const submitted = fixture.bindings.submit()
    fixture.reply.resolve()
    await Promise.resolve()
    fixture.state.authOpen = false
    fixture.state.authOpen = true
    fixture.bindings.username.value = 'bob'
    fixture.refresh.resolve()
    await submitted
    assert.equal(fixture.state.authOpen, true)
    assert.equal(fixture.bindings.username.value, 'bob')
  } finally { fixture.dispose() }
})

test('switching servers clears credentials and suppresses stale sign-in errors', async () => {
  const fixture = authFixture()
  try {
    const submitted = fixture.bindings.submit()
    fixture.state.selectedServerId = 'two'
    assert.equal(fixture.state.authOpen, false)
    assert.equal(fixture.bindings.password.value, '')
    fixture.state.authOpen = true
    fixture.reply.reject(new Error('Old account rejected'))
    await submitted
    assert.equal(fixture.bindings.error.value, '')
    assert.equal(fixture.state.authOpen, true)
    assert.equal(fixture.bindings.busy.value, false)
  } finally { fixture.dispose() }
})

function rememberedFixture() {
  const state = vue.reactive({ selectedServerId: 'one', authOpen: false, serverLoading: false, catalog: { registrationEnabled: true, registrationFields: [] } })
  const saved = pending(), calls = [], lookups = []
  const component = setup('AuthDialog', { state, selectedServer: vue.computed(() => ({ apiUrl: 'https://example.org/' })),
    call(method, args) {
      calls.push({ method, args })
      if (method === 'accounts.rememberedLogin') { const reply = lookups.length === 0 ? saved : pending(); lookups.push(reply); return reply.promise }
      return Promise.resolve()
    }, refreshAccounts: () => Promise.resolve() })
  state.authOpen = true
  return { ...component, state, saved, calls, lookups }
}

test('remember password starts unchecked and is sent only as an explicit login choice', async () => {
  const fixture = rememberedFixture()
  try {
    assert.equal(fixture.bindings.rememberPassword.value, false)
    fixture.saved.resolve(null); await Promise.resolve()
    fixture.bindings.username.value = 'alice'; fixture.bindings.password.value = 'fixture-password'
    await fixture.bindings.submit()
    assert.equal(fixture.calls.find(call => call.method === 'accounts.login').args.rememberPassword, false)
    fixture.state.authOpen = true
    fixture.bindings.username.value = 'alice'; fixture.bindings.password.value = 'fixture-password'; fixture.bindings.rememberPassword.value = true
    await fixture.bindings.submit()
    assert.equal(fixture.calls.filter(call => call.method === 'accounts.login').at(-1).args.rememberPassword, true)
  } finally { fixture.dispose() }
})

test('remembered credentials fill a pristine login form and retain the checkbox without signing in', async () => {
  const fixture = rememberedFixture()
  try {
    fixture.saved.resolve({ username: 'alice', password: 'fixture-password' }); await new Promise(setImmediate)
    assert.equal(fixture.bindings.username.value, 'alice')
    assert.equal(fixture.bindings.password.value, 'fixture-password')
    assert.equal(fixture.bindings.rememberPassword.value, true)
    assert.equal(fixture.calls.some(call => call.method === 'accounts.login'), false)
    fixture.bindings.username.value = 'bob'
    assert.equal(fixture.bindings.password.value, '')
    assert.equal(fixture.bindings.rememberPassword.value, false)
    fixture.state.authOpen = false
    assert.equal(fixture.bindings.password.value, '')
    assert.equal(fixture.bindings.rememberPassword.value, false)
  } finally { fixture.dispose() }
})

for (const change of ['server', 'typing', 'mode', 'unmount']) {
  test(`late remembered credentials cannot populate a form after ${change}`, async () => {
    const fixture = rememberedFixture()
    try {
      if (change === 'server') { fixture.state.selectedServerId = 'two'; fixture.state.authOpen = true }
      if (change === 'typing') { fixture.bindings.username.value = 'bob'; fixture.bindings.password.value = 'typed-password' }
      if (change === 'mode') fixture.bindings.mode.value = 'register'
      if (change === 'unmount') fixture.dispose()
      fixture.saved.resolve({ username: 'alice', password: 'old-password' }); await new Promise(setImmediate)
      assert.equal(fixture.bindings.username.value, change === 'typing' ? 'bob' : '')
      assert.equal(fixture.bindings.password.value, change === 'typing' ? 'typed-password' : '')
      if (change === 'server') {
        assert.equal(fixture.lookups.length, 2)
        fixture.lookups[1].resolve({ username: 'second-user', password: 'second-password' }); await new Promise(setImmediate)
        assert.equal(fixture.bindings.username.value, 'second-user')
        assert.equal(fixture.bindings.password.value, 'second-password')
      }
    } finally { if (change !== 'unmount') fixture.dispose() }
  })
}


test('uncreated character labels never expose a login identifier', () => {
  const account = { username: 'private-login', displayName: 'private-login', stats: { level: null }, levelIconUrl: null }
  assert.equal(identity.exports.characterName(account), null)
  assert.equal(identity.exports.characterName({ ...account, hasCharacter: false, stats: { level: 1 } }), null)
  assert.equal(identity.exports.characterName({ ...account, displayName: '' }), null)
  assert.equal(identity.exports.characterName({ ...account, displayName: 'Game nickname', stats: { level: 12 } }), 'Game nickname')
  assert.equal(identity.exports.characterName({ ...account, hasCharacter: true }), 'private-login')
  assert.equal(identity.exports.characterName({ ...account, stats: { level: 12 } }), null)
})

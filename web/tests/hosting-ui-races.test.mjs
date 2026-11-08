import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

function pending() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

const profiles = [
  { id: 'one', game: 'fixture', build: 'build-one', name: 'First' },
  { id: 'two', game: 'fixture', build: 'build-two', name: 'Second' },
]
const sessions = profiles.map(profile => ({ profileId: profile.id, runId: `run-${profile.id}`, state: 'ready' }))
function setup(refreshServers = async () => {}, list = async () => ({ profiles, sessions }), destination) {
  const state = vue.reactive({ selectedGameId: 'fixture', selectedServerId: '', selectedBuildId: '', selectedInstanceId: '',
    servers: [{ id: 'local-one' }, { id: 'local-two' }],
    instances: [{ id: 'client-one', game: 'fixture', build: 'build-one' }, { id: 'client-two', game: 'fixture', build: 'build-two' }] })
  const selectedGame = vue.ref({ builds: [] })
  const calls = [], routes = [], unmount = [], scope = vue.effectScope()
  const source = compileScript(parse(readFileSync(new URL('../src/pages/HostingPage.vue', import.meta.url), 'utf8')).descriptor, { id: 'HostingPage' }).content
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === 'vue-router') return { useRouter: () => ({ push: async path => { routes.push(path) } }) }
    if (id === '../state') return { state, refreshServers, selectedGame }
    if (id === '../bridge') return { inHost: true, call: async (method, params) => {
      calls.push({ method, params })
      if (method === 'hosting.list') return list()
      if (method === 'hosting.destination') return destination ? destination(params) : { id: `local-${params.id}`, game: 'fixture' }
      return []
    } }
    if (id === '../i18n') return { localized: value => value }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, setTimeout, clearTimeout })
  const bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  bindings.overview.value = { profiles: structuredClone(profiles), sessions: structuredClone(sessions) }
  bindings.selectedId.value = 'one'
  return { state, calls, routes, bindings, selectedGame, dispose() { unmount.splice(0).forEach(callback => callback()); scope.stop() } }
}

test('changing hosting selection while connecting cannot mix profiles or navigate', async () => {
  const wait = pending(), fixture = setup(() => wait.promise)
  try {
    const connecting = fixture.bindings.connect()
    fixture.bindings.selectedId.value = 'two'
    wait.resolve()
    await connecting
    assert.equal(fixture.state.selectedServerId, '')
    assert.equal(fixture.state.selectedBuildId, '')
    assert.deepEqual(fixture.routes, [])
  } finally { fixture.dispose() }
})

test('an old ready invocation cannot connect after a replacement becomes ready', async () => {
  const wait = pending(), fixture = setup(() => wait.promise)
  try {
    const connecting = fixture.bindings.connect()
    fixture.bindings.overview.value.sessions[0].runId = 'replacement'
    wait.resolve()
    await connecting
    assert.equal(fixture.state.selectedServerId, '')
    assert.deepEqual(fixture.routes, [])
  } finally { fixture.dispose() }
})

test('leaving hosting during connection prevents global selection and navigation', async () => {
  const wait = pending(), fixture = setup(() => wait.promise)
  const connecting = fixture.bindings.connect()
  fixture.dispose()
  wait.resolve()
  await connecting
  assert.equal(fixture.state.selectedServerId, '')
  assert.deepEqual(fixture.routes, [])
})

test('older status responses cannot overwrite newer readiness', async () => {
  const old = pending(), fresh = pending()
  let count = 0
  const fixture = setup(undefined, () => ++count === 1 ? old.promise : fresh.promise)
  try {
    const first = fixture.bindings.refresh(), second = fixture.bindings.refresh()
    fresh.resolve({ profiles, sessions: [{ ...sessions[0], state: 'failed' }] })
    await second
    old.resolve({ profiles, sessions })
    await first
    assert.equal(fixture.bindings.overview.value.sessions[0].state, 'failed')
  } finally { fixture.dispose() }
})

test('normal hosting connection verifies its exact invocation and selects the matching client', async () => {
  const fixture = setup()
  try {
    await fixture.bindings.connect()
    assert.equal(fixture.state.selectedServerId, 'local-one')
    assert.equal(fixture.state.selectedBuildId, 'build-one')
    assert.equal(fixture.state.selectedInstanceId, 'client-one')
    assert.deepEqual(fixture.routes, ['/home'])
    const request = fixture.calls.find(call => call.method === 'hosting.destination')
    assert.equal(request?.params.id, 'one')
    assert.equal(request?.params.runId, 'run-one')
  } finally { fixture.dispose() }
})

test('a delayed destination reply cannot connect after changing the selected profile', async () => {
  const wait = pending(), entered = pending(), fixture = setup(undefined, undefined, () => { entered.resolve(); return wait.promise })
  try {
    const connecting = fixture.bindings.connect()
    await entered.promise
    fixture.bindings.selectedId.value = 'two'
    wait.resolve({ id: 'local-one', game: 'fixture' })
    await connecting
    assert.equal(fixture.state.selectedServerId, '')
    assert.deepEqual(fixture.routes, [])
  } finally { fixture.dispose() }
})

test('a rejected invocation cannot change the selected destination', async () => {
  const fixture = setup(undefined, undefined, async () => { throw new Error('Invocation replaced') })
  try {
    await fixture.bindings.connect()
    assert.equal(fixture.state.selectedServerId, '')
    assert.equal(fixture.bindings.error.value, 'Invocation replaced')
    assert.deepEqual(fixture.routes, [])
  } finally { fixture.dispose() }
})

test('connecting without a compatible client clears a previous build selection', async () => {
  const fixture = setup()
  try {
    fixture.state.instances = [fixture.state.instances[1]]
    fixture.state.selectedInstanceId = 'client-two'
    await fixture.bindings.connect()
    assert.equal(fixture.state.selectedInstanceId, '')
    assert.equal(fixture.state.selectedBuildId, 'build-one')
  } finally { fixture.dispose() }
})

test('a stale status failure cannot replace a newer successful refresh', async () => {
  const old = pending()
  let count = 0
  const fixture = setup(undefined, () => ++count === 1 ? old.promise : Promise.resolve({ profiles, sessions }))
  try {
    const first = fixture.bindings.refresh()
    await fixture.bindings.refresh()
    old.reject(new Error('Old status unavailable'))
    await first
    assert.equal(fixture.bindings.overview.value.sessions[0].state, 'ready')
  } finally { fixture.dispose() }
})

test('a current status failure still reaches the page error handler', async () => {
  const fixture = setup(undefined, async () => { throw new Error('Current status unavailable') })
  try {
    await assert.rejects(fixture.bindings.refresh(), /Current status unavailable/)
  } finally { fixture.dispose() }
})


test('a disabled hosting deep link cannot poll, configure, start or connect', async () => {
  const fixture = setup()
  try {
    fixture.selectedGame.value.localHostingEnabled = false
    await fixture.bindings.refresh()
    fixture.bindings.edit()
    await fixture.bindings.start()
    await fixture.bindings.connect()
    assert.deepEqual(fixture.calls, [])
    assert.deepEqual(fixture.routes, [])
    assert.equal(fixture.bindings.dialog.value, false)
  } finally { fixture.dispose() }
})

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { policyPlugins, policySources } from '../src/clientPolicy.ts'
import { mergeServerPresentation } from '../src/serverDescription.ts'

function pending() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const entry = { id: 'room', kind: 'room', details: { game: 'fixture', name: 'Room', builds: ['build-one'] } }
async function setup(join = async () => ({ id: 'joined', game: 'fixture' }), refreshServers = async () => {}, list = async () => ({ entries: [entry] }), catalog = async () => ({ instances: [] }), savedTab, profile = async () => ({ text: 'profile', inspection: { exists: false, identical: false } })) {
  const state = vue.reactive({ selectedGameId: 'fixture', selectedServerId: '', selectedBuildId: '', selectedInstanceId: '', busy: false,
    servers: [{ id: 'joined', game: 'fixture', transient: true }], instances: [{ id: 'client', game: 'fixture', build: 'build-one' }] })
  const selectedGame = vue.ref({ builds: [{ id: 'build-one', name: 'Build' }] })
  const calls = [], routes = [], unmount = [], scope = vue.effectScope(), route = vue.reactive({ query: {} })
  const source = compileScript(parse(readFileSync(new URL('../src/pages/MultiplayerPage.vue', import.meta.url), 'utf8')).descriptor, { id: 'MultiplayerPage' }).content
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === 'vue-router') return { useRoute: () => route, useRouter: () => ({ push: async path => routes.push(path), replace: async target => { route.query = target.query } }) }
    if (id === '../state') return { state, refreshServers, gameServers: vue.computed(() => state.servers.filter(server => server.game === state.selectedGameId)), selectedGame }
    if (id === '../clientPolicy') return { policyPlugins, policySources }
    if (id === '../bridge') return { inHost: true, on: () => () => {}, call: async (method, params) => {
      calls.push({ method, params })
      if (method === 'directory.join' || method === 'servers.importProfile') return join(params)
      if (method === 'directory.profile') return profile(params)
      if (method === 'directory.list') return list(params)
      if (method === 'servers.catalog') return catalog(params)
      return []
    } }
    if (id === '../multiplayerPreview') return { multiplayerPreview: false }
    if (id === '../i18n') return { localized: value => value }
    if (id === '../serverDescription') return { mergeServerPresentation }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  const preferences = new Map(savedTab ? [['multiplayerTab', savedTab]] : [])
  runInNewContext(javascript, { module, exports: module.exports, require, setTimeout, clearTimeout,
    localStorage: { getItem: key => preferences.get(key) ?? null, setItem: (key, value) => preferences.set(key, value) } })
  const bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  bindings.sources.value = [{ id: 'one', enabled: true, baseUrl: 'http://one.example/' }, { id: 'two', enabled: true, baseUrl: 'http://two.example/' }]
  bindings.sourceId.value = 'one'
  await vue.nextTick(); await vue.nextTick()
  bindings.entries.value = [entry]; bindings.buildId.value = 'build-one'
  return { state, calls, routes, bindings, selectedGame, route, preferences, dispose() { unmount.splice(0).forEach(callback => callback()); scope.stop() } }
}

test('a late room join cannot select a destination after switching games', async () => {
  const reply = pending(), fixture = await setup(() => reply.promise)
  try {
    const joining = fixture.bindings.join()
    fixture.state.selectedGameId = 'another-game'
    reply.resolve({ id: 'joined', game: 'fixture' }); await joining
    assert.equal(fixture.state.selectedServerId, '')
    assert.deepEqual(fixture.routes, [])
  } finally { fixture.dispose() }
})

test('saved cards expose version loading and keep previous labels during a refresh', async () => {
  const first = pending(), second = pending()
  let request = 0
  const fixture = await setup(undefined, undefined, undefined, () => ++request === 1 ? first.promise : second.promise)
  try {
    fixture.route.query.tab = 'server'
    fixture.state.servers = [{ id: 'saved', game: 'fixture', name: 'Saved', apiUrl: 'https://saved.example/' }]
    await vue.nextTick()
    assert.equal(fixture.bindings.cards.value[0].buildsLoading, true)
    assert.deepEqual(Array.from(fixture.bindings.cards.value[0].builds), [])
    first.resolve({ instances: [{ build: 'build-one' }] }); await new Promise(resolve => setImmediate(resolve))
    assert.equal(fixture.bindings.cards.value[0].buildsLoading, false)
    assert.deepEqual(Array.from(fixture.bindings.cards.value[0].builds), ['build-one'])
    const refreshing = fixture.bindings.loadSavedCatalogs()
    assert.equal(fixture.bindings.cards.value[0].buildsLoading, true)
    assert.deepEqual(Array.from(fixture.bindings.cards.value[0].builds), ['build-one'])
    second.resolve({ instances: [{ build: 'build-two' }] }); await refreshing
    assert.equal(fixture.bindings.cards.value[0].buildsLoading, false)
    assert.deepEqual(Array.from(fixture.bindings.cards.value[0].builds), ['build-two'])
  } finally { fixture.dispose() }
})

test('a stale catalog completion cannot clear the current card loading state', async () => {
  const first = pending(), second = pending()
  let request = 0
  const fixture = await setup(undefined, undefined, undefined, () => ++request === 1 ? first.promise : second.promise)
  try {
    fixture.route.query.tab = 'server'
    fixture.state.servers = [{ id: 'saved', game: 'fixture', name: 'Saved', apiUrl: 'https://saved.example/' }]
    await vue.nextTick()
    const current = fixture.bindings.loadSavedCatalogs()
    first.resolve({ instances: [{ build: 'old' }] }); await new Promise(resolve => setImmediate(resolve))
    assert.equal(fixture.bindings.cards.value[0].buildsLoading, true)
    assert.deepEqual(Array.from(fixture.bindings.cards.value[0].builds), [])
    second.reject(new Error('Unavailable')); await current
    assert.equal(fixture.bindings.cards.value[0].buildsLoading, false)
    assert.deepEqual(Array.from(fixture.bindings.cards.value[0].builds), [])
  } finally { fixture.dispose() }
})

test('directory timeouts show a localized retry message and a successful refresh clears it', async () => {
  let timeout = true
  const fixture = await setup(undefined, undefined, async () => {
    if (timeout) throw new Error('directory_timeout')
    return { entries: [entry] }
  })
  try {
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.error.value, 'multiplayer.timeout')
    assert.equal(fixture.bindings.loading.value, false)
    assert.equal(fixture.bindings.entries.value.length, 0)
    timeout = false
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.error.value, '')
    assert.equal(fixture.bindings.entries.value[0].id, 'room')
  } finally { fixture.dispose() }
})

test('switching sources away and back invalidates the old join', async () => {
  const reply = pending(), fixture = await setup(() => reply.promise)
  try {
    const joining = fixture.bindings.join()
    fixture.bindings.sourceId.value = 'two'; fixture.bindings.sourceId.value = 'one'
    reply.resolve({ id: 'joined', game: 'fixture' }); await joining
    assert.equal(fixture.state.selectedServerId, '')
    assert.deepEqual(fixture.routes, [])
  } finally { fixture.dispose() }
})

test('leaving the page during server refresh prevents the late join navigation', async () => {
  const reply = pending(), entered = pending(), fixture = await setup(undefined, () => { entered.resolve(); return reply.promise })
  const joining = fixture.bindings.join()
  await entered.promise
  fixture.dispose(); reply.resolve(); await joining
  assert.equal(fixture.state.selectedServerId, '')
  assert.deepEqual(fixture.routes, [])
})

test('old source errors cannot appear in a newer source context', async () => {
  const reply = pending(), fixture = await setup(() => reply.promise)
  try {
    const joining = fixture.bindings.join()
    fixture.bindings.sourceId.value = 'two'
    reply.reject(new Error('Old room unavailable')); await joining
    assert.equal(fixture.bindings.error.value, '')
  } finally { fixture.dispose() }
})

test('joining without a compatible client clears the old selected client', async () => {
  const fixture = await setup()
  try {
    fixture.state.instances = [{ id: 'old-client', game: 'fixture', build: 'another-build' }]
    fixture.state.selectedInstanceId = 'old-client'
    await fixture.bindings.join()
    assert.equal(fixture.state.selectedInstanceId, '')
    assert.equal(fixture.state.selectedBuildId, 'build-one')
  } finally { fixture.dispose() }
})

test('normal room join keeps its source, build and matching client', async () => {
  const fixture = await setup()
  try {
    await fixture.bindings.join()
    assert.equal(fixture.state.selectedServerId, 'joined')
    assert.equal(fixture.state.selectedInstanceId, 'client')
    assert.deepEqual(fixture.routes, ['/home'])
    const request = fixture.calls.find(call => call.method === 'directory.join').params
    assert.equal(request.sourceId, 'one'); assert.equal(request.entryId, 'room'); assert.equal(request.build, 'build-one')
  } finally { fixture.dispose() }
})

test('a room that disappears during server refresh is not selected', async () => {
  let fixture
  fixture = await setup(undefined, async () => { fixture.state.servers = [] })
  try {
    await fixture.bindings.join()
    assert.equal(fixture.state.selectedServerId, '')
    assert.equal(fixture.bindings.error.value, 'multiplayer.connectionChanged')
    assert.deepEqual(fixture.routes, [])
  } finally { fixture.dispose() }
})

test('a mismatched game reply is rejected before global selection', async () => {
  const fixture = await setup(async () => ({ id: 'joined', game: 'another-game' }))
  try {
    await fixture.bindings.join()
    assert.equal(fixture.state.selectedServerId, '')
    assert.equal(fixture.bindings.error.value, 'multiplayer.connectionChanged')
  } finally { fixture.dispose() }
})

test('a current room failure remains visible', async () => {
  const fixture = await setup(async () => { throw new Error('Current room unavailable') })
  try {
    await fixture.bindings.join()
    assert.equal(fixture.bindings.error.value, 'Current room unavailable')
  } finally { fixture.dispose() }
})


test('unreleased rooms remain browsable while room joins stay unavailable and server joins still work', async () => {
  const fixture = await setup()
  try {
    fixture.selectedGame.value.roomsEnabled = false
    fixture.bindings.kind.value = 'room'
    assert.equal(fixture.bindings.kind.value, 'room')
    assert.equal(fixture.bindings.directoryTabs.value[0].disabled, undefined)
    fixture.calls.length = 0
    await fixture.bindings.join()
    assert.equal(fixture.calls.some(call => call.method === 'directory.join'), false)
    fixture.bindings.kind.value = 'server'
    fixture.bindings.entries.value = [{ ...entry, id: 'server', kind: 'server' }]
    await vue.nextTick()
    fixture.bindings.buildId.value = 'build-one'
    await fixture.bindings.join()
    assert.equal(fixture.calls.some(call => call.method === 'directory.profile'), true)
    assert.equal(fixture.calls.some(call => call.method === 'servers.importProfile'), true)
    assert.deepEqual(fixture.routes, ['/home'])
  } finally { fixture.dispose() }
})

test('the multiplayer page restores its own last tab and explicit links override it', async () => {
  const first = await setup(undefined, undefined, undefined, undefined, 'server')
  try {
    assert.equal(first.bindings.kind.value, 'server')
    first.bindings.selectKind('room')
    await vue.nextTick()
    assert.equal(first.preferences.get('multiplayerTab'), 'room')
    const reopened = await setup(undefined, undefined, undefined, undefined, first.preferences.get('multiplayerTab'))
    try {
      assert.equal(reopened.bindings.kind.value, 'room')
      reopened.route.query.tab = 'server'
      await vue.nextTick()
      assert.equal(reopened.bindings.kind.value, 'server')
      assert.equal(reopened.preferences.get('multiplayerTab'), 'server')
      assert.deepEqual([...reopened.preferences.keys()], ['multiplayerTab'])
    } finally { reopened.dispose() }
  } finally { first.dispose() }
})

test('cards keep the active server first and stably reorder favorites across saved and discovered rows', async () => {
  const fixture = await setup()
  try {
    fixture.route.query.tab = 'server'
    fixture.state.servers = [
      { id: 'ordinary', game: 'fixture', name: 'Ordinary', apiUrl: 'https://ordinary.example/' },
      { id: 'active', game: 'fixture', name: 'Active', apiUrl: 'https://active.example/' },
      { id: 'saved-favorite', game: 'fixture', name: 'Saved favorite', apiUrl: 'https://saved.example/' },
    ]
    fixture.state.selectedServerId = 'active'
    fixture.bindings.entries.value = [{ id: 'discovered', kind: 'server', details: { game: 'fixture', name: 'Discovered', apiUrl: 'https://discovered.example/', builds: [] } }]
    fixture.bindings.favorites.value = ['fixture:https://saved.example', 'fixture:https://discovered.example']
    assert.deepEqual(Array.from(fixture.bindings.cards.value, card => card.key), ['saved:active', 'saved:saved-favorite', 'directory:discovered', 'saved:ordinary'])
    fixture.bindings.favorites.value = []
    assert.deepEqual(Array.from(fixture.bindings.cards.value, card => card.key), ['saved:active', 'saved:ordinary', 'saved:saved-favorite', 'directory:discovered'])
    fixture.bindings.favorites.value = ['fixture:https://discovered.example']
    assert.deepEqual(Array.from(fixture.bindings.cards.value, card => card.key), ['saved:active', 'directory:discovered', 'saved:ordinary', 'saved:saved-favorite'])
  } finally { fixture.dispose() }
})

test('discovering and viewing servers do not fetch profiles; a different saved profile waits for confirmation', async () => {
  const profile = { text: 'confirmed-toml', inspection: { exists: true, identical: false, fingerprint: 'previous', server: { name: 'Existing' } } }
  const fixture = await setup(undefined, undefined, async () => ({ entries: [{ ...entry, id: 'server', kind: 'server' }] }), undefined, 'server', async () => profile)
  try {
    fixture.bindings.entries.value = [{ ...entry, id: 'server', kind: 'server' }]
    await vue.nextTick()
    fixture.bindings.openDetails('directory:server')
    await vue.nextTick()
    fixture.bindings.buildId.value = 'build-one'
    assert.equal(fixture.calls.some(call => call.method === 'directory.profile'), false)
    await fixture.bindings.join()
    assert.equal(fixture.calls.some(call => call.method === 'servers.importProfile'), false)
    assert.equal(fixture.bindings.profileConflict.value?.text, 'confirmed-toml', JSON.stringify({ calls: fixture.calls, error: fixture.bindings.error.value }))
    await fixture.bindings.overwriteDiscoveredProfile()
    assert.equal(fixture.calls.find(call => call.method === 'servers.importProfile').params.expectedFingerprint, 'previous')
    assert.deepEqual(fixture.routes, ['/home'])
  } finally { fixture.dispose() }
})

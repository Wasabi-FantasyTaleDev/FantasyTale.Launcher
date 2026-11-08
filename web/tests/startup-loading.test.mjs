import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import * as vue from 'vue'
import { clientFailureCode } from '../src/launchFailure.ts'
import * as schemes from '../src/plugins/schemes.ts'

function deferred() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

function setup(delays = {}, saved = {}) {
  const policyModule = { exports: {} }
  runInNewContext(ts.transpileModule(readFileSync(new URL('../src/accounts/login-policy.ts', import.meta.url), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { module: policyModule, exports: policyModule.exports })
  const catalog = deferred(), sync = deferred(), calls = [], scope = vue.effectScope()
  const game = { id: 'fixture', status: 'enabled', builds: [{ id: 'test-1', branch: 'test' }], clientLanguages: [] }
  const storage = new Map([['game', game.id], ...Object.entries(saved)])
  const call = async (method, params) => {
    calls.push(method)
    if (typeof delays[method] === 'function') return delays[method](params)
    if (delays[method]) return delays[method].promise
    if (method === 'games.list') return [game]
    if (method === 'store.sync') return sync.promise
    if (method === 'servers.catalog') return catalog.promise
    if (method === 'servers.list') return [{ id: 'server', game: game.id }]
    if (method === 'settings.network') return { ipPreference: 'china' }
    if (method === 'accounts.list') return { current: null, accounts: [] }
    return []
  }
  const module = { exports: {} }
  const source = ts.transpileModule(readFileSync(new URL('../src/state.ts', import.meta.url), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText
  const require = id => {
    if (id === 'vue') return vue
    if (id === './bridge') return { call, on() {}, inHost: true }
    if (id === './theme') return { prefs: vue.reactive({ systemAccent: false }) }
    if (id === './clientLanguages') return { supportedClientLanguages: () => [], clientLanguageInstalled: () => true }
    if (id === './accounts/expiry') return { accountExpiryDelay: () => null, liveAccountSessions: value => value }
    if (id === './accounts/login-policy') return policyModule.exports
    if (id === './i18n') return { i18n: { global: { t: key => key } } }
    if (id === './launchFailure') return { clientFailureCode }
    if (id === './plugins/schemes') return schemes
    throw new Error(`Unexpected dependency ${id}`)
  }
  scope.run(() => runInNewContext(source, { module, exports: module.exports, require, setTimeout, clearTimeout,
    localStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) },
    window: { addEventListener() {} }, document: { documentElement: { style: { removeProperty() {}, setProperty() {} } } },
  }))
  return { ...module.exports, catalog, sync, calls, game, storage, dispose: () => scope.stop() }
}

test('recovery cleans schemes before their game or client has been opened', () => {
  const broken = { id: 'fixture.plugin', version: '1.0.0', enabled: true, options: {} }
  const healthy = { ...broken, version: '2.0.0' }
  const fixture = setup({}, { 'pluginSchemes.other': JSON.stringify({ schemes: [
    { id: 'default', name: '', builds: { first: [broken], second: [healthy] } },
    { id: 'custom', name: 'Custom', builds: { first: [broken] } },
  ], selections: {} }) })
  try {
    fixture.forgetRecoveredPlugins({ game: 'other', instanceId: 'absent', pluginId: broken.id, version: broken.version })
    const saved = JSON.parse(fixture.storage.get('pluginSchemes.other'))
    assert.deepEqual(saved.schemes[0].builds, { first: [], second: [healthy] })
    assert.deepEqual(saved.schemes[1].builds, { first: [] })
  } finally { fixture.dispose() }
})

test('a recovery event during scheme application cannot be overwritten by its older response', async () => {
  const broken = { id: 'fixture.plugin', version: '1.0.0', enabled: true, options: {} }
  const client = { id: 'first', game: 'fixture', name: 'First', build: 'test-1', plugins: [broken] }
  const fixture = setup({
    'plugins.resolveScheme': params => params.plugins,
    'plugins.applyScheme': () => {
      fixture.forgetRecoveredPlugins({ game: client.game, instanceId: client.id, pluginId: broken.id, version: broken.version })
      return { ...client, plugins: [] }
    },
  }, { instance: client.id, 'pluginSchemes.fixture': JSON.stringify({ schemes: [
    { id: 'default', name: '', builds: { 'test-1': [broken], 'test-2': [broken] } },
  ], selections: {} }) })
  try {
    fixture.state.games = [fixture.game]
    fixture.state.instances = [client, { ...client, id: 'second', build: 'test-2' }]
    fixture.state.instancesLoaded = true
    await fixture.ensurePluginScheme()
    const saved = JSON.parse(fixture.storage.get('pluginSchemes.fixture'))
    assert.deepEqual(saved.schemes[0].builds, { 'test-1': [], 'test-2': [] })
    assert.ok(fixture.state.instances.every(instance => instance.plugins.length === 0))
    fixture.forgetRecoveredPlugins({ game: client.game, instanceId: 'second', pluginId: broken.id, version: broken.version })
    assert.deepEqual(JSON.parse(fixture.storage.get('pluginSchemes.fixture')), saved)
  } finally { fixture.dispose() }
})

test('local workspace becomes ready while remote catalog and store synchronization are still pending', async () => {
  const fixture = setup()
  try {
    await fixture.loadGames()
    assert.equal(fixture.state.loaded, true)
    assert.equal(fixture.state.games.length, 1)
    await new Promise(resolve => setImmediate(resolve))
    assert.equal(fixture.state.selectedServerId, 'server')
    assert.equal(fixture.state.serverLoading, true)
    assert.equal(fixture.state.catalog, null)
    assert.equal(fixture.calls.filter(method => method === 'servers.catalog').length, 1)
    fixture.catalog.resolve({ schema: 1, instances: [] })
    await new Promise(resolve => setImmediate(resolve))
    assert.equal(fixture.state.serverLoading, false)
    assert.equal(fixture.state.catalog.schema, 1)
  } finally { fixture.dispose() }
})

test('remote failure after startup stays in server diagnostics and cannot return the workspace to loading', async () => {
  const fixture = setup()
  try {
    await fixture.loadGames()
    fixture.catalog.reject(new Error('Fixture server unavailable'))
    await new Promise(resolve => setImmediate(resolve))
    assert.equal(fixture.state.loaded, true)
    assert.equal(fixture.state.serverLoading, false)
    assert.equal(fixture.state.serverError, 'Fixture server unavailable')
    assert.equal(fixture.state.error, '')
  } finally { fixture.dispose() }
})

test('overlapping instance refreshes share a single local inventory request', async () => {
  const listing = deferred(), fixture = setup({ 'instances.list': listing })
  try {
    const first = fixture.refreshInstances(false), second = fixture.refreshInstances(false)
    assert.equal(fixture.calls.filter(method => method === 'instances.list').length, 1)
    listing.resolve([])
    await Promise.all([first, second])
  } finally { fixture.dispose() }
})

test('plugin preparation and stale-session recovery do not hold the startup splash', async () => {
  const fixture = setup({ 'plugins.list': deferred(), 'instances.recoverStopped': deferred() })
  try {
    await fixture.loadGames()
    assert.equal(fixture.state.loaded, true)
    assert.ok(fixture.calls.includes('instances.recoverStopped'))
  } finally { fixture.dispose() }
})

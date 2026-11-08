import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import * as vue from 'vue'
import { clientFailureCode } from '../src/launchFailure.ts'
import * as schemes from '../src/plugins/schemes.ts'

function pending() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

const settle = () => new Promise(resolve => setImmediate(resolve))

test('opening the plugin inventory defers a busy scheme write while preserving its next-launch intent', async () => {
  const busy = Object.assign(new Error('instanceBusy.message'), { code: 'instance_in_use' })
  let blocked = true
  const fixture = setup({ 'plugins.applyScheme': () => blocked ? Promise.reject(busy) : { id: 'local', game: 'game', build: 'build', plugins: [] } })
  try {
    fixture.state.games = [{ id: 'game', status: 'enabled', builds: [{ id: 'build' }], supportsPlugins: true }]
    fixture.state.selectedGameId = 'game'
    fixture.state.instances = [{ id: 'local', game: 'game', build: 'build', plugins: [] }]
    fixture.state.selectedInstanceId = 'local'; fixture.state.instancesLoaded = true
    await fixture.refreshPlugins()
    assert.ok(fixture.calls.includes('plugins.list'))
    assert.ok(fixture.calls.includes('plugins.applyScheme'))
    assert.equal(fixture.state.error, '')
    await assert.rejects(fixture.ensurePluginScheme(), error => error.code === 'instance_in_use')
    blocked = false
    await fixture.ensurePluginScheme()
  } finally { fixture.dispose() }
})

const runningClient = { instanceId: 'local', processId: 123, hook: { state: 'ready' } }
test('a confirmed client crash takes priority over a later connection cleanup failure', async () => {
  const fixture = setup()
  try {
    const failure = { instanceId: 'local', error: 'launch_cleanup_failed', phase: 'bootstrap-dispose',
      exitConfirmed: true, exitCode: -1073741819 }
    fixture.emit('instances.error', failure)
    await settle()
    assert.equal(fixture.state.error, 'launchCleanup.clientExited')
    assert.equal(fixture.state.launchFailureMessage, fixture.state.error)
    assert.equal(fixture.state.launchFailure.exitCode, -1073741819)
    assert.equal(fixture.state.launchFailure.phase, 'bootstrap-dispose')
  } finally { fixture.dispose() }
})

for (const event of ['instances.changed', 'instances.exited', 'instances.readiness', 'instances.error']) {
  test(`${event} refreshes running state despite blocked inventory and rejects a stale process reply`, async () => {
    const inventory = pending(), previous = pending()
    let queries = 0
    const fixture = setup({ 'instances.list': inventory.promise,
      'instances.running': () => ++queries === 1 ? previous.promise : [] })
    const refresh = fixture.refreshInstances(false)
    try {
      fixture.state.running = [runningClient]
      fixture.emit(event, { instanceId: 'local', error: 'Cleanup failed' })
      previous.resolve([runningClient])
      await settle()
      assert.equal(fixture.state.instancesLoading, true)
      assert.equal(fixture.isRunning('local'), false)
      assert.equal(queries, 2)
      if (event === 'instances.error') assert.equal(fixture.state.error, 'Cleanup failed')
    } finally { inventory.resolve([]); await refresh; fixture.dispose() }
  })
}

test('shell polling and window focus repair missed exit events without loading client inventories', async () => {
  const timers = new Map()
  let timerId = 0, running = [runningClient]
  const fixture = setup({ 'instances.running': () => running }, '', {}, {
    setTimeout(callback, delay) { timers.set(++timerId, { callback, delay }); return timerId },
    clearTimeout(id) { timers.delete(id) },
  })
  const stop = fixture.pollRunning()
  try {
    await settle()
    assert.equal(fixture.isRunning('local'), true)
    assert.equal(timers.size, 1)
    const [id, timer] = [...timers][0]
    assert.equal(timer.delay, 2000)
    running = []; timers.delete(id); await timer.callback()
    assert.equal(fixture.isRunning('local'), false)
    running = [runningClient]; fixture.focus(); await settle()
    assert.equal(fixture.isRunning('local'), true)
    assert.equal(fixture.calls.includes('instances.list'), false)
    stop()
    assert.equal(timers.size, 0)
    const calls = fixture.calls.length
    fixture.focus(); await settle()
    assert.equal(fixture.calls.length, calls)
  } finally { stop(); fixture.dispose() }
})

test('overlapping running polls coalesce and a failed query preserves live state until a successful retry', async () => {
  const reply = pending()
  let queries = 0
  const fixture = setup({ 'instances.running': () => ++queries === 1 ? reply.promise : [] })
  try {
    fixture.state.running = [runningClient]
    const first = fixture.refreshRunning(), second = fixture.refreshRunning()
    assert.equal(first, second)
    assert.equal(queries, 1)
    reply.reject(new Error('Status unavailable'))
    await assert.rejects(first, /Status unavailable/)
    assert.equal(fixture.isRunning('local'), true)
    await fixture.refreshRunning()
    assert.equal(fixture.isRunning('local'), false)
    assert.equal(queries, 2)
  } finally { fixture.dispose() }
})

function setup(overrides = {}, savedInstance = '', saved = {}, scheduler = { setTimeout, clearTimeout }) {
  const policyModule = { exports: {} }
  runInNewContext(ts.transpileModule(readFileSync(new URL('../src/accounts/login-policy.ts', import.meta.url), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { module: policyModule, exports: policyModule.exports })
  const source = readFileSync(new URL('../src/state.ts', import.meta.url), 'utf8')
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const game = { id: 'game', name: { en: 'Game' }, status: 'enabled', builds: [{ id: 'build', branch: 'test' }], clientLanguages: ['en'] }
  const calls = [], storage = new Map([['instance', savedInstance], ...Object.entries(saved)]), module = { exports: {} }, locale = vue.ref('en')
  const events = new Map(), windowEvents = new Map()
  const replies = {
    'games.list': [game], 'instances.list': [{ id: 'local', game: 'game', build: 'build', plugins: [] }],
    'plugins.applyScheme': { id: 'local', game: 'game', build: 'build', plugins: [] },
    'plugins.resolveScheme': params => params.plugins,
    'instances.running': [], 'servers.list': [{ id: 'server', game: 'game' }],
    'servers.catalog': { instances: [] }, 'accounts.list': { accounts: [], current: null },
    'settings.network': { ipPreference: 'global' }, 'plugins.list': [], 'instances.recoverStopped': null,
    'store.sync': [], ...overrides,
  }
  const call = (method, params) => {
    calls.push(method)
    if (!(method in replies)) throw new Error(`Unexpected call ${method}`)
    return Promise.resolve(typeof replies[method] === 'function' ? replies[method](params) : replies[method])
  }
  const require = id => {
    if (id === 'vue') return vue
    if (id === './bridge') return { call, inHost: true, on(name, callback) { events.set(name, callback) } }
    if (id === './theme') return { prefs: { systemAccent: true } }
    if (id === './clientLanguages') return { supportedClientLanguages: () => [], clientLanguageInstalled: () => false }
    if (id === './accounts/expiry') return { accountExpiryDelay: () => null, liveAccountSessions: value => value }
    if (id === './accounts/login-policy') return policyModule.exports
    if (id === './launchFailure') return { clientFailureCode }
    if (id === './plugins/schemes') return schemes
    if (id === './i18n') return { i18n: { global: { t: key => key, locale } } }
    throw new Error(`Unexpected dependency ${id}`)
  }
  const scope = vue.effectScope()
  scope.run(() => runInNewContext(javascript, {
    module, exports: module.exports, require, ...scheduler,
    localStorage: { getItem: key => storage.get(key) ?? null, setItem: (key, value) => storage.set(key, value) },
    window: { addEventListener(name, callback) { if (!windowEvents.has(name)) windowEvents.set(name, new Set()); windowEvents.get(name).add(callback) },
      removeEventListener(name, callback) { windowEvents.get(name)?.delete(callback) } },
    document: { documentElement: { style: { removeProperty() {}, setProperty() {} } } },
  }))
  return { ...module.exports, calls, storage, locale, emit: (name, value) => events.get(name)?.(value),
    focus: () => windowEvents.get('focus')?.forEach(callback => callback()), dispose: () => scope.stop() }
}

test('store activation survives switching to an inactive disabled scheme and an unregistered build after restart', async () => {
  const old = { id: 'fixture.plugin', version: '1.0.0', enabled: false, options: { speed: 2 } }
  const selected = { ...old, enabled: true }, other = { id: 'fixture.other', version: '1.0.0', enabled: true, options: {} }
  let clients = [{ id: 'local', game: 'game', build: 'build', plugins: [old] },
    { id: 'second', game: 'game', build: 'second', plugins: [old, other] }]
  const library = { schemes: [{ id: 'default', name: '', builds: { build: [old], second: [old, other] } }], selections: {} }
  const overrides = { 'instances.list': () => clients.map(client => ({ ...client, plugins: schemes.cloneSelection(client.plugins) })),
    'plugins.applyScheme': params => {
      const client = clients.find(client => client.id === params.instanceId)
      client.plugins = schemes.cloneSelection(params.plugins).map(plugin => plugin.id === old.id ? { ...plugin, options: { speed: 2, ...plugin.options } } : plugin)
      return { ...client }
    } }
  const first = setup(overrides, 'local', { game: 'game', 'pluginSchemes.game': JSON.stringify(library) })
  let saved
  try {
    first.state.games = [{ id: 'game', status: 'enabled', builds: [{ id: 'build' }, { id: 'second' }, { id: 'third' }] }]
    first.state.selectedGameId = 'game'; first.state.selectedServerId = 'server'; first.state.instancesLoaded = true
    first.state.instances = clients.map(client => ({ ...client }))
    await first.ensurePluginScheme()
    const remember = first.prepareStorePluginInstallation({ id: old.id, game: 'game', version: old.version, builds: ['build', 'second', 'third'] }, 'server')
    remember('install-task')
    // Host activation reaches both registered clients; the third build has no configuration yet.
    clients[0].plugins = [selected]; clients[1].plugins = [selected, other]
    await first.refreshInstances(false)
    first.state.selectedInstanceId = 'second'
    await first.ensurePluginScheme(); await settle()
    assert.equal(first.selectedInstance.value.plugins.find(plugin => plugin.id === old.id).enabled, true)
    assert.deepEqual(JSON.parse(JSON.stringify(first.selectedInstance.value.plugins.find(plugin => plugin.id === other.id))), other)
    saved = Object.fromEntries(first.storage)
  } finally { first.dispose() }
  clients.push({ id: 'third', game: 'game', build: 'third', plugins: [old] })
  const second = setup(overrides, 'third', { ...saved, 'clientSelection.game': JSON.stringify({ instanceId: 'third', buildId: 'third' }) })
  try {
    second.state.games = [{ id: 'game', status: 'enabled', builds: [{ id: 'build' }, { id: 'second' }, { id: 'third' }] }]
    second.state.selectedGameId = 'game'; second.state.selectedServerId = 'server'; second.state.instancesLoaded = true
    second.state.instances = clients.map(client => ({ ...client }))
    await second.ensurePluginScheme(); await second.refreshInstances(false)
    assert.equal(second.selectedInstance.value.plugins.find(plugin => plugin.id === old.id).enabled, true)
    assert.equal(second.selectedInstance.value.plugins.find(plugin => plugin.id === old.id).options.speed, 2)
    // A later manual disable must not be undone by the recorded store choice.
    clients[2].plugins = [{ ...selected, enabled: false }]
    await second.refreshInstances(false); await second.ensurePluginScheme(true)
    assert.equal(second.selectedInstance.value.plugins.find(plugin => plugin.id === old.id).enabled, false)
  } finally { second.dispose() }
})

test('game visibility follows configured launcher locales and reappears when the locale changes', async () => {
  const fixture = setup()
  try {
    fixture.state.games = [
      { id: 'first', status: 'enabled', builds: [], clientLanguages: [], unsupportedRegions: ['zh-CN'] },
      { id: 'second', status: 'enabled', builds: [], clientLanguages: [] },
    ]
    fixture.state.selectedGameId = 'first'
    fixture.locale.value = 'zh-CN'
    await settle()
    assert.deepEqual(Array.from(fixture.homeGames.value, game => game.id), ['second'])
    assert.equal(fixture.state.selectedGameId, 'second')
    fixture.locale.value = 'zh-TW'
    await settle()
    assert.deepEqual(Array.from(fixture.homeGames.value, game => game.id), ['first', 'second'])
    fixture.locale.value = 'ko'
    await settle()
    assert.equal(fixture.homeGames.value.length, 2)
    assert.equal(fixture.state.games.length, 2)
  } finally { fixture.dispose() }
})

test('plugin schemes restore each server selection and preserve unresolved imports across inventory refresh', async () => {
  const first = { id: 'fixture.first', version: '1.0.0', enabled: true, required: false, options: { speed: 3 } }
  const missing = { id: 'fixture.missing', version: '2.0.0', enabled: true, required: false, options: { speed: 9 } }
  const library = { schemes: [{ id: 'default', name: '', builds: { build: [first] } },
    { id: 'custom', name: 'Custom', builds: { build: [first, missing] } }],
  selections: { [JSON.stringify(['server', 'build'])]: 'custom' } }
  const applied = [], fixture = setup({ 'plugins.applyScheme': params => {
    applied.push(params)
    return { id: 'local', game: 'game', build: 'build', plugins: params.plugins.filter(plugin => plugin.id !== missing.id) }
  } }, 'local', { 'pluginSchemes.game': JSON.stringify(library) })
  try {
    fixture.state.games = [{ id: 'game', status: 'enabled', builds: [{ id: 'build' }] }]
    fixture.state.selectedGameId = 'game'; fixture.state.selectedServerId = 'server'
    fixture.state.instances = [{ id: 'local', game: 'game', build: 'build', plugins: [first] }]
    fixture.state.instancesLoaded = true
    await fixture.ensurePluginScheme()
    fixture.capturePluginScheme(fixture.selectedInstance.value)
    assert.equal(fixture.pluginSchemeLibrary.value.schemes.find(s => s.id === 'custom').builds.build.length, 2)
    fixture.state.selectedServerId = 'other'
    await fixture.ensurePluginScheme()
    assert.equal(fixture.selectedPluginScheme.value, 'default')
    assert.equal(applied.at(-1).plugins.length, 1)
    fixture.state.selectedServerId = 'server'
    await fixture.ensurePluginScheme()
    assert.equal(fixture.selectedPluginScheme.value, 'custom')
    assert.equal(applied.at(-1).plugins.length, 2)
  } finally { fixture.dispose() }
})

test('packages installed while running are applied after exit without changing schemes', async () => {
  const first = { id: 'fixture.first', version: '1.0.0', enabled: true, options: {} }
  const missing = { id: 'fixture.missing', version: '2.0.0', enabled: true, options: { speed: 9 } }
  const library = { schemes: [{ id: 'default', name: '', builds: { build: [first, missing] } }], selections: {} }
  let installed = false, applies = 0
  const fixture = setup({ 'plugins.applyScheme': params => {
    applies++
    return { id: 'local', game: 'game', build: 'build', plugins: params.plugins.filter(p => installed || p.id !== missing.id) }
  } }, 'local', { 'pluginSchemes.game': JSON.stringify(library) })
  try {
    fixture.state.games = [{ id: 'game', status: 'enabled', builds: [{ id: 'build' }] }]
    fixture.state.selectedGameId = 'game'; fixture.state.selectedServerId = 'server'
    fixture.state.instances = [{ id: 'local', game: 'game', build: 'build', plugins: [first] }]
    fixture.state.instancesLoaded = true
    await fixture.ensurePluginScheme()
    assert.equal(applies, 1)
    installed = true; fixture.state.running = [runningClient]
    await fixture.ensurePluginScheme(true)
    assert.equal(applies, 1)
    fixture.state.running = []
    await fixture.ensurePluginScheme()
    assert.equal(applies, 2)
    assert.equal(fixture.selectedInstance.value.plugins.find(p => p.id === missing.id).options.speed, 9)
    await fixture.ensurePluginScheme()
    assert.equal(applies, 2)
  } finally { fixture.dispose() }
})

test('malformed saved schemes retain a backup and restore a usable default scheme', () => {
  const fixture = setup({}, '', { 'pluginSchemes.game': '{invalid' })
  try {
    fixture.state.selectedGameId = 'game'
    assert.equal(fixture.pluginSchemeLibrary.value.schemes[0].id, 'default')
    assert.equal(fixture.storage.get('pluginSchemes.game.invalid'), '{invalid')
    assert.equal(fixture.state.error, 'pluginSchemes.recovered')
  } finally { fixture.dispose() }
})

test('a completed plugin upgrade is captured from the host before a saved scheme can restore its previous release', async () => {
  const previous = { id: 'fixture.diagnostic', version: '1.2.0', enabled: true, options: { max_records: 128 } }
  const missing = { id: 'fixture.missing', version: '2.0.0', enabled: false, options: { speed: 9 } }
  const upgraded = { ...previous, version: '1.2.2' }
  const library = { schemes: [{ id: 'default', name: '', builds: { build: [previous, missing] } }], selections: {} }
  let host = { id: 'local', game: 'game', build: 'build', plugins: [previous] }
  const applied = [], fixture = setup({ 'instances.list': () => [host], 'plugins.applyScheme': params => {
    applied.push(params.plugins.map(p => ({ ...p, options: { ...p.options } })))
    host = { ...host, plugins: params.plugins.filter(p => p.id !== missing.id) }
    return host
  } }, 'local', { 'pluginSchemes.game': JSON.stringify(library) })
  try {
    fixture.state.games = [{ id: 'game', status: 'enabled', builds: [{ id: 'build' }] }]
    fixture.state.selectedGameId = 'game'; fixture.state.selectedServerId = 'server'
    fixture.state.instances = [host]; fixture.state.instancesLoaded = true
    await fixture.ensurePluginScheme()
    host = { ...host, plugins: [upgraded] }
    await fixture.refreshPluginSelectionsAfterDownload()
    assert.equal(host.plugins.find(p => p.id === previous.id).version, '1.2.2')
    assert.equal(fixture.selectedInstance.value.plugins.find(p => p.id === previous.id).version, '1.2.2')
    const saved = fixture.pluginSchemeLibrary.value.schemes[0].builds.build
    assert.equal(saved.find(p => p.id === previous.id).version, '1.2.2')
    assert.equal(saved.find(p => p.id === previous.id).options.max_records, 128)
    assert.equal(saved.find(p => p.id === missing.id).options.speed, 9)
    assert.equal(applied.at(-1).find(p => p.id === previous.id).version, '1.2.2')
  } finally { fixture.dispose() }
})

test('download completion rereads client selections after an older inventory request already in flight', async () => {
  const previous = { id: 'fixture.diagnostic', version: '1.2.0', enabled: false, options: { max_records: 64 } }
  const old = { id: 'local', game: 'game', build: 'build', plugins: [previous] }
  let host = old, reads = 0
  const inventory = pending(), fixture = setup({ 'instances.list': () => ++reads === 1 ? inventory.promise : [host],
    'plugins.applyScheme': params => { host = { ...host, plugins: params.plugins }; return host } }, 'local')
  try {
    fixture.state.games = [{ id: 'game', status: 'enabled', builds: [{ id: 'build' }] }]
    fixture.state.selectedGameId = 'game'; fixture.state.selectedServerId = 'server'
    fixture.state.instances = [old]; fixture.state.instancesLoaded = true
    await fixture.ensurePluginScheme()
    const earlier = fixture.refreshInstances(false)
    host = { ...old, plugins: [{ ...previous, version: '1.2.2' }] }
    const completion = fixture.refreshPluginSelectionsAfterDownload()
    inventory.resolve([old])
    await Promise.all([earlier, completion])
    assert.equal(host.plugins[0].version, '1.2.2')
    assert.equal(fixture.selectedInstance.value.plugins[0].version, '1.2.2')
    assert.equal(host.plugins[0].enabled, false)
    assert.equal(host.plugins[0].options.max_records, 64)
  } finally { fixture.dispose() }
})

test('workspace and server accounts load while client inspection and store sync remain pending', async () => {
  const instances = pending(), store = pending()
  const fixture = setup({ 'instances.list': instances.promise, 'store.sync': store.promise })
  try {
    await fixture.loadGames()
    await settle()
    assert.equal(fixture.state.loaded, true)
    assert.equal(fixture.state.selectedGameId, 'game')
    assert.deepEqual([...fixture.state.instances], [])
    assert.equal(fixture.state.ipPreference, 'global')
    assert.ok(fixture.calls.includes('accounts.list'))
    assert.ok(fixture.state.catalog)
    instances.resolve([{ id: 'local', game: 'game', build: 'build' }])
    await settle()
    assert.equal(fixture.state.selectedInstanceId, 'local')
    assert.equal(fixture.state.selectedBuildId, 'build')
  } finally { fixture.dispose() }
})

test('refreshing destinations for local play does not await the previously selected server catalog', async () => {
  const catalog = pending(), fixture = setup({ 'servers.catalog': catalog.promise })
  try {
    await fixture.loadGames(); await settle()
    const before = fixture.calls.filter(method => method === 'servers.catalog').length
    await fixture.refreshServers({ refreshSelectedServices: false })
    assert.equal(fixture.calls.filter(method => method === 'servers.catalog').length, before)
    assert.equal(fixture.state.servers[0].id, 'server')
  } finally { catalog.resolve({ instances: [] }); await settle(); fixture.dispose() }
})

test('a failed independent background task cannot prevent accounts or instances from loading', async () => {
  const network = pending(), store = pending()
  const fixture = setup({ 'settings.network': network.promise, 'store.sync': store.promise }, 'local')
  try {
    await fixture.loadGames()
    network.reject(new Error('Network preferences unavailable'))
    await settle()
    assert.equal(fixture.state.loaded, true)
    assert.equal(fixture.state.selectedInstanceId, 'local')
    assert.ok(fixture.state.catalog)
    assert.match(fixture.state.error, /Network preferences unavailable/)
  } finally { fixture.dispose() }
})

test('late startup inspection preserves a build the user selected while scanning', async () => {
  const instances = pending(), store = pending()
  const fixture = setup({ 'instances.list': instances.promise, 'store.sync': store.promise })
  try {
    await fixture.loadGames()
    fixture.state.selectedBuildId = 'other-build'
    instances.resolve([{ id: 'local', game: 'game', build: 'build' }])
    await settle()
    assert.equal(fixture.state.selectedBuildId, 'other-build')
    assert.equal(fixture.state.selectedInstanceId, '')
  } finally { fixture.dispose() }
})

const rememberedClient = { id: 'remembered', game: 'game', build: 'store-build', name: 'My client', clientInstalled: true }
const rememberedBuild = { id: 'store-build', branch: 'test', name: { en: 'Remembered release' } }
function rememberedWorkspace() {
  return {
    game: 'game',
    'clientSelection.game': JSON.stringify({ instanceId: 'remembered', buildId: 'store-build' }),
    clientWorkspace: JSON.stringify({ instances: [rememberedClient], builds: [{ game: 'game', builds: [rememberedBuild] }] }),
  }
}

test('startup keeps the previous client and store build visible while inspection is pending', async () => {
  const instances = pending(), store = pending()
  const fixture = setup({ 'instances.list': instances.promise, 'store.sync': store.promise }, 'remembered', rememberedWorkspace())
  try {
    await fixture.loadGames()
    await settle()
    assert.equal(fixture.selectedInstance.value?.id, 'remembered')
    assert.equal(fixture.selectedInstance.value?.clientInstalled, true)
    assert.equal(fixture.selectedBuild.value?.id, 'store-build')
    assert.equal(fixture.state.instancesLoaded, false)
    instances.resolve([rememberedClient])
    await settle()
    assert.equal(fixture.state.instancesLoaded, true)
    assert.equal(fixture.state.selectedInstanceId, 'remembered')
  } finally { fixture.dispose() }
})

test('successful inspection replaces a stale client cache, but a failure preserves it', async () => {
  for (const failure of [false, true]) {
    const instances = pending(), store = pending()
    const fixture = setup({ 'instances.list': instances.promise, 'store.sync': store.promise }, 'remembered', rememberedWorkspace())
    try {
      await fixture.loadGames()
      if (failure) instances.reject(new Error('Client metadata unavailable'))
      else instances.resolve([])
      await settle()
      assert.equal(fixture.state.instancesLoaded, !failure)
      assert.equal(fixture.state.instances.some(item => item.id === 'remembered'), failure)
      assert.equal(JSON.parse(fixture.storage.get('clientWorkspace')).instances.some(item => item.id === 'remembered'), failure)
    } finally { fixture.dispose() }
  }
})

test('a successful refresh persists display data and restores it on the next launch without plugin settings', async () => {
  const store = pending()
  const first = setup({ 'instances.list': [{ ...rememberedClient, plugins: [{ options: { privateValue: 'secret' } }] }], 'store.sync': store.promise })
  let saved
  try {
    await first.loadGames()
    await settle()
    saved = Object.fromEntries(first.storage)
    assert.equal(saved.clientWorkspace.includes('privateValue'), false)
    assert.equal(saved.clientWorkspace.includes('secret'), false)
  } finally { first.dispose() }
  const second = setup({ 'instances.list': pending().promise, 'store.sync': pending().promise }, '', saved)
  try {
    await second.loadGames()
    assert.equal(second.selectedInstance.value?.id, 'remembered')
    assert.equal(second.selectedInstance.value?.clientInstalled, true)
  } finally { second.dispose() }
})

test('a failed running-process query cannot discard a successful client list', async () => {
  const running = pending(), store = pending()
  const fixture = setup({ 'instances.running': running.promise, 'store.sync': store.promise })
  try {
    await fixture.loadGames()
    running.reject(new Error('Process status unavailable'))
    await settle()
    assert.equal(fixture.state.instancesLoaded, true)
    assert.equal(fixture.selectedInstance.value?.id, 'local')
  } finally { fixture.dispose() }
})


test('process-query failure cannot end inventory loading before the client list completes', async () => {
  const instances = pending(), running = pending()
  const fixture = setup({ 'instances.list': instances.promise, 'instances.running': running.promise, 'store.sync': pending().promise })
  try {
    await fixture.loadGames()
    running.reject(new Error('Process status unavailable'))
    await settle()
    assert.equal(fixture.state.instancesLoading, true)
    assert.equal(fixture.state.instancesLoaded, false)
    instances.resolve([])
    await settle()
    assert.equal(fixture.state.instancesLoading, false)
    assert.equal(fixture.state.instancesLoaded, true)
  } finally { fixture.dispose() }
})

test('cold or corrupt-cache startup stays pending until authoritative inventory arrives', async () => {
  for (const cache of [undefined, '{broken', 'null', '{"instances":[null],"builds":[]}']) {
    const instances = pending()
    const fixture = setup({ 'instances.list': instances.promise, 'store.sync': pending().promise }, '', cache ? { clientWorkspace: cache } : {})
    try {
      await fixture.loadGames()
      await settle()
      assert.equal(fixture.state.instancesLoaded, false)
      assert.equal(fixture.selectedInstance.value, undefined)
      instances.resolve([])
      await settle()
      assert.equal(fixture.state.instancesLoaded, true)
      assert.equal(fixture.selectedBuild.value.id, 'build')
    } finally { fixture.dispose() }
  }
})

test('switching games restores each game client and version independently', async () => {
  const other = { id: 'other-client', game: 'other', build: 'other-build', name: 'Other client', clientInstalled: true }
  const fixture = setup({
    'games.list': [
      { id: 'game', status: 'enabled', builds: [rememberedBuild], clientLanguages: [] },
      { id: 'other', status: 'enabled', builds: [{ ...rememberedBuild, id: 'other-build' }], clientLanguages: [] },
    ],
    'instances.list': [rememberedClient, other], 'store.sync': pending().promise,
  }, 'remembered', { ...rememberedWorkspace(), 'clientSelection.other': JSON.stringify({ instanceId: other.id, buildId: other.build }) })
  try {
    await fixture.loadGames()
    await settle()
    fixture.selectGame('other')
    await settle()
    assert.equal(fixture.selectedInstance.value.id, other.id)
    fixture.selectGame('game')
    await settle()
    assert.equal(fixture.selectedInstance.value.id, rememberedClient.id)
    assert.equal(fixture.selectedBuild.value.id, rememberedClient.build)
  } finally { fixture.dispose() }
})

test('choosing a version before inspection finishes resolves to its existing client', async () => {
  const instances = pending()
  const fixture = setup({ 'instances.list': instances.promise, 'store.sync': pending().promise })
  try {
    await fixture.loadGames()
    fixture.state.selectedBuildId = 'build'
    instances.resolve([{ id: 'local', game: 'game', build: 'build', clientInstalled: true }])
    await settle()
    assert.equal(fixture.selectedInstance.value.id, 'local')
    assert.equal(fixture.selectedInstance.value.clientInstalled, true)
  } finally { fixture.dispose() }
})


test('fast store synchronization cannot remove a remembered version while inventory is pending', async () => {
  const instances = pending()
  const fixture = setup({ 'instances.list': instances.promise }, 'remembered', rememberedWorkspace())
  try {
    await fixture.loadGames()
    await settle()
    assert.equal(fixture.state.instancesLoaded, false)
    assert.equal(fixture.selectedBuild.value.id, rememberedBuild.id)
    assert.equal(fixture.selectedInstance.value.clientInstalled, true)
    instances.resolve([])
    await settle()
    assert.equal(fixture.state.instancesLoaded, true)
    assert.equal(fixture.selectedBuild.value.id, 'build')
  } finally { fixture.dispose() }
})


test('cached game definitions make startup interactive before any host catalog response', async () => {
  const games = pending(), instances = pending()
  const saved = rememberedWorkspace()
  const workspace = JSON.parse(saved.clientWorkspace)
  workspace.games = [{ id: 'game', name: { en: 'Game' }, status: 'enabled', builds: [rememberedBuild], clientLanguages: [] }]
  saved.clientWorkspace = JSON.stringify(workspace)
  const fixture = setup({ 'games.list': games.promise, 'instances.list': instances.promise, 'store.sync': pending().promise }, 'remembered', saved)
  try {
    await fixture.loadGames()
    assert.equal(fixture.state.loaded, true)
    assert.equal(fixture.state.instancesLoaded, false)
    assert.equal(fixture.state.clientWorkspaceCached, true)
    assert.equal(fixture.selectedInstance.value.id, 'remembered')
    assert.equal(fixture.selectedBuild.value.id, 'store-build')
    fixture.state.selectedInstanceId = ''
    fixture.state.selectedBuildId = 'store-build'
    assert.equal(fixture.selectedBuild.value.id, 'store-build')
  } finally { fixture.dispose() }
})


const savedSignIn = { game: 'game', 'server.game': 'server' }
const restoredAccount = { username: 'private-login', displayName: 'Player', stats: {} }
const restoredSessions = { current: restoredAccount, accounts: [{ account: restoredAccount, expiresAt: '2099-01-01T00:00:00Z' }] }

test('saved-session startup advertises restoration before host data and ends after renewal', async () => {
  const accounts = pending()
  const fixture = setup({ 'accounts.list': accounts.promise, 'store.sync': pending().promise }, '', savedSignIn)
  try {
    assert.equal(fixture.state.accountRestoring, true)
    await fixture.loadGames()
    await settle()
    assert.equal(fixture.state.accountRestoring, true)
    assert.equal(fixture.state.accountsLoading, true)
    assert.equal(fixture.state.account, null)
    accounts.resolve(restoredSessions)
    await settle()
    assert.equal(fixture.state.accountRestoring, false)
    assert.equal(fixture.state.accountsLoading, false)
    assert.equal(fixture.state.account.displayName, 'Player')
    assert.equal([...fixture.storage.values()].some(value => value.includes('private-login')), false)
  } finally { fixture.dispose() }
})

test('signed-out startup finishes its connection state after the initial account response', async () => {
  const accounts = pending()
  const fixture = setup({ 'accounts.list': accounts.promise, 'store.sync': pending().promise }, '', savedSignIn)
  try {
    assert.equal(fixture.state.accountRestoring, true)
    await fixture.loadGames()
    await settle()
    assert.equal(fixture.state.accountsLoading, true)
    assert.equal(fixture.state.accountRestoring, true)
    accounts.resolve({ current: null, accounts: [] })
    await settle()
    assert.equal(fixture.state.accountsLoading, false)
  } finally { fixture.dispose() }
})

test('expired or failed restoration ends its spinner and does not fabricate a signed-in account', async () => {
  for (const failure of [false, true]) {
    const accounts = pending()
    const fixture = setup({ 'accounts.list': accounts.promise, 'store.sync': pending().promise }, '', savedSignIn)
    try {
      await fixture.loadGames()
      await settle()
      if (failure) accounts.reject(new Error('Sign-in service unavailable'))
      else accounts.resolve({ current: null, accounts: [] })
      await settle()
      assert.equal(fixture.state.accountRestoring, false)
      assert.equal(fixture.state.accountsLoading, false)
      assert.equal(fixture.state.account, null)
      assert.equal(fixture.state.serverError, failure ? 'Sign-in service unavailable' : '')
    } finally { fixture.dispose() }
  }
})

test('refreshing the same server retains its valid visible account during pending requests', async () => {
  const accounts = pending(), catalog = pending()
  const fixture = setup({ 'accounts.list': accounts.promise, 'servers.catalog': catalog.promise }, '', savedSignIn)
  try {
    fixture.state.account = restoredAccount
    fixture.state.accounts = restoredSessions.accounts
    const refreshing = fixture.refreshServices()
    assert.equal(fixture.state.account.displayName, 'Player')
    assert.equal(fixture.state.accounts.length, 1)
    assert.equal(fixture.state.accountRestoring, false)
    accounts.resolve(restoredSessions)
    catalog.resolve({ instances: [] })
    await refreshing
  } finally { fixture.dispose() }
})

test('a late request cannot clear the restoration state of another selected server', async () => {
  const first = pending()
  const fixture = setup({ 'accounts.list': first.promise }, '', savedSignIn)
  try {
    const readingFirst = fixture.refreshAccounts()
    fixture.state.selectedServerId = 'other'
    assert.equal(fixture.state.accountRestoring, true)
    first.resolve(restoredSessions)
    await readingFirst
    assert.equal(fixture.state.accountRestoring, true)
    assert.equal(fixture.state.account, null)
  } finally { fixture.dispose() }
})

test('failure before account requests clears the initial restoration indicator', async () => {
  const games = pending()
  const fixture = setup({ 'games.list': games.promise }, '', savedSignIn)
  try {
    const loading = fixture.loadGames()
    games.reject(new Error('Game definitions unavailable'))
    await loading
    assert.equal(fixture.state.accountRestoring, false)
    assert.match(fixture.state.error, /Game definitions unavailable/)
  } finally { fixture.dispose() }
})


test('name-only room discovery does not restore accounts or request a login', async () => {
  const fixture = setup({ 'servers.catalog': { instances: [{ build: 'test-1', launcherLogin: 'none' }], launcherLogin: 'none' } }, '', savedSignIn)
  try {
    fixture.state.servers = [{ id: fixture.state.selectedServerId, game: 'fixture', kind: 'room' }]
    fixture.calls.length = 0
    await fixture.refreshServices()
    assert.equal(fixture.calls.includes('accounts.list'), false)
    assert.equal(fixture.state.accountRestoring, false)
    assert.equal(fixture.state.account, null)
    assert.equal(fixture.state.authOpen, false)
  } finally { fixture.dispose() }
})

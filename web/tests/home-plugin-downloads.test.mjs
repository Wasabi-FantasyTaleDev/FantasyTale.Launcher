import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, compileTemplate, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as model from '../src/downloads/model.ts'
import * as clientVersion from '../src/clientVersion.ts'
import { clientLanguageInstalled } from '../src/clientLanguages.ts'

const flush = async () => { for (let i = 0; i < 40; i++) await Promise.resolve() }
const task = () => ({ id: 'plugin-download', stage: 'downloading', target: { kind: 'plugin', gameId: 'fixture', instanceId: 'one' } })
// The plugin policy prompt's answer: false is an unmet requirement the user did not resolve.
let policyAllows = true

function setup(recommended = false, pending = false, requiredAnswer = { keys: [], tasks: [] }, respond, mounted = false, timers = { setTimeout: () => 1, clearTimeout() {} }) {
  const state = vue.reactive({ servers: [], selectedGameId: 'fixture', selectedServerId: 'server', selectedInstanceId: 'one', account: { username: 'account' }, clientLanguage: 'id', plugins: [], running: [] })
  const tasks = vue.ref([]), updates = vue.ref([]), calls = [], launches = [], routes = [], scope = vue.effectScope(), events = new Map()
  const trackDownload = task => { tasks.value = [task] }
  const dependencies = {
    state, selectedInstance: vue.computed(() => ({ id: state.selectedInstanceId, name: 'Client', language: 'id', plugins: [], clientInstalled: true })),
    loginRequired: vue.ref(true), loginPolicy: vue.ref('required'), gameReady: vue.ref(true), selectedBuild: vue.ref({ id: 'test-1', nativeLanguage: 'id', additionalLanguages: { 'zh-CN': 'test.zh' } }), gameInstances: vue.ref([]), homeGames: vue.ref([]),
    selectedServer: vue.ref({ kind: 'server' }),
    selectedGame: vue.ref({ id: 'fixture', localHostingEnabled: true }), gameServers: vue.ref([]), refreshServers: async () => {}, isRunning: () => false,
    perform: async action => { await action(); return true },
    ensurePluginScheme: async () => {}, pluginSchemesBusy: vue.reactive({ value: false }),
    setClientLanguage: async language => { state.clientLanguage = language },
  }
  dependencies.isClientLanguageInstalled = code => clientLanguageInstalled(dependencies.selectedBuild.value, code, state.plugins)
  const source = readFileSync(new URL('../src/pages/HomePage.vue', import.meta.url), 'utf8')
  const script = compileScript(parse(source).descriptor, { id: 'home-test' }).content
  const javascript = ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return vue
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key, locale: vue.ref('zh-CN') }) }
    if (id === 'vue-router') return { useRouter: () => ({ push: route => routes.push(route) }) }
    if (id === '../state') return dependencies
    // Ungrouped servers: no sections, so the section helpers change nothing.
    if (id === '../servers/sections') return { serverStatus: { serverId: '', value: null }, pendingAuthSection: { value: null },
      loadServerStatus: async () => ({ sections: [], servers: [] }), sectionsShown: () => false, accountSectionsShown: () => false,
      signInSections: () => [], accountSection: () => null, defaultSection: () => null, sectionName: id => id ?? '',
      accountKey: account => account ? `${account.section ?? ''}\u0000${account.username}` : '' }
    if (id === '../store/background') return { availableStoreUpdates: updates }
    if (id === '../theme') return { prefs: vue.reactive({ nsfw: false }) }
    if (id === '../i18n') return { localized: value => value, clientRegion: value => value }
    if (id === '../clientVersion') return clientVersion
    if (id === '../bridge') return { on: (event, callback) => { events.set(event, callback); return () => events.delete(event) }, inHost: true, call: async (method, parameters) => {
      calls.push(method)
      if (respond) return respond(method, parameters)
      if (method === 'instances.launch') launches.push(parameters)
      if (method === 'plugins.launchTransfers' && pending && !tasks.value.some(t => t.stage === 'completed')) return [task()]
      return []
    } }
    if (id === '../downloads/live') return { downloadTasks: tasks, trackDownload }
    if (id === '../downloads/model') return model
    if (id === '../plugins/resourceConflicts') return { launchWithResourceChoices: async (launch, valid) => valid() ? launch([]) : { cancelled: true }, finishResourceConflicts() {} }
    if (id === '../plugins/launchWarnings') return { requestLaunchWarnings: async () => [], finishLaunchWarnings() {} }
    if (id === '../plugins/launchUpdates') return { requestRequiredUpdates: async () => requiredAnswer, finishRequiredUpdates() {} }
    if (id === '../plugins/pluginPolicy') return { requestPluginPolicy: async () => policyAllows, cancelPolicy() {} }
    if (id === '../plugins/recommendations') return { dismissRecommendations() {}, requestRecommendations: async () => {
      if (recommended) { recommended = false; trackDownload(task()); return false }
      return true
    } }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, setTimeout: timers.setTimeout, clearTimeout: timers.clearTimeout, ResizeObserver: class { observe() {} disconnect() {} }, localStorage: { getItem: () => 'LocalPlayer', setItem() {} } })
  let bindings, app
  const route = vue.reactive({ name: 'home', path: '/home', fullPath: '/home', meta: {} })
  if (mounted) {
    const node = () => ({ children: [], parent: null })
    const renderer = vue.createRenderer({
      createElement: node, createText: node, createComment: node, setText() {}, setElementText() {}, patchProp() {},
      parentNode: child => child.parent, nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] ?? null,
      insert(child, parent, anchor) {
        if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1)
        const index = anchor ? parent.children.indexOf(anchor) : -1
        if (index < 0) parent.children.push(child); else parent.children.splice(index, 0, child)
        child.parent = parent
      },
      remove(child) { if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1); child.parent = null },
    })
    const home = vue.defineComponent({ name: 'HomePage', setup() { bindings = module.exports.default.setup({}, { expose() {} }); return () => vue.h('div') } })
    const other = vue.defineComponent({ name: 'OtherPage', render: () => vue.h('div') })
    const shell = parse(readFileSync(new URL('../src/App.vue', import.meta.url), 'utf8')).descriptor.template.content
      .match(/<router-view[\s\S]*?<\/router-view>/)[0]
    const renderModule = { exports: {} }
    runInNewContext(ts.transpileModule(compileTemplate({ source: shell, id: 'navigation-test' }).code,
      { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText,
      { module: renderModule, exports: renderModule.exports, require: () => ({ ...vue, Transition: (_, { slots }) => slots.default() }) })
    app = renderer.createApp({ setup: () => ({ route }), render: renderModule.exports.render })
    app.config.errorHandler = error => { console.error(error); throw error }
    app.component('router-view', { setup: (_, { slots }) => () => slots.default({ Component: route.name === 'home' ? home : other }) })
    app.mount(node())
  } else bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  return { state, tasks, updates, calls, launches, routes, bindings, dependencies, route, events, dispose: () => { app?.unmount(); scope.stop() } }
}

test('home update shortcut follows the selected game and opens the store update tab', () => {
  const fixture = setup()
  try {
    assert.equal(fixture.bindings.hasPluginUpdates.value, false)
    fixture.updates.value = [{ item: { game: 'other' } }]
    assert.equal(fixture.bindings.hasPluginUpdates.value, false)
    fixture.updates.value.push({ item: { game: 'fixture' } })
    assert.equal(fixture.bindings.hasPluginUpdates.value, true)
    fixture.bindings.openPluginUpdates()
    assert.equal(JSON.stringify(fixture.routes), JSON.stringify([{ name: 'store', query: { kind: 'plugin', updates: '1' } }]))
    fixture.dependencies.selectedGame.value = { id: 'third' }
    assert.equal(fixture.bindings.hasPluginUpdates.value, false)
  } finally { fixture.dispose() }
})

test('resource progress survives navigation and a later launch after explicit cancellation', async () => {
  const fixture = setup(false, false, { keys: [], tasks: [] }, undefined, true)
  try {
    const report = progress => fixture.events.get('instances.resourceProgress')({ instanceId: 'one', progress })
    const progress = { path: 'Packs/Models.pkn', completed: 1, total: 4, phase: 'building', percentage: 25 }
    report(progress)
    assert.equal(fixture.bindings.resourceVisible.value, true)
    fixture.route.name = 'plugins'; fixture.route.path = fixture.route.fullPath = '/plugins'
    await vue.nextTick()
    report({ ...progress, percentage: 50 })
    assert.equal(fixture.bindings.resourceProgress.value.percentage, 50)
    await fixture.bindings.cancelResources()
    report({ ...progress, percentage: 75 })
    assert.equal(fixture.bindings.resourceVisible.value, false)
    report(null)
    report(progress)
    assert.equal(fixture.bindings.resourceVisible.value, true)
    assert.equal(fixture.calls.filter(method => method === 'instances.cancelResourcePreparation').length, 1)
  } finally { fixture.dispose() }
})

test('navigation keeps an in-flight Home launch alive until its prerequisites finish', async () => {
  let finish
  const wait = new Promise(resolve => { finish = resolve })
  const fixture = setup(false, false, { keys: [], tasks: [] }, async method => {
    if (method === 'plugins.launchTransfers') { await wait; return [] }
    return []
  }, true)
  try {
    const launching = fixture.bindings.launch()
    await flush()
    fixture.route.name = 'plugins'; fixture.route.path = fixture.route.fullPath = '/plugins'
    await vue.nextTick()
    finish(); await launching; await flush()
    assert.equal(fixture.calls.filter(method => method === 'instances.launch').length, 1)
    fixture.route.name = 'home'; fixture.route.path = fixture.route.fullPath = '/home'
    await vue.nextTick()
    assert.equal(fixture.bindings.launchStarting.value, false)
  } finally { fixture.dispose() }
})

test('name-only room launches use the local player name without reopening account login', async () => {
  const fixture = setup()
  try {
    fixture.dependencies.selectedServer.value = { kind: 'room' }
    fixture.dependencies.loginPolicy.value = 'none'
    fixture.dependencies.loginRequired.value = false
    fixture.state.account = undefined
    fixture.bindings.account.value = 'PreviousRemoteName'
    fixture.bindings.requestLaunch()
    await flush()
    assert.equal(fixture.bindings.loginOpen.value, false)
    assert.equal(fixture.launches[0].account, 'LocalPlayer')
  } finally { fixture.dispose() }
})

test('saving local settings refreshes services without starting a game', async () => {
  const fixture = setup()
  let complete
  try {
    fixture.dependencies.refreshServices = () => new Promise(resolve => { complete = resolve })
    const connecting = fixture.bindings.localSaved()
    fixture.state.selectedInstanceId = 'other'
    complete(); await connecting; await flush()
    assert.equal(fixture.launches.length, 0)
  } finally { fixture.dispose() }
})

test('starting state begins before asynchronous launch preparation and follows native readiness', async () => {
  const fixture = setup()
  try {
    const launch = fixture.bindings.launch()
    assert.equal(fixture.bindings.launchStarting.value, true)
    await launch
    assert.equal(fixture.bindings.launchStarting.value, false)
    fixture.state.running = [{ instanceId: 'one', hook: { state: 'starting' } }]
    assert.equal(fixture.bindings.launchStarting.value, true)
    fixture.state.running[0].hook.state = 'ready'
    assert.equal(fixture.bindings.launchStarting.value, false)
    fixture.state.running[0].hook.state = 'failed'
    assert.equal(fixture.bindings.hookFailed.value, true)
  } finally { fixture.dispose() }
})

test('choosing a missing translation opens its exact store package without changing the language', async () => {
  const fixture = setup()
  try {
    await fixture.bindings.chooseClientLanguage('zh-CN')
    assert.equal(fixture.state.clientLanguage, 'id')
    assert.equal(fixture.routes[0].name, 'store')
    assert.equal(fixture.routes[0].query.package, 'test.zh')
    fixture.state.plugins = [{ id: 'test.zh', kind: 'files', available: true, selection: { enabled: true } }]
    await fixture.bindings.chooseClientLanguage('zh-CN')
    assert.equal(fixture.state.clientLanguage, 'zh-CN')
    assert.equal(fixture.routes.length, 1)
  } finally { fixture.dispose() }
})

test('recommended plugins stay on Home and resume the requested launch after installation', async () => {
  const fixture = setup(true)
  try {
    await fixture.bindings.launch()
    assert.equal(fixture.routes.length, 0)
    assert.equal(fixture.bindings.clientUpdate.value.id, 'plugin-download')
    assert.equal(fixture.calls.includes('instances.launch'), false)
    fixture.tasks.value = [{ ...task(), stage: 'completed' }]
    await flush()
    assert.equal(fixture.calls.filter(method => method === 'instances.launch').length, 1)
    assert.equal(fixture.launches[0].launcherLanguage, 'zh-CN')
  } finally { fixture.dispose() }
})

test('starting uses cached update confirmation without requesting source discovery', async () => {
  const fixture = setup(false, true, { keys: ['auth@1->2'], tasks: ['plugin-download'] })
  try {
    await fixture.bindings.launch()
    assert.equal(fixture.launches.length, 1)
    assert.deepEqual(Array.from(fixture.launches[0].confirmedRequiredPluginUpdates), ['auth@1->2'])
    assert.deepEqual(Array.from(fixture.launches[0].confirmedPendingPlugins), ['plugin-download'])
    assert.equal(fixture.calls.includes('plugins.ensureLatest'), false)
    assert.equal(fixture.calls.includes('plugins.checkUpdates'), false)
    assert.equal(fixture.bindings.pluginTransfers.value.length, 0)
  } finally { fixture.dispose() }
})

test('canceling required update confirmation does not launch', async () => {
  const fixture = setup(false, false, null)
  try { await fixture.bindings.launch(); assert.equal(fixture.launches.length, 0) }
  finally { fixture.dispose() }
})

test('an unresolved plugin requirement blocks the launch', async () => {
  const fixture = setup()
  policyAllows = false
  try { await fixture.bindings.launch(); assert.equal(fixture.launches.length, 0) }
  finally { policyAllows = true; fixture.dispose() }
})

for (const answer of ['wait', 'now', 'cancel']) test(`pending optional plugin confirmation supports ${answer}`, async () => {
  const fixture = setup(false, true)
  try {
    const launch = fixture.bindings.launch()
    await flush()
    assert.equal(fixture.bindings.pluginTransfers.value.length, 1)
    assert.equal(fixture.launches.length, 0)
    fixture.bindings.finishPluginTransfers(answer)
    await launch
    if (answer === 'now') {
      assert.deepEqual(Array.from(fixture.launches[0].confirmedPendingPlugins), ['plugin-download'])
    } else {
      assert.equal(fixture.launches.length, 0)
      if (answer === 'wait') {
        fixture.tasks.value = [{ ...task(), stage: 'completed' }]
        await flush()
        assert.equal(fixture.launches.length, 1)
      }
    }
  } finally { fixture.dispose() }
})

for (const change of ['client', 'server', 'account', 'unmount']) test(`pending confirmation cannot authorize a changed ${change}`, async () => {
  const fixture = setup(false, true)
  try {
    const launch = fixture.bindings.launch(); await flush()
    if (change === 'client') fixture.state.selectedInstanceId = 'two'
    if (change === 'server') fixture.state.selectedServerId = 'other'
    if (change === 'account') fixture.state.account.username = 'other'
    if (change === 'unmount') fixture.dispose()
    await flush(); fixture.bindings.finishPluginTransfers('now'); await launch
    assert.equal(fixture.launches.length, 0)
  } finally { fixture.dispose() }
})

for (const change of ['client', 'server', 'account', 'failed', 'canceled', 'unmount']) test(`plugin completion cannot launch after ${change}`, async () => {
  const fixture = setup(true)
  try {
    await fixture.bindings.launch()
    if (change === 'client') fixture.state.selectedInstanceId = 'two'
    if (change === 'server') fixture.state.selectedServerId = 'other'
    if (change === 'account') fixture.state.account.username = 'other'
    if (change === 'unmount') fixture.dispose()
    fixture.tasks.value = [{ ...task(), stage: ['failed', 'canceled'].includes(change) ? change : 'completed' }]
    await flush()
    assert.equal(fixture.calls.includes('instances.launch'), false)
  } finally { fixture.dispose() }
})


test('a cached installed client can launch before background inventory finishes', async () => {
  const fixture = setup()
  try {
    fixture.state.instancesLoaded = false
    fixture.state.clientWorkspaceCached = true
    fixture.state.clientLanguage = 'zh-CN'
    assert.equal(fixture.bindings.clientPending.value, false)
    await fixture.bindings.launch()
    assert.equal(fixture.launches.length, 1)
    assert.equal(fixture.launches[0].id, 'one')
    assert.equal(fixture.calls.includes('instances.list'), false)
    assert.equal(fixture.calls.includes('instances.update'), false)
  } finally { fixture.dispose() }
})


test('account restoration blocks duplicate login while version selection remains independent', async () => {
  const fixture = setup()
  try {
    fixture.state.account = null
    fixture.state.authOpen = false
    fixture.state.accountRestoring = true
    assert.equal(fixture.bindings.loginConnecting.value, true)
    fixture.bindings.requestLaunch()
    assert.equal(fixture.state.authOpen, false)
    assert.equal(fixture.launches.length, 0)
    fixture.state.accountRestoring = false
    fixture.bindings.requestLaunch()
    assert.equal(fixture.state.authOpen, true)
  } finally { fixture.dispose() }
})

test('a pending login catalog keeps the connection animation until sign-in is available', async () => {
  const fixture = setup()
  try {
    fixture.state.account = null
    fixture.state.authOpen = false
    fixture.state.accountRestoring = false
    fixture.state.serverLoading = true
    assert.equal(fixture.bindings.loginConnecting.value, true)
    fixture.bindings.requestLaunch()
    assert.equal(fixture.state.authOpen, false)
    fixture.state.serverLoading = false
    assert.equal(fixture.bindings.loginConnecting.value, false)
    fixture.bindings.requestLaunch()
    assert.equal(fixture.state.authOpen, true)
  } finally { fixture.dispose() }
})


test('name-only play without a remembered nickname prompts without account authentication', async () => {
  const fixture = setup()
  try {
    fixture.dependencies.selectedServer.value = { kind: 'room' }
    fixture.dependencies.loginPolicy.value = 'none'
    fixture.dependencies.loginRequired.value = false
    fixture.state.account = undefined
    fixture.bindings.localPlayerName.value = ''
    fixture.bindings.requestLaunch()
    await flush()
    assert.equal(fixture.bindings.nicknameOpen.value, true)
    assert.equal(fixture.bindings.loginOpen.value, false)
    assert.equal(fixture.state.authOpen, undefined)
    assert.equal(fixture.launches.length, 0)
    fixture.bindings.localPlayerName.value = 'NewNickname'
    fixture.bindings.nicknameOpen.value = false
    fixture.bindings.requestLaunch()
    await flush()
    assert.equal(fixture.launches[0].account, 'NewNickname')
    assert.equal(fixture.bindings.loginOpen.value, false)
  } finally { fixture.dispose() }
})

test('local-only destinations appear once through the single-player selector entry', () => {
  const fixture = setup()
  try {
    fixture.dependencies.gameServers.value = [{ id: 'remote', name: 'Remote' }, { id: 'local-one', name: 'Fixture', localOnly: true }]
    const options = fixture.bindings.servers.value
    assert.deepEqual(Array.from(options, item => item.value), ['remote', 'action:local-play', 'action:add-server'])
  } finally { fixture.dispose() }
})


test('selecting single-player reuses a ready matching invocation without reopening setup', async () => {
  const fixture = setup(false, false, undefined, async method => {
    if (method === 'hosting.list') return {
      profiles: [{ id: 'host', game: 'fixture', build: 'test-1', mode: 'private' }],
      sessions: [{ profileId: 'host', runId: 'ready-run', state: 'ready' }],
    }
    if (method === 'hosting.destination') return { id: 'local-host', game: 'fixture' }
    throw new Error(method)
  })
  try {
    fixture.state.servers = [{ id: 'local-host', game: 'fixture', localOnly: true }]
    await fixture.bindings.chooseServer('action:local-play')
    assert.equal(fixture.state.selectedServerId, 'local-host')
    assert.equal(fixture.bindings.localPlayOpen.value, false)
    assert.deepEqual(fixture.calls, ['hosting.list', 'hosting.destination'])
  } finally { fixture.dispose() }
})

test('single-player setup only opens when no matching ready invocation exists', async () => {
  const fixture = setup(false, false, undefined, async () => ({ profiles: [], sessions: [] }))
  try {
    await fixture.bindings.chooseServer('action:local-play')
    assert.equal(fixture.bindings.localPlayOpen.value, true)
    assert.equal(fixture.state.selectedServerId, 'server')
  } finally { fixture.dispose() }
})

for (const blockedPhase of ['scheme', 'update', 'launch']) {
  test(`startup deadline releases the launch UI and cannot start late after ${blockedPhase}`, async () => {
    let expire, release
    const blocked = new Promise(resolve => { release = resolve })
    const fixture = setup(false, false, undefined, async method => {
      if (method === 'instances.update' && blockedPhase === 'update') return await blocked
      if (method === 'instances.launch' && blockedPhase === 'launch') return await blocked
      return []
    }, false, { setTimeout: callback => { expire = callback; return 1 }, clearTimeout() {} })
    let actualStarts = 0
    try {
      fixture.dependencies.ensurePluginScheme = () => blockedPhase === 'scheme' ? blocked : Promise.resolve()
      fixture.dependencies.perform = async action => {
        fixture.state.busy = true
        try { await action(); return true }
        catch (error) { fixture.state.error = error.message; return false }
        finally { fixture.state.busy = false }
      }
      fixture.state.instancesLoaded = true
      fixture.state.clientLanguage = 'ko'
      const starting = fixture.bindings.launch()
      await flush()
      assert.equal(fixture.bindings.launchPreparing.value, true)
      // The start clock runs only while the host starts the client, never while the launch is still being prepared.
      if (blockedPhase === 'scheme') { assert.equal(expire, undefined); return }
      expire()
      await starting
      assert.equal(fixture.bindings.launchPreparing.value, false)
      assert.equal(!!fixture.state.busy, false)
      assert.equal(fixture.state.error, 'launch.timeout')
      actualStarts = fixture.calls.filter(method => method === 'instances.launch').length
      release({ instanceId: 'one', processId: 1 })
      await flush()
      assert.equal(fixture.calls.filter(method => method === 'instances.launch').length, actualStarts)
      assert.ok(fixture.calls.includes('instances.cancelLaunch'))
      assert.equal(fixture.state.error, 'launch.timeout')
    } finally { release([]); fixture.dispose() }
  })
}

for (const blockedPhase of ['scheme', 'update', 'launch']) {
  test(`clicking the launch button again cancels a pending ${blockedPhase} without a timeout error`, async () => {
    let release
    const blocked = new Promise(resolve => { release = resolve })
    const fixture = setup(false, false, undefined, async method => {
      if (method === 'instances.update' && blockedPhase === 'update') return await blocked
      if (method === 'instances.launch' && blockedPhase === 'launch') return await blocked
      return []
    })
    try {
      fixture.dependencies.ensurePluginScheme = () => blockedPhase === 'scheme' ? blocked : Promise.resolve()
      fixture.state.instancesLoaded = true
      fixture.state.clientLanguage = 'ko'
      const starting = fixture.bindings.launch()
      await flush()
      assert.equal(fixture.bindings.launchStarting.value, true)
      await fixture.bindings.requestLaunch()
      await starting
      assert.equal(fixture.bindings.launchPreparing.value, false)
      assert.notEqual(fixture.state.error, 'launch.timeout')
      const count = fixture.calls.filter(method => method === 'instances.launch').length
      release({ instanceId: 'one', processId: 2 })
      await flush()
      assert.equal(fixture.calls.filter(method => method === 'instances.launch').length, count)
      // Before the host starts anything there is nothing to cancel there; the launch is simply abandoned.
      assert.equal(fixture.calls.includes('instances.cancelLaunch'), blockedPhase !== 'scheme')
    } finally { release([]); fixture.dispose() }
  })
}

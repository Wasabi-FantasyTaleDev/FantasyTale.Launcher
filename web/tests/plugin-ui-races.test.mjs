import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as library from '../src/plugins/library.ts'
import * as configuration from '../src/plugins/configuration.ts'
import * as versions from '../src/store/versions.ts'

function pending() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

function setup(call, refreshPlugins = async () => {}) {
  const state = vue.reactive({ selectedInstanceId: 'one', selectedServerId: 'server', error: '', busy: false, plugins: [] })
  const instances = [{ id: 'one', game: 'fixture', plugins: [] }, { id: 'two', game: 'fixture', plugins: [] }]
  state.instances = instances
  const dependencies = {
    state, selectedInstance: vue.computed(() => state.instances.find(i => i.id === state.selectedInstanceId)),
    gameInstances: vue.ref(instances), supportsPlugins: vue.ref(true), isRunning: () => false,
    refreshPlugins, perform: async action => { await action(); return true },
    selectedPluginScheme: vue.ref('default'), pluginSchemesBusy: vue.reactive({ value: false }), capturePluginScheme() {},
  }
  const downloads = vue.reactive({ tasks: [] }), unmount = [], scope = vue.effectScope()
  const source = readFileSync(new URL('../src/pages/PluginsPage.vue', import.meta.url), 'utf8')
  const script = compileScript(parse(source).descriptor, { id: 'PluginsPage' }).content
  const javascript = ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === 'vue-router') return { useRoute: () => ({ query: {} }) }
    if (id === '../state') return dependencies
    if (id === '../bridge') return { inHost: true, call }
    if (id === '../i18n') return { localized: value => value }
    if (id === '../plugins/notices') return { showPluginNotice() {} }
    if (id === '../plugins/library') return library
    if (id === '../plugins/configuration') return configuration
    if (id === '../plugins/updatePreview') return { pluginUpdatePreviewAvailable: false, createPluginUpdatePreview: () => ({ stop() {} }) }
    if (id === '../store/versions') return versions
    if (id === '../downloads/live') return { downloads, trackDownload: task => downloads.tasks.push(task) }
    if (id === '../downloads/model') return { isTerminal: task => task.stage === 'completed' }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, setTimeout: () => 1, clearTimeout() {} })
  const bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  return { state, instances, downloads, bindings, dispose() { unmount.forEach(callback => callback()); scope.stop() } }
}

const plugin = (id = 'fixture.plugin', custom = false) => ({
  id, game: 'fixture', version: '1.0.0', options: [], configurationPage: custom ? 'settings.html' : null, selection: null,
})

test('manual update checks show closable InfoBar notices without changing the check button label', async () => {
  const fixture = setup(async method => method === 'plugins.checkUpdates' ? [] : undefined)
  try {
    assert.equal(fixture.bindings.notices.value.length, 0)
    await fixture.bindings.checkUpdates()
    const notice = fixture.bindings.notices.value.find(item => item.key === 'check')
    assert.equal(notice.title, 'pluginUpdates.checked')
    assert.equal(notice.message, 'pluginUpdates.current')
    fixture.bindings.dismissedNotices.add(notice.key)
    assert.equal(fixture.bindings.notices.value.length, 0)
    await fixture.bindings.checkUpdates()
    assert.equal(fixture.bindings.notices.value.length, 1)
  } finally { fixture.dispose() }
})

test('pending updates and discovery errors use deduplicated InfoBar notices and ignore retired-source status', () => {
  const fixture = setup(async () => undefined)
  try {
    fixture.bindings.pendingUpgrades.value = 4
    fixture.bindings.discoveryError.value = 'integrity'
    fixture.bindings.updates.value = [{ status: 'integrity' }, { status: 'source' }]
    assert.deepEqual(Array.from(fixture.bindings.notices.value, item => item.key), ['pending:4', 'error:integrity'])
    fixture.bindings.dismissedNotices.add('pending:4')
    assert.equal(fixture.bindings.notices.value.length, 1)
  } finally { fixture.dispose() }
})

test('only running games show exit deferral and activation failures replace pending progress', () => {
  const fixture = setup(async () => undefined)
  try {
    fixture.bindings.pendingUpgrades.value = 1
    assert.equal(fixture.bindings.notices.value[0].message, 'pluginUpdates.pendingActivation')
    fixture.bindings.waitingForExit.value = 1
    assert.equal(fixture.bindings.notices.value[0].message, 'pluginUpdates.waitingForExit')
    fixture.bindings.activationFailure.value = 'integrity'
    assert.deepEqual(Array.from(fixture.bindings.notices.value, item => item.key), ['activation-failed'])
    fixture.state.selectedInstanceId = 'two'
    assert.equal(fixture.bindings.notices.value.length, 0)
  } finally { fixture.dispose() }
})

test('an update shows pending activity immediately and admits only one transfer', async () => {
  const admission = pending(), calls = [], fixture = setup(async (method, params) => {
    calls.push({ method, params })
    if (method === 'plugins.installUpdate') return admission.promise
  })
  try {
    const item = plugin()
    fixture.bindings.updates.value = [{ id: item.id, installedVersion: item.version, status: 'available', version: '2.0.0' }]
    const action = fixture.bindings.applyUpdate(item)
    assert.equal(fixture.bindings.updateTaskActive(item), true)
    await fixture.bindings.applyUpdate(item)
    assert.equal(calls.filter(call => call.method === 'plugins.installUpdate').length, 1)
    admission.resolve({ id: 'update', stage: 'checking', target: {} })
    await action
    assert.equal(fixture.bindings.startingUpdates.size, 0)
    assert.equal(fixture.bindings.updateTaskActive(item), true)
  } finally { fixture.dispose() }
})

test('applying an already downloaded update preserves a disabled selection and options', async () => {
  const calls = [], fixture = setup(async (method, params) => { calls.push({ method, params }) })
  try {
    const item = plugin(), next = { ...item, version: '2.0.0' }
    fixture.state.plugins = [item, next]
    fixture.instances[0].plugins = [{ id: item.id, version: item.version, enabled: false, options: { color: 'blue' } }]
    fixture.bindings.updates.value = [{ id: item.id, installedVersion: item.version, status: 'installed', version: next.version }]
    await fixture.bindings.applyUpdate(item)
    const change = calls.find(call => call.method === 'plugins.configure').params
    assert.equal(change.version, next.version)
    assert.equal(change.enabled, false)
    assert.equal(change.options.color, 'blue')
    assert.equal(change.selectVersion, true)
    assert.equal(fixture.bindings.startingUpdates.size, 0)
  } finally { fixture.dispose() }
})

test('choosing another release transfers the enabled selection and inherits its options', async () => {
  const calls = [], fixture = setup(async (method, params) => { calls.push({ method, params }) })
  try {
    const selected = { id: 'fixture.plugin', version: '1.0.0', enabled: true, options: { color: 'yellow' } }
    const old = { ...plugin(), available: true, selection: selected }
    const next = { ...plugin(), version: '2.0.0', available: true }
    fixture.state.plugins = [old, next]
    fixture.instances[0].plugins = [selected]
    await fixture.bindings.selectVersion(old, next.version)
    const call = calls.find(c => c.method === 'plugins.configure')
    assert.equal(call.params.version, '2.0.0')
    assert.equal(call.params.enabled, true)
    assert.equal(call.params.options, undefined)
    assert.equal(call.params.selectVersion, true)
    assert.equal(call.params.expectedVersion, '1.0.0')
    assert.equal(fixture.bindings.switching.value, undefined)
  } finally { fixture.dispose() }
})

test('rejected packages without an original source can check enabled stores and install a pinned update', async () => {
  const calls = [], issue = { folder: 'library/rejected', id: 'fixture.plugin', version: '1.0.0', sourceId: 'source' }
  const fixture = setup(async (method, params) => {
    calls.push({ method, params })
    if (method === 'plugins.checkUpdates') return [{ id: issue.id, installedVersion: issue.version, status: 'available', version: '2.0.0', manifestSha256: 'hash' }]
    if (method === 'plugins.installUpdate') return { id: 'transfer', stage: 'queued', target: {} }
  })
  try {
    await fixture.bindings.updateIssue({ ...issue, sourceId: null })
    assert.equal(calls.length, 1)
    assert.equal(calls[0].method, 'plugins.checkUpdates')
    assert.equal(fixture.bindings.issueUpdate(issue).status, 'available')
    await fixture.bindings.updateIssue(issue)
    assert.equal(calls[1].method, 'plugins.installUpdate')
    assert.equal(calls[1].params.version, '2.0.0')
    assert.equal(calls[1].params.manifestSha256, 'hash')
    assert.equal(fixture.downloads.tasks[0].id, 'transfer')
  } finally { fixture.dispose() }
})

test('rejected package uninstall waits for confirmation and removes only the confirmed issue', async () => {
  const calls = [], fixture = setup(async (method, params) => { calls.push({ method, params }) })
  const issue = { folder: 'library/rejected', id: 'fixture.plugin', version: '1.0.0' }
  try {
    fixture.state.selectedGameId = 'fixture'
    fixture.bindings.loadIssues.value = [issue, { ...issue, id: 'fixture.other', folder: 'library/other' }]
    fixture.bindings.uninstallingIssue.value = issue
    assert.equal(calls.length, 0)
    fixture.bindings.uninstallingIssue.value = undefined
    await fixture.bindings.uninstallIssue()
    assert.equal(calls.length, 0)
    fixture.bindings.uninstallingIssue.value = issue
    await fixture.bindings.uninstallIssue()
    assert.equal(calls[0].method, 'plugins.uninstall')
    assert.equal(calls[0].params.game, 'fixture')
    assert.equal(calls[0].params.id, issue.id)
    assert.equal(fixture.bindings.loadIssues.value.length, 1)
    assert.equal(fixture.bindings.loadIssues.value[0].folder, 'library/other')
    assert.equal(fixture.bindings.uninstallingIssue.value, undefined)
  } finally { fixture.dispose() }
})

test('rejected library updates can target another installed client when the selected build is incompatible', async () => {
  const calls = [], issue = { folder: 'library/rejected', game: 'fixture', id: 'fixture.plugin', version: '1.0.0', sourceId: 'source' }
  const fixture = setup(async (method, params) => {
    calls.push({ method, params })
    if (method === 'plugins.checkUpdates') return [{ id: issue.id, installedVersion: issue.version, status: params.instanceId === 'two' ? 'available' : 'current', version: '2.0.0', manifestSha256: 'hash' }]
    if (method === 'plugins.installUpdate') return { id: 'transfer', stage: 'queued', target: {} }
  })
  try {
    await fixture.bindings.updateIssue(issue)
    assert.equal(calls.length, 2)
    assert.equal(fixture.bindings.issueUpdate(issue).status, 'available')
    await fixture.bindings.updateIssue(issue)
    assert.equal(calls[2].method, 'plugins.installUpdate')
    assert.equal(calls[2].params.instanceId, 'two')
  } finally { fixture.dispose() }
})
test('choosing another release of a disabled plugin keeps it disabled and saving accepts that selection', async () => {
  const calls = [], fixture = setup(async (method, params) => { calls.push({ method, params }) })
  try {
    const selected = { id: 'fixture.plugin', version: '1.0.0', enabled: false, options: {} }
    const next = { ...plugin(), version: '2.0.0', available: true }
    fixture.instances[0].plugins = [selected]
    fixture.state.plugins = [next]
    await fixture.bindings.selectVersion(next, next.version)
    assert.equal(calls[0].params.enabled, false)
    await fixture.bindings.edit(next)
    await fixture.bindings.save()
    assert.equal(calls[1].params.selectVersion, true)
    assert.equal(calls[1].params.expectedVersion, '1.0.0')
    assert.equal(calls[1].params.enabled, false)
  } finally { fixture.dispose() }
})

test('saving plugin settings retains installed client inventory without inspecting client files', async () => {
  const calls = [], selected = { id: 'fixture.plugin', version: '1.0.0', enabled: true, options: {} }
  const fixture = setup(async (method, params) => {
    calls.push({ method, params })
    assert.equal(method, 'plugins.configure')
    return { id: 'one', game: 'fixture', plugins: [{ ...selected, options: params.options }] }
  })
  try {
    Object.assign(fixture.state.instances[0], {
      clientInstalled: true, installationPath: 'D:/Games/fixture', externalInstallation: true,
      diagnosticsEnabled: true, plugins: [selected],
    })
    const item = { ...plugin(), options: [{ key: 'model', type: 'string', default: 'classic' }] }
    fixture.state.plugins = [item]
    await fixture.bindings.edit(item)
    fixture.bindings.values.model = 'modern'
    await fixture.bindings.save()
    assert.equal(fixture.state.error, '')
    assert.equal(fixture.state.instances[0].clientInstalled, true)
    assert.equal(fixture.state.instances[0].installationPath, 'D:/Games/fixture')
    assert.equal(fixture.state.instances[0].externalInstallation, true)
    assert.equal(fixture.state.instances[0].diagnosticsEnabled, true)
    assert.equal(fixture.state.instances[0].plugins[0].options.model, 'modern')
    assert.equal(fixture.state.plugins[0].selection.options.model, 'modern')
    assert.equal(calls.length, 1)
    assert.equal(calls[0].params.deferRefresh, true)
    assert.equal(fixture.bindings.editing.value, undefined)
  } finally { fixture.dispose() }
})

test('an old update check cannot finish a newer client check', async () => {
  const first = pending(), second = pending()
  const fixture = setup((_method, params) => params.instanceId === 'one' ? first.promise : second.promise)
  try {
    const oldCheck = fixture.bindings.checkUpdates()
    fixture.state.selectedInstanceId = 'two'
    const newCheck = fixture.bindings.checkUpdates()
    first.resolve([{ id: 'old' }]); await oldCheck
    assert.equal(fixture.bindings.checking.value, true)
    assert.equal(fixture.bindings.updates.value.length, 0)
    second.resolve([{ id: 'new' }]); await newCheck
    assert.equal(fixture.bindings.checking.value, false)
    assert.equal(fixture.bindings.updates.value[0].id, 'new')
  } finally { fixture.dispose() }
})

test('switching away and back suppresses old update errors', async () => {
  const response = pending(), fixture = setup(() => response.promise)
  try {
    const checking = fixture.bindings.checkUpdates()
    fixture.state.selectedServerId = 'other'
    fixture.state.selectedServerId = 'server'
    response.reject(new Error('Old source unavailable')); await checking
    assert.equal(fixture.state.error, '')
    assert.equal(fixture.bindings.checked.value, false)
    assert.equal(fixture.bindings.checking.value, false)
  } finally { fixture.dispose() }
})

test('a closed custom editor ignores its late error', async () => {
  const response = pending(), fixture = setup(() => response.promise)
  try {
    const editing = fixture.bindings.edit(plugin('custom', true))
    fixture.bindings.closeEditor()
    response.reject(new Error('Old page missing')); await editing
    assert.equal(fixture.state.error, '')
    assert.equal(fixture.bindings.editing.value, undefined)
    assert.equal(Object.keys(fixture.bindings.configurationHtml).length, 0)
    assert.equal(Object.keys(fixture.bindings.configurationErrors).length, 0)
  } finally { fixture.dispose() }
})

test('a custom page cannot populate a new client editor', async () => {
  const response = pending(), fixture = setup(() => response.promise)
  try {
    const oldEditor = fixture.bindings.edit(plugin('custom', true))
    fixture.state.selectedInstanceId = 'two'
    await fixture.bindings.edit(plugin('current'))
    response.resolve('<h1>Old client</h1>'); await oldEditor
    assert.equal(fixture.bindings.editing.value.id, 'current')
    assert.equal(Object.keys(fixture.bindings.configurationHtml).length, 0)
  } finally { fixture.dispose() }
})

test('switching clients suppresses a custom page error without opening another editor', async () => {
  const response = pending(), fixture = setup(() => response.promise)
  try {
    const editing = fixture.bindings.edit(plugin('custom', true))
    fixture.state.selectedInstanceId = 'two'
    response.reject(new Error('Old page missing')); await editing
    assert.equal(fixture.state.error, '')
    assert.equal(fixture.bindings.editing.value, undefined)
  } finally { fixture.dispose() }
})

test('an old save cannot close a newly opened editor', async () => {
  const response = pending(), fixture = setup(() => response.promise)
  try {
    await fixture.bindings.edit(plugin('old'))
    const saving = fixture.bindings.save()
    fixture.bindings.closeEditor()
    await fixture.bindings.edit(plugin('current'))
    response.resolve(); await saving
    assert.equal(fixture.bindings.editing.value.id, 'current')
  } finally { fixture.dispose() }
})

test('an old activation cannot close a newer version confirmation', async () => {
  const response = pending(), fixture = setup(() => response.promise)
  try {
    fixture.bindings.switching.value = { plugin: plugin('old'), from: '0.9.0', instanceId: 'one' }
    const switching = fixture.bindings.switchVersion()
    fixture.bindings.switching.value = { plugin: plugin('current'), from: '0.9.0', instanceId: 'one' }
    response.resolve(); await switching
    assert.equal(fixture.bindings.switching.value.plugin.id, 'current')
  } finally { fixture.dispose() }
})

test('a late update download stays global without attaching to another client card', async () => {
  const response = pending(), fixture = setup(() => response.promise)
  try {
    fixture.bindings.updates.value = [{ id: 'fixture.plugin', installedVersion: '1.0.0', status: 'available', version: '2.0.0', manifestSha256: 'digest' }]
    const downloading = fixture.bindings.downloadUpdate(plugin())
    fixture.state.selectedInstanceId = 'two'
    response.resolve({ id: 'download', target: { instanceId: 'one' }, stage: 'downloading' }); await downloading
    assert.equal(fixture.downloads.tasks.length, 1)
    assert.equal(fixture.bindings.updateTask(plugin()), undefined)
  } finally { fixture.dispose() }
})

test('unmounted plugin pages ignore delayed discovery failures', async () => {
  const response = pending(), fixture = setup(() => response.promise)
  const checking = fixture.bindings.checkUpdates()
  fixture.dispose()
  response.reject(new Error('Old discovery')); await checking
  assert.equal(fixture.state.error, '')
})

for (const change of ['client', 'server', 'unmount']) {
  test(`a completed download cannot check another context after ${change}`, async () => {
    const refresh = pending(), calls = []
    const fixture = setup((method, params) => { calls.push({ method, params }); return [] }, () => refresh.promise)
    fixture.bindings.updateTasks['fixture.plugin:1.0.0'] = 'download'
    fixture.downloads.tasks.push({ id: 'download', stage: 'completed' })
    await vue.nextTick()
    if (change === 'client') fixture.state.selectedInstanceId = 'two'
    else if (change === 'server') fixture.state.selectedServerId = 'other'
    else fixture.dispose()
    refresh.resolve()
    await new Promise(resolve => setImmediate(resolve))
    assert.deepEqual(calls, [])
    if (change !== 'unmount') fixture.dispose()
  })
}

test('a stale completion refresh failure cannot appear on another client', async () => {
  const refresh = pending()
  let refreshCalls = 0
  const fixture = setup(() => [], () => ++refreshCalls === 1 ? refresh.promise : Promise.resolve())
  try {
    fixture.bindings.updateTasks['fixture.plugin:1.0.0'] = 'download'
    fixture.downloads.tasks.push({ id: 'download', stage: 'completed' })
    await vue.nextTick()
    fixture.state.selectedInstanceId = 'two'
    refresh.reject(new Error('Old library refresh'))
    await new Promise(resolve => setImmediate(resolve))
    assert.equal(fixture.state.error, '')
  } finally { fixture.dispose() }
})

test('a completed download checks updates while its original context remains current', async () => {
  const refresh = pending(), calls = [], fixture = setup((method, params) => { calls.push({ method, params }); return [] }, () => refresh.promise)
  try {
    fixture.bindings.updateTasks['fixture.plugin:1.0.0'] = 'download'
    fixture.downloads.tasks.push({ id: 'download', stage: 'completed' })
    await vue.nextTick()
    refresh.resolve()
    await new Promise(resolve => setImmediate(resolve))
    assert.equal(calls.length, 1)
    assert.equal(calls[0].method, 'plugins.checkUpdates')
    assert.equal(calls[0].params.instanceId, 'one')
    assert.equal(calls[0].params.serverId, 'server')
    assert.equal(fixture.bindings.checked.value, true)
  } finally { fixture.dispose() }
})


test('normal uninstall confirms a plugin identity without offering a single-version deletion', async () => {
  const calls = [], fixture = setup(async (method, params) => { calls.push({ method, params }) })
  try {
    fixture.bindings.uninstalling.value = plugin()
    assert.equal(calls.length, 0)
    fixture.bindings.uninstalling.value = undefined
    await fixture.bindings.uninstallPlugin()
    assert.equal(calls.length, 0)
    fixture.bindings.uninstalling.value = plugin()
    await fixture.bindings.uninstallPlugin()
    assert.equal(calls[0].method, 'plugins.uninstall')
    assert.equal(calls[0].params.id, 'fixture.plugin')
    assert.equal(calls[0].params.version, undefined)
    assert.equal(fixture.bindings.uninstalling.value, undefined)
  } finally { fixture.dispose() }
})

test('dismissing a pending version switch does not close a newer confirmation', async () => {
  const response = pending(), fixture = setup(() => response.promise)
  try {
    const first = { plugin: plugin(), from: '0.9.0', instanceId: 'one' }
    fixture.bindings.switching.value = first
    const operation = fixture.bindings.switchVersion()
    fixture.bindings.switching.value = undefined
    const second = { plugin: plugin('fixture.other'), from: '0.8.0', instanceId: 'one' }
    fixture.bindings.switching.value = second
    response.resolve()
    await operation
    assert.equal(fixture.bindings.switching.value.plugin.id, 'fixture.other')
  } finally { fixture.dispose() }
})


test('an activated update replaces a previously browsed release instead of displaying its unchecked toggle', async () => {
  const fixture = setup(async () => undefined)
  try {
    const old = { ...plugin(), selection: { enabled: true } }
    fixture.state.plugins = [old]; await vue.nextTick()
    fixture.bindings.libraryVersions['fixture:fixture.plugin'] = '1.0.0'
    fixture.state.plugins = [{ ...old, selection: null }, { ...old, version: '2.0.0', selection: { enabled: true } }]
    await vue.nextTick()
    assert.equal(fixture.bindings.libraryVersions['fixture:fixture.plugin'], undefined)
    assert.equal(fixture.bindings.plugins.value[0].version, '2.0.0')
    assert.equal(fixture.bindings.plugins.value[0].selection.enabled, true)
  } finally { fixture.dispose() }
})

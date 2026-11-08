import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { updates, groups } from './store-update-helpers.mjs'
import { createStoreSession } from '../src/store/session.ts'

const source = id => ({ id, name: id, enabled: true, kind: 'http', baseUrl: `https://${id}.example/`, catalogPath: 'source.toml' })
const plugin = (id = 'shared', version = '2.0.0', game = 'fixture') => ({ id, kind: 'plugin', game, builds: ['one'], version, manifestSha256: `${id}-${version}`, name: { en: id } })
const local = (version = '1.0.0', game = 'fixture') => ({ id: 'shared', game, version })
const group = (item, offers = [{ source: source('first'), releases: [item] }]) => ({ key: item.id, source: offers[0].source, releases: offers[0].releases, offers })
const client = (id, version = '1.0.0', enabled = true, game = 'fixture', build = 'one') => ({ id, game, build, plugins: [{ id: 'shared', version, enabled }] })
test('renamed offers discover every former identity and retain disabled client update targets', () => {
  const item = { ...plugin('successor'), pluginDefinition: { id: 'successor', aliases: ['shared', 'ancient'] } }
  assert.equal(updates.pluginUpdateFrom(item, [local()]), '1.0.0')
  assert.equal(updates.pluginUpdateFrom(item, [{ ...local(), id: 'ancient' }]), '1.0.0')
  assert.equal(updates.pluginUpdateFrom(item, [local('3.1.0')]), '3.1.0')
  assert.equal(updates.pluginUpdateFrom(item, [{ ...local('3.0.0'), id: 'successor' }, local()]), undefined)
  const current = { ...client('current', '2.0.0'), plugins: [{ id: 'successor', version: '2.0.0', enabled: true }] }
  assert.deepEqual(Array.from(updates.pluginUpgradeTargets(item, [client('disabled', '3.1.0', false), current])), ['disabled'])
})

test('only a newer local plugin offer is an update; games, metadata and downloaded newer releases stay separate', () => {
  const item = plugin()
  assert.equal(updates.pluginUpdateFrom(item, []), undefined)
  assert.equal(updates.pluginUpdateFrom(item, [local('1.0.0', 'other')]), undefined)
  assert.equal(updates.pluginUpdateFrom(item, [local()]), '1.0.0')
  for (const version of ['2.0.0', '2.0.0+other', '3.0.0', 'unversioned'])
    assert.equal(updates.pluginUpdateFrom(item, [local(version)]), undefined)
  assert.equal(updates.pluginUpdateFrom(item, [local(), local('3.0.0')]), undefined)
  assert.equal(updates.pluginUpdateFrom(plugin('shared', '2.0.0-rc.1'), [local()]), undefined)
  assert.equal(updates.pluginUpdateFrom(plugin('shared', '2.0.0-rc.1'), [local('2.0.0-beta.1')]), '2.0.0-beta.1')
  assert.equal(updates.pluginUpdateFrom(plugin('shared', '2.0.0-rc.1'), [local('1.0.0+build-tag')]), undefined)
})

test('update plans select the first eligible source, preserving alternative hashes and compatible releases', () => {
  const offers = [{ source: source('first'), releases: [plugin('shared', '1.0.0')] },
    { source: source('second'), releases: [plugin('shared', '2.0.0')] },
    { source: source('third'), releases: [plugin('shared', '9.0.0')] }]
  const plans = updates.storePluginUpdates([group(plugin(), offers)], [local()], () => true)
  assert.equal(plans.length, 1)
  assert.equal(plans[0].source.id, 'second')
  assert.equal(plans[0].item.version, '2.0.0')
  assert.equal(plans[0].offers[0].source.id, 'first')
  assert.equal(plans[0].offers[2].source.id, 'third')
  assert.equal(offers[0].releases[0].version, '1.0.0')
  offers[1].releases[0].launcherCompatible = false
  assert.equal(updates.storePluginUpdates([group(plugin(), offers)], [local()], () => true)[0].source.id, 'third')
  assert.equal(updates.storePluginUpdates([group(plugin(), offers)], [local()], () => false).length, 0)
})

test('update targets include disabled selections but exclude unrelated, unsupported and already newer clients', () => {
  const clients = [client('enabled'), client('disabled', '1.0.0', false), client('other', '1.0.0', true, 'other'),
    client('unsupported', '1.0.0', true, 'fixture', 'two'), client('newer', '3.0.0'), { id: 'empty', game: 'fixture', build: 'one', plugins: [] }]
  assert.deepEqual(Array.from(updates.pluginUpgradeTargets(plugin(), clients)), ['enabled', 'disabled'])
})

test('explicit installation targets every compatible client including absent and disabled selections', () => {
  const clients = [client('enabled'), client('disabled', '2.0.0', false),
    { id: 'empty', game: 'fixture', build: 'one', plugins: [] },
    client('other', '1.0.0', true, 'other'), client('unsupported', '1.0.0', true, 'fixture', 'two')]
  assert.deepEqual(Array.from(updates.pluginInstallationTargets(plugin(), clients)), ['enabled', 'disabled', 'empty'])
})

test('sources missing the exact target version stay gray at the bottom in configured order', () => {
  const offers = [{ source: source('old'), releases: [plugin('shared', '1.0.0')] },
    { source: source('exact'), releases: [plugin()] },
    { source: source('different'), releases: [plugin('shared', '3.0.0')] },
    { source: source('mirror'), releases: [plugin()] }]
  const choices = updates.pluginSourceChoices(offers, '2.0.0', true)
  assert.deepEqual(Array.from(choices, option => option.value), ['exact', 'mirror', 'old', 'different'])
  assert.deepEqual(Array.from(choices, option => option.disabled), [false, false, true, true])
  assert.ok(updates.pluginSourceChoices(offers, '2.0.0').every(option => !option.disabled))
})

function setupBatch(call = async (_method, params) => ({ id: params.packageId })) {
  const item = plugin(), other = plugin('another')
  const props = vue.reactive({ open: true, updates: [
    ...updates.storePluginUpdates([group(item, [{ source: source('first'), releases: [item] }, { source: source('second'), releases: [plugin('shared', '3.0.0'), { ...plugin(), manifestSha256: 'second-2.0.0' }] }])], [local()], () => true),
    ...updates.storePluginUpdates([group(other)], [{ ...local(), id: 'another' }], () => true),
  ] })
  const state = vue.reactive({ selectedGameId: 'fixture', selectedServerId: 'server', instances: [client('enabled'), client('disabled', '1.0.0', false)] })
  const tasks = vue.ref([])
  const calls = [], navigations = [], tracked = [], emissions = [], scope = vue.effectScope()
  const require = id => {
    if (id === 'vue') return vue
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === 'vue-router') return { useRouter: () => ({ push: async target => navigations.push(target) }) }
    if (id === '../bridge') return { inHost: true, call: async (method, params) => { calls.push({ method, params }); return call(method, params) } }
    if (id === '../state') return { state }
    if (id === '../store/updates') return updates
    if (id === '../downloads/live') return { downloadTasks: tasks, trackDownload: task => { tracked.push(task.id); tasks.value.push({ stage: 'downloading', ...task }) }, downloadAction: async task => { tasks.value = tasks.value.map(item => item.id === task.id ? { ...item, stage: 'queued' } : item) } }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  const script = compileScript(parse(readFileSync(new URL('../src/components/StorePluginUpdatesDialog.vue', import.meta.url), 'utf8')).descriptor, { id: 'StorePluginUpdatesDialog' }).content
  const module = { exports: {} }
  runInNewContext(ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
    { module, exports: module.exports, require })
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: event => emissions.push(event) }))
  return { props, state, calls, navigations, tracked, bindings, emissions, tasks, dispose() { scope.stop() } }
}

test('batch confirmation pins each source and installs for all compatible clients', async () => {
  const fixture = setupBatch()
  try {
    fixture.bindings.chooseSource(0, 'second')
    fixture.props.updates[0].offers[1].source.baseUrl = 'https://changed.example/'
    fixture.props.updates[0].offers[1].releases[1].manifestSha256 = 'changed'
    await fixture.bindings.install()
    assert.equal(fixture.calls.length, 2)
    assert.equal(fixture.calls[0].method, 'store.installPluginTargets')
    assert.equal(fixture.calls[0].params.expectedSource.baseUrl, 'https://second.example/')
    assert.equal(fixture.calls[0].params.version, '2.0.0')
    assert.equal(fixture.calls[0].params.manifestSha256, 'second-2.0.0')
    assert.deepEqual(Array.from(fixture.calls[0].params.instanceIds), ['enabled', 'disabled'])
    assert.equal(fixture.calls[1].method, 'store.installPluginTargets')
    assert.deepEqual(Array.from(fixture.calls[1].params.instanceIds), ['enabled', 'disabled'])
    assert.equal(fixture.navigations.length, 0)
    assert.equal(fixture.emissions.length, 0)
    assert.deepEqual(fixture.tracked, ['shared', 'another'])
  } finally { fixture.dispose() }
})

test('batch download-only mode never activates clients, and changed selections are excluded', async () => {
  for (const downloadOnly of [true, false]) {
    const fixture = setupBatch()
    try {
      fixture.bindings.activateAfterInstall.value = !downloadOnly
      if (!downloadOnly) for (const client of fixture.state.instances) client.plugins = []
      await fixture.bindings.install()
      assert.equal(fixture.calls[0].method, 'store.downloadPlugin')
      assert.equal(fixture.calls[1].method, downloadOnly ? 'store.downloadPlugin' : 'store.installPluginTargets')
    } finally { fixture.dispose() }
  }
})

test('batch partial failures are visible, continue independently and retry only unqueued cards', async () => {
  let fail = true
  const fixture = setupBatch(async (_method, params) => {
    if (params.packageId === 'shared' && fail) throw new Error('Source unavailable')
    return { id: params.packageId }
  })
  try {
    await fixture.bindings.install()
    assert.equal(fixture.bindings.plans.value[0].error, 'Source unavailable')
    assert.equal(fixture.bindings.queued.value, 1)
    assert.equal(fixture.navigations.length, 0)
    fail = false; await fixture.bindings.install()
    assert.equal(fixture.calls.length, 3)
    assert.equal(fixture.calls[2].params.packageId, 'shared')
    assert.equal(fixture.bindings.queued.value, 2)
    assert.equal(fixture.emissions.length, 0)
    fixture.tasks.value = fixture.tasks.value.map(task => ({ ...task, stage: 'completed' }))
    await fixture.bindings.install()
    assert.equal(fixture.emissions.length, 1)
  } finally { fixture.dispose() }
})

test('pending batch admissions cannot be duplicated or retargeted', async () => {
  let finish
  const fixture = setupBatch((_method, params) => params.packageId === 'shared' ? new Promise(resolve => { finish = resolve }) : { id: params.packageId })
  try {
    const pending = fixture.bindings.install()
    fixture.bindings.chooseSource(0, 'second')
    await fixture.bindings.install()
    assert.equal(fixture.calls.length, 1)
    assert.equal(fixture.bindings.plans.value[0].sourceId, 'first')
    finish({ id: 'shared' }); await pending
    assert.equal(fixture.calls.length, 2)
  } finally { fixture.dispose() }
})

test('batch source switching cannot silently choose a different update version', async () => {
  const fixture = setupBatch()
  try {
    fixture.props.open = false; await vue.nextTick()
    fixture.props.updates[0].offers[1].releases = [plugin('shared', '3.0.0')]
    fixture.props.updates[0].offers.unshift({ source: source('old'), releases: [plugin('shared', '1.0.0')] })
    fixture.props.open = true; await vue.nextTick()
    const choices = fixture.bindings.sourceOptions.value[0]
    assert.deepEqual(Array.from(choices, option => option.value), ['first', 'old', 'second'])
    assert.equal(choices[1].disabled, true)
    assert.equal(choices[2].disabled, true)
    assert.equal(choices[1].label, 'old · 1.0.0')
    assert.equal(choices[2].label, 'second · 3.0.0')
    fixture.bindings.chooseSource(0, 'second')
    fixture.bindings.chooseSource(0, 'old')
    assert.equal(fixture.bindings.plans.value[0].sourceId, 'first')
    await fixture.bindings.install()
    assert.equal(fixture.calls[0].params.version, '2.0.0')
    assert.equal(fixture.calls[0].params.sourceId, 'first')
  } finally { fixture.dispose() }
})

async function setupPage() {
  const route = vue.reactive({ query: { kind: 'plugin' } })
  const inventory = [local(), { ...local('2.0.0'), id: 'current' }]
  const packages = [plugin('new'), plugin('current'), plugin()]
  const state = vue.reactive({ games: [{ id: 'fixture', status: 'enabled', supportsPlugins: true, builds: [{ id: 'one' }] }] })
  const downloads = vue.reactive({ tasks: [] }), requests = [], scope = vue.effectScope(), unmount = []
  let inventoryFailure = false
  const require = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key, locale: vue.ref('en') }) }
    if (id === 'vue-router') return { useRoute: () => route }
    if (id === '../bridge') return { inHost: true, call: async (method, params) => {
      requests.push({ method, params })
      if (method === 'plugins.list') { if (inventoryFailure) throw new Error('Library unavailable'); return inventory.map(item => ({ ...item })) }
      if (method === 'store.sources') return [source('first')]
      if (method === 'store.catalog') return { packages }
      throw new Error(`Unexpected method ${method}`)
    } }
    if (id === '../store/background') return {
      liveStoreSession: createStoreSession({
        sources: () => require('../bridge').call('store.sources'),
        catalog: (source, force) => require('../bridge').call('store.catalog', { id: source.id, force }),
        plugins: () => require('../bridge').call('plugins.list'), error: error => error.message,
      }),
      canInstallStorePlugin: item => state.games.some(game => game.id === item.game && game.status === 'enabled'
        && game.supportsPlugins && game.builds.some(build => !item.builds.length || item.builds.includes(build.id))),
    }
    if (id === '../state') return { state }
    if (id === '../store/updates') return updates
    if (id === '../store/groups') return groups
    if (id === '../store/preview') return {}
    if (id === '../store/session') return { storeSession: (_key, loader) => createStoreSession(loader) }
    if (id === '../downloads/live') return { downloads }
    if (id === '../downloads/model') return { formatBytes: value => String(value) }
    if (id === '../i18n') return { localized: value => typeof value === 'string' ? value : value?.en ?? '' }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  const script = compileScript(parse(readFileSync(new URL('../src/pages/StorePage.vue', import.meta.url), 'utf8')).descriptor,
    { id: 'StorePage' }).content.replaceAll('import.meta.env.DEV', 'false')
  const module = { exports: {} }
  runInNewContext(ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
    { module, exports: module.exports, require })
  const bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  await new Promise(resolve => setImmediate(resolve))
  return { bindings, inventory, requests, downloads, route, failInventory() { inventoryFailure = true },
    dispose() { unmount.forEach(callback => callback()); scope.stop() } }
}

test('All defaults to every plugin; Updates filters, selects and searches only real upgrades', async () => {
  const fixture = await setupPage()
  try {
    assert.equal(fixture.bindings.updateFilter.value, 'all')
    assert.equal(fixture.bindings.groups.value.length, 3)
    assert.equal(fixture.bindings.updates.value.length, 1)
    assert.equal(fixture.bindings.groups.value[0].releases[0].id, 'shared')
    assert.equal(fixture.bindings.isInstalled(plugin()), true)
    assert.equal(fixture.bindings.isInstalled(plugin('new')), false)
    assert.equal(fixture.requests.find(request => request.method === 'plugins.list').params, undefined)
    assert.equal(fixture.bindings.currentUpToDate.value, false)
    fixture.bindings.selected.value = fixture.bindings.groups.value.find(group => group.releases[0].id === 'current').key
    assert.equal(fixture.bindings.currentUpToDate.value, true)
    fixture.bindings.updateFilter.value = 'updates'; await vue.nextTick()
    assert.equal(fixture.bindings.groups.value.length, 1)
    assert.equal(fixture.bindings.current.value.id, 'shared')
    fixture.bindings.query.value = 'new'; await vue.nextTick()
    assert.equal(fixture.bindings.groups.value.length, 0)
    fixture.bindings.query.value = ''; fixture.bindings.updateFilter.value = 'all'; await vue.nextTick()
    assert.equal(fixture.bindings.groups.value.length, 3)
  } finally { fixture.dispose() }
})

test('completed plugin downloads refresh the local inventory and remove outdated update badges', async () => {
  const fixture = await setupPage()
  try {
    fixture.inventory.splice(0, 1, local('2.0.0'))
    fixture.downloads.tasks = [{ id: 'complete', target: { kind: 'plugin' }, stage: 'completed' }]
    await new Promise(resolve => setImmediate(resolve))
    assert.equal(fixture.bindings.updates.value.length, 0)
    fixture.failInventory(); await fixture.bindings.refreshInventory()
    assert.equal(fixture.bindings.inventoryError.value, 'Library unavailable')
    assert.equal(fixture.bindings.updates.value.length, 0)
  } finally { fixture.dispose() }
})


test('closing a pending batch stops further admissions and leaves a reopened confirmation intact', async () => {
  let complete
  const response = new Promise(resolve => { complete = resolve })
  const fixture = setupBatch(() => response)
  try {
    const operation = fixture.bindings.install()
    fixture.bindings.close()
    fixture.props.open = false; await vue.nextTick()
    fixture.props.open = true; await vue.nextTick()
    complete({ id: 'old-admission' }); await operation
    assert.equal(fixture.calls.length, 1)
    assert.deepEqual(fixture.tracked, ['old-admission'])
    assert.ok(fixture.bindings.plans.value.every(plan => !plan.taskId))
    assert.equal(fixture.bindings.busy.value, false)
  } finally { fixture.dispose() }
})

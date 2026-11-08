import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { updates } from './store-update-helpers.mjs'

const plugin = (hash, version = '1.0.0', builds = ['one']) => ({ id: 'shared', kind: 'plugin', game: 'fixture', builds, name: { en: 'Shared' }, version, manifestSha256: hash })
const source = id => ({ id, name: id, enabled: true, kind: 'http', baseUrl: `https://${id}.example/`, catalogPath: 'source.toml' })
function setup(offers, call = async () => ({ id: 'download' })) {
  const props = vue.reactive({ open: true, source: offers[0].source, item: offers[0].releases[0], offers })
  const state = vue.reactive({ plugins: [], selectedGameId: 'fixture', selectedServerId: 'server', instances: [
    { id: 'first-client', game: 'fixture', build: 'one', name: 'First', plugins: [] }, { id: 'second-client', game: 'fixture', build: 'two', name: 'Second', plugins: [] },
  ] })
  const tasks = vue.ref([])
  const scope = vue.effectScope(), calls = [], navigations = [], installations = []
  const dependencies = id => {
    if (id === 'vue') return vue
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === 'vue-router') return { useRouter: () => ({ push: async value => navigations.push(value) }) }
    if (id === '../bridge') return { inHost: true, call: async (method, params) => { calls.push({ method, params }); return await call(method, params) } }
    if (id === '../state') return { state, prepareStorePluginInstallation: (item, server) => task => installations.push({ item, server, task }) }
    if (id === '../store/updates') return updates
    if (id === '../i18n') return { localized: value => value.en }
    if (id === '../downloads/live') return { downloadTasks: tasks, trackDownload: task => tasks.value.push({ stage: 'downloading', ...task }), downloadAction: async () => {} }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  const script = compileScript(parse(readFileSync(new URL('../src/components/StorePluginDialog.vue', import.meta.url), 'utf8')).descriptor, { id: 'StorePluginDialog' }).content
  const module = { exports: {} }
  runInNewContext(ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
    { module, exports: module.exports, require: dependencies })
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit() {} }))
  return { props, state, bindings, calls, navigations, installations, dispose() { scope.stop() } }
}

test('source selection keeps its exact version and hash through targeted installation', async () => {
  const fixture = setup([{ source: source('first'), releases: [plugin('a')] }, { source: source('second'), releases: [plugin('b')] }])
  try {
    assert.equal(fixture.bindings.activateAfterInstall.value, true)
    assert.equal(fixture.bindings.primaryText.value, 'install.install')
    assert.equal(fixture.bindings.sourceId.value, 'first')
    fixture.bindings.chooseSource('second')
    await fixture.bindings.install()
    const request = fixture.calls[0]
    assert.equal(request.method, 'store.installPluginTargets')
    assert.equal(request.params.sourceId, 'second')
    assert.equal(request.params.expectedSource.baseUrl, 'https://second.example/')
    assert.equal(request.params.expectedSource.kind, 'http')
    assert.equal(request.params.expectedSource.catalogPath, 'source.toml')
    assert.equal(request.params.manifestSha256, 'b')
    assert.equal(request.params.version, '1.0.0')
    assert.equal(request.params.instanceIds[0], 'first-client')
  } finally { fixture.dispose() }
})

test('unchecking activation downloads only and reopening restores its checked default', async () => {
  const fixture = setup([{ source: source('first'), releases: [plugin('a')] }, { source: source('second'), releases: [plugin('b')] }])
  try {
    assert.equal(fixture.bindings.targets.value.length, 1)
    fixture.bindings.activateAfterInstall.value = false
    assert.equal(fixture.bindings.primaryText.value, 'store.download')
    fixture.bindings.chooseSource('second')
    assert.equal(fixture.bindings.activateAfterInstall.value, false)
    await fixture.bindings.install()
    assert.equal(fixture.calls[0].method, 'store.downloadPlugin')
    assert.equal(fixture.calls[0].params.sourceId, 'second')
    assert.equal(fixture.calls[0].params.manifestSha256, 'b')
    assert.equal(fixture.calls[0].params.instanceIds, undefined)
    assert.equal(fixture.installations.length, 0)
    assert.equal(fixture.bindings.targets.value[0], 'first-client')
    fixture.props.open = false; await vue.nextTick()
    fixture.props.open = true; await vue.nextTick()
    assert.equal(fixture.bindings.activateAfterInstall.value, true)
  } finally { fixture.dispose() }
})

test('checked activation remains an installation choice when no compatible client is registered', async () => {
  const fixture = setup([{ source: source('first'), releases: [plugin('a', '1.0.0', ['unregistered'])] }])
  try {
    await fixture.bindings.install()
    assert.equal(fixture.calls[0].method, 'store.installPluginTargets')
    assert.equal(fixture.calls[0].params.game, 'fixture')
    assert.deepEqual(Array.from(fixture.calls[0].params.instanceIds), [])
    assert.equal(fixture.installations.length, 1)
    assert.equal(fixture.installations[0].task, 'download')
    assert.equal(fixture.installations[0].item.builds[0], 'unregistered')
  } finally { fixture.dispose() }
})

test('a rejected store installation cannot persist an enable choice', async () => {
  const fixture = setup([{ source: source('first'), releases: [plugin('a')] }], async () => { throw new Error('Release changed') })
  try {
    await fixture.bindings.install()
    assert.equal(fixture.installations.length, 0)
    assert.equal(fixture.bindings.error.value, 'Release changed')
  } finally { fixture.dispose() }
})

test('install-and-enable includes compatible versions that never selected an existing library plugin', async () => {
  const fixture = setup([{ source: source('first'), releases: [plugin('new', '2.0.0', ['one', 'two'])] }])
  try {
    fixture.state.plugins = [{ id: 'shared', game: 'fixture', version: '1.0.0' }]
    fixture.state.instances[0].plugins = [{ id: 'shared', version: '1.0.0', enabled: true }]
    await fixture.bindings.install()
    assert.equal(fixture.calls[0].method, 'store.installPluginTargets')
    assert.deepEqual(Array.from(fixture.calls[0].params.instanceIds), ['first-client', 'second-client'])
  } finally { fixture.dispose() }
})

test('an update uses the confirmed source, version transition and update action', async () => {
  const fixture = setup([{ source: source('first'), releases: [plugin('a')] }, { source: source('second'), releases: [plugin('b', '2.0.0')] }])
  try {
    fixture.props.open = false; await vue.nextTick()
    fixture.props.localPlugins = [{ id: 'shared', game: 'fixture', version: '1.0.0' }]
    fixture.props.source = fixture.props.offers[1].source
    fixture.props.item = fixture.props.offers[1].releases[0]
    fixture.state.instances[0].plugins = [{ id: 'shared', version: '1.0.0', enabled: true }]
    fixture.state.instances[1].plugins = []
    fixture.props.open = true; await vue.nextTick()
    assert.equal(fixture.bindings.sourceId.value, 'second')
    assert.equal(fixture.bindings.updateFrom.value, '1.0.0')
    assert.equal(fixture.bindings.primaryText.value, 'store.update')
    assert.equal(fixture.bindings.installationItems.value[0].installedVersion, '1.0.0')
    fixture.bindings.activateAfterInstall.value = false
    assert.equal(fixture.bindings.primaryText.value, 'store.update')
    fixture.bindings.chooseSource('first')
    assert.equal(fixture.bindings.sourceOptions.value[0].value, 'second')
    assert.equal(fixture.bindings.sourceOptions.value[1].disabled, true)
    assert.equal(fixture.bindings.sourceOptions.value[1].label, 'first · 1.0.0')
    assert.equal(fixture.bindings.sourceId.value, 'second')
    assert.equal(fixture.bindings.updateFrom.value, '1.0.0')
  } finally { fixture.dispose() }
})

test('a different source can have different versions without reusing another source hash', async () => {
  const fixture = setup([{ source: source('first'), releases: [plugin('a')] }, { source: source('second'), releases: [plugin('new', '2.0.0'), plugin('old', '0.5.0')] }])
  try {
    assert.equal(fixture.bindings.sourceOptions.value[0].label, 'first · 1.0.0')
    assert.equal(fixture.bindings.sourceOptions.value[1].label, 'second · 2.0.0')
    fixture.bindings.chooseSource('second')
    assert.equal(fixture.bindings.item.value.version, '2.0.0')
    fixture.bindings.chooseRelease('old')
    await fixture.bindings.install()
    assert.equal(fixture.calls[0].params.sourceId, 'second')
    assert.equal(fixture.calls[0].params.manifestSha256, 'old')
    assert.equal(fixture.calls[0].params.version, '0.5.0')
  } finally { fixture.dispose() }
})

test('source selection automatically activates compatible clients using one shared download', async () => {
  const fixture = setup([{ source: source('first'), releases: [plugin('a')] }, { source: source('second'), releases: [plugin('b', '1.0.0', ['two'])] }])
  try {
    fixture.bindings.chooseSource('second')
    assert.deepEqual([...fixture.bindings.targets.value], ['second-client'])
    await fixture.bindings.install()
    assert.equal(fixture.calls[0].method, 'store.installPluginTargets')
    assert.deepEqual([...fixture.calls[0].params.instanceIds], ['second-client'])
    assert.equal(fixture.calls[0].params.manifestSha256, 'b')
  } finally { fixture.dispose() }
})

test('catalog refresh cannot silently retarget an open confirmation', async () => {
  const offers = [{ source: source('first'), releases: [plugin('a')] }, { source: source('second'), releases: [plugin('b')] }]
  const fixture = setup(offers)
  try {
    fixture.bindings.chooseSource('second')
    fixture.props.offers[1].releases[0].manifestSha256 = 'replacement'
    fixture.props.offers[1].source.id = 'replacement-source'
    fixture.props.offers[1].source.baseUrl = 'https://replacement.example/'
    await fixture.bindings.install()
    assert.equal(fixture.calls[0].params.sourceId, 'second')
    assert.equal(fixture.calls[0].params.manifestSha256, 'b')
    assert.equal(fixture.calls[0].params.expectedSource.baseUrl, 'https://second.example/')
  } finally { fixture.dispose() }
})

test('source switches cannot alter a pending request and incompatible launchers cannot submit', async () => {
  let finish
  const fixture = setup([{ source: source('first'), releases: [plugin('a')] }, { source: source('second'), releases: [plugin('b')] }], () => new Promise(resolve => { finish = resolve }))
  try {
    const pending = fixture.bindings.install()
    fixture.bindings.chooseSource('second')
    assert.equal(fixture.bindings.sourceId.value, 'first')
    finish({ id: 'download' }); await pending
    fixture.bindings.chooseSource('second')
    fixture.bindings.item.value.launcherCompatible = false
    await fixture.bindings.install()
    assert.equal(fixture.calls.length, 1)
  } finally { fixture.dispose() }
})

import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

async function settle() { for (let i = 0; i < 40; i++) await Promise.resolve(); await vue.nextTick() }
function setup({ packageInstalled = true, start } = {}) {
  const props = vue.reactive({ open: false, initialBuild: 'build', defaultScope: 'private', selectAfterSave: true })
  const client = { id: 'client', game: 'fixture', build: 'build', clientInstalled: true }
  const state = vue.reactive({ selectedGameId: 'fixture', instances: [client], servers: [], selectedServerId: '', selectedInstanceId: '', selectedBuildId: '' })
  const game = { id: 'fixture', name: { en: 'Fixture' }, builds: [{ id: 'build', name: { en: 'Build' }, supportsHosting: true }] }
  const packages = packageInstalled ? [{ directory: 'D:/package', package: { game: 'fixture', builds: ['build'] } }] : []
  const calls = [], emitted = [], cleanup = [], scope = vue.effectScope(), tracked = []
  let saved, download = { id: 'download', stage: 'queued', target: { name: 'Server' } }
  const source = compileScript(parse(readFileSync(new URL('../src/components/LocalServerSetupDialog.vue', import.meta.url), 'utf8')).descriptor, { id: 'local-play' }).content
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount: callback => cleanup.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../i18n') return { localized: value => value?.en ?? '', clientRegion: value => value }
    if (id === '../clientVersion') return { clientVersionCaption: () => '' }
    if (id === '../downloads/live') return { trackDownload: task => tracked.push(task) }
    if (id === '../state') return { state, selectedGame: vue.ref(game), selectedInstance: vue.ref(client), refreshServers: async options => { calls.push({ method: 'state.refreshServers', params: options }); state.servers = [{ id: 'local' }] } }
    if (id === '../bridge') return { inHost: true, call: async (method, params) => {
      calls.push({ method, params })
      if (method === 'hosting.list') return { profiles: [], sessions: [] }
      if (method === 'hosting.packages') return packages
      if (method === 'hosting.localAddresses') return ['192.168.1.2']
      if (method === 'hosting.availablePorts') return [20000, 20001, 20002, 20003]
      if (method === 'hosting.save') { saved = params; return params }
      if (method === 'hosting.start') return start ? start(params) : { profileId: saved.id, runId: 'run', state: 'ready' }
      if (method === 'hosting.destination') return { id: 'local', game: 'fixture' }
      if (method === 'store.sources') return [{ id: 'source', enabled: true }]
      if (method === 'store.catalog') return { packages: [{ kind: 'server', game: 'fixture', builds: ['build'], id: 'server', version: '1', manifestSha256: 'hash', name: { en: 'Server' } }] }
      if (method === 'store.installServer') return download
      if (method === 'downloads.list') return [download]
      throw new Error(`Unexpected method ${method}`)
    } }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  const preferences = new Map()
  runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, {
    module, exports: module.exports, require, crypto: { randomUUID }, setTimeout: () => 1, clearTimeout() {},
    localStorage: { getItem: key => preferences.get(key), setItem: (key, value) => preferences.set(key, value) },
  })
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: (...args) => emitted.push(args) }))
  return { props, state, calls, emitted, bindings, tracked, preferences, setDownload: value => { download = value }, dispose() { cleanup.forEach(fn => fn()); scope.stop() } }
}

test('single-player saves a registered client and nickname without starting the server', async () => {
  const fixture = setup()
  try {
    fixture.props.open = true; await settle()
    assert.equal(fixture.calls.some(call => call.method === 'hosting.save' || call.method === 'hosting.start'), false)
    fixture.bindings.nickname.value = 'LocalPlayer'
    await fixture.bindings.save(); await settle()
    const saved = fixture.calls.find(call => call.method === 'hosting.save').params
    assert.equal(saved.requireAuthentication, false)
    assert.equal(saved.clientInstanceId, 'client')
    assert.equal(saved.storageMode, 'local')
    assert.equal(saved.gamePort, 20000)
    assert.equal(saved.relayPort, 20001)
    assert.equal(saved.apiPort, 20002)
    assert.equal(saved.adminPort, 20003)
    assert.equal(fixture.bindings.advancedOpen.value, false)
    assert.equal(fixture.state.selectedInstanceId, 'client')
    assert.equal(fixture.state.selectedServerId, `local-${saved.id}`)
    assert.equal(fixture.calls.find(call => call.method === 'state.refreshServers').params.refreshSelectedServices, false)
    assert.ok(fixture.emitted.some(([event]) => event === 'saved'))
    assert.equal(fixture.calls.some(call => call.method === 'hosting.start'), false)
    assert.equal(fixture.preferences.get('localPlay.playerName'), 'LocalPlayer')
  } finally { fixture.dispose() }
})

test('closing local setup never starts or connects a server', async () => {
  const fixture = setup()
  try {
    fixture.props.open = true; await settle()
    assert.equal(fixture.calls.some(call => call.method === 'hosting.start'), false)
    fixture.bindings.close()
    assert.equal(fixture.bindings.busy.value, false)
    assert.ok(fixture.emitted.some(([event]) => event === 'close'))
    await settle()
    assert.equal(fixture.state.selectedServerId, '')
    assert.equal(fixture.emitted.some(([event]) => event === 'connected'), false)
    assert.equal(fixture.calls.some(call => call.method === 'hosting.stop'), false)
  } finally { fixture.dispose() }
})

test('the setup dialog keeps cancel, Escape and backdrop dismissal enabled during startup', () => {
  const template = parse(readFileSync(new URL('../src/components/LocalServerSetupDialog.vue', import.meta.url), 'utf8')).descriptor.template.content
  const dialog = template.match(/<WDialog\b[^>]*>/)[0]
  assert.doesNotMatch(dialog, /close-disabled/)
  assert.match(dialog, /dismiss-on-backdrop/)
  assert.match(dialog, /@close="close"/)
})

test('missing packages require installation consent and failed tasks remain visible for retry', async () => {
  const fixture = setup({ packageInstalled: false })
  try {
    fixture.props.open = true; await settle()
    assert.equal(fixture.calls.some(call => call.method === 'store.installServer'), false)
    assert.equal(fixture.calls.some(call => call.method === 'hosting.start'), false)
    await fixture.bindings.install()
    assert.equal(fixture.tracked[0].id, 'download')
    fixture.setDownload({ id: 'download', stage: 'failed', failure: 'source', target: { name: 'Server' } })
    await fixture.bindings.poll(fixture.bindings.revision)
    assert.equal(fixture.bindings.taskSnapshot.value.stage, 'failed')
    assert.equal(fixture.bindings.activeTask.value, 'download')
    assert.equal(fixture.calls.filter(call => call.method === 'store.installServer').length, 1)
    assert.equal(fixture.calls.some(call => call.method === 'hosting.start'), false)
  } finally { fixture.dispose() }
})

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { randomUUID } from 'node:crypto'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as clientLanguages from '../src/clientLanguages.ts'
import * as clientVersion from '../src/clientVersion.ts'

const failuresModule = { exports: {} }
runInNewContext(ts.transpileModule(readFileSync(new URL('../src/serverFailures.ts', import.meta.url), 'utf8'),
  { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
  { module: failuresModule, exports: failuresModule.exports })
const serverFailures = failuresModule.exports

function setup(mode, instance, reply) {
  const props = vue.reactive({ open: false, mode, instance })
  const calls = [], emitted = []
  const state = vue.reactive({ busy: false, selectedServerId: '', clientLanguage: 'en', error: '' })
  const game = { id: 'fixture', builds: [{ id: 'test-1', branch: 'test', name: { en: 'Fixture' }, releaseVersion: '', nativeLanguage: 'en' }] }
  const source = readFileSync(new URL('../src/components/InstanceDialog.vue', import.meta.url), 'utf8')
  const script = compileScript(parse(source).descriptor, { id: 'InstanceDialog' }).content
  const javascript = ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return vue
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../serverFailures') return serverFailures
    if (id === '../state') return { state, selectedGame: vue.ref(game), perform: async action => { await action(); return true } }
    if (id === '../bridge') return { inHost: true, call: async (method, params) => {
      calls.push({ method, params })
      if (reply) return reply(method, params)
      if (method === 'instances.inspectInstallation') return { path: 'D:/Fixture', containsFiles: true, detectedBuildIds: ['test-1'] }
      if (method === 'instances.create') return { ...params }
    } }
    if (id === '../i18n') return { localized: name => name?.en, clientRegion: value => value }
    if (id === '../clientLanguages') return clientLanguages
    if (id === '../clientVersion') return clientVersion
    if (id === '../downloads/live') return { trackDownload() {} }
    if (id === '../plugins/recommendations') return { requestRecommendations: async () => {} }
    if (id === '../plugins/clientInstallComponents') return { useClientInstallComponents: () => ({ confirm: () => async () => [] }) }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, crypto: { randomUUID } })
  const scope = vue.effectScope()
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: event => emitted.push(event) }))
  return { props, calls, emitted, bindings, state, dispose: () => scope.stop() }
}

test('new installations default to automatic checks and preserve an opt-out through folder confirmation', async () => {
  const fixture = setup('create')
  try {
    fixture.props.open = true
    await vue.nextTick()
    assert.equal(fixture.bindings.checkIntegrity.value, true)
    fixture.bindings.checkIntegrity.value = false
    await fixture.bindings.save()
    assert.equal(fixture.calls.some(call => call.method === 'instances.create'), false)
    await fixture.bindings.createClient(fixture.bindings.pendingCreate.value, true)
    assert.equal(fixture.calls.find(call => call.method === 'instances.create').params.checkIntegrity, false)
    assert.equal(fixture.emitted.at(-1), 'close')
    fixture.props.open = false
    await vue.nextTick()
    fixture.props.open = true
    await vue.nextTick()
    assert.equal(fixture.bindings.checkIntegrity.value, true)
  } finally { fixture.dispose() }
})

test('client download discovery retains source diagnostics for the details action and retry', async () => {
  const cause = new serverFailures.ServerRequestError('Source returned HTTP 503', { code: 'http_error', httpStatus: 503, transportCode: 'unavailable' })
  let attempts = 0
  const fixture = setup('create', undefined, async method => {
    if (method === 'servers.clientRelease') { attempts++; throw cause }
  })
  try {
    fixture.props.open = true
    await vue.nextTick()
    fixture.bindings.createdRequest.value = { serverId: 'server', presetId: 'client' }
    await fixture.bindings.discover()
    const failure = fixture.bindings.downloadFailure.value
    assert.equal(failure.details.code, 'client_download_unavailable')
    assert.equal(failure.details.httpStatus, 503)
    assert.equal(failure.details.transportCode, 'unavailable')
    assert.equal(failure.details.reason, cause.message)
    await fixture.bindings.discover()
    assert.equal(attempts, 2)
  } finally { fixture.dispose() }
})

test('download failures expose details while expected credential failures stay concise', () => {
  const source = compileScript(parse(readFileSync(new URL('../src/components/ServerFailureNotice.vue', import.meta.url), 'utf8')).descriptor, { id: 'ServerFailureNotice' }).content
  const module = { exports: {} }
  const require = id => id === 'vue' ? vue : id === 'vue-i18n' ? { useI18n: () => ({ t: key => key }) }
    : id === 'vue-router' ? { useRouter: () => ({}) } : id === '../serverFailures' ? serverFailures : { default: {} }
  runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, { module, exports: module.exports, require })
  const props = vue.reactive({ error: new serverFailures.ServerRequestError('Unavailable', { code: 'client_download_unavailable', reason: 'HTTP 503' }) })
  const scope = vue.effectScope()
  try {
    const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit() {} }))
    assert.equal(bindings.showDetails.value, true)
    assert.match(bindings.text.value, /HTTP 503/)
    props.error = new serverFailures.ServerRequestError('Incorrect password', { code: 'wrong_password' })
    assert.equal(bindings.showDetails.value, false)
  } finally { scope.stop() }
})

test('client settings load the persisted opt-out and save a changed preference', async () => {
  const fixture = setup('edit', { id: 'existing', name: 'Existing', build: 'test-1', checkIntegrity: false, language: 'en', endpoint: null })
  try {
    fixture.props.open = true
    await vue.nextTick()
    assert.equal(fixture.bindings.checkIntegrity.value, false)
    fixture.bindings.checkIntegrity.value = true
    await fixture.bindings.save()
    assert.equal(fixture.calls.find(call => call.method === 'instances.update').params.checkIntegrity, true)
    assert.equal(fixture.state.error, '')
  } finally { fixture.dispose() }
})

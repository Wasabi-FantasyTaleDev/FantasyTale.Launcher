import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as versions from '../src/store/versions.ts'
import { updates } from './store-update-helpers.mjs'

const groupModule = { exports: {} }
runInNewContext(ts.transpileModule(readFileSync(new URL('../src/store/groups.ts', import.meta.url), 'utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
  { module: groupModule, exports: groupModule.exports, require: () => versions })

function deferred() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

async function setup() {
  let entries = [{ id: 'source', kind: 'http', enabled: true, baseUrl: 'https://store.example/', catalogPath: 'source.toml' }]
  let response = Promise.resolve({ packages: [{ id: 'client', kind: 'client', game: 'fixture', builds: ['build'], name: 'Client', version: '1', manifestSha256: 'one' }] })
  const scope = vue.effectScope(), unmount = []
  const sessionModule = { exports: {} }
  runInNewContext(ts.transpileModule(readFileSync(new URL('../src/store/session.ts', import.meta.url), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { module: sessionModule, exports: sessionModule.exports, require: id => {
    if (id === 'vue') return vue
    throw new Error(`Unexpected session dependency ${id}`)
  } })
  const source = compileScript(parse(readFileSync(new URL('../src/pages/StorePage.vue', import.meta.url), 'utf8')).descriptor, { id: 'StorePage' }).content.replaceAll('import.meta.env.DEV', 'false')
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key, locale: vue.ref('en') }) }
    if (id === 'vue-router') return { useRoute: () => ({ query: {} }) }
    if (id === '../bridge') return { inHost: true, call: async (method, params) => method === 'plugins.list' ? [] : method === 'store.sources' ? entries : typeof response === 'function' ? response(params) : response }
    if (id === '../state') return { state: vue.reactive({ games: [] }) }
    if (id === '../i18n') return { localized: value => value }
    if (id === '../downloads/model') return { formatBytes: value => String(value) }
    if (id === '../store/preview') return { previewStoreCatalog: () => ({ packages: [] }) }
    if (id === '../store/versions') return { newestFirst: value => value }
    if (id === '../store/groups') return groupModule.exports
    if (id === '../store/session') return sessionModule.exports
    if (id === '../store/background') return { canInstallStorePlugin: () => false,
      liveStoreSession: sessionModule.exports.createStoreSession({
        sources: () => require('../bridge').call('store.sources'),
        catalog: (source, force) => require('../bridge').call('store.catalog', { id: source.id, force }),
        plugins: () => require('../bridge').call('plugins.list'), error: error => error.message,
      }) }

    if (id === '../store/updates') return updates
    if (id === '../downloads/live') return { downloads: vue.reactive({ tasks: [] }) }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require })
  const bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  await new Promise(resolve => setImmediate(resolve))
  return { bindings, setSources(value) { entries = value }, setCatalog(value) { response = value },
    dispose() { unmount.forEach(callback => callback()); scope.stop() } }
}

test('store refresh preserves the visible catalog and selection while awaiting its replacement', async () => {
  const fixture = await setup(), reply = deferred()
  try {
    assert.equal(fixture.bindings.current.value.id, 'client')
    const selection = fixture.bindings.selected.value
    fixture.setCatalog(reply.promise)
    const refreshing = fixture.bindings.refresh(true)
    await new Promise(resolve => setImmediate(resolve))
    assert.equal(fixture.bindings.loading.value, true)
    assert.equal(fixture.bindings.current.value.id, 'client')
    assert.equal(fixture.bindings.selected.value, selection)
    reply.resolve({ packages: [{ ...fixture.bindings.current.value, version: '2', manifestSha256: 'two' }] })
    await refreshing
    assert.equal(fixture.bindings.loading.value, false)
    assert.equal(fixture.bindings.current.value.version, '2')
    assert.equal(fixture.bindings.selected.value, selection)
  } finally { fixture.dispose() }
})

test('a failed refresh keeps the previous store content and exposes the source failure', async () => {
  const fixture = await setup(), reply = deferred()
  try {
    fixture.setCatalog(reply.promise)
    const refreshing = fixture.bindings.refresh(true)
    reply.reject(new Error('Source unavailable')); await refreshing
    assert.equal(fixture.bindings.loading.value, false)
    assert.equal(fixture.bindings.current.value.id, 'client')
    assert.equal(fixture.bindings.errors.value.source, 'Source unavailable')
  } finally { fixture.dispose() }
})

test('changing a store endpoint discards its previous catalog before loading the new endpoint', async () => {
  const fixture = await setup(), reply = deferred()
  try {
    fixture.setSources([{ id: 'source', kind: 'http', enabled: true, baseUrl: 'https://replacement.example/', catalogPath: 'source.toml' }])
    fixture.setCatalog(reply.promise)
    const refreshing = fixture.bindings.refresh(true)
    await new Promise(resolve => setImmediate(resolve))
    assert.equal(fixture.bindings.loading.value, true)
    assert.equal(fixture.bindings.groups.value.length, 0)
    assert.equal(fixture.bindings.current.value, undefined)
    reply.resolve({ packages: [] }); await refreshing
    assert.equal(fixture.bindings.loading.value, false)
  } finally { fixture.dispose() }
})

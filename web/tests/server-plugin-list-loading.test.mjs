import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const transpile = source => ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
const inventory = { exports: {} }
runInNewContext(transpile(readFileSync(new URL('../src/plugins/serverPluginInventory.ts', import.meta.url), 'utf8')), { module: inventory, exports: inventory.exports })

function pending() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
async function settle() { for (let i = 0; i < 6; i++) { await Promise.resolve(); await vue.nextTick() } }

function setup(reply, plugins = []) {
  const props = vue.reactive({ active: true, game: 'g', builds: ['b'], recommendations: [{ id: 'a.b.c', required: true, disabled: false, builds: [] }], sources: [], serverId: 'srv' })
  const state = vue.reactive({ instances: [{ id: 'i1', game: 'g', build: 'b', name: 'Client' }], selectedInstanceId: 'i1', plugins })
  const calls = [], scope = vue.effectScope()
  const source = compileScript(parse(readFileSync(new URL('../src/components/ServerPluginListPanel.vue', import.meta.url), 'utf8')).descriptor, { id: 'panel' }).content
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount() {} }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../bridge') return { inHost: true, demoMode: undefined, call: async (method, args) => { calls.push({ method, args }); return reply(method, args) } }
    if (id === '../state') return { state }
    if (id === '../i18n') return { localized: value => typeof value === 'string' ? value : value?.en }
    if (id === '../store/versions') return { newestFirst: items => items }
    if (id === '../plugins/serverPluginInventory') return inventory.exports
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(transpile(source), { module, exports: module.exports, require, crypto, URL })
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit() {} }))
  return { bindings, calls, state }
}
const installedPlugin = { id: 'a.b.c', game: 'g', name: { en: 'Real name' }, available: true, selection: { enabled: true } }

test('a row never reads as not installed or shows the raw id before the inventory is known', async () => {
  const inventoryRead = pending()
  const fixture = setup(method => method === 'plugins.list' ? inventoryRead.promise : [])
  await settle()
  const { installState, title, plugins } = fixture.bindings
  assert.equal(plugins.value, null)
  assert.equal(installState('a.b.c'), 'unknown')
  assert.equal(title('a.b.c'), '')
  inventoryRead.resolve([installedPlugin])
  await settle()
  assert.equal(installState('a.b.c'), 'installed')
  assert.equal(title('a.b.c'), 'Real name')
})

test('an id still unnamed once the inventory is read falls back to the id and offers Install', async () => {
  const fixture = setup(method => method === 'plugins.list' ? [] : [])
  await settle()
  assert.equal(fixture.bindings.installState('a.b.c'), 'missing')
  assert.equal(fixture.bindings.title('a.b.c'), 'a.b.c')
})

test('the inventory and the sources load in parallel, and the network-free list omits the server', async () => {
  const fixture = setup(() => new Promise(() => {}))
  await settle()
  assert.deepEqual(fixture.calls.map(c => c.method).sort(), ['plugins.list', 'store.sources'])
  assert.equal(fixture.calls.find(c => c.method === 'plugins.list').args.serverId, undefined)
})

test('a later visit draws the remembered inventory at once', async () => {
  setup(method => method === 'plugins.list' ? [installedPlugin] : []); await settle()
  const never = new Promise(() => {})
  const again = setup(() => never); await settle()
  assert.equal(again.bindings.installState('a.b.c'), 'installed')
  assert.equal(again.bindings.title('a.b.c'), 'Real name')
})

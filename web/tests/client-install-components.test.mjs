import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import * as vue from 'vue'

const required = { id: 'auth', name: { en: 'Authentication' }, required: true }
const optional = { id: 'font', name: { en: 'Font' }, required: false }
const flush = () => new Promise(resolve => setImmediate(resolve))

test('installation displays all recommendations but checks only required and explicit opt-in defaults', async () => {
  const checked = { ...optional, id: 'checked', defaultSelected: true }
  const unchecked = { ...optional, id: 'unchecked', defaultSelected: false }
  const f = setup(async () => [optional, checked, unchecked, required])
  try {
    f.context.open = true; await flush()
    assert.equal(f.bindings.items.value.length, 4)
    assert.deepEqual([...f.bindings.selected.value], ['checked', 'auth'])
    f.bindings.selectPlugin('font', true)
    f.bindings.selectPlugin('checked', false)
    await f.bindings.confirm()('client')
    assert.deepEqual([...f.calls.at(-1).params.ids], ['auth', 'font'])
  } finally { f.dispose() }
})
function deferred() { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }
function setup(reply = async () => [optional, required]) {
  const context = vue.reactive({ open: false, game: 'fixture', serverId: 'first', build: { id: 'build', nativeLanguage: 'id', additionalLanguages: { 'zh-CN': 'translation.zh', ko: 'translation.ko' } } })
  const calls = [], tracked = [], unmount = [], scope = vue.effectScope()
  const source = readFileSync(new URL('../src/plugins/clientInstallComponents.ts', import.meta.url), 'utf8')
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount: callback => unmount.push(callback) }
    if (id === '../bridge') return { call: async (method, params) => { calls.push({ method, params }); return method === 'plugins.installationPlan' ? await reply(params) : [{ id: 'plugin-task' }] } }
    if (id === '../downloads/live') return { trackDownload: task => tracked.push(task) }
    if (id === './clientInstallPreview') return { previewInstallationComponents: () => [] }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
    { module, exports: module.exports, require })
  const bindings = scope.run(() => module.exports.useClientInstallComponents(() => ({ ...context })))
  return { context, bindings, calls, tracked, dispose() { unmount.forEach(callback => callback()); scope.stop() } }
}

test('installation keeps native language checked and combines optional translations with mandatory plugins', async () => {
  const f = setup()
  try {
    f.context.open = true; await flush()
    assert.deepEqual([...f.bindings.languages.value], ['id'])
    f.bindings.selectLanguage('id', false)
    f.bindings.selectLanguage('zh-CN', true); f.bindings.selectLanguage('ko', true)
    f.bindings.selectPlugin('auth', false); f.bindings.selectPlugin('font', false)
    assert.deepEqual([...f.bindings.languages.value], ['id', 'zh-CN', 'ko'])
    f.bindings.selected.value = []
    await f.bindings.confirm()('client')
    const request = f.calls.at(-1)
    assert.equal(request.method, 'plugins.installationComponents')
    assert.deepEqual([...request.params.ids], ['auth'])
    assert.deepEqual([...request.params.languages], ['id', 'zh-CN', 'ko'])
    assert.equal(f.tracked[0].id, 'plugin-task')
  } finally { f.dispose() }
})

test('confirmed installation retains its server and selections while discovery changes', async () => {
  const f = setup()
  try {
    f.context.open = true; await flush()
    f.bindings.selectPlugin('font', false)
    const confirmed = f.bindings.confirm()
    f.context.serverId = 'second'; await flush()
    await confirmed('original-client')
    assert.equal(f.calls.at(-1).params.serverId, 'first')
    assert.deepEqual([...f.calls.at(-1).params.ids], ['auth'])
  } finally { f.dispose() }
})

test('stale recommendation replies cannot populate a different build or a closed dialog', async () => {
  const first = deferred(), second = deferred()
  const f = setup(params => params.build === 'build' ? first.promise : second.promise)
  try {
    f.context.open = true; await flush()
    f.context.build = { id: 'other', nativeLanguage: 'en', additionalLanguages: {} }; await flush()
    first.resolve([required]); await flush()
    assert.equal(f.bindings.items.value.length, 0)
    assert.equal(f.bindings.loading.value, true)
    assert.equal(f.bindings.additionalLanguages.value.length, 0)
    f.context.open = false; await flush()
    second.resolve([optional]); await flush()
    assert.equal(f.bindings.items.value.length, 0)
    assert.equal(f.bindings.loading.value, false)
  } finally { f.dispose() }
})

test('failed planning blocks installation and a successful retry restores the form', async () => {
  let fail = true
  const f = setup(async () => { if (fail) throw new Error('Catalog unavailable'); return [required] })
  try {
    f.context.open = true; await flush()
    assert.throws(() => f.bindings.confirm(), /Catalog unavailable/)
    fail = false; await f.bindings.refresh()
    await f.bindings.confirm()('client')
    assert.equal(f.calls.at(-1).params.instanceId, 'client')
  } finally { f.dispose() }
})

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

async function settle() { for (let i = 0; i < 30; i++) await Promise.resolve(); await vue.nextTick() }
function setup(read = async () => ['first', 'latest']) {
  const props = vue.reactive({ open: false, session: { profileId: 'profile', runId: 'first' } })
  const calls = [], timers = new Map(), cleanup = [], scope = vue.effectScope(), emitted = []
  let timerId = 0
  const source = compileScript(parse(readFileSync(new URL('../src/components/HostingLogDialog.vue', import.meta.url), 'utf8')).descriptor, { id: 'hosting-log' }).content
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount: callback => cleanup.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../bridge') return { call: async (method, params) => { calls.push({ method, params }); return read(params) } }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, {
    module, exports: module.exports, require,
    setTimeout: callback => { timers.set(++timerId, callback); return timerId }, clearTimeout: id => timers.delete(id),
  })
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: event => emitted.push(event) }))
  const viewport = { scrollTop: 0, scrollHeight: 900 }
  bindings.logView.value = viewport
  return { props, bindings, viewport, calls, emitted, timers, dispose() { cleanup.forEach(callback => callback()); scope.stop() } }
}

test('log details open at the bottom and retain content when session snapshots refresh', async () => {
  const fixture = setup()
  try {
    fixture.props.open = true; await settle()
    assert.equal(fixture.viewport.scrollTop, 900)
    assert.equal(fixture.bindings.text.value, 'first\nlatest')
    fixture.props.session = { ...fixture.props.session, state: 'failed' }; await settle()
    assert.equal(fixture.calls.length, 1)
    assert.equal(fixture.bindings.text.value, 'first\nlatest')
    assert.equal(fixture.bindings.loading.value, false)
    fixture.viewport.scrollTop = 0
    fixture.viewport.scrollHeight = 1200
    const [id, refresh] = fixture.timers.entries().next().value
    fixture.timers.delete(id); refresh(); await settle()
    assert.equal(fixture.viewport.scrollTop, 1200)
    assert.equal(fixture.calls.length, 2)
  } finally { fixture.dispose() }
})

test('closing details ignores a late response and stops its refresh timer', async () => {
  let resolve
  const fixture = setup(() => new Promise(ready => { resolve = ready }))
  try {
    fixture.props.open = true; await settle()
    fixture.bindings.close()
    resolve(['late']); await settle()
    assert.deepEqual(fixture.emitted, ['close'])
    assert.equal(fixture.bindings.text.value, '')
    assert.equal(fixture.timers.size, 0)
    assert.equal(fixture.viewport.scrollTop, 0)
  } finally { fixture.dispose() }
})

test('switching invocations rejects the previous logs even when its read finishes later', async () => {
  let resolve
  const fixture = setup(params => params.runId === 'first' ? new Promise(ready => { resolve = ready }) : Promise.resolve(['replacement']))
  try {
    fixture.props.open = true; await settle()
    fixture.props.session = { profileId: 'profile', runId: 'second' }; await settle()
    resolve(['stale']); await settle()
    assert.equal(fixture.bindings.text.value, 'replacement')
    assert.equal(fixture.timers.size, 1)
  } finally { fixture.dispose() }
})

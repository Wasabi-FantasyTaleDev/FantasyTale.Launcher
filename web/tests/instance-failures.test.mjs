import assert from 'node:assert/strict'
import test from 'node:test'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { InstanceRequestError, instanceFailure } from '../src/instanceFailures.ts'

test('translated occupancy errors retain the exact lease and client context for viewing and copying', () => {
  const details = { operation: 'plugins.applyScheme', instanceId: 'client', clientPath: 'D:/Client', lockPath: 'D:/Data/.locks/client.lock', osError: 32, kind: 'client-lease' }
  const error = instanceFailure({ error: 'instance_in_use', instanceFailure: details }, key => key)
  assert.ok(error instanceof InstanceRequestError)
  assert.equal(error.message, 'instanceBusy.message')
  assert.equal(error.code, 'instance_in_use')
  assert.deepEqual(JSON.parse(JSON.stringify(error.details)), details)
  details.operation = 'later request'
  assert.equal(error.details.operation, 'plugins.applyScheme')
})

test('a file-exists message and unrelated disk errors do not claim an active client lease', () => {
  for (const reply of [{ error: 'File exists' }, { error: 'instance_lock_unavailable' }, { error: 'disk error', code: 'disk' }])
    assert.equal(instanceFailure(reply, key => key), undefined)
})

function setup(respond) {
  const copied = [], calls = [], scope = vue.effectScope()
  const props = vue.reactive({ error: new InstanceRequestError('busy', { operation: 'plugins.configure', instanceId: 'client', lockPath: 'D:/Data/client.lock', osError: 32 }) })
  const source = compileScript(parse(readFileSync(new URL('../src/components/InstanceFailureNotice.vue', import.meta.url), 'utf8')).descriptor, { id: 'instance-failure' }).content
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return vue
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../bridge') return { call: async (method, params) => { calls.push({ method, params }); return respond(method, params) } }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, navigator: { clipboard: { writeText: async text => copied.push(text) } } })
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit() {} }))
  return { bindings, props, calls, copied, dispose: () => scope.stop() }
}

test('view details opens without another write and copies the lease context with recent events', async () => {
  const fixture = setup(() => ({ lifecycle: 'plugins.configure requested' }))
  try {
    assert.equal(fixture.calls.length, 0)
    await fixture.bindings.showDetails()
    assert.equal(fixture.bindings.open.value, true)
    assert.equal(fixture.calls[0].method, 'instances.launchDiagnostics')
    await fixture.bindings.copy()
    const details = JSON.parse(fixture.copied[0])
    assert.equal(details.lockPath, 'D:/Data/client.lock')
    assert.equal(details.osError, 32)
    assert.equal(details.recentLauncherEvents, 'plugins.configure requested')
  } finally { fixture.dispose() }
})

test('unavailable diagnostic logs do not prevent copying the original occupancy context', async () => {
  const fixture = setup(() => { throw new Error('private-error') })
  try {
    await fixture.bindings.showDetails()
    await fixture.bindings.copy()
    assert.equal(JSON.parse(fixture.copied[0]).operation, 'plugins.configure')
    assert.equal(fixture.bindings.detailError.value, '')
    assert.ok(!fixture.copied[0].includes('private-error'))
  } finally { fixture.dispose() }
})

test('a newer failure closes stale details and ignores a delayed diagnostic reply', async () => {
  let complete
  const fixture = setup(() => new Promise(resolve => { complete = resolve }))
  try {
    const pending = fixture.bindings.showDetails()
    fixture.props.error = new InstanceRequestError('new busy', { instanceId: 'other' })
    await vue.nextTick()
    complete({ lifecycle: 'stale-events' })
    await pending
    assert.equal(fixture.bindings.open.value, false)
    assert.equal(fixture.bindings.lifecycle.value, '')
  } finally { fixture.dispose() }
})

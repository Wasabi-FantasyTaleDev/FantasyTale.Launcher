import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { clientFailureCode } from '../src/launchFailure.ts'

function setup(respond) {
  const calls = [], copied = [], scope = vue.effectScope()
  const props = vue.reactive({ failure: { instanceId: 'client', phase: 'bootstrap-dispose', failureType: 'IOException', error: 'launch_cleanup_failed' }, message: 'Connection error' })
  const source = compileScript(parse(readFileSync(new URL('../src/components/LaunchFailureNotice.vue', import.meta.url), 'utf8')).descriptor, { id: 'launch-failure' }).content
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return vue
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../state') return { state: { instances: [{ id: 'client', name: 'Client', build: 'cn-324' }] } }
    if (id === '../launchFailure') return { clientFailureCode }
    if (id === '../bridge') return { call: async (method, params) => { calls.push({ method, params }); return respond(method, params) } }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, navigator: { clipboard: { writeText: async text => copied.push(text) } } })
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit() {} }))
  return { bindings, props, calls, copied, dispose: () => scope.stop() }
}

test('details open directly without a diagnostic plugin and copy the recorded failure plus launch events', async () => {
  const fixture = setup(() => ({ lifecycle: 'gateway-process-exited' }))
  try {
    assert.equal(fixture.calls.length, 0)
    await fixture.bindings.showDetails()
    assert.equal(fixture.bindings.open.value, true)
    assert.equal(fixture.calls[0].method, 'instances.launchDiagnostics')
    await fixture.bindings.copy()
    const details = JSON.parse(fixture.copied[0])
    assert.equal(details.build, 'cn-324')
    assert.equal(details.phase, 'bootstrap-dispose')
    assert.equal(details.recentLauncherEvents, 'gateway-process-exited')
  } finally { fixture.dispose() }
})

test('failure to read logs retains copyable error context and clears the loading state', async () => {
  const fixture = setup(() => { throw new Error('private-path') })
  try {
    await fixture.bindings.showDetails()
    assert.equal(fixture.bindings.busy.value, false)
    assert.equal(fixture.bindings.detailError.value, 'launchCleanup.readFailed')
    await fixture.bindings.copy()
    assert.equal(JSON.parse(fixture.copied[0]).failureType, 'IOException')
    assert.ok(!fixture.copied[0].includes('private-path'))
  } finally { fixture.dispose() }
})

test('a late diagnostic reply cannot replace a newer failure', async () => {
  let complete
  const fixture = setup(() => new Promise(resolve => { complete = resolve }))
  try {
    const pending = fixture.bindings.showDetails()
    fixture.props.failure = { ...fixture.props.failure, instanceId: 'other' }
    await vue.nextTick()
    complete({ lifecycle: 'stale-events' })
    await pending
    assert.equal(fixture.bindings.open.value, false)
    assert.equal(fixture.bindings.lifecycle.value, '')
    assert.equal(fixture.bindings.busy.value, false)
  } finally { fixture.dispose() }
})

test('a game access violation remains visible and copyable beside Gateway cleanup failure', async () => {
  const fixture = setup(() => ({ lifecycle: 'launch-client-exited' }))
  try {
    Object.assign(fixture.props.failure, { exitConfirmed: true, exitCode: -1073741819 })
    assert.equal(fixture.bindings.clientCode.value, '0xC0000005')
    assert.equal(fixture.bindings.stage.value, 'bootstrap')
    await fixture.bindings.showDetails()
    await fixture.bindings.copy()
    assert.equal(JSON.parse(fixture.copied[0]).exitCode, -1073741819)
    assert.equal(JSON.parse(fixture.copied[0]).phase, 'bootstrap-dispose')
  } finally { fixture.dispose() }
})

test('ordinary exits and unconfirmed process states do not acquire a Windows crash diagnosis', () => {
  for (const exitCode of [undefined, null, 0, 1, 23, 0x40000001, 0x80000001, NaN, Infinity, 0x1c0000005])
    assert.equal(clientFailureCode({ exitConfirmed: true, exitCode }), undefined)
  assert.equal(clientFailureCode({ exitConfirmed: false, exitCode: -1073741819 }), undefined)
  assert.equal(clientFailureCode({ exitConfirmed: true, exitCode: 0xc0000005 }), '0xC0000005')
})

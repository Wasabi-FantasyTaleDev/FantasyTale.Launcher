import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as guidance from '../src/downloads/updateFailure.ts'

function setup(respond, inHost = true, component = 'LauncherUpdateFailureDialog') {
  const calls = [], mounted = [], unmount = [], timers = [], state = vue.reactive({ error: '' }), scope = vue.effectScope()
  const source = compileScript(parse(readFileSync(new URL(`../src/components/${component}.vue`, import.meta.url), 'utf8')).descriptor, { id: component }).content
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onMounted: callback => mounted.push(callback), onBeforeUnmount: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../state') return { state }
    if (id === '../downloads/updateFailure') return guidance
    if (id === '../downloads/launcherUpdate') return { launcherUpdate: { refresh: async () => {}, show() {} } }
    if (id === '../bridge') return { inHost, call: async (method, params) => { calls.push({ method, params }); return respond(method, params) } }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, setTimeout: callback => { timers.push(callback); return timers.length }, clearTimeout() {} })
  const bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  return { calls, mounted, bindings, timers, state, dispose() { unmount.splice(0).forEach(callback => callback()); scope.stop() } }
}

const failure = { status: 'failed', manifestSha256: 'a'.repeat(64), failure: 'network', version: '0.1.7' }
const manual = { version: '0.1.7', url: 'https://downloads.example/latest' }

test('settings retain copyable installation diagnostics while restored staging is ready, then clear them after success', async () => {
  let attempt = { status: 'failed', failure: 'disk', phase: 'staging', errorCode: -2147024784 }
  const ready = { status: 'ready', version: '0.1.23', manifestSha256: 'b'.repeat(64), failure: null }
  const fixture = setup(method => method === 'launcherUpdates.state' ? ready : method === 'launcherUpdates.lastAttempt' ? attempt : [], true, 'LauncherUpdateSettings')
  try {
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.hasDiagnostics.value, true)
    assert.match(fixture.bindings.diagnosticText.value, /launcherUpdateDiagnostics.phases.staging/)
    assert.match(fixture.bindings.diagnosticText.value, /HRESULT: 0x80070070/)
    assert.equal(fixture.bindings.detailsOpen.value, false)
    assert.equal(fixture.calls.some(call => call.method === 'launcherUpdates.download' || call.method === 'launcherUpdates.restart'), false)
    attempt = null
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.hasDiagnostics.value, false)
    assert.equal(fixture.bindings.diagnosticText.value, '')
  } finally { fixture.dispose() }
})

test('settings ignore a delayed installation result after unmount', async () => {
  let resolve
  const pending = new Promise(yes => { resolve = yes })
  const fixture = setup(method => method === 'launcherUpdates.state' ? failure : method === 'launcherUpdates.lastAttempt' ? pending : [], true, 'LauncherUpdateSettings')
  const refreshing = fixture.bindings.refresh()
  fixture.dispose()
  resolve({ status: 'rolled-back', failure: 'startup', phase: 'frontend-startup', errorCode: -1 })
  await refreshing
  assert.equal(fixture.bindings.hasDiagnostics.value, false)
  assert.equal(fixture.bindings.lastAttempt.value, null)
})

test('installation diagnostics translate fixed fields and retain only a numeric HRESULT', () => {
  const attempt = { status: 'failed', failure: 'disk', phase: 'staging', errorCode: -2147024784 }
  assert.deepEqual(guidance.updateAttemptDetails(attempt, key => key), [
    'launcherUpdateDiagnostics.lastAttempt: launcherUpdateDiagnostics.failed',
    'clientUpdates.disk', 'launcherUpdateDiagnostics.phase: launcherUpdateDiagnostics.phases.staging', 'HRESULT: 0x80070070',
  ])
  assert.deepEqual(guidance.updateAttemptDetails(null, key => key), [])
  assert.deepEqual(guidance.updateAttemptDetails({ ...attempt, status: 'completed' }, key => key), [])
  assert.deepEqual(guidance.updateAttemptDetails({ ...attempt, status: 'token=secret' }, key => key), [])
  const legacy = guidance.updateAttemptDetails({ status: 'rolled-back', failure: null, phase: null, errorCode: null }, key => key)
  assert.deepEqual(legacy, ['launcherUpdateDiagnostics.lastAttempt: launcherUpdateDiagnostics.rolled-back'])
  for (const errorCode of ['https://private.example/?token=secret', 2147483648, -2147483649, 1.5, NaN, Infinity]) {
    const safe = guidance.updateAttemptDetails({ ...attempt, failure: 'token=secret', phase: 'private.example', errorCode }, key => key)
    assert.deepEqual(safe, ['launcherUpdateDiagnostics.lastAttempt: launcherUpdateDiagnostics.failed'])
  }
})

test('discovery explains its recorded cause and never renders untrusted reason text', async () => {
  let update = { ...failure, manifestSha256: null, version: null, failureReason: 'serverAccess', httpStatus: 403 }
  const fixture = setup(() => update)
  try {
    await fixture.bindings.refresh()
    assert.equal(fixture.state.error, 'launcherUpdates.reasons.serverAccess')
    update = { ...update, failureReason: 'timeout', httpStatus: 504 }
    await fixture.bindings.refresh()
    assert.equal(fixture.state.error, 'launcherUpdates.reasons.timeout')
    update = { ...update, failureReason: 'private-url?token=secret' }
    await fixture.bindings.refresh()
    assert.equal(fixture.state.error, 'launcherUpdates.reasons.networkUnknown')
    assert.equal(fixture.bindings.open.value, false)
  } finally { fixture.dispose() }
})

test('failed automatic staging offers the detected version and opens a browser only after user action', async () => {
  const fixture = setup(method => method === 'launcherUpdates.state' ? failure : method === 'launcherUpdates.manualDownload' ? manual : null)
  try {
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.open.value, true)
    assert.equal(fixture.bindings.version.value, '0.1.7')
    assert.equal(fixture.calls.some(call => call.method === 'shell.openExternal'), false)
    await fixture.bindings.openDownload()
    assert.equal(fixture.calls.find(call => call.method === 'shell.openExternal').params.url, manual.url)
    assert.equal(fixture.bindings.open.value, false)
  } finally { fixture.dispose() }
})

test('dismissing one failure does not reopen the same prompt on every poll', async () => {
  const fixture = setup(method => method === 'launcherUpdates.state' ? failure : manual)
  try {
    await fixture.bindings.refresh(); fixture.bindings.open.value = false
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.open.value, false)
    assert.equal(fixture.calls.filter(call => call.method === 'launcherUpdates.manualDownload').length, 1)
  } finally { fixture.dispose() }
})

test('unreachable discovery reports a connection error without offering a manual update', async () => {
  const fixture = setup(method => method === 'launcherUpdates.state' ? { ...failure, manifestSha256: null, version: null } : { ...manual, version: null })
  try {
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.version.value, null)
    assert.equal(fixture.bindings.open.value, false)
    assert.equal(fixture.state.error, 'launcherUpdates.reasons.networkUnknown')
    assert.equal(fixture.calls.some(call => call.method === 'launcherUpdates.manualDownload'), false)
  } finally { fixture.dispose() }
})

test('a delayed manual-download response cannot reopen a prompt after unmount', async () => {
  let resolve
  const pending = new Promise(yes => { resolve = yes })
  const fixture = setup(method => method === 'launcherUpdates.state' ? failure : pending)
  const refreshing = fixture.bindings.refresh()
  await vue.nextTick(); await vue.nextTick()
  fixture.dispose(); resolve(manual); await refreshing
  assert.equal(fixture.bindings.open.value, false)
  assert.equal(fixture.timers.length, 0)
})

test('a browser-opening failure leaves the dialog available and clears its busy state', async () => {
  const fixture = setup(method => {
    if (method === 'shell.openExternal') throw new Error('browser unavailable')
    return method === 'launcherUpdates.state' ? failure : manual
  })
  try {
    await fixture.bindings.refresh(); await fixture.bindings.openDownload()
    assert.equal(fixture.bindings.open.value, true)
    assert.equal(fixture.bindings.busy.value, false)
    assert.equal(fixture.state.error, 'browser unavailable')
  } finally { fixture.dispose() }
})

test('ordinary browser previews do not start native update failure polling', () => {
  const fixture = setup(() => { throw new Error('No native call expected') }, false)
  try { fixture.mounted.forEach(callback => callback()); assert.equal(fixture.calls.length, 0) }
  finally { fixture.dispose() }
})


test('an unverified label cannot trigger an update-failure prompt', async () => {
  const fixture = setup(() => ({ ...failure, manifestSha256: null }))
  try {
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.open.value, false)
    assert.equal(fixture.calls.length, 1)
  } finally { fixture.dispose() }
})

test('successful discovery clears its own check error without clearing unrelated errors', async () => {
  let update = { ...failure, manifestSha256: null, version: null }
  const fixture = setup(() => update)
  try {
    await fixture.bindings.refresh()
    update = { status: 'current' }
    await fixture.bindings.refresh()
    assert.equal(fixture.state.error, '')
    update = { ...failure, manifestSha256: null, version: null }
    await fixture.bindings.refresh()
    fixture.state.error = 'unrelated operation failed'
    update = { status: 'current' }
    await fixture.bindings.refresh()
    assert.equal(fixture.state.error, 'unrelated operation failed')
  } finally { fixture.dispose() }
})

test('a discovery integrity failure uses the check-failure message', async () => {
  const fixture = setup(() => ({ ...failure, manifestSha256: null, version: null, failure: 'integrity' }))
  try {
    await fixture.bindings.refresh()
    assert.equal(fixture.state.error, 'launcherUpdates.reasons.unknown')
    assert.equal(fixture.bindings.open.value, false)
  } finally { fixture.dispose() }
})

test('changed discovery during manual-link resolution does not show a stale version', async () => {
  const fixture = setup(method => method === 'launcherUpdates.state' ? failure : { ...manual, version: null })
  try {
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.open.value, false)
  } finally { fixture.dispose() }
})

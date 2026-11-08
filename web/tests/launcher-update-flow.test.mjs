import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import * as vue from 'vue'

const source = readFileSync(new URL('../src/downloads/launcherUpdateFlow.ts', import.meta.url), 'utf8')
const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
const module = { exports: {} }
runInNewContext(javascript, { module, exports: module.exports, require: id => { assert.equal(id, 'vue'); return vue } })
const hash = 'a'.repeat(64), other = 'b'.repeat(64)
test('mandatory discovery opens immediately and cannot be dismissed before or after staging', async () => {
  const f = fixture(); f.update({ mandatory: true, deprecatedMaxVersion: '0.1.37' })
  await f.flow.refresh(); assert.equal(f.flow.state.dialog, 'download')
  f.flow.dismiss(); assert.equal(f.flow.state.dialog, 'download')
  await f.flow.accept(); f.flow.dismiss(); assert.equal(f.flow.state.dialog, 'progress')
  f.update({ status: 'ready' }); f.task({ stage: 'ready' }); await f.flow.refresh()
  assert.equal(f.flow.state.dialog, 'confirm'); f.flow.dismiss(); assert.equal(f.flow.state.dialog, 'confirm')
  assert.equal(f.calls.filter(call => call[0] === 'restart').length, 0)
})
test('mandatory transfer failure remains actionable and never starts an unverified installation', async () => {
  const f = fixture(); f.update({ mandatory: true }); await f.flow.refresh(); await f.flow.accept()
  f.update({ status: 'failed', failure: 'integrity' }); f.task({ stage: 'failed' }); await f.flow.refresh()
  assert.equal(f.flow.state.dialog, 'download'); f.flow.dismiss(); assert.equal(f.flow.state.dialog, 'download')
  await f.flow.install(); assert.equal(f.calls.filter(call => call[0] === 'restart').length, 0)
  await f.flow.accept(); assert.equal(f.calls.filter(call => call[0] === 'download').length, 2)
})
test('mandatory handoff failure requires an explicit retry and cannot dismiss its error', async () => {
  const f = fixture(); f.update({ mandatory: true, status: 'ready' }); f.task({ stage: 'ready' })
  f.transport.restart = async () => { f.calls.push(['restart', hash]); throw new Error('Handoff failed') }
  await f.flow.refresh(); await f.flow.install(); await f.flow.refresh(); f.flow.dismiss()
  assert.equal(f.flow.state.dialog, 'progress'); assert.equal(f.calls.length, 1)
  await f.flow.install(); assert.equal(f.calls.length, 2)
})
function fixture() {
  let update = { status: 'available', version: '0.1.13', revision: 2, manifestSha256: hash, failure: null }
  let task = { id: 'selected-update', stage: 'downloading', target: { kind: 'launcher' } }
  const calls = []
  const transport = {
    read: async () => update,
    download: async selected => { calls.push(['download', selected]); update = { ...update, status: 'downloading' }; return task },
    task: async () => task,
    restart: async selected => { calls.push(['restart', selected]) },
  }
  const flow = module.exports.createLauncherUpdateFlow(transport)
  return { flow, transport, calls, update(value) { update = { ...update, ...value } }, task(value) { task = { ...task, ...value } } }
}

test('discovery stays collapsed until the user opens the update entry', async () => {
  const f = fixture(); await f.flow.refresh()
  assert.equal(f.flow.state.dialog, null); assert.equal(f.flow.visible.value, true)
  assert.equal(f.calls.length, 0)
  assert.equal(f.calls.length, 0)
  f.flow.show(); assert.equal(f.flow.state.dialog, 'download')
})
test('dismissing progress keeps downloading alive but completed staging requires installation confirmation', async () => {
  const f = fixture(); await f.flow.refresh(); await f.flow.accept(); f.flow.dismiss()
  assert.equal(f.flow.active.value, true); assert.equal(f.flow.state.dialog, null)
  f.update({ status: 'ready' }); f.task({ stage: 'ready' }); await f.flow.refresh()
  assert.equal(f.flow.state.dialog, 'confirm'); assert.deepEqual(f.calls, [['download', hash]])
  await f.flow.install()
  assert.deepEqual(f.calls, [['download', hash], ['restart', hash]])
  await f.flow.refresh(); assert.equal(f.calls.length, 2)
})
test('automatic background download and ready state cannot restart without confirmation', async () => {
  const f = fixture(); f.update({ status: 'downloading' }); await f.flow.refresh(); f.flow.show()
  assert.equal(f.flow.state.dialog, 'progress')
  f.update({ status: 'ready' }); f.task({ stage: 'ready' }); await f.flow.refresh()
  assert.equal(f.calls.length, 0)
  await f.flow.install(); assert.deepEqual(f.calls, [['restart', hash]])
})

test('a restored ready update opens installation confirmation directly without downloading again', async () => {
  const f = fixture(); f.update({ status: 'ready' }); f.task({ stage: 'ready' })
  await f.flow.refresh()
  assert.equal(f.flow.state.dialog, 'confirm'); assert.equal(f.flow.canInstall.value, true)
  assert.equal(f.calls.length, 0)
  f.flow.dismiss(); await f.flow.refresh(); assert.equal(f.flow.state.dialog, null)
  f.flow.show(); assert.equal(f.flow.state.dialog, 'confirm')
  await f.flow.install(); assert.deepEqual(f.calls, [['restart', hash]])
})
test('a changed selection invalidates earlier restart consent', async () => {
  const f = fixture(); await f.flow.refresh(); await f.flow.accept()
  f.update({ status: 'ready', manifestSha256: other }); f.task({ stage: 'ready' }); await f.flow.refresh()
  assert.equal(f.flow.state.consent, ''); assert.equal(f.flow.state.dialog, 'confirm')
  assert.equal(f.calls.some(call => call[0] === 'restart'), false)
})
test('stale ready tasks cannot trigger handoff for the selected download', async () => {
  const f = fixture(); await f.flow.refresh(); await f.flow.accept()
  f.update({ status: 'ready' }); f.task({ id: 'another-task', stage: 'ready' }); await f.flow.refresh()
  await f.flow.install()
  assert.equal(f.calls.some(call => call[0] === 'restart'), false)
})
test('pause preserves consent but a canceled transfer revokes it', async () => {
  const f = fixture(); await f.flow.refresh(); await f.flow.accept()
  f.task({ stage: 'paused' }); await f.flow.refresh(); assert.equal(f.flow.active.value, true)
  f.update({ status: 'available' }); f.task({ stage: 'canceled' }); await f.flow.refresh()
  assert.equal(f.flow.state.consent, ''); assert.equal(f.flow.active.value, false)
})
test('handoff failure disarms automatic retries and remains actionable', async () => {
  const f = fixture(); f.transport.restart = async () => { f.calls.push(['restart', hash]); throw new Error('Fixture handoff failed') }
  await f.flow.refresh(); await f.flow.accept(); f.update({ status: 'ready' }); f.task({ stage: 'ready' })
  await f.flow.refresh(); await f.flow.install(); await f.flow.refresh()
  assert.equal(f.calls.filter(call => call[0] === 'restart').length, 1)
  assert.equal(f.flow.state.consent, ''); assert.equal(f.flow.state.restarting, false)
  assert.equal(f.flow.state.dialog, 'progress'); assert.match(f.flow.state.error, /Fixture handoff failed/)
  f.flow.show(); assert.equal(f.flow.state.dialog, 'confirm')
})
test('a delayed state poll cannot overwrite new consent or a dismissed progress dialog', async () => {
  const f = fixture(); await f.flow.refresh()
  let release; f.transport.read = () => new Promise(resolve => { release = resolve })
  const pending = f.flow.refresh(); await f.flow.accept(); f.flow.dismiss()
  release({ status: 'ready', version: 'Stale', manifestSha256: other }); await pending
  assert.equal(f.flow.state.consent, hash); assert.equal(f.flow.state.dialog, null)
  assert.equal(f.calls.some(call => call[0] === 'restart'), false)
})
test('stopped polling and a delayed download cannot restart or reopen the workspace', async () => {
  const f = fixture(); await f.flow.refresh()
  let release; f.transport.download = () => new Promise(resolve => { release = resolve })
  const accepting = f.flow.accept(); f.flow.stop(); release({ id: 'selected-update', stage: 'ready' }); await accepting
  await f.flow.refresh(); assert.equal(f.calls.length, 0); assert.equal(f.flow.state.restarting, false)
})
test('verification failure releases consent without a success or restart indication', async () => {
  const f = fixture(); await f.flow.refresh(); await f.flow.accept()
  f.update({ status: 'failed', failure: 'integrity' }); f.task({ stage: 'failed' }); await f.flow.refresh()
  assert.equal(f.flow.state.consent, ''); assert.equal(f.flow.state.dialog, null)
  assert.equal(f.calls.some(call => call[0] === 'restart'), false)
})
test('canceling the installation prompt leaves staged files ready and never restarts', async () => {
  const f = fixture(); await f.flow.refresh(); await f.flow.accept()
  f.update({ status: 'ready' }); f.task({ stage: 'ready' }); await f.flow.refresh()
  f.flow.dismiss(); await f.flow.refresh(); await f.flow.refresh()
  assert.equal(f.flow.state.dialog, null); assert.equal(f.calls.length, 1)
  f.flow.show(); assert.equal(f.flow.state.dialog, 'confirm')
  await f.flow.install(); assert.deepEqual(f.calls, [['download', hash], ['restart', hash]])
})
test('installation cannot begin while transfer or verification is still pending', async () => {
  const f = fixture(); await f.flow.refresh(); await f.flow.accept(); await f.flow.install()
  assert.equal(f.calls.length, 1); assert.equal(f.flow.canInstall.value, false)
  f.update({ status: 'ready' }); f.task({ stage: 'verifying' }); await f.flow.refresh(); await f.flow.install()
  assert.equal(f.calls.length, 1); assert.equal(f.flow.canInstall.value, false)
})

test('installation progress cannot be dismissed during handoff, but failure unlocks dismissal', async () => {
  const f = fixture(); await f.flow.refresh(); await f.flow.accept()
  f.update({ status: 'ready' }); f.task({ stage: 'ready' }); await f.flow.refresh()
  let fail; f.transport.restart = () => new Promise((resolve, reject) => { fail = reject })
  const installing = f.flow.install()
  assert.equal(f.flow.state.restarting, true)
  f.flow.dismiss(); assert.equal(f.flow.state.dialog, 'progress')
  fail(new Error('Fixture handoff failed')); await installing
  assert.equal(f.flow.state.restarting, false)
  f.flow.dismiss(); assert.equal(f.flow.state.dialog, null)
})


test('background completion prompts once even when the progress surface was never opened', async () => {
  const f = fixture(); f.update({ status: 'downloading' }); await f.flow.refresh()
  assert.equal(f.flow.state.dialog, null)
  f.update({ status: 'ready' }); f.task({ stage: 'ready' }); await f.flow.refresh()
  assert.equal(f.flow.state.dialog, 'confirm'); assert.equal(f.calls.length, 0)
  f.flow.dismiss(); await f.flow.refresh(); await f.flow.refresh()
  assert.equal(f.flow.state.dialog, null); assert.equal(f.flow.canInstall.value, true)
  f.update({ manifestSha256: other }); await f.flow.refresh()
  assert.equal(f.flow.state.dialog, 'confirm'); assert.equal(f.calls.length, 0)
})

test('completion notification waits for verification and does not accept a missing task', async () => {
  const f = fixture(); f.update({ status: 'ready' }); f.task({ stage: 'verifying' })
  await f.flow.refresh(); assert.equal(f.flow.state.dialog, null)
  const readTask = f.transport.task; f.transport.task = async () => null
  await f.flow.refresh(); assert.equal(f.flow.state.dialog, null)
  f.transport.task = readTask; f.task({ stage: 'ready' })
  await f.flow.refresh(); assert.equal(f.flow.state.dialog, 'confirm')
  assert.equal(f.calls.length, 0)
})

test('completion replaces an open progress dialog without starting installation', async () => {
  const f = fixture(); await f.flow.refresh(); await f.flow.accept()
  assert.equal(f.flow.state.dialog, 'progress')
  f.update({ status: 'ready' }); f.task({ stage: 'ready' }); await f.flow.refresh()
  assert.equal(f.flow.state.dialog, 'confirm')
  assert.deepEqual(f.calls, [['download', hash]])
})

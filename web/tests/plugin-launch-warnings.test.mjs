import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import { reactive, watch, ref } from 'vue'

function setup(call) {
  const module = { exports: {} }, tasks = ref([])
  const source = readFileSync(new URL('../src/plugins/launchWarnings.ts', import.meta.url), 'utf8')
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const require = id => {
    if (id === 'vue') return { reactive, watch }
    if (id === '../downloads/live') return { downloadTasks: tasks, trackDownload: task => { tasks.value = [...tasks.value, task] }, downloadAction: async () => {} }
    if (id === '../bridge') return { call, inHost: true }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require })
  return { ...module.exports, tasks }
}
const item = { key: 'fixture.auth@1.0.0', name: { en: 'Authentication' }, warning: { en: 'Disabled' } }
const flush = async () => { for (let i = 0; i < 10; i++) await Promise.resolve() }

test('enabled components do not show a warning', async () => {
  const fixture = setup(async () => [])
  assert.deepEqual([...await fixture.requestLaunchWarnings('one')], [])
  assert.equal(fixture.launchWarningPrompt.open, false)
})

test('cancel blocks launch and confirming applies only to this request', async () => {
  let calls = 0
  const fixture = setup(async () => { calls++; return [item] })
  const first = fixture.requestLaunchWarnings('one'); await flush()
  assert.equal(fixture.launchWarningPrompt.open, true)
  fixture.finishLaunchWarnings(false)
  assert.equal(await first, null)
  const second = fixture.requestLaunchWarnings('one'); await flush()
  fixture.finishLaunchWarnings(true)
  assert.deepEqual([...await second], [item.key])
  const third = fixture.requestLaunchWarnings('one'); await flush()
  assert.equal(fixture.launchWarningPrompt.open, true)
  fixture.finishLaunchWarnings(false)
  assert.equal(await third, null)
  assert.equal(calls, 3)
})

test('a second client cannot reuse an open confirmation', async () => {
  const fixture = setup(async () => [item])
  const first = fixture.requestLaunchWarnings('one'); await flush()
  assert.equal(await fixture.requestLaunchWarnings('two'), null)
  fixture.finishLaunchWarnings(true)
  assert.deepEqual([...await first], [item.key])
})

test('failed warning lookup cannot become launch authorization', async () => {
  const fixture = setup(async () => { throw new Error('definition invalid') })
  await assert.rejects(fixture.requestLaunchWarnings('one'), /definition invalid/)
  assert.equal(fixture.launchWarningPrompt.open, false)
})


test('enable and continue waits for installation and verifies all components before authorizing launch', async () => {
  let enabled = false
  const component = { ...item, id: 'fixture.auth', version: '1.0.0', installed: false }
  const fixture = setup(async method => {
    if (method === 'plugins.launchWarnings') return enabled ? [] : [component]
    if (method === 'plugins.installationComponents') return [{ id: 'download', stage: 'downloading', target: {} }]
    throw new Error(method)
  })
  const pending = fixture.requestLaunchWarnings('one', 'server'); await flush()
  await fixture.enableLaunchWarnings()
  assert.equal(fixture.launchWarningPrompt.open, true)
  assert.equal(fixture.launchWarningPrompt.tasks.length, 1)
  enabled = true
  fixture.tasks.value = [{ id: 'download', stage: 'completed', target: {} }]
  await flush()
  assert.deepEqual([...await pending], [component.key])
  assert.equal(fixture.launchWarningPrompt.open, false)
})

test('failed enable stays in the dialog and cancellation never authorizes the pending launch', async () => {
  const fixture = setup(async method => {
    if (method === 'plugins.launchWarnings') return [item]
    throw new Error('Source unavailable')
  })
  const pending = fixture.requestLaunchWarnings('one'); await flush()
  await fixture.enableLaunchWarnings()
  assert.equal(fixture.launchWarningPrompt.error, 'Source unavailable')
  assert.equal(fixture.launchWarningPrompt.open, true)
  fixture.finishLaunchWarnings(false)
  assert.equal(await pending, null)
})

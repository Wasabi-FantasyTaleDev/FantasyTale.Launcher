import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import { reactive } from 'vue'

const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve() }
const update = { key: 'auth@1->2', id: 'auth', name: { en: 'Authentication' }, installedVersion: '1.0.0', version: '2.0.0', task: { id: 'transfer', stage: 'downloading', files: [{ size: 100, received: 25 }] } }
function setup(read) {
  const module = { exports: {} }, timers = new Map()
  let sequence = 0, calls = 0
  const source = readFileSync(new URL('../src/plugins/launchUpdates.ts', import.meta.url), 'utf8')
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const require = id => {
    if (id === 'vue') return { reactive }
    if (id === '../bridge') return { inHost: true, call: async method => {
      assert.equal(method, 'plugins.launchUpdates'); calls++; return read()
    } }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require,
    setTimeout: callback => { const id = ++sequence; timers.set(id, callback); return id }, clearTimeout: id => timers.delete(id) })
  return { ...module.exports, get calls() { return calls }, async poll() { const pending = [...timers.values()]; timers.clear(); for (const callback of pending) await callback(); await flush() } }
}

test('unknown versions never wait for background source discovery', async () => {
  const fixture = setup(() => ({ checking: true, items: [] }))
  const answer = await fixture.requestRequiredUpdates('client', 'server')
  assert.equal(answer.keys.length, 0); assert.equal(fixture.calls, 1)
  assert.equal(fixture.requiredUpdatePrompt.open, false)
})

test('known updates show live progress and confirmation authorizes only the displayed versions', async () => {
  let items = [update]
  const fixture = setup(() => ({ checking: false, items }))
  const result = fixture.requestRequiredUpdates('client', 'server'); await flush()
  assert.equal(fixture.requiredUpdatePrompt.open, true)
  assert.equal(fixture.requiredUpdatePrompt.items[0].task.files[0].received, 25)
  items = [{ ...update, task: { ...update.task, files: [{ size: 100, received: 75 }] } }]
  await fixture.poll()
  assert.equal(fixture.requiredUpdatePrompt.items[0].task.files[0].received, 75)
  fixture.finishRequiredUpdates(true)
  const answer = await result
  assert.deepEqual([...answer.keys], ['auth@1->2']); assert.deepEqual([...answer.tasks], ['transfer'])
})

for (const stage of ['failed', 'paused', 'canceled']) test(`${stage} transfer still allows explicit use of the installed version`, async () => {
  const fixture = setup(() => ({ checking: false, items: [{ ...update, task: { ...update.task, stage } }] }))
  const result = fixture.requestRequiredUpdates('client', 'server'); await flush()
  fixture.finishRequiredUpdates(true)
  assert.deepEqual([...(await result).keys], [update.key])
})

test('multiple mandatory updates remain separate cards; cancel cannot authorize launch', async () => {
  const fixture = setup(() => ({ checking: false, items: [update, { ...update, id: 'other', key: 'other@1->2', task: null }] }))
  const result = fixture.requestRequiredUpdates('client', 'server'); await flush()
  assert.equal(fixture.requiredUpdatePrompt.items.length, 2)
  assert.equal(await fixture.requestRequiredUpdates('other-client', 'server'), null)
  fixture.finishRequiredUpdates(false); assert.equal(await result, null)
})

test('applied updates dismiss the warning and resume launch without stale confirmations', async () => {
  let items = [update]
  const fixture = setup(() => ({ checking: false, items }))
  const result = fixture.requestRequiredUpdates('client', 'server'); await flush()
  items = []; await fixture.poll()
  const answer = await result
  assert.equal(answer.keys.length, 0); assert.equal(answer.tasks.length, 0)
  assert.equal(fixture.requiredUpdatePrompt.open, false)
})

test('selection change during local lookup cannot open a stale dialog', async () => {
  let release
  const fixture = setup(() => new Promise(resolve => { release = resolve }))
  const result = fixture.requestRequiredUpdates('client', 'server'); await flush()
  fixture.finishRequiredUpdates(false); release({ checking: false, items: [update] })
  assert.equal(await result, null); assert.equal(fixture.requiredUpdatePrompt.open, false)
})

test('a canceled prompt cannot be revived by an in-flight progress poll', async () => {
  let release, waiting = false
  const fixture = setup(() => waiting ? new Promise(resolve => { release = resolve }) : { checking: false, items: [update] })
  const result = fixture.requestRequiredUpdates('client', 'server'); await flush()
  waiting = true; const poll = fixture.poll(); await flush()
  fixture.finishRequiredUpdates(false); release({ checking: false, items: [update] }); await poll
  assert.equal(await result, null); assert.equal(fixture.requiredUpdatePrompt.open, false)
})

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import { reactive, watch, ref } from 'vue'

function setup(call, refresh = async () => {}) {
  const downloads = [], downloadTasks = ref([]), module = { exports: {} }
  const source = readFileSync(new URL('../src/plugins/recommendations.ts', import.meta.url), 'utf8')
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const require = id => {
    if (id === 'vue') return { reactive, watch }
    if (id === '../bridge') return { call, inHost: true }
    if (id === '../downloads/live') return { downloadTasks, trackDownload: task => { downloads.push(task); downloadTasks.value = [...downloadTasks.value, task] }, downloadAction: async task => { downloadTasks.value = downloadTasks.value.map(item => item.id === task.id ? { ...item, stage: 'queued' } : item) } }
    if (id === '../state') return { refreshInstances: refresh }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require })
  return { ...module.exports, downloads, downloadTasks }
}
const recommendation = { id: 'fixture.font', name: { en: 'Font weight' }, required: false, defaultSelected: true }
const flush = async () => { for (let i = 0; i < 10; i++) await Promise.resolve() }

test('recommended cards remain visible while omitted and false defaults stay unchecked', async () => {
  const offered = [recommendation, { ...recommendation, id: 'unchecked', defaultSelected: false },
    { id: 'omitted', name: { en: 'Optional' }, required: false }, { ...recommendation, id: 'required', required: true, defaultSelected: false }]
  const calls = [], fixture = setup(async (method, args) => {
    calls.push({ method, args })
    return method === 'clientPolicy.claimPlugins' ? offered : []
  })
  const pending = fixture.requestRecommendations('one', 'One')
  await flush()
  assert.deepEqual(fixture.recommendationPrompt.items.map(item => item.id), offered.map(item => item.id))
  assert.deepEqual([...fixture.recommendationPrompt.selected], [recommendation.id, 'required'])
  fixture.recommendationPrompt.selected = ['omitted']
  await fixture.acceptRecommendations()
  assert.deepEqual([...calls.at(-1).args.ids], ['omitted', 'required'])
  assert.equal(await pending, true)
})

test('empty recommendations proceed without opening a dialog', async () => {
  const fixture = setup(async () => [])
  assert.equal(await fixture.requestRecommendations('one', 'One'), true)
  assert.equal(fixture.recommendationPrompt.open, false)
})

test('duplicate requests share one claim and cancellation lets the requested launch continue', async () => {
  const calls = [], fixture = setup(async (method, args) => { calls.push({ method, args }); return [recommendation] })
  const first = fixture.requestRecommendations('one', 'One', 'server')
  assert.equal(fixture.requestRecommendations('one', 'One', 'server'), first)
  await flush()
  assert.equal(calls.length, 1)
  assert.equal(calls[0].args.serverId, 'server')
  assert.deepEqual([...fixture.recommendationPrompt.selected], ['fixture.font'])
  fixture.dismissRecommendations()
  assert.equal(await first, true)
})

test('independent client prompts serialize and capture their original server', async () => {
  const fixture = setup(async () => [recommendation])
  const first = fixture.requestRecommendations('one', 'One', 'server-one')
  const second = fixture.requestRecommendations('two', 'Two', 'server-two')
  await flush()
  assert.equal(fixture.recommendationPrompt.instanceId, 'one')
  fixture.dismissRecommendations(); await first; await flush()
  assert.equal(fixture.recommendationPrompt.instanceId, 'two')
  assert.equal(fixture.recommendationPrompt.serverId, 'server-two')
  fixture.dismissRecommendations(); assert.equal(await second, true)
})

test('download acceptance tracks durable tasks and defers launch until installation finishes', async () => {
  const calls = [], task = { id: 'download', stage: 'downloading' }
  const fixture = setup(async (method, args) => { calls.push({ method, args }); return method === 'clientPolicy.claimPlugins' ? [recommendation] : [task] })
  const first = fixture.requestRecommendations('one', 'One', 'server')
  await flush(); await fixture.acceptRecommendations()
  assert.equal(fixture.recommendationPrompt.open, true)
  assert.equal(fixture.downloads[0], task)
  fixture.downloadTasks.value = [{ ...task, stage: 'completed' }]
  await flush()
  assert.equal(await first, true)
  assert.equal(fixture.recommendationPrompt.open, false)
  assert.equal(calls[1].args.instanceId, 'one')
  assert.equal(calls[1].args.serverId, 'server')
})

test('failed acceptance remains open and retry preserves the selection', async () => {
  let attempts = 0
  const fixture = setup(async method => {
    if (method === 'clientPolicy.claimPlugins') return [recommendation]
    if (++attempts === 1) throw new Error('source unavailable')
    return []
  })
  const first = fixture.requestRecommendations('one', 'One')
  await flush(); await fixture.acceptRecommendations()
  assert.equal(fixture.recommendationPrompt.open, true)
  assert.equal(fixture.recommendationPrompt.busy, false)
  assert.equal(fixture.recommendationPrompt.error, 'source unavailable')
  await fixture.acceptRecommendations(); assert.equal(await first, true)
})


test('mandatory recommendations cannot proceed through cancellation', async () => {
  const fixture = setup(async () => [{ ...recommendation, required: true }])
  const request = fixture.requestRecommendations('one', 'One')
  await flush()
  fixture.dismissRecommendations()
  assert.equal(await request, false)
})

test('mandatory recommendations are included even if a draft loses its selection', async () => {
  const calls = [], fixture = setup(async (method, args) => {
    calls.push({ method, args })
    return method === 'clientPolicy.claimPlugins' ? [{ ...recommendation, required: true }] : []
  })
  const request = fixture.requestRecommendations('one', 'One')
  await flush()
  fixture.recommendationPrompt.selected = []
  await fixture.acceptRecommendations()
  assert.equal(await request, true)
  assert.deepEqual([...calls[1].args.ids], ['fixture.font'])
})


test('an already completed admission snapshot releases launch without another poll', async () => {
  const fixture = setup(async method => method === 'clientPolicy.claimPlugins' ? [recommendation] : [{ id: 'done', stage: 'completed' }])
  const request = fixture.requestRecommendations('one', 'One')
  await flush(); await fixture.acceptRecommendations(); await flush()
  assert.equal(await request, true)
})

test('retry resumes the admitted task instead of admitting another plugin package', async () => {
  let admissions = 0
  const fixture = setup(async method => {
    if (method === 'clientPolicy.claimPlugins') return [recommendation]
    admissions++; return [{ id: 'failed', stage: 'failed' }]
  })
  const request = fixture.requestRecommendations('one', 'One')
  await flush(); await fixture.acceptRecommendations(); await fixture.acceptRecommendations()
  assert.equal(admissions, 1)
  assert.equal(fixture.downloadTasks.value[0].stage, 'queued')
  fixture.dismissRecommendations(); assert.equal(await request, false)
})


test('a dismissed pending admission tracks its transfer without closing the next prompt', async () => {
  let complete
  const admission = new Promise(resolve => { complete = resolve })
  const fixture = setup(async method => method === 'clientPolicy.claimPlugins' ? [recommendation] : admission)
  const first = fixture.requestRecommendations('one', 'One')
  await flush()
  const accepting = fixture.acceptRecommendations()
  fixture.dismissRecommendations()
  assert.equal(await first, false)
  const second = fixture.requestRecommendations('two', 'Two')
  await flush()
  complete([{ id: 'old-admission', stage: 'downloading' }])
  await accepting
  assert.equal(fixture.downloads[0].id, 'old-admission')
  assert.equal(fixture.recommendationPrompt.instanceId, 'two')
  assert.equal(fixture.recommendationPrompt.open, true)
  assert.equal(fixture.recommendationPrompt.tasks.length, 0)
  fixture.dismissRecommendations(); await second
})

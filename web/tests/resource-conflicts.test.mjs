import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { test } from 'node:test'
import ts from 'typescript'
import { reactive } from 'vue'

function setup() {
  const module = { exports: {} }
  const source = readFileSync(new URL('../src/plugins/resourceConflicts.ts', import.meta.url), 'utf8')
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  runInNewContext(javascript, { module, exports: module.exports, require: id => { assert.equal(id, 'vue'); return { reactive } } })
  return module.exports
}
const conflict = id => ({ id, code: 'resource.conflict.field-value', member: 'table.xml', key: '7', field: 'name', parties: [
  { pluginId: 'one', version: '1.0.0', name: { values: { en: 'One' } } },
  { pluginId: 'two', version: '1.0.0', name: { values: { en: 'Two' } } },
] })
const report = (snapshot = 'snapshot', conflicts = [conflict('first')]) => ({ snapshot, build: 'test-1', language: 'zh-CN', conflicts })
const flush = async () => { for (let i = 0; i < 10; i++) await Promise.resolve() }
const plain = value => JSON.parse(JSON.stringify(value))

test('structured bridge error keeps conflict report and rejects incomplete reports', () => {
  const fixture = setup()
  assert.ok(fixture.resourceConflictError({ code: 'resource_conflict', conflictReport: report() }) instanceof fixture.ResourceConflictError)
  assert.equal(fixture.resourceConflictError({ code: 'other', conflictReport: report() }), undefined)
  assert.equal(fixture.resourceConflictError({ code: 'resource_conflict', conflictReport: report('x', []) }), undefined)
})

test('every group requires an explicit valid plugin choice before retry', async () => {
  const fixture = setup(), requests = []
  const pending = fixture.launchWithResourceChoices(async choices => {
    requests.push(plain(choices))
    if (!choices.length) throw new fixture.ResourceConflictError(report())
    return { processId: 17 }
  }, () => true)
  await flush()
  fixture.finishResourceConflicts(true)
  assert.equal(fixture.resourceConflictPrompt.open, true)
  fixture.resourceConflictPrompt.selected.first = 'not-a-party'
  fixture.finishResourceConflicts(true)
  assert.equal(requests.length, 1)
  fixture.resourceConflictPrompt.selected.first = 'two'
  fixture.finishResourceConflicts(true)
  assert.deepEqual(plain(await pending), { processId: 17 })
  assert.deepEqual(requests[1], [{ snapshot: 'snapshot', conflictId: 'first', keepPluginId: 'two' }])
})

test('cancel or expired launch never retries', async () => {
  for (const expired of [false, true]) {
    const fixture = setup(); let valid = true, requests = 0
    const pending = fixture.launchWithResourceChoices(async () => { requests++; throw new fixture.ResourceConflictError(report()) }, () => valid)
    await flush()
    fixture.resourceConflictPrompt.selected.first = 'one'
    valid = !expired
    fixture.finishResourceConflicts(expired)
    assert.deepEqual(plain(await pending), { cancelled: true })
    assert.equal(requests, 1)
  }
})

test('staged conflicts retain same-snapshot choices and discard choices after input changes', async () => {
  const fixture = setup(), requests = []
  const reports = [report(), report('snapshot', [conflict('second')]), report('changed')]
  const pending = fixture.launchWithResourceChoices(async choices => {
    requests.push(plain(choices))
    if (reports.length) throw new fixture.ResourceConflictError(reports.shift())
    return { processId: 18 }
  }, () => true)
  for (const id of ['first', 'second', 'first']) {
    await flush()
    fixture.resourceConflictPrompt.selected[id] = 'one'
    fixture.finishResourceConflicts(true)
  }
  await pending
  assert.equal(requests[2].length, 2)
  assert.deepEqual(requests[3], [{ snapshot: 'changed', conflictId: 'first', keepPluginId: 'one' }])
})

test('ordinary launch failures stay failures', async () => {
  const fixture = setup()
  await assert.rejects(fixture.launchWithResourceChoices(async () => { throw new Error('network') }, () => true), /network/)
  assert.equal(fixture.resourceConflictPrompt.open, false)
})

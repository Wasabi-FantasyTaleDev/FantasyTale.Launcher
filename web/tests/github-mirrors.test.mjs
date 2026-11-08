import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

function pending() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const preferences = { ipPreference: 'china', gitHubMirror: false, gitHubMirrorSource: 'auto', defaultPending: false }
const overview = { providers: [{ id: 'one', name: 'One' }], results: [] }
function setup(call, component = 'GitHubMirrorSettings.vue', values = {}) {
  const unmount = [], scope = vue.effectScope(), timers = new Map()
  const source = compileScript(parse(readFileSync(new URL(`../src/components/${component}`, import.meta.url), 'utf8')).descriptor, { id: 'Mirrors' }).content
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../bridge') return { call }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require,
    setTimeout(callback) { const id = Symbol(); timers.set(id, callback); return id }, clearTimeout(id) { timers.delete(id) } })
  const props = vue.reactive(values)
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {} }))
  return { bindings, props, timers, dispose() { unmount.splice(0).forEach(callback => callback()); scope.stop() } }
}

test('a delayed country-default response cannot replace a manually selected mirror', async () => {
  const old = pending(), chosen = { ...preferences, gitHubMirror: true, gitHubMirrorSource: 'one' }
  const fixture = setup(method => method === 'settings.network' ? old.promise : Promise.resolve(chosen))
  try {
    const load = fixture.bindings.loadPreferences()
    await fixture.bindings.save({ githubMirror: true, githubMirrorSource: 'one' })
    old.resolve({ ...preferences, defaultPending: true })
    await load
    assert.equal(fixture.bindings.preferences.value.gitHubMirrorSource, 'one')
    assert.equal(fixture.bindings.preferences.value.gitHubMirror, true)
    assert.equal(fixture.timers.size, 0)
  } finally { fixture.dispose() }
})

test('a stale overview poll cannot overwrite a completed measurement or restart polling', async () => {
  const old = pending(), measurement = pending()
  let polls = 0
  const complete = { ...overview, results: [{ id: 'one', state: 'available', milliseconds: 20, bytesPerSecond: 1000 }] }
  const fixture = setup(method => method === 'settings.testGitHubMirrors' ? measurement.promise : ++polls === 1 ? old.promise : Promise.resolve(complete))
  try {
    const testRun = fixture.bindings.testMirrors()
    measurement.resolve(complete.results)
    await testRun
    old.resolve(overview)
    await Promise.resolve(); await Promise.resolve()
    assert.equal(fixture.bindings.overview.value.results[0].state, 'available')
    assert.equal(fixture.bindings.testing.value, false)
    assert.equal(fixture.timers.size, 0)
  } finally { fixture.dispose() }
})

test('opening the speed dialog again shares the active measurement instead of launching another', async () => {
  const measurement = pending(); let tests = 0
  const fixture = setup(method => {
    if (method === 'settings.testGitHubMirrors') { tests++; return measurement.promise }
    return Promise.resolve(overview)
  })
  try {
    const testRun = fixture.bindings.testMirrors()
    fixture.bindings.dialog.value = false
    await fixture.bindings.testMirrors()
    assert.equal(tests, 1)
    assert.equal(fixture.bindings.dialog.value, true)
    measurement.resolve([])
    await testRun
    assert.equal(fixture.timers.size, 0)
  } finally { fixture.dispose() }
})

test('leaving settings cancels UI polling and ignores later network results', async () => {
  const measurement = pending(), old = pending()
  const fixture = setup(method => method === 'settings.testGitHubMirrors' ? measurement.promise : old.promise)
  const testRun = fixture.bindings.testMirrors()
  fixture.dispose()
  measurement.resolve([{ id: 'one', state: 'available' }]); old.resolve(overview)
  await testRun
  assert.equal(fixture.bindings.overview.value.providers.length, 0)
  assert.equal(fixture.timers.size, 0)
})

test('the country suggestion waits for the shell and keeps the user switch choice during polling', async () => {
  const fixture = setup(() => Promise.resolve({ ...preferences, suggestionPending: true }), 'GitHubMirrorSuggestionDialog.vue', { ready: false })
  try {
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.open.value, false)
    assert.equal(fixture.bindings.enabled.value, true)
    fixture.props.ready = true
    assert.equal(fixture.bindings.open.value, true)
    fixture.bindings.enabled.value = false
    await fixture.bindings.refresh()
    assert.equal(fixture.bindings.enabled.value, false)
    assert.equal(fixture.bindings.open.value, true)
  } finally { fixture.dispose() }
})

test('the first-run confirmation saves the selected switch and ignores stale pending results', async () => {
  const old = pending(), writes = []
  let reads = 0
  const fixture = setup((method, params) => {
    if (method === 'settings.network') return ++reads === 1 ? Promise.resolve({ ...preferences, suggestionPending: true }) : old.promise
    if (method === 'settings.showGitHubMirrorSuggestion') return Promise.resolve({ ...preferences, suggestionPending: false })
    writes.push(params.githubMirror)
    return Promise.resolve({ ...preferences, suggestionPending: false })
  }, 'GitHubMirrorSuggestionDialog.vue', { ready: true })
  try {
    await fixture.bindings.refresh()
    const late = fixture.bindings.refresh()
    await fixture.bindings.save()
    old.resolve({ ...preferences, suggestionPending: true })
    await late
    assert.deepEqual(writes, [true])
    assert.equal(fixture.bindings.open.value, false)
    assert.equal(fixture.timers.size, 0)
  } finally { fixture.dispose() }
})

test('confirming with the suggestion switch off persists disabled', async () => {
  const writes = []
  const fixture = setup((method, params) => {
    if (method === 'settings.network') return Promise.resolve({ ...preferences, suggestionPending: true })
    if (method === 'settings.showGitHubMirrorSuggestion') return Promise.resolve({ ...preferences, suggestionPending: false })
    writes.push(params.githubMirror)
    return Promise.resolve({ ...preferences, suggestionPending: false })
  }, 'GitHubMirrorSuggestionDialog.vue', { ready: true })
  try {
    await fixture.bindings.refresh()
    fixture.bindings.enabled.value = false
    await fixture.bindings.save()
    assert.deepEqual(writes, [false])
    assert.equal(fixture.bindings.open.value, false)
  } finally { fixture.dispose() }
})

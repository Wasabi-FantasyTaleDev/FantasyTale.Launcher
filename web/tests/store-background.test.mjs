import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import test from 'node:test'
import ts from 'typescript'
import * as vue from 'vue'
import { createStoreSession } from '../src/store/session.ts'
import { groups, updates } from './store-update-helpers.mjs'
const { groupStorePackages } = groups
const { storePluginUpdates } = updates

function fixture() {
  const state = vue.reactive({ plugins: [], games: [{ id: 'game', status: 'enabled', supportsPlugins: true, builds: [{ id: 'build' }] }] })
  const downloads = vue.reactive({ tasks: [] })
  const sources = ['first', 'mirror'].map(id => ({ id, enabled: true, baseUrl: 'https://example.invalid/' }))
  const events = new Map()
  let inventory = [{ id: 'one', game: 'game', version: '1.0.0' }], fail = false, interval, calls = 0
  const packages = [{ id: 'one', game: 'game', kind: 'plugin', version: '2.0.0', builds: [] }]
  const require = id => {
    if (id === 'vue') return vue
    if (id === '../state') return { state }
    if (id === '../downloads/live') return { downloads }
    if (id === '../i18n') return { i18n: { global: { t: key => key } } }
    if (id === './groups') return { groupStorePackages }
    if (id === './updates') return { storePluginUpdates }
    if (id === './session') return { storeSession: (_key, loader) => createStoreSession(loader) }
    if (id === '../bridge') return { inHost: true, on: (event, callback) => { events.set(event, callback); return () => events.delete(event) }, call: async method => {
      calls++
      if (fail) throw new Error('Offline')
      if (method === 'store.sources') return sources
      if (method === 'store.catalog') return { packages }
      if (method === 'plugins.list') return inventory.map(plugin => ({ ...plugin }))
      throw new Error(method)
    } }
    throw new Error(id)
  }
  const module = { exports: {} }, scope = vue.effectScope()
  runInNewContext(ts.transpileModule(readFileSync(new URL('../src/store/background.ts', import.meta.url), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { module, exports: module.exports, require, window: { setInterval: callback => { interval = callback; return 1 } }, clearInterval: () => { interval = undefined } })
  const stop = scope.run(() => module.exports.startStoreUpdateChecks())
  return { ...module.exports, state, downloads, sources, tick: () => new Promise(resolve => setImmediate(resolve)),
    emitChanged: () => events.get('plugins.changed')?.(),
    update: () => { inventory = [{ id: 'one', game: 'game', version: '2.0.0' }] },
    fail: () => { fail = true }, refresh: () => interval?.(), calls: () => calls,
    stop: () => { stop(); scope.stop() } }
}

test('shell discovers distinct plugin updates without opening Store and reacts to completed downloads', async () => {
  const f = fixture()
  try {
    await f.tick()
    assert.equal(f.availableStoreUpdates.value.length, 1)
    assert.equal(f.liveStoreSession.state.sources.length, 2)
    f.update(); f.downloads.tasks.push({ id: 'installed', target: { kind: 'plugin' }, stage: 'completed' })
    await f.tick()
    assert.equal(f.availableStoreUpdates.value.length, 0)
    f.state.games[0].status = 'disabled'
    assert.equal(f.availableStoreUpdates.value.length, 0)
  } finally { f.stop() }
})

test('background failures preserve known results and disposal stops refresh work', async () => {
  const f = fixture()
  await f.tick(); f.fail(); f.refresh(); await f.tick()
  assert.equal(f.availableStoreUpdates.value.length, 1)
  assert.equal(f.liveStoreSession.state.loadError, 'Offline')
  f.stop(); const before = f.calls(); f.refresh()
  f.downloads.tasks.push({ id: 'done', target: { kind: 'plugin' }, stage: 'completed' }); await f.tick()
  assert.equal(f.calls(), before)
})


test('late activation events clear stale home and navigation badges even when the completed task list no longer changes', async () => {
  const f = fixture()
  try {
    await f.tick()
    f.downloads.tasks.push({ id: 'automatic', target: { kind: 'plugin' }, stage: 'completed' })
    await f.tick()
    assert.equal(f.availableStoreUpdates.value.length, 1)
    f.update(); f.emitChanged(); await f.tick()
    assert.equal(f.availableStoreUpdates.value.length, 0)
  } finally { f.stop() }
})

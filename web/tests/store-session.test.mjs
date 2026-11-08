import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createStoreSession, storeSession } from '../src/store/session.ts'
const source = (url = 'https://example.invalid/') => ({ id: 'first', enabled: true, kind: 'http', baseUrl: url, catalogPath: 'catalog.toml' })
const catalog = (...ids) => ({ id: 'fixture', packages: ids.map(id => ({ id })) })
function pending() { let resolve; const promise = new Promise(yes => { resolve = yes }); return { promise, resolve } }
const tick = () => new Promise(resolve => setImmediate(resolve))
const loader = () => ({ sources: async () => [source()], catalog: async () => catalog('old'), plugins: async () => [], error: error => error.message })

test('revisit immediately retains results during background refresh', async () => {
 const data = loader(), session = storeSession('cached-fixture', data)
 assert.equal(session.state.loaded, false); await session.refresh()
 const response = pending(); data.catalog = () => response.promise
 const revisit = storeSession('cached-fixture', loader()); assert.equal(revisit, session)
 const operation = revisit.refresh(); await tick()
 assert.equal(session.state.loading, true); assert.equal(session.state.loaded, true)
 assert.equal(session.state.catalogs.first.packages[0].id, 'old')
 response.resolve(catalog('old', 'new')); await operation
 assert.deepEqual(session.state.catalogs.first.packages.map(item => item.id), ['old', 'new'])
})
test('refresh errors retain catalog and inventory', async () => {
 const data = loader(); data.plugins = async () => [{ id: 'local' }]
 const session = createStoreSession(data); await session.refresh(); await tick()
 data.catalog = async () => { throw new Error('Catalog unavailable') }
 data.plugins = async () => { throw new Error('Inventory unavailable') }
 await session.refresh(); await tick()
 assert.equal(session.state.catalogs.first.packages[0].id, 'old')
 assert.equal(session.state.localPlugins[0].id, 'local')
 assert.equal(session.state.errors.first, 'Catalog unavailable')
 assert.equal(session.state.inventoryError, 'Inventory unavailable')
})
test('empty success replaces old results and unchanged data keeps identity', async () => {
 const data = loader(), session = createStoreSession(data); await session.refresh()
 const before = session.state.catalogs.first; await session.refresh()
 assert.equal(session.state.catalogs.first, before)
 data.catalog = async () => catalog(); await session.refresh()
 assert.equal(session.state.catalogs.first.packages.length, 0); assert.equal(session.state.loaded, true)
})
test('changed source address and removed sources cannot retain previous packages', async () => {
 const data = loader(), session = createStoreSession(data); await session.refresh()
 const response = pending(); data.sources = async () => [source('https://different.invalid/')]
 data.catalog = () => response.promise
 const operation = session.refresh(); await tick(); assert.equal(session.state.catalogs.first, undefined)
 response.resolve(catalog('other')); await operation
 data.sources = async () => []; await session.refresh(); assert.deepEqual(Object.keys(session.state.catalogs), [])
})
test('superseded request cannot overwrite newer results', async () => {
 const data = loader(), session = createStoreSession(data), old = pending()
 data.catalog = () => old.promise; const first = session.refresh(); await tick()
 data.catalog = async () => catalog('latest'); await session.refresh()
 old.resolve(catalog('stale')); await first
 assert.equal(session.state.catalogs.first.packages[0].id, 'latest'); assert.equal(session.state.loading, false)
})
test('initial failures remain distinct from a successful empty cache; preview is isolated', async () => {
 const data = loader(); data.catalog = async () => { throw new Error('offline') }
 const session = createStoreSession(data); await session.refresh()
 assert.equal(session.state.loaded, false); assert.equal(session.state.loading, false)
 assert.notEqual(storeSession('preview-fixture', loader()), storeSession('desktop-fixture', loader()))
})

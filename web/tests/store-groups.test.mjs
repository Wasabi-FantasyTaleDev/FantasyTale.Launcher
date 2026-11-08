import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import * as versions from '../src/store/versions.ts'

const module = { exports: {} }
runInNewContext(ts.transpileModule(readFileSync(new URL('../src/store/groups.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { module, exports: module.exports, require: () => versions })
const { groupStorePackages } = module.exports
const source = (id, enabled = true) => ({ id, name: id, enabled })
const plugin = (hash, version = '1.0.0', game = 'fixture') => ({ id: 'shared', kind: 'plugin', game, version, manifestSha256: hash })
const catalog = (...packages) => ({ packages })

test('all sources deduplicates plugin identities and follows source order before version precedence', () => {
  const sources = [source('first'), source('second')]
  const groups = groupStorePackages(sources, { first: catalog(plugin('old'), plugin('new', '2.0.0')),
    second: catalog(plugin('later', '9.0.0')) }, 'plugin')
  assert.equal(groups.length, 1)
  assert.equal(groups[0].source.id, 'first')
  assert.equal(groups[0].offers.length, 2)
  assert.equal(groups[0].offers[0].source.id, 'first')
  assert.equal(groups[0].offers[1].source.id, 'second')
  assert.equal(groups[0].releases[0].manifestSha256, 'new')
  assert.equal(groups[0].releases[1].manifestSha256, 'old')
  assert.equal(groups[0].offers[1].releases[0].version, '9.0.0')
  const reversed = groupStorePackages([...sources].reverse(), { first: catalog(plugin('first')), second: catalog(plugin('second')) }, 'plugin')
  assert.equal(reversed[0].source.id, 'second')
})

test('source filtering and disabled sources retain separate games and kinds', () => {
  const sources = [source('disabled', false), source('first'), source('second')]
  const catalogs = { disabled: catalog(plugin('disabled')), first: catalog(plugin('first'), plugin('other', '1.0.0', 'other-game'),
    { ...plugin('client'), kind: 'client' }), second: catalog(plugin('second')) }
  const groups = groupStorePackages(sources, catalogs, 'plugin')
  assert.equal(groups.length, 2)
  assert.equal(groups[0].offers.length, 2)
  assert.equal(groups[0].source.id, 'first')
  assert.equal(groups[1].offers.length, 1)
  const filtered = groupStorePackages(sources, catalogs, 'plugin', 'second')
  assert.equal(filtered.length, 1)
  assert.equal(filtered[0].offers.length, 1)
  assert.equal(filtered[0].source.id, 'second')
  assert.equal(groupStorePackages(sources, catalogs, 'plugin', 'disabled').length, 0)
  assert.equal(groupStorePackages(sources, catalogs, 'client').length, 1)
})

test('catalog completion order does not change priority or merge different same-version hashes', () => {
  const sources = [source('first'), source('second')]
  const catalogs = { second: catalog(plugin('second-hash')), first: catalog(plugin('first-hash')) }
  const group = groupStorePackages(sources, catalogs, 'plugin')[0]
  assert.equal(group.offers[0].source.id, 'first')
  assert.equal(group.offers[0].releases[0].manifestSha256, 'first-hash')
  assert.equal(group.offers[1].releases[0].manifestSha256, 'second-hash')
  assert.equal(catalogs.first.packages[0].manifestSha256, 'first-hash')
})

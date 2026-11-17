import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'

const source = readFileSync(new URL('../src/clientVersion.ts', import.meta.url), 'utf8')
const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
const module = { exports: {} }
runInNewContext(javascript, { module, exports: module.exports })
const caption = module.exports.clientVersionCaption
const wireId = module.exports.clientWireId

test('release and patch captions retain the actual wire identity and never use the disambiguating suffix', () => {
  assert.equal(caption({ id: 'cn-706', branch: 'cn', releaseVersion: '0.2.51.002' }), 'CN Ver.0.2.51.002 (cn-706)')
  assert.equal(caption({ id: 'kr-812-2', protocolVersion: 812, branch: 'kr', releaseVersion: '', patchVersion: '458' }), 'KR Patch V458 (kr-812)')
  assert.equal(caption({ id: 'id-400', branch: 'id', releaseVersion: '', patchVersion: '113' }), 'ID Patch V113 (id-400)')
  assert.equal(caption({ id: 'unknown', branch: 'custom', region: 'TW', releaseVersion: '', patchVersion: '369' }), 'TW Patch V369 (unknown)')
  assert.equal(caption({ id: 'kr-1', branch: 'kr', releaseVersion: '1.0', patchVersion: '2' }), 'KR Ver.1.0 (kr-1)')
  assert.equal(caption({ id: 'kr-1', branch: 'kr', releaseVersion: '' }), undefined)
})

test('a hidden build is listed only once a client of that build exists for the same game', () => {
  const listed = module.exports.buildListed
  const kr186 = { id: 'kr-186', hidden: true }
  assert.equal(listed('bubblefighter', { id: 'tw-410' }, []), true)
  assert.equal(listed('bubblefighter', kr186, []), false)
  assert.equal(listed('bubblefighter', kr186, [{ game: 'other', build: 'kr-186' }, { game: 'bubblefighter', build: 'kr-812' }]), false)
  assert.equal(listed('bubblefighter', kr186, [{ game: 'bubblefighter', build: 'kr-186' }]), true)
})

test('the Home version picker and the default selection use only listed builds', () => {
  const home = readFileSync(new URL('../src/pages/HomePage.vue', import.meta.url), 'utf8')
  const state = readFileSync(new URL('../src/state.ts', import.meta.url), 'utf8')
  assert.match(home, /const choices = computed\(\(\) => listedBuilds\.value\.flatMap\(/)
  assert.match(state, /export const listedBuilds = computed\(\(\) => selectedGame\.value\?\.builds\.filter\(b => buildListed\(/)
  assert.match(state, /export const selectedBuild = computed\(\(\) => listedBuilds\.value\.find\(/)
  assert.doesNotMatch(state, /selectedGame\.value\?\.builds\[0\]/)
})

test('a build without version metadata is named by its wire identity', () => {
  assert.equal(wireId({ id: 'kr-186', branch: 'kr', region: '', protocolVersion: 186 }), 'kr-186')
  assert.equal(wireId({ id: 'fixture', branch: 'kr', region: '' }), 'fixture')
  assert.equal(caption({ id: 'kr-186', branch: 'kr', region: '', protocolVersion: 186, releaseVersion: '', patchVersion: undefined }), undefined)
})

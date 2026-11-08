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

test('release and patch captions retain the actual wire identity and never use the disambiguating suffix', () => {
  assert.equal(caption({ id: 'cn-706', branch: 'cn', releaseVersion: '0.2.51.002' }), 'CN Ver.0.2.51.002 (cn-706)')
  assert.equal(caption({ id: 'kr-812-2', protocolVersion: 812, branch: 'kr', releaseVersion: '', patchVersion: '458' }), 'KR Patch V458 (kr-812)')
  assert.equal(caption({ id: 'id-400', branch: 'id', releaseVersion: '', patchVersion: '113' }), 'ID Patch V113 (id-400)')
  assert.equal(caption({ id: 'unknown', branch: 'custom', region: 'TW', releaseVersion: '', patchVersion: '369' }), 'TW Patch V369 (unknown)')
  assert.equal(caption({ id: 'kr-1', branch: 'kr', releaseVersion: '1.0', patchVersion: '2' }), 'KR Ver.1.0 (kr-1)')
  assert.equal(caption({ id: 'kr-1', branch: 'kr', releaseVersion: '' }), undefined)
})

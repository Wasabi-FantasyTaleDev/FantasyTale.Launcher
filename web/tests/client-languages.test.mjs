import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'

const source = readFileSync(new URL('../src/clientLanguages.ts', import.meta.url), 'utf8')
const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
const module = { exports: {} }
runInNewContext(javascript, { module, exports: module.exports })
const { supportedClientLanguages, clientLanguageInstalled } = module.exports

test('native and additional languages share one list even before their packages are installed', () => {
  const build = { nativeLanguage: 'id', additionalLanguages: { 'zh-CN': 'test.zh', ko: 'test.ko' } }
  assert.deepEqual([...supportedClientLanguages(build)], ['id', 'zh-CN', 'ko'])
  assert.deepEqual([...supportedClientLanguages(build, [], ['id', 'zh-CN'])], ['id', 'zh-CN'])
  assert.equal(clientLanguageInstalled(build, 'id', []), true)
  assert.equal(clientLanguageInstalled(build, 'zh-CN', []), false)
  const plugin = { id: 'test.zh', kind: 'files', available: true, selection: { enabled: true } }
  assert.equal(clientLanguageInstalled(build, 'zh-CN', [plugin]), true)
  for (const override of [{ available: false }, { kind: 'native' }, { launcherCompatible: false }, { languagePackCompatible: false }, { selection: { enabled: false } }]) {
    assert.equal(clientLanguageInstalled(build, 'zh-CN', [{ ...plugin, ...override }]), false)
  }
  assert.deepEqual([...supportedClientLanguages(undefined)], [])
})

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'

function load(path) {
  const source = readFileSync(new URL(path, import.meta.url), 'utf8')
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } }).outputText
  const module = { exports: {} }
  runInNewContext(javascript, { module, exports: module.exports })
  return module.exports
}

const { pluginPolicyError, pluginPolicyErrorCodes } = load('../src/pluginPolicyErrors.ts')
const languages = ['zh-CN', 'zh-TW', 'ko', 'id', 'vi', 'th', 'en']
const locale = language => JSON.parse(readFileSync(new URL(`../src/locales/${language}.json`, import.meta.url), 'utf8'))

test('a coded plugin policy error becomes its message with the plugin id', () => {
  const calls = []
  const text = pluginPolicyError('server_plugin_required:bubblefighter.fantasy.kr-auth', (key, values) => { calls.push([key, values]); return 'translated' })
  assert.equal(text, 'translated')
  // The values object comes from the module's VM context, so compare its content, not its prototype.
  assert.equal(JSON.stringify(calls), JSON.stringify([['pluginPolicyErrors.server_plugin_required', { plugin: 'bubblefighter.fantasy.kr-auth' }]]))
})

test('other errors pass through untranslated', () => {
  const never = () => { throw new Error('must not translate') }
  for (const error of ['server_plugin_required', 'server_plugin_required:', ':x', 'unknown_code:x', 'The selected plugin version changed.'])
    assert.equal(pluginPolicyError(error, never), undefined, error)
})

test('every launcher language has a message for every code and names the plugin', () => {
  for (const language of languages) {
    const messages = locale(language).pluginPolicyErrors
    assert.ok(messages, language)
    assert.deepEqual(Object.keys(messages).sort(), [...pluginPolicyErrorCodes].sort(), language)
    for (const code of pluginPolicyErrorCodes) assert.match(messages[code], /\{plugin\}/, `${language} ${code}`)
  }
})

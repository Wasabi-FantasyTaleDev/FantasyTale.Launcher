import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as completions from '../src/downloads/completions.ts'
import * as model from '../src/downloads/model.ts'

test('restored completed transfers stay in history while current-session completion remains visible on Home', () => {
  const tasks = vue.ref([]), sessionDownloadIds = vue.reactive(new Set())
  const source = readFileSync(new URL('../src/components/HomeUpdate.vue', import.meta.url), 'utf8')
  const script = compileScript(parse(source).descriptor, { id: 'home-history' }).content
  const javascript = ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return vue
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key, locale: vue.ref('en') }) }
    if (id === 'vue-router') return { useRouter: () => ({ push() {} }) }
    if (id === '../i18n') return { localized: () => 'Fixture' }
    if (id === '../state') return { state: { selectedGameId: 'fixture' }, selectedBuild: vue.ref({ id: 'test-1' }), selectedInstance: vue.ref({ id: 'one' }) }
    if (id === '../downloads/model') return model
    if (id === '../downloads/live') return { downloads: {}, downloadTasks: tasks, sessionDownloadIds }
    if (id.endsWith('.vue')) return { default: {} }
    if (id === '../downloads/completions') return completions
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require })
  const bindings = module.exports.default.setup({}, { expose() {} })
  assert.equal(bindings.expanded.value, false)
  const archived = { id: 'archived', mode: 'live', stage: 'completed', target: { kind: 'plugin', gameId: 'fixture', instanceId: 'one' } }
  tasks.value = [archived]
  assert.equal(bindings.task.value, undefined)
  const active = { ...archived, id: 'current', stage: 'downloading' }
  sessionDownloadIds.add(active.id)
  tasks.value = [archived, active]
  assert.equal(bindings.task.value.id, 'current')
  assert.equal(bindings.expanded.value, false)
  bindings.expanded.value = true
  tasks.value = [archived, { ...active, stage: 'completed' }]
  assert.equal(bindings.task.value.id, 'current')
  assert.equal(bindings.expanded.value, true)
  completions.acknowledgeClientLaunch(tasks.value, 'one', 'fixture', 'test-1')
  assert.equal(bindings.task.value, undefined)
  assert.equal(tasks.value.length, 2)
  completions.dismissedCompletionIds.clear()
  tasks.value = [archived, { ...active, id: 'another', stage: 'downloading' }]
  assert.equal(bindings.expanded.value, false)
  tasks.value = [archived, { ...active, stage: 'completed' }]
  sessionDownloadIds.clear()
  assert.equal(bindings.task.value, undefined)
  assert.equal(tasks.value.length, 2)
})

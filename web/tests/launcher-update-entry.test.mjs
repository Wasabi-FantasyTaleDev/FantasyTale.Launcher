import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const source = readFileSync(new URL('../src/components/LauncherUpdateEntry.vue', import.meta.url), 'utf8')
const script = compileScript(parse(source).descriptor, { id: 'LauncherUpdateEntry' }).content
const javascript = ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
function fixture() {
  let resolve, reject, receive
  const reply = new Promise((yes, no) => { resolve = yes; reject = no })
  const scope = vue.effectScope(), unmount = [], calls = []
  const flow = vue.reactive({ update: { status: 'available' }, task: null, restarting: false })
  const launcherUpdate = { state: flow, active: vue.ref(false), visible: vue.ref(false), canInstall: vue.ref(false),
    show: () => calls.push('show'), dismiss() {}, accept() {}, install() {}, refresh: async () => calls.push('refresh'), stop() {} }
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key, locale: vue.ref('en') }) }
    if (id === '../bridge') return { inHost: true, on: (_, listener) => { receive = listener; return () => {} }, call: () => reply }
    if (id === '../state') return { state: {} }
    if (id === '../downloads/launcherUpdate') return { launcherUpdate, launcherUpdatePreview: false }
    if (id === '../downloads/model') return { formatBytes() {}, transferMetrics() {} }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, setTimeout, clearTimeout })
  const bindings = scope.run(() => module.exports.default.setup({ compact: false }, { expose() {}, emit() {} }))
  const requirement = minimum => ({ minimumLauncherVersion: minimum, currentLauncherVersion: '0.1.14', subject: 'test' })
  return { bindings, flow, calls, resolve, reject, receive: minimum => receive(requirement(minimum)), raw: data => receive(data),
    dispose() { unmount.forEach(callback => callback()); scope.stop() } }
}

test('required upgrade check opens the shared update flow after discovery', async () => {
  const f = fixture()
  try { f.receive('0.2.0'); const checking = f.bindings.checkRequiredUpdate(); f.resolve(); await checking
    assert.equal(f.bindings.requiredUpgrade.value, null); assert.deepEqual(f.calls, ['refresh', 'show'])
  } finally { f.dispose() }
})

for (const scenario of ['dismissed', 'replaced', 'unmounted']) {
  test(`late required upgrade check cannot reopen a ${scenario} dialog`, async () => {
    const f = fixture()
    try {
      f.receive('0.2.0'); const checking = f.bindings.checkRequiredUpdate()
      if (scenario === 'dismissed') f.bindings.requiredUpgrade.value = null
      if (scenario === 'replaced') f.receive('0.3.0')
      if (scenario === 'unmounted') f.dispose()
      f.resolve(); await checking
      assert.equal(f.calls.includes('show'), false)
      if (scenario === 'replaced') assert.equal(f.bindings.requiredUpgrade.value.minimumLauncherVersion, '0.3.0')
    } finally { f.dispose() }
  })
}

test('late network failure does not leak into a replacement requirement', async () => {
  const f = fixture()
  try { f.receive('0.2.0'); const checking = f.bindings.checkRequiredUpdate(); f.receive('0.3.0'); f.reject(new Error('offline')); await checking
    assert.equal(f.bindings.requirementError.value, '')
  } finally { f.dispose() }
})

test('installation cannot be obscured by upgrade requirements and malformed events are ignored', () => {
  const f = fixture()
  try {
    f.raw(null); f.raw({ minimumLauncherVersion: 14 }); assert.equal(f.bindings.requiredUpgrade.value, null)
    f.flow.restarting = true; f.receive('0.3.0'); assert.equal(f.bindings.requiredUpgrade.value, null)
  } finally { f.dispose() }
})

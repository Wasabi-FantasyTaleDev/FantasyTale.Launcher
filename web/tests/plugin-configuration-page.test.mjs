import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const source = readFileSync(new URL('../src/components/PluginConfigurationPage.vue', import.meta.url), 'utf8')
const script = compileScript(parse(source).descriptor, { id: 'PluginConfigurationPage' }).content
const javascript = ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText

function fixture() {
  const mounted = [], unmounted = [], listeners = new Map(), sent = [], changes = [], scope = vue.effectScope()
  const locale = vue.ref('en')
  const dark = vue.ref(false)
  const props = vue.reactive({ html: '<main>Settings</main>', options: [{
    key: 'custom_resolutions', type: 'string', default: '', minimum: 0, maximum: 0,
    maxLength: 4096, values: [], label: { en: 'Additional resolutions', 'zh-CN': '扩展分辨率' },
    description: { en: 'Preserve original display choices.' }, valueLabels: null,
  }], values: { custom_resolutions: '1920x1080' } })
  const child = { postMessage(message, targetOrigin) {
    assert.equal(targetOrigin, '*')
    // Browsers clone the payload here and reject Vue proxies with DataCloneError.
    sent.push(structuredClone(message))
  } }
  const window = { addEventListener: (type, callback) => listeners.set(type, callback), removeEventListener: type => listeners.delete(type) }
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onMounted: callback => mounted.push(callback), onBeforeUnmount: callback => unmounted.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ locale }) }
    if (id === '../theme') return { isDark: () => dark.value }
    throw new Error('Unexpected dependency ' + id)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, window, TextEncoder })
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: (...args) => changes.push(args) }))
  bindings.frame.value = vue.markRaw({ contentWindow: child })
  mounted.forEach(callback => callback())
  const receive = (data, origin = 'null', eventSource = child) => listeners.get('message')?.({ data, origin, source: eventSource })
  return { bindings, props, locale, dark, sent, changes, receive, listeners, dispose() { unmounted.forEach(callback => callback()); scope.stop() } }
}

test('an open settings page follows the launcher theme without changing draft values', async () => {
  const f = fixture()
  try {
    f.bindings.initialize()
    assert.equal(f.sent.at(-1).theme, 'light')
    f.dark.value = true
    await vue.nextTick()
    assert.equal(f.sent.at(-1).theme, 'dark')
    assert.equal(f.sent.at(-1).values.custom_resolutions, '1920x1080')
    assert.equal(f.changes.length, 0)
  } finally { f.dispose() }
})

test('iframe initialization sends cloneable snapshots of reactive option definitions', () => {
  const f = fixture()
  try {
    assert.equal(vue.isProxy(f.props.options), true)
    assert.equal(vue.isProxy(f.props.options[0].label), true)
    f.bindings.initialize()
    assert.equal(f.sent.length, 1)
    const message = f.sent[0]
    assert.equal(message.type, 'fantasytale:configuration')
    assert.equal(message.schema, 1)
    assert.equal(message.options[0].label['zh-CN'], '扩展分辨率')
    assert.equal(message.options[0].maxLength, 4096)
    assert.equal(message.values.custom_resolutions, '1920x1080')
    f.props.options[0].label.en = 'Changed'
    assert.equal(message.options[0].label.en, 'Additional resolutions')
  } finally { f.dispose() }
})

test('a sandboxed page ready message receives its configuration', () => {
  const f = fixture()
  try {
    f.receive({ type: 'fantasytale:ready', schema: 1 }, 'https://other.example')
    f.receive({ type: 'fantasytale:ready', schema: 1 }, 'null', {})
    assert.equal(f.sent.length, 0)
    f.receive({ type: 'fantasytale:ready', schema: 1 })
    assert.equal(f.sent.length, 1)
  } finally { f.dispose() }
})

test('only positive integer heights from the active settings page resize its frame', () => {
  const f = fixture()
  try {
    const resize = height => ({ type: 'fantasytale:resize', schema: 1, height })
    f.receive(resize(240), 'https://other.example')
    f.receive(resize(240), 'null', {})
    for (const height of [undefined, 0, -1, 1.5, '240', NaN, Infinity, Number.MAX_SAFE_INTEGER + 1]) f.receive(resize(height))
    assert.equal(f.bindings.frameHeight.value, 160)
    assert.equal(f.bindings.contentSized.value, false)
    f.receive(resize(240))
    assert.equal(f.bindings.frameHeight.value, 240)
    assert.equal(f.bindings.contentSized.value, true)
    assert.equal(f.changes.length, 0)
  } finally { f.dispose() }
})

test('changing the HTML document resets intrinsic sizing and restores ordinary scrolling', async () => {
  const f = fixture()
  try {
    f.receive({ type: 'fantasytale:resize', schema: 1, height: 520 })
    f.props.html = '<main>Another page</main>'
    await vue.nextTick()
    assert.equal(f.bindings.frameHeight.value, 160)
    assert.equal(f.bindings.contentSized.value, false)
  } finally { f.dispose() }
})

test('locale and draft changes resend cloneable current values', async () => {
  const f = fixture()
  try {
    f.bindings.initialize()
    f.props.values.custom_resolutions = '2560x1440'
    f.locale.value = 'zh-CN'
    await vue.nextTick()
    assert.equal(f.sent.at(-1).language, 'zh-CN')
    assert.equal(f.sent.at(-1).values.custom_resolutions, '2560x1440')
    assert.equal(f.sent[0].values.custom_resolutions, '1920x1080')
  } finally { f.dispose() }
})

test('only validated changes from the active sandboxed page update the draft', () => {
  const f = fixture()
  try {
    for (const values of [{ missing: '' }, { custom_resolutions: false }, { custom_resolutions: 'a'.repeat(4097) }])
      f.receive({ type: 'fantasytale:change', schema: 1, values })
    f.receive({ type: 'fantasytale:change', schema: 2, values: { custom_resolutions: '' } })
    assert.equal(f.changes.length, 0)
    f.receive({ type: 'fantasytale:change', schema: 1, values: { custom_resolutions: '3440x1440' } })
    assert.equal(f.changes.length, 1)
    assert.equal(f.changes[0][0], 'change')
    assert.equal(f.changes[0][1].custom_resolutions, '3440x1440')
  } finally { f.dispose() }
  assert.equal(f.listeners.size, 0)
})

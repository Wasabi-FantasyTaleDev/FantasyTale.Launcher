import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

function compile(source, require = () => { throw new Error('Unexpected import') }) {
  const module = { exports: {} }
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  runInNewContext(javascript, { module, exports: module.exports, require })
  return module.exports
}
const colors = compile(readFileSync(new URL('../src/colors.ts', import.meta.url), 'utf8'))
function setup(store = new Map(), failWrite = false) {
  const props = vue.reactive({ modelValue: '#FFFFFF' }), emitted = [], scope = vue.effectScope()
  const source = compileScript(parse(readFileSync(new URL('../src/components/NativeChatColorPicker.vue', import.meta.url), 'utf8')).descriptor, { id: 'ColorPicker' }).content
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return vue
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../colors') return colors
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected import ${id}`)
  }
  runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, {
    module, exports: module.exports, require,
    localStorage: { getItem: key => store.get(key) ?? null, setItem: (key, value) => { if (failWrite) throw new Error('Storage unavailable'); store.set(key, value) } },
  })
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: (_, value) => { emitted.push(value); props.modelValue = value } }))
  return { bindings, emitted, props, dispose: () => scope.stop() }
}

test('hex and RGB conversions preserve channels and reject invalid or incomplete input', () => {
  assert.equal(colors.hexColor('#f145a6'), '#F145A6')
  assert.equal(colors.hexColor('#FF'), null)
  assert.equal(colors.rgbColor(['0', '255', '128']), '#00FF80')
  for (const invalid of [['256', '0', '0'], ['-1', '0', '0'], ['1.5', '0', '0'], ['', '0', '0']]) assert.equal(colors.rgbColor(invalid), null)
  assert.deepEqual(Array.from(colors.colorChannels('#F145A6')), ['241', '69', '166'])
})

test('picker, hex and RGB editing stay synchronized and incomplete RGB edits can be completed', async () => {
  const fixture = setup()
  try {
    fixture.bindings.editHex('#30db02')
    await vue.nextTick()
    assert.equal(fixture.props.modelValue, '#30DB02')
    assert.deepEqual(Array.from(fixture.bindings.rgb.value), ['48', '219', '2'])
    fixture.bindings.choose('#00EBDE')
    await vue.nextTick()
    assert.equal(fixture.bindings.hex.value, '#00EBDE')
    fixture.bindings.editRgb(0, '')
    await vue.nextTick()
    assert.deepEqual(Array.from(fixture.bindings.rgb.value), ['', '235', '222'])
    fixture.bindings.editRgb(0, '128')
    await vue.nextTick()
    assert.equal(fixture.props.modelValue, '#80EBDE')
  } finally { fixture.dispose() }
})

test('custom colors persist across picker instances, deduplicate and can be removed', async () => {
  const store = new Map(), first = setup(store)
  first.bindings.choose('#7C5DFF')
  first.bindings.save()
  first.bindings.save()
  first.dispose()
  const second = setup(store)
  try {
    assert.deepEqual(Array.from(second.bindings.saved.value), ['#7C5DFF'])
    second.bindings.remove('#7C5DFF')
    assert.deepEqual(Array.from(colors.savedColors(store.get('nativeUi.savedChatColors'))), [])
  } finally { second.dispose() }
})

test('malformed saved data and failed persistence do not fabricate saved colors', () => {
  assert.deepEqual(Array.from(colors.savedColors('broken')), [])
  assert.deepEqual(Array.from(colors.savedColors('["#ffffff",null,"bad","#FFFFFF"]')), ['#FFFFFF'])
  const fixture = setup(new Map(), true)
  try {
    fixture.bindings.save()
    assert.equal(fixture.bindings.saveError.value, true)
    assert.equal(fixture.bindings.saved.value.length, 0)
  } finally { fixture.dispose() }
})

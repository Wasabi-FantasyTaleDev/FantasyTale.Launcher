import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { randomUUID } from 'node:crypto'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

const flush = async () => { await vue.nextTick(); await new Promise(resolve => setImmediate(resolve)) }
const pending = () => { let resolve; const promise = new Promise(yes => { resolve = yes }); return { promise, resolve } }
const colorModule = { exports: {} }
runInNewContext(ts.transpileModule(readFileSync(new URL('../src/colors.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { module: colorModule, exports: colorModule.exports })
const capabilities = { build: 'fixture', operations: ['messageBox', 'notification', 'announcement.show', 'announcement.close', 'chat'].map(kind => ({ kind, supported: true, ready: true,
  features: kind === 'messageBox' ? ['buttons:1..2', 'icon:none', 'icon:notice', 'icon:warning', 'icon:question', 'icon:error'] : kind === 'chat' ? ['color:runs'] : [] })) }

function setup(handler) {
  const mounted = [], unmount = [], timers = new Map(), calls = [], scope = vue.effectScope()
  let timerId = 0
  const call = async (method, params) => {
    calls.push({ method, params })
    if (handler) { const result = handler(method, params); if (result !== undefined) return result }
    if (method === 'instances.running') return [{ instanceId: 'one', processId: 123 }, { instanceId: 'two', processId: 456 }]
    if (method === 'nativeUi.capabilities') return capabilities
    if (method === 'nativeUi.invoke') return { operationId: params.request.operationId, status: 'shown' }
    if (method === 'nativeUi.result') return { operationId: params.operationId, status: 'selected', buttonId: 'ok' }
    throw new Error(`Unexpected call ${method}`)
  }
  const source = readFileSync(new URL('../src/components/NativeGameUiTests.vue', import.meta.url), 'utf8')
  const script = compileScript(parse(source).descriptor, { id: 'NativeGameUiTests' }).content
  const javascript = ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onMounted: callback => mounted.push(callback), onUnmounted: callback => unmount.push(callback) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key, te: () => true }) }
    if (id === '../bridge') return { call }
    if (id === '../colors') return colorModule.exports
    if (id === '../state') return { state: { instances: [{ id: 'one', name: 'First', build: 'fixture' }, { id: 'two', name: 'Second', build: 'fixture' }] } }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, crypto: { randomUUID }, setInterval: () => 1, clearInterval() {},
    setTimeout: callback => { const id = ++timerId; timers.set(id, callback); return id }, clearTimeout: id => timers.delete(id) })
  const bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  return { bindings, calls, timers, async mount() { for (const callback of mounted) await callback(); await flush() },
    async poll() { const [id, callback] = timers.entries().next().value; timers.delete(id); await callback(); await flush() },
    dispose() { unmount.forEach(callback => callback()); scope.stop() } }
}

test('adapters with explicit close use the same caller identity for show and close', async () => {
  const fixture = setup()
  try {
    await fixture.mount()
    fixture.bindings.announcement.value = 'Native banner'
    await fixture.bindings.invoke('announcement.show')
    await fixture.bindings.invoke('announcement.close')
    const commands = fixture.calls.filter(call => call.method === 'nativeUi.invoke').map(call => call.params.request)
    assert.equal(commands.length, 2)
    assert.equal(commands[0].announcementId, commands[1].announcementId)
    assert.equal(commands[0].text, 'Native banner')
    assert.equal(commands[1].text, '')
    assert.notEqual(commands[0].operationId, commands[1].operationId)
    assert.equal(fixture.timers.size, 0)
  } finally { fixture.dispose() }
})

test('chat transmits ordered independent color runs as a local operation', async () => {
  const fixture = setup()
  try {
    await fixture.mount()
    fixture.bindings.chatCaption.value = 'Custom source'
    fixture.bindings.runs.value = [{ text: 'System ', color: '#00AAFF' }, { text: 'message', color: '#FFFFFF' }]
    await fixture.bindings.invoke('chat')
    const command = fixture.calls.find(call => call.method === 'nativeUi.invoke').params.request
    fixture.bindings.runs.value[0].color = '#FF0000FF'
    assert.equal(command.kind, 'chat')
    assert.equal(command.chatStyle, 'system')
    assert.equal(command.chatCaption, 'Custom source')
    assert.equal(command.runs[0].color, '#00AAFF')
    assert.equal(command.runs[1].text, 'message')
    assert.equal(command.text, '')
    assert.equal(command.buttons, undefined)
  } finally { fixture.dispose() }
})

test('native parameter limits prevent unsupported buttons, icons and mixed colors before submission', async () => {
  const fixture = setup(method => method === 'nativeUi.capabilities' ? { build: 'limited', operations: [
    { kind: 'messageBox', supported: true, ready: true, features: ['buttons:1..2', 'icon:none', 'icon:warning'] },
    { kind: 'chat', supported: true, ready: true, features: ['color:uniform'] },
    { kind: 'announcement.show', supported: true, ready: true, features: ['autoExpire'] },
  ] } : undefined)
  try {
    await fixture.mount()
    assert.equal(fixture.bindings.iconOptions.value.length, 2)
    assert.equal(fixture.bindings.buttonRange.value.min, 1)
    assert.equal(fixture.bindings.buttonRange.value.max, 2)
    fixture.bindings.addButton()
    assert.equal(fixture.bindings.buttons.value.length, 2)
    fixture.bindings.icon.value = 'error'
    await fixture.bindings.invoke('messageBox')
    assert.equal(fixture.calls.some(call => call.method === 'nativeUi.invoke'), false)
    fixture.bindings.runs.value = [{ text: 'First', color: '#00aaff' }, { text: 'Second', color: '#FFFFFF' }]
    await fixture.bindings.invoke('chat')
    assert.equal(fixture.calls.some(call => call.method === 'nativeUi.invoke'), false)
    fixture.bindings.runs.value[1].color = '#00AAFF'
    assert.equal(fixture.bindings.enabled('chat'), true)
    assert.equal(fixture.bindings.ownedAnnouncements.value, false)
    await fixture.bindings.invoke('announcement.show')
    const command = fixture.calls.find(call => call.method === 'nativeUi.invoke').params.request
    assert.equal(command.announcementId, undefined)
    assert.equal(fixture.timers.size, 0)
  } finally { fixture.dispose() }
})

test('early message boxes require exactly two buttons and preserve their painted-label spaces', async () => {
  const fixture = setup(method => method === 'nativeUi.capabilities' ? { build: 'early', operations: [
    { kind: 'messageBox', supported: true, ready: true, features: ['buttons:2', 'icon:question'] },
  ] } : undefined)
  try {
    await fixture.mount()
    fixture.bindings.icon.value = 'question'
    fixture.bindings.buttons.value = [{ id: 'yes', label: ' ' }]
    await fixture.bindings.invoke('messageBox')
    assert.equal(fixture.calls.some(call => call.method === 'nativeUi.invoke'), false)
    fixture.bindings.buttons.value.push({ id: 'no', label: ' ' })
    fixture.bindings.addButton()
    assert.equal(fixture.bindings.buttons.value.length, 2)
    fixture.bindings.text.value = '要立即返回广场吗？'
    await fixture.bindings.invoke('messageBox')
    const command = fixture.calls.find(call => call.method === 'nativeUi.invoke').params.request
    assert.equal(command.text, '要立即返回广场吗？')
    assert.equal(command.icon, 'question')
    assert.deepEqual(Array.from(command.buttons, button => button.label), [' ', ' '])
  } finally { fixture.dispose() }
})

test('blank button labels and multiline notifications are sent without trimming or substitution', async () => {
  const fixture = setup()
  try {
    await fixture.mount()
    fixture.bindings.buttons.value = [{ id: 'ok', label: '' }, { id: 'cancel', label: '   ' }]
    await fixture.bindings.invoke('messageBox')
    fixture.bindings.notice.value = 'First line\n第二行\r\nThird line'
    await fixture.bindings.invoke('notification')
    const commands = fixture.calls.filter(call => call.method === 'nativeUi.invoke').map(call => call.params.request)
    assert.equal(commands[0].buttons[0].label, '')
    assert.equal(commands[0].buttons[1].label, '   ')
    assert.equal(commands[1].text, 'First line\n第二行\r\nThird line')
    assert.equal(commands[1].chatCaption, undefined)
  } finally { fixture.dispose() }
})

test('message-box outcomes are polled without submitting another dialog', async () => {
  const fixture = setup()
  try {
    await fixture.mount()
    await fixture.bindings.invoke('messageBox')
    assert.equal(fixture.timers.size, 1)
    assert.equal(fixture.bindings.pending.value.has('messageBox'), true)
    await fixture.poll()
    assert.equal(fixture.bindings.results.value.messageBox.buttonId, 'ok')
    assert.equal(fixture.bindings.pending.value.has('messageBox'), false)
    assert.equal(fixture.calls.filter(call => call.method === 'nativeUi.invoke').length, 1)
    assert.equal(fixture.calls.filter(call => call.method === 'nativeUi.result').length, 1)
  } finally { fixture.dispose() }
})

test('switching processes discards a late result and never polls the new process for an old operation', async () => {
  const delayed = pending()
  const fixture = setup((method) => method === 'nativeUi.invoke' ? delayed.promise : undefined)
  try {
    await fixture.mount()
    const invocation = fixture.bindings.invoke('messageBox')
    const request = fixture.calls.find(call => call.method === 'nativeUi.invoke').params.request
    fixture.bindings.selected.value = 'two:456'
    await flush()
    delayed.resolve({ operationId: request.operationId, status: 'queued' })
    await invocation
    assert.equal(fixture.bindings.results.value.messageBox, undefined)
    assert.equal(fixture.timers.size, 0)
    assert.equal(fixture.bindings.pending.value.size, 0)
  } finally { fixture.dispose() }
})

test('refreshing capabilities preserves a live dialog and its result polling', async () => {
  const fixture = setup()
  try {
    await fixture.mount()
    await fixture.bindings.invoke('messageBox')
    await fixture.bindings.refreshCapabilities()
    assert.equal(fixture.timers.size, 1)
    assert.equal(fixture.bindings.pending.value.has('messageBox'), true)
    await fixture.bindings.invoke('messageBox')
    assert.equal(fixture.calls.filter(call => call.method === 'nativeUi.invoke').length, 1)
    await fixture.poll()
    assert.equal(fixture.bindings.results.value.messageBox.buttonId, 'ok')
  } finally { fixture.dispose() }
})

test('a stale capability refresh cannot replace the latest availability', async () => {
  const delayed = pending()
  let refreshes = 0
  const fixture = setup(method => {
    if (method !== 'nativeUi.capabilities') return undefined
    refreshes++
    return refreshes === 2 ? delayed.promise : capabilities
  })
  try {
    await fixture.mount()
    const oldRefresh = fixture.bindings.refreshCapabilities()
    await fixture.bindings.refreshCapabilities()
    delayed.resolve({ build: 'stale', operations: [] })
    await oldRefresh
    assert.equal(fixture.bindings.capabilities.value.build, 'fixture')
    assert.equal(fixture.bindings.enabled('messageBox'), true)
  } finally { fixture.dispose() }
})

test('unavailable capabilities disable each operation without substituting Overlay', async () => {
  const fixture = setup(method => method === 'nativeUi.capabilities' ? { build: 'fixture', operations: [] } : undefined)
  try {
    await fixture.mount()
    for (const kind of ['messageBox', 'notification', 'announcement.show', 'announcement.close', 'chat']) {
      assert.equal(fixture.bindings.enabled(kind), false)
      await fixture.bindings.invoke(kind)
    }
    assert.equal(fixture.calls.some(call => call.method === 'nativeUi.invoke'), false)
  } finally { fixture.dispose() }
})

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

function fixture(options = {}) {
  let now = 0, counter = 0, closed = 0
  const timers = new Map(), cleanup = []
  const props = vue.reactive({ message: 'Completed', title: '', severity: 'info', persistent: undefined, durationMs: 6000, ...options })
  const script = compileScript(parse(readFileSync(new URL('../src/components/ShellNotification.vue', import.meta.url), 'utf8')).descriptor, { id: 'toast' }).content
  const javascript = ts.transpileModule(script, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  runInNewContext(javascript, {
    module, exports: module.exports,
    require: id => id === 'vue' ? { ...vue, onBeforeUnmount: callback => cleanup.push(callback) } : { default: {} },
    performance: { now: () => now },
    setTimeout: (callback, delay) => { const id = ++counter; timers.set(id, { at: now + delay, callback }); return id },
    clearTimeout: id => timers.delete(id),
  })
  const scope = vue.effectScope()
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: () => { closed++ } }))
  return { props, bindings, get closed() { return closed }, get pending() { return timers.size },
    advance(ms) { now += ms; for (const [id, timer] of timers) if (timer.at <= now) { timers.delete(id); timer.callback() } },
    dispose() { cleanup.forEach(callback => callback()); scope.stop() },
  }
}

test('transient notification lifetime pauses while hovered or keyboard-focused', () => {
  const f = fixture()
  f.advance(2000); f.bindings.hover(true); f.advance(10000)
  assert.equal(f.closed, 0)
  f.bindings.focus(true); f.bindings.hover(false); f.advance(10000)
  assert.equal(f.closed, 0)
  f.bindings.focus(false); f.advance(3999)
  assert.equal(f.closed, 0)
  f.advance(1); assert.equal(f.closed, 1)
  f.dispose()
})

test('replacement messages get a fresh lifetime and manual close/unmount cancel pending callbacks', () => {
  const f = fixture()
  f.advance(5000); f.props.message = 'Another notification'; f.advance(1000)
  assert.equal(f.closed, 0)
  f.bindings.close(); assert.equal(f.closed, 1)
  f.advance(10000); assert.equal(f.closed, 1)
  f.props.message = 'Third notification'; assert.equal(f.pending, 1)
  f.dispose(); assert.equal(f.pending, 0)
})

test('info, success and warning notifications expire while errors remain until dismissed', () => {
  for (const severity of ['info', 'success', 'warning']) {
    const f = fixture({ severity })
    f.advance(5999); assert.equal(f.closed, 0)
    f.advance(1); assert.equal(f.closed, 1)
    f.bindings.hover(true); f.bindings.hover(false); f.advance(6000)
    assert.equal(f.closed, 1)
    f.dispose()
  }
  const error = fixture({ severity: 'error' })
  assert.equal(error.pending, 0)
  error.advance(86400000); assert.equal(error.closed, 0)
  error.bindings.close(); assert.equal(error.closed, 1)
  error.dispose()
})

test('callers can keep a non-error notification persistent or explicitly expire an error', () => {
  const pinned = fixture({ severity: 'success', persistent: true })
  pinned.advance(86400000); assert.equal(pinned.closed, 0); assert.equal(pinned.pending, 0)
  pinned.props.persistent = false
  pinned.advance(6000); assert.equal(pinned.closed, 1)
  pinned.dispose()
  const error = fixture({ severity: 'error', persistent: false, durationMs: 2000 })
  error.advance(1999); assert.equal(error.closed, 0)
  error.advance(1); assert.equal(error.closed, 1)
  error.dispose()
})

test('changing an active notification to error cancels its earlier expiry callback', () => {
  const f = fixture()
  f.advance(5000); f.props.severity = 'error'
  assert.equal(f.pending, 0)
  f.advance(60000); assert.equal(f.closed, 0)
  f.props.severity = 'success'
  f.advance(5999); assert.equal(f.closed, 0)
  f.advance(1); assert.equal(f.closed, 1)
  f.dispose()
})

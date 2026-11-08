import assert from 'node:assert/strict'
import test from 'node:test'
import { dismissPluginNotice, pluginNotices, showPluginNotice } from '../src/plugins/notices.ts'

test('repeated identical messages get a fresh identity and stale expiry cannot close their replacement', () => {
  pluginNotices.splice(0)
  showPluginNotice('update', 'success', 'Updated')
  const old = pluginNotices[0].id
  showPluginNotice('update', 'success', 'Updated')
  const replacement = pluginNotices[0].id
  assert.notEqual(replacement, old)
  showPluginNotice('other', 'info', 'Another operation')
  dismissPluginNotice(old)
  assert.equal(pluginNotices.length, 2)
  dismissPluginNotice(replacement)
  assert.equal(pluginNotices.length, 1)
  assert.equal(pluginNotices[0].key, 'other')
  pluginNotices.splice(0)
})

test('notification lifetime options survive bounded queue replacement', () => {
  pluginNotices.splice(0)
  showPluginNotice('long', 'warning', 'Waiting', { persistent: true })
  assert.equal(pluginNotices[0].persistent, true)
  showPluginNotice('long', 'warning', 'Waiting again', { persistent: false, durationMs: 12000 })
  assert.equal(pluginNotices.length, 1)
  assert.equal(pluginNotices[0].persistent, false)
  assert.equal(pluginNotices[0].durationMs, 12000)
  for (let i = 0; i < 10; i++) showPluginNotice(`operation:${i}`, 'info', 'Done')
  assert.equal(pluginNotices.length, 5)
  assert.equal(pluginNotices[0].key, 'operation:5')
  pluginNotices.splice(0)
})

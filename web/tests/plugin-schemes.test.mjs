import assert from 'node:assert/strict'
import test from 'node:test'
import { captureSchemeSelection, readLibrary, removeRecoveredRelease, planStoreInstallation, rememberStoreInstallation, captureSchemeInstallation, cancelStoreInstallation } from '../src/plugins/schemes.ts'

const plugin = (version, enabled = true) => ({ id: 'fixture.plugin', version, enabled, required: false, options: { speed: 2 } })
test('a running-client snapshot cannot erase pending store enablement and cancellation restores the prior choice', () => {
  const old = plugin('1.0.0', false), other = { ...old, id: 'fixture.other' }
  const library = readLibrary(JSON.stringify({ schemes: [{ id: 'default', name: '', builds: { first: [old, other] } }], selections: {} }))
  const plans = planStoreInstallation(library, { id: old.id, version: '2.0.0' }, ['first', 'second'], 'server', [{ build: 'first', plugins: [old, other] }])
  rememberStoreInstallation(library, plans, 'download')
  const restored = readLibrary(JSON.stringify(library))
  const scheme = restored.schemes[0]
  const captured = captureSchemeInstallation(restored, scheme, 'first', [old, other], [JSON.stringify([old.id, old.version])], [])
  scheme.builds.first = captured.plugins
  assert.equal(captured.plugins.find(plugin => plugin.id === old.id).enabled, true)
  assert.equal(captured.plugins.find(plugin => plugin.id === old.id).version, '2.0.0')
  cancelStoreInstallation(restored, 'download')
  assert.deepEqual(scheme.builds.first, [other, old])
  assert.deepEqual(scheme.builds.second, [])
  assert.deepEqual(restored.installations, [])
})
test('manual scheme changes during the request or before cancellation supersede the store snapshot', () => {
  const old = plugin('1.0.0', false)
  const library = readLibrary(JSON.stringify({ schemes: [{ id: 'default', name: '', builds: { first: [old] } }], selections: {} }))
  const plans = planStoreInstallation(library, { id: old.id, version: '2.0.0' }, ['first'], '', [])
  library.schemes[0].builds.first[0].options.speed = 9
  rememberStoreInstallation(library, plans, 'download')
  assert.equal(library.schemes[0].builds.first[0].version, '1.0.0')
  const next = planStoreInstallation(library, { id: old.id, version: '2.0.0' }, ['first'], '', [])
  rememberStoreInstallation(library, next, 'download')
  library.schemes[0].builds.first[0].enabled = false
  cancelStoreInstallation(library, 'download')
  assert.equal(library.schemes[0].builds.first[0].version, '2.0.0')
  assert.equal(library.schemes[0].builds.first[0].enabled, false)
})
test('a client edit made while installation is pending supersedes the saved enabled pin', () => {
  const old = plugin('1.0.0', false), edited = { ...old, options: { speed: 9 } }
  const library = readLibrary(JSON.stringify({ schemes: [{ id: 'default', name: '', builds: { first: [old] } }], selections: {} }))
  rememberStoreInstallation(library, planStoreInstallation(library, { id: old.id, version: '2.0.0' }, ['first'], '', [{ build: 'first', plugins: [old] }]), 'download')
  const captured = captureSchemeInstallation(library, library.schemes[0], 'first', [edited], [JSON.stringify([old.id, old.version])], [])
  assert.deepEqual(captured.plugins, [edited])
  assert.deepEqual(library.installations, [])
})
test('disabling a first installed release supersedes pending activation even without a previous client selection', () => {
  const library = readLibrary(JSON.stringify({ schemes: [{ id: 'default', name: '', builds: {} }], selections: {} }))
  const release = plugin('1.0.0', false)
  rememberStoreInstallation(library, planStoreInstallation(library, { id: release.id, version: release.version }, ['first'], '', []), 'download')
  const captured = captureSchemeInstallation(library, library.schemes[0], 'first', [release], [], [])
  assert.deepEqual(captured.plugins, [release])
  assert.deepEqual(library.installations, [])
})
test('store activation migrates an alias and restores that alias on cancellation without duplicate selections', () => {
  const old = plugin('1.0.0', false)
  const item = { id: 'fixture.next', version: '2.0.0', pluginDefinition: { aliases: [old.id], options: [{ key: 'speed' }] } }
  const library = readLibrary(JSON.stringify({ schemes: [{ id: 'default', name: '', builds: { first: [old] } }], selections: {} }))
  rememberStoreInstallation(library, planStoreInstallation(library, item, ['first'], '', [{ build: 'first', plugins: [old] }]), 'download')
  const scheme = library.schemes[0]
  scheme.builds.first = captureSchemeInstallation(library, scheme, 'first', [old], [], []).plugins
  assert.equal(scheme.builds.first.length, 1)
  assert.equal(scheme.builds.first[0].id, item.id)
  assert.deepEqual(scheme.builds.first[0].options, old.options)
  cancelStoreInstallation(library, 'download')
  assert.deepEqual(scheme.builds.first, [old])
})
test('recovery removes the exact failed release from every persisted scheme and build', () => {
  const broken = plugin('1.0.0'), healthy = plugin('2.0.0'), other = { ...broken, id: 'fixture.other' }
  const library = readLibrary(JSON.stringify({ schemes: [
    { id: 'default', name: '', builds: { first: [broken, other], second: [broken] } },
    { id: 'custom', name: 'Custom', builds: { first: [healthy], second: [broken, other] } },
  ], selections: { '["server","first"]': 'custom' } }))
  removeRecoveredRelease(library, broken.id, broken.version)
  const restored = readLibrary(JSON.stringify(library))
  assert.deepEqual(restored.schemes[0].builds, { first: [other], second: [] })
  assert.deepEqual(restored.schemes[1].builds, { first: [healthy], second: [other] })
  assert.deepEqual(restored.selections, library.selections)
  removeRecoveredRelease(restored, broken.id, broken.version)
  assert.deepEqual(restored, library)
})
test('a renamed installed successor replaces the unresolved former scheme identity without losing settings', () => {
  const old = plugin('1.0.0', false), next = { ...plugin('2.0.0', false), id: 'fixture.next' }
  const result = captureSchemeSelection([old], [next], [], [{ id: next.id, version: next.version, aliases: [old.id, 'fixture.ancient'] }])
  assert.deepEqual(result.plugins, [next])
  assert.deepEqual(result.plugins[0].options, old.options)
})
test('an automatic upgrade remains resolved so later toggles and settings cannot revert to the earlier scheme', () => {
  const old = plugin('1.0.0'), next = plugin('2.0.0')
  const first = captureSchemeSelection([old], [next], [JSON.stringify([old.id, old.version])])
  const disabled = captureSchemeSelection(first.plugins, [{ ...next, enabled: false }], first.resolved)
  assert.equal(disabled.plugins[0].enabled, false)
  const enabled = captureSchemeSelection(disabled.plugins, [{ ...next, options: { speed: 3 } }], disabled.resolved)
  assert.equal(enabled.plugins[0].enabled, true)
  assert.equal(enabled.plugins[0].version, '2.0.0')
  assert.deepEqual(enabled.plugins[0].options, { speed: 3 })
})
test('unavailable imported versions stay pinned until the exact release becomes selected', () => {
  const saved = plugin('3.0.0', false), fallback = plugin('1.0.0')
  const first = captureSchemeSelection([saved], [fallback], [JSON.stringify([fallback.id, fallback.version])])
  assert.deepEqual(first.plugins, [saved])
  const applied = captureSchemeSelection(first.plugins, [saved], first.resolved)
  const enabled = captureSchemeSelection(applied.plugins, [{ ...saved, enabled: true }], applied.resolved)
  assert.equal(enabled.plugins[0].enabled, true)
})

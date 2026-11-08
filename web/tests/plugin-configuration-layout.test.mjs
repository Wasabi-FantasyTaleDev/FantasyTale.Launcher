import assert from 'node:assert/strict'
import test from 'node:test'
import { settingsPages } from '../src/plugins/configuration.ts'

test('a single declared HTML page stays the only page', () => {
  const pages = [{ id: 'resolutions', kind: 'html', title: { en: 'Resolutions' }, options: ['custom_resolutions'] }]
  assert.equal(settingsPages({ configurationPages: pages, configurationPage: true, options: [{ key: 'custom_resolutions' }] }, 'Settings', 'Plugin configuration'), pages)
})

test('native and HTML pages retain the package order and explicit option membership', () => {
  const pages = [{ id: 'advanced', kind: 'native', title: { en: 'Advanced' }, options: ['enabled'] }, { id: 'colors', kind: 'html', title: { en: 'Colors' }, options: ['color'] }]
  assert.deepEqual(settingsPages({ configurationPages: pages, options: [] }, 'Settings', 'Plugin configuration'), pages)
})

test('a package without presentation declarations uses its standard form', () => {
  assert.deepEqual(settingsPages({ options: [{ key: 'enabled' }] }, 'Settings', 'Plugin configuration'), [{ id: 'standard', kind: 'native', title: { en: 'Settings' }, options: ['enabled'] }])
  assert.deepEqual(settingsPages(undefined, 'Settings', 'Plugin configuration'), [])
})

import assert from 'node:assert/strict'
import test from 'node:test'
import { libraryCards, libraryRelease, libraryUpdate } from '../src/plugins/library.ts'

const release = (version, available = false, selected = false, game = 'bubblefighter') => ({
  game, id: 'numeric-health', version, available,
  selection: selected ? { id: 'numeric-health', version, enabled: true, required: false, options: {} } : null,
})

test('the library shows one card before any client is added', () => {
  const cards = libraryCards([release('2.0.0'), release('1.0.0')], {})
  assert.equal(cards.length, 1)
  assert.equal(cards[0]?.version, '2.0.0')
})

test('selected versions and explicit rollback browsing keep one plugin card', () => {
  const packages = [release('3.0.0'), release('2.0.0', true, true), release('1.0.0', true)]
  assert.equal(libraryCards(packages, {})[0]?.version, '2.0.0')
  assert.equal(libraryCards(packages, { 'bubblefighter:numeric-health': '1.0.0' })[0]?.version, '1.0.0')
  assert.equal(libraryCards(packages, { 'bubblefighter:numeric-health': 'missing' })[0]?.version, '2.0.0')
})

test('compatible releases are preferred while separate games remain isolated', () => {
  const cards = libraryCards([release('2.0.0'), release('1.0.0', true), release('1.0.0', false, false, 'other')], {})
  assert.equal(cards.length, 2)
  assert.equal(cards[0]?.version, '1.0.0')
  assert.equal(cards[1]?.game, 'other')
})

test('update hints and activation cannot cross games with identical plugin IDs', () => {
  const current = release('1.0.0', true)
  const other = release('1.0.0', false, false, 'other')
  const update = { id: current.id, installedVersion: '1.0.0', status: 'available', version: '2.0.0' }
  assert.equal(libraryUpdate(current, 'bubblefighter', [update]), update)
  assert.equal(libraryUpdate(other, 'bubblefighter', [update]), undefined)
  assert.equal(libraryUpdate(current, undefined, [update]), undefined)
  const wrong = release('2.0.0', false, false, 'other')
  const correct = release('2.0.0', true)
  assert.equal(libraryRelease([wrong, correct], current, update.version), correct)
  assert.equal(libraryRelease([wrong], current, update.version), undefined)
  assert.equal(libraryRelease([correct], current, undefined), undefined)
})

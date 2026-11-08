import assert from 'node:assert/strict'
import { test } from 'node:test'
import { newestFirst } from '../src/store/versions.ts'

test('newly appended releases become the default with newest versions at the top', () => {
  const original = ['0.2.7', '0.2.9-rc.1', '0.2.10', '0.2.9'].map(version => ({ version }))
  assert.deepEqual(newestFirst(original).map(p => p.version), ['0.2.10', '0.2.9', '0.2.9-rc.1', '0.2.7'])
  assert.equal(original[0].version, '0.2.7')
})
test('prerelease numeric identifiers, stable releases and metadata follow precedence', () => {
  const releases = ['1.0.0-rc.2', '1.0.0-rc.10', '1.0.0', '1.0.0+build', '1.0.0-alpha'].map(version => ({ version }))
  assert.deepEqual(newestFirst(releases).map(p => p.version), ['1.0.0', '1.0.0+build', '1.0.0-rc.10', '1.0.0-rc.2', '1.0.0-alpha'])
})

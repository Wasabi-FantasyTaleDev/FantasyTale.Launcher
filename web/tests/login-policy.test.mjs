import assert from 'node:assert/strict'
import test from 'node:test'
import { resolveLoginPolicy } from '../src/accounts/login-policy.ts'

test('ticket clients still prompt for login when a server advertises optional or none', () => {
  assert.equal(resolveLoginPolicy('required', 'optional', 'optional'), 'required')
  assert.equal(resolveLoginPolicy('required', 'optional', 'none'), 'required')
})

test('client-owned and optional login retain server preference and definition fallback', () => {
  assert.equal(resolveLoginPolicy('optional', 'required', 'none'), 'none')
  assert.equal(resolveLoginPolicy('optional', 'required', 'required'), 'required')
  assert.equal(resolveLoginPolicy('optional', 'required'), 'optional')
  assert.equal(resolveLoginPolicy(undefined, 'required'), 'required')
  assert.equal(resolveLoginPolicy(undefined, undefined), 'optional')
})


test('only explicit rooms can use plain names for a ticket-required build', () => {
  assert.equal(resolveLoginPolicy('required', 'required', 'none', true), 'none')
  assert.equal(resolveLoginPolicy('required', 'required', 'required', true), 'required')
  assert.equal(resolveLoginPolicy('required', 'required', undefined, true), 'required')
  assert.equal(resolveLoginPolicy('required', 'required', 'none', false), 'required')
})

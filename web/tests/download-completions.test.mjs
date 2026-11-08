import assert from 'node:assert/strict'
import { test } from 'node:test'
import { acknowledgeClientLaunch, dismissedCompletionIds } from '../src/downloads/completions.ts'

const task = (id, kind='client', stage='completed', instanceId='one', buildId='build') => ({ id, stage, target:{ kind, instanceId, buildId, gameId:'fixture' } })
test('one successful launch acknowledges only completed work for that client', () => {
  dismissedCompletionIds.clear()
  const tasks=[task('client'),task('plugin','plugin'),task('other','client','completed','two'),task('pending','client','downloading'),task('launcher','launcher'),task('build','client','completed',undefined),task('old-build','client','completed',undefined,'other')]
  tasks[5].target.instanceId=undefined;tasks[6].target.instanceId=undefined
  acknowledgeClientLaunch(tasks,'one','fixture','build')
  assert.deepEqual([...dismissedCompletionIds].sort(),['build','client','plugin'])
  assert.equal(tasks.length,7)
  assert.equal(tasks[0].stage,'completed')
  tasks.push(task('later'))
  assert.equal(dismissedCompletionIds.has('later'),false)
  acknowledgeClientLaunch(tasks,'one','fixture','build')
  assert.equal(dismissedCompletionIds.has('later'),true)
})
test('a different game and removed history do not retain acknowledgements', () => {
  dismissedCompletionIds.clear();dismissedCompletionIds.add('gone')
  acknowledgeClientLaunch([task('client')],'one','other-game','build')
  assert.deepEqual([...dismissedCompletionIds],[])
})

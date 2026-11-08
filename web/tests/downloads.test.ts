import { test } from 'node:test'
import assert from 'node:assert/strict'
import { canCancel, canPause, downloadStageKey, formatBytes, matchesClient, transferMetrics, type DownloadTask } from '../src/downloads/model.ts'

const task = (extra: Partial<DownloadTask> = {}): DownloadTask => ({
  id: 'task', mode: 'live', target: { kind: 'client', gameId: 'sample', buildId: 'build-1', instanceId: 'copy-a', name: 'Sample', fromVersion: '1', toVersion: '2', sourceName: 'Source', sourceKind: 'alist', directory: 'D:\\Games\\Sample' },
  stage: 'downloading', files: [{ path: 'large.pack', size: 900, received: 450, stage: 'downloading' }, { path: 'small.pack', size: 100, received: 100, stage: 'downloaded' }],
  currentPath: 'large.pack', bytesPerSecond: 50, speedSampleCount: 3, stageProgress: null, failure: null, resumeStage: 'downloading', ...extra,
})
test('first package installs use installation wording while upgrades and self-updates retain update wording', () => {
  for (const kind of ['plugin', 'client', 'server', 'launcher'] as const) {
    for (const stage of ['checking', 'downloading', 'installing', 'completed', 'failed'] as const) {
      const value = task({ stage }); value.target = { ...value.target, kind, fromVersion: '' }
      assert.equal(downloadStageKey(value), `${kind === 'launcher' ? 'updates' : 'installation'}.${stage}`)
      value.target.fromVersion = '1.0.0'
      assert.equal(downloadStageKey(value), `updates.${stage}`)
    }
  }
})
test('total progress is weighted by bytes, with an independent file progress and ETA', () => {
  const metrics = transferMetrics(task())
  assert.ok(Math.abs(metrics.percent! - 55) < 1e-9)
  assert.equal(metrics.currentPercent, 50)
  assert.equal(metrics.remainingSeconds, 9)
  assert.equal(metrics.transferred, 1)
})
test('pause and non-transfer stages never display stale speed or a stale ETA', () => {
  for (const stage of ['paused', 'verifying', 'installing', 'ready', 'failed', 'canceled', 'completed'] as const) {
    const metrics = transferMetrics(task({ stage }))
    assert.equal(metrics.speed, 0)
    assert.equal(metrics.remainingSeconds, null)
  }
  assert.equal(transferMetrics(task({ speedSampleCount: 2 })).remainingSeconds, null)
  assert.equal(transferMetrics(task({ bytesPerSecond: 0 })).remainingSeconds, null)
})
test('unknown lengths do not manufacture a percentage or completion time', () => {
  const value = task(); value.files[0].size = null
  const metrics = transferMetrics(value)
  assert.equal(metrics.percent, null)
  assert.equal(metrics.currentPercent, null)
  assert.equal(metrics.remainingSeconds, null)
  assert.equal(transferMetrics(task({ files: [] })).percent, null)
})
test('bounded presentation handles restored offsets and invalid speed samples', () => {
  const value = task({ bytesPerSecond: NaN }); value.files[0].received = 1200
  assert.equal(transferMetrics(value).percent, 100)
  assert.equal(transferMetrics(value).speed, 0)
  value.files[0].received = -20
  assert.equal(transferMetrics(value).received, 100)
  assert.equal(formatBytes(null, 'en'), '—')
  assert.equal(formatBytes(0, 'en'), '0 B')
})
test('client updates stay attached to an installed copy across page and server changes', () => {
  const value = task()
  assert.equal(matchesClient(value, 'sample', 'copy-a', 'build-1'), true)
  assert.equal(matchesClient(value, 'sample', 'copy-b', 'build-1'), false)
  assert.equal(matchesClient(value, 'other-game', 'copy-a', 'build-1'), false)
  delete value.target.instanceId
  assert.equal(matchesClient(value, 'sample', undefined, 'build-1'), true)
  assert.equal(matchesClient(value, 'sample', undefined, 'build-2'), false)
})
test('installation cannot be paused or canceled halfway through replacement', () => {
  assert.equal(canPause(task()), true)
  assert.equal(canCancel(task()), true)
  assert.equal(canPause(task({ stage: 'installing' })), false)
  assert.equal(canCancel(task({ stage: 'installing' })), false)
  assert.equal(canCancel(task({ stage: 'ready' })), false)
})

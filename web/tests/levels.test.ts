import { test } from 'node:test'
import assert from 'node:assert/strict'
import { accountLevel } from '../src/levels.ts'
import type { AccountProfile, GameSummary, LevelIcon } from '../src/bridge.ts'

const icon = (path: string, x = 0, y = 0): LevelIcon => ({ path, x, y, width: 10, height: 10, sheetWidth: 512, sheetHeight: 512 })
// Synthetic thresholds and icon coordinates exercise the contract without exported game data.
const game = { levels: { displayOffset: 0, namesPending: false,
  entries: [[0, '0'], [1, '25'], [98, '1500000000'], [99, '1600000000'], [125, '52638337794']].map(([level, minimumExperience]) => ({
    level: Number(level), minimumExperience: String(minimumExperience), name: null, icons: { default: icon('levels/ordinary.png') },
  })),
  badges: { piero: { iconId: 10000, name: 'Piero', icon: icon('levels/badges.png', 20, 30) },
    unknown: { iconId: 10001, name: '?', icon: icon('levels/badges.png') } },
} } as GameSummary
const player = (extra: Partial<AccountProfile> = {}): AccountProfile => ({ username: 'fixture', displayName: 'Fixture', stats: { level: 98, exp: 1545783800 }, levelIconUrl: null, ...extra })

test('piero overrides the icon while retaining the ordinary level, experience and progress', () => {
  const normal = accountLevel(game, player())!
  const piero = accountLevel(game, player({ levelIconId: 10000 }))!
  assert.equal(piero.label, normal.label)
  assert.equal(piero.experience, normal.experience)
  assert.equal(piero.next, normal.next)
  assert.equal(piero.progress, normal.progress)
  assert.equal(piero.badgeName, 'Piero')
  assert.equal(piero.icon.path, 'levels/badges.png')
  assert.equal(piero.icon.x, 20)
  assert.equal(piero.icon.y, 30)
  assert.equal(normal.icon.path, 'levels/ordinary.png')
})

test('explicit badge names and legacy presentation values do not consume the experience ladder', () => {
  assert.equal(accountLevel(game, player({ rankBadge: 'piero' }))!.badgeName, 'Piero')
  const legacy = accountLevel(game, player({ stats: { level: 10000, exp: 1545783800 } }))!
  assert.equal(legacy.label, '98')
  assert.equal(legacy.experience, 1545783800n)
  assert.equal(accountLevel(game, player({ levelIconId: 10001 }))!.badgeName, '?')
  assert.equal(accountLevel(game, player({ rankBadge: 'unrecognized' }))!.icon.path, 'levels/ordinary.png')
})

test('inclusive thresholds, 64-bit source experience and unknown server ranks remain distinct', () => {
  assert.equal(accountLevel(game, player({ stats: { exp: 24 } }))!.label, '0')
  assert.equal(accountLevel(game, player({ stats: { exp: 25 } }))!.label, '1')
  assert.equal(accountLevel(game, player({ stats: { exp: 52638337794 } }))!.label, '125')
  assert.equal(accountLevel(game, player({ stats: { level: 999, exp: 25 } })), null)
  assert.equal(accountLevel(game, player({ stats: { level: 98, exp: 0 } }))!.progress, null)
})

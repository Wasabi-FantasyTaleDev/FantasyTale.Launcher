import assert from 'node:assert/strict'
import { test } from 'node:test'
import { readFileSync } from 'node:fs'
import { parse } from './helpers/toml.mjs'
import { serverDescription, mergeServerPresentation, serverIconReference, serverGallery } from '../src/serverDescription.ts'

test('cards use short introductions and details use full introductions in the same language', () => {
  const presentation = { motdTranslations: { en: 'Full English', 'zh-CN': 'Full Chinese\nSecond line' }, summaryTranslations: { en: 'Short English', 'zh-CN': 'Short Chinese' } }
  assert.equal(serverDescription(presentation, 'zh-CN'), 'Full Chinese\nSecond line')
  assert.equal(serverDescription(presentation, 'zh-CN', '', true), 'Short Chinese')
  assert.equal(serverDescription(presentation, 'ko'), 'Full English')
})

test('either introduction alone serves both surfaces and translated fallback stays in the requested language', () => {
  assert.equal(serverDescription({ summaryTranslations: { ko: 'Korean' } }, 'ko'), 'Korean')
  assert.equal(serverDescription({ motdTranslations: { ko: 'Korean' } }, 'ko', '', true), 'Korean')
  assert.equal(serverDescription({ motdTranslations: { ko: 'Full Korean' }, summaryTranslations: { en: 'Short English' } }, 'ko', '', true), 'Full Korean')
  assert.equal(serverDescription({ motd: 'Plain full' }, 'ko', '', true), 'Plain full')
  assert.equal(serverDescription({ summary: 'Plain short' }, 'ko'), 'Plain short')
  assert.equal(serverDescription({ motdTranslations: { ko: 'Only Korean' } }, 'vi'), 'Only Korean')
  assert.equal(serverDescription({ motdTranslations: { en: '   ' } }, 'vi', 'Directory description'), 'Directory description')
})

test('partial remote metadata preserves local translations while replacing only matching translations', () => {
  const local = { motdTranslations: { en: 'Local English', ko: 'Local Korean' }, summaryTranslations: { ko: 'Short Korean' }, images: ['local.png'] }
  const merged = mergeServerPresentation(local, { motdTranslations: { en: 'Remote English' }, onlinePlayers: 0, rosterEnabled: true })
  assert.equal(serverDescription(merged, 'en'), 'Remote English')
  assert.equal(serverDescription(merged, 'ko'), 'Local Korean')
  assert.equal(serverDescription(merged, 'ko', '', true), 'Short Korean')
  assert.equal(merged.onlinePlayers, 0)
  assert.equal(merged.rosterEnabled, true)
  assert.deepEqual(merged.images, ['local.png'])
})

test('bundled Chinese description alone includes QQ while other launcher languages fall back to English', () => {
  const game = parse(readFileSync(new URL('../../games/BubbleFighter/game.toml', import.meta.url), 'utf8')).game
  const profile = parse(readFileSync(new URL('../../games/BubbleFighter/servers/FantasyTale.toml', import.meta.url), 'utf8'))
  const presentation = { motdTranslations: profile.presentation.motd_translations, summaryTranslations: profile.presentation.summary_translations }
  assert.deepEqual(Object.keys(presentation.motdTranslations).sort(), ['en', 'zh'])
  for (const language of ['zh-CN', 'zh-TW']) assert.match(serverDescription(presentation, language), /681849714/)
  for (const language of ['en', 'ko', 'id', 'vi']) {
    assert.doesNotMatch(serverDescription(presentation, language), /681849714|QQ群/)
    assert.match(serverDescription(presentation, language), /Wasabi/)
  }
  assert.equal(game.name.vi, 'BF Online')
})

test('explicit remote empty values and visibility changes remain authoritative after the bridge merge', () => {
  const local = { motd: 'Saved description', summary: 'Saved summary', iconUrl: 'saved.png', images: ['saved.png'], rosterEnabled: true, whitelist: true, playerLimit: 50 }
  const merged = mergeServerPresentation(local, { motd: '', summary: '', iconUrl: null, images: [], rosterEnabled: false, whitelist: false, playerLimit: null, onlinePlayers: -1 })
  assert.equal(merged.motd, '')
  assert.equal(merged.summary, '')
  assert.equal(merged.iconUrl, null)
  assert.deepEqual(merged.images, [])
  assert.equal(merged.rosterEnabled, false)
  assert.equal(merged.whitelist, false)
  assert.equal(merged.playerLimit, null)
  assert.equal(merged.onlinePlayers, -1)
  assert.deepEqual(local.images, ['saved.png'])
})

test('icons and galleries resolve independently and empty translations fall back to configured resources', () => {
  const presentation = { iconUrlTranslations: { 'zh-CN': 'cn.png', en: 'en.png' }, imagesTranslations: { ko: [], vi: ['vi.png'] } }
  assert.equal(serverIconReference(presentation, 'zh-CN'), 'cn.png')
  assert.equal(serverIconReference(presentation, 'ko'), 'en.png')
  assert.deepEqual(serverGallery(presentation, 'zh-CN'), ['vi.png'])
  assert.deepEqual(serverGallery(presentation, 'ko'), ['vi.png'])
  assert.equal(serverIconReference({}, 'en'), undefined)
  assert.deepEqual(serverGallery({}, 'en'), [])
})

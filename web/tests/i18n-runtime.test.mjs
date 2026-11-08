import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { runInNewContext } from 'node:vm'
import test from 'node:test'
import ts from 'typescript'
import { createI18n } from 'vue-i18n'
import { createSSRApp, h } from 'vue'
import { renderToString } from '@vue/server-renderer'

function loadApplicationMessages({ stored = null, system = 'en' } = {}) {
  const cache = new Map()
  const external = createRequire(import.meta.url)
  function load(path) {
    if (cache.has(path)) return cache.get(path)
    const exports = {}
    cache.set(path, exports)
    const source = ts.transpileModule(readFileSync(path, 'utf8'), {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
    }).outputText
    const require = id => {
      if (id.startsWith('.')) {
        const target = resolve(dirname(path), id)
        if (id.endsWith('.json') || !id.endsWith('.ts') && existsSync(`${target}.json`)) {
          const jsonPath = id.endsWith('.json') ? target : `${target}.json`
          return JSON.parse(readFileSync(jsonPath, 'utf8'))
        }
        return load(id.endsWith('.ts') ? target : `${target}.ts`)
      }
      return external(id)
    }
    runInNewContext(source, { exports, require, localStorage: { getItem: () => stored }, navigator: { language: system } }, { filename: path })
    return exports
  }
  return load(fileURLToPath(new URL('../src/i18n/index.ts', import.meta.url)))
}

function flattenMessages(messages, prefix = '', out = {}) {
  for (const [key, value] of Object.entries(messages)) {
    const path = prefix ? `${prefix}.${key}` : key
    if (value && typeof value === 'object') flattenMessages(value, path, out)
    else out[path] = value
  }
  return out
}

test('every selectable locale defines all UI messages and retains their interpolation parameters', () => {
  const { i18n, languages } = loadApplicationMessages()
  const baseline = flattenMessages(i18n.global.getLocaleMessage('en'))
  const params = message => [...message.matchAll(/\{([A-Za-z0-9_]+)\}/g)].map(match => match[1]).sort()
  for (const language of languages) {
    const messages = flattenMessages(i18n.global.getLocaleMessage(language))
    for (const [key, message] of Object.entries(baseline)) {
      assert.equal(typeof messages[key], 'string', `${language}: ${key}`)
      assert.ok(messages[key].trim(), `${language}: ${key}`)
      assert.deepEqual(params(messages[key]), params(message), `${language}: ${key}`)
    }
    i18n.global.locale.value = language
    assert.match(i18n.global.t('pluginUpdates.found', { count: 3 }), /3/, language)
    assert.match(i18n.global.t('launcherUpdates.downloadBody', { version: '0.1.60', current: '0.1.59' }), /0\.1\.60/, language)
    assert.match(i18n.global.t('launcherUpdates.downloadBody', { version: '0.1.60', current: '0.1.59' }), /0\.1\.59/, language)
  }
})

test('Thai system detection, saved preferences and dynamic metadata fallback remain independent', () => {
  const thai = loadApplicationMessages({ system: 'th-TH' })
  assert.equal(thai.i18n.global.locale.value, 'th')
  assert.equal(thai.localized({ th: 'ไทย', en: 'English' }), 'ไทย')
  assert.equal(thai.localized({ en: 'English' }), 'English')
  assert.equal(thai.localized({ 'zh-CN': '中文' }), '中文')
  const metadata = { en: 'China 2013-12-18' }
  assert.equal(thai.localized(metadata), 'China 2013-12-18')
  assert.equal(thai.localized(metadata, { dates: true }), 'China 2556-12-18')
  thai.i18n.global.locale.value = 'en'
  assert.equal(thai.localized(metadata, { dates: true }), 'China 2013-12-18')
  assert.deepEqual(metadata, { en: 'China 2013-12-18' })
  const saved = loadApplicationMessages({ stored: 'ko', system: 'th-TH' })
  assert.equal(saved.i18n.global.locale.value, 'ko')
  const invalid = loadApplicationMessages({ stored: 'invalid', system: 'th-TH' })
  assert.equal(invalid.i18n.global.locale.value, 'th')
})

test('the actual seven-language catalog preserves named parameters and renders them as text', async () => {
  const { i18n, languages } = loadApplicationMessages()
  assert.deepEqual([...languages], ['zh-CN', 'zh-TW', 'ko', 'id', 'vi', 'th', 'en'])
  for (const locale of languages) {
    i18n.global.locale.value = locale
    const template = i18n.global.getLocaleMessage(locale).settings.version
    const value = '<iframe srcdoc="<b>sample</b>"></iframe> & 한국어 中文 Tiếng Việt ภาษาไทย'
    const translated = i18n.global.t('settings.version', { v: value })
    assert.equal(translated, template.replace('{v}', value), locale)
    const html = await renderToString(createSSRApp({ render: () => h('p', translated) }))
    assert.doesNotMatch(html, /<iframe|<b>/i, locale)
    assert.match(html, /&lt;iframe/, locale)
    assert.ok(i18n.global.te('resourceConflicts.title', locale), locale)
  }
})

test('the pinned translator escapes both list and named values without mutating caller data', () => {
  const translator = createI18n({ legacy: false, locale: 'en', escapeParameter: true,
    messages: { en: { mixed: '{0} {name}' } } }).global
  const named = { name: '<b>named</b>' }
  const translated = translator.t('mixed', ['<i>list</i>'], { named })
  assert.doesNotMatch(translated, /<\/?[bi]>/)
  assert.match(translated, /&lt;b&gt;/)
  assert.match(translated, /&lt;i&gt;/)
  assert.deepEqual(named, { name: '<b>named</b>' })
  assert.equal(translator.t('mixed', ['<i>list</i>'], { named }), translated)
})

test('the pinned translator neutralizes embedded documents and script markup in HTML messages', () => {
  const translator = createI18n({ legacy: false, locale: 'en', escapeParameter: true, warnHtmlMessage: false,
    messages: { en: { document: '<iframe srcdoc="<b>sample</b>"></iframe>', script: '<script>void(0)</script>' } } }).global
  assert.doesNotMatch(translator.t('document'), /\ssrcdoc\s*=/i)
  assert.doesNotMatch(translator.t('script'), /<script\b/i)
})

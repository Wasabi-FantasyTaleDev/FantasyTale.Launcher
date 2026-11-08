import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { parse as parseToml } from './helpers/toml.mjs'

function evaluate(source, require) {
  const javascript = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText
  const module = { exports: {} }
  runInNewContext(javascript, { module, exports: module.exports, require,
    localStorage: { getItem: () => null, setItem() {} }, window: { innerWidth: 1280 } })
  return module.exports
}

const { navEntries } = evaluate(readFileSync(new URL('../src/router.ts', import.meta.url), 'utf8'), id => {
  if (id === 'vue-router') return { createRouter: () => ({}), createWebHashHistory() {} }
  if (id.endsWith('.vue')) return { default: {} }
  throw new Error(`Unexpected dependency ${id}`)
})

const games = ['BubbleFighter', 'AirRider', 'TalesRunner'].map(folder =>
  parseToml(readFileSync(new URL(`../../games/${folder}/game.toml`, import.meta.url), 'utf8')).game)

test('routed pages retain one element root so leaving hosting cannot stall the out-in transition', () => {
  const routerSource = readFileSync(new URL('../src/router.ts', import.meta.url), 'utf8')
  const pages = [...routerSource.matchAll(/^import \w+ from '\.\/pages\/([^']+\.vue)'/gm)].map(match => match[1])
  for (const page of pages) {
    const template = parse(readFileSync(new URL(`../src/pages/${page}`, import.meta.url), 'utf8')).descriptor.template
    const roots = template.ast.children.filter(node => node.type === 1)
    assert.equal(roots.length, 1, `${page} must expose one transitionable root`)
    assert.equal(roots[0].tagType, 0, `${page} must use an element as its routed root`)
  }
})

function setup(game = 'bubblefighter', page = 'home') {
  const prefs = vue.reactive({ developerMode: false })
  const state = vue.reactive({ selectedGameId: game, loaded: true }), supportsPlugins = vue.ref(true)
  const definitions = vue.reactive(structuredClone(games))
  const selectedGame = vue.computed(() => {
    const declaration = definitions.find(entry => entry.id === state.selectedGameId)
    return declaration && { navigationEntries: declaration.navigation_entries, localHostingEnabled: declaration.local_hosting_enabled }
  })
  const navigationEntries = vue.computed(() => selectedGame.value?.navigationEntries ?? ['home', 'downloads', 'accounts', 'settings'])
  const route = vue.reactive({ name: page }), redirects = [], scope = vue.effectScope()
  const router = { replace: async target => { redirects.push(target.name); route.name = target.name }, push: async () => {} }
  const source = compileScript(parse(readFileSync(new URL('../src/components/AppNav.vue', import.meta.url), 'utf8')).descriptor, { id: 'AppNav' }).content.replaceAll('import.meta.env.DEV', 'false')
  const component = evaluate(source, id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount() {} }
    if (id === 'vue-router') return { useRoute: () => route, useRouter: () => router }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../router') return { navEntries }
    if (id === '../state') return { state, navigationEntries, selectedGame }
    if (id === '../theme') return { prefs }
    if (id === '../store/background') return { availableStoreUpdates: vue.ref([]) }
    if (id === '../store/preview') return { previewStoreUpdateCount: vue.ref() }
    if (id === '../bridge') return { inHost: true }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  })
  const bindings = scope.run(() => component.default.setup({}, { expose() {} }))
  const tags = () => [...bindings.mainItems.value, ...bindings.footerItems.value].map(entry => entry.tag)
  return { state, route, redirects, definitions, supportsPlugins, prefs, tags, bindings, dispose: () => scope.stop() }
}

test('developer mode adds one usable destination after all main entries and removes it when disabled', () => {
  const fixture = setup()
  try {
    assert.equal(fixture.bindings.mainItems.value.at(-1).tag, 'accounts')
    fixture.prefs.developerMode = true
    assert.equal(fixture.bindings.mainItems.value.at(-1).tag, 'developer')
    assert.equal(fixture.bindings.mainItems.value.at(-2).tag, 'accounts')
    assert.equal(fixture.bindings.disabled('developer'), false)
    fixture.route.name = 'developer'
    assert.equal(fixture.route.name, 'developer')
    fixture.state.selectedGameId = 'airrider'
    assert.equal(fixture.bindings.mainItems.value.at(-1).tag, 'developer')
    assert.equal(fixture.route.name, 'developer')
    fixture.prefs.developerMode = false
    assert.equal(fixture.bindings.mainItems.value.some(entry => entry.tag === 'developer'), false)
    assert.equal(fixture.bindings.disabled('developer'), true)
    assert.equal(fixture.route.name, 'home')
  } finally { fixture.dispose() }
})

test('developer bookmarks cannot expose the workspace while developer mode is off', () => {
  const fixture = setup('bubblefighter', 'developer')
  try {
    assert.equal(fixture.route.name, 'home')
    fixture.route.name = 'developer'
    assert.equal(fixture.route.name, 'home')
    assert.equal(fixture.redirects.length, 2)
  } finally { fixture.dispose() }
})

test('game switches retain exactly the shared entries and restore the complete pane', () => {
  const fixture = setup()
  try {
    const complete = fixture.tags()
    assert.deepEqual(complete, Array.from(navEntries, entry => entry.tag))
    for (const game of ['airrider', 'talesrunner', 'future-game']) {
      fixture.state.selectedGameId = game
      assert.deepEqual(fixture.tags(), ['home', 'downloads', 'accounts', 'settings'])
    }
    fixture.state.selectedGameId = 'bubblefighter'
    assert.deepEqual(fixture.tags(), complete)
  } finally { fixture.dispose() }
})

test('switching games and navigating through history cannot retain an unavailable page', () => {
  for (const entry of navEntries.filter(entry => !['home', 'downloads', 'accounts', 'settings'].includes(entry.tag))) {
    const fixture = setup('bubblefighter', entry.tag)
    try {
      fixture.state.selectedGameId = 'airrider'
      assert.equal(fixture.route.name, 'home')
      fixture.route.name = entry.tag
      assert.equal(fixture.route.name, 'home')
      assert.equal(fixture.redirects.length, 2)
    } finally { fixture.dispose() }
  }
})

test('a saved restricted route redirects on startup but shared pages survive game switches', () => {
  const saved = setup('airrider', 'plugins')
  try { assert.equal(saved.route.name, 'home') } finally { saved.dispose() }
  for (const page of ['home', 'downloads', 'accounts', 'settings']) {
    const fixture = setup('bubblefighter', page)
    try {
      fixture.state.selectedGameId = 'airrider'
      assert.equal(fixture.route.name, page)
      assert.equal(fixture.redirects.length, 0)
    } finally { fixture.dispose() }
  }
})

test('changing a game declaration enables a menu without a game-specific UI branch', () => {
  const fixture = setup('airrider')
  try {
    fixture.definitions.find(game => game.id === 'airrider').navigation_entries.push('store')
    assert.deepEqual(fixture.tags(), ['home', 'store', 'downloads', 'accounts', 'settings'])
    fixture.route.name = 'store'
    assert.equal(fixture.redirects.length, 0)
  } finally { fixture.dispose() }
})

test('initial navigation waits for the game catalog before deciding a saved page is unavailable', () => {
  const fixture = setup()
  try {
    fixture.state.loaded = false
    fixture.state.selectedGameId = 'airrider'
    fixture.route.name = 'store'
    assert.equal(fixture.route.name, 'store')
    fixture.state.loaded = true
    assert.equal(fixture.route.name, 'home')
  } finally { fixture.dispose() }
})

test('server plugin restrictions cannot hide a declared plugin library entry', () => {
  const fixture = setup('bubblefighter', 'plugins')
  try {
    fixture.supportsPlugins.value = false
    assert.ok(fixture.tags().includes('plugins'))
    assert.equal(fixture.route.name, 'plugins')
  } finally { fixture.dispose() }
})

test('local hosting stays visible but disabled until its declaration is enabled', () => {
  const fixture = setup()
  try {
    assert.ok(fixture.tags().includes('hosting'))
    fixture.definitions.find(game => game.id === 'bubblefighter').local_hosting_enabled = false
    assert.equal(fixture.bindings.disabled('hosting'), true)
    assert.equal(fixture.bindings.disabled('multiplayer'), false)
    fixture.definitions.find(game => game.id === 'bubblefighter').local_hosting_enabled = true
    assert.equal(fixture.bindings.disabled('hosting'), false)
  } finally { fixture.dispose() }
})

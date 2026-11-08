import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { policyPlugins, policySources } from '../src/clientPolicy.ts'
import { accountLevel } from '../src/levels.ts'
import { serverImageUrl } from '../src/serverArtwork.ts'
import { serverDescription, mergeServerPresentation, descriptionLanguages, serverGallery } from '../src/serverDescription.ts'

function pending() {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
async function settle() { await Promise.resolve(); await vue.nextTick(); await vue.nextTick() }
const rank = { path: 'rank.png', x: 0, y: 0, width: 20, height: 20, sheetWidth: 20, sheetHeight: 20 }
const game = { id: 'fixture', builds: [{ id: 'build', name: 'Readable client name' }], levels: {
  badges: {}, displayOffset: 1, entries: [{ level: 2, minimumExperience: '0', icons: { default: rank } }],
} }
function setup(file, initialProps, reply = async () => ({ schema: 1, enabled: true, players: [] })) {
  const props = vue.reactive(initialProps), state = vue.reactive({ games: [game], selectedGameId: game.id, selectedBuildId: 'build', instances: [], servers: [], busy: false })
  const calls = [], emitted = [], unmount = [], scope = vue.effectScope(), locale = vue.ref('zh-CN')
  const source = compileScript(parse(readFileSync(new URL(`../src/components/${file}.vue`, import.meta.url), 'utf8')).descriptor, { id: file }).content
  const javascript = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, useId: () => 'fixture-region', onMounted() {}, onBeforeUnmount: fn => unmount.push(fn) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key, locale }) }
    if (id === '../state') return { state, selectedGame: vue.computed(() => state.games[0]), refreshServers: async () => {}, perform: async action => { await action(); return true } }
    if (id === '../i18n') return { localized: value => value, languages: ['zh-CN', 'zh-TW', 'ko', 'id', 'vi', 'th', 'en'] }
    if (id === '../serverDescription') return { serverDescription, mergeServerPresentation, descriptionLanguages, serverGallery }
    if (id === '../levels') return { accountLevel }
    if (id === '../serverArtwork') return { serverImageUrl }
    if (id === '../clientPolicy') return { policyPlugins, policySources }
    if (id === '../bridge') return { inHost: true, call: async (method, args) => { calls.push({ method, args }); return reply(method, args) } }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(javascript, { module, exports: module.exports, require, URL, document: { removeEventListener() {} } })
  const bindings = scope.run(() => module.exports.default.setup(props, { expose() {}, emit: (...args) => emitted.push(args) }))
  return { props, state, locale, bindings, calls, emitted, dispose() { unmount.forEach(fn => fn()); scope.stop() } }
}
const entry = () => ({ id: 'entry', details: { name: 'Server', game: 'fixture', builds: ['build'], presentation: { rosterEnabled: true } } })

test('description editor starts in the launcher language and keeps drafts when switching languages', async () => {
  const fixture = setup('ServerDialog', { open: false, server: { id: 'fixture', name: 'Saved', apiUrl: 'https://game.example/', presentation: { motdTranslations: { en: 'English' } } } }, (_, args) => args)
  try {
    fixture.props.open = true; await settle()
    assert.equal(fixture.bindings.descriptionLanguage.value, 'zh-CN')
    assert.equal(fixture.bindings.motd.value, '')
    fixture.bindings.motd.value = 'Full Chinese\nSecond line'
    fixture.bindings.summary.value = 'Short Chinese'
    fixture.bindings.descriptionLanguage.value = 'en'
    assert.equal(fixture.bindings.motd.value, 'English')
    fixture.bindings.summary.value = 'Short English'
    fixture.bindings.descriptionLanguage.value = 'ko'
    fixture.bindings.motd.value = '  '
    fixture.bindings.descriptionLanguage.value = 'zh-CN'
    assert.equal(fixture.bindings.motd.value, 'Full Chinese\nSecond line')
    await fixture.bindings.save()
    const saved = fixture.calls.find(call => call.method === 'servers.save').args.presentation
    assert.deepEqual({ ...saved.motdTranslations }, { en: 'English', 'zh-CN': 'Full Chinese\nSecond line' })
    assert.deepEqual({ ...saved.summaryTranslations }, { 'zh-CN': 'Short Chinese', en: 'Short English' })
    assert.equal(fixture.props.server.presentation.motdTranslations.en, 'English')
  } finally { fixture.dispose() }
})

test('details fallback to a short introduction and react to launcher language changes', async () => {
  const value = entry(); value.details.presentation.summaryTranslations = { zh: 'Chinese short', en: 'English short' }
  const fixture = setup('ServerDetailsDialog', { open: true, entry: value })
  try {
    assert.equal(fixture.bindings.motd.value, 'Chinese short')
    fixture.locale.value = 'ko'
    assert.equal(fixture.bindings.motd.value, 'English short')
  } finally { fixture.dispose() }
})

test('local artwork resolves against the desktop resource origin while remote artwork stays unchanged', () => {
  const reference = '/server-media/' + 'a'.repeat(64) + '.png'
  assert.equal(serverImageUrl(reference, 'https://fantasytale.invalid/games/fixture/'), 'https://fantasytale.invalid' + reference)
  assert.equal(serverImageUrl(reference, 'app://localhost/games/fixture/'), 'app://localhost' + reference)
  assert.equal(serverImageUrl('https://images.example/icon.png', 'https://fantasytale.invalid/games/fixture/'), 'https://images.example/icon.png')
  assert.equal(serverImageUrl(reference), reference)
})

test('gallery selection appends multiple local files without replacing remote artwork or adding duplicates', async () => {
  const first = pending()
  const fixture = setup('ServerDialog', { open: false }, () => first.promise)
  try {
    fixture.props.open = true
    await settle()
    fixture.bindings.images.value = 'https://images.example/existing.png'
    const selection = fixture.bindings.chooseArtwork('gallery')
    assert.equal(fixture.calls[0].method, 'shell.importServerImages')
    assert.equal(fixture.calls[0].args.multiple, true)
    assert.equal(fixture.bindings.locked.value, true)
    await fixture.bindings.chooseArtwork('icon')
    assert.equal(fixture.calls.length, 1)
    first.resolve(['/server-media/a.png', '/server-media/b.jpg', '/server-media/a.png'])
    await selection
    assert.equal(fixture.bindings.images.value, 'https://images.example/existing.png\n/server-media/a.png\n/server-media/b.jpg')
    assert.equal(fixture.bindings.locked.value, false)
  } finally { fixture.dispose() }
})

test('canceling a native image chooser preserves fields and selecting an icon replaces only the icon', async () => {
  const responses = [[], ['/server-media/new.png']]
  const fixture = setup('ServerDialog', { open: false, server: { name: 'Saved', presentation: { iconUrl: 'https://images.example/icon.png', images: ['https://images.example/gallery.png'] } } }, () => responses.shift())
  try {
    fixture.props.open = true
    await settle()
    await fixture.bindings.chooseArtwork('icon')
    assert.equal(fixture.calls[0].args.multiple, false)
    assert.equal(fixture.bindings.iconUrl.value, 'https://images.example/icon.png')
    await fixture.bindings.chooseArtwork('icon')
    assert.equal(fixture.bindings.iconUrl.value, '/server-media/new.png')
    assert.equal(fixture.bindings.images.value, 'https://images.example/gallery.png')
    assert.equal(fixture.bindings.artworkError.value, '')
  } finally { fixture.dispose() }
})

test('a chooser finishing after closing and reopening cannot overwrite the new draft or unlock its active chooser', async () => {
  const first = pending(), second = pending()
  const fixture = setup('ServerDialog', { open: false }, () => fixture.calls.length === 1 ? first.promise : second.promise)
  try {
    fixture.props.open = true
    await settle()
    const oldSelection = fixture.bindings.chooseArtwork('icon')
    fixture.props.open = false
    await settle()
    fixture.props.open = true
    await settle()
    const newSelection = fixture.bindings.chooseArtwork('icon')
    first.resolve(['/server-media/stale.png'])
    await oldSelection
    assert.equal(fixture.bindings.iconUrl.value, '')
    assert.equal(fixture.bindings.locked.value, true)
    second.resolve(['/server-media/current.png'])
    await newSelection
    assert.equal(fixture.bindings.iconUrl.value, '/server-media/current.png')
    assert.equal(fixture.bindings.locked.value, false)
  } finally { fixture.dispose() }
})

test('changing games ignores pending image selection and unsupported images show a localized error', async () => {
  const first = pending()
  const fixture = setup('ServerDialog', { open: false }, () => fixture.calls.length === 1 ? first.promise : Promise.reject(new Error('server_image_format_unsupported')))
  try {
    fixture.props.open = true
    await settle()
    const selection = fixture.bindings.chooseArtwork('gallery')
    fixture.state.selectedGameId = 'another-game'
    await settle()
    first.resolve(['/server-media/stale.png'])
    await selection
    assert.equal(fixture.bindings.images.value, '')
    assert.equal(fixture.bindings.locked.value, false)
    await fixture.bindings.chooseArtwork('icon')
    assert.equal(fixture.bindings.artworkError.value, 'serverInfo.unsupportedImage')
    assert.equal(fixture.bindings.locked.value, false)
  } finally { fixture.dispose() }
})

test('disabled player lists disappear from navigation and cannot remain the active tab', async () => {
  const fixture = setup('ServerDetailsDialog', { open: true, busy: false, entry: entry() })
  try {
    fixture.bindings.tab.value = 'players'
    fixture.props.entry.details.presentation.rosterEnabled = false
    await settle()
    assert.deepEqual(Array.from(fixture.bindings.tabs.value, item => item.id), ['information', 'versions'])
    assert.equal(fixture.bindings.tab.value, 'information')
    assert.equal(fixture.bindings.buildOptions.value[0].label, 'Readable client name')
    fixture.props.entry.details.presentation.rosterEnabled = true
    await settle()
    assert.equal(fixture.bindings.tabs.value.at(-1).id, 'players')
  } finally { fixture.dispose() }
})

test('plugin navigation follows declared rules and closes a removed plugin tab', async () => {
  const value = entry()
  value.details.clientPolicy = { version: 1, sources: [{ name: 'source-only', kind: 'http', baseUrl: 'https://plugins.example/', catalogPath: 'source.toml' }], plugins: [] }
  const fixture = setup('ServerDetailsDialog', { open: true, busy: false, entry: value })
  try {
    assert.equal(fixture.bindings.hasPlugins.value, false)
    assert.equal(fixture.bindings.tabs.value.some(item => item.id === 'plugins'), false)
    fixture.props.entry.details.clientPolicy = { version: 1, sources: [], plugins: [{ id: 'disabled-plugin', rules: [{ id: 'off', type: 'disable', builds: [] }] }] }
    await settle()
    assert.equal(fixture.bindings.hasPlugins.value, true)
    assert.equal(fixture.bindings.tabs.value.some(item => item.id === 'plugins'), true)
    assert.equal(fixture.bindings.pluginCounts.value.disabled, 1)
    fixture.bindings.tab.value = 'plugins'
    fixture.props.entry.details.clientPolicy = { version: 1, sources: [], plugins: [] }
    await settle()
    assert.equal(fixture.bindings.hasPlugins.value, false)
    assert.equal(fixture.bindings.tab.value, 'information')
  } finally { fixture.dispose() }
})

test('supported versions have their own tab and keep the footer selection valid when server builds change', async () => {
  const fixture = setup('ServerDetailsDialog', { open: true, busy: false, entry: entry() })
  try {
    assert.equal(fixture.bindings.tabs.value.some(item => item.id === 'versions'), true)
    fixture.bindings.tab.value = 'versions'
    fixture.props.entry.details.builds = ['unknown-build']
    await settle()
    assert.equal(fixture.bindings.tab.value, 'versions')
    assert.equal(fixture.bindings.versions.value.length, 1)
    assert.equal(fixture.bindings.versions.value[0].available, false)
    assert.equal(fixture.bindings.buildId.value, '')
    fixture.props.entry.details.builds = ['build']
    await settle()
    assert.equal(fixture.bindings.buildId.value, 'build')
    fixture.props.open = false
    await settle()
    assert.equal(fixture.bindings.tab.value, 'information')
  } finally { fixture.dispose() }
})

test('only an installed client for the same game and build hides the add-client action', async () => {
  const fixture = setup('ServerDetailsDialog', { open: true, busy: false, entry: entry() })
  try {
    assert.equal(fixture.bindings.versions.value[0].installed, false)
    fixture.state.instances.push({ game: 'fixture', build: 'build', clientInstalled: false })
    fixture.state.instances.push({ game: 'another-game', build: 'build', clientInstalled: true })
    fixture.state.instances.push({ game: 'fixture', build: 'another-build', clientInstalled: true })
    assert.equal(fixture.bindings.versions.value[0].installed, false)
    fixture.state.instances.push({ game: 'fixture', build: 'build', clientInstalled: true })
    await settle()
    assert.equal(fixture.bindings.versions.value[0].installed, true)
    fixture.state.instances.pop()
    await settle()
    assert.equal(fixture.bindings.versions.value[0].installed, false)
  } finally { fixture.dispose() }
})

test('unadvertised or subsequently disabled rosters do not leave a details entry', async () => {
  const value = entry()
  delete value.details.presentation.rosterEnabled
  const fixture = setup('ServerDetailsDialog', { open: true, busy: false, entry: value })
  try {
    assert.equal(fixture.bindings.rosterEnabled.value, false)
    fixture.props.entry.details.presentation.rosterEnabled = true
    await settle()
    assert.equal(fixture.bindings.rosterEnabled.value, true)
    fixture.bindings.tab.value = 'players'
    fixture.bindings.rosterUnavailable.value = true
    await settle()
    assert.equal(fixture.bindings.tabs.value.some(item => item.id === 'players'), false)
    assert.equal(fixture.bindings.tab.value, 'information')
  } finally { fixture.dispose() }
})

test('a late server catalog cannot replace the selected server images or privacy settings', async () => {
  const first = pending(), second = pending()
  const fixture = setup('ServerDetailsDialog', { open: true, busy: false, server: { id: 'first', game: 'fixture' } }, (_, args) => args.id === 'first' ? first.promise : second.promise)
  try {
    fixture.props.server = { id: 'second', game: 'fixture' }
    await settle()
    second.resolve({ instances: [], presentation: { images: ['new.png'], rosterEnabled: false } })
    await settle()
    first.resolve({ instances: [], presentation: { images: ['stale.png'], rosterEnabled: true } })
    await settle()
    assert.equal(fixture.bindings.image.value, 'new.png')
    assert.equal(fixture.bindings.tabs.value.some(item => item.id === 'players'), false)
    assert.equal(fixture.bindings.loading.value, false)
  } finally { fixture.dispose() }
})

test('failed gallery images are removed and the viewer closes when no usable image remains', async () => {
  const value = entry()
  value.details.presentation.images = ['one.png', 'two.png']
  const fixture = setup('ServerDetailsDialog', { open: true, busy: false, entry: value })
  try {
    fixture.bindings.imageIndex.value = 1
    fixture.bindings.imageOpen.value = true
    fixture.bindings.excludeImage('two.png')
    await settle()
    assert.equal(fixture.bindings.imageIndex.value, 0)
    assert.equal(fixture.bindings.image.value, 'one.png')
    fixture.bindings.excludeImage('one.png')
    await settle()
    assert.equal(fixture.bindings.images.value.length, 0)
    assert.equal(fixture.bindings.imageOpen.value, false)
    fixture.props.entry = { ...entry(), id: 'another-entry' }
    await settle()
    assert.equal(fixture.bindings.failedImages.value.length, 0)
  } finally { fixture.dispose() }
})

test('lightbox arrow handling wraps only while open and leaves modifier shortcuts alone', () => {
  const fixture = setup('ImageViewer', { open: true, images: ['a', 'b', 'c'], index: 0, title: 'Server' })
  try {
    let prevented = 0
    const key = (name, extra = {}) => ({ key: name, preventDefault() { ++prevented }, stopPropagation() {}, ...extra })
    fixture.bindings.onArrow(key('ArrowLeft'))
    assert.deepEqual(fixture.emitted.at(-1), ['update:index', 2])
    fixture.bindings.onArrow(key('ArrowRight', { altKey: true }))
    fixture.props.open = false
    fixture.bindings.onArrow(key('ArrowRight'))
    assert.equal(prevented, 1)
    assert.equal(fixture.emitted.length, 1)
  } finally { fixture.dispose() }
})

test('large player lists render a bounded grid, retain actual ranks and reset deep scrolling on normalized search', async () => {
  const players = Array.from({ length: 10001 }, (_, index) => ({ name: index === 0 ? 'Alice' : `Player${index}`, level: index % 2 ? null : 2 }))
  const fixture = setup('OnlinePlayersPanel', { active: true, game: 'fixture', serverId: 'server' }, async () => ({ enabled: true, players }))
  try {
    await settle()
    assert.equal(fixture.bindings.entries.value.length, 10001)
    assert.ok(fixture.bindings.visible.value.length <= 24)
    assert.equal(fixture.bindings.visible.value[0].rank.label, '3')
    assert.equal(fixture.bindings.visible.value[0].rank.icon.path, 'rank.png')
    assert.equal(fixture.bindings.visible.value[1].rank, null)
    fixture.bindings.scrollTop.value = 100000
    assert.ok(fixture.bindings.first.value > 3000)
    fixture.bindings.query.value = 'ＡＬＩＣＥ'
    await settle()
    assert.equal(fixture.bindings.scrollTop.value, 0)
    assert.equal(fixture.bindings.visible.value.length, 1)
    assert.equal(fixture.bindings.visible.value[0].name, 'Alice')
    fixture.bindings.query.value = ''
    fixture.bindings.columns.value = 1
    await settle()
    fixture.bindings.scrollTop.value = fixture.bindings.contentHeight.value - 336
    assert.equal(fixture.bindings.visible.value.at(-1).name, 'Player10000')
  } finally { fixture.dispose() }
})

test('player cards display the known client build name and retain unknown or omitted builds safely', async () => {
  const players = [{ name: 'Known', buildId: 'build' }, { name: 'Future', buildId: 'future-build' }, { name: 'Legacy' }]
  const fixture = setup('OnlinePlayersPanel', { active: true, game: 'fixture', serverId: 'server' }, async () => ({ enabled: true, players }))
  try {
    await settle()
    assert.equal(fixture.bindings.entries.value[0].buildLabel, 'Readable client name')
    assert.equal(fixture.bindings.entries.value[1].buildLabel, 'future-build')
    assert.equal(fixture.bindings.entries.value[2].buildLabel, '')
    fixture.props.active = false
    await settle()
    assert.equal(fixture.bindings.entries.value.length, 0)
  } finally { fixture.dispose() }
})

test('a disabled or changed roster never exposes previously fetched player names', async () => {
  const first = pending(), second = pending()
  const fixture = setup('OnlinePlayersPanel', { active: true, game: 'fixture', serverId: 'first' }, (_, args) => args.id === 'first' ? first.promise : second.promise)
  try {
    fixture.props.serverId = 'second'
    await settle()
    second.resolve({ enabled: false, players: [{ name: 'Private player', level: 2 }] })
    await settle()
    first.resolve({ enabled: true, players: [{ name: 'Stale player', level: 2 }] })
    await settle()
    assert.equal(fixture.bindings.entries.value.length, 0)
    assert.equal(fixture.bindings.loading.value, false)
    assert.deepEqual(fixture.emitted, [['availability', false]])
    fixture.props.active = false
    await settle()
    assert.equal(fixture.bindings.roster.value, undefined)
  } finally { fixture.dispose() }
})

test('file import does not overwrite different saved data until confirmed and uses the inspected fingerprint', async () => {
  const fixture = setup('ServerProfileActions', { game: 'fixture', disabled: false }, async method => {
    if (method === 'shell.pickServerProfile') return 'profile-toml'
    if (method === 'servers.inspectProfile') return { exists: true, identical: false, fingerprint: 'saved-state', server: { name: 'Existing' } }
    if (method === 'servers.importProfile') return { id: 'imported', game: 'fixture' }
  })
  try {
    await fixture.bindings.importProfile()
    assert.equal(fixture.calls.some(call => call.method === 'servers.importProfile'), false)
    assert.equal(fixture.bindings.pending.value.text, 'profile-toml')
    fixture.bindings.pending.value = undefined
    assert.equal(fixture.emitted.length, 0)
    await fixture.bindings.importProfile()
    await fixture.bindings.overwrite()
    const imported = fixture.calls.find(call => call.method === 'servers.importProfile')
    assert.equal(imported.args.expectedFingerprint, 'saved-state')
    assert.equal(imported.args.text, 'profile-toml')
    assert.equal(fixture.emitted[0][0], 'saved')
  } finally { fixture.dispose() }
})

test('canceled file selection is a no-op and a pending selection cannot import into another game', async () => {
  const selection = pending()
  const fixture = setup('ServerProfileActions', { game: 'fixture', disabled: false }, () => selection.promise)
  try {
    const picking = fixture.bindings.importProfile()
    fixture.props.game = 'other-game'; await settle()
    selection.resolve('profile-toml'); await picking
    assert.equal(fixture.calls.length, 1)
    assert.equal(fixture.emitted.length, 0)
    assert.equal(fixture.bindings.pending.value, undefined)
  } finally { fixture.dispose() }
  const canceled = setup('ServerProfileActions', { game: 'fixture', disabled: false }, async () => null)
  try { await canceled.bindings.importProfile(); assert.equal(canceled.calls.length, 1); assert.equal(canceled.emitted.length, 0) }
  finally { canceled.dispose() }
})

test('export passes the saved TOML and suggested filename directly to the native save dialog', async () => {
  const fixture = setup('ServerProfileActions', { game: 'fixture', disabled: false, server: { id: 'saved' } }, async method =>
    method === 'servers.exportProfile' ? { text: 'profile-toml', fileName: '127.0.0.1_8080.toml' } : true)
  try {
    await fixture.bindings.exportProfile()
    assert.equal(fixture.calls[0].method, 'servers.exportProfile')
    assert.equal(fixture.calls[1].method, 'shell.saveServerProfile')
    assert.equal(fixture.calls[1].args.text, 'profile-toml')
    assert.equal(fixture.calls[1].args.fileName, '127.0.0.1_8080.toml')
    assert.equal(fixture.calls.some(call => call.method === 'servers.importProfile'), false)
  } finally { fixture.dispose() }
})


test('new server IDs are optional, file-safe and checked against saved identities without changing their case', async () => {
  const fixture = setup('ServerDialog', { open: false }, (_, args) => args)
  try {
    fixture.props.open = true; await settle()
    fixture.bindings.name.value = 'Community'
    fixture.bindings.apiUrl.value = 'https://game.example/'
    assert.equal(fixture.bindings.generatedId.value, 'game_example_443')
    fixture.bindings.apiUrl.value = 'https://[::1]/'
    assert.equal(fixture.bindings.generatedId.value, '__1_443')
    fixture.bindings.host.value = '127.0.0.1'; fixture.bindings.port.value = '0080'
    assert.equal(fixture.bindings.generatedId.value, '127_0_0_1_80')
    fixture.bindings.host.value = ''; fixture.bindings.port.value = ''; fixture.bindings.apiUrl.value = 'https://game.example/'
    assert.equal(fixture.bindings.valid.value, true)
    await fixture.bindings.save()
    assert.equal(fixture.calls.at(-1).args.id, '')
    assert.equal(fixture.calls.at(-1).args.createOnly, true)
    fixture.bindings.serverId.value = 'COM0'
    assert.equal(fixture.bindings.valid.value, true)
    fixture.bindings.serverId.value = 'Community_Server-2'
    assert.equal(fixture.bindings.valid.value, true)
    await fixture.bindings.save()
    assert.equal(fixture.calls.at(-1).args.id, 'Community_Server-2')
    for (const id of ['has space', 'server.example', '../outside', '服务器', 'café', 'CON', 'LPT1']) {
      fixture.bindings.serverId.value = id
      assert.equal(fixture.bindings.valid.value, false, id)
    }
    fixture.state.servers = [{ id: 'Community_Server-2' }]
    fixture.bindings.serverId.value = 'community_server-2'
    assert.equal(fixture.bindings.idError.value, 'serverInfo.idExists')
    fixture.state.servers = [{ id: 'game_example_443' }]
    fixture.bindings.serverId.value = ''
    assert.equal(fixture.bindings.idError.value, 'serverInfo.idExists')
  } finally { fixture.dispose() }
})

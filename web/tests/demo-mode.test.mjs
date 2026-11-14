import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, resolve } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

function loadDemoModules({ stored = null, dev = true, mode = 'development', nativeHost = false } = {}) {
  const storage = new Map()
  if (stored !== null) storage.set('uiDemoMode', stored)

  const timers = new Map()
  let nextTimerId = 1
  const fakeWindow = {
    setInterval(fn, ms) {
      const id = nextTimerId++
      timers.set(id, { fn, ms, interval: true })
      return id
    },
    clearInterval(id) {
      timers.delete(id)
    },
    setTimeout(fn, ms) {
      const id = nextTimerId++
      timers.set(id, { fn, ms, interval: false })
      return id
    },
    clearTimeout(id) {
      timers.delete(id)
    },
  }

  const emitted = []
  const external = nativeHost ? { sendMessage() {} } : {}
  const cache = new Map()
  const externalRequire = createRequire(import.meta.url)

  function loadModule(filePath) {
    if (cache.has(filePath)) return cache.get(filePath)
    if (filePath.endsWith('.json')) {
      const parsed = JSON.parse(readFileSync(filePath, 'utf8'))
      cache.set(filePath, parsed)
      return parsed
    }
    const raw = readFileSync(filePath, 'utf8')
    const rewritten = raw
      .replaceAll('(import.meta as ImportMeta & { env?: { DEV?: boolean; MODE?: string } }).env', 'globalThis.__importMetaEnv')
      .replaceAll('import.meta.env', 'globalThis.__importMetaEnv')
    const js = ts.transpileModule(rewritten, {
      compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, esModuleInterop: true },
    }).outputText

    const module = { exports: {} }
    cache.set(filePath, module.exports)

    const require = id => {
      if (id.startsWith('.')) {
        const base = resolve(dirname(filePath), id)
        const candidates = [base, `${base}.ts`, `${base}.tsx`, resolve(base, 'index.ts')]
        for (const candidate of candidates) {
          try {
            readFileSync(candidate)
            return loadModule(candidate)
          } catch {
            // Try next candidate extension.
          }
        }
      }
      return externalRequire(id)
    }

    const sandbox = {
      module,
      exports: module.exports,
      require,
      console,
      URL,
      Blob,
      setTimeout: fakeWindow.setTimeout,
      clearTimeout: fakeWindow.clearTimeout,
      setInterval: fakeWindow.setInterval,
      clearInterval: fakeWindow.clearInterval,
      localStorage: {
        getItem: key => (storage.has(key) ? storage.get(key) : null),
        setItem: (key, value) => storage.set(key, String(value)),
      },
      external,
      window: fakeWindow,
      __importMetaEnv: { DEV: dev, MODE: mode },
    }
    sandbox.globalThis = sandbox
    runInNewContext(js, sandbox, { filename: filePath })
    return module.exports
  }

  const srcRoot = fileURLToPath(new URL('../src', import.meta.url))
  const modeMod = loadModule(resolve(srcRoot, 'demo/mode.ts'))
  const fixturesMod = loadModule(resolve(srcRoot, 'demo/fixtures.ts'))
  const hostMod = loadModule(resolve(srcRoot, 'demo/host.ts'))
  const emit = (event, data) => emitted.push({ event, data })
  const host = { call: (method, params) => hostMod.demoHostCall(method, params, emit) }

  return {
    modeMod,
    fixturesMod,
    hostMod,
    host,
    emitted,
    storage,
    timers,
    tickIntervals() {
      for (const entry of [...timers.values()]) {
        if (entry.interval) entry.fn()
      }
    },
  }
}

test('resolveDemoMode defaults on in npm run dev and honors explicit exit across environments', () => {
  const { modeMod } = loadDemoModules()
  assert.equal(modeMod.resolveDemoMode(null, true, false, false), true)
  assert.equal(modeMod.resolveDemoMode('off', true, false, false), false)
  assert.equal(modeMod.resolveDemoMode(null, true, true, false), false)
  assert.equal(modeMod.resolveDemoMode('on', true, true, false), true)
  assert.equal(modeMod.resolveDemoMode(null, false, false, false), false)
  assert.equal(modeMod.resolveDemoMode('on', false, false, false), true)
  assert.equal(modeMod.resolveDemoMode(null, true, false, true), false)
  assert.equal(modeMod.resolveDemoMode('on', true, false, true), true)
})

test('setDemoMode persists selection, notifies listeners, and supports exiting in dev and desktop host', () => {
  const devEnv = loadDemoModules({ stored: null, dev: true, nativeHost: false })
  assert.equal(devEnv.modeMod.demoMode.enabled, true)
  const changes = []
  const unsub = devEnv.modeMod.onDemoModeChange(v => changes.push(v))
  devEnv.modeMod.setDemoMode(false)
  assert.equal(devEnv.modeMod.demoMode.enabled, false)
  assert.equal(devEnv.storage.get('uiDemoMode'), 'off')
  devEnv.modeMod.setDemoMode(true)
  assert.equal(devEnv.modeMod.demoMode.enabled, true)
  assert.equal(devEnv.storage.get('uiDemoMode'), 'on')
  assert.deepEqual(changes, [false, true])
  unsub()

  const hostEnv = loadDemoModules({ stored: null, dev: true, nativeHost: true })
  assert.equal(hostEnv.modeMod.demoMode.enabled, false)
  hostEnv.modeMod.setDemoMode(true)
  assert.equal(hostEnv.modeMod.demoMode.enabled, true)
})

test('demo host serves rich stateful mock data across pages, animates installs, and resets cleanly', async () => {
  const env = loadDemoModules()
  const { host, hostMod, emitted, tickIntervals } = env

  const games = await host.call('games.list')
  assert.ok(Array.isArray(games) && games.length >= 2)

  const initialInstances = await host.call('instances.list')
  assert.ok(initialInstances.length >= 2)

  const added = await host.call('instances.create', {
    id: 'test-client-kr812',
    game: 'bubblefighter',
    name: 'Test Client',
    build: 'kr-812',
    installationPath: 'C:\\Games\\TestClient',
    language: 'zh-CN',
    checkIntegrity: false,
  })
  assert.equal(added.name, 'Test Client')
  const afterAdd = await host.call('instances.list')
  assert.equal(afterAdd.length, initialInstances.length + 1)

  const sources = await host.call('store.sources')
  assert.ok(sources.length >= 1)
  const catalog = await host.call('store.catalog', { id: sources[0].id })
  const clientPkg = catalog.packages.find(pkg => pkg.kind === 'client')
  assert.ok(clientPkg)

  const task = hostMod.demoController.triggerInstallDemo('client')
  assert.equal(task.target.kind, 'client')
  const beforeBytes = task.files.reduce((sum, f) => sum + f.received, 0)
  for (let i = 0; i < 6; i++) tickIntervals()
  const listed = await host.call('downloads.list')
  const updated = listed.find(item => item.id === task.id)
  assert.ok(updated)
  const afterBytes = updated.files.reduce((sum, f) => sum + f.received, 0)
  assert.ok(afterBytes > beforeBytes || updated.stage !== task.stage, 'download ticker should advance task progress')
  assert.ok(emitted.some(e => e.event === 'downloads.changed'))

  const paused = await host.call('downloads.action', { id: task.id, action: 'pause' })
  assert.equal(paused.find(item => item.id === task.id)?.stage, 'paused')
  const resumed = await host.call('downloads.action', { id: task.id, action: 'resume' })
  assert.notEqual(resumed.find(item => item.id === task.id)?.stage, 'paused')

  const overview = await host.call('hosting.list')
  assert.ok(overview.profiles.length >= 1)

  env.modeMod.triggerDemoReset()
  const resetInstances = await host.call('instances.list')
  assert.equal(resetInstances.length, initialInstances.length)
})

test('no UI demo mode banner or badge appears in templates outside DeveloperPage and DemoModeTab', () => {
  const srcRoot = fileURLToPath(new URL('../src', import.meta.url))
  const vueFiles = [resolve(srcRoot, 'App.vue')]
  for (const dir of ['pages', 'components']) {
    for (const file of readdirSync(resolve(srcRoot, dir))) {
      if (!file.endsWith('.vue')) continue
      if (file === 'DeveloperPage.vue' || file === 'DemoModeTab.vue') continue
      vueFiles.push(resolve(srcRoot, dir, file))
    }
  }
  const forbidden = /\buiDemoMode\b|演示模式|Demo\s+Mode|developerMode\.demo|developerMode\.tabs\.demo/i
  for (const file of vueFiles) {
    const templateMatch = readFileSync(file, 'utf8').match(/<template>[\s\S]*<\/template>/)
    if (!templateMatch) continue
    assert.equal(forbidden.test(templateMatch[0]), false, `Unexpected demo indicator in ${file}`)
  }
})

test('demo mode exposes developer navigation entry by default even when developerMode preference is off', () => {
  const srcRoot = fileURLToPath(new URL('../src', import.meta.url))
  const prefs = vue.reactive({ developerMode: false })
  const demoMode = vue.reactive({ enabled: true })
  const state = vue.reactive({ selectedGameId: 'bubblefighter', loaded: true })
  const navigationEntries = vue.computed(() => ['home', 'store', 'plugins', 'multiplayer', 'hosting', 'servers', 'downloads', 'accounts', 'settings'])
  const selectedGame = vue.computed(() => ({ navigationEntries: navigationEntries.value, localHostingEnabled: true }))
  const route = vue.reactive({ name: 'home' })
  const scope = vue.effectScope()
  const compiled = compileScript(parse(readFileSync(resolve(srcRoot, 'components/AppNav.vue'), 'utf8')).descriptor, { id: 'AppNavDemoTest' })
    .content.replaceAll('import.meta.env.DEV', 'false')
  const js = ts.transpileModule(compiled, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText
  const module = { exports: {} }
  const require = id => {
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount() {} }
    if (id === 'vue-router') return { useRoute: () => route, useRouter: () => ({ replace: async target => { route.name = target.name }, push: async () => {} }) }
    if (id === 'vue-i18n') return { useI18n: () => ({ t: key => key }) }
    if (id === '../router') return { navEntries: [] }
    if (id === '../state') return { state, navigationEntries, selectedGame }
    if (id === '../theme') return { prefs }
    if (id === '../store/background') return { availableStoreUpdates: vue.ref([]) }
    if (id === '../store/preview') return { previewStoreUpdateCount: vue.ref() }
    if (id === '../downloads/live') return { downloadTasks: vue.ref([]) }
    if (id === '../bridge') return { inHost: false, demoMode }
    if (id.endsWith('.vue')) return { default: {} }
    throw new Error(`Unexpected dependency ${id}`)
  }
  runInNewContext(js, { module, exports: module.exports, require, localStorage: { getItem: () => null, setItem() {} }, window: { innerWidth: 1280 } })
  const bindings = scope.run(() => module.exports.default.setup({}, { expose() {} }))
  try {
    assert.equal(bindings.mainItems.value.some(entry => entry.tag === 'developer'), true)
    assert.equal(bindings.disabled('developer'), false)
    demoMode.enabled = false
    assert.equal(bindings.mainItems.value.some(entry => entry.tag === 'developer'), false)
  } finally {
    scope.stop()
  }
})

test('demo host pluginPolicy.check evaluates and resolves interactive findings', async () => {
  const { host, hostMod, tickIntervals } = loadDemoModules({ dev: true, mode: 'development', nativeHost: false })
  hostMod.demoController.preparePluginPolicyDemo('demo-bf-kr812')
  const initial = await host.call('pluginPolicy.check', { instanceId: 'demo-bf-kr812', language: 'en', launching: true })
  assert.ok(initial.some(f => f.status === 'disabled' && f.other === 'bubblefighter.fantasy.font-support-cn'))
  assert.ok(initial.some(f => f.status === 'missing' && f.other === 'bubblefighter.fantasy.wide-chat'))
  assert.ok(initial.some(f => f.status === 'version' && f.other === 'bubblefighter.fantasy.fps-unlock'))
  assert.ok(initial.some(f => f.status === 'conflict' && f.other === 'bubblefighter.fantasy.crash-reporter'))
  assert.ok(initial.some(f => f.type === 'recommend' && f.status === 'disabled'))

  await host.call('plugins.configure', { instanceId: 'demo-bf-kr812', id: 'bubblefighter.fantasy.font-support-cn', version: '1.0.0', enabled: true })
  await host.call('plugins.configure', { instanceId: 'demo-bf-kr812', id: 'bubblefighter.fantasy.crash-reporter', version: '1.1.0', enabled: false })
  await host.call('store.installPluginTargets', { sourceId: 'ft-official', packageId: 'bubblefighter.fantasy.wide-chat', version: '0.3.1', game: 'bubblefighter', instanceIds: ['demo-bf-kr812'] })
  await host.call('store.installPluginTargets', { sourceId: 'ft-official', packageId: 'bubblefighter.fantasy.fps-unlock', version: '0.3.0', game: 'bubblefighter', instanceIds: ['demo-bf-kr812'] })
  for (let i = 0; i < 60; i++) tickIntervals()

  const remaining = await host.call('pluginPolicy.check', { instanceId: 'demo-bf-kr812', language: 'en', launching: true })
  assert.equal(remaining.some(f => f.blocks), false)
  hostMod.demoController.clearPluginPolicyDemo()
})

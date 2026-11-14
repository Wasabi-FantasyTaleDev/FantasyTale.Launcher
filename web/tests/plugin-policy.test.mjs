import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { test } from 'node:test'
import { runInNewContext } from 'node:vm'
import { compileScript, parse } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as versions from '../src/store/versions.ts'

const updatesSource = readFileSync(new URL('../src/store/updates.ts', import.meta.url), 'utf8')
const updatesModule = { exports: {} }
runInNewContext(
  ts.transpileModule(updatesSource, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
  { module: updatesModule, exports: updatesModule.exports, require: id => id === './versions' ? versions : undefined },
)

const policySource = readFileSync(new URL('../src/plugins/pluginPolicy.ts', import.meta.url), 'utf8')
const dialogSource = readFileSync(new URL('../src/components/PluginPolicyDialog.vue', import.meta.url), 'utf8')
const dialogScript = compileScript(parse(dialogSource).descriptor, { id: 'PluginPolicyDialog' }).content

const flush = async () => {
  await new Promise(resolve => setImmediate(resolve))
  await vue.nextTick()
}

function loadPolicyEnv(respond) {
  const calls = []
  const state = vue.reactive({ instances: [], selectedInstanceId: 'client', selectedGameId: 'bubblefighter', selectedServerId: '', plugins: [] })
  const downloads = vue.reactive({ tasks: [] })
  const downloadTasks = vue.computed(() => downloads.tasks)
  const trackDownload = task => {
    downloads.tasks = [task, ...downloads.tasks.filter(item => item.id !== task.id)]
  }
  const downloadAction = async (task, action) => {
    calls.push({ method: 'downloads.action', params: { id: task.id, action } })
  }
  const refreshInstances = async () => {}
  const policyModule = { exports: {} }
  const bridgeMock = {
    inHost: true,
    call: async (method, params) => {
      calls.push({ method, params })
      return await respond(method, params)
    },
  }
  const policyJs = ts.transpileModule(policySource, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  runInNewContext(policyJs, {
    module: policyModule,
    exports: policyModule.exports,
    require: id => {
      if (id === 'vue') return vue
      if (id === '../bridge') return bridgeMock
      if (id === '../i18n') return { i18n: { global: { locale: vue.ref('zh-CN') } } }
      if (id === '../state') return { state, refreshInstances }
      if (id === '../store/updates') return updatesModule.exports
      throw new Error(`Unexpected policy dependency ${id}`)
    },
  })

  const scope = vue.effectScope()
  const dialogModule = { exports: {} }
  const dialogJs = ts.transpileModule(dialogScript, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  runInNewContext(dialogJs, {
    module: dialogModule,
    exports: dialogModule.exports,
    require: id => {
      if (id === 'vue') return vue
      if (id === 'vue-i18n') return { useI18n: () => ({ t: (key, vars) => vars ? `${key}:${JSON.stringify(vars)}` : key }) }
      if (id === '../bridge') return bridgeMock
      if (id === '../downloads/live') return { downloadTasks, trackDownload, downloadAction }
      if (id === '../i18n') return { localized: text => text?.['zh-CN'] ?? text?.en ?? '' }
      if (id === '../state') return { state, refreshInstances, prepareStorePluginInstallation: () => () => {} }
      if (id === '../store/versions') return versions
      if (id === '../store/updates') return updatesModule.exports
      if (id === '../plugins/pluginPolicy') return policyModule.exports
      if (id === '../plugins/policyPreview') return { isPolicyPreview: () => false }
      if (id.startsWith('../controls/') || id.endsWith('.vue')) return {}
      throw new Error(`Unexpected dialog dependency ${id}`)
    },
  })
  const bindings = scope.run(() => dialogModule.exports.default.setup({}, { expose() {} }))
  return { state, downloads, calls, policy: policyModule.exports, dialog: bindings, dispose: () => scope.stop() }
}

test('satisfiesRange enforces exact, bounding, and compound constraints', () => {
  assert.equal(versions.satisfiesRange('0.2.1', ''), true)
  assert.equal(versions.satisfiesRange('0.2.2', '>=0.2.0 <0.3.0'), true)
  assert.equal(versions.satisfiesRange('0.3.0', '>=0.2.0 <0.3.0'), false)
  assert.equal(versions.satisfiesRange('1.0.0', '1.0.0'), true)
  assert.equal(versions.satisfiesRange('1.0.1', '1.0.0'), false)
})

test('disabling a required dependency opens policy prompt and allows withdrawing or disabling dependents first', async () => {
  let banEnabled = true
  let checkCount = 0
  const env = loadPolicyEnv(async (method, params) => {
    if (method === 'pluginPolicy.check') {
      checkCount++
      if (!params.disabling || !banEnabled) return []
      return [{
        plugin: 'bubblefighter.fantasy.banpick',
        pluginVersion: '0.2.1',
        pluginName: { en: 'Ban Rules', 'zh-CN': '禁用规则' },
        pluginRequired: false,
        pluginActive: true,
        type: 'require',
        other: 'bubble.fighter.fantasy.nirinsoulink',
        range: '',
        status: 'disabled',
        blocks: true,
        defaultSelected: false,
        target: { id: 'bubble.fighter.fantasy.nirinsoulink', version: '0.2.2', name: { en: 'Nirinsou Link' }, kind: 'service' },
      }]
    }
    if (method === 'plugins.configure' && params.id === 'bubblefighter.fantasy.banpick') {
      banEnabled = params.enabled
      return { id: 'client' }
    }
  })
  try {
    const keepRequest = env.policy.requestPluginPolicy('client', { disabling: { id: 'bubble.fighter.fantasy.nirinsoulink', version: '0.2.2' } })
    await flush()
    assert.equal(env.policy.policyPrompt.open, true)
    assert.equal(env.policy.policyPrompt.findings.length, 1)

    await env.policy.enableTarget(env.policy.policyPrompt.findings[0])
    assert.equal(env.policy.policyResolved(), true)
    await env.policy.continuePolicy()
    assert.equal(await keepRequest, false)

    const proceedRequest = env.policy.requestPluginPolicy('client', { disabling: { id: 'bubble.fighter.fantasy.nirinsoulink', version: '0.2.2' } })
    await flush()
    assert.equal(env.policy.policyPrompt.open, true)
    await env.policy.switchDependent(env.policy.policyPrompt.findings[0], false)
    assert.equal(banEnabled, false)
    assert.equal(env.policy.policyResolved(), true)
    await env.policy.continuePolicy()
    assert.equal(await proceedRequest, true)
    assert.ok(checkCount >= 4)
  } finally {
    env.dispose()
  }
})

test('PolicyDialog installs inline inside the card and settles after the download task completes', async () => {
  let installed = false
  const env = loadPolicyEnv(async (method, params) => {
    if (method === 'pluginPolicy.check') {
      if (installed) return []
      return [{
        plugin: 'bubblefighter.fantasy.banpick',
        pluginVersion: '0.2.1',
        pluginName: { en: 'Ban Rules', 'zh-CN': '禁用规则' },
        pluginRequired: false,
        pluginActive: true,
        type: 'require',
        other: 'bubblefighter.fantasy.communication',
        range: '>=0.2.0',
        status: 'missing',
        blocks: true,
        defaultSelected: false,
        target: null,
      }]
    }
    if (method === 'store.sources') {
      return [{ id: 'official', name: 'Official', kind: 'http', baseUrl: 'https://example.invalid', enabled: true, builtIn: true }]
    }
    if (method === 'store.catalog') {
      return {
        packages: [
          {
            id: 'bubble.fighter.fantasy.nirinsoulink',
            pluginDefinition: { id: 'bubble.fighter.fantasy.nirinsoulink', aliases: ['bubblefighter.fantasy.communication'] },
            kind: 'plugin',
            game: 'bubblefighter',
            builds: ['*'],
            version: '0.1.9',
            manifestSha256: '1'.repeat(64),
            launcherCompatible: true,
          },
          {
            id: 'bubble.fighter.fantasy.nirinsoulink',
            pluginDefinition: { id: 'bubble.fighter.fantasy.nirinsoulink', aliases: ['bubblefighter.fantasy.communication'] },
            kind: 'plugin',
            game: 'bubblefighter',
            builds: ['*'],
            version: '0.2.2',
            manifestSha256: '2'.repeat(64),
            launcherCompatible: true,
          },
          {
            id: 'bubble.fighter.fantasy.nirinsoulink',
            pluginDefinition: { id: 'bubble.fighter.fantasy.nirinsoulink', aliases: ['bubblefighter.fantasy.communication'] },
            kind: 'plugin',
            game: 'bubblefighter',
            builds: ['other-build'],
            version: '0.3.0',
            manifestSha256: '3'.repeat(64),
            launcherCompatible: true,
          },
        ],
      }
    }
    if (method === 'store.installPluginTargets') {
      assert.equal(params.packageId, 'bubble.fighter.fantasy.nirinsoulink')
      assert.equal(params.version, '0.2.2')
      assert.deepEqual([...params.instanceIds], ['client'])
      return {
        id: 'task-nirinsou',
        mode: 'live',
        target: {
          kind: 'plugin',
          packageId: params.packageId,
          gameId: 'bubblefighter',
          instanceId: 'client',
          name: 'Nirinsou Link',
          fromVersion: '',
          toVersion: '0.2.2',
          sourceName: 'Official',
          sourceKind: 'http',
          directory: 'plugins/nirinsoulink',
        },
        stage: 'downloading',
        files: [{ path: 'plugin.toml', size: 1000, received: 500, stage: 'downloading' }],
        currentPath: 'plugin.toml',
        bytesPerSecond: 500,
        speedSampleCount: 3,
        stageProgress: null,
        failure: null,
        resumeStage: 'downloading',
      }
    }
    if (method === 'plugins.list') return env.state.plugins
  })
  try {
    env.state.instances = [{
      id: 'client', name: 'KR-1256', game: 'bubblefighter', build: 'kr-1256', clientInstalled: true,
      plugins: [{ id: 'bubblefighter.fantasy.banpick', version: '0.2.1', enabled: true, options: {} }],
    }]
    const pending = env.policy.requestPluginPolicy('client', { enabling: { id: 'bubblefighter.fantasy.banpick', version: '0.2.1' } })
    await flush()
    assert.equal(env.policy.policyPrompt.open, true)

    const card = env.policy.policyPrompt.cards[0]
    await env.dialog.triggerCardAction(card)
    assert.equal(env.policy.policyPrompt.error, '')
    assert.equal(env.dialog.showCardProgress(card), true)
    assert.equal(env.dialog.cardAction(card).kind, 'installing')
    assert.equal(env.dialog.cardAction(card).disabled, true)

    installed = true
    env.state.plugins = [{ id: 'bubble.fighter.fantasy.nirinsoulink', version: '0.2.2', kind: 'service', game: 'bubblefighter', available: true }]
    env.downloads.tasks[0].stage = 'completed'
    await flush()
    await flush()
    assert.equal(env.dialog.showCardProgress(card), false)
    assert.equal(env.dialog.cardAction(card).kind, 'enabled')
    assert.equal(env.policy.policyPrompt.findings.length, 0)
    assert.equal(env.policy.policyResolved(), true)

    env.policy.cancelPolicy()
    assert.equal(await pending, false)
  } finally {
    env.dispose()
  }
})

test('PolicyDialog groups multiple dependent plugins in the same dialog and keeps settled cards disabled as enabledDone', async () => {
  let nirinsouEnabled = false
  let previewsInstalled = false
  const env = loadPolicyEnv(async (method, params) => {
    if (method === 'pluginPolicy.check') {
      const findings = []
      if (!nirinsouEnabled) {
        findings.push({
          plugin: 'bubblefighter.fantasy.banpick',
          pluginVersion: '0.2.1',
          pluginName: { en: 'Ban Rules', 'zh-CN': '禁用规则' },
          pluginRequired: false,
          pluginActive: true,
          type: 'require',
          other: 'bubble.fighter.fantasy.nirinsoulink',
          range: '>=0.2.0',
          status: 'disabled',
          blocks: true,
          defaultSelected: false,
          target: {
            id: 'bubble.fighter.fantasy.nirinsoulink',
            version: '0.2.2',
            name: { en: 'Fantasy Tale Nirinsou Link', 'zh-CN': 'Fantasy Tale Nirinsou Link' },
            description: { en: 'IPC bridge.', 'zh-CN': '实现启动器与插件通信。' },
            kind: 'service',
          },
        })
      }
      if (!previewsInstalled) {
        findings.push({
          plugin: 'bubblefighter.fantasy.banpick',
          pluginVersion: '0.2.1',
          pluginName: { en: 'Ban Rules', 'zh-CN': '禁用规则' },
          pluginRequired: false,
          pluginActive: true,
          type: 'require',
          other: 'bubblefighter.fantasy.item-previews',
          range: '>=0.1.0',
          status: 'missing',
          blocks: true,
          defaultSelected: false,
          target: null,
        })
      }
      findings.push({
        plugin: 'bubblefighter.fantasy.mode-extension',
        pluginVersion: '0.1.25',
        pluginName: { en: 'Mode Extension', 'zh-CN': '模式扩展' },
        pluginRequired: false,
        pluginActive: true,
        type: 'require',
        other: 'bubblefighter.fantasy.game-ui-resources',
        range: '>=0.1.2',
        status: 'missing',
        blocks: true,
        defaultSelected: false,
        target: null,
      })
      return findings
    }
    if (method === 'store.sources') {
      return [{ id: 'official', name: 'Official', kind: 'http', baseUrl: 'https://example.invalid', enabled: true, builtIn: true }]
    }
    if (method === 'store.catalog') {
      return {
        packages: [
          {
            id: 'bubblefighter.fantasy.item-previews',
            kind: 'plugin',
            game: 'bubblefighter',
            builds: ['*'],
            version: '0.1.0',
            manifestSha256: 'a'.repeat(64),
            name: { en: 'Item Previews', 'zh-CN': '物品预览图' },
            description: { en: 'Item icons.', 'zh-CN': '为其他组件提供物品预览图片。' },
            launcherCompatible: true,
          },
          {
            id: 'bubblefighter.fantasy.game-ui-resources',
            kind: 'plugin',
            game: 'bubblefighter',
            builds: ['*'],
            version: '0.1.2',
            manifestSha256: 'b'.repeat(64),
            name: { en: 'Game Interface Graphics', 'zh-CN': '游戏界面素材' },
            description: { en: 'UI resources.', 'zh-CN': '提供游戏界面素材。' },
            launcherCompatible: true,
          },
        ],
      }
    }
    if (method === 'plugins.configure' && params.id === 'bubble.fighter.fantasy.nirinsoulink') {
      nirinsouEnabled = params.enabled
      return { id: 'client' }
    }
    if (method === 'plugins.list') return env.state.plugins
  })
  try {
    env.state.instances = [{
      id: 'client', name: 'KR-1256', game: 'bubblefighter', build: 'kr-1256', clientInstalled: true,
      plugins: [
        { id: 'bubblefighter.fantasy.banpick', version: '0.2.1', enabled: true, options: {} },
        { id: 'bubblefighter.fantasy.mode-extension', version: '0.1.25', enabled: true, options: {} },
      ],
    }]
    const pending = env.policy.requestPluginPolicy('client', { launching: true })
    await flush()
    await flush()
    assert.equal(env.policy.policyPrompt.open, true)
    assert.equal(env.policy.policyPrompt.cards.length, 3)
    assert.equal(env.dialog.blockingGroups.value.length, 2)
    assert.equal(env.dialog.blockingGroups.value[0].plugin, 'bubblefighter.fantasy.banpick')
    assert.equal(env.dialog.blockingGroups.value[1].plugin, 'bubblefighter.fantasy.mode-extension')

    const [nirinsouCard, previewCard, assetsCard] = env.policy.policyPrompt.cards
    assert.equal(env.dialog.cardName(nirinsouCard), 'Fantasy Tale Nirinsou Link')
    assert.equal(env.dialog.cardVersion(nirinsouCard), '0.2.2')
    assert.equal(env.dialog.cardDescription(nirinsouCard), '实现启动器与插件通信。')
    assert.equal(env.dialog.cardAction(nirinsouCard).kind, 'enable')
    assert.equal(env.dialog.cardAction(nirinsouCard).disabled, false)

    assert.equal(env.dialog.cardName(previewCard), '物品预览图')
    assert.equal(env.dialog.cardVersion(previewCard), '0.1.0')
    assert.equal(env.dialog.cardDescription(previewCard), '为其他组件提供物品预览图片。')
    assert.equal(env.dialog.cardAction(previewCard).kind, 'install')

    assert.equal(env.dialog.cardName(assetsCard), '游戏界面素材')
    assert.equal(env.dialog.cardVersion(assetsCard), '0.1.2')
    assert.equal(env.dialog.cardAction(assetsCard).kind, 'install')

    await env.dialog.triggerCardAction(nirinsouCard)
    assert.equal(env.policy.policyPrompt.cards.length, 3)
    assert.equal(env.dialog.cardAction(env.policy.policyPrompt.cards[0]).kind, 'enabled')
    assert.equal(env.dialog.cardAction(env.policy.policyPrompt.cards[0]).disabled, true)
    assert.equal(env.dialog.cardAction(env.policy.policyPrompt.cards[1]).kind, 'install')

    env.policy.cancelPolicy()
    assert.equal(await pending, false)
  } finally {
    env.dispose()
  }
})

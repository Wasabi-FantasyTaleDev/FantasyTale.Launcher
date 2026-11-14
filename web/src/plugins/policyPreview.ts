import type { Instance, PluginSummary } from '../bridge'
import { downloads } from '../downloads/live'
import type { DownloadTask } from '../downloads/model'
import type { StoreCatalog, StorePackage } from '../store/types'
import { defaultStoreSource } from '../store/preview'
import type { PolicyFinding } from './pluginPolicy'

const BAN_ID = 'bubblefighter.fantasy.banpick'
const MODE_ID = 'bubblefighter.fantasy.mode-extension'
const NIRINSOU_ID = 'bubble.fighter.fantasy.nirinsoulink'
const PREVIEWS_ID = 'bubblefighter.fantasy.item-previews'
const UI_ASSETS_ID = 'bubblefighter.fantasy.game-ui-resources'
const BRIDGE_ID = 'bubblefighter.fantasy.native-ui-bridge'

let banEnabled = true
let modeEnabled = true
let nirinsouEnabled = false
let previewsInstalled = false
let uiAssetsInstalled = false
let bridgeVersion = '0.1.18'
let bridgeEnabled = true
let taskCounter = 0

interface PreviewDownloadSpec {
  id: string
  fromVersion: string
  toVersion: string
  totalBytes: number
  files: { path: string; size: number }[]
  onComplete: () => void
}

const activeTimers = new Map<string, ReturnType<typeof setInterval>>()

const previewCatalogPackages: StorePackage[] = [
  {
    id: PREVIEWS_ID,
    kind: 'plugin',
    game: 'bubblefighter',
    builds: ['*'],
    version: '0.1.0',
    name: {
      'zh-CN': '物品预览图',
      'zh-TW': '物品預覽圖',
      en: 'Item Previews',
      ko: '아이템 미리보기',
      id: 'Pratinjau Item',
      vi: 'Ảnh xem trước vật phẩm',
      th: 'ภาพตัวอย่างไอเทม',
    },
    description: {
      'zh-CN': '为其他组件提供物品预览图片。本组件自身不改变游戏，会在需要它的组件启用时自动使用。',
      'zh-TW': '為其他元件提供物品預覽圖片。本元件本身不會改變遊戲，會在需要它的元件啟用時自動使用。',
      en: 'Provides item preview pictures for other components. It does not change the game by itself and is used automatically while a component that needs it is enabled.',
      ko: '다른 구성 요소에 아이템 미리보기 이미지를 제공합니다. 이 구성 요소 자체는 게임을 바꾸지 않으며, 필요로 하는 구성 요소가 켜져 있을 때 자동으로 사용됩니다.',
      id: 'Menyediakan gambar pratinjau item untuk komponen lain. Komponen ini tidak mengubah game sendiri dan dipakai otomatis selama komponen yang membutuhkannya aktif.',
      vi: 'Cung cấp ảnh xem trước vật phẩm cho các thành phần khác. Thành phần này tự nó không thay đổi trò chơi và được dùng tự động khi một thành phần cần đến nó được bật.',
    },
    publisher: 'Fantasy Tale Studio',
    size: 1_887_436,
    manifest: `plugins/${PREVIEWS_ID}/0.1.0/manifest.toml`,
    manifestSha256: 'a'.repeat(64),
    launcherCompatible: true,
  },
  {
    id: UI_ASSETS_ID,
    kind: 'plugin',
    game: 'bubblefighter',
    builds: ['*'],
    version: '0.1.2',
    name: {
      'zh-CN': '游戏界面素材',
      'zh-TW': '遊戲介面素材',
      en: 'Game Interface Graphics',
      ko: '게임 화면 그래픽',
      id: 'Grafis Antarmuka Game',
      vi: 'Đồ họa giao diện trò chơi',
      th: 'กราฟิกอินเทอร์เฟซเกม',
    },
    description: {
      'zh-CN': '为其他组件提供与游戏一致的窗口、按钮和字体，让它们在游戏中的界面看起来和原版一样。本组件自身不改变游戏，会在需要它的组件启用时自动使用。',
      'zh-TW': '為其他元件提供與遊戲一致的視窗、按鈕和字型，讓它們在遊戲中的介面看起來和原版一樣。本元件本身不會改變遊戲，會在需要它的元件啟用時自動使用。',
      en: "Provides the game's own windows, buttons and fonts so that other components look like the original game. It does not change the game by itself and is used automatically while a component that needs it is enabled.",
      ko: '다른 구성 요소가 원래 게임처럼 보이도록 게임의 창, 버튼, 글꼴을 제공합니다. 이 구성 요소 자체는 게임을 바꾸지 않으며, 필요로 하는 구성 요소가 켜져 있을 때 자동으로 사용됩니다.',
      id: 'Menyediakan jendela, tombol, dan huruf asli game agar komponen lain terlihat seperti game aslinya. Komponen ini tidak mengubah game sendiri dan dipakai otomatis selama komponen yang membutuhkannya aktif.',
      vi: 'Cung cấp cửa sổ, nút bấm và phông chữ của chính trò chơi để các thành phần khác trông giống bản gốc. Thành phần này tự nó không thay đổi trò chơi và được dùng tự động khi một thành phần cần đến nó được bật.',
    },
    publisher: 'Fantasy Tale Studio',
    size: 3_565_158,
    manifest: `plugins/${UI_ASSETS_ID}/0.1.2/manifest.toml`,
    manifestSha256: 'b'.repeat(64),
    launcherCompatible: true,
  },
  {
    id: BRIDGE_ID,
    kind: 'plugin',
    game: 'bubblefighter',
    builds: ['*'],
    version: '0.1.20',
    name: {
      'zh-CN': 'Fantasy Tale 游戏控件调用支持',
      'zh-TW': 'Fantasy Tale 遊戲控制項呼叫支援',
      en: 'Fantasy Tale Game Control Support',
      ko: 'Fantasy Tale 게임 컨트롤 호출 지원',
      id: 'Dukungan Kontrol Game Fantasy Tale',
      vi: 'Hỗ trợ gọi điều khiển trò chơi Fantasy Tale',
    },
    description: {
      'zh-CN': '启用后可在已适配的版本中调用原生组件能力',
      'zh-TW': '啟用後可在已適配的版本中呼叫原生元件功能',
      en: 'Enables native component capabilities in supported client builds.',
      ko: '지원되는 클라이언트 버전에서 기본 구성 요소 기능을 호출할 수 있습니다.',
      id: 'Memungkinkan pemanggilan kemampuan komponen bawaan pada versi klien yang didukung.',
      vi: 'Cho phép gọi chức năng thành phần gốc trên các phiên bản được hỗ trợ.',
    },
    publisher: 'Fantasy Tale Studio',
    size: 983_040,
    manifest: `plugins/${BRIDGE_ID}/0.1.20/manifest.toml`,
    manifestSha256: 'c'.repeat(64),
    launcherCompatible: true,
  },
]

function clearPreviewTimers(): void {
  for (const timer of activeTimers.values()) clearInterval(timer)
  activeTimers.clear()
  downloads.tasks = downloads.tasks.filter(task => !task.id.startsWith('policy-preview-'))
}

/** Explicit browser-only fixture (`?policyPreview=1`); never active in the desktop host or production builds. */
export const isPolicyPreview = (): boolean =>
  import.meta.env.DEV && typeof window !== 'undefined' && window.location.hash.includes('policyPreview=1')

export function resetPolicyPreview(): void {
  clearPreviewTimers()
  banEnabled = true
  modeEnabled = true
  nirinsouEnabled = false
  previewsInstalled = false
  uiAssetsInstalled = false
  bridgeVersion = '0.1.18'
  bridgeEnabled = true
}

export function previewPolicyInstances(): Instance[] {
  const plugins = [
    { id: BAN_ID, version: '0.2.1', enabled: banEnabled, required: false, options: {} },
    { id: MODE_ID, version: '0.1.25', enabled: modeEnabled, required: false, options: {} },
    { id: NIRINSOU_ID, version: '0.2.2', enabled: nirinsouEnabled, required: false, options: {} },
    { id: BRIDGE_ID, version: bridgeVersion, enabled: bridgeEnabled, required: false, options: {} },
  ]
  if (previewsInstalled) plugins.push({ id: PREVIEWS_ID, version: '0.1.0', enabled: true, required: false, options: {} })
  if (uiAssetsInstalled) plugins.push({ id: UI_ASSETS_ID, version: '0.1.2', enabled: true, required: false, options: {} })
  return [{
    id: 'preview-kr-1256',
    name: 'KR-1256 (Preview)',
    game: 'bubblefighter',
    build: 'kr-1256',
    endpoint: null,
    clientInstalled: true,
    plugins,
  }]
}

const previewBanPlugin: PluginSummary = {
  id: BAN_ID,
  game: 'bubblefighter',
  version: '0.2.1',
  kind: 'overlay',
  name: {
    'zh-CN': '禁用规则',
    'zh-TW': '禁用規則',
    en: 'Ban Rules',
    ko: '금지 규칙',
    id: 'Aturan Larangan',
    vi: 'Quy tắc cấm',
    th: 'กฎการแบน',
  },
  description: {
    'zh-CN': '进入房间后可选择本局禁用的武器、角色、服装和饰品等物品，同房玩家可随时查看当前禁用列表。',
    'zh-TW': '進入房間後可選擇本局禁用的武器、角色、服裝與飾品等道具，同房玩家可隨時查看目前禁用清單。',
    en: 'Pick banned weapons, characters, outfits, and accessories in a room so players can view the active room ban list.',
  },
  authors: ['Fantasy Tale Studio'],
  requires: [`${PREVIEWS_ID}@>=0.1.0`, `${UI_ASSETS_ID}@>=0.1.2`],
  policy: [
    { plugin: NIRINSOU_ID, type: 'require', range: '>=0.2.0', builds: [], languages: [], defaultSelected: false, satisfied: false },
    { plugin: PREVIEWS_ID, type: 'require', range: '>=0.1.0', builds: [], languages: [], defaultSelected: false, satisfied: false },
  ],
  available: true,
  required: false,
  selection: { id: BAN_ID, version: '0.2.1', enabled: true, required: false, options: {} },
  options: [],
}

const previewModePlugin: PluginSummary = {
  id: MODE_ID,
  game: 'bubblefighter',
  version: '0.1.25',
  kind: 'overlay',
  name: {
    'zh-CN': '模式扩展',
    'zh-TW': '模式擴充',
    en: 'Mode Extension',
    ko: '모드 확장',
    id: 'Ekstensi Mode',
    vi: 'Mở rộng chế độ',
    th: 'โหมดเพิ่มเติม',
  },
  description: {
    'zh-CN': '创建房间时选择服务器支持的全部模式，确认后才应用选择。需要当前客户端的原生控件支持。',
    'zh-TW': '建立房間時選擇伺服器支援的全部模式，確認後才套用選擇。需要目前用戶端的原生控制項支援。',
    en: 'Choose any server mode while creating a room. The choice is applied after confirmation and requires native UI support for the client.',
  },
  authors: ['Fantasy Tale Studio'],
  requires: [`${BRIDGE_ID}@>=0.1.20`, `${UI_ASSETS_ID}@>=0.1.2`],
  policy: [
    { plugin: BRIDGE_ID, type: 'require', range: '>=0.1.20', builds: [], languages: [], defaultSelected: false, satisfied: false },
    { plugin: UI_ASSETS_ID, type: 'require', range: '>=0.1.2', builds: [], languages: [], defaultSelected: false, satisfied: false },
  ],
  available: true,
  required: false,
  selection: { id: MODE_ID, version: '0.1.25', enabled: true, required: false, options: {} },
  options: [],
}

export function previewPolicyPlugins(base: PluginSummary[]): PluginSummary[] {
  const plugins = base.map(plugin => {
    if (plugin.id === NIRINSOU_ID) {
      return { ...plugin, selection: { id: NIRINSOU_ID, version: plugin.version, enabled: nirinsouEnabled, required: false, options: {} } }
    }
    if (plugin.id === BRIDGE_ID) {
      return { ...plugin, version: bridgeVersion, selection: { id: BRIDGE_ID, version: bridgeVersion, enabled: bridgeEnabled, required: false, options: {} } }
    }
    return plugin
  })
  if (!plugins.some(plugin => plugin.id === BAN_ID)) {
    plugins.unshift({
      ...previewBanPlugin,
      policy: previewBanPlugin.policy?.map(rule => ({
        ...rule,
        satisfied: rule.plugin === NIRINSOU_ID ? nirinsouEnabled : previewsInstalled,
      })),
      selection: { id: BAN_ID, version: '0.2.1', enabled: banEnabled, required: false, options: {} },
    })
  }
  if (!plugins.some(plugin => plugin.id === MODE_ID)) {
    plugins.splice(1, 0, {
      ...previewModePlugin,
      policy: previewModePlugin.policy?.map(rule => ({
        ...rule,
        satisfied: rule.plugin === BRIDGE_ID ? bridgeVersion === '0.1.20' && bridgeEnabled : uiAssetsInstalled,
      })),
      selection: { id: MODE_ID, version: '0.1.25', enabled: modeEnabled, required: false, options: {} },
    })
  }
  if (previewsInstalled && !plugins.some(plugin => plugin.id === PREVIEWS_ID)) {
    const pkg = previewCatalogPackages.find(item => item.id === PREVIEWS_ID)!
    plugins.push({
      id: PREVIEWS_ID,
      game: 'bubblefighter',
      version: pkg.version,
      kind: 'assets',
      name: pkg.name,
      description: pkg.description,
      authors: ['Fantasy Tale Studio'],
      available: true,
      required: false,
      selection: { id: PREVIEWS_ID, version: pkg.version, enabled: true, required: false, options: {} },
      options: [],
    })
  }
  if (uiAssetsInstalled && !plugins.some(plugin => plugin.id === UI_ASSETS_ID)) {
    const pkg = previewCatalogPackages.find(item => item.id === UI_ASSETS_ID)!
    plugins.push({
      id: UI_ASSETS_ID,
      game: 'bubblefighter',
      version: pkg.version,
      kind: 'assets',
      name: pkg.name,
      description: pkg.description,
      authors: ['Fantasy Tale Studio'],
      available: true,
      required: false,
      selection: { id: UI_ASSETS_ID, version: pkg.version, enabled: true, required: false, options: {} },
      options: [],
    })
  }
  return plugins
}

export function previewPolicyCatalog(sourceId = defaultStoreSource.id): StoreCatalog {
  return {
    id: sourceId,
    name: { en: 'Fantasy Tale' },
    packages: previewCatalogPackages.map(pkg => ({ ...pkg, builds: [...pkg.builds] })),
  }
}

export function installPreviewPolicyPackage(params?: unknown): DownloadTask {
  const { packageId, instanceIds } = (params ?? {}) as { packageId?: string; instanceIds?: string[] }
  const pkg = previewCatalogPackages.find(item => item.id === packageId)
  if (!pkg) throw new Error(`Unknown preview package: ${packageId ?? ''}`)

  const spec: PreviewDownloadSpec = pkg.id === PREVIEWS_ID
    ? {
        id: PREVIEWS_ID,
        fromVersion: '',
        toVersion: pkg.version,
        totalBytes: pkg.size ?? 1_887_436,
        files: [
          { path: 'plugin.toml', size: 4096 },
          { path: 'data/items.pack', size: (pkg.size ?? 1_887_436) - 4096 },
        ],
        onComplete: () => { previewsInstalled = true },
      }
    : pkg.id === UI_ASSETS_ID
      ? {
          id: UI_ASSETS_ID,
          fromVersion: '',
          toVersion: pkg.version,
          totalBytes: pkg.size ?? 3_565_158,
          files: [
            { path: 'plugin.toml', size: 4096 },
            { path: 'data/ui-atlas.pack', size: (pkg.size ?? 3_565_158) - 4096 },
          ],
          onComplete: () => { uiAssetsInstalled = true },
        }
      : {
          id: BRIDGE_ID,
          fromVersion: bridgeVersion,
          toVersion: pkg.version,
          totalBytes: pkg.size ?? 983_040,
          files: [
            { path: 'plugin.toml', size: 4096 },
            { path: 'game_controls.dll', size: (pkg.size ?? 983_040) - 4096 },
          ],
          onComplete: () => {
            bridgeVersion = pkg.version
            bridgeEnabled = true
          },
        }

  const taskId = `policy-preview-${++taskCounter}-${pkg.id}`
  const task: DownloadTask = {
    id: taskId,
    mode: 'preview',
    target: {
      kind: 'plugin',
      packageId: pkg.id,
      gameId: 'bubblefighter',
      instanceId: instanceIds?.[0] ?? 'preview-kr-1256',
      name: pkg.name['zh-CN'] || pkg.name.en || pkg.id,
      nameLocalized: pkg.name,
      fromVersion: spec.fromVersion,
      toVersion: spec.toVersion,
      sourceName: defaultStoreSource.name,
      sourceKind: defaultStoreSource.kind,
      directory: `plugins/${pkg.id}`,
    },
    stage: 'checking',
    files: spec.files.map(file => ({ path: file.path, size: file.size, received: 0, stage: 'queued' })),
    currentPath: spec.files[0]?.path ?? null,
    bytesPerSecond: 0,
    speedSampleCount: 0,
    stageProgress: null,
    failure: null,
    resumeStage: 'downloading',
  }

  let tickIndex = 0
  const speed = Math.max(480 * 1024, Math.round(spec.totalBytes / 1.8))
  const timer = setInterval(() => {
    const live = downloads.tasks.find(item => item.id === taskId)
    if (!live) {
      clearInterval(timer)
      activeTimers.delete(taskId)
      return
    }
    tickIndex++
    if (live.stage === 'checking') {
      if (tickIndex >= 2) {
        live.stage = 'downloading'
        live.speedSampleCount = 3
        live.bytesPerSecond = speed
      }
      return
    }
    if (live.stage === 'downloading') {
      const stepBytes = Math.round(speed * 0.16)
      let remaining = stepBytes
      for (const file of live.files) {
        if (file.size == null || file.received >= file.size) continue
        live.currentPath = file.path
        file.stage = 'downloading'
        const take = Math.min(remaining, file.size - file.received)
        file.received += take
        remaining -= take
        if (file.received >= file.size) file.stage = 'downloaded'
        if (remaining <= 0) break
      }
      if (live.files.every(file => file.size != null && file.received >= file.size)) {
        live.stage = 'verifying'
        live.stageProgress = 0
        live.bytesPerSecond = 0
      }
      return
    }
    if (live.stage === 'verifying') {
      live.stageProgress = Math.min(100, (live.stageProgress ?? 0) + 50)
      if (live.stageProgress >= 100) {
        for (const file of live.files) file.stage = 'verified'
        live.stage = 'installing'
        live.stageProgress = 0
      }
      return
    }
    if (live.stage === 'installing') {
      live.stageProgress = Math.min(100, (live.stageProgress ?? 0) + 34)
      if (live.stageProgress >= 100) {
        spec.onComplete()
        live.stage = 'completed'
        live.stageProgress = 100
        clearInterval(timer)
        activeTimers.delete(taskId)
      }
    }
  }, 160)
  activeTimers.set(taskId, timer)

  return task
}

export function configurePreviewPolicyPlugin(params?: unknown): Instance {
  const { id, enabled } = (params ?? {}) as { id?: string; enabled?: boolean }
  if (id === BAN_ID && typeof enabled === 'boolean') banEnabled = enabled
  if (id === MODE_ID && typeof enabled === 'boolean') modeEnabled = enabled
  if (id === NIRINSOU_ID && typeof enabled === 'boolean') nirinsouEnabled = enabled
  if (id === BRIDGE_ID && typeof enabled === 'boolean') bridgeEnabled = enabled
  return previewPolicyInstances()[0]!
}

export function checkPreviewPolicy(params?: unknown): PolicyFinding[] {
  const req = (params ?? {}) as { enabling?: { id: string } | null; disabling?: { id: string } | null }
  const activeBan = req.enabling?.id === BAN_ID || banEnabled
  const activeMode = req.enabling?.id === MODE_ID || modeEnabled
  const findings: PolicyFinding[] = []

  if (activeBan) {
    const nirinsouDisabling = req.disabling?.id === NIRINSOU_ID
    if (!nirinsouEnabled || nirinsouDisabling) {
      findings.push({
        plugin: BAN_ID,
        pluginVersion: '0.2.1',
        pluginName: previewBanPlugin.name,
        pluginDescription: previewBanPlugin.description,
        pluginKind: previewBanPlugin.kind,
        pluginRequired: false,
        pluginActive: true,
        type: 'require',
        other: NIRINSOU_ID,
        range: '>=0.2.0',
        status: 'disabled',
        blocks: true,
        defaultSelected: false,
        target: {
          id: NIRINSOU_ID,
          version: '0.2.2',
          name: { en: 'Fantasy Tale Nirinsou Link', 'zh-CN': 'Fantasy Tale Nirinsou Link', 'zh-TW': 'Fantasy Tale Nirinsou Link' },
          description: {
            'zh-CN': '实现与 Fantasy Tale Launcher 以及相关插件与服务器间的通信，不建议关闭',
            'zh-TW': '實現與 Fantasy Tale Launcher 及相關模組與伺服器間的通訊，不建議關閉',
            en: 'Enables communication between Fantasy Tale Launcher, related plugins and servers. Disabling it is not recommended.',
          },
          kind: 'service',
        },
      })
    }

    if (!previewsInstalled) {
      findings.push({
        plugin: BAN_ID,
        pluginVersion: '0.2.1',
        pluginName: previewBanPlugin.name,
        pluginDescription: previewBanPlugin.description,
        pluginKind: previewBanPlugin.kind,
        pluginRequired: false,
        pluginActive: true,
        type: 'require',
        other: PREVIEWS_ID,
        range: '>=0.1.0',
        status: 'missing',
        blocks: true,
        defaultSelected: false,
        target: null,
      })
    }
  }

  if (activeMode) {
    const bridgePkg = previewCatalogPackages.find(item => item.id === BRIDGE_ID)!
    if (bridgeVersion !== '0.1.20') {
      findings.push({
        plugin: MODE_ID,
        pluginVersion: '0.1.25',
        pluginName: previewModePlugin.name,
        pluginDescription: previewModePlugin.description,
        pluginKind: previewModePlugin.kind,
        pluginRequired: false,
        pluginActive: true,
        type: 'require',
        other: BRIDGE_ID,
        range: '>=0.1.20',
        status: 'version',
        blocks: true,
        defaultSelected: false,
        target: {
          id: BRIDGE_ID,
          version: bridgeVersion,
          name: bridgePkg.name,
          description: bridgePkg.description,
          kind: 'native',
        },
      })
    }

    if (!uiAssetsInstalled) {
      findings.push({
        plugin: MODE_ID,
        pluginVersion: '0.1.25',
        pluginName: previewModePlugin.name,
        pluginDescription: previewModePlugin.description,
        pluginKind: previewModePlugin.kind,
        pluginRequired: false,
        pluginActive: true,
        type: 'require',
        other: UI_ASSETS_ID,
        range: '>=0.1.2',
        status: 'missing',
        blocks: true,
        defaultSelected: false,
        target: null,
      })
    }
  }

  return findings
}

if (import.meta.hot) import.meta.hot.dispose(clearPreviewTimers)

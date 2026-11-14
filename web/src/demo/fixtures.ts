import previewMirrors from '../preview-github-mirrors.json'
import type {
  BridgeMethods,
  BuildSummary,
  DirectoryEntry,
  DirectorySource,
  GameSummary,
  LaunchCleanupFailure,
  LauncherUpdateSourceSettings,
  LauncherUpdateState,
  MirrorOverview,
  NetworkPreferences,
  OnlinePlayerList,
  PluginOption,
  PluginSummary,
  Server,
  ServerPresentation,
} from '../bridge'
import type { DownloadTask } from '../downloads/model'
import type { InstanceFailureDetails } from '../instanceFailures'
import type { ServerFailureDetails } from '../serverFailures'
import type { NativeUiCapabilities, NativeUiControl, NativeUiControlEvent } from '../nativeUi'
import type { StoreCatalog, StorePackage, StoreSource } from '../store/types'

export const demoPlayerNicknames = [
  'prologueCHL',
  'Philosophyz',
  'TrustInMe',
  'FakeMe',
  'FindTheBlue',
  'FetishismArk',
  'CANOE',
  'EarthOrSky',
  'RaisedBed',
  'PottedOne',
  'TenderHearts',
  'Remembrance',
  'KaguraHikari',
  'KanbeKotori',
  'R1nM4ki',
  'LoveMoritaka',
  'Hank1973',
  'Relieved',
  'tales1301',
] as const

function svgDataUrl(svg: string): string {
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
}

export const demoServerImages = {
  banner: svgDataUrl(`<svg xmlns="http://www.w3.org/2000/svg" width="960" height="360" viewBox="0 0 960 360">
    <defs>
      <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#1f5c99"/><stop offset="0.55" stop-color="#317cc2"/><stop offset="1" stop-color="#58b4e3"/></linearGradient>
      <linearGradient id="wave" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#ffffff" stop-opacity="0.22"/><stop offset="1" stop-color="#ffffff" stop-opacity="0.04"/></linearGradient>
    </defs>
    <rect width="960" height="360" fill="url(#bg)"/>
    <circle cx="150" cy="96" r="56" fill="#fff7dc" opacity="0.9"/>
    <circle cx="780" cy="84" r="110" fill="#ffffff" opacity="0.1"/>
    <path d="M0 248c140-54 270-50 420 10s290 54 540-22v124H0Z" fill="url(#wave)"/>
    <path d="M0 290c180-40 330-26 510 18s280 28 450-12v64H0Z" fill="#0d2f52" opacity="0.28"/>
    <text x="48" y="316" font-family="Segoe UI, sans-serif" font-size="22" font-weight="600" letter-spacing="2" fill="#ffffff" opacity="0.92">FANTASY TALE SERVER SHOWCASE</text>
  </svg>`),
  gallery1: svgDataUrl(`<svg xmlns="http://www.w3.org/2000/svg" width="960" height="720" viewBox="0 0 960 720">
    <defs><linearGradient id="sky" x2="0" y2="1"><stop stop-color="#b9dce8"/><stop offset="1" stop-color="#eff5ed"/></linearGradient><linearGradient id="water" x2="1" y2="1"><stop stop-color="#62aebc"/><stop offset="1" stop-color="#397b9d"/></linearGradient></defs>
    <path fill="url(#sky)" d="M0 0h960v720H0z"/>
    <circle cx="754" cy="150" r="74" fill="#fff3d6"/>
    <path d="M0 390 174 215 300 390 480 255 740 420 960 260v460H0" fill="#a1c3b7"/>
    <path d="m0 457 206-109 177 106 159-94 247 113 171-84v331H0" fill="#6f9e97"/>
    <path d="M0 540q260-145 492-30t468-5v215H0" fill="url(#water)"/>
    <path d="m274 397 168-70 140 80-157 79z" fill="#f3e7cd"/><path d="m274 397 151 89v96l-151-87z" fill="#bd9e81"/><path d="m425 486 157-79v96l-157 79z" fill="#d5b797"/>
    <path d="m346 365 85-92 92 93-90 49z" fill="#d1a06c"/><path d="m431 273 92 93v82l-90-49z" fill="#e4c69e"/>
    <path d="m92 429 66-116 65 116z" fill="#4f8276"/><path d="m690 468 79-157 79 157z" fill="#4f8276"/>
    <text x="42" y="653" font-family="Segoe UI, sans-serif" font-size="18" letter-spacing="3" fill="white" opacity=".9">ARENA COASTLINE · SHOWCASE 01</text>
  </svg>`),
  gallery2: svgDataUrl(`<svg xmlns="http://www.w3.org/2000/svg" width="960" height="720" viewBox="0 0 960 720">
    <defs><linearGradient id="night" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#19253f"/><stop offset="1" stop-color="#35557d"/></linearGradient></defs>
    <rect width="960" height="720" fill="url(#night)"/>
    <circle cx="240" cy="180" r="62" fill="#fde9b8"/>
    <rect x="120" y="340" width="720" height="230" rx="20" fill="#223b5a" stroke="#6aa9d9" stroke-opacity="0.45" stroke-width="4"/>
    <rect x="170" y="390" width="180" height="120" rx="12" fill="#3a6c9c"/>
    <rect x="390" y="390" width="180" height="120" rx="12" fill="#2c848c"/>
    <rect x="610" y="390" width="180" height="120" rx="12" fill="#4c64a8"/>
    <text x="42" y="653" font-family="Segoe UI, sans-serif" font-size="18" letter-spacing="3" fill="white" opacity=".9">TOURNAMENT LOBBY · SHOWCASE 02</text>
  </svg>`),
}

export const demoCustomHtmlPluginPage = `<!doctype html>
<html>
<head>
<meta charset="utf-8" />
<style>
  :root { color-scheme: light dark; font-family: "Segoe UI", system-ui, sans-serif; }
  body { margin: 0; padding: 16px; color: var(--ft-text, #1a1a1a); background: transparent; display: grid; gap: 12px; }
  .card { padding: 14px 16px; border-radius: 8px; border: 1px solid color-mix(in srgb, currentColor 16%, transparent); background: color-mix(in srgb, currentColor 4%, transparent); display: grid; gap: 8px; }
  .row { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
  label { font-size: 13px; font-weight: 600; }
  small { font-size: 12px; opacity: 0.72; }
  select, input[type="text"] { padding: 6px 10px; border-radius: 6px; border: 1px solid color-mix(in srgb, currentColor 22%, transparent); background: transparent; color: inherit; }
</style>
</head>
<body>
  <div class="card">
    <div class="row">
      <div>
        <label for="profilePreset">Telemetry Detail Level</label><br/>
        <small>Interactive custom HTML configuration page inside a sandboxed iframe</small>
      </div>
      <select id="profilePreset">
        <option value="compact">Compact</option>
        <option value="standard" selected>Standard</option>
        <option value="verbose">Verbose</option>
      </select>
    </div>
    <div class="row">
      <label for="captureHotkey">Quick Dump Suffix</label>
      <input id="captureHotkey" type="text" value="session-demo" />
    </div>
  </div>
  <script>
    const preset = document.getElementById('profilePreset');
    const suffix = document.getElementById('captureHotkey');
    function send(key, value) {
      parent.postMessage({ type: 'fantasytale:change', key, value }, '*');
    }
    preset.addEventListener('change', () => send('detailLevel', preset.value));
    suffix.addEventListener('input', () => send('tag', suffix.value));
    window.addEventListener('message', (event) => {
      const data = event.data || {};
      if (data.type === 'fantasytale:init' && data.options) {
        if (data.options.detailLevel) preset.value = String(data.options.detailLevel);
        if (data.options.tag) suffix.value = String(data.options.tag);
      }
      if (data.type === 'fantasytale:theme') {
        document.documentElement.style.setProperty('--ft-text', data.dark ? '#f3f3f3' : '#1a1a1a');
      }
    });
  </script>
</body>
</html>`

export const demoSampleSchemeToml = `# Bubble Fighter Fantasy plugin scheme
format = 1
name = "Competitive KR-812 Preset"

[builds.kr-812]
[[builds.kr-812.plugins]]
id = "bubblefighter.fantasy.font-support-cn"
version = "1.0.0"
enabled = true

[[builds.kr-812.plugins]]
id = "bubblefighter.fantasy.no-file-verify"
version = "1.0.0"
enabled = true

[[builds.kr-812.plugins]]
id = "bubblefighter.fantasy.fps-unlock"
version = "0.2.0"
enabled = true
[builds.kr-812.plugins.options]
targetFps = 144
adaptiveSync = true
`

export const demoSampleServerProfileToml = `id = "srv-imported-demo"
game = "bubblefighter"
name = "Dream Bubble Arena"
address = "127.0.0.1:7210"
builds = ["kr-812", "cn-706"]
description = "Imported demo community server profile."
`

export type DemoHostingOverview = BridgeMethods['hosting.list']
export type DemoClientUpdatesOverview = BridgeMethods['updates.check']
export type DemoServerPackage = BridgeMethods['hosting.packages'][number]

export interface DemoWorkspace {
  games: GameSummary[]
  builds: BuildSummary[]
  storeSources: StoreSource[]
  storeCatalogs: Record<string, StoreCatalog>
  instances: BridgeMethods['instances.list']
  running: BridgeMethods['instances.running']
  instanceMeta: Record<string, { path: string; serverId: string | null; clientLanguage: string | null; lastPlayed: string | null }>
  servers: Server[]
  serverMeta: Record<string, { status: string; pingMs: number | null }>
  playersByServer: Record<string, OnlinePlayerList>
  directoryEntries: DirectoryEntry[]
  directorySources: DirectorySource[]
  accountsByServer: Record<string, BridgeMethods['accounts.list']>
  plugins: PluginSummary[]
  pluginCompatibility: Record<string, string[]>
  schemeStates: Record<string, string>
  downloads: DownloadTask[]
  clientUpdates: DemoClientUpdatesOverview
  launcherUpdate: LauncherUpdateState
  launcherUpdateSources: LauncherUpdateSourceSettings[]
  launcherUpdatePreferences: BridgeMethods['launcherUpdates.preferences']
  network: NetworkPreferences
  mirrors: MirrorOverview
  hosting: DemoHostingOverview & {
    localRoot: string
    packages: DemoServerPackage[]
    settings: { port: number; externalAddress: string; publicDirectoryUrl: string; upnp: boolean }
    logs: Record<string, string[]>
  }
  nativeUi: {
    capabilities: NativeUiCapabilities
    controls: NativeUiControl[]
    events: NativeUiControlEvent[]
    cursor: number
  }
  resourceOverrides: BridgeMethods['instances.resourceOverrides']
}

const dummyHash = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'

function makeOption(partial: Partial<PluginOption> & Pick<PluginOption, 'key' | 'type' | 'default'>): PluginOption {
  return {
    minimum: 0,
    maximum: 9999,
    maxLength: 256,
    values: [],
    label: { en: partial.key },
    description: null,
    ...partial,
  }
}

export function createDemoWorkspace(): DemoWorkspace {
  const mib = 1024 * 1024
  const builds: BuildSummary[] = [
    {
      id: 'cn-279',
      branch: 'cn',
      region: 'cn',
      protocolVersion: 279,
      name: {
        en: 'China 2011-04-08',
        'zh-CN': '国服 2011-04-08',
        'zh-TW': '中國 2011-04-08',
        ko: '중국 2011-04-08',
        id: 'Tiongkok 2011-04-08',
        vi: 'Trung Quốc 2011-04-08',
        th: 'จีน 2011-04-08',
      },
      releaseVersion: '0.1.5.005',
      description: null,
      login: 'gamewidget',
      defaultPort: 7210,
      requiresAccount: false,
      launcherLogin: null,
      nativeLanguage: 'zh-CN',
      additionalLanguages: { en: 'bubblefighter.fantasy.lang-en' },
      buildDate: '2011-04-08 18:35:38',
      supportsHosting: true,
    },
    {
      id: 'cn-406',
      branch: 'cn',
      region: 'cn',
      protocolVersion: 406,
      name: {
        en: 'China 2013-06-28',
        'zh-CN': '国服 2013-06-28',
        'zh-TW': '中國 2013-06-28',
        ko: '중국 2013-06-28',
        id: 'Tiongkok 2013-06-28',
        vi: 'Trung Quốc 2013-06-28',
        th: 'จีน 2013-06-28',
      },
      releaseVersion: '0.2.35.001',
      description: null,
      login: 'gamewidget-ticket',
      defaultPort: 7230,
      requiresAccount: true,
      launcherLogin: 'ticket',
      nativeLanguage: 'zh-CN',
      additionalLanguages: {},
      buildDate: '2013-06-28 11:20:00',
      supportsHosting: false,
    },
    {
      id: 'cn-706',
      branch: 'cn',
      region: 'cn',
      protocolVersion: 706,
      name: {
        en: 'China 2020-07-16',
        'zh-CN': '国服 2020-07-16',
        'zh-TW': '中國 2020-07-16',
        ko: '중국 2020-07-16',
        id: 'Tiongkok 2020-07-16',
        vi: 'Trung Quốc 2020-07-16',
        th: 'จีน 2020-07-16',
      },
      releaseVersion: '2.0.10.008',
      description: null,
      login: 'gamewidget-ticket',
      defaultPort: 7230,
      requiresAccount: true,
      launcherLogin: 'ticket',
      nativeLanguage: 'zh-CN',
      additionalLanguages: { en: 'bubblefighter.fantasy.lang-en' },
      buildDate: '2020-07-16 10:00:00',
      supportsHosting: false,
    },
    {
      id: 'kr-812',
      branch: 'kr',
      region: 'kr',
      protocolVersion: 812,
      name: {
        en: 'Korea 2015-04-22',
        'zh-CN': '韩服 2015-04-22',
        'zh-TW': '韓國 2015-04-22',
        ko: '한국 2015-04-22',
        id: 'Korea 2015-04-22',
        vi: 'Hàn Quốc 2015-04-22',
        th: 'เกาหลี 2015-04-22',
      },
      releaseVersion: '2.0.48.012',
      description: null,
      login: 'launcher-ticket',
      defaultPort: 7210,
      requiresAccount: true,
      launcherLogin: 'ticket',
      nativeLanguage: 'ko',
      additionalLanguages: {
        'zh-CN': 'bubblefighter.fantasy.lang-zh-cn',
        en: 'bubblefighter.fantasy.lang-en',
      },
      buildDate: '2015-04-22 14:12:00',
      supportsHosting: true,
    },
    {
      id: 'kr-1256',
      branch: 'kr',
      region: 'kr',
      protocolVersion: 1256,
      name: {
        en: 'Korea 2026-01-14',
        'zh-CN': '韩服 2026-01-14',
        'zh-TW': '韓國 2026-01-14',
        ko: '한국 2026-01-14',
        id: 'Korea 2026-01-14',
        vi: 'Hàn Quốc 2026-01-14',
        th: 'เกาหลี 2026-01-14',
      },
      releaseVersion: '3.9.2.001',
      description: null,
      login: 'launcher-ticket',
      defaultPort: 7230,
      requiresAccount: true,
      launcherLogin: 'ticket',
      nativeLanguage: 'ko',
      additionalLanguages: { 'zh-CN': 'bubblefighter.fantasy.lang-zh-cn' },
      buildDate: '2026-01-14 09:00:00',
      supportsHosting: true,
    },
    {
      id: 'id-400',
      branch: 'id',
      region: 'id',
      protocolVersion: 400,
      name: {
        en: 'Indonesia 2015-09-10',
        'zh-CN': '印尼服 2015-09-10',
        'zh-TW': '印尼 2015-09-10',
        ko: '인도네시아 2015-09-10',
        id: 'Indonesia 2015-09-10',
        vi: 'Indonesia 2015-09-10',
        th: 'อินโดนีเซีย 2015-09-10',
      },
      releaseVersion: '1.4.20.002',
      description: null,
      login: 'gamewidget',
      defaultPort: 7210,
      requiresAccount: false,
      launcherLogin: null,
      nativeLanguage: 'id',
      additionalLanguages: {},
      buildDate: '2015-09-10 15:00:00',
      supportsHosting: false,
    },
  ]

  const games: GameSummary[] = [
    {
      id: 'bubblefighter',
      navigationEntries: ['home', 'instances', 'multiplayer', 'hosting', 'downloads', 'store', 'plugins', 'accounts'],
      supportsInstances: true,
      supportsPlugins: true,
      launcherLogin: 'required',
      clientLanguages: ['zh-CN', 'zh-TW', 'ko', 'id', 'en'],
      profileFields: ['level', 'lucci', 'cash'],
      levelIcon: true,
      bundledInstances: true,
      backgroundAudioTracks: [],
      status: 'enabled',
      homeOrder: 0,
      heroTitle: 'Crazy Shooting Bubble Fighter',
      preparing: false,
      serviceNotice: null,
      name: {
        en: 'Bubble Fighter Fantasy',
        'zh-CN': '泡泡战士 Fantasy',
        'zh-TW': '泡泡大亂鬥 Fantasy',
        ko: '버블파이터 Fantasy',
        id: 'Bubble Fighter Fantasy',
        vi: 'Bubble Fighter Fantasy',
        th: 'Bubble Fighter Fantasy',
      },
      accent: '#0078D4',
      backgroundVideo: null,
      backgroundVideos: [],
      builds,
      localHostingEnabled: true,
      roomsEnabled: true,
    },
    {
      id: 'airrider',
      navigationEntries: ['home', 'downloads', 'accounts'],
      supportsInstances: false,
      supportsPlugins: true,
      launcherLogin: 'required',
      clientLanguages: ['zh-CN', 'zh-TW', 'ko', 'en'],
      profileFields: ['level', 'lucci', 'cash'],
      levelIcon: true,
      bundledInstances: true,
      backgroundAudioTracks: [],
      status: 'placeholder',
      homeOrder: 10,
      heroTitle: 'Crazy Racing Air Rider',
      preparing: false,
      serviceNotice: null,
      name: { en: 'Air Rider Fantasy', 'zh-CN': '跑跑飞空艇 Fantasy' },
      accent: '#169DAE',
      backgroundVideo: null,
      backgroundVideos: [],
      builds: [],
      localHostingEnabled: false,
      roomsEnabled: false,
    },
    {
      id: 'talesrunner',
      navigationEntries: ['home', 'downloads', 'accounts'],
      supportsInstances: false,
      supportsPlugins: false,
      launcherLogin: 'required',
      clientLanguages: [],
      profileFields: ['level', 'exp', 'tr', 'cash'],
      levelIcon: true,
      bundledInstances: false,
      backgroundAudioTracks: [],
      status: 'placeholder',
      homeOrder: 20,
      preparing: true,
      serviceNotice: {
        en: 'Fantasy Runner support is currently under preparation.',
        'zh-CN': 'Fantasy Runner 正在筹备接入中。',
        'zh-TW': 'Fantasy Runner 正在籌備接入中。',
      },
      name: { en: 'Fantasy Runner', 'zh-CN': '超级跑跑 Fantasy' },
      accent: '#9C78B6',
      backgroundVideo: null,
      backgroundVideos: [],
      builds: [],
      localHostingEnabled: false,
      roomsEnabled: false,
    },
  ]

  const storeSources: StoreSource[] = [
    {
      id: 'ft-official',
      name: 'Fantasy Tale Archive',
      kind: 'alist',
      baseUrl: 'https://archive.fantasytale.example',
      catalogPath: '/FantasyTale/catalog/index.toml',
      enabled: true,
      builtIn: true,
    },
    {
      id: 'community-mirror',
      name: 'BubbleFighter Community Mirror',
      kind: 'github',
      baseUrl: 'https://github.com/fantasytale-community/bf-mirror',
      catalogPath: 'catalog/store.toml',
      enabled: true,
      builtIn: false,
    },
    {
      id: 'experimental-lab',
      name: 'Nightly Plugin Lab',
      kind: 'alist',
      baseUrl: 'https://lab.fantasytale.example',
      catalogPath: '/nightly/catalog.toml',
      enabled: false,
      builtIn: false,
    },
  ]

  const officialPackages: StorePackage[] = [
    {
      id: 'kr-812',
      kind: 'client',
      game: 'bubblefighter',
      name: { en: 'Korea 2015-04-22 (kr-812)', 'zh-CN': '韩服 2015-04-22 (kr-812)', 'zh-TW': '韓國 2015-04-22 (kr-812)', ko: '한국 2015-04-22 (kr-812)' },
      description: { en: 'Baseline golden client build with full plugin & hosting compatibility.', 'zh-CN': '标准黄金基线客户端版本，完整支持插件生态与本地开服。' },
      version: '2.0.48',
      builds: ['kr-812'],
      authors: ['Nexon / Archive'],
      publisher: 'Fantasy Tale Archive',
      manifest: 'https://archive.fantasytale.example/kr-812.toml',
      manifestSha256: dummyHash,
      size: 1620 * mib,
      launcherCompatible: true,
    },
    {
      id: 'kr-1256',
      kind: 'client',
      game: 'bubblefighter',
      name: { en: 'Korea 2026-01-14 (kr-1256)', 'zh-CN': '韩服 2026-01-14 (kr-1256)', 'zh-TW': '韓國 2026-01-14 (kr-1256)', ko: '한국 2026-01-14 (kr-1256)' },
      description: { en: 'Modern final Korean build with expanded weapons and costume catalog.', 'zh-CN': '现代韩服最终存档客户端，拥有完整模式与时装内容。' },
      version: '3.9.2',
      builds: ['kr-1256'],
      authors: ['Nexon / Archive'],
      publisher: 'Fantasy Tale Archive',
      manifest: 'https://archive.fantasytale.example/kr-1256.toml',
      manifestSha256: dummyHash,
      size: 1907 * mib,
      launcherCompatible: true,
    },
    {
      id: 'cn-279',
      kind: 'client',
      game: 'bubblefighter',
      name: { en: 'China 2011-04-08 (cn-279)', 'zh-CN': '国服 2011-04-08 (cn-279)', 'zh-TW': '中國 2011-04-08 (cn-279)', ko: '중국 2011-04-08 (cn-279)' },
      description: { en: 'Classic 2011 Chinese release with native Simplified Chinese interface.', 'zh-CN': '经典 2011 年国服早期版本，原汁原味怀旧体验。' },
      version: '0.1.5',
      builds: ['cn-279'],
      authors: ['TianCity / Archive'],
      publisher: 'Fantasy Tale Archive',
      manifest: 'https://archive.fantasytale.example/cn-279.toml',
      manifestSha256: dummyHash,
      size: 980 * mib,
      launcherCompatible: true,
    },
    {
      id: 'cn-706',
      kind: 'client',
      game: 'bubblefighter',
      name: { en: 'China 2020-07-16 (cn-706)', 'zh-CN': '国服 2020-07-16 (cn-706)', 'zh-TW': '中國 2020-07-16 (cn-706)', ko: '중국 2020-07-16 (cn-706)' },
      description: { en: '2020 Chinese edition with modern UI layout and extended zombie/survival stages.', 'zh-CN': '2020 年国服版本，界面清晰且玩法模式齐全。' },
      version: '2.0.10',
      builds: ['cn-706'],
      authors: ['TianCity / Archive'],
      publisher: 'Fantasy Tale Archive',
      manifest: 'https://archive.fantasytale.example/cn-706.toml',
      manifestSha256: dummyHash,
      size: 1843 * mib,
      launcherCompatible: true,
    },
    {
      id: 'bubblefighter.fantasy.font-support-cn',
      kind: 'plugin',
      game: 'bubblefighter',
      name: { en: 'Chinese font support', 'zh-CN': '中文字体支持', 'zh-TW': '中文字體支援', ko: '중국어 글꼴 지원' },
      description: { en: 'Fixes thin stroke weights and enables crisp Simplified Chinese glyph rendering.', 'zh-CN': '为非国服客户端提供清晰的简体中文字体渲染支持并修正字重。' },
      version: '1.2.0',
      builds: ['kr-812', 'kr-1256', 'cn-706', 'id-400'],
      authors: ['Fantasy Tale Studio'],
      publisher: 'Fantasy Tale Studio',
      manifest: 'https://archive.fantasytale.example/font-support-cn-1.2.0.toml',
      manifestSha256: dummyHash,
      size: 3.4 * mib,
      pluginDefinition: { id: 'bubblefighter.fantasy.font-support-cn', aliases: [], options: [] },
      launcherCompatible: true,
    },
    {
      id: 'bubblefighter.fantasy.fps-unlock',
      kind: 'plugin',
      game: 'bubblefighter',
      name: { en: 'Frame rate unlock', 'zh-CN': '帧率解锁', 'zh-TW': '影格率解鎖', ko: '프레임 제한 해제' },
      description: { en: 'Raises client render frame cap up to 240 FPS with optional adaptive pacing.', 'zh-CN': '突破客户端原生帧率上限（最高支持 240 FPS）并支持平滑步进。' },
      version: '0.3.0',
      builds: ['kr-812', 'cn-279', 'cn-706'],
      authors: ['Fantasy Tale Studio'],
      publisher: 'Fantasy Tale Studio',
      manifest: 'https://archive.fantasytale.example/fps-unlock-0.3.0.toml',
      manifestSha256: dummyHash,
      size: 1.8 * mib,
      pluginDefinition: { id: 'bubblefighter.fantasy.fps-unlock', aliases: [], options: [{ key: 'targetFps' }, { key: 'adaptiveSync' }] },
      launcherCompatible: true,
    },
    {
      id: 'bubblefighter.fantasy.shop-unlock',
      kind: 'plugin',
      game: 'bubblefighter',
      name: { en: 'Shop access', 'zh-CN': '商店开放', 'zh-TW': '商店開放', ko: '상점 이용' },
      description: { en: 'Enables in-game catalog browsing and purchase dialogs on KR-1256 builds.', 'zh-CN': '修复韩服最终版本商城入口被锁定导致无法打开商店的问题。' },
      version: '1.0.0',
      builds: ['kr-1256'],
      authors: ['Fantasy Tale Studio'],
      publisher: 'Fantasy Tale Studio',
      manifest: 'https://archive.fantasytale.example/shop-unlock-1.0.0.toml',
      manifestSha256: dummyHash,
      size: 1.1 * mib,
      pluginDefinition: { id: 'bubblefighter.fantasy.shop-unlock', aliases: [], options: [] },
      launcherCompatible: true,
    },
    {
      id: 'bubblefighter.fantasy.wide-chat',
      kind: 'plugin',
      game: 'bubblefighter',
      name: { en: 'Wide chat box', 'zh-CN': '宽聊天框', 'zh-TW': '寬聊天框', ko: '넓은 채팅 상자' },
      description: { en: 'Widens lobby and room chat overlays so long messages display without wrapping abruptly.', 'zh-CN': '加宽大厅与房间内聊天输入框，显著提升长句和中文排版可读性。' },
      version: '0.3.1',
      builds: ['kr-812', 'cn-706'],
      authors: ['Fantasy Tale Studio'],
      publisher: 'Fantasy Tale Studio',
      manifest: 'https://archive.fantasytale.example/wide-chat-0.3.1.toml',
      manifestSha256: dummyHash,
      size: 0.9 * mib,
      pluginDefinition: { id: 'bubblefighter.fantasy.wide-chat', aliases: [], options: [] },
      launcherCompatible: true,
    },
    {
      id: 'bfserver-emulator-v3',
      kind: 'server',
      game: 'bubblefighter',
      name: { en: 'BubbleFighter.Fantasy Server Suite', 'zh-CN': 'BubbleFighter.Fantasy 服务端套件', 'zh-TW': 'BubbleFighter.Fantasy 伺服器套件' },
      description: { en: 'Plugin-driven standalone game + relay server supporting kr-812, cn-279, and cn-706.', 'zh-CN': '模块化高性能开源服务端，开箱即用支持多客户端版本本地开服。' },
      version: '1.4.2',
      builds: ['kr-812', 'cn-279', 'cn-706'],
      authors: ['BFServerEmulatorV3'],
      publisher: 'BFServerEmulatorV3',
      manifest: 'https://archive.fantasytale.example/bfserver-1.4.2.toml',
      manifestSha256: dummyHash,
      size: 48 * mib,
      launcherCompatible: true,
    },
    {
      id: 'bfserver-sandbox-kr1256',
      kind: 'server',
      game: 'bubblefighter',
      name: { en: 'KR-1256 Research Sandbox Server', 'zh-CN': 'KR-1256 实验研究服务端' },
      description: { en: 'Dedicated local sandbox profile tailored for Korea 2026 client protocol experiments.', 'zh-CN': '专为韩服最新存档适配的实验性本地沙盒服务端包。' },
      version: '0.9.4',
      builds: ['kr-1256'],
      authors: ['BFServerEmulatorV3'],
      publisher: 'BFServerEmulatorV3',
      manifest: 'https://archive.fantasytale.example/bfserver-kr1256-0.9.4.toml',
      manifestSha256: dummyHash,
      size: 52 * mib,
      launcherCompatible: true,
    },
    {
      id: 'bubblefighter.fantasy.lang-zh-cn',
      kind: 'plugin',
      game: 'bubblefighter',
      name: { en: 'Simplified Chinese Interface Pack', 'zh-CN': '简体中文界面翻译包', 'zh-TW': '簡體中文介面翻譯包' },
      description: { en: 'Adds full Simplified Chinese UI and item descriptions for Korean clients.', 'zh-CN': '为韩服客户端提供完整的简体中文 UI 与道具文本本地化覆盖。' },
      version: '2.1.0',
      builds: ['kr-812', 'kr-1256'],
      authors: ['Fantasy Localization'],
      publisher: 'Fantasy Localization',
      manifest: 'https://archive.fantasytale.example/lang-zh-cn-2.1.0.toml',
      manifestSha256: dummyHash,
      size: 14 * mib,
      pluginDefinition: { id: 'bubblefighter.fantasy.lang-zh-cn', aliases: [], options: [] },
      launcherCompatible: true,
    },
  ]

  const storeCatalogs: Record<string, StoreCatalog> = {
    'ft-official': {
      id: 'ft-official',
      name: { en: 'Fantasy Tale Archive', 'zh-CN': 'Fantasy Tale 官方归档源' },
      packages: officialPackages,
    },
    'community-mirror': {
      id: 'community-mirror',
      name: { en: 'BubbleFighter Community Mirror', 'zh-CN': '泡泡战士社区镜像源' },
      packages: officialPackages.filter(p => p.id === 'bubblefighter.fantasy.font-support-cn' || p.id === 'bubblefighter.fantasy.wide-chat' || p.id === 'bfserver-emulator-v3'),
    },
    'experimental-lab': {
      id: 'experimental-lab',
      name: { en: 'Nightly Plugin Lab', 'zh-CN': '每夜测试实验源' },
      packages: [
        {
          ...officialPackages.find(p => p.id === 'bubblefighter.fantasy.fps-unlock')!,
          version: '0.4.0-alpha.1',
          name: { en: 'Frame rate unlock (Nightly)', 'zh-CN': '帧率解锁（实验版）' },
        },
      ],
    },
  }

  const mainPresentation: ServerPresentation = {
    state: 'online',
    whitelist: false,
    onlinePlayers: 128,
    peakOnlinePlayers: 214,
    playerLimit: 500,
    rosterEnabled: true,
    motd: 'Official flagship community hub running classic Arena, Pirate Mode, and weekend custom tournaments with zero pay-to-win.',
    motdTranslations: {
      en: 'Official flagship community hub running classic Arena, Pirate Mode, and weekend custom tournaments with zero pay-to-win.',
      'zh-CN': '社区旗舰联机大厅：完整开放经典模式、海盗模式、爆破及周末趣味房，平衡对战与持续赛季活动。',
      'zh-TW': '社群旗艦連線大廳：完整開放經典模式、海盜模式與週末賽事房間。',
    },
    summary: 'Flagship multi-build community hub',
    summaryTranslations: { 'zh-CN': '多版本全模式旗舰大厅' },
    images: [demoServerImages.banner, demoServerImages.gallery1, demoServerImages.gallery2],
  }

  const servers: Server[] = [
    {
      id: 'srv-fantasy-main',
      game: 'bubblefighter',
      name: 'Fantasy Tale',
      apiUrl: 'https://api.fantasytale.example',
      endpoint: { host: 'play.fantasytale.example', port: 7210 },
      builds: ['kr-812', 'cn-706', 'kr-1256'],
      bundledInstances: true,
      builtIn: true,
      presentation: mainPresentation,
      clientPolicy: {
        version: 1,
        plugins: [
          { id: 'bubblefighter.fantasy.no-file-verify', rules: [{ id: 'rule-req-1', type: 'require', builds: ['kr-812', 'cn-706'] }] },
          { id: 'bubblefighter.fantasy.font-support-cn', rules: [{ id: 'rule-rec-1', type: 'recommend', builds: ['kr-812'] }] },
          { id: 'bubblefighter.fantasy.shop-unlock', rules: [{ id: 'rule-rec-2', type: 'recommend', builds: ['kr-1256'] }] },
        ],
        sources: [
          {
            name: 'Fantasy Tale Official Source',
            kind: 'alist',
            baseUrl: 'https://archive.fantasytale.example',
            catalogPath: '/FantasyTale/catalog/index.toml',
          },
        ],
      },
    },
    {
      id: 'srv-kr-retro',
      game: 'bubblefighter',
      name: 'Retro Nostalgia 2015',
      apiUrl: 'https://retro.fantasytale.example',
      endpoint: { host: 'retro.fantasytale.example', port: 7210 },
      builds: ['kr-812', 'cn-279', 'cn-406', 'id-400'],
      bundledInstances: false,
      builtIn: false,
      presentation: {
        state: 'online',
        onlinePlayers: 64,
        peakOnlinePlayers: 112,
        playerLimit: 300,
        rosterEnabled: true,
        motd: 'Pure vanilla balance ruleset tuned for 2011-2015 builds with original map rotation.',
        motdTranslations: {
          'zh-CN': '纯粹怀旧规则服务器，保留早期武器手感与经典地图轮换。',
        },
        images: [demoServerImages.gallery2],
      },
    },
    {
      id: 'srv-local-dev',
      game: 'bubblefighter',
      name: 'Local Server (127.0.0.1:7210)',
      apiUrl: null,
      endpoint: { host: '127.0.0.1', port: 7210 },
      builds: ['kr-812', 'cn-279'],
      kind: 'room',
      localOnly: true,
      bundledInstances: false,
      builtIn: false,
      presentation: {
        state: 'online',
        onlinePlayers: 2,
        playerLimit: 16,
        rosterEnabled: true,
        motd: 'Loopback sandbox powered by local FantasyTale server runner.',
        motdTranslations: {
          'zh-CN': '由启动器本地开服模块托管的回环测试服务器。',
        },
      },
    },
  ]

  const plugins: PluginSummary[] = [
    {
      id: 'bubblefighter.fantasy.font-support-cn',
      game: 'bubblefighter',
      version: '1.0.0',
      name: { en: 'Chinese font support', 'zh-CN': '中文字体支持', 'zh-TW': '中文字體支援', ko: '중국어 글꼴 지원' },
      description: {
        en: 'Provides crisp Simplified Chinese glyph weights on Korean & Indonesian clients.',
        'zh-CN': '为非国服客户端提供清晰的简体中文字体显示并修正字重。',
      },
      authors: ['Fantasy Tale Studio'],
      kind: 'resources',
      available: true,
      required: false,
      canUninstall: true,
      launcherCompatible: true,
      languagePackCompatible: true,
      options: [],
      selection: { id: 'bubblefighter.fantasy.font-support-cn', version: '1.0.0', enabled: true, required: false, options: {} },
    },
    {
      id: 'bubblefighter.fantasy.no-file-verify',
      game: 'bubblefighter',
      version: '1.0.0',
      name: { en: 'Remove file verification', 'zh-CN': '移除文件校验', 'zh-TW': '移除檔案校驗', ko: '파일 검증 제거' },
      description: {
        en: 'Skips archive integrity self-check so custom skins and translations load safely.',
        'zh-CN': '移除客户端对已修改资源包的启动自检。',
      },
      authors: ['Fantasy Tale Studio'],
      kind: 'patch',
      available: true,
      required: true,
      canUninstall: false,
      launcherCompatible: true,
      languagePackCompatible: true,
      options: [],
      selection: { id: 'bubblefighter.fantasy.no-file-verify', version: '1.0.0', enabled: true, required: true, options: {} },
    },
    {
      id: 'bubblefighter.fantasy.crash-reporter',
      game: 'bubblefighter',
      version: '1.1.0',
      name: { en: 'Diagnostics & Telemetry', 'zh-CN': '诊断日志与设置面板示例', 'zh-TW': '診斷日誌與設定範例' },
      description: {
        en: 'Includes both native structured settings and an embedded HTML custom configuration panel.',
        'zh-CN': '内置原生配置项与独立 HTML 自定义配置页，便于排查客户端与插件状态。',
      },
      authors: ['Fantasy Tale Studio'],
      kind: 'service',
      available: true,
      required: false,
      canUninstall: true,
      launcherCompatible: true,
      languagePackCompatible: true,
      options: [
        makeOption({
          key: 'captureMinidump',
          type: 'bool',
          default: true,
          label: { en: 'Write minidump on exit crash', 'zh-CN': '异常退出时保存转储快照' },
        }),
        makeOption({
          key: 'ringBufferKb',
          type: 'int',
          default: 512,
          minimum: 64,
          maximum: 4096,
          label: { en: 'Ring buffer size (KB)', 'zh-CN': '环形日志缓冲区大小 (KB)' },
        }),
        makeOption({
          key: 'detailLevel',
          type: 'enum',
          default: 'standard',
          values: ['compact', 'standard', 'verbose'],
          label: { en: 'Log detail level', 'zh-CN': '日志详细级别' },
        }),
        makeOption({
          key: 'tag',
          type: 'string',
          default: 'session-demo',
          maxLength: 64,
          label: { en: 'Session label suffix', 'zh-CN': '会话标签后缀' },
        }),
      ],
      configurationPage: true,
      configurationPages: [
        { id: 'native-settings', kind: 'native', title: { en: 'General', 'zh-CN': '基础选项' }, options: ['captureMinidump', 'ringBufferKb'] },
        { id: 'html-panel', kind: 'html', title: { en: 'Advanced Panel', 'zh-CN': '自定义界面' }, options: ['detailLevel', 'tag'] },
      ],
      selection: {
        id: 'bubblefighter.fantasy.crash-reporter',
        version: '1.1.0',
        enabled: true,
        required: false,
        options: { captureMinidump: true, ringBufferKb: 512, detailLevel: 'standard', tag: 'session-demo' },
      },
    },
    {
      id: 'bubblefighter.fantasy.fps-unlock',
      game: 'bubblefighter',
      version: '0.2.0',
      name: { en: 'Frame rate unlock', 'zh-CN': '帧率解锁', 'zh-TW': '影格率解鎖', ko: '프레임 제한 해제' },
      description: {
        en: 'Unlocks render frame rate cap for high-refresh displays.',
        'zh-CN': '提高客户端渲染帧率上限，适配高刷显示器。',
      },
      authors: ['Fantasy Tale Studio'],
      kind: 'patch',
      available: true,
      required: false,
      canUninstall: true,
      launcherCompatible: true,
      languagePackCompatible: true,
      options: [
        makeOption({ key: 'targetFps', type: 'int', default: 144, minimum: 60, maximum: 240, label: { en: 'Target FPS', 'zh-CN': '目标帧率' } }),
        makeOption({ key: 'adaptiveSync', type: 'bool', default: true, label: { en: 'Adaptive frame pacing', 'zh-CN': '平滑帧步进' } }),
      ],
      selection: {
        id: 'bubblefighter.fantasy.fps-unlock',
        version: '0.2.0',
        enabled: true,
        required: false,
        options: { targetFps: 144, adaptiveSync: true },
      },
    },
    {
      id: 'bubblefighter.fantasy.lang-zh-cn',
      game: 'bubblefighter',
      version: '2.1.0',
      name: { en: 'Simplified Chinese Interface Pack', 'zh-CN': '简体中文界面翻译包', 'zh-TW': '簡體中文介面翻譯包' },
      description: { en: 'Localization file pack for Korean client builds.', 'zh-CN': '适用于韩服客户端的简体中文翻译资源包。' },
      authors: ['Fantasy Localization'],
      kind: 'files',
      available: true,
      required: false,
      canUninstall: true,
      launcherCompatible: true,
      languagePackCompatible: true,
      options: [],
      selection: { id: 'bubblefighter.fantasy.lang-zh-cn', version: '2.1.0', enabled: true, required: false, options: {} },
    },
  ]

  const pluginCompatibility: Record<string, string[]> = {
    'bubblefighter.fantasy.font-support-cn': ['kr-812', 'kr-1256', 'cn-706', 'id-400'],
    'bubblefighter.fantasy.no-file-verify': ['kr-812', 'kr-1256', 'cn-706', 'id-400'],
    'bubblefighter.fantasy.crash-reporter': ['kr-812', 'cn-279', 'cn-706'],
    'bubblefighter.fantasy.fps-unlock': ['kr-812', 'cn-279', 'cn-706'],
    'bubblefighter.fantasy.lang-zh-cn': ['kr-812', 'kr-1256'],
  }

  const instances: BridgeMethods['instances.list'] = [
    {
      id: 'demo-bf-kr812',
      game: 'bubblefighter',
      build: 'kr-812',
      name: 'KR 2015 Classic (kr-812)',
      installationPath: 'D:\\Games\\BubbleFighter\\kr-812',
      endpoint: { host: 'play.fantasytale.example', port: 7210 },
      clientInstalled: true,
      language: 'zh-CN',
      diagnosticsEnabled: true,
      recoveryRequired: false,
      plugins: [
        { id: 'bubblefighter.fantasy.font-support-cn', version: '1.0.0', enabled: true, required: false, options: {} },
        { id: 'bubblefighter.fantasy.no-file-verify', version: '1.0.0', enabled: true, required: true, options: {} },
        { id: 'bubblefighter.fantasy.crash-reporter', version: '1.1.0', enabled: true, required: false, options: { captureMinidump: true, ringBufferKb: 512, detailLevel: 'standard', tag: 'session-demo' } },
        { id: 'bubblefighter.fantasy.fps-unlock', version: '0.2.0', enabled: true, required: false, options: { targetFps: 144, adaptiveSync: true } },
        { id: 'bubblefighter.fantasy.lang-zh-cn', version: '2.1.0', enabled: true, required: false, options: {} },
      ],
    },
    {
      id: 'demo-bf-cn279',
      game: 'bubblefighter',
      build: 'cn-279',
      name: 'CN 2011 Nostalgia (cn-279)',
      installationPath: 'D:\\Games\\BubbleFighter\\cn-279',
      endpoint: { host: '127.0.0.1', port: 7210 },
      clientInstalled: true,
      language: 'zh-CN',
      diagnosticsEnabled: false,
      recoveryRequired: false,
      plugins: [],
    },
    {
      id: 'demo-bf-cn406',
      game: 'bubblefighter',
      build: 'cn-406',
      name: 'CN 2013 Recovery Sample (cn-406)',
      installationPath: 'D:\\Games\\BubbleFighter\\cn-406',
      endpoint: { host: 'play.fantasytale.example', port: 7210 },
      clientInstalled: true,
      language: 'zh-CN',
      diagnosticsEnabled: true,
      recoveryRequired: true,
      plugins: [],
    },
  ]

  const instanceMeta: DemoWorkspace['instanceMeta'] = {
    'demo-bf-kr812': { path: 'D:\\Games\\BubbleFighter\\kr-812', serverId: 'srv-fantasy-main', clientLanguage: 'zh-CN', lastPlayed: '2026-11-12T20:15:00Z' },
    'demo-bf-cn279': { path: 'D:\\Games\\BubbleFighter\\cn-279', serverId: 'srv-local-dev', clientLanguage: 'zh-CN', lastPlayed: '2026-11-11T18:30:00Z' },
    'demo-bf-cn406': { path: 'D:\\Games\\BubbleFighter\\cn-406', serverId: 'srv-fantasy-main', clientLanguage: 'zh-CN', lastPlayed: null },
  }

  const running: BridgeMethods['instances.running'] = [
    {
      instanceId: 'demo-bf-cn279',
      processId: 18432,
      hook: { state: 'ready', error: '', plugins: [] },
    },
  ]

  const accountsByServer: Record<string, BridgeMethods['accounts.list']> = {
    'srv-fantasy-main': {
      current: {
        username: 'Philosophyz',
        displayName: 'Philosophyz',
        stats: { level: 32, lucci: 128500, cash: 4200 },
        levelIconUrl: null,
        hasCharacter: true,
      },
      accounts: [
        {
          account: {
            username: 'Philosophyz',
            displayName: 'Philosophyz',
            stats: { level: 32, lucci: 128500, cash: 4200 },
            levelIconUrl: null,
            hasCharacter: true,
          },
          expiresAt: '2026-12-31T23:59:59Z',
        },
        {
          account: {
            username: 'KaguraHikari',
            displayName: 'KaguraHikari',
            stats: { level: 18, lucci: 54000, cash: 800 },
            levelIconUrl: null,
            hasCharacter: true,
          },
          expiresAt: '2026-12-15T23:59:59Z',
        },
      ],
    },
    'srv-kr-retro': {
      current: {
        username: 'tales1301',
        displayName: 'tales1301',
        stats: { level: 12, lucci: 29400, cash: 300 },
        levelIconUrl: null,
        hasCharacter: true,
      },
      accounts: [
        {
          account: {
            username: 'tales1301',
            displayName: 'tales1301',
            stats: { level: 12, lucci: 29400, cash: 300 },
            levelIconUrl: null,
            hasCharacter: true,
          },
          expiresAt: '2026-12-01T12:00:00Z',
        },
      ],
    },
  }

  const directoryEntries: DirectoryEntry[] = [
    {
      id: 'dir-srv-1',
      kind: 'server',
      details: {
        game: 'bubblefighter',
        name: 'Fantasy Tale Main Hall',
        apiUrl: 'https://api.fantasytale.example',
        endpoint: { host: 'play.fantasytale.example', port: 7210 },
        builds: ['kr-812', 'cn-706', 'kr-1256'],
        description: 'Flagship multi-build public server hosted by the community archive.',
        presentation: mainPresentation,
      },
    } as unknown as DirectoryEntry,
    {
      id: 'dir-srv-2',
      kind: 'server',
      details: {
        game: 'bubblefighter',
        name: 'Asia East Retro Arena',
        apiUrl: 'https://retro.fantasytale.example',
        endpoint: { host: 'retro.fantasytale.example', port: 7210 },
        builds: ['kr-812', 'cn-279'],
        description: 'Low-latency East Asia classic server with vanilla weapon balance.',
        presentation: servers[1]!.presentation,
      },
    } as unknown as DirectoryEntry,
    {
      id: 'dir-room-1',
      kind: 'room',
      details: {
        game: 'bubblefighter',
        name: 'No-Item Classic 4v4 [All Welcome]',
        apiUrl: '',
        endpoint: { host: '127.0.0.1', port: 7210 },
        builds: ['kr-812'],
        description: 'Host: prologueCHL · Map: Pirate Harbor',
        presentation: { state: 'online', onlinePlayers: 6, playerLimit: 8, rosterEnabled: true },
      },
    } as unknown as DirectoryEntry,
    {
      id: 'dir-room-2',
      kind: 'room',
      details: {
        game: 'bubblefighter',
        name: 'CN-279 Weekend Scrim',
        apiUrl: '',
        endpoint: { host: '127.0.0.1', port: 7211 },
        builds: ['cn-279'],
        description: 'Host: LoveMoritaka · Map: Factory 02',
        presentation: { state: 'online', onlinePlayers: 3, playerLimit: 8, rosterEnabled: true },
      },
    } as unknown as DirectoryEntry,
  ]

  const directorySources: DirectorySource[] = [
    {
      id: 'dir-src-official',
      name: 'Fantasy Tale Public Index',
      baseUrl: 'https://directory.fantasytale.example/v1',
      enabled: true,
      builtIn: true,
    } as unknown as DirectorySource,
  ]

  const hosting = {
    localRoot: 'D:\\Games\\FantasyTale\\Hosting',
    settings: {
      port: 7210,
      externalAddress: 'demo.fantasytale.example:7210',
      publicDirectoryUrl: 'https://directory.fantasytale.example/v1',
      upnp: true,
    },
    packages: [
      {
        id: 'bfserver-emulator-v3',
        sourceId: 'ft-official',
        directory: 'D:\\Games\\Servers\\bfserver-emulator-v3',
        package: officialPackages.find(p => p.id === 'bfserver-emulator-v3')!,
      } as unknown as DemoServerPackage,
    ],
    profiles: [
      {
        id: 'host-kr812',
        game: 'bubblefighter',
        name: 'My KR-812 Local Lobby',
        build: 'kr-812',
        mode: 'room',
        packagePath: 'D:\\Games\\Servers\\bfserver-emulator-v3',
        advertisedHost: 'demo.fantasytale.example',
        requireAuthentication: true,
        temporaryData: false,
        gamePort: 7210,
        relayPort: 7211,
        apiPort: 7212,
        adminPort: 7213,
        clientInstanceId: 'demo-bf-kr812',
      },
      {
        id: 'host-cn279',
        game: 'bubblefighter',
        name: 'CN-279 Classic Room Server',
        build: 'cn-279',
        mode: 'lan',
        packagePath: 'D:\\Games\\Servers\\bfserver-emulator-v3',
        advertisedHost: '192.168.1.24',
        requireAuthentication: false,
        temporaryData: false,
        gamePort: 7220,
        relayPort: 7221,
        apiPort: 7222,
        adminPort: 7223,
        clientInstanceId: 'demo-bf-cn279',
      },
    ],
    sessions: [
      {
        runId: 'host-run-1',
        profileId: 'host-kr812',
        state: 'ready',
        processId: 24180,
        startedAt: '2026-11-13T01:30:00Z',
        exitCode: null,
        failure: null,
        forcedStop: false,
        apiUrl: 'http://127.0.0.1:7212',
        adminUrl: 'http://127.0.0.1:7213',
        logPath: 'D:\\Games\\Servers\\bfserver-emulator-v3\\logs\\host-run-1.log',
      },
    ],
    logs: {
      'host-run-1': [
        '[01:30:01] [Host] Loaded profile "My KR-812 Local Lobby" (kr-812)',
        '[01:30:02] [Game] Listening on 0.0.0.0:7210',
        '[01:30:02] [Relay] UDP relay bound on port 7211',
        '[01:30:03] [Directory] Published room "My KR-812 Local Lobby" to Fantasy Tale Public Index',
        '[01:31:10] [Auth] Player "TrustInMe" connected from 127.0.0.1',
      ],
    },
  } as unknown as DemoWorkspace['hosting']

  const downloads: DownloadTask[] = [
    {
      id: 'demo-dl-paused',
      mode: 'live',
      target: {
        kind: 'plugin',
        buildId: 'kr-812',
        instanceId: 'demo-bf-kr812',
        packageId: 'bubblefighter.fantasy.wide-chat',
        name: 'Wide chat box',
        nameLocalized: { en: 'Wide chat box', 'zh-CN': '宽聊天框', 'zh-TW': '寬聊天框', ko: '넓은 채팅 상자' },
        fromVersion: '',
        toVersion: '0.3.1',
        directory: 'D:\\Games\\Plugins\\wide-chat',
        sourceName: 'Fantasy Tale Archive',
        sourceKind: 'alist',
      },
      stage: 'paused',
      files: [
        { path: 'plugin.toml', size: 24 * 1024, received: 24 * 1024, stage: 'downloaded' },
        { path: 'UI/LobbyChatBox.xml', size: 880 * 1024, received: 420 * 1024, stage: 'downloading' },
      ],
      currentPath: 'UI/LobbyChatBox.xml',
      bytesPerSecond: 0,
      speedSampleCount: 0,
      stageProgress: null,
      failure: null,
      resumeStage: 'downloading',
    },
    {
      id: 'demo-dl-done',
      mode: 'live',
      target: {
        kind: 'client',
        buildId: 'kr-812',
        instanceId: 'demo-bf-kr812',
        name: 'KR 2015 Classic (kr-812)',
        nameLocalized: { en: 'KR 2015 Classic (kr-812)', 'zh-CN': '韩服经典存档 kr-812' },
        fromVersion: '2.0.47',
        toVersion: '2.0.48',
        directory: 'D:\\Games\\BubbleFighter\\kr-812',
        sourceName: 'Fantasy Tale Archive',
        sourceKind: 'alist',
      },
      stage: 'completed',
      files: [
        { path: 'Data/characters.pack', size: 220 * mib, received: 220 * mib, stage: 'verified' },
        { path: 'Data/maps.pack', size: 480 * mib, received: 480 * mib, stage: 'verified' },
        { path: 'BubbleFighter.exe', size: 18 * mib, received: 18 * mib, stage: 'verified' },
      ],
      currentPath: 'BubbleFighter.exe',
      bytesPerSecond: 0,
      speedSampleCount: 4,
      stageProgress: 100,
      failure: null,
      resumeStage: 'downloading',
    },
  ]

  const clientUpdates = {
    checking: false,
    error: null,
    clients: [
      {
        instanceId: 'demo-bf-kr812',
        status: 'available',
        installedVersion: '2.0.47',
        version: '2.0.48',
        manifestSha256: dummyHash,
        checkedAt: '2026-11-13T01:45:00Z',
      },
      {
        instanceId: 'demo-bf-cn279',
        status: 'current',
        installedVersion: '0.1.5',
        version: '0.1.5',
        manifestSha256: dummyHash,
        checkedAt: '2026-11-13T01:45:00Z',
      },
      {
        instanceId: 'demo-bf-cn406',
        status: 'unmanaged',
        installedVersion: '',
        version: null,
        manifestSha256: null,
        checkedAt: '2026-11-13T01:45:00Z',
      },
    ],
  } as unknown as DemoClientUpdatesOverview

  const launcherUpdate: LauncherUpdateState = {
    status: 'current',
    version: '0.9.0',
    revision: 12,
    manifestSha256: dummyHash,
    failure: null,
  }

  const launcherUpdateSources: LauncherUpdateSourceSettings[] = [
    {
      name: 'Fantasy Tale Official CDN',
      kind: 'alist',
      baseUrl: 'https://archive.fantasytale.example',
      feedPath: 'FantasyTale/launcher/win-x64/current.toml',
      minimumRevision: 1,
    },
    {
      name: 'GitHub Releases Fallback',
      kind: 'github',
      baseUrl: 'https://github.com/fantasytale/FantasyTale.Launcher',
      feedPath: 'releases/latest/current.toml',
      minimumRevision: 1,
    },
  ]

  const network: NetworkPreferences = {
    ipPreference: 'china',
    gitHubMirror: false,
    gitHubMirrorSource: 'gh-proxy.com',
    defaultPending: false,
    suggestionPending: false,
  }

  const mirrors: MirrorOverview = {
    providers: previewMirrors,
    results: previewMirrors.slice(0, 6).map((provider, i) => ({
      id: provider.id,
      state: i < 5 ? 'available' : 'failed',
      milliseconds: i < 5 ? 38 + i * 24 : null,
      bytesPerSecond: i < 5 ? Math.round((9.6 - i * 1.4) * mib) : null,
    })),
  }

  const controls: NativeUiControl[] = [
    { id: 'ctrl-1', form: 'MainLobbyForm', name: 'btnShop', kind: 'button', text: 'Shop', writable: true, x: 710, y: 52, width: 96, height: 34, enabled: true },
    { id: 'ctrl-2', form: 'MainLobbyForm', name: 'lblOnlineCount', kind: 'label', text: 'Online: 128 Players', writable: true, x: 24, y: 20, width: 180, height: 22, enabled: true },
    { id: 'ctrl-3', form: 'MainLobbyForm', name: 'txtChatInput', kind: 'textbox', text: '', writable: true, maximumLength: 120, x: 32, y: 650, width: 380, height: 28, enabled: true },
    { id: 'ctrl-4', form: 'MainLobbyForm', name: 'lstRooms', kind: 'listbox', text: 'Pirate Harbor [6/8]', writable: false, x: 40, y: 120, width: 520, height: 360, enabled: true, arrayIndex: 0, selectionValue: 1, selected: true },
  ]

  const capabilities: NativeUiCapabilities = {
    build: 'cn-279',
    operations: [
      { kind: 'messageBox', supported: true, ready: true, features: ['buttons', 'icons'] },
      { kind: 'notification', supported: true, ready: true, features: [] },
      { kind: 'announcement.show', supported: true, ready: true, features: [] },
      { kind: 'announcement.close', supported: true, ready: true, features: [] },
      { kind: 'chat', supported: true, ready: true, features: ['runs', 'system'] },
      { kind: 'control.inspect', supported: true, ready: true, features: [] },
      { kind: 'control.setText', supported: true, ready: true, features: [] },
      { kind: 'control.addButton', supported: true, ready: true, features: [] },
      { kind: 'control.remove', supported: true, ready: true, features: [] },
      { kind: 'control.events', supported: true, ready: true, features: [] },
      { kind: 'control.activate', supported: true, ready: true, features: [] },
    ],
  }

  return {
    games,
    builds,
    storeSources,
    storeCatalogs,
    instances,
    running,
    instanceMeta,
    servers,
    serverMeta: {
      'srv-fantasy-main': { status: 'online', pingMs: 24 },
      'srv-kr-retro': { status: 'online', pingMs: 42 },
      'srv-local-dev': { status: 'online', pingMs: 1 },
    },
    playersByServer: {
      'srv-fantasy-main': {
        schema: 1,
        enabled: true,
        complete: true,
        truncated: false,
        players: [
          { name: 'Philosophyz', level: 32, buildId: 'kr-812' },
          { name: 'prologueCHL', level: 30, buildId: 'kr-812' },
          { name: 'TrustInMe', level: 27, buildId: 'kr-812' },
          { name: 'FakeMe', level: 25, buildId: 'cn-706' },
          { name: 'FindTheBlue', level: 24, buildId: 'kr-812' },
          { name: 'FetishismArk', level: 22, buildId: 'kr-1256' },
          { name: 'CANOE', level: 21, buildId: 'kr-812' },
          { name: 'EarthOrSky', level: 20, buildId: 'cn-706' },
          { name: 'RaisedBed', level: 19, buildId: 'kr-812' },
          { name: 'PottedOne', level: 18, buildId: 'kr-812' },
          { name: 'TenderHearts', level: 17, buildId: 'cn-706' },
          { name: 'Remembrance', level: 16, buildId: 'kr-812' },
        ],
      } as unknown as OnlinePlayerList,
      'srv-kr-retro': {
        schema: 1,
        enabled: true,
        complete: true,
        truncated: false,
        players: [
          { name: 'tales1301', level: 28, buildId: 'kr-812' },
          { name: 'KaguraHikari', level: 18, buildId: 'kr-812' },
          { name: 'KanbeKotori', level: 15, buildId: 'cn-279' },
          { name: 'R1nM4ki', level: 12, buildId: 'kr-812' },
        ],
      } as unknown as OnlinePlayerList,
      'srv-local-dev': {
        schema: 1,
        enabled: true,
        complete: true,
        truncated: false,
        players: [
          { name: 'LoveMoritaka', level: 50, buildId: 'cn-279' },
          { name: 'Hank1973', level: 42, buildId: 'kr-812' },
          { name: 'Relieved', level: 36, buildId: 'cn-706' },
        ],
      } as unknown as OnlinePlayerList,
    },
    directoryEntries,
    directorySources,
    accountsByServer,
    plugins,
    pluginCompatibility,
    schemeStates: {
      bubblefighter: demoSampleSchemeToml,
    },
    downloads,
    clientUpdates,
    launcherUpdate,
    launcherUpdateSources,
    launcherUpdatePreferences: { autoCheck: true, autoDownload: true },
    network,
    mirrors,
    hosting,
    nativeUi: {
      capabilities,
      controls,
      events: [{ sequence: 1, controlId: 'ctrl-1', event: 'click' }],
      cursor: 1,
    },
    resourceOverrides: {
      preparedAt: '2026-11-13T01:55:00Z',
      files: [
        {
          target: 'Data/LobbyLogo.pak',
          size: 142_300,
          sha256: dummyHash,
          originalSha256: null,
          cacheReused: true,
          restoreOnExit: true,
          state: 'overridden',
          changes: [
            {
              pluginId: 'bubblefighter.fantasy.font-support-cn',
              version: '1.0.0',
              name: { en: 'Chinese font support', 'zh-CN': '中文字体支持' },
              member: 'UI/LobbyLogo.png',
              action: 'replace',
              originalSha256: null,
              sha256: dummyHash,
            },
          ],
        },
        {
          target: 'Data/Crosshair.pak',
          size: 65_536,
          sha256: dummyHash,
          originalSha256: dummyHash,
          cacheReused: false,
          restoreOnExit: true,
          state: 'modified',
          changes: [
            {
              pluginId: 'bubblefighter.fantasy.fps-unlock',
              version: '0.2.0',
              name: { en: 'Frame rate unlock', 'zh-CN': '帧率解锁' },
              member: 'UI/Crosshair.dds',
              action: 'add',
              originalSha256: null,
              sha256: dummyHash,
            },
          ],
        },
      ],
    },
  }
}

export function demoScenarioPayloads(): {
  serverFailure: ServerFailureDetails
  instanceFailure: InstanceFailureDetails
  launchFailure: LaunchCleanupFailure
} {
  return {
    serverFailure: {
      code: 'server_maintenance',
      httpStatus: 503,
      reason: 'Scheduled weekly season balance patch in progress.',
      maintenanceStart: '2026-11-13 02:00 UTC',
      maintenanceEnd: '2026-11-13 03:00 UTC',
      maintenanceDescription: 'Servers will reopen automatically after database verification.',
    },
    instanceFailure: {
      operation: 'launch',
      instanceId: 'demo-bf-kr812',
      clientPath: 'D:\\Games\\BubbleFighter\\kr-812\\BubbleFighter.exe',
      lockPath: 'D:\\Games\\BubbleFighter\\kr-812\\.ft.lock',
      osError: 32,
      kind: 'instance_in_use',
    },
    launchFailure: {
      instanceId: 'demo-bf-kr812',
      exitConfirmed: true,
      exitCode: -1073741819,
    } as unknown as LaunchCleanupFailure,
  }
}

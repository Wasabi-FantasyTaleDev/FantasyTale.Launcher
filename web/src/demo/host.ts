import type {
  BridgeMethods,
  DirectoryEntry,
  DirectorySource,
  Instance,
  LauncherUpdateState,
  PluginSummary,
  Server,
} from '../bridge'
import { createDemoWorkspace, demoCustomHtmlPluginPage, demoPlayerNicknames, demoSampleSchemeToml, demoSampleServerProfileToml, type DemoWorkspace } from './fixtures'
import { onDemoModeChange, onDemoReset } from './mode'
import type { DownloadTask, DownloadTarget } from '../downloads/model'
import type { PolicyFinding } from '../plugins/pluginPolicy'

type Emitter = (event: string, data?: unknown) => void
type Params = Record<string, any>
type DemoHandlers = {
  [K in keyof BridgeMethods]?: (p: Params) => unknown
}

const mib = 1024 * 1024

let workspace: DemoWorkspace = withServerSections(createDemoWorkspace())

// The demo cluster has three sections with separate data: Main is under maintenance while Test Server and Oversea
// Server stay open, and the same username is saved once per section.
// The demo's Test and Oversea servers sign players in separately, so each has its own accounts.
const demoSections = [{ id: 'main', name: 'Main', default: true }, { id: 'test', name: 'Test Server', separate: true }, { id: 'oversea', name: 'Oversea Server', separate: true }]
function demoMaintenance() {
  const start = new Date(); start.setMinutes(0, 0, 0)
  const end = new Date(start.getTime() + 3 * 3600_000)
  return { start: start.toISOString(), end: end.toISOString(), description: '**Weekly update**: the new *WeaponMaster* mode, laboratory improvements and server performance work.' }
}
const demoServerStatus = () => ({
  sections: demoSections,
  servers: [
    { id: 1, name: 'Automatic (Beijing)', section: 'main', automatic: true, state: 'maintenance' as const, maintenance: demoMaintenance() },
    { id: 2, name: 'Beijing (Alibaba Cloud)', section: 'main', state: 'maintenance' as const, maintenance: demoMaintenance() },
    { id: 3, name: 'Shanghai (Huawei Cloud)', section: 'main', state: 'online' as const, players: 86, capacity: 500 },
    { id: 6, name: 'Test (JD Cloud)', section: 'test', state: 'online' as const, players: 12, capacity: 300 },
    { id: 7, name: 'Oversea (United States)', section: 'oversea', state: 'online' as const, players: 38, capacity: 500 },
  ],
})
// Notices are Markdown, newest first; the oldest one counts as already seen, so only Show all notices lists it.
const demoNotices = [
  { id: 'n-20261113-event', level: 'info' as const, publishedAt: '2026-11-13T09:00:00Z', title: 'Weekend double experience',
    body: `From **Friday 18:00** to **Sunday 24:00**, every round grants *double experience*.

- All modes count
- Party bonuses stack` },
  { id: 'n-20261113-update', level: 'important' as const, publishedAt: '2026-11-13T08:00:00Z', title: 'Version 0.3.1 is live',
    body: `## What is new

1. **WeaponMaster** and the laboratory
2. Many gameplay fixes
3. A redesigned administration WebUI

Read the full notes at [the project page](https://example.com/notes).` },
  { id: 'n-20261101-welcome', level: 'info' as const, publishedAt: '2026-11-01T00:00:00Z', title: 'Welcome to Fantasy Tale',
    body: 'Thank you for playing. Report problems with `Help > Diagnostics`.' },
]
let seenNotices = new Set<string>(['n-20261101-welcome'])
function withServerSections(value: DemoWorkspace): DemoWorkspace {
  const main = value.accountsByServer['srv-fantasy-main']
  if (main) {
    for (const entry of main.accounts) entry.account.section = 'main'
    if (main.current) main.current.section = 'main'
    const philosophyz = main.accounts.find(entry => entry.account.username === 'Philosophyz')
    if (philosophyz) for (const [section, level] of [['test', 41], ['oversea', 7]] as const)
      main.accounts.push({ ...clone(philosophyz), account: { ...clone(philosophyz.account), section, stats: { ...philosophyz.account.stats, level } } })
  }
  return value
}
const sameAccount = (account: { username: string; section?: string | null }, username: string, section?: string) =>
  account.username === username && (account.section ?? null) === (section ?? account.section ?? null)
let downloadTicker: ReturnType<typeof setInterval> | undefined
let launcherTicker: ReturnType<typeof setInterval> | undefined
let activeEmitter: Emitter | undefined
let nextIdCounter = 100
let shortcuts = new Set<string>(['demo-bf-kr812'])
let activePolicyDemoInstanceId: string | null = null

function nextUid(prefix: string): string {
  nextIdCounter += 1
  return `${prefix}-${Date.now().toString(36)}-${nextIdCounter}`
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}

function emit(event: string, data?: unknown): void {
  activeEmitter?.(event, data === undefined ? undefined : clone(data))
}

function stopTickers(): void {
  if (downloadTicker) {
    clearInterval(downloadTicker)
    downloadTicker = undefined
  }
  if (launcherTicker) {
    clearInterval(launcherTicker)
    launcherTicker = undefined
  }
}

export function resetDemoWorkspace(): void {
  stopTickers()
  workspace = withServerSections(createDemoWorkspace())
  seenNotices = new Set<string>(['n-20261101-welcome'])
  shortcuts = new Set<string>(['demo-bf-kr812'])
  activePolicyDemoInstanceId = null
  emit('downloads.changed', workspace.downloads)
  emit('instances.changed', workspace.instances)
  emit('plugins.changed', workspace.plugins)
  emit('store.changed')
}

onDemoReset(resetDemoWorkspace)
onDemoModeChange(enabled => {
  if (!enabled) stopTickers()
})

function hasActiveDownload(task: DownloadTask): boolean {
  return task.stage === 'checking' || task.stage === 'queued' || task.stage === 'downloading'
    || task.stage === 'verifying' || task.stage === 'installing'
}

function applyTaskCompletion(task: DownloadTask): void {
  const target = task.target
  if (target.kind === 'client') {
    const existing = target.instanceId
      ? workspace.instances.find(i => i.id === target.instanceId)
      : undefined
    if (existing) {
      const cu = workspace.clientUpdates.clients.find((c: any) => c.instanceId === existing.id)
      if (cu) {
        cu.status = 'current'
        cu.installedVersion = cu.version ?? existing.build
      }
      emit('instances.changed', workspace.instances)
    } else if (target.buildId) {
      const newId = target.instanceId || nextUid('demo-inst')
      const installPath = target.directory || `D:\\Games\\BubbleFighter\\${target.buildId}-${nextIdCounter}`
      const created: Instance = {
        id: newId,
        game: target.gameId || 'bubblefighter',
        build: target.buildId,
        name: target.name || `Client ${target.buildId}`,
        installationPath: installPath,
        endpoint: { host: 'play.fantasytale.example', port: 7210 },
        clientInstalled: true,
        language: target.buildId.startsWith('kr') ? 'zh-CN' : null,
        checkIntegrity: true,
        diagnosticsEnabled: false,
        recoveryRequired: false,
        plugins: [],
      }
      workspace.instances.push(created)
      workspace.instanceMeta[newId] = {
        path: installPath,
        serverId: workspace.servers[0]?.id ?? null,
        clientLanguage: created.language ?? null,
        lastPlayed: null,
      }
      emit('instances.changed', workspace.instances)
    }
  } else if (target.kind === 'plugin' && target.toVersion) {
    const pkgId = target.packageId || target.name
    const existingByVersion = workspace.plugins.find(p => p.id === pkgId && p.version === target.toVersion)
    const existingById = workspace.plugins.find(p => p.id === pkgId)
    if (!existingByVersion && existingById) {
      existingById.version = target.toVersion
      existingById.available = true
      existingById.selection = {
        id: existingById.id,
        version: target.toVersion,
        enabled: true,
        required: existingById.selection?.required ?? false,
        options: existingById.selection?.options ?? {},
      }
    } else if (!existingByVersion) {
      const catalogPkg = Object.values(workspace.storeCatalogs)
        .flatMap(c => c.packages)
        .find(p => (p.id === pkgId || p.name.en === target.name) && p.version === target.toVersion)
      const createdPlugin: PluginSummary = {
        id: catalogPkg?.id ?? pkgId,
        game: catalogPkg?.game ?? 'bubblefighter',
        version: target.toVersion,
        name: catalogPkg?.name ?? { en: target.name },
        description: catalogPkg?.description ?? { en: '' },
        authors: catalogPkg?.authors ?? ['Fantasy Tale Studio'],
        kind: 'service',
        available: true,
        required: false,
        canUninstall: true,
        launcherCompatible: true,
        languagePackCompatible: true,
        options: [],
        selection: { id: catalogPkg?.id ?? pkgId, version: target.toVersion, enabled: true, required: false, options: {} },
      }
      workspace.plugins.push(createdPlugin)
      workspace.pluginCompatibility[createdPlugin.id] = catalogPkg?.builds ?? ['kr-812', 'cn-706']
    } else {
      existingByVersion.available = true
      existingByVersion.selection = {
        id: existingByVersion.id,
        version: existingByVersion.version,
        enabled: true,
        required: existingByVersion.selection?.required ?? false,
        options: existingByVersion.selection?.options ?? {},
      }
    }
    if (target.instanceId) {
      const inst = workspace.instances.find(i => i.id === target.instanceId)
      if (inst) {
        const idx = inst.plugins.findIndex(p => p.id === pkgId)
        const nextEntry = { id: pkgId, version: target.toVersion, enabled: true, required: false, options: {} }
        if (idx >= 0) inst.plugins[idx] = nextEntry
        else inst.plugins.push(nextEntry)
      }
    }
    emit('plugins.changed', workspace.plugins)
    if (target.instanceId) emit('instances.changed', workspace.instances)
  } else if (target.kind === 'server') {
    const srvId = target.packageId || nextUid('srv-pkg')
    const catalogPkg = Object.values(workspace.storeCatalogs)
      .flatMap(c => c.packages)
      .find(p => p.id === srvId)
    if (!workspace.hosting.packages.some((p: any) => p.package?.id === srvId)) {
      workspace.hosting.packages.push({
        id: srvId,
        sourceId: 'ft-official',
        directory: target.directory || `D:\\Games\\Servers\\${srvId}`,
        package: catalogPkg ?? {
          id: srvId,
          kind: 'server',
          game: 'bubblefighter',
          name: target.nameLocalized ?? { en: target.name },
          description: { en: 'Installed local server package' },
          version: target.toVersion || '1.0.0',
          builds: target.buildId ? [target.buildId] : ['kr-812', 'cn-279'],
          authors: ['BFServerEmulatorV3'],
          publisher: 'BFServerEmulatorV3',
          manifest: '',
          manifestSha256: '',
          size: 48 * mib,
        },
      } as unknown as DemoWorkspace['hosting']['packages'][number])
    }
  }
}

function tickDownloads(): void {
  let anyActive = false
  let changed = false
  for (const task of workspace.downloads) {
    if (!hasActiveDownload(task)) continue
    anyActive = true
    changed = true
    if (task.stage === 'queued') {
      task.stage = 'checking'
      task.stageProgress = 0
      continue
    }
    if (task.stage === 'checking') {
      task.stage = 'downloading'
      task.stageProgress = null
      task.bytesPerSecond = 11.4 * mib
      task.speedSampleCount = 1
      continue
    }
    if (task.stage === 'downloading') {
      task.bytesPerSecond = (10.5 + Math.sin(Date.now() / 1600) * 1.8) * mib
      task.speedSampleCount = Math.min(12, task.speedSampleCount + 1)
      const nextFile = task.files.find(f => f.received < (f.size ?? 0))
      if (!nextFile) {
        task.stage = 'verifying'
        task.stageProgress = 0
        task.bytesPerSecond = 0
        continue
      }
      task.currentPath = nextFile.path
      nextFile.stage = 'downloading'
      const step = Math.max(256 * 1024, (nextFile.size ?? mib) * 0.22)
      nextFile.received = Math.min(nextFile.size ?? 0, Math.round(nextFile.received + step))
      if (nextFile.received >= (nextFile.size ?? 0)) nextFile.stage = 'downloaded'
    } else if (task.stage === 'verifying') {
      task.stageProgress = Math.min(100, (task.stageProgress ?? 0) + 25)
      const count = Math.floor(((task.stageProgress ?? 0) / 100) * task.files.length)
      task.files.forEach((f, idx) => {
        f.stage = idx < count ? 'verified' : idx === count ? 'verifying' : 'downloaded'
      })
      if (task.stageProgress >= 100) {
        if (task.target.kind === 'launcher') {
          task.stage = 'ready'
          task.stageProgress = 100
        } else {
          task.stage = 'installing'
          task.stageProgress = 0
        }
      }
    } else if (task.stage === 'installing') {
      task.stageProgress = Math.min(100, (task.stageProgress ?? 0) + 34)
      if (task.stageProgress >= 100) {
        task.stage = 'completed'
        task.stageProgress = 100
        applyTaskCompletion(task)
      }
    }
  }
  if (changed) emit('downloads.changed', workspace.downloads)
  if (!anyActive && downloadTicker) {
    clearInterval(downloadTicker)
    downloadTicker = undefined
  }
}

function ensureDownloadTicker(): void {
  if (downloadTicker) return
  if (workspace.downloads.some(hasActiveDownload)) {
    downloadTicker = setInterval(tickDownloads, 260)
  }
}

export function enqueueDemoDownload(target: DownloadTarget, initialStage: 'checking' | 'downloading' = 'checking'): DownloadTask {
  const paths = target.kind === 'plugin'
    ? ['plugin.toml', 'plugin.dll', 'assets/pack.dat']
    : target.kind === 'server'
      ? ['BubbleFighter.Fantasy.Host.exe', 'profiles/kr-812.yaml', 'packets/opcodes.yaml']
      : target.kind === 'launcher'
        ? ['FantasyTale.Launcher.exe', 'web.bundle', 'runtime.pack']
        : ['Data/characters.pack', 'Data/maps.pack', 'Data/textures.pack', 'BubbleFighter.exe']
  const totalMb = target.kind === 'plugin' ? 3 : target.kind === 'server' ? 48 : target.kind === 'launcher' ? 42 : 420
  const perFile = Math.round((totalMb * mib) / paths.length)
  const task: DownloadTask = {
    id: nextUid('demo-task'),
    mode: 'live',
    target,
    stage: initialStage,
    files: paths.map(path => ({ path, size: perFile, received: 0, stage: 'queued' })),
    currentPath: paths[0],
    bytesPerSecond: initialStage === 'downloading' ? 11.2 * mib : 0,
    speedSampleCount: initialStage === 'downloading' ? 2 : 0,
    stageProgress: 0,
    failure: null,
    resumeStage: 'downloading',
  }
  workspace.downloads.unshift(task)
  emit('downloads.changed', workspace.downloads)
  ensureDownloadTicker()
  return task
}

function startLauncherDownloadAnimation(): void {
  if (launcherTicker) clearInterval(launcherTicker)
  workspace.launcherUpdate = {
    status: 'downloading',
    version: '0.9.5',
    revision: 15,
    manifestSha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f9011223344556677889900aabbccddeeff',
    failure: null,
  }
  const existingTask = workspace.downloads.find(d => d.target.kind === 'launcher' && !['completed', 'canceled'].includes(d.stage))
  if (!existingTask) {
    enqueueDemoDownload({
      kind: 'launcher',
      name: 'Fantasy Tale Launcher',
      fromVersion: '0.9.0',
      toVersion: '0.9.5',
      directory: 'D:\\Apps\\FantasyTale',
      sourceName: 'Fantasy Tale Official CDN',
      sourceKind: 'alist',
    }, 'downloading')
  }
  launcherTicker = setInterval(() => {
    const active = workspace.downloads.find(d => d.target.kind === 'launcher')
    if (!active || active.stage === 'ready' || active.stage === 'completed') {
      workspace.launcherUpdate.status = 'ready'
      clearInterval(launcherTicker)
      launcherTicker = undefined
    }
  }, 300)
}

function filterPluginReleases(instanceId?: string, serverId?: string): PluginSummary[] {
  const inst = instanceId ? workspace.instances.find(i => i.id === instanceId) : undefined
  const srv = serverId ? workspace.servers.find(s => s.id === serverId) : undefined
  return workspace.plugins.map(pkg => {
    const item = clone(pkg)
    const compatBuilds = workspace.pluginCompatibility[pkg.id] ?? ['kr-812', 'cn-706']
    if (inst) {
      const saved = inst.plugins.find(p => p.id === pkg.id && p.version === pkg.version)
      item.selection = saved ? clone(saved) : { id: pkg.id, version: pkg.version, enabled: false, required: false, options: clone(pkg.selection?.options ?? {}) }
      if (inst.game && pkg.game !== inst.game) item.available = false
      if (compatBuilds.length && !compatBuilds.includes(inst.build)) item.available = false
    } else if (srv) {
      if (pkg.game !== srv.game) item.available = false
    }
    return item
  })
}

let pendingSupportOffers: any[] = []
let overlayTestPrefs = { enabled: false, passThroughOutside: true }

const handlers: DemoHandlers = {
  'app.info': (_p: Params) => ({ version: '0.9.5-demo', language: typeof navigator !== 'undefined' ? navigator.language : 'en', os: 'Windows 11' }),
  'window.minimize': (_p: Params) => null,
  'window.toggleMaximize': (_p: Params) => false,
  'window.close': (_p: Params) => null,
  'window.ready': (_p: Params) => null,
  'window.exit': (_p: Params) => null,
  'window.startDrag': (_p: Params) => null,
  'window.theme': (_p: Params) => null,
  'window.state': (_p: Params) => ({ maximized: false }),
  'window.language': (_p: Params) => null,

  'games.list': (_p: Params) => clone(workspace.games),

  'instances.list': (_p: Params) => clone(workspace.instances),
  'instances.running': (_p: Params) => clone(workspace.running),
  'instances.services': (_p: Params) => ({}),
  'instances.inspectInstallation': (p: Params) => {
    const installPath = p.installationPath || `D:\\Games\\BubbleFighter\\${p.id || 'client'}`
    return { path: installPath, containsFiles: false, detectedBuildIds: ['kr-812'] }
  },
  'instances.create': (p: Params) => {
    const id = p.id || nextUid('demo-inst')
    const installPath = p.installationPath || `D:\\Games\\BubbleFighter\\${p.build || 'kr-812'}-${nextIdCounter}`
    const created: Instance = {
      id,
      game: p.game || 'bubblefighter',
      build: p.build || 'kr-812',
      name: p.name || `Demo Client (${p.build || 'kr-812'})`,
      installationPath: installPath,
      endpoint: p.endpoint ?? { host: 'play.fantasytale.example', port: 7210 },
      clientInstalled: true,
      language: p.language ?? null,
      checkIntegrity: p.checkIntegrity ?? true,
      diagnosticsEnabled: false,
      recoveryRequired: false,
      plugins: [],
    }
    workspace.instances.push(created)
    workspace.instanceMeta[id] = {
      path: installPath,
      serverId: p.serverId ?? workspace.servers[0]?.id ?? null,
      clientLanguage: p.language ?? null,
      lastPlayed: null,
    }
    emit('instances.changed', workspace.instances)
    return clone(created)
  },
  'instances.update': (p: Params) => {
    const inst = workspace.instances.find(i => i.id === p.id)
    if (!inst) throw new Error('Instance not found.')
    if (typeof p.name === 'string') inst.name = p.name
    if (typeof p.installationPath === 'string') inst.installationPath = p.installationPath
    if ('endpoint' in p) inst.endpoint = p.endpoint ?? inst.endpoint
    if ('build' in p && p.build) inst.build = p.build
    if ('language' in p) inst.language = p.language ?? null
    if ('checkIntegrity' in p) inst.checkIntegrity = Boolean(p.checkIntegrity)
    const meta = workspace.instanceMeta[inst.id] ?? { path: inst.installationPath || 'D:\\Games\\BubbleFighter', serverId: null, clientLanguage: null, lastPlayed: null }
    if ('serverId' in p) meta.serverId = p.serverId ?? null
    if ('language' in p) meta.clientLanguage = p.language ?? null
    workspace.instanceMeta[inst.id] = meta
    emit('instances.changed', workspace.instances)
    return clone(inst)
  },
  'instances.delete': (p: Params) => {
    workspace.instances = workspace.instances.filter(i => i.id !== p.id)
    workspace.running = workspace.running.filter(r => r.instanceId !== p.id)
    emit('instances.changed', workspace.instances)
    return { deletedFiles: p.deleteFiles ? 24 : 0, preservedFiles: p.deleteFiles ? 0 : 12 }
  },
  'instances.clone': (p: Params) => {
    const src = workspace.instances.find(i => i.id === (p.sourceId || p.id))
    if (!src) throw new Error('Source instance not found.')
    const id = p.id || nextUid('demo-inst')
    const installPath = p.installationPath || `D:\\Games\\BubbleFighter\\copy-${nextIdCounter}`
    const cloned: Instance = {
      ...clone(src),
      id,
      name: p.name || `${src.name} (Copy)`,
      installationPath: installPath,
      recoveryRequired: false,
    }
    workspace.instances.push(cloned)
    workspace.instanceMeta[id] = {
      path: installPath,
      serverId: workspace.instanceMeta[src.id]?.serverId ?? null,
      clientLanguage: workspace.instanceMeta[src.id]?.clientLanguage ?? null,
      lastPlayed: null,
    }
    emit('instances.changed', workspace.instances)
    return clone(cloned)
  },
  'instances.recover': (p: Params) => {
    const inst = workspace.instances.find(i => i.id === p.id)
    if (!inst) throw new Error('Instance not found.')
    inst.recoveryRequired = false
    emit('instances.changed', workspace.instances)
    return null
  },
  'instances.recoverStopped': (_p: Params) => null,
  'instances.openFolder': (_p: Params) => null,
  'instances.openDiagnostics': (_p: Params) => null,
  'instances.launchDiagnostics': (_p: Params) => ({ lifecycle: 'running' }),
  'instances.cancelResourcePreparation': (_p: Params) => null,
  'instances.cancelLaunch': (_p: Params) => null,
  'instances.hookPing': (_p: Params) => 'pong',
  'instances.launch': (p: Params) => {
    const inst = workspace.instances.find(i => i.id === p.instanceId || i.id === p.id)
    if (!inst) throw new Error('Instance not found.')
    let running = workspace.running.find(r => r.instanceId === inst.id)
    if (!running) {
      const pid = 21000 + (++nextIdCounter)
      running = {
        instanceId: inst.id,
        processId: pid,
        hook: { state: 'starting', error: '', plugins: [] },
      }
      let step = 0
      const prepTimer = setInterval(() => {
        step += 1
        if (step <= 3) {
          emit('instances.resourceProgress', {
            instanceId: inst.id,
            path: step === 1 ? 'Data/fonts_patch.pack' : step === 2 ? 'Data/lang_zh_cn.pak' : 'Plugins/hook_runtime.bin',
            total: 3,
            completedArchives: step,
            totalArchives: 3,
            elapsedSeconds: step * 0.4,
            percentage: Math.min(100, step * 34),
          })
          return
        }
        clearInterval(prepTimer)
        emit('instances.resourceProgress', { instanceId: inst.id, done: true })
        if (workspace.instanceMeta[inst.id]) workspace.instanceMeta[inst.id]!.lastPlayed = new Date().toISOString()
        running!.hook = { state: 'ready', error: '', plugins: [] }
        workspace.running = [...workspace.running.filter(r => r.instanceId !== inst.id), running!]
        emit('instances.readiness', { instanceId: inst.id, ready: true })
        emit('instances.changed', workspace.instances)
      }, 320)
      workspace.running.push(running)
    }
    return clone(running)
  },
  'instances.stop': (p: Params) => {
    const stopped = workspace.running.filter(r => r.instanceId === p.id || r.instanceId === p.instanceId)
    workspace.running = workspace.running.filter(r => !stopped.includes(r))
    for (const s of stopped) {
      emit('instances.exited', { instanceId: s.instanceId, exitCode: 0 })
    }
    emit('instances.changed', workspace.instances)
    return stopped[0] ? { instanceId: stopped[0].instanceId, exitCode: 0 } : null
  },
  'instances.resourceOverrides': (_p: Params) => clone(workspace.resourceOverrides),

  'servers.list': (_p: Params) => clone(workspace.servers),
  'servers.save': (p: Params) => {
    const existing = p.id && !p.createOnly ? workspace.servers.find(s => s.id === p.id) : undefined
    if (existing) {
      if (p.name) existing.name = p.name
      if ('apiUrl' in p) existing.apiUrl = p.apiUrl ?? null
      if ('endpoint' in p) existing.endpoint = p.endpoint ?? null
      if ('preferIp' in p) existing.preferIp = p.preferIp
      if ('useHttps' in p) existing.useHttps = p.useHttps
      if ('presentation' in p) existing.presentation = p.presentation ?? null
      return clone(existing)
    }
    const id = p.id || nextUid('srv-custom')
    const created: Server = {
      id,
      game: p.game || 'bubblefighter',
      name: p.name || 'Custom Demo Server',
      apiUrl: p.apiUrl ?? null,
      endpoint: p.endpoint ?? { host: '127.0.0.1', port: 7210 },
      builds: p.builds?.length ? p.builds : ['kr-812'],
      bundledInstances: Boolean(p.bundledInstances),
      builtIn: false,
      preferIp: p.preferIp ?? null,
      useHttps: Boolean(p.useHttps),
      presentation: p.presentation ?? {
        state: 'online',
        onlinePlayers: 14,
        playerLimit: 200,
        rosterEnabled: true,
        motd: p.description || 'Custom server added in Demo Mode.',
      },
    }
    workspace.servers.push(created)
    workspace.serverMeta[id] = { status: 'online', pingMs: 19 }
    return clone(created)
  },
  'servers.delete': (p: Params) => {
    workspace.servers = workspace.servers.filter(s => s.id !== p.id)
    return null
  },
  'servers.clientRelease': (p: Params) => {
    const src = workspace.storeSources[0]!
    const pkg = workspace.storeCatalogs[src.id]?.packages.find(item => item.kind === 'client' && (!p.presetId || item.builds.includes(p.presetId) || item.id === p.presetId))
      ?? workspace.storeCatalogs[src.id]?.packages.find(item => item.kind === 'client')
    return pkg ? clone({ source: src, package: pkg }) : null
  },
  'servers.installClient': (p: Params) => {
    const buildId = p.buildId || p.presetId || 'kr-812'
    return enqueueDemoDownload({
      kind: 'client',
      buildId,
      instanceId: p.instanceId,
      name: p.name || `Client ${buildId}`,
      fromVersion: '',
      toVersion: '2.0.48',
      directory: `D:\\Games\\BubbleFighter\\${buildId}`,
      sourceName: 'Fantasy Tale Archive',
      sourceKind: 'alist',
    })
  },
  'servers.catalog': (p: Params) => {
    const srv = workspace.servers.find(s => s.id === p.id)
    const gameId = srv?.game || 'bubblefighter'
    const buildIds = srv?.builds?.length ? srv.builds : ['kr-812', 'cn-279', 'cn-706']
    return {
      schema: 1,
      game: gameId,
      name: srv?.name || 'Demo Server',
      registrationEnabled: true,
      registrationFields: [
        { key: 'username', label: { en: 'Account', 'zh-CN': '账号' }, type: 'text', required: true, maxLength: 24, choices: [] },
        { key: 'password', label: { en: 'Password', 'zh-CN': '密码' }, type: 'password', required: true, maxLength: 32, choices: [] },
        { key: 'nickname', label: { en: 'Nickname', 'zh-CN': '游戏昵称' }, type: 'text', required: false, maxLength: 16, choices: [] },
      ],
      presentation: srv?.presentation ?? null,
      clientPolicy: srv?.clientPolicy ?? undefined,
      instances: buildIds.map(build => ({
        id: `preset-${srv?.id ?? 'srv'}-${build}`,
        name: { en: `${srv?.name ?? 'Server'} · ${build}` },
        build,
        endpoint: srv?.endpoint ?? { host: 'play.fantasytale.example', port: 7210 },
        releaseVersion: workspace.builds.find(b => b.id === build)?.releaseVersion ?? '1.0.0',
      })),
    }
  },
  'servers.status': (p: Params) => (p?.serverId || 'srv-fantasy-main') === 'srv-fantasy-main' ? demoServerStatus() : { sections: [], servers: [] },
  'servers.notices': (p: Params) => (p?.serverId || 'srv-fantasy-main') === 'srv-fantasy-main' ? clone(demoNotices.filter(notice => p?.all || !seenNotices.has(notice.id))) : [],
  'servers.markNoticesSeen': (p: Params) => { for (const id of p?.ids ?? []) seenNotices.add(id); return null },
  'servers.onlinePlayers': (p: Params) => clone(workspace.playersByServer[p.id] ?? { schema: 1, enabled: true, players: [] }),
  'servers.inspectProfile': (p: Params) => {
    const id = 'srv-imported-demo'
    const exists = workspace.servers.some(s => s.id === id)
    return {
      exists,
      identical: false,
      fingerprint: `demo-fp-${nextIdCounter}`,
      server: {
        id,
        game: p.game || 'bubblefighter',
        name: 'Dream Bubble Arena',
        apiUrl: 'https://dream.fantasytale.example',
        endpoint: { host: '127.0.0.1', port: 7210 },
        builds: ['kr-812', 'cn-706'],
        bundledInstances: false,
        builtIn: false,
      },
    }
  },
  'servers.importProfile': (p: Params) => {
    const existing = workspace.servers.find(s => s.id === 'srv-imported-demo')
    const created: Server = {
      id: 'srv-imported-demo',
      game: p.game || 'bubblefighter',
      name: 'Dream Bubble Arena',
      apiUrl: 'https://dream.fantasytale.example',
      endpoint: { host: '127.0.0.1', port: 7210 },
      builds: ['kr-812', 'cn-706'],
      bundledInstances: false,
      builtIn: false,
      presentation: {
        state: 'online',
        onlinePlayers: 22,
        playerLimit: 120,
        motd: 'Imported demo server profile.',
      },
    }
    if (existing) Object.assign(existing, created)
    else workspace.servers.push(created)
    return clone(created)
  },
  'servers.exportProfile': (p: Params) => {
    const srv = workspace.servers.find(s => s.id === p.id)
    return {
      fileName: `${srv?.id || 'server'}.toml`,
      text: demoSampleServerProfileToml,
    }
  },
  'servers.refreshProfile': (p: Params) => {
    const srv = workspace.servers.find(s => s.id === p.id)
    if (!srv) throw new Error('Server not found.')
    return clone(srv)
  },

  'directory.list': (_p: Params) => ({ schema: 1, entries: clone(workspace.directoryEntries) }),
  'directory.sources': (_p: Params) => clone(workspace.directorySources),
  'directory.saveSource': (p: Params) => {
    const idx = workspace.directorySources.findIndex(s => s.id === p.id)
    const entry: DirectorySource = {
      id: p.id || nextUid('dir-src'),
      name: p.name || 'Community Directory',
      baseUrl: p.baseUrl || p.url || 'https://directory.fantasytale.example/custom',
      enabled: p.enabled ?? true,
      builtIn: false,
    }
    if (idx >= 0) workspace.directorySources[idx] = entry
    else workspace.directorySources.push(entry)
    return clone(entry)
  },
  'directory.deleteSource': (p: Params) => {
    workspace.directorySources = workspace.directorySources.filter(s => s.id !== p.id)
    return null
  },
  'directory.publications': (_p: Params) => workspace.hosting.profiles
    .filter((pr: any) => pr.id === 'host-kr812')
    .map((pr: any) => ({
      profileId: pr.id,
      runId: 'host-run-1',
      sourceId: 'dir-src-official',
      roomId: 'dir-room-1',
      state: 'published' as const,
      expiresAt: '2026-11-13T10:00:00Z',
      error: null,
    })),
  'directory.publish': (p: Params) => ({
    profileId: p.id || 'host-kr812',
    runId: p.runId || 'host-run-1',
    sourceId: p.sourceId || 'dir-src-official',
    roomId: nextUid('room'),
    state: 'published',
    expiresAt: '2026-11-13T12:00:00Z',
    error: null,
  }),
  'directory.withdraw': (_p: Params) => null,
  'directory.profile': (p: Params) => ({
    text: demoSampleServerProfileToml,
    inspection: {
      exists: false,
      identical: false,
      fingerprint: `dir-fp-${p.entryId || 'entry'}`,
      server: clone(workspace.servers[0]!),
    },
  }),
  'directory.join': (p: Params) => {
    const entry = workspace.directoryEntries.find(e => e.id === p.entryId)
    return clone(workspace.servers.find(s => s.kind === 'room') ?? workspace.servers[0] ?? {
      id: p.entryId || 'srv-room-joined',
      game: entry?.details.game || 'bubblefighter',
      name: entry?.details.name || 'Joined Demo Room',
      apiUrl: null,
      endpoint: { host: '127.0.0.1', port: 7210 },
      builds: entry?.details.builds || ['kr-812'],
      bundledInstances: false,
      builtIn: false,
    })
  },
  'directory.onlinePlayers': (_p: Params) => clone(workspace.playersByServer['srv-fantasy-main']),
  'directory.submit': (p: Params) => {
    const details = p.details ?? p
    const entry: DirectoryEntry = {
      id: nextUid('dir-srv'),
      kind: 'server',
      expiresAt: '2026-11-14T00:00:00Z',
      details: {
        game: details.game || 'bubblefighter',
        name: details.name || 'Submitted Demo Server',
        apiUrl: details.apiUrl || 'https://custom.fantasytale.example',
        builds: details.builds?.length ? details.builds : ['kr-812'],
        description: details.description || 'Submitted via Demo Mode',
      },
    }
    workspace.directoryEntries.unshift(entry)
    return { id: entry.id, details: entry.details, submittedAt: new Date().toISOString() }
  },

  'accounts.list': (p: Params) => {
    const sid = p?.serverId || 'srv-fantasy-main'
    return clone(workspace.accountsByServer[sid] ?? workspace.accountsByServer['srv-fantasy-main'] ?? { current: null, accounts: [] })
  },
  'accounts.select': (p: Params) => {
    const sid = p.serverId || 'srv-fantasy-main'
    const list = workspace.accountsByServer[sid] ?? workspace.accountsByServer['srv-fantasy-main']
    if (list) {
      const found = list.accounts.find(a => sameAccount(a.account, p.username, p.section))
      if (found) list.current = clone(found.account)
    }
    return clone(list ?? { current: null, accounts: [] })
  },
  'accounts.current': (p: Params) => {
    const sid = p?.serverId || 'srv-fantasy-main'
    return clone((workspace.accountsByServer[sid] ?? workspace.accountsByServer['srv-fantasy-main'])?.current ?? null)
  },
  'accounts.rememberedLogin': (_p: Params) => null,
  'accounts.refresh': (p: Params) => {
    const sid = p?.serverId || 'srv-fantasy-main'
    return clone((workspace.accountsByServer[sid] ?? workspace.accountsByServer['srv-fantasy-main'])?.current ?? null)
  },
  'accounts.login': (p: Params) => {
    const sid = p.serverId || 'srv-fantasy-main'
    const username = (p.username || demoPlayerNicknames[0]).trim()
    const list = (workspace.accountsByServer[sid] ??= { current: null, accounts: [] })
    const existing = list.accounts.find(a => sameAccount(a.account, username, p.section))
    if (existing) {
      list.current = clone(existing.account)
      return clone(existing.account)
    }
    const profile = {
      section: p.section ?? null,
      username,
      displayName: username,
      stats: { level: 10, lucci: 12000, cash: 500 },
      levelIconUrl: null,
      hasCharacter: true,
    }
    list.current = clone(profile)
    list.accounts.push({ account: profile, expiresAt: '2026-12-31T23:59:59Z' })
    return clone(profile)
  },
  'accounts.register': (p: Params) => {
    handlers['accounts.login']!(p)
    return null
  },
  'accounts.logout': (p: Params) => {
    const sid = p.serverId || 'srv-fantasy-main'
    const list = workspace.accountsByServer[sid]
    if (list) {
      list.accounts = list.accounts.filter(a => !sameAccount(a.account, p.username, p.section))
      if (list.current && sameAccount(list.current, p.username, p.section)) list.current = list.accounts[0]?.account ?? null
    }
    return null
  },

  'plugins.list': (p: Params) => filterPluginReleases(p?.instanceId, p?.serverId),
  'plugins.configure': (p: Params) => {
    const inst = workspace.instances.find(i => i.id === (p.instanceId || p.id))
    if (!inst) throw new Error('Instance not found.')
    const id = p.id as string
    const version = p.version as string
    const enabled = Boolean(p.enabled)
    const options = clone(p.options ?? {})
    const existing = inst.plugins.find(item => item.id === id)
    if (existing) {
      existing.version = version || existing.version
      existing.enabled = enabled
      if (p.options) existing.options = options
    } else {
      inst.plugins.push({ id, version: version || '1.0.0', enabled, required: false, options })
    }
    emit('plugins.changed', workspace.plugins)
    emit('instances.changed', workspace.instances)
    return clone(inst)
  },
  'plugins.resolveScheme': (p: Params) => clone(p?.plugins ?? []),
  'plugins.applyScheme': (p: Params) => {
    const inst = workspace.instances.find(i => i.id === p.instanceId)
    if (!inst) throw new Error('Instance not found.')
    inst.plugins = Array.isArray(p.plugins) ? clone(p.plugins) : inst.plugins
    emit('plugins.changed', workspace.plugins)
    emit('instances.changed', workspace.instances)
    return clone(inst)
  },
  'plugins.scheme.parse': (p: Params) => ({
    game: 'bubblefighter',
    name: p?.name || 'Competitive KR-812 Preset',
    builds: [
      {
        id: 'kr-812',
        plugins: [
          { id: 'bubblefighter.fantasy.font-support-cn', version: '1.0.0', enabled: true, options: {} },
          { id: 'bubblefighter.fantasy.no-file-verify', version: '1.0.0', enabled: true, options: {} },
        ],
      },
    ],
  }),
  'plugins.scheme.format': (_p: Params) => demoSampleSchemeToml,
  'plugins.uninstall': (p: Params) => {
    workspace.plugins = workspace.plugins.filter(pkg => !(pkg.id === p.id && (!p.version || pkg.version === p.version)))
    for (const inst of workspace.instances) {
      inst.plugins = inst.plugins.filter(sel => !(sel.id === p.id && (!p.version || sel.version === p.version)))
    }
    emit('plugins.changed', workspace.plugins)
    return null
  },
  'plugins.uninstallRejected': (_p: Params) => null,
  'plugins.configurationContext': (_p: Params) => ({}),
  'plugins.configurationPage': (_p: Params) => demoCustomHtmlPluginPage,
  'plugins.prepareImport': (p: Params) => {
    if (p?.operationId) {
      emit('plugins.importProgress', { operationId: p.operationId, stage: 'checking', percent: 60 })
    }
    return {
      token: `import-tok-${++nextIdCounter}`,
      id: 'bubblefighter.fantasy.community-pack',
      version: '1.0.0',
      kind: 'patch',
      name: { en: 'Community Texture Sample', 'zh-CN': '本地导入插件包示例' },
      description: { en: 'Local package inspected inside UI Demo Mode.', 'zh-CN': '演示模式本地包导入测试条目。' },
      fileCount: 14,
      size: 2.4 * mib,
    }
  },
  'plugins.installImport': (p: Params) => {
    const id = `bubblefighter.fantasy.local-sample-${++nextIdCounter}`
    if (p?.operationId) {
      emit('plugins.importProgress', { operationId: p.operationId, stage: 'installing', percent: 75 })
    }
    workspace.plugins.push({
      id,
      game: p?.game || 'bubblefighter',
      version: '1.0.0',
      name: { en: 'Local Imported Plugin', 'zh-CN': `本地导入插件 #${nextIdCounter}` },
      description: { en: 'Installed from simulated local package file.', 'zh-CN': '通过演示模式模拟本地文件导入的插件。' },
      authors: ['Local Author'],
      kind: 'patch',
      available: true,
      required: false,
      canUninstall: true,
      launcherCompatible: true,
      languagePackCompatible: true,
      options: [],
      selection: { id, version: '1.0.0', enabled: Boolean(p?.enable ?? true), required: false, options: {} },
    })
    workspace.pluginCompatibility[id] = ['kr-812', 'cn-706']
    if (p?.operationId) {
      emit('plugins.importProgress', { operationId: p.operationId, stage: 'completed', percent: 100 })
    }
    emit('plugins.changed', workspace.plugins)
    return { id, version: '1.0.0', skipped: [] }
  },
  'plugins.discardImport': (_p: Params) => null,
  'plugins.loadIssues': (_p: Params) => [],
  'plugins.recoveryNotices': (_p: Params) => [],
  'plugins.deprecationNotices': (_p: Params) => [],
  'plugins.dismissRecoveryNotice': (_p: Params) => null,
  'plugins.pendingUpgrades': (_p: Params) => ({ count: 0, waitingForExit: 0, failure: null }),
  'plugins.updateSnapshot': (_p: Params) => ({ checking: false, checkedAt: new Date().toISOString(), updates: [], error: null }),
  'plugins.checkUpdates': (_p: Params) => [
    {
      id: 'bubblefighter.fantasy.font-support-cn',
      installedVersion: '1.0.0',
      version: '1.2.0',
      status: 'available',
      manifestSha256: 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
      sourceName: 'Fantasy Tale Archive',
    },
  ],
  'plugins.installUpdate': (p: Params) => enqueueDemoDownload({
    kind: 'plugin',
    buildId: 'kr-812',
    instanceId: p.instanceId,
    packageId: p.id,
    name: p.id,
    fromVersion: p.installedVersion || '1.0.0',
    toVersion: p.version || '1.2.0',
    directory: 'D:\\Games\\Plugins',
    sourceName: 'Fantasy Tale Archive',
    sourceKind: 'alist',
  }),
  'plugins.ensureLatest': (_p: Params) => [],
  'plugins.launchWarnings': (_p: Params) => [],
  'plugins.launchTransfers': (_p: Params) => [],
  'plugins.launchUpdates': (_p: Params) => ({ checking: false, items: [] }),
  'pluginPolicy.check': (p: Params) => evaluateDemoPolicy(p),
  'pluginPolicy.supportOffers': (_p: Params) => clone(pendingSupportOffers),
  'pluginPolicy.answerSupportOffer': (p: Params) => {
    pendingSupportOffers = pendingSupportOffers.filter(o => !(o.game === p.game && o.plugin === p.plugin && o.version === p.version))
    return { skipped: [] }
  },
  'clientPolicy.claimPlugins': (_p: Params) => [],
  'clientPolicy.acceptPlugins': (_p: Params) => [],
  'plugins.installationPlan': (_p: Params) => [
    {
      id: 'bubblefighter.fantasy.font-support-cn',
      name: { en: 'Chinese font support', 'zh-CN': '中文字体支持' },
      description: { en: 'Improves CJK text rendering weight.', 'zh-CN': '改善非国服客户端的中文字体显示。' },
      required: false,
      defaultSelected: true,
    },
    {
      id: 'bubblefighter.fantasy.no-file-verify',
      name: { en: 'Remove file verification', 'zh-CN': '移除文件校验' },
      description: { en: 'Required when running translation or custom packs.', 'zh-CN': '加载翻译包或插件时的基础校验移除组件。' },
      required: true,
      defaultSelected: true,
    },
  ],
  'plugins.installationComponents': (p: Params) => {
    const ids: string[] = Array.isArray(p.ids) ? p.ids : []
    return ids.map(id => enqueueDemoDownload({
      kind: 'plugin',
      buildId: 'kr-812',
      instanceId: p.instanceId,
      packageId: id,
      name: id,
      fromVersion: '',
      toVersion: '1.0.0',
      directory: 'D:\\Games\\Plugins',
      sourceName: 'Fantasy Tale Archive',
      sourceKind: 'alist',
    }))
  },

  'store.sources': (_p: Params) => clone(workspace.storeSources),
  'store.saveSource': (p: Params) => {
    const idx = workspace.storeSources.findIndex(s => s.id === p.id)
    const saved: any = {
      id: p.id || nextUid('src'),
      name: p.name || 'Custom Store Source',
      kind: p.kind || 'alist',
      baseUrl: p.baseUrl || 'https://custom.fantasytale.example',
      catalogPath: p.catalogPath || '/catalog.toml',
      enabled: p.enabled ?? true,
      builtIn: false,
    }
    if (idx >= 0) workspace.storeSources[idx] = saved
    else {
      workspace.storeSources.push(saved)
      workspace.storeCatalogs[saved.id] = { id: saved.id, name: { en: saved.name }, packages: [] }
    }
    emit('store.changed')
    return clone(saved)
  },
  'store.removeSource': (p: Params) => {
    workspace.storeSources = workspace.storeSources.filter(s => s.id !== p.id)
    delete workspace.storeCatalogs[p.id]
    emit('store.changed')
    return null
  },
  'store.catalog': (p: Params) => clone(workspace.storeCatalogs[p.id] ?? { id: p.id, name: { en: p.id }, packages: [] }),
  'store.sync': (_p: Params) => workspace.storeSources.map(s => ({ sourceId: s.id, status: 'current', checkedAt: new Date().toISOString(), failure: null })),
  'store.syncState': (_p: Params) => workspace.storeSources.map(s => ({ sourceId: s.id, status: 'current', checkedAt: new Date().toISOString(), failure: null })),
  'store.installClient': (p: Params) => {
    const pkg = Object.values(workspace.storeCatalogs).flatMap(c => c.packages).find(item => item.id === p.packageId && (!p.version || item.version === p.version))
    const src = workspace.storeSources.find(s => s.id === p.sourceId)
    return enqueueDemoDownload({
      kind: 'client',
      buildId: pkg?.builds?.[0] || 'kr-812',
      instanceId: p.instanceId || undefined,
      packageId: pkg?.id || p.packageId,
      name: pkg?.name?.['zh-CN'] || pkg?.name?.en || p.packageId || 'Client',
      nameLocalized: pkg?.name,
      fromVersion: '',
      toVersion: pkg?.version || p.version || '2.0.48',
      directory: p.directory || `D:\\Games\\BubbleFighter\\${pkg?.builds?.[0] || 'kr-812'}`,
      sourceName: src?.name || 'Fantasy Tale Archive',
      sourceKind: src?.kind || 'alist',
    })
  },
  'store.installPlugin': (p: Params) => {
    const pkg = Object.values(workspace.storeCatalogs).flatMap(c => c.packages).find(item => item.id === p.packageId && (!p.version || item.version === p.version))
    const src = workspace.storeSources.find(s => s.id === p.sourceId)
    const inst = p.instanceId ? workspace.instances.find(i => i.id === p.instanceId) : undefined
    const installed = inst?.plugins.find(item => item.id === (pkg?.id || p.packageId))
    const toVersion = pkg?.version || p.version || '1.0.0'
    return enqueueDemoDownload({
      kind: 'plugin',
      buildId: pkg?.builds?.[0],
      instanceId: p.instanceId || undefined,
      packageId: pkg?.id || p.packageId,
      name: pkg?.name?.['zh-CN'] || pkg?.name?.en || p.packageId || 'Plugin',
      nameLocalized: pkg?.name,
      fromVersion: installed && installed.version !== toVersion ? installed.version : '',
      toVersion,
      directory: 'D:\\Games\\Plugins',
      sourceName: src?.name || 'Fantasy Tale Archive',
      sourceKind: src?.kind || 'alist',
    })
  },
  'store.installPluginTargets': (p: Params) => handlers['store.installPlugin']!({ ...p, instanceId: Array.isArray(p.instanceIds) ? p.instanceIds[0] : p.instanceId }),
  'store.downloadPlugin': (p: Params) => handlers['store.installPlugin']!(p),
  'store.installServer': (p: Params) => {
    const pkg = Object.values(workspace.storeCatalogs)
      .flatMap(c => c.packages)
      .find(item => item.id === p.packageId && (!p.version || item.version === p.version))
    const src = workspace.storeSources.find(s => s.id === p.sourceId)
    return enqueueDemoDownload({
      kind: 'server',
      buildId: pkg?.builds?.[0],
      packageId: p.packageId,
      name: pkg?.name?.['zh-CN'] || pkg?.name?.en || p.packageId || 'Server',
      nameLocalized: pkg?.name,
      fromVersion: '',
      toVersion: p.version || '1.0.0',
      directory: `D:\\Games\\Servers\\${p.packageId || 'server'}`,
      sourceName: src?.name || 'Fantasy Tale Archive',
      sourceKind: src?.kind || 'alist',
    })
  },

  'downloads.list': (_p: Params) => clone(workspace.downloads),
  'downloads.action': (p: Params) => {
    const t = workspace.downloads.find(d => d.id === p.id)
    if (t) {
      if (p.action === 'pause') {
        t.resumeStage = t.stage === 'verifying' ? 'verifying' : 'downloading'
        t.stage = 'paused'
        t.bytesPerSecond = 0
      } else if (p.action === 'resume' || p.action === 'retry') {
        t.failure = null
        t.stage = t.resumeStage || 'downloading'
        t.bytesPerSecond = 11.4 * mib
        ensureDownloadTicker()
      } else if (p.action === 'cancel') {
        t.stage = 'canceled'
        t.bytesPerSecond = 0
      }
      emit('downloads.changed', workspace.downloads)
    }
    return clone(workspace.downloads)
  },
  'downloads.clearFinished': (_p: Params) => {
    workspace.downloads = workspace.downloads.filter(t => t.stage !== 'completed' && t.stage !== 'canceled')
    emit('downloads.changed', workspace.downloads)
    return clone(workspace.downloads)
  },

  'updates.list': (_p: Params) => clone(workspace.clientUpdates),
  'updates.check': (_p: Params) => {
    const cuState = workspace.clientUpdates as any
    cuState.checking = false
    const kr = cuState.clients.find((c: any) => c.instanceId === 'demo-bf-kr812')
    if (kr) {
      kr.status = 'available'
      kr.installedVersion = '2.0.47'
      kr.version = '2.0.48'
    }
    return clone(workspace.clientUpdates)
  },
  'updates.install': (p: Params) => {
    const cu = (workspace.clientUpdates as any).clients.find((c: any) => c.instanceId === p.instanceId)
    const inst = workspace.instances.find(i => i.id === p.instanceId)
    return enqueueDemoDownload({
      kind: 'client',
      buildId: inst?.build || 'kr-812',
      instanceId: p.instanceId,
      name: inst?.name || p.instanceId,
      fromVersion: cu?.installedVersion || '',
      toVersion: cu?.version ?? '2.0.48',
      directory: inst?.installationPath || 'D:\\Games\\BubbleFighter\\kr-812',
      sourceName: cu?.sourceName || 'Fantasy Tale Archive',
      sourceKind: 'alist',
    })
  },
  'settings.updates': (_p: Params) => ({ autoCheck: true, autoDownload: false, autoCheckPlugins: true, autoUpdatePlugins: true, removeOldPluginVersions: true }),
  'settings.setUpdates': (p: Params) => ({ autoCheck: Boolean(p.autoCheck), autoDownload: Boolean(p.autoDownload), autoCheckPlugins: Boolean(p.autoCheckPlugins ?? true), autoUpdatePlugins: Boolean(p.autoUpdatePlugins ?? true), removeOldPluginVersions: Boolean(p.removeOldPluginVersions ?? true) }),
  'settings.launcherLanguage': (_p: Params) => null,
  'settings.overlayTest': (_p: Params) => clone(overlayTestPrefs),
  'settings.setOverlayTest': (p: Params) => {
    overlayTestPrefs = { enabled: Boolean(p.enabled), passThroughOutside: Boolean(p.passThroughOutside ?? overlayTestPrefs.passThroughOutside) }
    return clone(overlayTestPrefs)
  },

  'launcherUpdates.state': (_p: Params) => clone(workspace.launcherUpdate),
  'launcherUpdates.sourceResults': (_p: Params) => workspace.launcherUpdateSources.map(s => ({ name: s.name, state: clone(workspace.launcherUpdate) })),
  'launcherUpdates.lastAttempt': (_p: Params) => null,
  'launcherUpdates.manualDownload': (_p: Params) => ({ url: 'http://archive-cn.600819.xyz:5244/FantasyTale/launcher/win-x64/portable', version: '0.9.5' }),
  'launcherUpdates.preferences': (_p: Params) => clone(workspace.launcherUpdatePreferences),
  'launcherUpdates.setPreferences': (p: Params) => {
    Object.assign(workspace.launcherUpdatePreferences, p)
    return clone(workspace.launcherUpdatePreferences)
  },
  'launcherUpdates.sources': (_p: Params) => clone(workspace.launcherUpdateSources),
  'launcherUpdates.setSources': (p: Params) => {
    if (Array.isArray(p?.sources)) workspace.launcherUpdateSources = clone(p.sources)
    return clone(workspace.launcherUpdateSources)
  },
  'launcherUpdates.check': (_p: Params) => {
    workspace.launcherUpdate = {
      status: 'available',
      version: '0.9.5',
      revision: 15,
      manifestSha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f9011223344556677889900aabbccddeeff',
      failure: null,
    }
    return clone(workspace.launcherUpdate)
  },
  'launcherUpdates.download': (_p: Params) => {
    startLauncherDownloadAnimation()
    return clone(workspace.downloads.find(d => d.target.kind === 'launcher') ?? enqueueDemoDownload({
      kind: 'launcher',
      name: 'Fantasy Tale Launcher',
      fromVersion: '0.9.0',
      toVersion: '0.9.5',
      directory: 'D:\\Apps\\FantasyTale',
      sourceName: 'Fantasy Tale Official CDN',
      sourceKind: 'alist',
    }, 'downloading'))
  },
  'launcherUpdates.restart': (_p: Params) => {
    workspace.launcherUpdate.status = 'current'
    workspace.launcherUpdate.version = '0.9.5'
    return null
  },
  'launcherUpdates.openLog': (_p: Params) => null,

  'settings.network': (_p: Params) => clone(workspace.network),
  'settings.showGitHubMirrorSuggestion': (_p: Params) => {
    workspace.network.suggestionPending = false
    return clone(workspace.network)
  },
  'settings.setNetwork': (p: Params) => {
    if ('ipPreference' in p && p.ipPreference) workspace.network.ipPreference = p.ipPreference
    if ('githubMirror' in p) workspace.network.gitHubMirror = Boolean(p.githubMirror)
    if ('githubMirrorSource' in p && p.githubMirrorSource) workspace.network.gitHubMirrorSource = p.githubMirrorSource
    if ('dismissSuggestion' in p && p.dismissSuggestion) workspace.network.suggestionPending = false
    return clone(workspace.network)
  },
  'settings.githubMirrors': (_p: Params) => clone(workspace.mirrors),
  'settings.testGitHubMirrors': async (_p: Params) => {
    workspace.mirrors.results = workspace.mirrors.providers.map((provider, i) => ({
      id: provider.id,
      state: i % 5 === 4 ? 'failed' : 'available',
      milliseconds: i % 5 === 4 ? null : 32 + ((i * 19) % 190),
      bytesPerSecond: i % 5 === 4 ? null : Math.round((11.2 - (i % 6) * 1.1) * mib),
    }))
    return clone(workspace.mirrors.results)
  },

  'hosting.list': (_p: Params) => clone({ profiles: workspace.hosting.profiles, sessions: workspace.hosting.sessions }),
  'hosting.packages': (_p: Params) => clone(workspace.hosting.packages),
  'hosting.availablePorts': (_p: Params) => [17230, 17231, 18090, 18094],
  'hosting.localAddresses': (_p: Params) => ['127.0.0.1', '192.168.1.24'],
  'hosting.destination': (_p: Params) => clone(workspace.servers.find(s => s.kind === 'room') ?? workspace.servers[0]!),
  'hosting.releasePrivate': (_p: Params) => null,
  'hosting.save': (p: Params) => {
    const profiles = workspace.hosting.profiles as any[]
    const existing = p.id ? profiles.find(pr => pr.id === p.id) : undefined
    if (existing) {
      Object.assign(existing, p)
      return clone(existing)
    }
    const created = {
      id: nextUid('host'),
      game: p.game || 'bubblefighter',
      name: p.name || 'New Local Server',
      build: p.build || 'kr-812',
      mode: p.mode || 'room',
      packagePath: p.packagePath || 'D:\\Games\\Servers\\bfserver-emulator-v3',
      advertisedHost: p.advertisedHost || '127.0.0.1',
      requireAuthentication: p.requireAuthentication ?? true,
      temporaryData: p.temporaryData ?? false,
      gamePort: p.gamePort || 7215,
      relayPort: p.relayPort || 7216,
      apiPort: p.apiPort || 7217,
      adminPort: p.adminPort || 7218,
      clientInstanceId: p.clientInstanceId ?? null,
    }
    profiles.push(created)
    return clone(created)
  },
  'hosting.delete': (p: Params) => {
    workspace.hosting.profiles = workspace.hosting.profiles.filter((pr: any) => pr.id !== p.id)
    workspace.hosting.sessions = workspace.hosting.sessions.filter((s: any) => s.profileId !== p.id)
    return null
  },
  'hosting.start': (p: Params) => {
    const profile = (workspace.hosting.profiles as any[]).find(pr => pr.id === p.id)
    if (!profile) throw new Error('Hosting profile not found.')
    const runId = nextUid('host-run')
    const port = profile.gamePort || 7210
    const session: any = {
      runId,
      profileId: profile.id,
      state: 'starting',
      processId: 24200 + (++nextIdCounter),
      startedAt: new Date().toISOString(),
      exitCode: null,
      failure: null,
      forcedStop: false,
      apiUrl: `http://127.0.0.1:${profile.apiPort || 7212}`,
      adminUrl: `http://127.0.0.1:${profile.adminPort || 7213}`,
      logPath: `D:\\Games\\Servers\\bfserver-emulator-v3\\logs\\${runId}.log`,
    }
    workspace.hosting.sessions = workspace.hosting.sessions.filter((s: any) => s.profileId !== profile.id)
    workspace.hosting.sessions.push(session)
    workspace.hosting.logs[runId] = [
      `[${new Date().toTimeString().slice(0, 8)}] [Host] Starting "${profile.name}" (${profile.build})...`,
    ]
    setTimeout(() => {
      session.state = 'ready'
      workspace.hosting.logs[runId]?.push(
        `[${new Date().toTimeString().slice(0, 8)}] [Game] Server ready on 127.0.0.1:${port}`,
      )
    }, 350)
    return clone(session)
  },
  'hosting.stop': (p: Params) => {
    const target: any = workspace.hosting.sessions.find((s: any) => s.profileId === p.id || s.runId === p.runId)
    if (target) {
      target.state = 'stopped'
      workspace.hosting.logs[target.runId]?.push(`[${new Date().toTimeString().slice(0, 8)}] [Host] Server stopped cleanly.`)
    }
    workspace.hosting.sessions = workspace.hosting.sessions.filter((s: any) => s.profileId !== p.id && s.runId !== p.runId)
    return clone(target ?? { runId: p.runId || '', profileId: p.id || '', state: 'stopped', processId: 0, startedAt: new Date().toISOString(), exitCode: 0, failure: null, forcedStop: false, apiUrl: null, adminUrl: null, logPath: '' })
  },
  'hosting.log': (p: Params) => clone(workspace.hosting.logs[p.runId] ?? []),

  'nativeUi.capabilities': (_p: Params) => clone(workspace.nativeUi.capabilities),
  'nativeUi.invoke': (p: Params) => {
    const req = p?.request ?? p
    const opId = req?.operationId || nextUid('op')
    const kind = req?.kind
    if (kind === 'control.inspect') {
      return { operationId: opId, status: 'ok', controls: clone(workspace.nativeUi.controls), cursor: workspace.nativeUi.cursor }
    }
    if (kind === 'control.setText') {
      const match = workspace.nativeUi.controls.find(c =>
        (req.target?.id && c.id === req.target.id)
        || (req.target?.name && c.name === req.target.name),
      )
      if (match) match.text = req.text ?? ''
      return { operationId: opId, status: 'ok' }
    }
    if (kind === 'control.addButton') {
      const def = req.definition
      const newCtrl = {
        id: nextUid('ctrl-btn'),
        form: req.target?.form || 'MainLobbyForm',
        name: def?.name || `btnDemo${nextIdCounter}`,
        kind: 'button',
        text: req.text || 'Demo Button',
        writable: true,
        x: def?.x ?? 20,
        y: def?.y ?? 20,
        width: def?.width ?? 120,
        height: def?.height ?? 32,
        enabled: true,
      }
      workspace.nativeUi.controls.push(newCtrl)
      return { operationId: opId, status: 'ok', controls: [clone(newCtrl)] }
    }
    if (kind === 'control.remove') {
      workspace.nativeUi.controls = workspace.nativeUi.controls.filter(c => c.id !== req.target?.id && c.name !== req.target?.name)
      return { operationId: opId, status: 'ok' }
    }
    if (kind === 'control.events') {
      return { operationId: opId, status: 'ok', events: clone(workspace.nativeUi.events), cursor: workspace.nativeUi.cursor }
    }
    if (kind === 'control.activate') {
      workspace.nativeUi.cursor += 1
      workspace.nativeUi.events.push({
        sequence: workspace.nativeUi.cursor,
        controlId: req.target?.id || workspace.nativeUi.controls[0]?.id || 'ctrl-1',
        event: 'click',
      })
      return { operationId: opId, status: 'ok', cursor: workspace.nativeUi.cursor }
    }
    return {
      operationId: opId,
      status: 'ok',
      buttonId: req?.buttons?.[0]?.id ?? null,
    }
  },
  'nativeUi.result': (p: Params) => ({ operationId: String(p?.operationId ?? ''), status: 'ok' }),

  'shell.openExternal': (_p: Params) => null,
  'shell.pickFolder': (_p: Params) => 'D:\\Games\\BubbleFighter\\demo-selected',
  'shell.pickPluginZip': (_p: Params) => 'D:\\Downloads\\demo-plugin-1.0.0.ftpkg',
  'shell.importServerImages': (_p: Params) => [],
  'shell.pickServerProfile': (_p: Params) => demoSampleServerProfileToml,
  'shell.saveServerProfile': (_p: Params) => true,
  'shell.pickPluginScheme': (_p: Params) => demoSampleSchemeToml,
  'shell.savePluginScheme': (_p: Params) => true,
}

function evaluateDemoPolicy(p: Params): PolicyFinding[] {
  const instanceId = String(p.instanceId ?? '')
  if (!instanceId || activePolicyDemoInstanceId !== instanceId) return []
  const inst = workspace.instances.find(i => i.id === instanceId)
  if (!inst) return []

  const enabling = p.enabling as { id?: string } | undefined
  const disabling = p.disabling as { id?: string } | undefined
  const isEnabled = (id: string) => {
    if (disabling?.id === id) return false
    if (enabling?.id === id) return true
    return Boolean(inst.plugins.find(item => item.id === id)?.enabled)
  }
  const installedEntry = (id: string) => inst.plugins.find(item => item.id === id)
  const meta = (id: string) => workspace.plugins.find(item => item.id === id)
  const toTarget = (plugin: PluginSummary | undefined) => plugin ? ({
    id: plugin.id,
    version: plugin.version,
    name: clone(plugin.name),
    description: plugin.description ? clone(plugin.description) : null,
    kind: plugin.kind,
  }) : null

  const findings: PolicyFinding[] = []
  const langPlugin = meta('bubblefighter.fantasy.lang-zh-cn')
  const verifyPlugin = meta('bubblefighter.fantasy.no-file-verify')

  if (langPlugin && isEnabled(langPlugin.id)) {
    const fontSupport = meta('bubblefighter.fantasy.font-support-cn')
    if (fontSupport && !isEnabled(fontSupport.id)) {
      findings.push({
        plugin: langPlugin.id,
        pluginVersion: langPlugin.version,
        pluginName: clone(langPlugin.name),
        pluginDescription: clone(langPlugin.description),
        pluginKind: langPlugin.kind,
        pluginRequired: false,
        pluginActive: true,
        type: 'require',
        other: fontSupport.id,
        range: '>=1.0.0',
        status: 'disabled',
        blocks: true,
        defaultSelected: false,
        target: toTarget(fontSupport),
      })
    }

    if (!installedEntry('bubblefighter.fantasy.wide-chat')) {
      findings.push({
        plugin: langPlugin.id,
        pluginVersion: langPlugin.version,
        pluginName: clone(langPlugin.name),
        pluginDescription: clone(langPlugin.description),
        pluginKind: langPlugin.kind,
        pluginRequired: false,
        pluginActive: true,
        type: 'require',
        other: 'bubblefighter.fantasy.wide-chat',
        range: '>=0.3.0',
        status: 'missing',
        blocks: true,
        defaultSelected: false,
        target: null,
      })
    }

    const crashReporter = meta('bubblefighter.fantasy.crash-reporter')
    if (crashReporter && isEnabled(crashReporter.id)) {
      findings.push({
        plugin: langPlugin.id,
        pluginVersion: langPlugin.version,
        pluginName: clone(langPlugin.name),
        pluginDescription: clone(langPlugin.description),
        pluginKind: langPlugin.kind,
        pluginRequired: false,
        pluginActive: true,
        type: 'conflict',
        other: crashReporter.id,
        range: '*',
        status: 'conflict',
        blocks: true,
        defaultSelected: false,
        target: toTarget(crashReporter),
      })
    }

    const hudPlugin = meta('bubblefighter.fantasy.hud')
    if (hudPlugin && !isEnabled(hudPlugin.id)) {
      findings.push({
        plugin: langPlugin.id,
        pluginVersion: langPlugin.version,
        pluginName: clone(langPlugin.name),
        pluginDescription: clone(langPlugin.description),
        pluginKind: langPlugin.kind,
        pluginRequired: false,
        pluginActive: true,
        type: 'recommend',
        other: hudPlugin.id,
        range: '>=1.0.0',
        status: 'disabled',
        blocks: false,
        defaultSelected: false,
        target: toTarget(hudPlugin),
      })
    }
  }

  if (verifyPlugin && isEnabled(verifyPlugin.id)) {
    const fpsUnlock = meta('bubblefighter.fantasy.fps-unlock')
    const fpsInstalled = installedEntry('bubblefighter.fantasy.fps-unlock')
    if (fpsUnlock && fpsInstalled?.version === '0.2.0') {
      findings.push({
        plugin: verifyPlugin.id,
        pluginVersion: verifyPlugin.version,
        pluginName: clone(verifyPlugin.name),
        pluginDescription: clone(verifyPlugin.description),
        pluginKind: verifyPlugin.kind,
        pluginRequired: verifyPlugin.required,
        pluginActive: true,
        type: 'require',
        other: fpsUnlock.id,
        range: '>=0.3.0',
        status: 'version',
        blocks: true,
        defaultSelected: false,
        target: {
          id: fpsUnlock.id,
          version: fpsInstalled.version,
          name: clone(fpsUnlock.name),
          description: clone(fpsUnlock.description),
          kind: fpsUnlock.kind,
        },
      })
    }
  }

  return findings
}

/** Handles bridge invocations while UI Demo Mode is active. */
export async function demoHostCall<K extends keyof BridgeMethods>(
  method: K,
  params: unknown,
  emitter: Emitter,
): Promise<BridgeMethods[K]> {
  activeEmitter = emitter
  const handler = handlers[method]
  if (!handler) {
    return undefined as unknown as BridgeMethods[K]
  }
  const result = await handler((params ?? {}) as Params)
  return result as BridgeMethods[K]
}

/** Control helpers used by the Developer Options -> Demo Mode tab to trigger realistic states & animations. */
export const demoController = {
  getWorkspace(): DemoWorkspace {
    return workspace
  },
  resetWorkspace(): void {
    resetDemoWorkspace()
  },
  setLauncherUpdateState(status: LauncherUpdateState['status'], errorFailure?: 'network' | 'disk' | null): void {
    if (launcherTicker) {
      clearInterval(launcherTicker)
      launcherTicker = undefined
    }
    workspace.launcherUpdate = {
      status,
      version: status === 'unconfigured' || status === 'checking' ? null : '0.9.5',
      revision: status === 'unconfigured' ? null : 15,
      manifestSha256: 'a1b2c3d4e5f60718293a4b5c6d7e8f9011223344556677889900aabbccddeeff',
      failure: errorFailure ?? (status === 'failed' ? 'network' : null),
      failureReason: status === 'failed' ? 'timeout' : null,
      httpStatus: status === 'failed' ? 504 : null,
    }
  },
  triggerLauncherDownload(): void {
    startLauncherDownloadAnimation()
  },
  triggerInstallDemo(kind: 'client' | 'plugin' | 'server'): DownloadTask {
    return enqueueDemoDownload(
      kind === 'client'
        ? {
            kind: 'client',
            buildId: 'cn-706',
            name: 'China 2020-07-16 (cn-706)',
            nameLocalized: { en: 'China 2020-07-16 (cn-706)', 'zh-CN': '国服 2020-07-16 (cn-706)' },
            fromVersion: '',
            toVersion: '2.0.10',
            directory: 'D:\\Games\\BubbleFighter\\cn-706',
            sourceName: 'Fantasy Tale Archive',
            sourceKind: 'alist',
          }
        : kind === 'server'
          ? {
              kind: 'server',
              buildId: 'kr-1256',
              packageId: 'bfserver-sandbox-kr1256',
              name: 'KR-1256 Research Sandbox Server',
              nameLocalized: { en: 'KR-1256 Research Sandbox Server', 'zh-CN': 'KR-1256 实验研究服务端' },
              fromVersion: '',
              toVersion: '0.9.4',
              directory: 'D:\\Games\\Servers\\bfserver-sandbox-kr1256',
              sourceName: 'Fantasy Tale Archive',
              sourceKind: 'alist',
            }
          : {
              kind: 'plugin',
              buildId: 'kr-1256',
              packageId: 'bubblefighter.fantasy.shop-unlock',
              name: 'Shop access',
              nameLocalized: { en: 'Shop access', 'zh-CN': '商店开放' },
              fromVersion: '',
              toVersion: '1.0.0',
              directory: 'D:\\Games\\Plugins\\shop-unlock',
              sourceName: 'Fantasy Tale Archive',
              sourceKind: 'alist',
            },
      'checking',
    )
  },
  setMirrorSuggestion(pending: boolean): void {
    workspace.network.suggestionPending = pending
  },
  triggerSupportOffer(): void {
    pendingSupportOffers = [
      {
        game: 'bubblefighter',
        plugin: 'bubblefighter.fantasy.font-support-cn',
        version: '1.2.0',
        name: { en: 'Chinese font support', 'zh-CN': '中文字体支持' },
        description: {
          en: 'This update newly supports your CN 2011 Nostalgia client build.',
          'zh-CN': '本次插件升级新增支持您的怀旧版客户端，可按需勾选启用。',
        },
        clients: [
          { id: 'demo-bf-kr812', name: 'KR 2015 Classic (kr-812)', build: 'kr-812' },
          { id: 'demo-bf-cn279', name: 'CN 2011 Nostalgia (cn-279)', build: 'cn-279' },
        ],
      },
    ]
    emit('pluginPolicy.supportOffers')
  },
  triggerUpgradeRequired(): void {
    emit('launcher.upgradeRequired', {
      minimumLauncherVersion: '1.0.0',
      currentLauncherVersion: workspace.launcherUpdate.version || '0.9.0',
      subject: 'Fantasy Tale',
    })
  },
  preparePluginPolicyDemo(instanceId?: string): void {
    const inst = (instanceId ? workspace.instances.find(i => i.id === instanceId) : undefined) ?? workspace.instances[0]
    if (!inst) return
    const hudId = 'bubblefighter.fantasy.hud'
    if (!workspace.plugins.some(p => p.id === hudId)) {
      workspace.plugins.push({
        id: hudId,
        game: 'bubblefighter',
        version: '1.4.0',
        name: { en: 'HUD Overlay', 'zh-CN': 'HUD 悬浮层', 'zh-TW': 'HUD 懸浮層', ko: 'HUD 오버레이' },
        description: {
          en: 'Displays frame timing, latency and match telemetry in-game.',
          'zh-CN': '在游戏内实时显示延迟、帧率与对局状态。',
        },
        authors: ['Fantasy Tale Studio'],
        kind: 'overlay',
        available: true,
        required: false,
        canUninstall: true,
        launcherCompatible: true,
        languagePackCompatible: true,
        options: [],
        selection: { id: hudId, version: '1.4.0', enabled: false, required: false, options: {} },
      })
      workspace.pluginCompatibility[hudId] = ['kr-812', 'cn-706']
    }
    const fpsSummary = workspace.plugins.find(p => p.id === 'bubblefighter.fantasy.fps-unlock')
    if (fpsSummary) fpsSummary.version = '0.2.0'
    workspace.plugins = workspace.plugins.filter(p => p.id !== 'bubblefighter.fantasy.wide-chat')

    const upsert = (id: string, version: string, enabled: boolean, required = false) => {
      const idx = inst.plugins.findIndex(p => p.id === id)
      const entry = { id, version, enabled, required, options: {} }
      if (idx >= 0) inst.plugins[idx] = entry
      else inst.plugins.push(entry)
    }
    inst.plugins = inst.plugins.filter(p => p.id !== 'bubblefighter.fantasy.wide-chat')
    upsert('bubblefighter.fantasy.lang-zh-cn', '2.1.0', true, false)
    upsert('bubblefighter.fantasy.no-file-verify', '1.0.0', true, true)
    upsert('bubblefighter.fantasy.font-support-cn', '1.0.0', false, false)
    upsert('bubblefighter.fantasy.crash-reporter', '1.1.0', true, false)
    upsert('bubblefighter.fantasy.fps-unlock', '0.2.0', true, false)
    upsert(hudId, '1.4.0', false, false)
    activePolicyDemoInstanceId = inst.id
    emit('plugins.changed', workspace.plugins)
    emit('instances.changed', workspace.instances)
  },
  clearPluginPolicyDemo(): void {
    activePolicyDemoInstanceId = null
  },
}

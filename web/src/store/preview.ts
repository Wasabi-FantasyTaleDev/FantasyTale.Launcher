import { ref } from 'vue'
import builds from '../preview-builds.json'
import type { StoreCatalog, StoreSource } from './types'
import type { LocalPlugin } from './updates'

export const defaultStoreSource: StoreSource = { id: 'fantasy-tale', name: 'Fantasy Tale Archive', kind: 'alist', baseUrl: 'http://archive-cn.600819.xyz:5244/', catalogPath: 'FantasyTale/source.toml', enabled: true, builtIn: true, preferIp: true }
/** Display-only count for the isolated development catalog. */
export const previewStoreUpdateCount = ref<number>()
let sources: StoreSource[] = [{ ...defaultStoreSource }]

/** Browser edits stay in memory and never overwrite desktop configuration. */
export function previewSources() { return sources.map(source => ({ ...source })) }
export function savePreviewSource(source: StoreSource) {
  const saved = { ...source, builtIn: source.id === defaultStoreSource.id }
  sources = [...sources.filter(s => s.id !== source.id), saved]
  return saved
}
export function removePreviewSource(id: string) { sources = sources.filter(s => s.id !== id); return null }

/** Isolated multi-source fixtures used only by the development store preview. */
export function previewStoreSources(): StoreSource[] {
  return [{ ...defaultStoreSource },
    { ...defaultStoreSource, id: 'community-mirror', name: 'Community Mirror', kind: 'http', baseUrl: 'https://store-preview.invalid/', builtIn: false },
    { ...defaultStoreSource, id: 'archive-store', name: 'Archive Store', kind: 'github', baseUrl: 'https://github.com/example/store-preview/', builtIn: false }]
}

/** Opt-in catalog fixture; no direct URL or installable artifact is provided. */
export function previewStoreCatalog(sourceId = defaultStoreSource.id, morePlugins = false): StoreCatalog {
  const catalog: StoreCatalog = {
    id: 'fantasy-tale', name: { en: 'Fantasy Tale' }, packages: [
      ...builds.filter(build => build.enabled !== false && !build.hidden).map((build, index) => ({ id: `bubblefighter.${build.id}`, kind: 'client' as const, game: 'bubblefighter', builds: [build.id],
        version: build.releaseVersion || '1.0.0', name: build.name,
        description: { en: 'A separate Bubble Fighter client installation. Review the release and region before choosing an installation path.', 'zh-CN': '独立安装的泡泡战士客户端。你可以在这里查看版本与地区信息，并为不同版本使用独立的安装路径。', 'zh-TW': '獨立安裝的泡泡大亂鬥用戶端。可在此查看版本與地區資訊，並為不同版本使用獨立的安裝路徑。' },
        publisher: 'Fantasy Tale Studio', size: (980 + index * 215) * 1024 * 1024, manifest: `releases/${build.id}/manifest.toml`, manifestSha256: '0'.repeat(64) })),
      { id: 'bubblefighter.fantasy.font-support-cn', kind: 'plugin', game: 'bubblefighter', builds: ['kr-812', 'cn-706', 'cn-717', 'id-400', 'id-1809'], version: '1.0.0',
        name: { en: 'Font weight', 'zh-CN': '字体粗细修正', 'zh-TW': '字型粗細修正' },
        description: { en: 'Adjusts font weight on compatible clients. Compatibility is checked before enabling the plugin.', 'zh-CN': '修正兼容客户端中的字体粗细。启用前会检查客户端版本是否受支持。', 'zh-TW': '修正相容用戶端中的字型粗細，啟用前會檢查用戶端版本是否受支援。' },
        publisher: 'Fantasy Tale Studio', size: 48 * 1024, manifest: 'plugins/font-weight/1.0.0/manifest.toml', manifestSha256: '0'.repeat(64) },
      { id: 'bubblefighter.fantasy.diagnostics', kind: 'plugin', game: 'bubblefighter', builds: [], version: '1.0.0',
        name: { en: 'Crash recorder', 'zh-CN': '崩溃记录', 'zh-TW': '當機記錄' },
        description: { en: 'Preserves local diagnostics when the client unexpectedly exits.', 'zh-CN': '在客户端异常退出时保存本地诊断信息，帮助排查问题。', 'zh-TW': '在用戶端異常結束時保存本機診斷資訊，協助排查問題。' },
        publisher: 'Fantasy Tale Studio', size: 144 * 1024, manifest: 'plugins/crash-recorder/1.0.0/manifest.toml', manifestSha256: '0'.repeat(64) },
    ],
  }
  if (morePlugins) {
    const template = catalog.packages.find(item => item.kind === 'plugin')!
    catalog.packages.push(...Array.from({ length: 10 }, (_, index) => ({ ...template, id: `preview.plugin-${index}`,
      name: { en: `Preview plugin ${index + 1}`, 'zh-CN': `预览插件 ${index + 1}` }, version: '1.1.0',
      manifest: `plugins/preview-${index}/manifest.toml`, manifestSha256: String(index).repeat(64) })))
  }
  if (sourceId !== defaultStoreSource.id) {
    catalog.id = sourceId
    catalog.packages = catalog.packages.filter(item => item.kind === 'plugin').map(item => ({ ...item,
      version: sourceId === 'community-mirror' && item.id.endsWith('font-weight') ? '1.1.0' : item.version,
      manifestSha256: (sourceId === 'community-mirror' ? '1' : '2').repeat(64) }))
  }
  return catalog
}

/** Local-library fixtures never enter desktop inventories or persisted configuration. */
export function previewStorePlugins(morePlugins = false): LocalPlugin[] {
  return [{ game: 'bubblefighter', id: 'bubblefighter.fantasy.font-support-cn', version: '0.9.0' },
    { game: 'bubblefighter', id: 'bubblefighter.fantasy.diagnostics', version: '0.8.0' },
    ...(morePlugins ? Array.from({ length: 9 }, (_, index) => ({ game: 'bubblefighter', id: `preview.plugin-${index}`, version: index === 8 ? '1.1.0' : '1.0.0' })) : [])]
}

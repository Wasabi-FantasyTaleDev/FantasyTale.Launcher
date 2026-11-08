// Mock catalogue data so pages render like the real launcher before the .NET
// bridge methods exist. Every shape here mirrors what a future bridge call returns.
import type { LocalizedText } from './bridge'

export interface Instance {
  id: string
  name: string
  buildId: string
  serverId: string
  pluginCount: number
  /** ISO date of the last launch, or null if never launched. */
  lastPlayed: string | null
  sizeMb: number
}

export interface Server {
  id: string
  name: string
  url: string
  status: 'online' | 'offline'
  pingMs: number | null
  playersOnline: number
  maxPlayers: number
  /** Build ids this server accepts. */
  builds: string[]
}

export interface Download {
  id: string
  name: LocalizedText
  sizeMb: number
  doneMb: number
  /** Bytes per second for active transfers. */
  speedBps: number
  status: 'active' | 'queued' | 'paused' | 'completed'
}

export interface InstalledPlugin {
  id: string
  name: LocalizedText
  description: LocalizedText
  version: string
  enabled: boolean
  /** Build ids the plugin was verified against. */
  builds: string[]
}

export interface StorePlugin {
  id: string
  name: LocalizedText
  description: LocalizedText
  author: string
  version: string
  downloads: number
  category: 'patch' | 'feature' | 'locale'
  installed: boolean
}

export interface Account {
  id: string
  serverId: string
  username: string
}

export interface NewsItem {
  id: string
  title: string
  summary: string
  date: string
  tag: 'update' | 'event' | 'maintenance'
}

export const instances: Instance[] = [
  { id: 'ins-main', name: 'CN 2020 daily', buildId: 'cn-706', serverId: 'srv-cn', pluginCount: 3, lastPlayed: '2026-06-28T14:20:00', sizeMb: 1843 },
  { id: 'ins-kr', name: 'KR 2015 vanilla', buildId: 'kr-812', serverId: 'srv-kr', pluginCount: 1, lastPlayed: '2026-06-21T09:05:00', sizeMb: 1620 },
  { id: 'ins-dev', name: 'KR 2026 test', buildId: 'kr-1256', serverId: 'srv-kr', pluginCount: 2, lastPlayed: null, sizeMb: 1907 },
]

export const servers: Server[] = [
  { id: 'srv-cn', name: 'NetEase CN', url: 'https://bf.example.cn', status: 'online', pingMs: 32, playersOnline: 1284, maxPlayers: 4000, builds: ['cn-279', 'cn-324', 'cn-706', 'cn-717', 'cn-735'] },
  { id: 'srv-kr', name: 'Nexon KR', url: 'https://bf.example.kr', status: 'online', pingMs: 58, playersOnline: 862, maxPlayers: 3000, builds: ['kr-812', 'kr-1256'] },
  { id: 'srv-id', name: 'Garena ID', url: 'https://cs.example.id', status: 'offline', pingMs: null, playersOnline: 0, maxPlayers: 2000, builds: ['id-400', 'id-1809'] },
]

export const downloads: Download[] = [
  { id: 'dl-1', name: { en: 'Bubble Fighter client cn-706', 'zh-CN': '泡泡战士客户端 cn-706', 'zh-TW': '泡泡大亂鬥用戶端 cn-706', ko: '버블파이터 클라이언트 cn-706', id: 'Klien Bubble Fighter cn-706', vi: 'Client Bubble Fighter cn-706' }, sizeMb: 1843, doneMb: 1211, speedBps: 8_400_000, status: 'active' },
  { id: 'dl-2', name: { en: 'Plugin: Shop access', 'zh-CN': '插件：商店开放', 'zh-TW': '模組：商店開放', ko: '플러그인: 상점 입구 (KR 2026)', id: 'Plugin: Pintu masuk toko (KR 2026)', vi: 'Plugin: Lối vào cửa hàng (KR 2026)' }, sizeMb: 2, doneMb: 0, speedBps: 0, status: 'queued' },
  { id: 'dl-3', name: { en: 'Bubble Fighter client kr-812', 'zh-CN': '泡泡战士客户端 kr-812', 'zh-TW': '泡泡大亂鬥用戶端 kr-812', ko: '버블파이터 클라이언트 kr-812', id: 'Klien Bubble Fighter kr-812', vi: 'Client Bubble Fighter kr-812' }, sizeMb: 1620, doneMb: 1620, speedBps: 0, status: 'completed' },
  { id: 'dl-4', name: { en: 'Bubble Fighter client id-400', 'zh-CN': '泡泡战士客户端 id-400', 'zh-TW': '泡泡大亂鬥用戶端 id-400', ko: '버블파이터 클라이언트 id-400', id: 'Klien Crazy Shooter id-400', vi: 'Client Bubble Fighter id-400' }, sizeMb: 1480, doneMb: 512, speedBps: 0, status: 'paused' },
]

export const installedPlugins: InstalledPlugin[] = [
  {
    id: 'bubblefighter.fantasy.font-support-cn',
    version: '1.0.0',
    enabled: true,
    builds: ['kr-812', 'cn-706', 'cn-717', 'id-400', 'id-1809'],
    name: { "en": "Chinese font support", "zh-CN": "中文字体支持", "zh-TW": "中文字體支援", "ko": "중국어 글꼴 지원", "id": "Dukungan font Mandarin", "vi": "Hỗ trợ phông chữ tiếng Trung" },
    description: { "en": "Provides Simplified Chinese font support for non-Chinese clients and corrects overly thin text caused by font weights that differ from the original Chinese client settings.", "zh-CN": "对非国服客户端提供简体中文字体支持，并修复字重与国服原设定不同导致的字体过细问题", "zh-TW": "為非中國版本用戶端提供簡體中文字體支援，並修復字重與中國版本原始設定不同導致的字體過細問題", "ko": "중국 이외 지역 클라이언트에 중국어 간체 글꼴을 지원하고, 중국 클라이언트의 원래 설정과 다른 글꼴 굵기로 인해 글자가 지나치게 가늘어지는 문제를 수정합니다.", "id": "Menyediakan dukungan font Mandarin Sederhana untuk klien non-Tiongkok dan memperbaiki teks yang terlalu tipis akibat perbedaan ketebalan font dari pengaturan asli klien Tiongkok.", "vi": "Hỗ trợ phông chữ tiếng Trung giản thể cho máy khách ngoài Trung Quốc và sửa lỗi chữ quá mảnh do độ đậm khác với thiết lập gốc của máy khách Trung Quốc." },
  },
  {
    id: 'bubblefighter.fantasy.no-file-verify',
    version: '1.0.0',
    enabled: true,
    builds: ['kr-812', 'cn-706', 'cn-717', 'cn-735', 'id-400', 'id-1809'],
    name: { "en": "Remove file verification", "zh-CN": "移除文件校验", "zh-TW": "移除檔案校驗", "ko": "파일 검증 제거", "id": "Hapus verifikasi file", "vi": "Loại bỏ kiểm tra tệp" },
    description: { "en": "Removes checks for modified files. Disabling it may prevent entering the game.", "zh-CN": "移除对文件修改的校验，关闭后可能会导致无法进入游戏。", "zh-TW": "移除對檔案修改的校驗，關閉後可能會導致無法進入遊戲。", "ko": "수정된 파일의 검증을 제거합니다. 끄면 게임에 들어가지 못할 수 있습니다.", "id": "Menghapus pemeriksaan file yang diubah. Menonaktifkannya dapat menghalangi masuk ke game.", "vi": "Loại bỏ kiểm tra tệp đã sửa. Tắt có thể khiến bạn không vào được trò chơi." },
  },
  {
    id: 'bubblefighter.fantasy.shop-unlock',
    version: '1.0.0',
    enabled: false,
    builds: ['kr-1256'],
    name: { "en": "Shop access", "zh-CN": "商店开放", "zh-TW": "商店開放", "ko": "상점 이용", "id": "Akses toko", "vi": "Mở cửa hàng" },
    description: { "en": "Fixes shop access in the final Korean client.", "zh-CN": "修复韩服最终版本无法进入商店的问题", "zh-TW": "修復韓國最終版本無法進入商店的問題", "ko": "한국 최종 버전에서 상점에 들어갈 수 없는 문제를 수정합니다.", "id": "Memperbaiki akses toko pada versi Korea terakhir.", "vi": "Sửa lỗi không vào được cửa hàng ở phiên bản Hàn Quốc cuối cùng." },
  },
]

export const storePlugins: StorePlugin[] = [
  {
    id: 'bubblefighter.fantasy.font-support-cn',
    version: '1.0.0',
    author: 'Fantasy Tale Studio',
    downloads: 4210,
    category: 'locale',
    installed: true,
    name: { "en": "Chinese font support", "zh-CN": "中文字体支持", "zh-TW": "中文字體支援", "ko": "중국어 글꼴 지원", "id": "Dukungan font Mandarin", "vi": "Hỗ trợ phông chữ tiếng Trung" },
    description: { "en": "Provides Simplified Chinese font support for non-Chinese clients and corrects overly thin text caused by font weights that differ from the original Chinese client settings.", "zh-CN": "对非国服客户端提供简体中文字体支持，并修复字重与国服原设定不同导致的字体过细问题", "zh-TW": "為非中國版本用戶端提供簡體中文字體支援，並修復字重與中國版本原始設定不同導致的字體過細問題", "ko": "중국 이외 지역 클라이언트에 중국어 간체 글꼴을 지원하고, 중국 클라이언트의 원래 설정과 다른 글꼴 굵기로 인해 글자가 지나치게 가늘어지는 문제를 수정합니다.", "id": "Menyediakan dukungan font Mandarin Sederhana untuk klien non-Tiongkok dan memperbaiki teks yang terlalu tipis akibat perbedaan ketebalan font dari pengaturan asli klien Tiongkok.", "vi": "Hỗ trợ phông chữ tiếng Trung giản thể cho máy khách ngoài Trung Quốc và sửa lỗi chữ quá mảnh do độ đậm khác với thiết lập gốc của máy khách Trung Quốc." },
  },
  {
    id: 'bubblefighter.fantasy.no-file-verify',
    version: '1.0.0',
    author: 'Fantasy Tale Studio',
    downloads: 3988,
    category: 'patch',
    installed: true,
    name: { "en": "Remove file verification", "zh-CN": "移除文件校验", "zh-TW": "移除檔案校驗", "ko": "파일 검증 제거", "id": "Hapus verifikasi file", "vi": "Loại bỏ kiểm tra tệp" },
    description: { "en": "Removes checks for modified files. Disabling it may prevent entering the game.", "zh-CN": "移除对文件修改的校验，关闭后可能会导致无法进入游戏。", "zh-TW": "移除對檔案修改的校驗，關閉後可能會導致無法進入遊戲。", "ko": "수정된 파일의 검증을 제거합니다. 끄면 게임에 들어가지 못할 수 있습니다.", "id": "Menghapus pemeriksaan file yang diubah. Menonaktifkannya dapat menghalangi masuk ke game.", "vi": "Loại bỏ kiểm tra tệp đã sửa. Tắt có thể khiến bạn không vào được trò chơi." },
  },
  {
    id: 'bubblefighter.fantasy.shop-unlock',
    version: '1.0.0',
    author: 'Fantasy Tale Studio',
    downloads: 512,
    category: 'patch',
    installed: false,
    name: { "en": "Shop access", "zh-CN": "商店开放", "zh-TW": "商店開放", "ko": "상점 이용", "id": "Akses toko", "vi": "Mở cửa hàng" },
    description: { "en": "Fixes shop access in the final Korean client.", "zh-CN": "修复韩服最终版本无法进入商店的问题", "zh-TW": "修復韓國最終版本無法進入商店的問題", "ko": "한국 최종 버전에서 상점에 들어갈 수 없는 문제를 수정합니다.", "id": "Memperbaiki akses toko pada versi Korea terakhir.", "vi": "Sửa lỗi không vào được cửa hàng ở phiên bản Hàn Quốc cuối cùng." },
  },
  {
    id: 'bubblefighter.fantasy.wide-chat',
    version: '0.3.1',
    author: 'Fantasy Tale Studio',
    downloads: 187,
    category: 'feature',
    installed: false,
    name: {
      en: 'Wide chat box',
      'zh-CN': '宽聊天框',
      'zh-TW': '寬聊天框',
      ko: '넓은 채팅 상자',
      id: 'Kotak obrolan lebar',
      vi: 'Khung chat rộng',
    },
    description: {
      en: 'Widens the lobby chat box so long names and CJK text fit on one line.',
      'zh-CN': '加宽大厅聊天框，让长名字和中文文本放得下。',
    },
  },
  {
    id: 'bubblefighter.fantasy.fps-unlock',
    version: '0.2.0',
    author: 'Fantasy Tale Studio',
    downloads: 1044,
    category: 'feature',
    installed: false,
    name: {
      en: 'Frame rate unlock',
      'zh-CN': '帧率解锁',
      'zh-TW': '影格率解鎖',
      ko: '프레임 제한 해제',
      id: 'Buka batas FPS',
      vi: 'Mở khóa FPS',
    },
    description: {
      en: 'Raises the rendering frame-rate cap on builds that ship with one.',
      'zh-CN': '提高自带帧率上限的客户端版本的渲染上限。',
    },
  },
]

export const accounts: Account[] = [
  { id: 'acc-1', serverId: 'srv-cn', username: 'BubbleCat' },
  { id: 'acc-2', serverId: 'srv-kr', username: '바람돌이' },
]

export const news: NewsItem[] = [
  {
    id: 'n1',
    title: 'Launcher preview refresh',
    summary: 'The launcher frontend now follows the Windows 11 Fluent design: Mica surfaces, NavigationView and themed controls.',
    date: '2026-07-02',
    tag: 'update',
  },
  {
    id: 'n2',
    title: 'Summer splash event',
    summary: 'Double water-balloon damage weekends run through July on supported servers.',
    date: '2026-06-27',
    tag: 'event',
  },
  {
    id: 'n3',
    title: 'Store mirror maintenance',
    summary: 'Plugin downloads may be slower while the mirror rotates to a new CDN edge.',
    date: '2026-06-19',
    tag: 'maintenance',
  },
]

export function formatSize(mb: number): string {
  return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${Math.round(mb)} MB`
}

export function formatSpeed(bps: number): string {
  if (bps <= 0) return ''
  const mbps = bps / 1_048_576
  return mbps >= 1 ? `${mbps.toFixed(1)} MB/s` : `${Math.round(bps / 1024)} KB/s`
}

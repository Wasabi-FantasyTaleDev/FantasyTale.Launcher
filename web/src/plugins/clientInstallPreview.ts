import type { BuildSummary, ClientComponent } from '../bridge'

/** Opt-in installation fixtures never supply executable downloads or desktop settings. */
export function previewInstallationBuild(build: BuildSummary): BuildSummary {
  return { ...build, additionalLanguages: Object.fromEntries(['zh-CN', 'zh-TW', 'en', 'ko', 'id', 'vi']
    .filter(code => code !== build.nativeLanguage).map(code => [code, `preview.language.${code.toLowerCase()}`])) }
}

export function previewInstallationComponents(): ClientComponent[] {
  const names = [
    ['国服认证组件', 'Client authentication', '游玩此版本需要安装并启用此组件。', true],
    ['运行环境组件', 'Runtime component', '提供此版本启动时需要的运行环境。', true],
    ['字体粗细修正', 'Font weight correction', '调整游戏字体，使界面文字更清晰。', false],
    ['崩溃记录', 'Crash reports', '记录异常信息，方便排查游戏运行问题。', false],
    ['高清截图', 'High-resolution screenshots', '保存清晰的游戏截图。', false],
    ['窗口体验优化', 'Window improvements', '改善窗口切换和显示体验。', false],
    ['界面缩放', 'Interface scaling', '为不同分辨率提供更合适的界面尺寸。', false],
    ['音量控制', 'Volume controls', '分别调整背景音乐与音效音量。', false],
    ['输入辅助', 'Input accessibility', '提供额外的输入辅助设置。', false],
    ['按键提示', 'Key hints', '显示当前操作对应的按键提示。', false],
    ['游戏统计', 'Game statistics', '记录本地游戏时间与对局统计。', false],
    ['色彩辅助', 'Color accessibility', '提供更容易区分的界面颜色。', false],
    ['截图管理', 'Screenshot library', '集中浏览和整理游戏截图。', false],
    ['画面预设', 'Display presets', '保存并切换常用画面设置。', false],
  ] as const
  return names.map(([name, english, description, required], index) => ({ id: `preview.component.${index}`,
    name: { 'zh-CN': name, en: english }, description: { 'zh-CN': description, en: `Installation preview: ${english}.` }, required }))
}

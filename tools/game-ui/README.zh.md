# Overlay 游戏 UI 源码

这里保存启动器可选游戏 UI 渲染器的源码。在 `web/` 执行 `npm ci` 后，运行
`npm run build:game-ui`，即可类型检查并重新生成
`src/FantasyTale.Launcher.Overlay/overlay/ui-library/` 下两个已提交的构建文件。
两套界面共用现有 Vue、Vite 依赖；库不打包 Vue，运行时使用 Overlay 的 import map。

在仓库根目录执行 `node --test tools/game-ui/launcher-presentation.test.mjs`，
可独立检查字体、Canvas、日期边界和草稿状态。在 `web/` 执行 `npm test` 也会运行
完整的游戏 UI 合成测试。未设置 `FT_GAME_UI_RESOURCES` 时，跳过三个私有资源集成
测试组；设置该变量即启用这些检查，指定的外部目录必须存在并包含所需资源，否则测试失败。

组件接口见 `README.vue.md`。游戏模块在启动渲染器之前决定界面版本。泡泡战士
2017-02-07 及以后的客户端使用 v3，包含当天；更早客户端保留所声明的 v1/v2。
Provider 收到客户端日期时也执行同样的边界判断。`BfExtensionSettings` 在三个主题
间共用控件树和状态，只发送 change、save、cancel 事件，不实现游戏功能。

启动器保留此前已认可的 v1/v2 文字效果。空字体名的 UiLabel、扩展设置正文和文字
选项卡使用 BFMalgun（DF_GB_Y9.TTF）；明确指定 Default、DefaultBold、NexonBTBold
的标签以及窗口、分组标题使用 BF（GB13000.ttf）。v1 确认、取消文字保留
BFMalgun 和 13 逻辑像素。v3 使用自己的原生字体声明。所有字体均保留 400 字重、
四倍 Canvas 缓冲和明确的逻辑输出尺寸，不合成粗体。这些显示选择有意保留启动器
原貌，与源实验库的新默认字体继承规则不同。

库不包含游戏图片、字体或音效。本地资源目录需要提供 `fonts/`、所用主题的
`v1/`、`v2/` 或 `v3/` 清单和精灵图片；开启音效时还需要 `sounds/`。
这些资源不进入 Git，也不会自动加入发布包。音效保留原事件增益，Provider 默认
音量为 0.75，浏览器首次播放需要用户手势。Provider 在切换主题时复用播放器，
卸载时释放它。

当前库只覆盖部分原生控件。浏览器字形栅格仍存在近似；教学对话的可选预制字形图集
使用独立绘制路径，资源契约见 `TUTORIAL.zh.md`。

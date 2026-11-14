# 游戏界面素材

[English](README.md)

仅含数据的插件包（`kind = "assets"`），包含共享游戏界面组件库绘制时使用的游戏原版界面图片、字体和编辑器外观表。
在浮层中绘制原版风格窗口的插件包（例如禁选）声明 `requires = ["bubblefighter.fantasy.game-ui-resources"]`，
并把它作为 `BfUiProvider` 的资源根目录：

```js
const root = overlayBus.packageUrl('bubblefighter.fantasy.game-ui-resources', 'ui.json').replace(/ui\.json$/, '');
```

## 数据目录不在仓库中

这些图片和字体来自游戏客户端，绝不能提交（`store-plugins/**/data/` 已被忽略）。`tools/game-ui/pack-resources.py`
把准备好的资源根目录转换为插件包数据：主题图片转为无损 WebP（逐像素校验，主题 `manifest.json` 和 `ui.json`
记录 `imageFormat: "webp"`），字体转为 WOFF2，并由 `fonts/fonts.json` 记录原文件名到存储文件名的对应关系。

```text
ui.json                      各主题的编辑器外观
v1/、v2/、v3/                主题 manifest.json 和图片（v3：2017 年及以后的客户端，例如 kr-1256）
fonts/                       WOFF2 字体和 fonts.json
sounds/                      界面音效及其 manifest.json
```

## 打包时填充

```sh
python -X utf8 tools/game-ui/pack-resources.py <准备好的资源根目录> <生成目录>
python tools/publish-store/stage_assets_package.py \
  --package store-plugins/BubbleFighter/bubblefighter.fantasy.game-ui-resources \
  --pack <生成目录> --output <新的暂存目录>
```

暂存脚本会拒绝链接和意外条目，检查每个主题清单，并确认 `fonts.json` 指向的文件存在。

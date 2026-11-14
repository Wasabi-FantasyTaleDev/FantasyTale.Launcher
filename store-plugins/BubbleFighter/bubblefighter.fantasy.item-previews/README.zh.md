# 物品预览图

[English](README.md)

纯数据包（`kind = "assets"`），保存预先生成的物品预览图。其他插件包声明
`requires = ["bubblefighter.fantasy.item-previews"]`，再通过 Overlay 的插件包文件主机读取：

```js
overlayBus.packageUrl('bubblefighter.fantasy.item-previews', 'pack.json')
```

本包自身不改变游戏。参见[共享数据包](../../../docs/plugin-management.zh.md#共享数据包与依赖)和
[插件包文件](../../../docs/hook/overlay.zh.md#插件包文件)。

## 数据文件夹不在仓库中

这些图片属于游戏美术资源，由服务端仓库的物品查看工具生成，绝不能提交（`store-plugins/**/data/` 已被忽略）。
生成的数据目录结构如下，它将成为本包的 `data/` 文件夹：

```text
pack.json                    数据索引
atlas/<hash>.webp            图集，以小写十六进制内容哈希命名
builds/<build id>.json       各版本的物品表
```

## 打包时填充

```sh
python tools/publish-store/stage_assets_package.py \
  --package store-plugins/BubbleFighter/bubblefighter.fantasy.item-previews \
  --pack <生成的数据目录> \
  --output <新的暂存文件夹>
python tools/publish-store/publish_plugins.py --package <暂存文件夹> ...
```

脚本把清单复制到暂存文件夹，把数据目录复制为 `data/`，拒绝链接和多余条目，并检查 `pack.json` 与
`builds/*.json` 中提到的每个文件都存在；未被引用的图集只给出警告。发布工具会拒绝发布 `data/` 为空或缺失的本包。

# 教学对话：还原依据、接入与文案生成

本文对应 `tools/game-ui` 的 `BfTutorialDialogue`。原生证据来自 cn-279（用户提供的 cn2011 客户端），浏览器运行只需要 Vue、Canvas、Web Audio 和静态资源。Python 只用于离线准备资源。

## 当前预览

在本目录执行：

```sh
npm run dev
```

打开 `http://127.0.0.1:8766/`，View 选择“教学对话（cn2011）”。默认文案为“Fantasy 服务器配置引导”，也可以切换回“原版 cn2011 教学”。

- 配置引导包含五步；每步对应一个原尺寸气泡。
- 首次音频被浏览器拦截时，显示“点击开始教学（播放声音）”；在音频解锁和两个声音采样加载完成前不启动文字计时。
- 点一下补全当前句，再点一下请求下一句。
- 演示默认打开“自动下一句”和“循环播放”，关闭自动播放后可以逐句检查。
- 自动推进的停留计时从打字完成后开始。原版教学路径进入移动、射击等任务时，本来不会继续显示下一句；循环演示会跳过这些游戏任务节点，继续展示对话。
- 演示背景只是便于检查透明素材的底色，没有重建教学关卡的 3D 场景。

## 已查证的原生参数

所有坐标均为 1024×768 逻辑画布中的坐标，不能直接用视频截图像素当逻辑坐标。

| 项目 | cn-279 证据 / 数值 |
| --- | --- |
| 入口与对象 | `sub_857740` 创建 TutorialMgr；`sub_8AAE10` 创建 NarratorObject |
| 对话配置 | `sub_784BC0`；由 TutorialMgr 读取当前 progress 的对话属性 |
| 绘制及逐字计时 | `sub_785010` |
| 字体 | `DefaultBold`；`Config/Fonts_cn.csv` 映射到 `Data/GB13000.ttf` |
| 字号、字重 | 15、400；字体别名中的 Bold 不代表额外使用 700 |
| 字形加载标志 | `sub_53F870`：40968，即 `0xA008` |
| 文字颜色 | `0xFF222222` |
| 装饰模式 | 1；记录的装饰色 `0xFF666666`；该模式不进入轮廓扩张的两遍绘制分支 |
| 文字矩形 | `(727,400,250,100)` |
| 对齐 | format 4：靠左、文字纹理整体垂直居中 |
| 自动换行 | 原生调用不含 `0x10`；原版文案使用翻译表中的显式换行 |
| 气泡中心 | `(847,474)`；原图 328×192，左上角 `(683,378)` |
| 人物中心 | `(884,646)`；原图 272×243，左上角 `(748,524.5)` |
| 逐字间隔 | `Config/InGameUI.xml` 的 `textPrintSpeed=70` 毫秒；代码默认值 50 被配置覆盖 |
| 点击行为 | `sub_7849D0`、`sub_784950`、`sub_785010`：先补全文字，再推进 |

`sub_53FDF0`、`sub_540180` 测量并组合每行纹理；`sub_540FF0` 根据整块纹理高度执行垂直居中。Web 逐字时也重新计算当前可见文字的高度，所以换行出现时，文字块会随原生规则重新居中。

原版中文来自 `StringTable/TranslatedXmlTable.csv`，键形如 `TutorialStage-Stage1-[progress]-dialogueString / 21`。截图中的句子为：

```text
在正式进入游戏之前，先要
接受训练。
```

## 素材与音效

| 内容 | 原始位置 | Web 包内位置 |
| --- | --- | --- |
| 黑妞 | `Pack/Images0007.pkn` → `Images/TutorialStage/Dizni.png` | `Images/TutorialStage/Dizni.png` |
| 备用角色 | 同归档 → `Images/TutorialStage/Dao.png` | `Images/TutorialStage/Dao.png` |
| 气泡 | 同归档 → `Images/TutorialStage/ChatBalloonNew.png` | `Images/TutorialStage/ChatBalloonNew.png` |
| 字体 | `Data/GB13000.ttf` | Provider 的 `fonts/GB13000.ttf`；字形图集另见下文 |
| 逐字声 | `Sounds/ui.fsb`，从 0 开始的子声音索引 9 | `Sounds/ui-tut-type.wav` |
| 气泡出现声 | `Sounds/ui.fsb`，子声音索引 14 | `Sounds/ui-tut-textbox.wav` |

注意文件实际拼写为 `ChatBalloonNew.png`，不是 `ChatBallonNew.png`。鼠标与“跳过”字样已经包含在原气泡 PNG 中。

`Sounds/SoundList.xml` 将 `ui-tut-type`、`ui-tut-textbox` 映射到 tutorial 的 type、textbox 事件。cn-279 `sub_512040` 给声音加载器传入 FSB 密钥；准备脚本从对应客户端的可执行文件读取它。

两个声音均为 44100 Hz、单声道、16 位 PCM WAV。逐字声为 2240 个采样，约 50.79 毫秒；气泡出现声为 8512 个采样，约 193.02 毫秒。Python 解码输出已经与原客户端声音 DLL 的解码缓冲区逐字节比对一致。原事件查询得到音量 1、音高偏移 0。没有合成替代音效。

逐字路径每个 UTF-16 单位触发一次原声，包括标点与换行。延迟帧只推进一个单位，不补播积压的一串声音。Web 同时保留的声音源最多为 8 个，组件销毁时停止并释放声音与动画帧。

## 字体图集的实现与边界

使用原版 TTF、字号 15、字重 400、原生加载标志，离线生成灰度字形覆盖率与以下数据：

- `glyphs.png`：单个字符的位图，不是整段文案截图。
- `glyphs.json`：字符裁剪矩形、bearing、整数 advance、行高度和生成版本。
- 不合成粗体、不额外描边、不拉窄文字。

运行时按文字逐个组合字形，依据原生基线 `字号−1` 放置，并按每行度量居中。未收录的字符会用已加载的原字体 TTF 经 Canvas 兜底，因此不会仅因缺少图集条目而消失，但边缘和度量可能不同。固定场景应重新生成图集，保证全部字符都走同一条路径。

原版包当前包含 50 条中文对话、368 个字符。配置引导的独立包包含五条对话、236 个字符。生成器会验证字符覆盖、字宽和文字高度；原字体本身不支持的字符会报错。

当前离线字形光栅化运行库版本为 2.13.2；不能据此声称它与 2011 可执行文件中链接的旧版本逐像素一致。字体、加载标志、字号、字重、位置和 advance 都依据客户端处理，截图视频压缩与宿主最终缩放仍会影响视觉结果。

## Vue 接入

组件从 `src/index.ts` 导出。宿主依赖 Vue，并引入构建产物的样式。

```vue
<script setup lang="ts">
import { ref } from 'vue';
import { BfUiProvider, BfTutorialDialogue, type BfTutorialPage } from '@bubble-fighter/game-ui-vue';
import '@bubble-fighter/game-ui-vue/style.css';

const progress = ref(1);
const narrator = ref<InstanceType<typeof BfTutorialDialogue>>();
function advance(next: BfTutorialPage | undefined) {
  if (next) progress.value = next.progress;
  // A real setup host may validate its form before changing progress here.
}
</script>

<template>
  <BfUiProvider theme="v1" resource-root="/game-ui/">
    <button @click="narrator?.start()">开始引导</button>
    <BfTutorialDialogue
      ref="narrator"
      stage="FantasySetup"
      :progress="progress"
      resource-root="/game-ui/tutorial/fantasy-setup/"
      :sound="true"
      :autoplay="false"
      :loop="false"
      @advance="advance"
    />
  </BfUiProvider>
</template>
```

`BfUiProvider.resourceRoot` 提供普通 UI 的主题资源与字体；`BfTutorialDialogue.resourceRoot` 指向独立的教学资源包，两者不是同一个目录。库构建不会把 `local/` 的客户端资源自动打入 JS；宿主需要把资源复制到自己的静态资源目录。

教学资源包应包含：

```text
manifest.json
glyphs.json
glyphs.png
Images/TutorialStage/ChatBalloonNew.png
Images/TutorialStage/Dizni.png
Images/TutorialStage/Dao.png
Sounds/ui-tut-type.wav
Sounds/ui-tut-textbox.wav
```

| Prop | 默认值 | 作用 |
| --- | --- | --- |
| `stage` | `Stage1` | 资源包中的对话组 |
| `progress` | `21` | 当前对话 ID，由宿主控制 |
| `resourceRoot` | `/local/tutorial/cn-279/` | 教学资源包 URL 前缀 |
| `text` | 未设置 | 可选替换当前页文字；固定文案推荐生成资源包 |
| `character` | `Dizni` | 支持 `Dizni`、`Dao` |
| `sound` | `true` | 原版音效；开启时会等待音频就绪再出字 |
| `autoplay` | `false` | 字打完后按当前页 `holdMs` 请求下一页 |
| `loop` | `false` | 对话演示连续播放，尾页回到首条；跳过缺少对话的任务节点 |

- `ready(pages)`：资源加载完成，返回页列表。
- `complete()`：当前句打字结束或被点击补全，不表示服务器配置完成。
- `advance(nextPage | undefined)`：请求换页；宿主须更新 `progress`。关闭循环时，原版流程进入任务节点或到达结尾可能返回 `undefined`。
- `error(error)`：资源或音频错误。
- 暴露 `start()`、`restart()`；声音首次启动应从用户点击处理器调用 `start()`。

组件本身是透明的 1024×768 图层，可盖在游戏或启动器场景上。根据宿主区域统一计算 `scale=min(可用宽度/1024, 可用高度/768)`，对整个图层缩放；不要独立更改人物、气泡或字体的横纵比例。`src/demo/TutorialPreview.vue` 展示了预览缩放和文案切换。

真实配置向导通常应关闭 `autoplay` 与 `loop`，在宿主表单通过验证后才进入下一个配置步骤。`sourceProgress` 表示文案原始步骤，`part/parts` 表示该步骤是否被长文案分页；这次精简后的配置引导为一页一步。

## 生成新的固定文案

准备阶段需要 Python、Pillow 和字形光栅化绑定。Windows 建议使用 x64 Python，以便安装包含对应本地库的包；当前机器默认 Python 是 ARM64，已验证使用随工作区运行环境提供的 x64 Python。浏览器端不依赖 Python。

```sh
python -m pip install Pillow freetype-py==2.5.1
```

`--python-dependencies` 可以指向通过 `pip --target` 安装的依赖目录。现有机器已准备在仓库的 `artifacts/game-ui/tutorial-python-x64`。

UTF-8 JSON 是推荐文案格式：

```json
[
  { "progress": 1, "text": "你好！\n我们开始配置吧。", "holdMs": 4500 },
  { "progress": 2, "text": "先给服务器起个名字吧？", "holdMs": -1 }
]
```

`holdMs=-1` 表示不自动进入下一页。也支持字符串数组或使用空行分隔段落的 UTF-8 TXT。显式 `\n` 保留换行。`clickComplete` 默认开启。

在仓库根目录执行：

```sh
python tools/game-ui/generate-tutorial-text.py --input <dialogue-input.json> --output tools/game-ui/local/tutorial/fantasy-setup --stage FantasySetup --paginate
```

- 从原版包复制气泡、角色与原声音效；只替换对话和图集。
- 输出独立资源包，禁止直接覆盖原版 `cn-279` 包。
- `--paginate` 只用于自定义文案：按实际 advance 换行，尽量保留英文词组与标点，不拆开能完整放入一页的原始段落；超过原文字区域容量时分页。
- 不加 `--paginate` 时，原文宽高超过 250×100 会报错，让作者显式调整换行。
- 不会缩小字体或拉伸气泡来容纳长文案。
- 输出 `text-report.json` 记录每页文字高度及 UTF-16 单位数。
- 校验通过前不会替换现有字形图集；生成临时文件放在输出目录所在磁盘，避免占用系统临时目录。
- 只换文字、不新增字符时，可复用图集；出现新字或标点时重新运行生成器。
- 请提供本地对话 JSON 文件；私人示例文案不包含在源码发行中。

示例命令（请提供自己的 UTF-8 对话输入及本地准备的资源）：

```powershell
python tools/game-ui/generate-tutorial-text.py --input <dialogue-input.json> --output tools/game-ui/local/tutorial/fantasy-setup --stage FantasySetup --paginate --python-dependencies <python-dependencies>
```

## 从客户端重新准备原版包

准备脚本依赖本项目的归档工具，可一次提取图片、配置、官方中文翻译，解码原声，并生成原版对话的字形图集：

```sh
python tools/game-ui/prepare-tutorial.py --client-root <cn-279-client-root>
```

此脚本针对 cn-279 已查证的归档和地址；不要直接套在其他版本可执行文件上。输出位于 `tools/game-ui/local/tutorial/cn-279/`，中间资源及归档来源位于 `artifacts/game-ui/tutorial-v1/`。

## 验证与交付范围

```sh
cd web
npm run build:game-ui
cd ..
node --test tools/game-ui/*.test.mjs
```

在 `web/` 运行 `npm test` 可同时执行全部前端与游戏 UI 测试。逐字计时、延迟帧不追赶补播、点击、页面跳转和音频生命周期使用合成测试数据，无需游戏文件也始终执行。

字形覆盖、配置引导分页及原始 PCM 校验是独立的集成测试，仅在 `tools/game-ui/local/` 缺少对应私有资源组时跳过。将 `FT_GAME_UI_RESOURCES` 设为本地准备的资源根目录后，三个资源组（`tutorial/cn-279/`、`tutorial/fantasy-setup/`、`sounds/`）都必须存在且有效，否则测试失败。跳过集成测试不代表已通过原版客户端验收，也不代表具有素材再分发许可。

此交付是可复用的对话显示组件和示例文案，尚未把服务端命名、客户端导入、数据库安装或管理员创建功能接入真实表单。教程的其他图片动作、关卡任务、相机路径及所有运行时分支也不在当前组件范围内。

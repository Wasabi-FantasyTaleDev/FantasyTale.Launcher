# 模式扩展

[English](README.md)

游戏模块的托管控制器通过 native UI bridge，在指定的创建房间 `.ui` 中添加原生“模式扩展”按钮。
弹窗按当前客户端选择 v1、v2 或 v3 窗口，使用每行三个按钮的纵向滚动列表、现代版本
模式贴图和 FreeType 文字。标题与简介分别使用原生标签区域；当前语言缺少原生简介时，
显示该语言的“隐藏模式扩展”。

打开弹窗时读取当前原生选择；房间标题中已识别的扩展标签优先。点击模式只改变草稿，
点击底部“确认”才应用。取消或关闭原生创建房间窗口会关闭弹窗。若当前客户端提供
对应且可用的原生模式按钮，控制器直接激活该按钮；其他配置允许的模式通过房间标题
最前面的 `#去掉空白字符的本地化模式名称#` 表示。成功应用后，创建房间窗口标题追加
本地化的已选择模式提示。

控制器负责游戏会话中的原生表单发现、入口创建、模式可用性、房间标题标签、修改与
恢复。Overlay 浏览器隐藏或挂起时，这些工作仍继续运行。前端只显示后端状态、记录
临时选择并发送用户操作，不读取原生控件或轮询游戏。`controller.json` 配置有界的
观察与操作时限、待处理操作数量，以及已选择模式的标题提示。

只有组件所属的 Nirinsou Link 通道已连接、创建房间上下文处于启用状态，而且当前场景
为 `plaza` 时，控制器才开始观察原生 UI。即使服务端允许在 `main_menu` 创建房间，
该场景下仍保持休眠。离开广场、连接断开或游戏登录上下文更换，会立即撤销观察权限，
并使当前原生操作代次失效。缺少通道、原生绑定、生命周期接口或协商权限时也保持休眠。

组件依赖 `bubble.fighter.fantasy.nirinsoulink`，并声明
`game.room.create.context`、`game.room.create.context.get`、
`game.session.changed` 和 `game.session.state` 四项 v1 消息权限。控制器先订阅两类
事件，再以空对象 `{}` 请求两个快照；每次连接和上下文更换时同步，不定时发起
网络请求。登录更换后丢弃旧场景证据，并等待新的场景同步完成。若同步失败，后续有效
的广场事件可以触发一次合并后的重新同步；事件本身不能恢复权限。事件和回复携带连接
代次与接收顺序，因此晚分发的旧事件不能覆盖更新的快照。较低上下文版本、重复状态和
已失效连接的回复都不能恢复观察权限。

排队中的原生读取保留原操作标识与用途，不再由修改操作的时限取消共享管道。确认时
始终在用户操作之后重新读取原生状态。已受理的修改在完整管道交换之间检查结果等待
时限，结果不明确时不重放。清理入口遇到暂时繁忙会保留其所有权，完成清理后才允许
创建新入口。

原生表单绑定覆盖 `kr-812`、`kr-1256`、`cn-279`、`cn-324`、`cn-404`、`cn-406`、`cn-408`、`cn-706`、`cn-717`、`cn-735`、`cn-774`、`id-400`、`id-1809` 与 `tw-454`。其他版本需要相应的 native UI bridge 适配与
`room-bindings.json` 条目。47 项名称目录与版本支持验证是独立的数据。目录包含
原生创建房间界面已有的模式，默认可用性策略显示全部目录项。

## 按版本配置可用模式

`mode-catalog.json` 保存完整的模式 ID、多语言名称和服务端标签。
`mode-availability.json` 决定列表显示哪些模式。控制器同时接收完整目录，以便正确
识别并替换标题中原本存在、但已被当前策略隐藏的模式标签。

随组件提供的默认配置为：

```json
{"format":1,"rules":[]}
```

以下仅演示配置格式，不代表已提供或已验证的版本支持矩阵：

```json
{
  "format": 1,
  "rules": [
    {"builds": ["kr-*"], "hidden": [8]},
    {"builds": ["kr-812"], "allow": [1, 3, 12]}
  ]
}
```

| 字段 | 含义 |
| --- | --- |
| `format` | 必填格式版本，目前为 `1`。 |
| `rules` | 按顺序处理的显式限制规则。空数组保留完整目录。 |
| `builds` | 非空的客户端版本 ID 或含 `*` 通配符的列表，不区分大小写，匹配整个 ID。 |
| `allow` | 可选，保留的模式 ID；多条匹配规则的允许集合取交集。空数组不保留任何模式。 |
| `hidden` | 可选，移除的模式 ID；匹配的隐藏规则优先于允许规则。 |

每条规则至少包含 `allow` 或 `hidden`。ID 必须为完整目录中存在且不重复的整数。
配置错误会在打开列表前明确失败。没有匹配限制规则的版本保留完整目录。
`mode-availability.schema.json` 提供格式定义，运行时还会核对 ID 是否属于实际目录。
应根据部署环境中已验证的客户端和服务端行为填写规则。

独立的 `room-bindings.json` 指定准确的 `.ui`、房间名输入框、原生模式按钮组与新增
入口的布局。native UI bridge 在运行时返回按钮的可用状态和模式选择值。对话框可能
从多份资源加载的版本，为每份资源各写一条绑定，入口跟随当前打开的那一份。修改原生
绑定或适配代码需要对应客户端的证据。

## 显示状态协议

模块先订阅 `mode-extension.state`，再发送 `mode-extension.ready`。后端通过该状态
事件返回当前快照，并在状态改变后继续推送：

```json
{
  "revision": 1,
  "sessionId": "ed550ef0b0f2449489ea4b5b3e574522",
  "open": true,
  "busy": false,
  "selectedId": 1,
  "modes": [{"id": 1, "key": "Pvp", "names": {"en": "No Item"}}],
  "error": null
}
```

`revision` 在面板生命周期内递增。打开弹窗必须具有非空会话 ID；已关闭或只显示错误
的状态可以使用 `null`。`selectedId` 也可以为 `null`。错误格式是
`{status, reason, recovery?}`；`recovery: "restart"` 表示需要重启游戏，没有恢复提示时
可以省略该字段或设为 `null`。

`modes` 是后端确定的完整显示列表。前端仅补充本地化简介，并读取贴图、窗口资源与
字体，不读取可用性策略、原生绑定或完整标签目录。弹窗打开或会话变化时，草稿以
`selectedId` 为初始选择；忙碌状态更新保留草稿，不重复绘制未变化的模式列表。

| 用户操作 | 事件 | 数据 |
| --- | --- | --- |
| 确认草稿 | `mode-extension.confirm` | `{sessionId, modeId}` |
| 取消弹窗 | `mode-extension.cancel` | `{sessionId}` |
| 关闭错误提示 | `mode-extension.dismiss` | `{sessionId}` |

操作发出后，前端等待后端的新状态来关闭弹窗或清除错误。旧版本状态和已更换会话的
回调会被忽略。组件卸载时取消状态订阅并隐藏面板。

## 私有素材与打包

原始客户端素材作为私有分发输入保管，生成的图片、字体和资源清单不要提交到源码仓库。
源码组件包含代码、策略与本地化数据；私有 staging 过程添加 `mode-skins.json` 和
其引用的 `modes/` 图片。本组件依赖单独打包的
`bubblefighter.fantasy.game-ui-resources` 以及 native UI bridge。

使用 `tools/game-ui/mode-extension-demo/prepare.py`、维护中的 ItemViewer 源码和
已准备好的 v1/v2/v3 资源生成模式素材。在 Launcher 根目录执行：

```powershell
python tools/game-ui/mode-extension-demo/stage.py --package store-plugins/BubbleFighter/bubblefighter.fantasy.mode-extension --resources artifacts/mode-extension-resources --output artifacts/mode-extension-package
```

输出目录必须是新目录或空目录。工具在创建输出前检查全部引用图片，拒绝链接及越界
路径，去除私有原始档案位置，仅复制模式按钮素材。公共窗口与字体继续由已有资源包
提供。Staging 仅生成待检查的组件包，不会发布或启用组件。

## 预览与针对性验证

Vite 预览配置位于 `tools/game-ui/mode-extension-demo/vite.config.mjs`。
`BF_UI_RESOURCE_ROOT` 指向私有资源目录；`LAUNCHER_WEB_ROOT` 可指向已有且兼容的
前端依赖目录。预览地址为 `http://127.0.0.1:4191/`，参数支持 `theme=v1|v2|v3`、
`locale`，以及用于版本策略的可选 `build`。切换主题会切换窗口外观；v1/v2 的僵尸
模式使用幽灵僵尸贴图，v3 使用后期僵尸贴图，名称、简介和模式 ID 保持对应原模式。

运行 `node --test tools/game-ui/mode-extension.test.mjs` 检查草稿与确认流程、鼠标捕获
拖拽、主题贴图和版本策略。运行
`node --test web/tests/overlay-panels.test.mjs web/tests/mode-extension-panel.test.mjs`
检查实际前端模块的显示协议与订阅生命周期。托管控制器测试位于
`tests/FantasyTale.Launcher.Core.Tests/NativeRoomModeControllerTests.cs`。
同目录的 `ModeExtensionDataTests.cs` 检查组件数据校验、可用性策略和多语言标题标签。
`RoomCreateObservationGateTests.cs` 检查广场授权、登录上下文更换、重连及过期回复。
各客户端版本及 native UI bridge 仍需相应的真实客户端验收。

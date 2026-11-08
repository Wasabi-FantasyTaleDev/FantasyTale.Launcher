# 可选的游戏内 Overlay

Launcher 可以在 32 位游戏旁边运行一个可选的 x64 Overlay 进程。Overlay
会跟随游戏客户区，并提供相互隔离的 HTML 面板。窗口跟随游戏层级，切换到其他
应用时仍保留在游戏上方，其他前台窗口可以正常遮住游戏和 Overlay；游戏最小化
时 Overlay 一起隐藏。
当前客户端安装并启用了 `kind = "overlay"` 的插件，或开启本机开发者启动测试时会启动它。

Overlay 选择最大的可见 `CS_OWNDC` 游戏图形窗口，排除独立的 Splash 加载窗口。
保留启动时传入的进程标识，初始窗口句柄失效时仍可继续跟随。
定位按 Overlay 自身窗口 DPI 将客户区物理像素换算为 WPF 坐标，窗口事件之外，可见时每 100 毫秒、
空闲时每 1000 毫秒检查一次跟随状态，并在 Overlay 关闭时停止。正式图形窗口出现前隐藏界面内容，
游戏在前台时提升 Overlay；切换到其他应用时将它放回游戏上方、其他前台窗口下方，
不反复提升普通窗口的层级。最小化时同时隐藏 WPF 表面和浏览器宿主，保留面板状态供还原使用。
初次设置鼠标穿透不会抢占焦点。

所有附着到游戏的 Overlay 都保留 `WS_EX_NOACTIVATE`，`WM_MOUSEACTIVATE`
返回 `MA_NOACTIVATE`，且不主动激活自身。窗口、无边框和全屏模式下点击鼠标控件
都保持游戏的前台焦点，避免失焦导致显示模式重置。独立预览仍可正常独立交互。
在不切走游戏焦点的前提下向 Overlay 文本框输入，仍需要独立的键盘输入路径。

消息框和对话框收到按钮动作或关闭事件后默认隐藏，包括通过 `game-ui.mount`
直接挂载的组件。所属插件仍会收到动作事件，隐藏后释放该面板占用的鼠标区域。

WebView2 控制器延后到正式图形窗口可见、WPF 完成客户区布局后才创建。
加载画面期间可以连接管道并排队面板命令，但不会在隐藏的加载窗口上初始化浏览器。
页面安装 JavaScript 消息监听器并发出就绪消息后，才开始发送排队的命令。

组件挂载就绪还会等待主题图片和字体异步加载后生成实际渲染表面。空的 Vue 挂载容器
不再产生成功的 `game-ui.mounted` 事件；资源加载失败或超时会产生 `game-ui.error`。

## 本机开发者测试

在“开发者 → 开发者选项”中开启“UI Overlay 测试”，下次启动游戏会显示
`Overlay Test`。测试默认关闭，不要求安装 Overlay 插件。“框外点击穿透”在这个
测试中默认开启。两项设置只保存到本机数据目录的 `overlay-test.toml`，下次启动
游戏生效。隐藏开发者页面不会改变已保存的测试设置。

游戏风格组件仍需可用的资源提供者。本机开发者可以将 `resource_root` 指向包含
`fonts/`、`v1/` 和 `v2/` 的现有绝对目录，设置页面会保留这个可选配置。
游戏资源不会随源码提交，测试开关本身也不提供这些资源。

窗口类别由 cn-324 sub_87D8B0（图形窗口 style 40）和 sub_795920（加载窗口
style 8）确认。这用于识别图形窗口的切换，不代表某个具体场景或全部资源已经加载完成。

Overlay 插件是基础服务层，不会进入 x86 Hook Runtime，不会修改游戏文件，
也不会成为普通客户端启动的前置条件。插件可以提供受限的内联文档或一个
包内相对路径的 HTML 文件。网络 URL、绝对路径和父目录穿越路径都会被拒绝。

Launcher 与 Overlay 使用每次启动独立的命名管道和版本 1 协议。Launcher
创建管道、启动 Overlay、校验连接进程身份并验证绑定本次启动的握手。消息由
四字节小端长度和 UTF-8 JSON 组成，单帧上限为 256 KiB。命令包括
`register`、`unregister`、`show`、`hide` 和 `send`；页面事件以
`panel-event` 返回。未知命令、格式错误、超时和断线只会使可选 Overlay 会话
失败，不会阻止游戏启动。

当前版本在 `FantasyTale.Launcher.Sdk` 中提供了契约。Native Hook 插件也可以
通过可选的、版本化的 x86 到 Launcher 桥接使用此服务。Runtime 会提供插件
自己的面板命名空间，并通过本次启动绑定的 broker 管道转发内联 HTML、显示
状态和发出的事件。Overlay 缺失或失败时返回 `not-found`，因此 Native 插件
仍必须在没有 Overlay 时正常工作。可选 ABI 尾部的 `poll_event` 在工作线程读取所属插件的
页面事件；使用前检查 `struct_size`。每个面板最多保留 32 条、每条 4096 UTF-8 字节的事件；
超量会显式产生 `overlay.events-overflow` / `overlay.event-too-large`。调用者准备 4097 字节
输出缓冲区。Native 文件面板仍需包资源代理，当前 Native 路径使用内联 HTML。

Native 插件分别连接同一个启动会话的管道名。Broker 最多接受 64 个连接，各自处理独立的
请求／响应帧，一个连接停顿或格式异常不会占住其他插件的流；断开后归还名额，允许后续插件
连接或重连。请求必须等游戏进程标识完成绑定后才能认证，避免启动早期的绑定竞态。关闭时
取消监听并关闭全部连接。版本 1 消息格式保持不变，也不会增加 WebView 页面数量。

## 单页面、事件驱动与空闲状态

游戏窗口出现后启动 renderer，正式图形窗口可用后准备浏览器；默认没有任何可见面板。
`IOverlayHost.IsReady` / `WaitUntilReadyAsync` 表示页面已安装消息监听器，管道握手与页面就绪
是不同状态。一个游戏会话始终使用同一 WebView 页面，不因显示多个面板而创建额外浏览器窗口。
启用的包入口只注册，不自动显示；插件通过事件调用 `Show`、`Hide` 或信息框接口唤醒内容。

所有面板隐藏后，隐藏原生表面并停止输入计时器，短暂空闲后尝试 `TrySuspendAsync`。
新命令先 `Resume` 再发送，页面和面板状态保持在内存中。等待完成的页面命令和活动计数
可避免正在处理事件时错误挂起。此机制不证明游戏完成了特定 3D 场景的加载。

## 游戏风格接口与自定义 HTML

默认使用封装接口，插件只提供内容、按钮和回调，不需要自行处理 CSS、素材或字体：

```csharp
await overlay.WaitUntilReadyAsync(cancellationToken);
using var confirmation = overlay.ShowConfirmation(
    "my-plugin.confirm", "提示", "是否继续？",
    onAction: args => HandleAction(args.Data.GetProperty("name").GetString()));
confirmation.SetBackdrop(true);
```

`ShowMessageBox`、`ShowConfirmation`、`ShowDialog`、`ShowGameUi` 和 `CreateGameUiPanel`
共用页面。信息框默认居中、模态，可通过标题栏拖动，操作、关闭或 Escape 后隐藏。
插件卸载或功能结束时释放 handle。`game-ui.action`、`.change`、`.close`、`.mounted` 和
`.error` 返回所属插件；托管回调使用独立有界队列，回调内可以继续发送命令。

Native 插件包含 `fthook/overlay_ui.h`，先调用 `fthook_create_game_ui`，再用
`fthook_show_message_box` 传入标题、文本、按钮 JSON，通过工作线程的
`fthook_poll_ui_event` 读取操作。本次 ABI 尾部扩展对应 Runtime 3.3.2。

高级插件仍可通过 `RegisterPanel` 提供自定义 HTML/CSS。带 `data-overlay-action="retry"`
的按钮产生 `html.action`；`Send("html.update", new { status = "..." })` 更新带
`data-overlay-text="status"` 的文字。插入 HTML 不执行内联 script；各面板使用 ShadowRoot
隔离样式。文字画布使用明确的逻辑尺寸，避免按钮容器拉伸字形。

默认文字当前仅实现简体中文；`locales.json` 为繁体中文、韩语、印尼语、越南语和英语
保留明确 TODO，补齐前回退到简体中文。

## 层级、输入、遮罩与分辨率

注册项包括 `Layer`（0..10000）、`Interactive`、`Modal`、`PassThroughOutside`、`Backdrop` 和 `BackdropColor`。
隐藏面板不接收输入。普通面板按边界接收输入；有模态面板时只有最上层模态面板接收，
关闭后恢复下层输入。`BringToFront` 在相同 layer 内提升面板；模态面板高于普通面板。
Tab 保持在最上层模态内。可选全客户区半透明遮罩只取最高的请求者，避免多层透明度叠加；
最终颜色待提供，当前默认颜色可配置。

模态层级与框外游戏输入可以分别配置。设置 `PassThroughOutside = true`、向游戏 UI
辅助方法传入 `passThroughOutside: true`，或调用 `panel.SetPassThroughOutside(true)`，
消息框内仍接收操作，框外点击直接交给游戏。最上层模态面板决定此策略，下层 Overlay
面板仍被阻挡。其他模态面板默认阻挡框外输入；关闭穿透后恢复全客户区输入捕获。
拖动期间始终保留鼠标捕获，直到释放，即使平时允许框外穿透也不会中断拖动。

客户区调整、图形 HWND 替换和 `WM_DPICHANGED` 会更新渲染表面。尚未拖动的信息框保持
居中，拖动过的信息框限制在新客户区内。拖动期间保留指针捕获；不修改系统 DPI。
跨显示器实机验收与确定性的坐标测试仍需区分。

`OverlaySoundCue` / `PlaySound` 预留打开对话框、按钮按下和通知音效，默认组件调用同一个
适配层。TODO：补充实际音效文件与映射；未配置时静音，不创建音频对象或播放计时器。

## 原生 UI 锚点与动作适配

`IOverlayAnchorProvider.Subscribe` 抽象 Hook、图像识别和 UI 资源定位来源。
`panel.FollowAnchor(provider, targetId)` 返回需释放的订阅；`SetAnchor` 与 Native
`fthook_set_ui_anchor` 使用同一契约。矩形使用游戏客户区像素，不是桌面坐标；
`ReferenceWidth` / `ReferenceHeight` 可声明设计视口，页面再转换到当前 CSS 像素。
支持覆盖、居中、左、右、上、下附着。源目标隐藏时面板隐藏，源坐标推送改变时一起移动；
附着面板不单独拖离源目标。传入 null 可解除锚点并恢复原布局。

TODO：经验证的各版本控件定位、图像截取/模板追踪、置信度与目标丢失策略、限定搜索区域
和频率。基础层当前没有持续图像扫描；未来扫描需排除自身 Overlay，并在游戏最小化时停止。

`IOverlayNativeUiBridge.InvokeAsync` 为命名目标预留 Press、Focus、SetValue、Close 语义动作。
HTML 事件先返回所属插件，由插件明确调用已批准的动作。TODO：各版本原生处理器与目标白名单。
基础层不发送游戏点击、任意地址调用或游戏数据包；Unsupported、NotReady 与 HTML 点击成功
是不同结果。

## 按版本选择界面与能力

每个版本的 `[overlay]` 声明主题、能力及定位目标。2012 年及以前使用 `v1`，之后使用 `v2`。
`v3` 只预留定义，素材和 renderer 补齐前记录不可用并跳过。`IOverlayHost.BuildContext` 和
`overlayBus.buildContext` 暴露当前版本配置；`overlayBus.hasCapability(name)` 查询能力。
一个插件可以保留共同逻辑，按数据选择模式列表、素材和控件位置。

```toml
[overlay]
theme = "v2"
capabilities = ["room.mode.extra"]

[[overlay.targets]]
id = "room.shell"
locator = "hook"

[[overlay.targets]]
id = "room.create"
locator = "ui-resource"
resource = "Ui/CreateRoom.ui"
control = "CreateRoom"
parent_target = "room.shell"
```

定位类型包括 `hook`、`image`、`ui-resource`。资源标识必须为相对路径；父目标必须存在，
禁止循环引用。声明定位目标不意味着已经启用对应的图像扫描或原生内存读取。

## 可选的 `.ui` 资源定位

构建定义可以声明 `locator = "ui-resource"`、相对 `.ui` 资源标识和控件名称。
`OverlayUiResourceParser` 支持受限的 XML/JSON 布局子集，
`OverlayUiResourceAnchors.Resolve` 会把嵌套的设计坐标转换为统一的
`OverlayAnchor`。资源声明的设计视口会保留为 `ReferenceWidth`/`ReferenceHeight`，
页面随后沿用现有的分辨率和 DPI 缩放路径。

这只是基础契约，不代表已经理解每个游戏的私有二进制 `.ui` 格式。对应版本的适配器
需要自行解码私有格式，并通过 `IOverlayAnchorProvider` 发布相同的矩形；资源缺失、格式
不支持或解析失败都必须显式报告。静态 `.ui` 坐标无法感知游戏加载后用户拖动原生控件，
因此控件会动态移动时仍应由 Hook 或受限的图像定位器提供运行时坐标。

`IOverlayUiResourceDecoder` 是真实私有格式的公开适配接口；XML／JSON 解析器只处理适配后
的标准化布局。`OverlayUiResourceAnchorProvider` 可直接接入 `FollowAnchor`，布局重载时
调用 `Update` 推送变化，控件隐藏或移除时附着面板一起隐藏。资源上限为 1 MiB、4096 个命名
控件，并限制嵌套深度；每个 provider 最多 128 个订阅。重复控件名、DTD、非有限数值、
不完整矩形或视口都会被拒绝。原始二进制 `.ui` 尚未实现解码器。

# 可选的伴随通信通道

伴随通信通道面向玩家的名称是 **Fantasy Tale Nirinsou Link**（简称 Nirinsou）；代码、wire 和配置仍使用
“companion”。它是正在实现的可选基础层，独立于原游戏协议、Hook ABI 和 Overlay
渲染器。没有它游戏仍可运行；使用它的插件必须明确处理 `Unavailable`、`NotBound`、
`Busy`、`Reconnecting` 和 `OutcomeUnknown`。

首版采用 WebSocket 长连接和 UTF-8 JSON v1：使用 `wss`，只有服务器的启动器 API 本身是明文 http 时才允许 `ws`，
因此一次性票据不会比换取它的 Bearer 令牌受到更少的保护。服务端使用
`fantasytale.companion.v1` 子协议，Launcher 使用 `ClientWebSocket`。一个 WebSocket
消息就是一个完整 envelope，基础层不再增加自定义长度前缀。gRPC、SignalR 和 protobuf
暂不加入，除非真实吞吐或兼容性证明需要新的 wire major。

公开 SDK 只提供窄的 `ICompanionChannel`，不暴露 socket 或 JSON 客户端：

```text
State、Capabilities
SendAsync(type、version、payload)
RequestAsync(type、version、payload、timeout)
Subscribe(type、version、handler) -> IDisposable
```

每个注册都绑定插件 owner 和一次启动，并通过现有插件 effect 生命周期释放。只有一个
通信基础组件持有连接，插件不能各自开 socket。Overlay 使用结构化通知 DTO，再映射到
受信的游戏风格信息框或自定义面板。服务端消息不能携带任意 HTML、JavaScript、组件源码、
进程地址、数据包或命令。

服务端需要在启动流程签发独立的一次性 companion 连接票据，只通过 WebSocket 升级请求的
Authorization header 传入，绝不放进 URL、subprotocol、日志、页面或客户端文件。票据绑定
Launcher 授权会话、服务器、build、能力和有效期。原游戏票据兑换且游戏登录完成前，启动
保持 `PendingGame`；不能用用户名、IP 或 PID 猜测绑定。注销、撤销、过期和游戏退出都会
使绑定失效。重连获取新票据和状态快照，不重放旧 socket 消息。

初始上限为完整消息 64 KiB、JSON 深度 16、收发各 128 条及各 1 MiB 字节上限、32 个挂起
请求、握手 10 秒、请求默认 10 秒、心跳 20 秒和关闭排空 3 秒。重连使用有界
指数退避和抖动。带副作用的请求不自动重试；除非业务定义 operation id 并由服务端记录幂等性，
否则断线时返回 `OutcomeUnknown`。

服务端端点应由 companion 插件持有独立 Kestrel 监听，或使用明确隔离的路由，不能占用现有
短 HTTP worker 池。catalog 没声明能力时保持不可用，不阻止普通登录。若以后要求 Launcher
完全退出而游戏仍保持连接，需要另行设计独立的 companion 进程。

验收需要覆盖断线重连、票据过期或撤销、游戏重登、多开、旧 epoch、重复 ID、队列／消息／
UTF-8 上限、慢客户端、没有 Overlay、插件卸载，以及日志中的凭据、票据和 payload 完整脱敏。

Unicode 验收还要覆盖中／韩／印尼／英文混排、补充平面字符、组合字符，以及被 WebSocket
分片切开的 UTF-8 序列。每次添加分片前都累计字节数并检查上限，完整消息收齐后才严格解码
UTF-8 和解析 JSON；不能逐片解码并用替换字符容错。文本长度限制和 UTF-8 字节限制分别计算。旧游戏若使用
GBK／CP949，只能由明确的 build adapter 转换，通道本身不能使用系统 ANSI 代码页。
.NET 字符串、JSON 和 JavaScript 全程保留 Unicode，envelope ID 使用字符串避免 JavaScript
整数精度损失。字体缺字属于 Overlay fallback 问题，不能误报为编码错误。

## Launcher 实现与接入边界

`FantasyTale.Launcher.Communication` 已实现实验性的客户端传输，不依赖游戏模块或 WPF。
Host 已把它接为可选内置服务 provider，但未配置账号授权 factory 时不会建立网络连接。
`CompanionConnection` 持有一个 epoch，`CompanionSession` 每次连接重新取得
授权，按有上限的抖动退避重连并恢复插件订阅。旧连接的发送队列和请求不会重放；认证或协议
拒绝会停止重试。账号授权 provider 后续负责一次受控的 access token 刷新，通信组件不会直接
读取已保存密码或账号 token。

每个插件获得可释放的受限 channel，仅能使用获准的消息类型和 schema 版本。传输使用一个
发送循环、一个接收循环和一个按序回调 worker；异步回调可以发出请求而不会阻塞响应接收。
回调接收取消令牌，慢消费者超出收件队列条数／字节上限后会关闭本次连接。插件忽略取消时，
释放返回 `companion.shutdown-pending`，但保留工作所有权直到回调实际结束，在此之前不重连。
控制消息保留独立的 8 条队列，默认接收上限为每秒 128 条、每条最多 4096 个分片，其他消息、
队列、路由、请求和超时上限均有校验。

**Fantasy Tale 游戏控件调用支持** 通过共享接口供其他插件调用原生 UI 能力。
游戏进程内的适配器使用 C++：报告支持的操作，在游戏 UI 线程执行并返回调用结果，
不负责 Overlay 回退策略。C# 通信基础组件负责通知业务编排及原生／Overlay 的选择。
原生调用结果不确定时不能自动重复弹出 Overlay，必须确认原生界面没有显示后才能回退。
共享原生控件接口、本地服务传输、开发者测试页及 kr-812 信息框和文字提示的初步适配器已实现，
但尚未完成实际游戏显示验收。通信组件保留已有 Overlay 通知接收功能，仅在通知需要显示时获取
Overlay。Companion 优先使用原生组件的路由及托管插件包加载器仍待实现。

`CompanionOverlayNotifications` 是单独的可选 consumer，只依赖公开通信和 Overlay 接口。
它接收 `ui.notification.show` schema 1 的纯文本标题、正文和 1..4 个 `{id,label}` 按钮，
再通过 `ui.notification.result` 回传 `notification_id` 和 `phase`：

- `queued`：已经在本机注册，准备发送挂载命令。
- `shown`：页面实际产生 `game-ui.mounted`。
- `selected`：返回允许的按钮 id 或取消。
- `busy`、`failed`、`unavailable`、`closed`：受限交付未能完成或已关闭。

默认最多 4 个对话框、32 条结果队列，等待页面就绪 10 秒。标题限制 256 个 UTF-16 代码单元、
正文 8192、按钮 id 64、按钮文字 128，同时受完整消息 UTF-8 字节上限限制。通知通过受信的
游戏风格接口绘制，不会从网络挂载 HTML 或脚本。断线会关闭原 epoch 的通知，并取消旧通知
排队的回传。省略按钮时默认简体中文“确定”，其他语言默认值与 Overlay 一起保留 TODO。

**服务端绑定：**`/v1/launch` 在游戏票据旁返回 `companion` 授权（`url`、一次性 `ticket`、`launchId`、`expiresAt`），
游戏票据记录同一个启动。Host 只在内存中为该客户端保存授权，首次连接使用它，每次重连都以 `launchId` 调用
`POST /v1/companion/tickets` 获取新票据；返回 `launch_not_found` 时会话以不可用结束。游戏登录兑换游戏票据时
服务器绑定该连接，游戏连接结束时关闭它。回复中没有授权或授权格式错误时，游戏照常运行，记录 `server-unavailable`。

**尚未接入：**双方统一 wire fixtures、服务端状态快照和 catalog 能力发现。Native owner broker、
可选插件声明和跟随游戏会话的服务管理已经实现。插件尚未发布，也没有声称通过真实服务器验收。本地验证
使用真实回环 TCP WebSocket 与 SDK Overlay 替身；TLS 升级、服务端重启和实机通知仍需独立验收。

## 预留 Gateway 生命周期服务

SDK 预留 `IGatewayControl.StartAsync` 和 `StopAsync`，供后续 Nirinsou Link consumer 获取
当前启动会话的可选服务。这次只有接口契约：不注册服务，不声明或订阅 Gateway 控制消息，
收到未知消息不会启动或关闭任何进程。

初始 Center 仍默认关闭，必须由服务端 catalog 显式开启。开启后省略模式默认 gateway，
明确指定 native 则由服务端自身实现 Center。未来经过认证的服务端请求可以另行授权，
在已绑定游戏会话期间主动启动 Gateway；仅仅连接 Nirinsou Link 不会获得进程控制权限。

`GatewayStartRequest` 只携带操作标识和不透明授权标识。Host 必须将授权绑定到当前服务端
签发的 launch、伴随连接 epoch、客户端构建、认证来源、有效期、服务器目录和 Automatic
候选池。请求不能传入可执行文件、命令行、任意连接地址或扩大的节点范围。未来实现复用
随启动器分发的 Gateway provider 和唯一租约，串行处理启停，并复用匹配的现有实例，
避免重复启动。Gateway 就绪不代表游戏已经切换连接，原生连接迁移仍是独立契约。

`GatewayStopRequest` 携带操作标识和启动结果返回的 Gateway 租约标识，通过该租约的正常
控制握手关闭，不允许控制其他游戏会话或任意 PID；旧租约标识也不能关闭替换后的新实例。
自动选服完成、游戏退出和主动关闭共用相同的清理所有权。重复操作必须幂等；结果不确定时
明确返回不确定状态，不盲目重试。epoch 结束后撤销 consumer 和控制句柄，重连不重放操作。

## 可选服务插件

Launcher 内部服务使用 `kind = "service"`，通过 `[service] provider` 选择已注册的内置
provider，不接受程序集路径或服务端地址。目前包括 `companion` 和 `overlay-notifications`。
通信组件持有可选的通知接收功能，在 `[service.messages]` 中仅声明通知所需的两条
schema-one 路由；其他业务路由仍由各 consumer 自行声明：

```toml
[plugin]
id = "bubblefighter.fantasy.communication"
version = "0.2.0"
game = "bubblefighter"
kind = "service"
minimum_launcher_version = "0.1.14"
name = { en = "Fantasy Tale Nirinsou Link", "zh-CN" = "Fantasy Tale Nirinsou Link" }

[service]
provider = "companion"
[service.messages]
"ui.notification.show" = 1
"ui.notification.result" = 1
```

这些插件使用现有插件库、导入和设置系统，不写入 x86 Hook 会话，也不能携带内存补丁、Native
DLL 或文件替换。内置通信组件默认开启、非必选；游戏控件调用支持则是独立的 Native 插件，
不请求 Companion 路由。每个客户端只能选择一个同类服务 provider。
通信组件支持整数设置 `heartbeat_interval_ms`／`request_timeout_ms`／`max_dialogs`；
不支持的键、类型或范围会在启动游戏前拒绝。

`LauncherServiceManager` 在游戏进程创建后接管生命周期，游戏结束或 Host 退出时清理服务。
缺少通信或 Overlay 时记录依赖状态，不擅自启用。账号接入需提供服务端授权的
`CompanionSession` factory，不能根据用户名、PID 或 Overlay 管道推断身份。
诊断方法 `instances.services` 只返回插件 id 和状态码。未配置账号 factory 时保持休眠，不创建
socket。集成测试已组合真实服务管理器和传输，完成通知、挂载／按钮回传及模拟游戏生命周期结束
后的清理，不启动 Launcher 界面或真实游戏。

授权 provider 失败后，`failed` 状态会保留到游戏退出或 Host 关闭；诊断不暴露异常文本或响应
内容。清理时即使前一步失败，也会尝试释放后续依赖，并始终移除客户端条目。通知诊断跟随
通道可用性；有界结果投递失败时报告 `delivery-failed`。重连测试确认旧对话框被关闭、旧按钮的
迟到点击不能进入新连接，而新通知仍能正常绘制。

## Native 插件调用（Runtime 3.3.3）

可选 Native 插件在同一份清单中声明自己需要的消息类型和 schema，Launcher 与 Runtime 均读取：

```toml
[companion.messages]
"room.mode.extra" = 1
```

仅 `kind = "native"` 支持该表。每次启动最多 64 个 Native owner，包含托管 consumer 在内的
总路由数最多 128；不同插件请求冲突的 schema 时在启动前拒绝。声明不会擅自启用通信基础插件，
也不代表获得服务器授权，缺少依赖仍作为可选功能处理。

Schema 值必须是 TOML 整数，两侧都拒绝从布尔值或浮点数隐式转换。本地 owner 使用合法插件
标识，包括以数字开头的标识；网络消息类型仍单独要求以小写字母开头。

ABI 1 的带大小 Host 表新增可选尾部 `companion`。包含 `fthook/companion.h`，通过
`fthook_get_companion(host)` 安全检查旧 Runtime；缺少 API 时走普通游戏功能。插件工作线程
在堆上准备 131073 字节输出缓冲区，使用 `invoke` 调用 `state`、`subscribe`、`unsubscribe`、
`send`、`request`、`poll`。Runtime 和 Launcher 分别校验已安装 owner、消息类型及 schema。

`state` 返回 JSON 状态和能力；`send`／`request` 返回 `status`、可选 `request_id` 和
`response`。Accepted 仅表示入队，不代表服务器已执行。订阅确认的输出为空；`poll` 返回
一条 `{type,version,id,payload}` JSON，队列为空时返回 `FTHOOK_NOT_FOUND`。取消订阅同时
移除该路由的排队事件；插件按现有清理契约取消订阅并停止工作线程。

每个 owner 最多排队 32 条、合计 128 KiB；超量返回 `{"error":"companion.events-overflow"}`。
连接 epoch 取消后丢弃旧事件，避免重连后继续投递。Native 请求共用托管通道的请求预算，游戏
退出时取消。超时为 0 时使用配置默认值，否则为 100..300000 毫秒。本地同步传输也有有限时限，
禁止在渲染回调中调用。请求发出后若返回 `FTHOOK_FAILED`，服务器结果可能未知，不能自动重放。

独立的 `FantasyTale.CompanionBroker.<本地 launch id>` 管道复用带 64 连接上限的进程认证服务，
采用四字节小端长度及 UTF-8 TOML，内部携带有界 JSON。这里的随机 id 只用于命名本地 IPC，
绝不代替服务端签发的启动身份。双方校验对端进程，不向 Native 暴露连接票据、账号 token、
网络地址或 socket；无需新增 `session.toml` 字段，也不依赖 Overlay renderer。

Host 跟随游戏管理代理生命周期，有授权后才延迟创建受限 SDK channel。正式账号 factory 缺失
时返回不可用，不建立网络连接。受控 x86 测试程序已经通过真实 Host 管理器、命名管道、Runtime
客户端和本机 WebSocket 完成双向链路验证；Native DLL 激活测试覆盖可选 ABI 尾部及旧表回退，
不启动真实游戏或 Overlay。`tools/package-native.ps1 -RuntimeOnly` 仅刷新开发 Runtime，
不会重写既有插件文件或已发布的 hash。
启动器发布打包另行调用 `tools/package-native.ps1 -BundledPluginsOnly`，准备内置的游戏控件和诊断 DLL。
此步骤不改写商店插件包或其清单；发布工具会在生成更新索引前检查所有内置原生库是否完整。

## 暂定消息格式

每个 WebSocket 文本消息使用 snake_case envelope 字段。`hello` 发送 protocol、game、build、
launch_id、locale 和每能力版本；`welcome` 回传一致身份、服务器 epoch、pending_game／connected
状态及接受的能力。进入游戏后服务端发送 `companion.state`，变为 connected。

```json
{"kind":"event","id":"n1","type":"ui.notification.show","version":1,"epoch":"example-epoch","payload":{"title":"提示","text":"服务器通知","buttons":[{"id":"ok","label":"确定"}]}}
{"kind":"event","id":"r1","type":"ui.notification.result","version":1,"epoch":"example-epoch","payload":{"notification_id":"n1","phase":"selected","action":"ok"}}
```

response／error 必须复用请求的 type／version，并带 reply_to；welcome 之后的所有消息带
协商的 epoch。心跳使用 `companion.heartbeat` schema 1 和 null payload，heartbeat_ack 通过
reply_to 指向探测 id。未知种类、旧 epoch、重复 envelope 字段／id、未协商类型和格式错误会
以不含凭据的原因码结束连接。首版接收服务端事件和客户端请求的响应；服务端主动 RPC handler
尚未纳入此 SDK。

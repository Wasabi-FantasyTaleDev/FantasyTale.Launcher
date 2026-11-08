# Hook 插件格式（`plugin.toml`，schema 1）

[English](plugin-format.md)（以英文版为准）

启动器执行的 `kind = "files"` 包使用[文件替换契约](../file-plugins.zh.md)。从 Launcher 0.1.54 和 FtHook 3.7.1 起，文件包也可声明按版本限定的 `[[patches]]`。只有存在匹配补丁的客户端版本才会将该包写入原生会话。文件部署仍完全由启动器负责；运行时校验文件元数据，但不会打开或安装其中的文件。不含补丁的文件包仍只由启动器执行。

`kind = "overlay"` 使用 [Overlay 基础层](overlay.zh.md)；`kind = "service"` 通过
`[service]` 选择内置 Launcher provider，参见[伴随通信服务契约](companion.zh.md)。
这两类由 Launcher 持有，均不进入 Native 会话。服务插件沿用现有插件库和设置界面，不能携带
内存补丁、DLL 或文件替换；只有明确的受信策略才能要求必选，否则保持可选。

一个 hook 插件就是一个文件夹，根目录放 `plugin.toml`。FtHook 运行时在游戏进程内读取它；启动器读取同一个文件，
用来展示插件、检查兼容性和生成选项表单。新增插件永远不需要重新编译宿主 DLL。

Native consumer 可以通过可选 `[companion.messages]` 声明消息类型和准确 schema，要求 Runtime
>=3.3.3，参见 [Native 通信调用](companion.zh.md)。该表只声明消息权限，不接受服务器地址或
凭据，也不会隐式启用通信基础插件。

插件配置 HTML 页面和枚举标签文件不设固定字节或行数上限。延后激活的升级记录
也不设固定体积或数量上限；重启后仍会核对客户端选择和来源身份。

可以在“插件”页导入本地文件夹或 ZIP，安装与版本选择见
[插件管理](../plugin-management.zh.md)。导入包属于可选插件，不能声明
`required_on`，也不能替换内置插件的标识。

```text
bubblefighter.fantasy.font-weight/
  plugin.toml
  README.md            可选
  icon.png             可选，启动器显示
  font-weight.dll      仅 kind = "native" 时需要
```

完整示例见英文版。下面说明各部分的含义。

## `[plugin]`

| 键 | 必填 | 含义 |
| --- | --- | --- |
| `id` | 是 | 全局唯一，小写 `a-z0-9.-`，以游戏 id 为前缀。本项目自带的插件统一用 `bubblefighter.fantasy.`，第三方插件用 `bubblefighter.<发布者>.` |
| `version` | 是 | SemVer 2.0，商店按它比较和更新 |
| `game` | 是 | `game.toml` 中的游戏 id |
| `kind` | 是 | `patch`：只有声明式补丁；`native`：额外加载 `[native].library` |
| `runtime` | 否 | 兼容的 FtHook 版本范围（如 `>=3.0.0`） |
| `minimum_launcher_version` | 否 | 启用和解析插件所需的最低启动器 SemVer，例如 `0.1.14` |
| `name`、`description` | 是 / 否 | 多语言表，`en` 必填，缺少的语言回退到 `en` |
| `authors`、`license`、`homepage` | 否 | 启动器和商店展示 |
| `diagnostics` | 否 | 默认为 `false`。兼容的对应插件被启用后，显示该客户端的诊断入口。 |

## 最低启动器版本

最低启动器版本按 SemVer 数字段和预发布优先级比较，与 `runtime` 范围独立。
不兼容的已安装插件仍可在插件库中展示并显示要求；尝试启用时引导到共用更新流程，
启动前解析再次检查。原生解析器校验该字段格式，应用版本门槛由启动器执行。
旧版严格清单读取器不能加载带此新字段的包，不应通过修改已发布不可变包加入它。

## `[compatibility]`

版本选择器可以是版本 id（`kr-1256`）、分支（`branch:cn`）或 `*`。被 `builds` 匹配且不被 `exclude` 匹配的版本上，
插件**可用**。在不可用的版本上，启动器显示警告并不加载它，运行时也会独立拒绝。游戏／客户端 `clientPolicy` 的 `require` 规则强制启用插件。
原生运行时从会话的 `required` 标志读取这个决定，不从插件包读取启用规则。

## `[[options]]`

带类型的选项，启动器生成表单并写入 `session.toml`。`type` 为 `bool`、`int`（`min`、`max`）、`string`（`max_length`）
或 `enum`（`values`）。每个选项都有 `default` 和多语言 `label`。超出声明范围的值会在应用任何补丁之前被拒绝。

## `[[patches]]`

| 键 | 含义 |
| --- | --- |
| `id` | 补丁 id，在同一版本内唯一。多条可以共用一个 id，只要它们的 `builds` 不重叠，即同一补丁的按版本变体 |
| `builds` | 可选，进一步限定本条适用的版本 |
| `when` | 可选，一个 `bool` 选项名，为 true 时才应用 |
| `client_languages` | 可选，不重复的游戏语言标签，与 `session.client_language` 忽略大小写匹配。省略或空列表适用于全部游戏语言，需要 FtHook 3.7.2。 |
| `locate` | 定位方式（见下） |
| `expect` | 写入前该处必须是的字节（十六进制，允许 `??` 通配） |
| `write` | 要写入的字节，长度与 `expect` 相同 |
| `timing` | `unpacked`（默认）：重试直到找到或超过 `deadline_ms`（默认 60000）；`immediate`：插件加载时只尝试一次 |

`locate` 三选一：

* `pattern = "AA BB ?? CC"`：在主模块的可执行节中搜索。`match = "unique"`（默认）出现多次即失败；
  `match = "first"` 取最低地址。
* `rva = 0x5F3A10`：固定 RVA，只允许在 `builds` 恰好指定一个版本时使用。
* `locator = "fthook.config-md5-verdict"`：由运行时或原生插件提供的命名定位器，用于需要代码才能找到的位置。

`offset`（默认 0）加到定位结果上。

### 应用规则

语言筛选读取用户选择的**游戏语言**，与启动器界面语言无关。非空
`client_languages` 在会话未提供语言或语言不匹配时跳过本条补丁，并与
`builds`、`when` 同时生效。它不改变文件部署或游戏语言选择；使用时应声明兼容的运行时范围。

1. 先定位当前版本适用的全部补丁并逐一核对 `expect`。任何一条失败，该插件**一个字节都不写**，结果为 `signature-mismatch`。
2. 目标处已经是 `write` 的内容，视为已安装（`already-present`）。
3. 写入限制在一个内存页内，写后回读、刷新指令缓存、恢复原保护属性；写入失败会回滚。
4. 每个插件输出一行结果：`installed`、`already-present`、`skipped-option`、`unavailable-build`、`signature-mismatch`、
   `timeout`、`failed` 或 `rollback-failed`，并附原因。
5. 不同插件的补丁范围不能重叠。运行时会在写入后一个插件之前拒绝它，并在失败原因中同时写出两个插件及补丁 id。前一个成功的插件保持已安装；调换顺序可能改变被拒绝的插件，但不会静默覆盖它。
6. 写入前先记录并校验全部原始字节；实际写入时再次核对完整快照，包括 `expect` 中的通配字节。字节变化会拒绝写入。回滚仅恢复本次真正写入的补丁，原本已安装的补丁保持不动。
7. 补丁日志或底层字节、指令缓存及保护属性恢复不完整时，插件报告 `rollback-failed`；会话停止加载后续插件，并以 `patch-restoration` 阻止就绪，即使相关插件是可选插件。其原生 DLL 保留，但不再公开 locator。IAT 回滚只撤销本次交换；保护恢复失败时，原本已存在的补丁保持不动。

发布特征码之前，要用 `tools/verify-plugin` 在它声明支持的每个版本上检查：唯一命中、原字节正确、改写跳转后目标不变。

至少提供一个 `--image`。退出码 `0` 表示已检查补丁且没有剩余运行时检查；`1` 表示字节或
特征码检查失败；`2` 表示验证未完成或命令用法错误。不支持的版本、纯原生插件及命名
locator 不能证明离线验收。混合特征码与 locator 的插件，即使特征码检查通过，仍需游戏内验证。

## `[native]`（kind = "native"）

```toml
[native]
library = "shop-ui.dll"        # 插件目录中的 x86 DLL
entry = "fthook_plugin_main"   # 可选，默认即此名
```

DLL 按 `native/runtime/include/fthook/plugin_abi.h` 的签名导出 `entry`，获得宿主 API（日志、特征码搜索、
带校验的补丁、选项读取、版本 id、注册定位器），返回结果码。同一插件的声明式 `[[patches]]` 在入口成功返回后才应用，
所以原生插件可以先注册自己补丁要用的定位器。

原生插件加载需要 FtHook 3.1 或更新版本。ABI 版本、入口与清理顺序、资源上限和进程生命周期
见 [native-abi.md](native-abi.md)。每个会话最多 64 个插件，每份 manifest 最多 256 个声明式补丁。

`[plugin] update_source` 绑定受管理的插件来源。最新版本要求属于游戏／客户端 `clientPolicy` 的
`latest` 规则，见[客户端策略](../client-policy.zh.md)。原生读取器拒绝 `required_on`、`warn_on` 和
`require_latest`，不负责网络更新或热加载。

# 自建商店源参考

[English](self-hosted-store.md)

## 一个总目录，内容按需读取

商店只需提供一份 UTF-8 TOML 总目录，默认路径为 `FantasyTale/source.toml`。
schema 2 在同一文件内包含客户端资源、插件资源和客户端 Profile。浏览时不需要为每个
资源或 Profile 分别请求接口。发行文件清单与实际内容仍然独立保存，由总目录记录路径
和 SHA-256，需要安装时再读取。

这是分发规范，不是账号登录或启动票据接口。添加商店源也不会赋予它更新 Launcher
本体的权限。本体更新源独立配置，见[启动器更新](launcher-updates.zh.md)。

## 订阅 GitHub 服务端发行版

schema 2 目录可以订阅 GitHub 最新的正式发行版。下面的条目仍放在现有 HTTP 或
AList 商店源中；以后保持压缩包文件名一致，直接在 GitHub 发布新版即可：

```toml
[[github_releases]]
id = "community.server"
game = "example-game"
builds = ["example-1"]
repository = "owner/server"
asset = "server-win-x64.zip"
executable = "Server.Host.exe"
name = { en = "Community server" }
publisher = "Community"
```

刷新只解析标签、版本、大小及 GitHub 提供的 SHA-256，不提前下载压缩包，草稿和预发行版
不会被选中。用户明确安装后固定该标签和摘要，从 GitHub 下载并校验后再解压。
暂停和重启后的任务仍使用原来版本，不会中途混入新版；资源归属仍是原商店源，来源必须
保持启用且地址一致。

不需要填写外层文件夹。安装时从验证过的文件索引中寻找 `executable` 对应的根目录。
客户端压缩包同样按版本定义中的可执行文件及别名寻找，支持多层嵌套和外层目录改名。
只安装所选根目录下的文件；找不到或有多个候选时，在写入游戏文件前明确报错。

成功解析的发行快照随目录持久化。网络故障或 API 限额时保留匹配且已验证的快照，
同时报告刷新失败；没有快照则只暂时缺少这个订阅，其他商店资源仍然可用。
发行元数据不合法时拒绝新数据并保留原完整目录。发布者也可在订阅旁附带已验证的
`github_asset` 资源快照，供首次读取回退；发布工具不会把 GitHub 压缩包或生成的
清单上传到 AList。

GitHub 的 403/429 响应包含额度重置时间或 `Retry-After` 时，当前 Launcher 会话中的
商店源共用这个截止时间，到期前不再重复查询 GitHub。刷新仍会读取原商店目录及其普通
资源。这一等待状态不会跨 Launcher 重启保留，也不能恢复已经耗尽的匿名 API 额度。

可通过以下进程级环境变量，运行真实下载队列的公开 AList 订阅验收：

```powershell
$env:FT_TEST_GITHUB_LIVE_ALIST_BASE = 'https://archive.example.org/'
$env:FT_TEST_GITHUB_LIVE_CATALOG = 'FantasyTale/source.toml'
$env:FT_TEST_GITHUB_LIVE_PACKAGE = 'community.server'
$env:FT_TEST_GITHUB_LIVE_REFERENCE_ARCHIVE = 'D:/Store/server-win-x64.zip'
dotnet test tests/FantasyTale.Launcher.Core.Tests -c Release --filter FullyQualifiedName~PublicGitHubStoreTests
```

测试不使用管理员凭据，不预填下载缓存，也不启动服务端。它验证浏览时没有下载压缩包、
明确安装后从 GitHub 下载、每个安装文件均与另行提供的压缩包一致，以及完成任务恢复时
没有网络请求。参考压缩包的大小和 SHA-256 必须与所选发行版一致。测试输出明确区分
发行元数据刷新成功与网络／额度故障下使用已验证目录快照的情况；安装成功本身不代表
获得了最新元数据。测试结束后清理隔离的安装目录。

## 部署方式与请求

在“管理来源”中选择以下任一方式：

| 类型 | 基础地址 | 总目录请求 |
| --- | --- | --- |
| `http` | `https://files.example.org/` | `GET https://files.example.org/FantasyTale/source.toml` |
| `alist` | `https://archive.example.org/` | `POST /api/fs/get`，随后 GET 解析出的下载地址 |

目录路径可以修改。目录和文件清单内的路径均相对于来源根目录，而非当前 TOML 所在文件夹。
HTTP 源可以直接使用静态网站：成功响应返回 TOML 原始字节，不要返回 HTML 预览页或
JSON 包装。无需支持 HEAD。内容长度应正确，支持 HTTP Range 可以实现断点续传。

AList 解析请求正文：

```json
{"path":"/FantasyTale/source.toml","password":""}
```

成功响应结构如下，`size` 必须替换为真实字节数：

```json
{"code":200,"message":"success","data":{"is_dir":false,"size":1234,"raw_url":"https://storage.example.org/source.toml","sign":""}}
```

没有 `raw_url` 时，适配器使用 `/d/<path>?sign=<sign>`。解析后的下载响应必须返回原始
文件内容。临时存储地址按需解析，不作为永久资源身份保存。
目前桌面来源编辑器面向公开源，不应要求用户输入管理员凭据才能浏览。

## 总目录字段

```toml
schema = 2

[catalog]
id = "community"
name = { en = "Community store", zh-CN = "社区商店" }
```

在同一文件后追加 `[[packages]]` 和 `[[profiles]]`，空数组可以省略。未知字段会被拒绝。
目录和文件清单不设固定的应用层大小或条目数量上限。schema 2 支持 Profile 和内联插件
定义，统一目录使用此格式。

| 资源字段 | 含义 |
| --- | --- |
| `id`、`kind`、`game` | 稳定的资源标识、类型和游戏 ID；本文客户端和插件分别使用 `client`、`plugin` |
| `builds` | 客户端恰好一个版本；插件填写明确支持的版本 |
| `version` | 客户端可选更新标签；插件使用 SemVer |
| `name`、`description` | 本地化纯文本；名称包含英文，描述可选 |
| `publisher` | 必填的发布者展示名称，不代表身份认证 |
| `size` | 文件清单中需要下载的总字节数 |
| `manifest`、`manifest_sha256` | 文件清单相对路径及其原始字节的 SHA-256 |
| `update_mode` | 客户端当前文件快照使用 `files`；版本化发行使用 `release` |
| `revision` | 发行排序值；有序发行使用唯一、递增的正数 |
| `authors`、`uploader` | 可选作者列表和上传者名称；上传者为空时省略 |
| `plugin_definition` | schema 2 中内联的完整 `plugin.toml` 文本 |
| `minimum_launcher_version` | schema 2 可选最低 Launcher SemVer；须与内联插件定义一致 |

资源身份绑定其配置的来源。其他源不能仅凭相同 ID 替换已安装资源。路径不允许 URL、
目录穿越、反斜杠、百分号转义、查询参数或片段。大小和哈希应由发布工具生成，不能使用
示例摘要作为真实发布数据。

## 每个客户端的自动检查

添加客户端对话框中的“检查完整性”默认开启，客户端列表的编辑操作也可以修改
这个已保存的选项。关闭后跳过客户端的自动更新检查，避免自动更新覆盖玩家自行
安装的补丁。仍可手动检查更新。此选项不会跳过下载压缩包或文件在安装前的
SHA-256 完整性校验。

## 添加客户端

准备干净的客户端分发目录，参考 `tools/publish-store/publish.example.toml`。
输出目录必须与输入目录分开：

```toml
[store]
id = "community"
name = "Community store"
output = "./public"
storage_gib = 200

[[clients]]
game = "bubblefighter"
build = "cn-279"
name = "China 2011"
directory = "./clients/cn-279"
exclude = ["logs/*", "*.log", "*.dmp"]
```

```powershell
python tools/publish-store/publish_store.py --config D:/Store/publish.toml
```

工具在输出根目录生成总目录、带哈希的文件清单和按内容寻址的文件。`--watch 30` 定期扫描，
复用未变化内容；`--full-check` 跳过发布端输入元数据缓存。客户端版本必须对应受支持的
游戏模块和有效 Profile；仅增加目录条目不能实现一个新游戏。

### 登记已经上传到 AList 的压缩包

如果完整客户端压缩包已经单独上传，可以使用 `register_archives.py` 登记，
无需重新上传。准备 schema 2 的候选总目录，原样保留所有已发布的包条目，
只增加新的客户端压缩包条目。将新条目引用的压缩包清单放在输入目录下，
并保存当前公开总目录作为并发检查的基线。配置对应的本地原始压缩包：

```toml
[[archives]]
source_path = "FantasyTale/client/cn-279/client.7z"
local_path = "D:/Distributions/client.7z"
```

```powershell
python tools/publish-store/register_archives.py --input D:/Store/public --candidate D:/Store/candidate.toml --baseline D:/Store/current.toml --references D:/Store/references.toml --base-url https://archive.example.org/ --receipt D:/Store/preflight.json --validator tools/publish-launcher/bin/Release/net10.0/FantasyTale.Launcher.Publish.dll --check-only
```

先运行 `dotnet build tools/publish-launcher -c Release` 构建校验工具。
认证仅从进程环境中的 `FT_ALIST_USERNAME` 和 `FT_ALIST_PASSWORD` 读取。
确认预检结果后，去掉 `--check-only` 才会发布。工具在一次本地读取中计算
SHA-256 与 MD5，核对存储提供方的 MD5 和大小，并用单字节请求确认匿名下载
可用。此流程要求提供方返回 MD5，不将其表述为远端 SHA-256 证明。
Launcher 安装时仍会将完整下载的压缩包与清单 SHA-256 核对。
登记工具只上传新的不可变清单，最后才上传总目录；最后写入前再次核对压缩包
元数据与总目录基线，不允许替换或删除现有包。无凭据收据会标明使用了什么
证据。启用使用新 Profile 字段的目录前，先发布兼容的 Launcher。

## 添加与更新 Profile

每项 Profile 包含 `game`、`build`、正数 `revision`，以及字符串 `definition`。
后者是完整的 schema 1 build TOML，内嵌身份必须与外层一致。修改定义时必须递增修订号，
不能降低修订号或在同一修订号下改变内容。

建议独立维护定义文件，避免手动转义长字符串。从已支持的
`games/<game>/builds/<build>.toml` 开始，在组合工具配置中填写：

```toml
[[profiles]]
definition = "./profiles/cn-279.toml"
revision = 1
```

build 定义中的 `[build]` 提供身份、可执行文件、显示名称、分支和协议版本。
可选 `region` 为两个大写字母，必填的 `protocol_version` 为非负整数，例如 `400`。
`[launch]` 提供模块的
登录方式、参数、登录策略和管理员权限要求。`[plugins]` 使用 `required`、`recommended`
和 `default` 三个插件 ID 数组；必装与推荐含义不同。指纹和模块专用启动行为必须符合
实际客户端，不应猜测登录方式。

客户端 ID 使用地区和 wire version，例如 `id-400`。同一协议的不同客户端使用
`id-400-2`、`id-400-3` 等 ID，同时声明 `protocol_version = 400`。
`[build]` 可选的 `release_version` 和 `patch_version` 仅描述发行版本，不能代替
wire version。启动时读取的本地版本信息可补充这些描述，不改变客户端 Profile ID。

`[build] build_date` 是可选字段，用带引号的固定格式，例如
`build_date = "2011-01-01 00:00:00"`，替代原来的 `year`。填写已查明的发行、
签名或编译日期；未知时省略，不影响添加、安装或启动。解析不使用用户系统的语言、
日历或时区。现有内置签名日期沿用此前展示的韩国时间（UTC+09:00），原始 UTC
签名证据仍保留在指纹目录中。日期仅用于描述，不决定协议或是否支持启动。

客户端语言在 `[launch]` 下只区分原生语言与追加语言：

```toml
[launch]
login = "nmco"
native_language = "id"
additional_languages = { "zh-CN" = "example.translation.zh-cn" }
```

`native_language` 表示未修改客户端自带的语言。`additional_languages` 将追加语言代码
映射到语言包文件插件 ID；没有追加语言时省略或写 `{}`。示例插件 ID 仅是占位符，
应先发布真实兼容的语言包再声明支持。每种追加语言需要独立插件 ID，且不能重复声明
原生语言。语言包使用可恢复的文件替换，沿用文件插件的哈希校验、异常恢复和退出恢复
机制，避免永久覆盖原生文件。即使安装并启用了多个语言包，每次启动也只加载当前
选择语言的替换文件；其他插件保持原有行为。

首页合并展示语言，不向玩家区分内部两类字段。未安装的追加语言也显示，点击后跳转
商店并选中对应语言包；只有为当前客户端安装并启用兼容语言包后才能使用。
服务器明确给出的语言列表只限制 Profile 的可选项，不会补出尚不存在的语言实现。
尚未完成的翻译先不声明。原来的 `planned_languages` 与 build 层 `client_languages`
已移除，继续填写会报配置错误，不会当成可用语言。

把资源和 Profile 组合成一份最终目录并验证：

```powershell
python tools/publish-store/compose_catalog.py --catalog D:/Store/public/FantasyTale/source.toml --config D:/Store/profiles.toml --output D:/Store/unified-source.toml
dotnet run --project tools/publish-launcher -c Release -- --validate-catalog D:/Store/unified-source.toml
```

输出必须是新路径。验证后将它发布到配置的总目录路径，保留全部引用对象。组合操作只在
本地准备当前 schema 2 目录，验证通过后再执行上传。

## 添加插件

准备干净的插件分发文件夹，根目录包含 `plugin.toml` 和运行文件。定义中 `[plugin]`
声明身份、游戏、SemVer 版本、本地化名称和 `authors`；`[compatibility]` 声明支持的
客户端版本，并按插件需要配置实现和选项。同一个插件可以支持多个版本，不要为每个
已安装客户端重复创建资源。编写方式见[插件格式](hook/plugin-format.zh.md)，清单格式见
[商店来源](store-sources.zh.md)。

`tools/publish-store/publish_plugins.py` 生成文件清单并把新发行合并进远端目录。
以下命令会实际发布：

```powershell
# 仅在进程环境中提供 FT_ALIST_USERNAME 和 FT_ALIST_PASSWORD。
python tools/publish-store/publish_plugins.py --package D:/Store/plugin-package --output D:/Store/plugin-output --base-url https://archive.example.org/
```

内容变化必须使用新 SemVer，不覆盖已发布的同版本插件。工具保留无关条目；后续组合目录
也应保留已有 Profile。schema 2 的 `plugin_definition` 填完整插件定义文本，ID、游戏、
版本必须与资源条目一致；最低 Launcher 版本若两处均填写，也必须一致。下载元数据不会
执行插件代码。

## 发布顺序与验证

先发布内容，再发布不可变文件清单，最后替换唯一的总目录。本地预先计算各文件哈希并写入
清单，客户端据此验证下载内容。不要在引用文件上传完毕前发布新总目录。旧对象应继续保留，
供已绑定旧清单的任务完成下载。避免多个发布者同时修改目录，组合时保留无关资源与 Profile。

HTTP 源把生成的目录树放到网站根目录下，最后原子替换总目录。AList 使用
`tools/publish-store/upload_alist.py`，选项、验证模式和并发控制见
[商店来源](store-sources.zh.md)。需要可写存储驱动；管理员登录成功不等于上传链路可用。

用匿名客户端检查：获取并验证总目录，读取其中一份清单，对照其原始字节 SHA-256，
再安装到独立测试目录。核对 Profile 身份、必装／推荐插件以及插件的多版本兼容性。
哈希证明内容与所选目录一致，不证明发布者身份。不要分发账号数据、凭据、会话票据、
本地覆盖配置、诊断文件或私有研究资料。

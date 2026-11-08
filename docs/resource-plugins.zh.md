# 游戏资源插件

资源插件使用 `kind = "resources"`，由游戏模块的 `IGameResourceModule` 准备资源。
公共 Core 不处理游戏归档格式、角色名称或各版本的成员布局，资源插件也不会交给
原生 Hook 加载器。完整字段示例见英文文档。

泡泡战士模块提供可复用的 `archive-members`：插件通过 TOML 声明方案和成员修改。
每项修改带原成员摘要及载荷 SHA-256；新增成员要求原路径不存在，删除成员必须
校验原摘要。路径禁止越界和符号链接，并保留客户端密钥所需的大小写。

归档行默认要求目标文件已存在。从启动器 0.1.62 起，`create = true` 明确要求
BFZ 归档不存在，并从空成员集开始生成。使用此字段的插件必须声明
`minimum_launcher_version = "0.1.62"` 或更高版本；省略或设为 `false` 仍要求
原文件已存在。创建归档不支持 PKN、删除成员、原成员摘要或字段级修改。
修改同一归档的所有方案必须一致声明不存在的原件，成员冲突仍通过常规选择处理。
即使复用缓存，准备和部署阶段也会拒绝已存在的目标。恢复时删除经过校验的新归档，
可能保留空的父目录。完整约定见[归档创建说明](hook/resource-archive-creation.md)。

所有插件的修改先合并，同一物理归档只生成一次。相同成员的相同修改会合并，
冲突修改会报错。未修改的 PKN 压缩加密块和 BFZ 本地记录直接保留，并核对原有
数据及索引元数据；修改后的成员会重新解码并验证。早期 PKN 保留其根路径、
未知头字段及区分大小写的密钥规则。

最多同时处理四个归档，单个任务逐成员处理，PKN 使用磁盘暂存。开始游戏时显示
真实准备进度并允许取消。持久缓存位于客户端目录之外，绑定原归档 SHA-256、
修改清单和载荷摘要；相同输入复用已验证结果。正式备份时再次核对原文件摘要，
防止准备期间客户端更新后覆盖了新文件。

## 部署、完整性校验与恢复

准备结果进入现有的 `RuntimeDeployment` 事务，与游戏启动、客户端修复共用
客户端锁。先备份、校验并刷盘，再提交持久恢复记录，最后替换文件。原始备份
不会从已修改归档反推生成。

正常退出或启动失败后精确恢复原文件。崩溃、蓝屏、断电后，下次启动器会先确认
游戏进程已经停止，再恢复中断的事务。如果目标被外部修改或必要备份损坏，就
保留恢复现场并停止写入。恢复和清理过程中再次中断，也可以继续恢复。

游戏运行期间，客户端校验和修复无法获得该客户端锁；异常退出后的修复必须先
恢复资源事务，再检查和替换原版内容。恢复不会清空插件偏好或有效缓存。

## 角色模型接口

`character-models` 使用相同的合成和恢复机制。首次设置为 `{}`，保留所有原版。
选择客户端本来使用的模型代际同样不修改资源；关闭插件保留已保存的选择。

其他扩展通过 `IGameModule.GetCapability<ICharacterModelCapabilities>()` 按接口
注册角色或读取最近一次解析的选择快照。注册返回可释放的句柄。角色包含稳定
身份、地区译名、默认代际和经过验证的资源方案；客户端没有、扩展也未提供的
角色不会显示。提供者变化会使准备失效。没有此可选接口时，扩展使用自己的默认
资源，不应因此强制依赖模型插件。

资源插件可在 `plugin.exclusive_with` 中声明不能同时使用的整文件替换插件。启用
其中一种方案时，会原子地关闭可选的冲突项，保留其设置与插件库版本。客户端策略
要求的组件受保护；手动修改配置也不能绕过启动前冲突检查。这可避免旧整包方案和
新的成员合成方案同时覆盖同一归档。

读取结果区分用户请求与有效代际，并提供资源来源和修订号。这是已解析的准备
快照，不代表已经通过游戏画面验收。新增角色、道具及服务端协议不在本次范围内。

## 表格与界面的细粒度修改

`provider = "resource-edits"` 修改客户端表格中的记录和原生 `.ui` 界面中的控件，而不是替换整个成员。多个插件可以修改同一成员：先合并全部修改，再从用户自己的客户端副本重新生成该成员一次，并与角色模型或整成员方案一起只合成一次归档。完整示例见 [examples/resource-edits](examples/resource-edits)。

清单有三个便捷段和一个通用段。`[[items]]` 以 `id` 定位物品表，可写 `name`、`description` 和 `set`，默认动作 `patch`。`[[strings]]` 以 `key`（复合键用数组）定位字符串表，写 `text`；默认目标 `strings`，`xml-strings` 选择复合键表；默认动作 `upsert`：记录存在则修改，不存在则新增。`[[edits]]` 可指定任意 `target` 与 `action`（`add`、`replace`、`patch`、`upsert`、`remove`）。每行都可用 `build`（名称或列表）和 `when`（已声明的布尔选项）。`clear = ["字段"]` 清空单元格；`expect = { 字段 = "原值" }` 是前置条件。未写出的字段保持原值，空字符串写入空值。

值可以是语言表，如 `{ "zh-CN" = "…", ko = "…" }`。依次使用实例所选的客户端语言、构建原生语言、`fallback_languages`；都没有时 `patch` 和 `upsert` 保留原文，`add` 和 `replace` 报错。文本按成员自身的字节序标记、编码、声明和换行严格解码与编码，无法编码的字符直接报错。

目标位置是数据。模块内置的 `resource-targets.toml` 记录每个已知构建的归档、成员、工作表、表头行、键列和逻辑字段映射。插件中 `build` 和 `name` 相同的 `[[targets]]` 行只覆盖写出的项；`fields` 与 `languages.<语言>` 按字段合并。字段按表头文本或 `#N` 定位，从不使用固定列号；缺失、重复或有歧义的表头、重复记录键、找不到的工作表都会报错。当前默认值覆盖 `cn-406`、`kr-812` 和 `kr-1256`。现代 YAML 字符串表和其他构建尚未支持，其他构建需要显式声明目标。

首格为 `Name:HorzTable` 的横向工作表（`FighterConfig.xml`）每列一条记录：`[Field]` `[Desc]` 行是记录键，之后每行是一个字段，名称在第一列。以 `#` 开头的行开始一个分区；同一字段名在两个分区出现时写作 `#分区/字段`，不加限定的重复名称会报错。`fighter` 目标指向 `FighterConfig`，修改行写 `sheet = "FighterConfigMode"` 可选择同一工作簿中的其他工作表。新增记录追加一列，删除记录删除整列，并把之后的单元格、列定义和计数左移。布局从工作表本身识别；目标声明的 `orientation`（`vertical` 或 `horizontal`）会与之核对。

界面修改写 `member` 与 `control`，控件路径由 `Name` 或 `Name[ArrayIndex]` 以 `/` 连接。`patch` 只改 `set` 中的属性；`replace` 与 `add` 需要该界面中已出现过的原生控件类型 `element`，`add` 可写从零开始的 `position`。`hide` 已保留，但由于没有经过证明的原生可见性字段，会返回 `resource.ui.hide-unsupported`。客户端插件可以通过 `GetCapability` 获得 `IGameUiForms`，只读解析界面。

### CSV 表

`[[tables]] path = "translations.csv"`（可选 `build`、`when` 和默认 `target`）从一张表读取任意目标的多处修改，示例见 [examples/resource-edits/translations.csv](examples/resource-edits/translations.csv)：

```text
target,key,field,member,action,value,zh-CN,ko,en,comment
item,1010002,name,,,,大炮,대포,Cannon,物品名
item,1010002,price,,,500,,,,数值和编号写在 value
xml-strings,Ui|MainMenu|BTN_EXIT,text,,,,退出,,,复合键
fighter,1,name,,,,大头,,Bazzi,横向记录
ui,pnlMain/btnExit,Text,Ui/MainMenuStage2.ui,,,退出游戏,,Quit,
```

`target`、`key`、`field` 必填，界面行用 `member` 指定界面文件。可选列：`action`（表格默认 `upsert`，界面默认 `patch`）、`build`（可写多个，以逗号或空格分隔）、`sheet`、`value`（各语言通用的文本，用于数值和编号）、`comment`。其他表头都视为客户端语言代码。复合键以 `|` 连接。同一记录、同一动作的多行合并为一次修改，名称和介绍可以分行写；同一字段出现两次，或同一记录用了两种动作，都会报错。语言单元格为空表示未提供该语言，按上文语言规则处理；整行都没有文本的视为待翻译模板行并跳过。`target` 以 `#` 开头的行是注释。

表格可以是 UTF-8、UTF-16 或 UTF-32，大端小端均可，有无字节序标记均可；Excel 的“CSV UTF-8”和“Unicode 文本”都能直接使用。没有字节序标记时，UTF-16 和 UTF-32 按 ASCII 表头中的零字节识别；也可在 `[[tables]]` 行写 `encoding = "utf-16le"`（或 `utf-8`、`utf-16be`、`utf-32le`、`utf-32be`）声明，字节序标记必须与声明一致。分隔符按表头自动识别为 Tab 或逗号；带引号的单元格可以包含分隔符、两个连写的引号和换行。GBK 等旧代码页的表会被拒绝，不会读成乱码。保留列名为小写，写成 `Key`、`TARGET` 会报错，不会被当成语言列。错误信息会指出表格和行号。表格中的修改与清单修改的合并和冲突规则完全相同。

### 正则表达式

`[[patterns]]` 一行可以同时修改许多记录或控件：

```toml
[[patterns]]                # 给所有物品名加括号
target = "item"
match = '.*'                # 锚定到整个键的正则；复合键以 | 连接
fields = ["name"]
find = '^(.+)$'             # 不锚定，替换当前文本中的每一处匹配
replace = { "zh-CN" = "[$1]", en = "<$1>" }

[[patterns]]                # 给一组记录写同一个值
target = "item"
match = '10100\d\d'
set = { price = 500 }

[[patterns]]                # 所有界面的 OK 按钮
target = "ui"
member_match = 'Ui/.*\.ui'
match = '.*btnOk.*'
fields = ["Text"]
find = '^OK$'
replace = { "zh-CN" = "确定" }
```

`match` 用正则选择，`key`（界面用 `control`）精确选择一条，二者只能写一个。界面行需写 `member` 或 `member_match`。一行要么用 `find`、`replace`、`fields` 替换，要么用 `set`、`clear` 直接写值；`action = "remove"` 删除所有匹配的记录或控件。`replace` 遵循上文语言规则，可用 `$1`、`${name}`，字面的美元符号写 `$$`。CSV 表中填了 `match` 或 `find` 的行就是正则行：`find` 加上 value 或语言列构成替换，`field` 可用 `|` 列出多个字段，`member_match` 选择界面文件。

正则使用 .NET 的线性时间引擎（`RegexOptions.NonBacktracking`）编译，任何正则都不会卡住启动；环视、反向引用和原子组会被拒绝。匹配以 UTF-16 码元为单位：替换结果会拆开基本多文种平面之外的字符时报错。合并之前，每个插件的正则按声明顺序作用于用户原版成员，后面的正则看到前面的结果，最终转换为实际发生变化的记录的普通修改和删除。它们与其他修改一样合并和产生冲突；同一插件对同一字段或记录的直接修改优先于它自己的正则。结果按归档和插件缓存，身份为原归档摘要和正则内容。

部分客户端表格存在重复的键，且无法确定客户端读取哪一条。重复的键指向所有同键记录：直接修改对每条做相同修改，正则按每条自己的文本分别替换。同名的兄弟控件按文档顺序写作 `Name@0`、`Name@1`，无名控件写作 `@0`、`@1`；`IGameUiForms` 返回的路径也采用这种写法。

### 冲突与选择

合并之前，字段名、逻辑别名与 `#N` 引用先解析成原始成员中的实际列或横向字段行。隐式选择的工作表与其显式名称使用相同身份；指向同一工作表的目标必须采用一致的表头与键布局。冲突的 `Field` 使用规范化后的 `#N` 位置。所有 `expect` 条件在应用修改前对原始内容统一检查，相同翻译不会使另一个插件的原值条件失效。同一插件自身相互矛盾的修改会作为无效插件报错。

不重叠的修改和完全相同的修改无论插件顺序都会合并。其余情况在暂存任何文件之前抛出 `GameResourceConflictException`。它的 `Report` 可序列化：`Snapshot`、`Build`、`Language` 和 `Conflicts`；每个冲突含稳定的 `Id`、`Code`（`resource.conflict.field-value`、`duplicate-add`、`remove-edit`、`add-edit`、`row-vs-field`、`replace-replace`、`parent-child`、`member-replace`）、`Archive`、`Member`、`Target`、`Key`、`Field`，以及 `Parties`（插件标识、版本、多语言名称、动作和简短请求值）。重试时通过 `LaunchContext.ResourceConflictChoices`（或启动参数 `resourceConflictChoices`）提交快照、冲突标识和要保留的插件。只跳过该组中落选的操作。选择仅在快照不变时有效：构建、客户端语言、插件版本、选项、清单或原归档变化后会重新询问。

重新生成的成员与合成归档缓存在一起。缓存身份包含原归档摘要、有序修改内容和生成器版本，因此语言、插件或客户端变化后会重新生成。

两个整成员提供方之间的冲突也会生成结构化报告，不要求同时存在字段级修改。保留整成员提供方时会移除全部落选替换和来源记录；保留细粒度修改后，可能继续出现更小范围的字段冲突，回答下一份报告时保留先前的选择。当前快照下未知的冲突标识或不参与冲突的胜者会报错。宿主桥接返回 `code = "resource_conflict"` 和 `conflictReport`，接收 `resourceConflictChoices` 数组，不设置固定冲突数量上限。

启动器通过冲突对话框展示每个参与插件的信息卡片。冲突资源、请求修改的内容和选择控件放在卡片外。每组冲突都需要选定保留的插件才能继续；取消后不会启动游戏。这些选择仅对本次启动有效。

实际字段地址另有绑定原归档摘要并校验完整性的缓存，未变化的后续启动可以复用成员，不必再次打开并解析 XML 或文本表。

### 托管资源接口

`BubbleFighterModule.GetCapability<IBubbleFighterResourceEdits>()` 提供目标查询、原生控件检查，以及用于物品、普通字符串、复合键字符串和界面控件的 `CreateManifest` 链式构建器。`ResourceTargets` 公开已知版本、目标、归档和成员路径常量，默认值仍来自嵌入的 TOML。`ResourceTargetOverride` 继承未指定的值，并支持与 TOML 一致的按语言字段映射。例如：

```csharp
var api = module.GetCapability<IBubbleFighterResourceEdits>()!;
var manifest = api.CreateManifest("en")
    .PatchItem("1010001", new Dictionary<string, string>
    {
        ["zh-CN"] = "示例翻译", ["en"] = "Example item"
    })
    .PatchString("MSG_EXAMPLE", "示例消息")
    .ToToml();
```

新增只写入键及指定字段，不克隆模板行；整行替换保留键并清空未指定的非键值。界面的新增与替换会在 XML 片段上应用声明的属性，禁止通过属性修改控件身份；新增子树也参与后代控件冲突检查。XML 声明的编码必须与实际 Unicode 字节一致，Tab 分隔表的键禁止携带 Tab 或换行来注入额外列或记录。

同一插件不能一边替换界面子树一边修改其后代，也不能一边删除子树一边写入后代属性。这种计划会在暂存前被拒绝，避免规范化执行顺序静默吞掉已声明的修改。应将所需的完整子树放入替换片段中。

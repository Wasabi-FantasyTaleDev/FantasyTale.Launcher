# 客户端策略

[English](client-policy.md)

游戏定义、客户端版本配置和服务器目标使用同一个格式版本为 1 的 `ClientPolicy`。
插件包和客户端不再有独立的推荐插件格式。未知策略字段和规则类型会被拒绝。

本地 `game.toml`、`builds/<build>.toml` 和商店分发的客户端配置均在其他 TOML 节后
声明根级表：

```toml
[[client_policy_sources]]
name = "managed-store"
kind = "http"
base_url = "https://store.example.invalid/"
catalog_path = "catalog.toml"

[[client_policy_rules]]
plugin = "example.component"
id = "install-offer"
type = "recommend"
builds = ["kr-1256"]
sources = ["managed-store"]
default_selected = false

[[client_policy_rules]]
plugin = "example.component"
id = "minimum"
type = "min_version"
builds = ["kr-1256"]
version = "1.2.0"
```

每条规则需要插件标识、稳定的规则 ID 和类型。`builds` 只接受精确客户端 ID；
空列表或省略时适用于该游戏的全部客户端，不支持分支选择器或通配符。

| 类型 | 额外字段 | 效果 |
| --- | --- | --- |
| `require` | 无 | 启动前必须安装并启用。 |
| `disable` | 无 | 仅在本次启动快照中禁用，不修改已保存选择。 |
| `recommend` | `sources`、可选的 `default_selected` | 每个客户端、每条规则提示一次；来源空列表允许已配置的商店。 |
| `min_version` | `version` | 启用的版本不得低于此 SemVer。 |
| `max_version` | `version` | 启用的版本不得高于此 SemVer。 |
| `latest` | 无 | 启用的组件在启动前解析最高兼容版本。 |

推荐插件始终显示在推荐列表中。省略 `default_selected` 时默认为 `false`，只有明确
设置为 `true` 才默认勾选，用户可以修改可选项。服务器 JSON 对应字段为
`defaultSelected`。其他规则类型不能使用此字段。本地和服务器推荐合并时，必须每条
适用推荐都明确要求勾选，合并后的卡片才默认勾选。首次安装与首次使用的推荐采用相同规则。

来源声明不会自动添加商店。安装使用玩家已启用的来源，并按传输类型、地址和目录匹配
具名来源。版本限制同时用于已安装候选和商店下载。来源归属（`game.plugin_sources`
和 `update_source`）与启用决定分开。`[plugins].default` 和 `game.default_plugins`
仅选择初始组件，不承担强制、推荐、禁用或版本约束。

游戏规则与本地或商店提供的客户端规则组合。服务器规则为本次启动叠加限制：取更严格的
最低和最高版本，累加强制要求，并对具名来源取交集。服务器不能放宽本地要求。
禁用与其他规则冲突、来源交集为空、版本范围倒置或强制最新与最高版本共存都会明确失败。
同一插件在重叠客户端范围内不能同时声明 `require` 和 `recommend`，游戏、客户端与服务器规则合并后也一样。不同客户端范围互不重叠时可分别使用这两种类型。单个声明中，作用范围重叠的同类型规则也会被拒绝。

原生运行时只读取托管会话的 `required` 标志，插件清单不能施加启用策略。
`required_on`、`warn_on`、`require_latest`、`game.plugin_policies` 及
`[plugins].required`／`recommended` 均被拒绝。旧推荐桥接方法由
`clientPolicy.claimPlugins` 和 `clientPolicy.acceptPlugins` 替换；确认记录写入
`client-policy.seen`。不提供旧格式别名、迁移读取器或兼容桥接。

当前仓库配置使用以下精确作用范围：

| 客户端 | 插件 | 规则 |
| --- | --- | --- |
| cn-279、cn-324、cn-404、cn-406、cn-408、cn-706、cn-717、cn-735、cn-774 | `bubblefighter.fantasy.login-fix-cn` | `require`、`latest` |
| kr-1256 | `bubblefighter.fantasy.login-fix-kr` | `require`、`latest` |
| kr-1256 | `bubblefighter.fantasy.shop-unlock` | `require` |
| kr-812、id-400、id-1809 | `bubblefighter.fantasy.font-support-cn` | `recommend` |

十三个客户端共 24 条显式规则。必选组件不再重复声明为推荐。
包的兼容范围、本地化警告和文件恢复行为仍属于包元数据。客户端或服务器未启用的版本不会因策略列入而开放。

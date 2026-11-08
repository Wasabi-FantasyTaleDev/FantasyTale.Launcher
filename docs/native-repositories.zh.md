# 原生代码仓库边界

[English](native-repositories.md)

启动器与泡泡战士实现采用独立仓库，分别维护版本和发行。这是迁移目标，当前源码仍在同一个仓库中。
仓库可见性由维护者另行决定，目前 GitHub 仓库保持私有。

## 归属

| 启动器仓库 | 泡泡战士插件仓库 |
| --- | --- |
| 托管 SDK、共享宿主、核心和下载器 | 泡泡战士托管模块 |
| 通用 FtHook 运行时及公开 C ABI | `nmcogame` 宿主及登录桥接 |
| 通用 overlay 和 companion 契约 | 认证、移动及游戏界面插件 |
| 商店/profile 解析及包校验 | 客户端 profile、游戏定位器及插件清单 |
| SDK 导出及合成 ABI 示例、测试 | 游戏专用样本及集成测试 |

必装、推荐插件及兼容版本仍由商店 profile 控制。除非清单要求更新的启动器或运行时契约，
发布游戏包不需要升级启动器。已经发布的包版本保持不可变，迁移必须保留插件 ID 和用户设置。
超级跑跑、跑跑飞空艇的实现模块继续放在私有仓库，本次决定不公开这些模块，也不移动服务端代码。

## 独立 ABI 分发

执行 `powershell -File tools/package-native-sdk.ps1`，在已忽略的 `artifacts/native-sdk`
下导出白名单限定的 SDK。可通过 `-OutputDirectory` 指定新目录，已有目录会被拒绝。
分发包括 C 头文件、许可证、双语说明和独立的 x86 定位器示例。
`sdk.json` 记录文件精确长度、哈希、运行时版本与 ABI 编号。
分发不包含二进制、游戏实现、会话文件或解析器依赖。

分发 SDK 前，应在仓库外目录使用 Visual Studio v145 和 Windows SDK 编译示例。
这只能证明 C ABI 示例可独立构建，不代表现有 C++ 插件已完成拆分，也不代表游戏行为已验收。

## 迁移验收

源码依赖清单和按顺序执行的 C ABI 迁移方案见
[原生插件仓库迁移](native-plugin-migration.zh.md)。

1. 先清点内部辅助函数依赖。现有移动插件使用模式匹配与字符串辅助代码，韩服认证还依赖内部会话读取器，
   工程仍通过本仓库路径链接 `fthook.lib`。应改为调用宿主接口，或提取范围明确、独立版本化的辅助包，
   不能直接把所有运行时内部实现都当作 SDK 导出。
2. 将游戏特有的 `nmcogame` 宿主及转发、登录行为一起迁移，明确它如何使用运行时，
   不得使启动器依赖游戏实现。保留会话及启动就绪契约，票据与凭据不得进入构建产物。
3. 直接包含游戏源码的测试随实现迁移；合成 ABI、事务测试留在运行时仓库。
   每个仓库必须可以依靠固定版本、文档明确的 SDK 输入独立构建。
4. 分离运行时、宿主及插件的打包。现有 `tools/package-native.ps1` 仍混合这些步骤，
   还会改写认证包文件哈希。迁移必须保留已发布包的不可变性，不能对冻结发行包运行它来走捷径。
5. 删除或迁移受支持的实现前，验证干净检出构建、插件加载、必装插件及 profile 行为。
   已弃用的实现和停止维护的实验从公开源码移除。分别记录两个仓库的来源及依赖声明。

## 私有仓库清单

Launcher 与客户端集成共分四个仓库。SDK 和通用运行时留在 Launcher 仓库，
专用插件按游戏集中维护，不逐个拆仓。创建新仓库时已确认四个仓库均为私有。

- [FantasyTale.Launcher](https://github.com/Wasabi-FantasyTaleDev/FantasyTale.Launcher)：通用 Launcher、SDK、运行时和 Overlay。
- [FantasyTale.Launcher.BubbleFighter](https://github.com/Wasabi-FantasyTaleDev/FantasyTale.Launcher.BubbleFighter)：泡泡战士集成与专用插件。
- [FantasyTale.Launcher.TalesRunner](https://github.com/Wasabi-FantasyTaleDev/FantasyTale.Launcher.TalesRunner)：私有超级跑跑集成与专用插件。
- [FantasyTale.Launcher.AirRider](https://github.com/Wasabi-FantasyTaleDev/FantasyTale.Launcher.AirRider)：私有跑跑飞空艇集成与专用插件。

泡泡战士本地检出已包含独立的一键跑步，以及包含客户端 profile 和现用插件定义的
集成源码快照，涵盖认证、Overlay 和诊断模块。快照中的原生工程仍引用 Launcher 的共享构建输入，
一键跑步和诊断模式已有独立公开 ABI 构建记录，后者源码位于该仓库的
`native/crash-recorder`。干净源码构建及实际 DLL 加载测试覆盖合成宿主中的异常记录、
失败清理、minidump 结构和真实同步接收记录，不代表游戏行为验收通过，也没有迁移
`nmcogame` 和认证组件。停止维护的前进起跳实验和已被替代的独立插件包已从公开源码移除。
超级跑跑和跑跑飞空艇仓库仍只有职责说明，上述迁移验收要求仍未完成。
现有服务端仓库独立维护，不在此次拆分范围。对外再分发仍需完成
[公开仓库审查](release/public-repository-audit.zh.md)中的剩余项目。

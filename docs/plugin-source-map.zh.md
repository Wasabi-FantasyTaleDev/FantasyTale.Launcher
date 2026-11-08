# 插件源码索引

[English](plugin-source-map.md)

`.vcxproj` 是构建入口，真正的 C/C++ 源码由其中的 `ClCompile` 指向，可能放在另一个目录。另一些插件由 TOML 补丁、托管资源提供器、HTML/Vue 组件或资源生成脚本实现，不需要单独的 C++ 动态库。

已对照当前商店：17 个现用插件 ID 的最新发布版本，在本快照中都有对应源码定义；另有 7 个保留的旧 ID，通过定义中的 `aliases` 迁移到现用实现，并不是另外 7 份缺失代码。这是源码覆盖核对，不代表游戏实测或依赖原始素材的最终安装包已逐字节复现。

下列路径均相对仓库根目录。`modules/BubbleFighter` 是普通源码目录，不是子模块。

| 插件 | 实现入口 |
| --- | --- |
| Fantasy Tale Nirinsou Link | [通信服务](../src/FantasyTale.Launcher.Communication/)、[宿主通信桥](../src/FantasyTale.Launcher.Host/Bridge/LauncherBackend.Companion.cs)、[游戏内通信](../games/BubbleFighter/native/nmcogame/messenger.cpp) |
| 一键跑步 | [独立原生实现](../modules/BubbleFighter/native/auto-dash/auto_dash.cpp)、[按版本声明的补丁](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.auto-dash/plugin.toml) |
| 新旧角色模型切换 | [托管资源组合](../src/FantasyTale.Launcher.Games.BubbleFighter/CharacterModels/)、[设置和定义](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.character-models/)、[打包生成脚本](../modules/BubbleFighter/tools/package-character-models.py) |
| 诊断模式 | [原生记录器和转储实现](../games/BubbleFighter/native/crash-recorder/) |
| 中文字体支持 | [补丁与文件部署定义](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.font-support-cn/plugin.toml)、[字体合并](../modules/BubbleFighter/tools/merge-client-fonts.py)、[轮廓加粗](../modules/BubbleFighter/tools/embolden-client-font.py)、[多版本生成](../modules/BubbleFighter/tools/build-client-font-variants.py) |
| 国服认证组件 | [原生适配器](../modules/BubbleFighter/games/BubbleFighter/native/login-bridge/native_adapter.cpp)、[插件入口](../modules/BubbleFighter/games/BubbleFighter/native/login-bridge/dllmain.cpp)、[定义和补丁](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.login-fix-cn/plugin.toml) |
| 韩服认证组件 | [原生实现](../games/BubbleFighter/native/kr-auth/kr_auth.cpp)、[定义](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.login-fix-kr/plugin.toml) |
| ESC 提前离场 | [原生输入处理](../modules/BubbleFighter/native/mid-match-exit/mid_match_exit.cpp)、[离场流程](../modules/BubbleFighter/native/mid-match-exit/exit_flow.cpp)、[清单模板](../modules/BubbleFighter/native/mid-match-exit/plugin.preview.toml)、[补丁](../modules/BubbleFighter/native/mid-match-exit/native-patches.toml)、[打包脚本](../modules/BubbleFighter/tools/package-mid-match-exit.py) |
| 原生风格组件 | [面板与定义](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.native-style-components/)、[Vue 组件](../tools/game-ui/src/) |
| 游戏控件调用支持 | [原生 UI 调用及服务实现](../games/BubbleFighter/native/game-controls/)、[托管调用客户端](../src/FantasyTale.Launcher.Core/Hooks/GameNativeUiClient.cs) |
| 文件校验跳过 | [TOML 补丁](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.no-file-verify/plugin.toml) |
| 血量数值显示 | [原生实现](../native/samples/bubblefighter.fantasy.numeric-health/numeric_health.cpp)、[客户端描述](../native/samples/bubblefighter.fantasy.numeric-health/client_descriptors.cpp) |
| Overlay | [叠加层应用](../src/FantasyTale.Launcher.Overlay/)、[原生叠加层接口](../native/runtime/include/fthook/overlay_ui.h)、[插件定义](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.overlay/plugin.toml) |
| 海盗卤蛋表情修复 | [资源定义](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.pirate-expression-fix/)、[资源生成脚本](../modules/BubbleFighter/tools/package-pirate-expression-fix.py) |
| 分辨率扩展 | [原生实现](../modules/BubbleFighter/native/resolution-extension/resolution_extension.cpp)、[分辨率数据模型](../modules/BubbleFighter/native/resolution-extension/resolution_model.cpp)、[设置与描述文件](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.resolution-extension/) |
| 场景样式选择 | [资源定义和设置](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.scene-styles/)、[资源生成脚本](../modules/BubbleFighter/tools/package-scene-styles.py) |
| 商店开放 | [TOML 补丁](../modules/BubbleFighter/games/BubbleFighter/plugins/bubblefighter.fantasy.shop-unlock/plugin.toml) |

共享[资源编辑提供器](../src/FantasyTale.Launcher.Games.BubbleFighter/ResourceEdits/)、[归档组合](../src/FantasyTale.Launcher.Games.BubbleFighter/Archives/)和[原生补丁引擎](../native/runtime/src/engine.cpp)负责执行数据定义。游戏字体、角色模型和场景贴图仍由使用者提供；源码保留生成脚本与配置，不包含这些原始素材。

`modules/BubbleFighter/native/cn-auth/cn_auth.vcxproj` 指向模块内 `games/BubbleFighter/native/login-bridge` 下的适配器，不是只有空工程。`native/sdk/sample_locator.vcxproj` 是 SDK 导出模板；`tools/package-native-sdk.ps1` 会把它与 `native/samples/sample.locator/sample.c` 一起复制到 SDK 包。请构建导出后的 SDK 示例，或源码树内 `native/samples/sample.locator` 的工程。

启动器集成请从根目录原生解决方案构建。模块内的集成副本通过已修正的相对导入使用根目录运行时，输出到模块独立的 `artifacts/native-integration` 目录；独立模块工程则使用固定版本 SDK。参见[贡献指南](../CONTRIBUTING.zh.md)和[工具清单](../tools/README.zh.md)。

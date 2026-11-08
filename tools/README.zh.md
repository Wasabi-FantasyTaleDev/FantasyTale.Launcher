# 开发与打包工具

[English](README.md) · [插件源码索引](../docs/plugin-source-map.zh.md)

公开仓库保留用于构建、打包、验证或复现现有功能的工具。仅供本地比对分析的脚本加入忽略规则，保留在开发者私有工作区。

使用 `python tools/verify-native-project-inputs.py --template-project native/sdk/sample_locator.vcxproj` 检查原生工程的显式源码输入。SDK 工程是导出模板，由 `package-native-sdk.ps1` 与示例源码组装。这项输入检查不替代编译器或 MSBuild 求值。

| 范围 | 保留用途 |
| --- | --- |
| `package-native.ps1`、`package-native-sdk.ps1` | 准备原生二进制，并按明确源码清单导出插件 SDK。 |
| `package-launcher.ps1`、`package-gateway.py`、`copy-runtime-notices.ps1`、`create-launcher-archive.py` | 生成发行包，验证依赖、许可证、架构与支持恢复的更新索引。 |
| `publish-launcher/`、`publish-store/` | 可复用的发行、商店发布、可导入插件 ZIP 生成及目录生命周期操作。凭据从外部配置取得。 |
| `verify-*.py`、`verify-plugin/`、`verify-launcher-*`、`webview-policy-test/` | 发行包校验、插件安全校验、真实升级及归档回退验收、浏览器边界回归测试。 |
| `inspect-client/`、`native-ui/` | 新增或审查客户端版本时，可复用的身份识别和原生描述文件校验，不执行客户端。 |
| `prepare-hook-test/`、`web-preview/` | 准备隔离原生测试目录，以及前端预览。 |
| `game-ui/src/` 及其构建、资源准备脚本 | 重建共享 Vue 游戏 UI 库，准备使用者自行提供的可选 UI 素材。`web/package.json` 依赖这些源码和测试。 |
| `import-level-assets.py`、`export-reviewed-source.py` | 可选的本地素材导入，以及按明确清单导出源码；不会自动发布游戏素材。 |
| `modules/BubbleFighter/tools/` | 插件打包生成器、字体合并及加粗、模型贴图修复、归档生成、原生 SDK 校验和按版本执行的补丁校验；保留对应配置和自动回归测试。 |

旧 `game-ui/extract.py` 和 `extract-v3.py` 依赖本仓库没有的旧归档工具工程。跨版本音效库比对脚本，以及角色模型、海盗验证程序，依赖调查期间的专用输入清单和固定实验断言。这些仅供本地分析的文件已移除跟踪并加入明确 `.gitignore` 规则；实际打包生成器和运行时实现继续保留。

`scene-style-evidence.toml`、`character-models.toml` 虽然沿用历史名称，仍是生成插件包所需的配置，删除后将无法重建资源插件。字体原始文件仍由使用者提供，源码保留合并脚本、版本规则和测试。

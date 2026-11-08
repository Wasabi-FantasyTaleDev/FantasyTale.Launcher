# FantasyTale.Launcher

[English](README.md)

面向经典网游社区服务器的多游戏启动器，基于 .NET 10、Photino 和 Vue 3 构建，
外观和交互以 Windows 11 原生 WinUI 3 应用为目标。

> 状态：早期开发。优先完成整体架构和客户端 hook 运行时，进度见
> [docs/roadmap.md](docs/roadmap.md)。

## 支持的游戏

| 游戏 | 状态 |
| --- | --- |
| 泡泡战士（버블파이터 / 泡泡大亂鬥 / Crazy Shooter） | 开发中，配合 [BubbleFighter.Fantasy](https://github.com/Wasabi-FantasyTaleDev/BubbleFighter.Fantasy) 使用 |
| TalesRunner | 计划中（模块不在本仓库） |
| AirRider | 计划中（模块不在本仓库） |

## 特性

- **多客户端共存**：每个安装的客户端都是独立实例，同一版本可以为不同服务器各装一份。
- **自由选择服务器**：按地址添加服务器。提供启动器 API 的服务器支持密码校验登录、
  账号信息展示，以及全自动的客户端安装和更新。
- **客户端 hook 插件**：hook 通过一个小型运行时在游戏内生效，像模组一样安装、启用和更新。
  大多数插件只是数据（`plugin.toml`），不需要编译。插件会声明支持的客户端版本，
  以及在哪些版本上必须启用。
- **插件商店与下载源**：可以添加自己的插件商店和下载源。下载默认使用 aria2
  （多线程、断点续传），也可以用普通 HTTP；支持 AList 作为下载源。
- **用定义扩展**：游戏、客户端版本、服务器、插件都是 TOML 文件。支持新的客户端版本只需新增一个文件，不需要重新编译。
- **七种界面语言**：简体中文、繁體中文、한국어、Bahasa Indonesia、Tiếng Việt、ไทย、English。泰语尚未翻译的文案保留英文，客户端日期标签使用泰国佛历。

## 文档

- [插件源码索引](docs/plugin-source-map.zh.md)：逐个插件的实现文件与资源生成入口。
- [开发工具清单](tools/README.zh.md)：保留的构建工具及仅供本地分析的忽略项。

- [架构](docs/architecture.zh.md)
- [Hook 插件格式](docs/hook/plugin-format.zh.md)
- [会话约定（英文）](docs/hook/session-contract.md)
- [设计决策（英文）](docs/decisions/README.md)
- [路线图（英文）](docs/roadmap.md)
- [贡献指南](CONTRIBUTING.zh.md)

## 相关项目

- [BubbleFighter.Fantasy](https://github.com/Wasabi-FantasyTaleDev/BubbleFighter.Fantasy)：与本启动器配套的泡泡战士服务端。
- [Photino.NET](https://github.com/tryphotino/photino.NET)：原生窗口与 WebView 宿主。
- [AList](https://github.com/alist-org)：文件列表与下载服务，作为下载源受支持。

## 许可证

MIT，见 [LICENSE](LICENSE) 和 [NOTICE.md](NOTICE.md)。本项目不包含任何游戏客户端文件；
游戏名称与商标归各自所有者。

# 参与贡献

[English](CONTRIBUTING.md)

开发环境为 Windows、.NET 10 SDK、Node.js、Python 3，以及 Visual Studio C++ 工具（v145 和 x86 构建工具）。启动器面向 Windows；注入游戏进程的插件始终使用 x86，启动器和 Gateway 可以使用 x64 或 ARM64。

在 Visual Studio 开发者 PowerShell 中，先构建原生运行时，再运行依赖它的托管测试：

```powershell
msbuild native/FantasyTale.Native.sln -p:Configuration=Release -p:Platform=x86 -m
./tools/package-native.ps1
dotnet build FantasyTale.Launcher.slnx -c Release
dotnet test FantasyTale.Launcher.slnx -c Release
```

运行 `native/out/Release` 下生成的原生测试，包括 `fthook_tests.exe`、`nmcogame_tests.exe`、`native_tests.exe` 和 `dump_writer_tests.exe`。警告视为错误，不能通过抑制警告绕过构建失败。

前端单独构建与测试：

```powershell
cd web
npm ci
npm test
npm run type-check
npm run build
npm run build:game-ui
```

真实客户端、外部商店和交互式系统提权检查需要额外条件。请明确记录跳过项，单元测试通过不代表游戏实测完成。不要让本地实验连接生产账号或数据库。

`modules/BubbleFighter` 是单独维护的泡泡战士源码快照，以普通文件纳入，没有嵌套 Git 元数据。独立原生 SDK 已固定版本；部分集成工程仍要求原有共享源码布局。启动器集成以根目录解决方案为入口。替换游戏资源的插件需要使用者自行提供合法取得的客户端原始文件，本快照不包含这些输入。

字体工具根据使用者提供的字体生成资源包。公开源码不包含原始或生成的游戏字体。分发依赖时保留许可证和要求提供的对应源码，参见 [NOTICE.md](NOTICE.md) 和[源码导出说明](docs/source-export.zh.md)。

通过独立分支和拉取请求提交修改，说明问题、修改后的行为、验证结果和限制。保留已保存的插件设置、明确的安装选择和运行时恢复机制。报文布局或内存补丁修改需要来自对应客户端消费路径的证据，引用函数地址时必须注明客户端版本。

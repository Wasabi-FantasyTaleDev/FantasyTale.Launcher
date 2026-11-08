# 已发布版本读取器的压缩包回退验收

使用未修改的旧版发行目录构建，再使用与其 Windows 架构一致的运行时执行：

```powershell
dotnet build tools/verify-launcher-fallback/VerifyLauncherFallback.csproj -c Release `
  -p:BaselinePackage=D:/path/to/unchanged/package
& 'C:/Program Files/dotnet/x64/dotnet.exe' `
  tools/verify-launcher-fallback/bin/Release/net10.0/VerifyLauncherFallback.dll `
  D:/path/to/unchanged/package D:/path/to/fresh/acceptance-output
```

程序核对已加载的 Core、Downloads、SDK 与发行目录的哈希。它直接引用发行版
程序集，不引用当前源码工程；存在归档解压器时，也从该目录复制。即使 SDK
运行在 ARM64 上，x64 发行版仍需使用 x64 运行时执行。

默认模式使用内存 HTTP 处理器。HTTP 与 AList 各运行十个场景：增量成功、完整
ZIP 回退、分卷 ZIP 回退、不同版本、传输损坏、内容变化、缺少文件、多余文件、
重复路径和路径穿越。成功暂存后逐文件独立验证，并在断网状态下恢复。发现更新
不能请求载荷；增量成功时不能请求压缩包元数据或分卷。

小型测试载荷使输出不随发行目录大小增长。成功时写出 `receipt.json`，记录读取器
身份与各场景结果。原始发行目录保持不变，不启动 Launcher、更新助手、游戏或
网络服务。这验证的是回退协议兼容性，不能证明线上压缩包可用、实际安装成功、
前端健康或未经测试的压缩格式可用。

验收真实发布的回退包时，提供预期的本地索引和对应的
`<index>.archive.toml` 文件：

```powershell
& 'C:/Program Files/dotnet/x64/dotnet.exe' `
  tools/verify-launcher-fallback/bin/Release/net10.0/VerifyLauncherFallback.dll `
  --public-archive D:/path/to/unchanged/package D:/candidate/current.toml `
  D:/path/to/fresh/public-acceptance-output
```

此模式保留发行版配置的来源，将发行目录复制到全新输出目录。仅模拟增量载荷
下载失败；更新发现、压缩包选择和压缩包字节均来自真实来源。它核对线上回退
元数据与预期发布是否一致，并完整验证每个下载分卷及展开内容对象的 SHA-256。
下载时的可见任务必须只列出压缩包分卷。旧版本在重开后会按变动目标重建已就绪
任务；离线任务必须恰好列出这些目标，且每个文件均验证完成，并恢复同一版本。加载的
读取器、解压器身份必须与未修改发行目录一致，安装文件与更新源配置保持不变。

磁盘需容纳发行目录副本、压缩包分卷和展开后的唯一内容对象。系统盘空间不足时，
将输出放在数据盘，并显式设置 TEMP/TMP。验收记录会区分真实网络与测试场景。
此模式仍只证明暂存和离线恢复；实际安装、外部更新助手行为和前端显示健康需
分别验收。该模式不会启动 Launcher、更新助手或游戏。

如果这次真实网络传输中断，将同一命令中的 `--public-archive` 改为
`--resume-public-archive`，并继续使用原有输出目录。工具先验证保留的安装目录仍与
未修改的旧版发行目录一致，再由正常下载引擎续传压缩包，并校验最终完整内容。
验收记录包含已有部分文件的字节数和是否复用输出目录；续传后的暂存耗时不代表
从零下载完整压缩包所需的时间。

如需检查已有暂存结果，使用
`--inspect-ready <unchanged-baseline-package> <existing-output>`，无需重新下载。
它检查已加载 Core 的身份，输出安全的离线状态和文件数量；此诊断不会生成
真实网络验收通过记录。

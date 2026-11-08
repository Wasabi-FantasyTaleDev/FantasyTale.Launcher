# 已发布读取器的增量暂存验收

使用未修改的分发包程序集构建工具，并以匹配的 Windows 架构运行：

```powershell
$env:TEMP = 'D:/acceptance/temp'
$env:TMP = $env:TEMP
dotnet build tools/verify-launcher-upgrade/VerifyLauncherUpgrade.csproj -c Release `
  -p:BaselinePackage=D:/baseline/package -o D:/acceptance/verifier
& 'C:/Program Files/dotnet/x64/dotnet.exe' `
  D:/acceptance/verifier/VerifyLauncherUpgrade.dll `
  D:/baseline/package D:/candidate/current.toml D:/acceptance/fresh-output 300 0 32
```

三个必填参数依次为基准分发目录、预期索引的准确文件，以及全新的输出目录。
可选参数依次为暂存验收期限（30 至 1800 秒）、显式网络重试次数（0 至 3 次），
以及预期显示的变更文件数。不确定文件数时省略最后一个参数。
示例中的 32 是特定测试场景的断言，不是软件包或文件数量上限。
运行前应在数据盘创建临时目录。

已加载的 Core 程序集必须与基准包匹配。工具复制该分发目录时排除其生成的事务目录，
使用其中配置的更新源，通过该包实际附带的读取器暂存更新，以完整 SHA-256 独立验证
内容，并在禁用网络后重新打开任务。工具不会启动 Launcher、游戏或外部更新程序，
也不会安装更新。使用本机测试源时，只修改隔离副本的源配置并记录此差异；
这种验收不代表线上更新源验收。

现有读取器不必把未变化的文件再复制到暂存缓存。每个选中的目标都必须满足以下条件之一：
暂存内容与清单匹配，或安装目录中的内容与清单匹配。该独立完整哈希审计使用四个工作任务，
不会代替产品正常流程中的元数据和哈希缓存。验收记录分别统计暂存目标、本地复用目标，
以及任务界面显示的文件数量。完整索引大小不代表下载文件数。如果工具发现请求了基准包中
内容已经相同的对象，就会拒绝验收。

要继续被中断或已经就绪的隔离输出：

```powershell
& 'C:/Program Files/dotnet/x64/dotnet.exe' `
  D:/acceptance/verifier/VerifyLauncherUpgrade.dll --resume-staging `
  D:/baseline/package D:/candidate/current.toml D:/acceptance/existing-output 300 0 32
```

继续验收前，工具会检查安装副本中所有原分发文件是否仍与基准一致，包括更新源配置。
如果副本已经更新或被修改，就会拒绝继续。工具不会重新复制分发包，也不会清除已有的
下载分片或已验证对象。记录中的请求计数只覆盖本次调用；继续验收的耗时不能用作首次完整
下载的速度数据。统计累计请求时，应另行保留此前的传输日志。HTTP 诊断只记录异常类型、
分类和套接字枚举，不记录异常消息或临时存储地址。

请使用能够可靠并发响应的 HTTP 服务。独立测试源应支持普通 GET，并在续传时正确处理
Range 和 Content-Range。暂存、本地文件复用和离线恢复不代表外部替换、自动重启、回滚
或前端实际显示已通过；这些仍需分别进行桌面和更新程序验收。

# WebView 文档策略检查

[English](README.md)

在已安装 WebView2 Runtime 的 Windows 上运行：

```powershell
dotnet run --project tools/webview-policy-test -c Release -r win-arm64
dotnet run --project tools/webview-policy-test -c Release -r win-x64
```

程序在屏幕外的 WebView2 窗口中使用 Overlay 实际的事件策略和共用文档匹配器。
检查真实消息来源、非字符串消息、hash 路由、重载、本地资源、被禁止的文档、弹窗、
不透明来源的设置 iframe，以及垃圾回收后的回调生命周期。失败返回非零退出码；
等待浏览器事件均有截止时间。输出记录进程架构和已安装的浏览器版本。测试页面和
浏览器配置使用新临时目录，浏览器释放后该目录可能短时间仍被占用。不使用真实账号或游戏资源。

Core 测试套件另行检查有歧义的 URI 表达和启动配置。两套测试均不能证明主 Photino
桥接已经受保护；该宿主需要原生事件来源，并针对实际依赖单独进行集成测试。

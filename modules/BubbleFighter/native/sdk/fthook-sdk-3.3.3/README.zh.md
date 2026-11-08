# 原生插件 SDK

[English](README.md)

此分发包含 FtHook 公开 C ABI 头文件、项目许可证及独立的 x86 定位器示例，
不包含运行时实现、游戏客户端、认证数据或内部 C++ 辅助库。插件不需要链接运行时静态库，
宿主通过 `fthook_host` 提供操作。

安装 v145 C++ 工具集和 Windows SDK 后，在 Visual Studio 开发者终端中构建解出的目录：

```powershell
msbuild sample/sample_locator.vcxproj -p:Configuration=Release -p:Platform=Win32
```

结果位于 `sample/out/sample_locator.dll`。示例仅为合成测试镜像注册定位器，
不会启用游戏功能，也不是可以安装的商店包。源码与运行时测试使用的定位器示例相同。
工程只依赖分发内的头文件和 Windows 构建工具，使用 C17、静态 CRT，并将警告视为错误。

`sdk.json` 记录运行时源码版本、ABI 编号及每个白名单载荷文件的长度和 SHA-256。
它是本地完整性清单，不是签名，也不代表已对外发布。

## 兼容性

插件通过模块定义文件导出不带修饰的 `fthook_plugin_main`，读取成员前校验 ABI 和结构大小。
示例要求完整的当前宿主结构。需要兼容旧宿主的插件必须检查所用成员的结束偏移。
即使结构足够大，可选的 overlay 和 companion 指针也可能为空。使用新增操作时应在插件清单中
声明最低运行时版本；SDK 源码版本不等于 ABI 编号。

CRT 对象、异常、STL 容器及分配内存的所有权不得跨模块传递。
取得资源前先登记清理回调；激活失败时必须解除观察者并等待回调结束。
成功加载的插件保持加载直到进程退出，ABI 不提供运行中替换 DLL 的操作。

## 仓库边界

启动器维护 ABI 头文件及运行时。游戏仓库固定 SDK 分发版本，独立发布包清单及二进制。
现有认证与移动插件仍链接内部 C++ 辅助代码，尚未迁移到这个独立 SDK。
迁移验收条件见启动器源码中的 `docs/native-repositories.zh.md`。

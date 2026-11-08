# 经审查的源码导出

[English](source-export.md)

公开源码快照应来自经审查的逐文件清单，而不是开发者工作目录的完整副本。本地恢复归档、
参考输入、凭据、游戏素材和私人调查记录不应进入快照。独立游戏模块需要单独审查；启动器
导出不会递归进入嵌套仓库，也不会复制其历史。

使用 `tools/export-reviewed-source.py` 和私人 JSON 清单。清单指定完整提交 ID，以及每个
允许输出的文件和 SHA-256；未列出的路径不会导出。使用前必须审查清单及文件内容；
导出器不是秘密扫描器，也不能代替再分发权限审查。

```json
{
  "schema": 1,
  "revision": "<完整的已审查提交 ID>",
  "files": [
    { "path": "LICENSE", "sha256": "<提交中文件的 SHA-256>", "action": "copy" },
    { "path": "web/src/preview-levels.json", "sha256": "<{} 加一个 LF 换行的 SHA-256>", "action": "empty-preview" }
  ]
}
```

```powershell
python tools/export-reviewed-source.py --repository . --manifest docs/private/reviewed-source.json --output artifacts/reviewed-source
```

输出目录必须尚不存在。工具流式读取不可变 Git 对象并验证输出哈希，不复制 `.git`、忽略文件、
未跟踪文件、链接或子模块。失败可能留下供检查的部分目录，不得发布该目录，也不得将它
复用为下次导出的目标。未提交改动有意不被导出。不要默认把私人审批记录或完整清单放入公开快照。

排除游戏导出的等级表和图片、私有媒体以及本地提取的字体和音频。保留可选的 `levels.toml`
定义，以便缺少资源的构建仍可加载。预览等级表只有一个明确转换：`empty-preview` 写入
精确的 `{}` 和 LF 换行，保留前端导入但不包含游戏导出数据。数值生命验证器测试自行构造
描述符和文件头，不代表已通过渲染验收，也不授予原始图像的分发许可。依赖许可证、声明和
需要随附的对应源码应与相应二进制一同保留。

必须构建并测试导出快照，不能只验证原工作区。执行托管 Release 构建和测试、原生解决方案
及测试，并在 `web/` 执行 `npm ci`、`npm test`、`npm run type-check`、`npm run build`
和 `npm run build:game-ui`。托管 Hook 测试前应按 README 准备原生运行时。只有私有素材
集成测试可以因素材缺失而跳过；显式指定 `FT_GAME_UI_RESOURCES` 后，素材缺失必须报错。
记录全部集成测试跳过项和产物前置条件。源码构建成功不等于完整便携包或真实客户端验收通过。

便携包验证器在执行分离的更新程序之前，会拒绝链接、本地状态、浏览器配置、本地配置覆盖、
日志、转储和 PDB。正常发布不包含托管调试符号。该文件名检查不能认证任意文件内容，也不
授予媒体分发许可。导出和验证命令不会推送、发布或更改仓库可见性。

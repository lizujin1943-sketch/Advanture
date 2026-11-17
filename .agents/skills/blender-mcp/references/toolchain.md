# Blender 工具链维护

本机应用路径为 `/Applications/Blender.app`，Codex 配置为 `~/.codex/config.toml`。Blender 配置按主次版本位于 `~/Library/Application Support/Blender/<major.minor>/`。

维护时分别检查应用、Python MCP 包、Blender 内 addon 和客户端启动配置；只升级其中一项可能仍加载旧协议。先确认运行进程与未保存文件，再备份程序、用户偏好、addon 和客户端配置。含凭据的配置备份放在用户私有目录，不放进仓库或日志。

官方来源：

- [Blender 发行版](https://www.blender.org/releases/) 与 [官方下载文件](https://download.blender.org/release/)：选择适合机器架构的稳定版，校验 SHA256 和应用签名。
- [MCP for Blender 源码](https://github.com/ahujasid/mcp-for-blender)、[PyPI](https://pypi.org/project/mcp-for-blender/) 和 [更新说明](https://www.mcp-for-blender.com/docs/reference/changelog)。旧包名是 `blender-mcp`，新包名是 `mcp-for-blender`。

更新命令：

```sh
uvx mcp-for-blender@latest update --dry-run
uvx mcp-for-blender@latest update
# 新 Blender 版本没有 addon 时，显式指定目标目录：
uvx mcp-for-blender@latest install-addon --addons-dir '/absolute/path/to/scripts/addons'
```

先读取对应版本的 CLI 行为。`setup --yes` 可能改写多个已检测到的客户端；用户只要求 Codex 时，只迁移该客户端的 Blender 配置并核对其他配置未变。已验证版本可固定，例如 `args = ["mcp-for-blender@2.3.0"]`。升级包和配置不会自动替换已运行的所有 MCP 进程；必要时提示重启客户端，不擅自退出正在进行任务的 Codex。

新版本启动前迁移偏好，并确认 addon 已启用。保留既有遥测与外部服务设置，不因更新而启用付费生成器。新版 addon 默认随 GUI 启动，后台模式不自动监听。

验证至少覆盖：版本和协议握手、场景读取、bpy 执行、一次视图或渲染；用于游戏资产时补动画与导出回读，并用新版只读打开一个现有 `.blend`。所有验证文件放在临时产物目录。若只更新工具链，不运行与之无关的完整游戏规则测试。

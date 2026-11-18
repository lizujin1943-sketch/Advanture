# Blender 工具链更新记录

2026-11-17 在本机完成安装与验证。

| 项目 | 原状态 | 更新后 |
|---|---|---|
| Blender（Apple Silicon） | 5.2.1 LTS | 5.3.0 稳定版，build `02f0899d78e3` |
| Codex 启动配置 | `uvx blender-mcp` | `uvx mcp-for-blender@2.3.0` |
| Blender addon | 1.6 / protocol 5 | 2.0 / protocol 16；5.2、5.3 目录均已更新 |
| 项目 Blender skill | 未安装 | `.agents/skills/blender-mcp/`，含制作、兼容性与维护指引 |

版本来源：[Blender 5.3](https://www.blender.org/releases/5-3/)、[官方下载目录](https://download.blender.org/release/Blender5.3/)、[MCP for Blender PyPI](https://pypi.org/project/mcp-for-blender/)、[官方仓库](https://github.com/ahujasid/mcp-for-blender)。没有额外安装第二套 MCP 客户端插件。

## 安装与使用

- 当前程序为 `/Applications/Blender.app`，已启动且 addon 自动连接。
- 5.2 用户偏好已迁移到 `~/Library/Application Support/Blender/5.3/`；遥测维持关闭，外部资产/生成服务维持关闭。
- Codex 配置只修改了 Blender 的启动参数，其余配置通过解析比较确认未变。
- 当前会话 MCP 已验证可用。Codex 下一次重启会按更新后的配置启动服务端；新项目 skill 下一轮对话可发现。
- MCP 2.3.0 带七份专题指南，但未注册 `get_guide` 工具。skill 提供按需读取包内指南的方法，无需修改官方包。

## 验证范围

安装包 SHA256 为 `2d802b54766741e612cead50b03ebbc8eb64dadc71eda932573d256b8a784720`，与官方哈希一致；`codesign --verify --deep --strict` 和 macOS Gatekeeper 验证通过，来源为 Notarized Developer ID。

已验证：

- GUI 中 MCP 读取场景、执行 bpy、截图；Blender 5.3.0 与 protocol 16 握手成功。
- 独立启动固定为 2.3.0 的 MCP stdio 服务，完成初始化、工具发现和实际 addon 状态调用。
- 在独立后台进程中创建材质、骨架、权重与 slotted action，检查实际蒙皮变形，完成渲染和 FBX 导出回读；回读保留 1 个网格、1 个骨架、2 根骨骼及 1 个动作。
- 新版只读打开 `source-assets/creatures/eldritch-redesign/CreatureDesignWorkbench.blend`，读取 2 个场景、68 个对象与 12 个图像数据块；原文件前后 SHA256 一致。
- 新 skill 的结构验证与官方包内指南读取通过。

验证脚本、日志、渲染、FBX 和 JSON 报告位于 `artifacts/blender-update-20261117/`。这次是工具链验证，没有重新制作怪物或验证全部历史 `.blend`；未运行 Unity 接入检查和完整游戏规则测试，因为没有修改游戏代码、权威状态或网络协议。

## 备份与回退

- 旧程序：仓库内 `.build/blender-update-20261117/Blender-5.2.1.app`。
- 安装包：`.build/blender-update-20261117/blender-5.3.0-macos-arm64.dmg`。
- 原始 Codex 配置和 Blender 5.2 偏好：`~/.codex/backups/blender-20261117/`，配置备份不进入仓库。
- addon 更新器另保留原插件为安装目录中的 `blender_mcp.py.bak`。

若需回退，先保存并关闭 Blender，将当前 5.3 程序移到备份位置，再将旧程序恢复至 `/Applications/Blender.app`。需要恢复旧 addon 时使用私有备份中的 `Blender-5.2`；Codex 仅恢复 Blender 配置段，避免覆盖更新之后其他工具的配置。不要用 5.2 直接覆盖保存已经由 5.3 修改的源文件。

# 项目工作目录

本项目的正式仓库与后续产出目录是：

`/Users/lizujin/Documents/project/git_project/Advanture`

- 所有代码、文档、脚本、测试结果和构建产物都在此目录及其子目录中维护。
- 旧目录 `/Users/lizujin/Documents/project/game/Advanture` 已迁移，仅保留定位指引；不要在旧目录继续开发。
- 如果会话仍从旧目录启动，后续工具调用显式使用这里的绝对路径作为工作目录。
- 帧同步核心保持纯 C#，独立于 Unity、Steam 和具体玩法；游戏示例放在 `samples/`。
- 完整验证入口为 `./scripts/verify.sh`；当前实现和验证范围见 `docs/stage-a-implementation.md` 、`docs/stage-b-implementation.md` 、`docs/stage-e-implementation.md` 与最新的 `docs/stage-c-d-implementation.md`；文件保存约定见 `docs/stage-d-storage.md`。
- `.build/`、`bin/`、`obj/` 和 `artifacts/` 是本地生成内容，不提交到 Git。

## Unity MCP

- 本仓库 Unity 工程是 `samples/TacticalGame.Unity`，不是仓库根目录。
- 使用 Unity MCP 前先读取 `mcpforunity://instances`，选择该工程对应的实例，再读取 `mcpforunity://project/info` 核实工程绝对路径；不要仅凭默认实例操作。
- 只操作 `/Users/lizujin/Documents/project/git_project/Advanture/samples/TacticalGame.Unity`。工程未连接时先建立连接，不操作其他已打开工程。
- 连接与安装说明见 `samples/TacticalGame.Unity/README.md` 的 Unity MCP 一节。

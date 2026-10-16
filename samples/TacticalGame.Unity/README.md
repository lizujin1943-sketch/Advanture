# 七号转运站：Unity 双人联机试玩

现已接入独立帧同步框架。两个客户端可在同一地图里移动、转向，并看到对方。房主程序同时运行服务器与自己的客户端；原第一人称本地场景保留。

## 最快体验

本机已经构建了 [macOS 联机程序](../../artifacts/tactical-network/Yard07Network.app)。在仓库根目录运行：

```sh
cd /Users/lizujin/Documents/project/git_project/Advanture
python3 scripts/run-yard-pair.py
```

两个窗口会自动创建、加入同一个房间。点击窗口取得角色控制，Esc 释放鼠标后可切换窗口。每次测试自动建立 `artifacts/tactical-network/manual-日期-时间-随机编号/`，其中包含两个程序日志和 `diagnostics/`。退出、断线和不同步会自动留下记录，使用方法见 [自动诊断说明](../../docs/session-diagnostics.md)。

也可双击程序使用房间菜单。一个进程选择“创建双人房间”；另一个选择“加入好友房间”。同机地址为 `127.0.0.1`，局域网填写房主电脑的 IPv4 地址，两端使用相同端口，默认 `7777`。等待双方就绪后才开局。

| 操作 | 按键 |
| --- | --- |
| 移动 | WASD |
| 加速 | Shift |
| 转向、抬头低头 | 鼠标 |
| AK-47 连射 / 换弹 | 左键 / R |
| 释放控制 / 恢复控制 | Esc / 点击画面 |
| 离开本局 | Esc 后点击“离开房间” |
| 显示同步诊断 | F3 |

地图使用二维平面规则，已支持 AK-47 射击、扣血、死亡与重生，没有跳跃、坡面或玩家互撞。此房间示例固定两人、面向可信本机或局域网；尚未接 Steam、中途加入或游戏存档菜单。

## 在 Unity 中打开

1. 先在仓库根目录运行 `./scripts/sync-unity-libraries.sh`，生成框架和示例的程序集。
2. 用 **Unity 2022.3.62f1** 打开本目录 `samples/TacticalGame.Unity`，不是仓库根目录。
3. 打开 `Assets/TacticalGame/Scenes/Yard07Network.unity`，点击 Play。编辑器可以与一个独立程序联机。
4. 工程的 **Active Input Handling 为 Both**。角色使用新版 Input System；当前房间菜单使用 IMGUI，需要旧输入处理。若修改该设置，按 Unity 提示重启编辑器。

构建列表默认启用联机场景。构建产物放在 `artifacts/tactical-network/`。原 `Assets/TacticalGame/Scenes/Yard07.unity` 是单机版，可单独打开 Play，不经过联网流程；原 [Yard07.app](../../artifacts/tactical-unity/Yard07.app) 也仍保留。

## 代码阅读与分层

先读 [双客户端接入实现说明](../../docs/yard-network-implementation.md)，其中按“按下 W 后发生什么”解释输入冻结、网络确认、预测回滚和画面更新，并列出源文件阅读顺序。

- `framework/`：通用纯 C# 同步框架，没有地图、角色或 Unity 依赖。
- `samples/TacticalGame.Simulation/`：纯 C# 整数移动、碰撞、朝向和完整状态。
- `samples/TacticalGame.Network/`：纯 C# 双人房间、开局与会话驱动。
- `Assets/TacticalGame/Network/`：Unity 键鼠、相机、模型和菜单。
- `adapters/Unity/`：通用框架 DLL 的本地包入口。

联机场景不运行 `FirstPersonMotor` 或 `CharacterController`；它们只用于保留的单机场景。地图碰撞由独立模拟计算，Unity 只展示结果。修改框架或两个纯 C# 示例后，要重新运行程序集同步脚本，不能直接修改生成的 DLL。

## 地图资源和重建

源模型来自 [TacticalMap](../TacticalMap/README.md)，Unity FBX 副本与材质在 `Assets/TacticalGame/Art/`。

- **Tactical Game → Create or Rebuild Yard 07**：重新生成原本地场景、材质和角色预制体。
- **Tactical Game → Create or Rebuild Network Yard 07**：读取本地场景的碰撞盒，导出整数地图，生成联机场景、房间入口和相机。

重建菜单会先提示保存当前场景，可以取消。生成内容会覆盖目标场景；需要保留的手工改动应另存。地图调整后先保存本地场景，再重建联机场景，并为双方提供相同构建。

## 验证

纯 C# 试玩测试：`./scripts/verify-tactical.sh`。完整框架与示例验收：`./scripts/verify.sh`。这些测试不依赖 Unity。

Unity 菜单 **Window → General → Test Runner → EditMode → Run All** 运行 22 项测试，包括运动、场景、输入、地图高度、诊断与武器表现去重检查。

构建 Development Build 后运行：

```sh
python3 scripts/run-yard-pair.py --smoke
```

脚本启动两个独立进程，提供移动和转向输入，检查三端同帧状态摘要、远端移动/转向及画面显示，并保存报告、检查点和截图后退出。普通试玩不启用自动输入。当前 macOS 实测结果与尚未覆盖的范围见 [验证记录](VERIFICATION.md)。

## Unity MCP


工程通过 Package Manager 安装 `com.coplaydev.unity-mcp`，固定为官方 `v10.3.0`。Unity 插件按工程安装；Codex 连接配置在仓库根目录 `.codex/config.toml`，复用本机 HTTP 服务 `http://127.0.0.1:8080/mcp`。项目配置仅在 Codex 信任该仓库时加载。

1. 打开本工程，等待 Package Manager 下载依赖并完成编译。
2. 打开 **Window → MCP for Unity**，使用 HTTP 传输，服务地址为 `http://127.0.0.1:8080`。如服务未运行，点击 **Start Server**，再启动 Unity 连接；依赖 `uv` 和 Python 3.10+。
3. 在 Codex 的 MCP 设置中重启 `unityMCP` 连接，使工具重新加载。
4. 操作前读取 `mcpforunity://instances`，选择 `TacticalGame.Unity@实例哈希`，再读取 `mcpforunity://project/info`，确认路径为本仓库下的 `samples/TacticalGame.Unity`。同一个服务可以连接多个编辑器，项目配置本身不会自动锁定目标实例。

关闭 Unity 后无法操作场景；重新打开后需要确认实例连接状态。

参考：[MCP for Unity 官方说明](https://github.com/CoplayDev/unity-mcp)、[Codex MCP 配置](https://developers.openai.com/codex/mcp/)。


## AK-47 对战

默认装备 AK-47。左键连射，R 换弹；30 发弹匣、90 发备弹、100 点生命、每次命中 25 点伤害。死亡后 3 秒自动重生。射线命中与高低障碍遮挡由独立游戏逻辑计算，两端显示持枪、枪声、闪光、生命、弹药和战绩。

运行 `python3 scripts/run-yard-pair.py --combat` 自动验证双方开火、换弹、击杀与重生，并比较服务器和两端的最终状态。游戏/地图格式已升级，双方需要新构建，旧试玩快照不自动迁移。

实现与阅读顺序见 [AK-47 实现说明](../../docs/ak47-implementation.md)。

# 游戏存档接入

> 使用 executing-plans 与 test-driven-development 流程实施。

**目标：** 房主可随时请求保存，等待全队收杆、结束钩救和救援后保存确认进度，通过大厅继续游戏；自动存档与手动存档分开，保存失败不破坏旧档。

**架构：** 复用 FrameSync 的 SaveCodec、FileSaveStore 和 CheckpointRestart；游戏外壳原子保存检查点、保存时间与席位归属。玩家本地持久身份用于恢复原席位，新会话更换网络身份与输入代数。Unity 仅负责菜单、提示和定时触发。

**技术栈：** 纯 C# netstandard2.1、现有真实 UDP 联机、Unity IMGUI。

**规格和全局约束：** 遵守 AGENTS.md、docs/agent-workflow.md、docs/stage-d-storage.md 和游戏设计文档。只保存房主确认态。用户于本轮明确选择安全状态存档：正在出钩、拉鱼、钩救、近战动作或救援时等待；不强制取消当前动作。读档无离线推进，不回血、不重置敌人或已结算交易。恢复默认等待保存时在线的原成员，提前开始视为未到玩家暂时离线。好友持有本机身份，自动恢复自己的角色；其他人不能占用已有归属的席位。旧版本或损坏文件在创建房间前拒绝。

**审查重点：** 战斗中保存、完整状态和累计时间、经济幂等、四人一致性、原席位恢复、输入清理、磁盘失败、错误恢复不覆盖运行状态。

- [x] 1. 为原子存档、损坏拒绝和战斗恢复编写失败测试。
- [x] 2. 实现有界游戏存档格式、席位身份和房主恢复入口。
- [x] 3. 接入大厅继续游戏、菜单保存/保存并离开、120 秒自动保存及结果提示。
- [x] 4. 验证四客户端恢复与奖励幂等，运行全量 ./scripts/verify.sh。
- [x] 5. 同步 Unity 运行库，编译并验证菜单与实际保存/继续流程，补齐设计与实现文档。

## 验证收尾（2026-10-29）

- `./scripts/verify.sh` 退出码为 0；HookIsland 398/398 项通过，包含 8 项存档专用与真实 UDP 恢复测试；其余框架、传输、背包、角色、落水、敌人、近战、复活和重开回归均通过。
- Unity 最终运行库同步后，`SaveSceneTests`、`RecoverySceneTests` 和 `FishingCameraViewTests` 共 25/25 项通过，无失败或跳过；控制台无错误。
- `HookPlatform` 菜单“保存并离开 → 大厅 → 继续 · 手动存档”实际操作通过。检查结束后退出 Play Mode，没有向正式场景添加自动验证组件。
- 保存和恢复使用已确认状态；正在出钩、牵引、钩救、救援、近战或击退时请求等待，成功提交后才离房。存档不推进离线时间，也不清空既有交易回执。
- 日志与结果：`artifacts/game-saves/verify.log`、`full-verification-result.json`、`unity-editmode-results.json`；实现边界见 `docs/game-save-implementation.md`。

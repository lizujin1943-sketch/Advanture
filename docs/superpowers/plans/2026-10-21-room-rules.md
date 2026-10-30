# 房主友方拖拽开关实施计划

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 房主能在游戏菜单中关闭/开启岸上友方拖拽，落水救援不受影响，所有端和存读档保持一致。

**Architecture:** 房间规则作为纯 C# 状态保存；槽位 0 的带序号设置命令通过正常帧输入交付。命令使用明确目标值，不使用可能被重复翻转的 toggle。Unity 只显示确认状态并排队请求；重开由服务器使用上局确认的规则创建新局初态，再走既有全员快照确认流程。

**Tech Stack:** C# netstandard2.1、.NET 8、Unity、现有 UDP 房间与确认帧。

**Spec:** `docs/hook-island-game-design.md` 的“整蛊怎么好笑，又不毁掉这一局”和“游回岸边与队友救援”；完整目标见 `docs/gameplay-gap-audit.md`。

## Global Constraints

- 默认允许友方拖拽；仅房主（槽位 0）能修改，死亡/被拉状态不剥夺房主的房间管理能力。
- 关闭只限制岸上玩家拖拽；普通鱼/食人鱼合钓与抢钓、落水救援、敌方击退不变。
- 关闭当帧先解除已有岸上拖拽，再处理玩家动作和钩索命中；尚在飞行的钩子也不得再挂住岸上队友。
- 同一请求重发只生效一次；旧序号不能覆盖新设置。界面收到确认回执前显示同步中。
- 规则及最新请求号保存进快照。房间重开保留开关，清除上一局请求号与本地待发意图；新房间默认开启。
- 纯 C#、网络、输入/表现分开；不修改通用同步核心；测试驱动必须显式开启且排除发布构建。

## Review Focus

- 关闭时已有拖拽、在途钩子和当帧新出钩都不能再让岸上玩家移动。
- 近期被拉、无圈、零耐力、倒地落水者仍可救援，真正登岸时释放，不能把开关当敌方伤害免疫。
- 非房主伪造更大序号不能改变设置或消耗房主的请求号。
- 重发/乱序/读档后的旧命令不能翻转新设置，其他库存/复活命令不能吞掉待发规则请求。
- 重开保留确认值，所有客户端从同一第零帧看到设置，不通过“下一帧补改”形成暂时开放窗口。

### Task 1: 确定性规则和命令

**Files:** 新增 `RoomRulesState.cs`、`RoomRulesCommand.cs`、`RoomRulesStateCodec.cs`、`HookGame.RoomRules.cs`；修改 `HookState.cs`、`HookInput.cs`、`HookGame.cs`；新增 `tools/HookIsland.Tests/RoomRulesTests.cs`。

**Interfaces:** `HookState.RoomRules`（`FriendlyPranksEnabled`、`LastRequest`）；`HookInput.RoomRules`（`Enabled`、`RequestId`）；`HookGame.CreateModule(bool initialFriendlyPranks = true)` 用于房间初态。

- [x] 写入关闭后不能钩岸上玩家、关闭立即释放已有拖拽、仅房主可修改的实际模拟测试；先用新增命令的规范字节编码使旧实现明确拒绝，确认 RED。
- [x] 实现独立设置命令、确认状态、去重、释放与救援例外；输入版本 8，规则/状态/房间版本 16。
- [x] 补充在途命中、同帧关闭与出钩、重新开启、零耐力/倒地水中救援、读档旧请求和非法编码测试。
- [x] 运行 `dotnet run --project tools/HookIsland.Tests -c Release -- --simulation-only`，要求全通过。

### Task 2: 网络请求、重开与菜单入口

**Files:** 新增 `samples/HookIsland.Network/Commands/RoomRulesRequestBuffer.cs`；修改 `IslandHost.cs`、`RoomProtocol.cs`、`proto/hook-island-room.proto`、`IslandFishingController.cs`、`FishingOverlay.cs`；新增规则网络测试及显式开发验收入口。

**Interfaces:** `RoomRulesRequestBuffer.TryQueue(RoomRulesState, bool)`、`Observe(RoomRulesState)`、`Compose(HookInput)`、`Reset()`；`IslandFishingController.RequestFriendlyPranks(bool)`、`RoomRulesPending`。规则命令在其他输入组合后加入，确认态更新后清理。

- [x] 先测试请求在未确认帧重发且保留其他操作、确认后停止、单人及双人/四人规则同步、房间重开保留设置，确认缺失行为的 RED。
- [x] 实现请求缓冲与房主菜单按钮，队友只读状态，提示落水救援始终可用；关闭整蛊时瞄准辅助不再吸附岸上队友。
- [x] 新局服务器从上局确认设置初始化，快照照常发给各端；离开/重开清空旧请求。
- [x] 真实 UDP 验证禁用/启用/中途取消与救援，比较完整 checkpoint；开发原生客户端验证相同流程与菜单可见性。

### Task 3: 集成、复核和记录

- [x] 同步 Unity 库，执行编辑器测试与开发/发布构建；确认验收组件不进入发布版本。
- [x] 执行 `./scripts/verify.sh`，检查全部输出；执行一次新的独立最终复核，修复有效问题并增加对应回归。
- [x] 更新玩法设计、缺口清单与实施说明，记录验证范围、初值决策和剩余总目标。

## 执行记录

- 使用现有正式共享工作目录 `f_1`；已有未提交功能全部保留，不自动创建脱离其未提交依赖的新工作树或打包提交。代码基线及证据写入 `artifacts/room-rules/`。
- 用户已授权实现文档中的缺失玩法，沿用现有技术栈和当前会话执行，不重复请求批准。
- 初值决策：默认开启；关闭立即结束岸上拖拽；同房间重开保留设置，新房间重置开启。若用户需要改成仅大厅设置，菜单和请求入口可调整，核心规则不变。
- Pre-flight: Task 1 的状态和命令由 Task 2 缓冲/菜单消费；重开只传递确认的布尔值，序号归零，避免旧局请求污染新局。
- Task 1: complete；初始 5 项新增字节命令用例在旧实现中全部失败，接入后通过；补齐后模拟 122/122 通过（simulation.log）。专项增加普通输入延迟和 UDP 后 23/23 通过（targeted.log）。
- Task 2: 请求缓冲的初次 RED 是尚无该类型的编译失败，不能代替行为复现；单人／四人重开保留关闭状态另有实际 0/2 行为失败（restart-red.log），修复初始化后 2/2 通过（restart-green.log）。
- Task 2: Ruling: 同房间重开由模块工厂接收已确认布尔值，序号归零，再分发完整初态快照 — 避免第一帧短暂重新开放整蛊，客户端恢复覆盖默认值 — 如果将来新增不恢复 checkpoint 的回放入口，必须同样传递初态规则。
- Task 3: Unity 54/54 通过，开发／发布构建均零错误、零警告；RoomRules 和 CooperativeFishing 自动验收类型仅存在于开发程序集（unity-editor-tests.json、release-isolation.json）。原生双人第 600 帧关闭，全部端通过流程检查；房主和队友菜单截图已目视确认。
- Final review: 一次独立只读复核发现双方在水中、路径穿过平台转角时可越过岸边继续落水。新增 room_rules_water_to_water_pull_stops_at_first_platform_landing 先实际失败 0/1，再将首次登岸停止条件扩展到关闭整蛊；模拟 132/132 通过（review-red.log、review-green.log）。初次隔离构建的 dotnet run 未执行新输出、只显示 0/0，不计入 RED；之后显式运行新生成的 DLL 完成复现与修复验证。
- Final: Ruling: 可上岸鱼／Boss 继续延期；跨机器／互联网／丢包压力不列入本机 UDP 已验收范围；基线前其他未提交功能及身份模型保持原范围 — 分别遵循用户边界、实际证据和共享工作区约束 — 后续上岸玩法、新网络环境和身份认证仍需专门设计与验收。
- Final review 无延期 minor。复核记录见 artifacts/room-rules/final-review.md；修复后重新跑完整钓鱼回归、Unity 和独立客户端，不再次派发同一复核。
- Task 2: complete；最终真实 UDP 1／2／4 人规则场景通过，原生三组均在第 600 帧结束，各端完整 checkpoint 逐字节相同。菜单请求通过确认队列交付、队友越权请求被拒绝，房主／客机截图已检查。
- Task 3: complete；全量仓库回归退出码 0；转角修复后最终钓鱼 148/148（规则 24、合钓 39），Unity 54/54，开发／发布构建成功且自动验收类型隔离；原生合钓三组及房间规则三组均通过。证据见 verification-summary.json。总目标尚有宝箱、委托、终局等未完成项，未标记总目标完成。

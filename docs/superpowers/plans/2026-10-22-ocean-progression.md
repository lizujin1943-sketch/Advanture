# 海域进度与恢复事件实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 委托进度开放新鱼群，长期无鱼获与连续处理怪物后出现可实际捕获的普通鱼和短暂恢复窗口，所有参与者状态一致。

**Architecture:** 不可变 OceanConfig/State 加 OceanSystem 管理调度；OceanRules 管生成与状态查询，OceanPlacement 管确定性岸边候选；HookGame 只编排帧序与事务。Unity 导出同一配置并读取确认态。

**Tech Stack:** 纯 C# netstandard2.1、.NET 8、Unity 2022.3、现有真实 UDP。

**Spec:** docs/superpowers/specs/2026-10-22-ocean-progression-design.md

## Global Constraints

- 基础阶段为 min(2,已完成委托数)，0／1／2 解锁；正式 24 个基础群加 2 个临时槽，合计不得超过 32。
- 默认无鱼获 2700 帧，压力窗口 900 帧／3 次，恢复 600 帧，恢复间隔 1800 帧，临时鱼存活 900 帧；所有计时为 30 Hz。
- 普通近圈永不关闭；捕获是唯一推进奖励来源，不注入金币／委托，不覆盖挂钩／挣脱中的临时鱼。
- 规则／状态／房间 19，输入 9，地图 HIMD，状态尾 OCN1；旧版本明确拒绝。
- 禁用配置使用旧行为；正式配置启用要求平台和委托，基础解锁最高档不能超过委托数。
- 用户明确延后 Boss 上岸；终局、商人和重连保留在总目标后续项。

## Review Focus

- 同帧成功捕获应取消无鱼获触发；失败捕获不能取消触发、移除临时鱼或推进阶段。
- 临时波次到期遇到合钓、反向抢钓、挣脱和扩建时，不能吞鱼、复制奖励或在地板内生成新目标。
- 恢复暂停不能删除已有水球、取消前摇或重施攻击；冷却应从原剩余帧续接。
- 恢复／新局和存档损坏：阶段机会不可重发，动态 home 与计时保留，越界或不规范未启用状态拒绝。
- 极端地块布局、全员无行动能力、找不到水域和满临时槽必须有界处理；没有新鱼也不能直接发资源。

### Task 1: 确定性鱼群与事件

**Files:** 创建 samples/HookIsland.Simulation/Data/Configuration/OceanConfig.cs、OceanState.cs、OceanRules.cs、OceanPlacement.cs、OceanSystem.cs、OceanStateCodec.cs、HookGame.Ocean.cs；修改 FishingEquipment.cs、HookState.cs、HookGame.cs、HookGame.Cooperative.cs、EnemySystem.cs、IslandMap.cs、RoomProtocol.cs、proto/hook-island-room.proto；创建 tools/HookIsland.Tests/OceanProgressionTests.cs 并接入 Program.cs。

**Interfaces:**
- `FishSchoolProfile(FishRarity minRarity, FishRarity maxRarity, int minWeightGrams, int maxWeightGrams, int maxGrade=1)`。
- `OceanConfig(IEnumerable<int>? unlocks=null,int visitSlots=2,int noCatchTicks=2700,int pressureWindowTicks=900,int pressureCount=3,int recoveryTicks=600,int recoveryCooldownTicks=1800,int visitTicks=900,FishSchoolProfile? opening=null,FishSchoolProfile? middle=null,FishSchoolProfile? far=null,FishSchoolProfile? recoveryVisit=null,FishSchoolProfile? middleVisit=null,FishSchoolProfile? rareVisit=null,bool enabled=true)`，及 `Disabled`。
- `FishingConfig.Ocean` 默认禁用；`HookState.Ocean` 默认规范空状态；`OceanState` 只读捕获所有计时与 `OceanVisit` 列表。
- `OceanRules.Home(IslandMap,HookState,int)`、`Available(HookState,int)`、`CreateMap(IslandMap,FishingConfig)` 供表现和输入驱动；生成函数返回独立 Fish，不改调度状态。
- `OceanSystem(config,state?)`，`Capture()`，`BeforeStep(map,tiles,fish,now)`，`Advance(map,tiles,players,fish,completed,EnemyState,now)`，`RecordCatch(fishId,now)`，`EndVisit(fishId)`，`AllowNewAttacks(now)`。访问配置 disabled 时不创建系统。
- `EnemySystem.Advance(...,bool allowNewAttacks=true)`；冷却计时及发起前摇受控，已有前摇与水球不受控。

- [x] 编写 `ocean_unlocks_by_commissions_not_time`、`ocean_stall_spawns_real_fish_without_rewards`、`ocean_recovery_preserves_windup_and_projectiles`；提供可编译空接口后运行隔离测试。Expected: 因尚无解锁／恢复行为失败，不以编译错误代替规则 RED。
- [x] 实现生成档、配置和状态、临时鱼放置与调度、捕获成功通知、敌人冷却门控及序列化；默认 near common 1～3 kg／等级1，middle common～rare 2～6 kg／最多3，far rare～legendary 2～6 kg／最多3。Expected: 首三项转绿。
- [x] 增补 Review Focus：同帧成功／失败捕获、食人鱼重置计时、压力阈值与窗口／间隔、合钓及抢钓、到期挂线／挣脱、扩建阻塞、无岸上活人、全部槽占用、世界边缘与位置失败、损坏快照／配置、保存继续与禁用回归。Expected: 所有 ocean_ 用例通过。
- [x] 运行完整模拟与真实 UDP 既有回归。Expected: 退出 0；保留 RED/GREEN 命令、输出和任务基线。

### Task 2: 配置、HUD 与真实输入

**Files:** 创建 Unity Runtime/Data/Settings/OceanSettings.cs、OceanSmokePilot.cs、OceanSmokeDriver.cs、Editor/OceanSceneTests.cs；修改 FishingSettings.cs、PlatformSettings.cs、HookPlatformSceneBuilder.cs、CommissionHud.cs、HudLayout.cs、FishingPresentation.cs、IslandFishingController.cs、PlatformSmokeDriver.cs；创建 tools/HookIsland.Tests/OceanProgressionNetworkTests.cs；修改测试工程、Program.cs、scripts/run-island-party.py、正式配置资产和 PlatformMap.bytes。

**Interfaces:** `OceanSettings.CreateConfig()`；`OceanSmokePilot.CreateMap(bool defaults=false)`、`Sample(map,state,slot,tick)`、`Complete(state)`；`--ocean` 只显式启用开发驱动，其他驱动互斥。`OceanRules.Home` 用于鱼方向和水纹，锁定与空闲槽显示为规范 Waiting，不可命中。

- [x] 写配置禁用／正式导出、面板扩展与指针阻挡检查；写 1／2／4 人普通输入 0／3／6 延迟测试，先失败。Expected: 无配置／事件驱动不能完成流程。
- [x] 实现绑定、导出和只读状态行，配置处写清初值；普通输入 pilot 靠真实捕鱼推进两档并等待一次恢复后捕获临时鱼。Expected: 不注入进度／奖励，既有委托流程仍能完成。
- [x] 运行同样场景的真实 UDP，各端事件序号、阶段、临时鱼及完整 checkpoint 相同；默认正式数值另做近岸可钩及循环验收。Expected: 1／2／4 人与默认值检查通过。
- [x] 同步程序集、正确 Unity 工程导出、运行 Editor 检查、开发／发布构建，原生 1／2／4 人验收。Expected: 画面可读、日志无异常、完整状态一致、正式发布无新验收类型。

### Task 3: 全量回归与交付

- [x] `./scripts/verify.sh` 完整回归，新局测试追加海域状态清零断言。Expected: 退出 0。
- [x] 一次独立最终代码复核，重要问题最多一轮 RED→GREEN 修复，无二次复核；对 Declined to judge 逐条裁定。
- [x] 更新主设计、实施说明、缺口清单与验证汇总；只关闭海域阶段，总目标保留未完成项。

## 执行记录与裁定

- Task 1 RED: `dotnet artifacts/ocean-progression/build/bin/HookIsland.Tests/release/HookIsland.Tests.dll --simulation-only --filter=ocean_` → 0/3；分别因时间提前解锁、未生成恢复鱼、冷却未暂停失败，见 red.log。先修正测试 fixture 缺少玩家人数的编译参数，再取得规则 RED。
- Ruling: 配置增加 `pauseMonster=true` 开关，默认规则不变；使前述可选偏好可以仅改配置实现。Map 构造即校验完整槽数，CreateMap 的输入为未附加临时槽的基础布局，测试 fixture 相应传入不带 Ocean 的 basis。

- 前一目标轮次：progress。共享委托完成：28 项专项、钓鱼/UDP 212、Unity 63、原生 1／2／4 人、完整 verify.sh 及独立复核均通过；不重复实施合钓。
- Builder check: ocean_progression_design_check 返回 nothing important left。
- Ruling: 沿用持续实施授权，异步问题属于可选偏好，不把未回复视为确认；采用文档已明示的可配置初值 — 现有主设计已要求恢复窗口与普通鱼补充 — 若用户选择不干预水怪，用配置关闭暂停，鱼群方案保留。
- Ruling: 沿用已有工作区集成和计划兼 ledger 的方式，在 artifacts/ocean-progression/baseline.json 保存独立任务基线；不整体提交、重置或清理其他任务文件 — 正式 Unity 工程与现有大规模未提交工作需保留 — 最终复核必须按任务基线而非 HEAD。
- Pre-flight Task 1→2: 配置导出要求地图基础位置数等于 unlocks.Count 并在尾部加 VisitSlots；运行和表现共用 OceanRules.Home，不能各自决定临时位置。禁用配置不加槽、不改生成。无接口冲突。
- Pre-flight Task 1→3: OCN1 必须最后编码／解码并在 Capture/Restore 传递；RecordCatch 只能在库存成功后提交。新局由初始化清零，重开测试用普通输入取得可见事件状态。
- Plan self-review: 各设计段落已映射到三个任务，Review Focus 均在 Task 1 补充用例和 Task 2/3 集成验收中；使用有界岸边扫描，不引入通用帧同步新依赖。

- Task 1 GREEN: 首三项 3/3，边界增补 24/24；`boundaries-red.log` 曾为 22/23，发现恢复窗口中的打断冷却多保留一帧，修正后通过。`core-regression.log` 全部模拟及既有 UDP 236/236，进程退出 0。第一次非提权运行 UDP 受本机沙盒 socket 限制，获准运行后全部通过。
- Task 2 RED/GREEN: 普通输入空驱动 `input-red.log` 0/1；Unity 未绑定配置 `unity-red.json` 0/1。实现后 1／2／4 人 × 0／3／6 帧延迟 9/9、正式鱼群和计时默认值 1/1（第 7255 帧；该受控场景关闭敌人，并非正式战斗平衡验收）。真实 UDP 1／2／4 人 3/3，完整 checkpoint 逐字节一致。
- Ruling: Task 1 全量 UDP 运行期间先准备 Task 2 的普通输入与配置用例；运行中的 Task 1 使用独立不可变 build 目录，没有替换其程序集。
- Task 2 集成修正: UDP 驱动最初在客户端初态恢复前调用 InspectState，`udp-harness-red.log` 0/3；增加 LocalParticipant 就绪检查后 3/3。Unity 首次全套 61/68，发现菜单写入地图后没有刷新已加载 TextAsset；导出后显式 ImportAsset，68/68 转绿。
- Task 1 追加存档边界: 平台覆盖临时鱼原 home 时，已挂钩状态仍可完整恢复，下一帧保留钓线；`expanded-home-restore.log` 1/1。该用例确认历史位置不能按当前水域生成条件拒绝。

- Task 2 complete: Unity 68/68；开发／发布严格构建零错误／警告，发布隔离通过；原生 1／2／4 人第 1015／931／1001 帧完成，均观察两档及恢复鱼捕获，四人场景实际四根线。新局 1／4 人 Ocean 清零 2/2。
- Ruling: 最终只读代码复核与已启动的全仓库 UDP 回归重叠进行 — 两者独立且生产源码已固定，回归结束前不宣布阶段完成 — 若复核需要修复则按 RED→GREEN 重新验证受影响程序集和全套测试。
- 画面观察待整体体验项处理：玩家紧贴时，既有头顶耐力/主辅提示相互覆盖；本次海域 HUD 与鱼位置可读，四条线清楚。

- Final review: ocean_final_review（gpt-6-astra / high，只读，独立上下文）未发现 Critical / Important / Minor，无修复回合、无二次复核。记录见 artifacts/ocean-progression/final-review.md。
- Final: Ruling: Boss／蟹将上岸 — 沿用用户明确延后，现有等级不代表上岸能力 — 若以后启用须另设计状态与结算。
- Final: Ruling: 终局、后期商人、晚加入／重连 — 保留为总目标未完成项，本阶段不代替 — 若误当完成将缺少完整局流程，缺口清单持续标明。
- Final: Ruling: 正式敌人下长期平衡 — 受控默认数值验收仅证明可达、可捕获，完整战斗与经济在全流程项验收 — 当前数值可能仍需调参。
- Final: Ruling: 旧头顶提示重叠 — 确认可复现，列入整体可读性后续项 — 紧贴玩家暂难分辨彼此耐力条，本轮不声称该表现已解决。
- Final: Ruling: 旧档迁移 — v19 / HIMD 明确拒绝旧档，符合本轮兼容决定 — 旧构建存档不能在新版继续。
- Final: Ruling: 最大扩建性能 — 代码扫描有界，当前原生场景通过；不声明最大布局帧预算 — 极端扩建仍可能需要性能优化。
- Final: Ruling: 全量回归终止结果 — 保持阶段未完成直到当前 verify.sh 退出并检查日志 — 中途有 PASS 不可替代完整成功。
- Final: minor (deferred): 无本轮独立复核新发现；既有头顶提示重叠另按总目标的整体体验项处理。
- Task 1 complete: 25 项海域规则通过；全部模拟／UDP 首轮 236/236，新增普通输入及 UDP 后最终钓鱼套件 250/250。
- Task 3 complete: `./scripts/verify.sh`（session 92774）完整退出 0，14 组计数套件共 678 项通过，含新局 14/14；任务复核 38 份生产文件摘要无漂移。主设计／实施说明／缺口清单和 verification-summary.json 已更新。仅海域阶段关闭，总目标保持 active。

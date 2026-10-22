# 合钓与反向抢钓实施计划

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 正式装备模式下，普通鱼和食人鱼共享多线牵引、反向抢钓、耐力分摊、主钓交接及一次性收益分配，并验证单人和多人真实 UDP。

**Architecture:** Fish.Owner / Piranha.Owner 表示当前主钓，玩家的目标关联构成有效钓线集合。独立合钓规则负责集合、角度和分摊；模拟按目标推进一次，库存批量事务结算所有参与者。Unity 只读取状态，开发验证驱动显式启用。

**Tech Stack:** C# netstandard2.1、.NET 8 验证程序、Unity、现有帧同步与 UDP 房间。

**Spec:** docs/hook-island-game-design.md 的“多人合钓与反向抢钓”。

## Global Constraints

- 夹角 ≤90° 合钓，>90° 原子切断旧线并接新线；零耐力命中失败，不影响旧线。
- 每次退出按当前剩余耐力选主钓；并列保留原主钓，否则最早加入；换主钓清除 >90° 旧线。
- 仅鱼移动造成的角度变化不脱钩；击退、超线长、耗尽、取消、不可继续出竿或离线时脱钩。
- 速度初值 1 / 1.5 / 2 / 2.5，可配置；同鱼每秒消耗按有效人数均分。
- 抵岸时最终挂线者均分金币和鱼骨，整数余数归主钓；全部成功或全部不提交。
- 上岸玩法不做；现有鱼仍按普通捕获规则。旧无装备原型保留既有行为。
- 核心规则留在 samples，不修改通用同步核心。所有证据写入 artifacts/cooperative-fishing。

## Review Focus

- 同帧耗尽、取消与新钩命中不得复活已经退出的线或重复移动目标。
- 多人库存容量或账本上限失败不得部分发奖；水中辅助者的溢出材料落在有效捕获平台。
- 同帧换主钓不得受玩家循环顺序影响耐力比较，反向清理不得触发抢钓。
- 快照必须保留加入顺序、消耗余量和主钓；拒绝孤立、重复顺序和不一致目标连接。
- 食人鱼最后一根线退出才挣脱，仍有钓线不恢复咬击，捕获只刷新一次。

### Task 1: 确定性合钓与原子结算

**Files:** 新增 CooperativeFishing.cs、CooperativeFishingStateCodec.cs、HookGame.Cooperative.cs；修改 HookGame.cs、HookState.cs、FishingEquipment.cs、EnemySystem.cs、EnemyStateCodec.cs、InventorySystem.cs、IslandMap.cs、RoomProtocol.cs；新增 tools/HookIsland.Tests/CooperativeFishingTests.cs。

**Interfaces:** CooperativeFishing.Count(IReadOnlyList<Fisher>, Fisher)、IsOpposite(Point, Point, Point)、Drain(int, int, ref int)、Speed(int, int, int)；InventorySystem.CreditMaterials(IReadOnlyList<MaterialCredit>)；模拟内部 JoinCatch、DetachCatch、AdvanceCatches。

- [x] 先加入真实模拟失败用例：同向/90° 加入、反向抢钓、零耐力、两人加速与均摊。
- [x] 运行 --filter=coop_，确认因旧独占规则失败。
- [x] 实现共享规则、批量结算、所有退出路径、主钓重选、快照与兼容版本。
- [x] 补充并验证并列、反向清理、长度、击退、最后退出、收益余数、恢复和事务失败用例。
- [x] 运行现有模拟用例，更新因已确认抵岸即捕获而失效的测试前置位置，保留原断言目的。

### Task 2: Unity 配置、只读表现与开发验收

**Files:** FishingSettings.cs/.asset、ReelingStaminaOverlay.cs、开发专用 CooperativeFishingSmokePilot.cs / Driver.cs、scripts/run-island-party.py、tools/HookIsland.Tests/CooperativeFishingNetworkTests.cs。

**Interfaces:** FishingConfig.CooperativeSpeedBonusPermille 默认 500；CooperativeFishingSmokePilot.Sample(IslandMap, HookState, int, ulong)，开发驱动仅提交普通输入。

- [x] 接入可配置加速和合钓主/辅提示，保留每人实际钓线。
- [x] 编写单人及多人普通输入场景，检查合钓、抢钓、分配与所有客户端 checkpoint 一致。
- [x] 同步 Unity 程序集、重新导出配置，执行编辑器检查和开发构建。
- [x] 启动独立原生客户端验收并查看截图与日志；确认发布隔离。

### Task 3: 回归、复核与文档

- [x] ./scripts/verify.sh 全量回归，保存日志。
- [x] 进行一次独立最终代码复核并修复有效问题，补充对应回归。
- [x] 更新合钓实施说明及设计的已实现范围，列出证据和验证局限。

## 执行记录

- 使用当前正式工作目录，保留已有未提交修改；基线保存至 artifacts/cooperative-fishing/baseline.json，不将其他任务改动打包提交。
- 用户已明确要求实施，直接按计划执行，不重复请求设计批准。
- 夹角采用鱼到玩家的水平向量点积；同帧按槽位处理新命中，所有有效连接统一扣耐力后再处理耗尽，最后处理移动与捕获。
- 最后一根线退出沿用当前挣脱动画；有圈水中玩家可主钓；未到有效岸上不结算。抢钓保留目标当前状态，不触发挣脱重置。

- Task 1 complete：合钓初始 6 项用例先失败 5 项，再实现转绿；补充事务、离线、四线抢钓和配置覆盖。最终模拟 111/111 通过，见 simulation-final.log。
- 自动验收输入延迟导致站位振荡：以 3／6 帧延迟重现（delayed-input-red.log），开发驱动靠近目标时减速后通过；玩法未为验收添加特判。
- Final review：独立只读复核发现超线长新钩可先断旧线。coop_overlong_incoming_line_cannot_cut_existing_hooks RED→GREEN，修复前先验证完整连接，再允许交接；模拟 110/110。
- Final: Ruling: 同帧多条取消维持逐槽位稳定事件顺序，不做取消批处理 — 与现有动作管线一致 — 同帧连续退出可能产生中间交接及反向旧线清理；该顺序已写入实施说明。
- Final: Ruling: 可上岸鱼／Boss、旧无装备原型保留明确范围边界；抢钓保留当前鱼状态，不新增尚未设计的挣扎系统 — 避免把本轮扩展成新玩法 — 后续接入需另行设计与兼容版本。
- Final review 没有延期的 minor 问题。独立复核记录见 artifacts/cooperative-fishing/final-review.md。
- 全量回归发现原钱包上限测试仍等待鱼到玩家脚下；新规则使无法结算的鱼停在岸边。将前置判定改为抵达有效平台，保留账本／鱼骨原子性和代数溢出断言，背包模拟 25/25 通过（inventory-shore-regression.log）。不修改生产规则来迁就旧测试。
- Unity 最终编辑器 52/52 通过；开发和发布构建零错误／警告。单人、双人、四人房间独立客户端验收完成，各房间所有端的完整 checkpoint 一致；发布程序集没有合钓自动验收类型。
- 游泳全量回归发现已挂线鱼在水中玩家脚下时遮挡救援钩：新增两种鱼共用的正常出钩回归先失败，再加入瞄准落水持鱼者时的碰撞避让。救援资格复用玩家钩取规则；模拟 111/111、原水中持鱼救援回归通过。该修复后重新同步并复验构建和客户端。
- Final: Ruling: 落水救援保持原有优先用途；与水中挂线者重叠且明确瞄准该玩家时，已挂线鱼避让钩子 — 防止合钓可命中范围使旧救援能力失效 — 独立普通鱼／食人鱼回归与泳圈回归验证，分离目标仍走原有命中顺序。
- 原食人鱼争抢测试改为两人合钓：保留仅捕获一次和 15 金币／6 鱼骨总产出，新增主钓 8、辅助 7 金币及各 3 鱼骨的断言，普通输入与逐帧恢复通过。
- 复活开发验收的敌人场景因新击退断线规则改变了收益时机；与保存的改动前源码对比并记录前后轨迹，确认是验收操作者意外落水后没有返回平台。只改开发 pilot：未赚足预定复活资金时先用普通输入游回平台，再钓鱼；原有免费救助、个人赚钱付费复活断言不变，含敌人用例通过。见 revival-trace-before.log、revival-trace-after.log、revival-pilot-green.log。
- 最终完成：./scripts/verify.sh 退出码 0，钓鱼 124/124、背包 26/26、角色 28/28、游泳 27/27、敌人 45/45、复活 26/26、重开局 10/10；框架、录制回放与底层 UDP 全部通过。Unity 52/52，开发／发布构建成功且发布类型隔离通过；原生 1／2／4 人房间分别在 600／722／717 帧核对完整 checkpoint。汇总见 artifacts/cooperative-fishing/verification-summary.json，范围与局限见 docs/cooperative-fishing-implementation.md。

# 共享委托与诱饵实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 玩家通过实际捕获推进三份共享委托，自动获得独立于金币的团队诱饵，单人和多人看到一致进度。

**Architecture:** CommissionConfig/State/System 分离配置、只读状态和推进规则；捕获成功事务提交进度；CMS1 快照持久化；Unity 配置和 HUD 只读取确认态。

**Tech Stack:** 纯 C# netstandard2.1、.NET 8、Unity 2022.3、既有 UDP 同步。

**Spec:** docs/superpowers/specs/2026-10-22-commissions-design.md

## Global Constraints

- 三份顺序目标 6/2、10/3、14/4（总鱼获/稀有），每份诱饵 1；配置上限 16 份、各阈值与奖励 1～1000。
- 每个成功捕获仅计一次；稀有依物种，与精英等级独立；食人鱼只计总鱼获；不扣已有资源。
- 成功捕获事务同步更新，失败不推进；恢复保留、重开清零。未来委托零进度，完成的鱼不重复计入下一份。
- 规则/状态/房间 18、输入 9、地图 HIMC；自动验收显式启用并排除发布包。
- 共享工作区保留原修改，任务基线 artifacts/commissions/baseline.json；不整体提交；Boss 上岸仍延后设计。

## Review Focus

- 合钓与抢钓的多人个人统计不能重复转成委托贡献；食人鱼必须走同一成功事务。
- 库存或钱包上限导致捕获未提交时，委托与诱饵不得提前发放。
- 同帧不同目标完成前后委托可以各记一次，但同一条鱼不能跨委托重复计数。
- 快照恢复、重放和重开必须保留或清零正确的进度；无效完成顺序、稀有计数、诱饵余额应拒绝。
- 未绑定配置应禁用；HUD 指针阻挡与确认态、发布包自动验收隔离必须成立。

### Task 1: 确定性委托与捕获事务

**Files:** 新建 samples/HookIsland.Simulation/Data/Configuration/CommissionConfig.cs、CommissionState.cs、CommissionSystem.cs、CommissionStateCodec.cs；修改 FishingEquipment、FishingCatalog、Data/fish.csv、HookState、HookGame、HookGame.Cooperative、IslandMap、RoomProtocol 及 proto 注释；新增 tools/HookIsland.Tests/CommissionTests.cs。

**Interfaces:** CommissionDefinition(int fish,int rare,int bait=1)；CommissionConfig(IEnumerable<CommissionDefinition>? definitions=null,bool enabled=true)；CommissionState(int completed,int bait,IEnumerable<CommissionProgress> entries)；CommissionSystem.Create(config)、RecordCatch(config,state,bool rare)；HookState.Commissions；FishingConfig.Commissions。

- [x] 写成功捕获、合钓单计、稀有替代路径三个测试；提供可编译接口，尚不接入事务。运行隔离 HookIsland.Tests --simulation-only --filter=commission_。Expected: 计数和诱饵为零而失败。
- [x] 实现规则、严格状态校验与捕获草稿提交，补 Review Focus 的失败事务、同帧目标、抢钓、食人鱼、恢复、禁用与配置边界。
- [x] 运行完整模拟。Expected: 所有测试通过；保存命令与结果。

### Task 2: 正式配置、HUD 与普通输入

**Files:** 新建 CommissionSettings.cs、CommissionHud.cs、CommissionSmokePilot.cs/Driver.cs、CommissionSceneTests.cs、CommissionNetworkTests.cs；修改 FishingSettings、HookPlatformSceneBuilder、GameplayHud、HudLayout、FishingOverlay、IslandFishingController、run-island-party.py 和测试工程链接。

**Interfaces:** CommissionSettings.CreateConfig()；CommissionHud.Draw(HudLayout) 仅读确认态；HudLayout.Commissions 及 Contains(...,bool commissions=false)；CommissionSmokePilot.CreateMap(bool defaults=false)、Sample(map,state,slot,tick)、Complete(state)。

- [x] 先写配置禁用／导出与 HUD 指针阻挡测试，普通输入测试（1/2/4 人、0/3/6 延迟）；Expected: 尚无对应入口而失败。
- [x] 接入正式配置与 HUD，补显式开发输入验收，不注入运行状态；模拟默认三份完整流程。Expected: 1/2/4 人全部完成且诱饵独立。
- [x] 导出地图、同步程序集、Unity 编辑器测试、开发与发布构建；Expected: 无编译错误，发布包无委托验收类型。
- [x] 真实 UDP 与原生 1/2/4 客户端；Expected: 完整 checkpoint 相同、显示进度一致、截图和日志有效。

### Task 3: 集成验证与复核

- [x] 运行 ./scripts/verify.sh。Expected: 退出 0。
- [x] 一次独立最终复核，Critical/Important 一次修复轮次并验证；记录每条范围裁定。
- [x] 更新主设计、实施说明和完整缺口清单；保存验证汇总，总目标保持原范围。

## 执行记录

- 前一目标轮次分类：progress。宝箱实现及重要修复完成，184 项钓鱼/UDP、57 项编辑器与原生 1/2/4 人证据已保存。
- 当前代码检索确认无委托/诱饵实现；主设计明确要求共享公共目标及替代条件，选择这一总目标缺口继续。
- Builder check: commissions_design_check 返回 nothing important left。
- 执行沿用用户持续实施授权和现有 C#/Unity 体系；可选澄清未回复，按明确标记的实现者初稿推进，不冒称细节获批。
- Ruling: 现有未提交集成与指定 Unity 工程保留在正式工作区，用任务前基线隔离差异，计划兼作持久 ledger — 不切空白 worktree、不打包其他改动 — 代价是最终复核需要按任务基线而非 Git HEAD 比较。
- Pre-flight: Task 1 的配置和状态由 Task 2 导出与读取；新增字段必须跨地图、快照及 capture/restore 完整传递。HUD 不修改状态；普通输入 pilot 仅通过正式捕获入口计数；接口无冲突。

- 本轮前一目标轮次分类：progress。规则 17 的合钓当前集成版本复验完成，184/184、原生 1/2/4 人完整状态一致；无需重做合钓，继续委托缺口。
- Task 1: complete — 三个捕获用例先 0/3 失败；扩展边界 2/10，随后事务与校验实现转绿。完整模拟 `dotnet artifacts/commissions/build/bin/HookIsland.Tests/release/HookIsland.Tests.dll --simulation-only` → 180/180，其中委托 15 项。证据见 artifacts/commissions/{red.log,boundaries-red.log,simulation-core.log}。
- 本轮前一目标轮次分类：progress。当前规则 18 的钓鱼／UDP 212/212、Unity 63/63、开发／发布构建通过；其中委托 28 项（15 个规则、10 个普通输入、3 个真实 UDP），支持下一步原生验收。合钓已经完成，不再重复实施或复验来替代总目标进展。
- Task 2: 配置／HUD 最初 1/5 通过、4 项因未接入失败，普通输入零捕获失败后转绿；默认三份目标经普通输入捕获 30 条鱼得到 3 诱饵，1／2／4 人及 0／3／6 帧输入延迟全部通过。`--commissions` 入口先因未知参数失败，再接入并检查帮助与场景互斥。
- Task 2: Ruling: 复用 `artifacts/cooperative-fishing/current-integration/` 的当前开发／发布构建 — 187 个生产源文件摘要未变化，避免重复构建同一源码 — 只要后续生产代码发生变化就必须重新构建；证据副本和委托发布隔离记录保存在 artifacts/commissions。
- Task 2: 原生 RED 发现 PlatformSmokeDriver 未排除委托标志，覆盖输入并在第 600 帧写入建造报告。补全已有场景互斥后重新编译至 `artifacts/commissions/HookPlatform.app`，1／2／4 人均 GREEN，分别第 1142／1080／976 帧完成，2／3／5 份完整 checkpoint 逐字节一致；所有客户端只有一个验收驱动，四人场景所有玩家均贡献且最多显示四条线。
- Task 2: complete — 28 项委托规则／普通输入／UDP、Unity 63/63、原生 1／2／4 人、发布隔离均通过。开发构建成功但有一条 Unity 未编译导入／后处理代码提示；本次变动为开发驱动互斥，原生流程已验证其生效，发布构建零错误／警告。最终构建、截图和状态证据在 artifacts/commissions。
- Task 3: 额外把委托接入既有真实 UDP 经济重开回归：正常捕鱼得到首份诱饵，失败并重开后检查各端目标与诱饵清零。完整 ./scripts/verify.sh 正在运行，尚不宣称全量完成。
- Task 3: complete — `./scripts/verify.sh > artifacts/commissions/verify-full.log 2>&1` 退出 0；钓鱼/UDP 212、背包 26、角色 28、游泳 27、敌人 45、复活 26、重开 12 项全部通过，框架和回放检查也通过。新增委托经济重开断言已随该入口通过。
- Final review: commissions_final_review（gpt-6-astra/high，只读，一次）未发现 Critical/Important/Minor；任务补丁与现有文件一致，原生完整 checkpoint 经独立读取比对一致。无修复轮次、无延期 minor，报告保存至 artifacts/commissions/final-review.md。
- Ruling: 接受 reviewer 对终局、诱饵消耗、胜利与 Boss 上岸的 Declined to judge — 前三项继续保留总目标后续任务，Boss 上岸按用户明确要求延期 — 本阶段只能称委托完成，不能称完整游戏完成。
- Ruling: 接受旧版本迁移、重连／晚加入、跨机器网络和长期经济平衡不由本轮下结论 — 旧版本明确拒绝，后四项证据未覆盖，重连与整体体验仍在缺口清单 — 不把快照或本机 UDP 通过替代这些验收。
- Ruling: 接受基线之外既有改动不作本轮审查结论 — 使用共享工作区任务基线，避免将其他任务改动误归本轮 — 关联回归由完整 verify.sh 提供，不能替代其他系统自身审查。
- Ruling: reviewer 未判断完整脚本完成状态 — 执行者已取得同一进程的终态退出码 0，并核对末尾重开 12/12 — 该项不再悬置。
- 文档、缺口清单和验证汇总已更新；共享委托阶段完成，总目标继续保持 active，下一项是委托进度驱动的鱼群／事件与恢复机会。

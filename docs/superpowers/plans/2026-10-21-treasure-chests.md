# 宝箱玩法实施计划

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 让单人可回收小箱和稀有箱，多人可合钩大箱并在岸上开启，奖励和状态在联机与恢复中一致。

**Architecture:** 独立宝箱配置、状态、漂流规则、开箱命令及快照；`HookGame.Treasure` 管理挂钩/回收/开箱事务。网络缓存可靠提交意图，Unity 配置与表现不写权威状态。

**Tech Stack:** C# netstandard2.1、.NET 8 测试程序、Unity 2022.3、既有 UDP 帧同步。

**Spec:** docs/superpowers/specs/2026-10-21-treasure-chests-design.md

## Global Constraints

- 保留现有未提交工作；基线为 artifacts/treasure-chests/baseline.json，不打包提交其他任务修改。
- 首钩主拉、无反向抢箱；大箱移动需两根钩、单人不刷大箱；不额外消耗耐力。
- 抵岸固定名单、均分余数归主拉；全部奖励/箱子状态一次提交；旧代次和旧请求不能重放。
- 自动验收显式启用且排除发布包；不改通用帧同步核心，不实现 Boss 上岸。

## Review Focus

- 同帧击退、掉线、超线长与抵岸不得冻结已失效玩家或留下孤立连接。
- 大箱岸上奖励固定后，开启者和旁观者不能改写名单；失效点击不能在刷新后生效。
- 多人金币/材料/统计上限失败必须全部回滚，满包溢出只能落在有效平台。
- 平台扩建后的刷新和漂流不能把箱子放入地块、世界外或产生不可到达的唯一机会。
- 交易输入组合和 E 的交互优先级不能丢失或重复消费请求，重开需清空旧回执缓存和表现。

### Task 1: 确定性宝箱与结算

**Files:** 新建 TreasureConfig.cs、TreasureState.cs、TreasureMotion.cs、TreasureCommand.cs、TreasureStateCodec.cs、HookGame.Treasure.cs；修改 HookGame/HookState/HookInput/FishingConfig/IslandMap/RoomProtocol；新增 TreasureTests.cs。

**Interfaces:** `TreasureConfig` 不可变可编码；`TreasureState.Chests` 独立只读快照；`TreasureCommand(int chestId,uint generation,uint requestId)`；`Fisher.HookedChest` 一基关联；`TreasureResult` 最终回执；`InventorySystem.CreditMaterials` 批量材料事务。

- [x] 增加正常钩取小箱和大箱双钩门槛的失败测试，运行 `dotnet run --project tools/HookIsland.Tests -c Release -- --simulation-only --filter=treasure_`，确认缺少宝箱行为而失败。
- [x] 接入配置、漂流、挂钩、参与者退出、抵岸、开箱和兼容编码，补充上述 Review Focus 的边界及存档测试。
- [x] 运行完整模拟测试，预期全部通过；记录初值与具体边界裁定。

### Task 2: 输入、Unity 与正常操作验收

**Files:** TreasureRequestBuffer.cs、TreasureController.cs、TreasurePresentation.cs、TreasureSettings.cs/.asset、TreasureSmokePilot.cs/Driver.cs、TreasureNetworkTests.cs；修改输入组合器、Unity 房间控制器/商人 E 入口、场景安装器、运行脚本。

**Interfaces:** 缓存 `TryQueue/Observe/Compose/Reset`；`TreasureController.TryOpenNearest` 仅排队；`TreasurePresentation.Update(HookState)` 只读；开发 pilot `Sample(map,state,slot,tick)` 只生成正式 HookInput。

- [x] 先验证重复请求、交易互斥和正常输入大箱流程失败，再接入缓存和交互。
- [x] 导出正式地图、同步程序集、运行 Unity 编辑器检查，构建开发与发布包。
- [x] 启动 1／2／4 原生客户端，验证实际钩线/提示/开启，逐字节比较 checkpoint，检查发布包无验收类型。

### Task 3: 集成、复核与缺口清单

- [x] 运行 `./scripts/verify.sh`，预期退出 0；有失败逐项定位并回归，不用特判迁就旧验收。
- [x] 执行一次独立最终代码复核；重要问题按 RED→GREEN 修复并重跑必要检查。
- [x] 更新设计、宝箱实施说明、总缺口清单；保留全部目标范围与明确待设计项。

## 执行记录

- 上一目标轮次分类：合钓复验未新增总目标完成项，按 no progress 处理。本轮重新核对当前文档与代码，宝箱确无状态、操作及结算入口，选择其为下一安全实现项。
- 用户已授权持续实现，按当前 C#/Unity 体系直接执行；初稿中的新增口径为实现者选择，已发出可选澄清，不视为用户确认。
- Pre-flight：配置→地图及快照、状态→钩索与 Unity、命令→所有输入组合器存在共享接口；新字段必须逐链保留，旧版本拒绝；奖励复用已有批量库存事务。
- Builder check：独立只读复核未发现会改变实现方案的未决问题；三项可选澄清仍按实现者初值处理。

- Task 1 RED：3/3 用例因未刷新宝箱失败；配置与 DTO 编译已成功，非接口错误。核心实现后 3/3 通过。
- Task 1 边界检查：12 项中 10 项通过；建造占用检测遗漏宝箱（已先复现），零耐力测试遗漏了等待出钩期间的自然恢复，按真实规则补初始恢复延迟。
- Ruling: 宝箱抵岸在本帧敌人伤害、落水伤害和死亡清理之后处理 — 当帧失效线不参与抵岸归属 — 若产品需要先抵岸再受击，需调整固定时序及边界测试。

- Task 1: complete，确定性模拟 160/160，含宝箱边界与 1/2/4 人、0/3/6 帧延迟的普通输入流程；见 artifacts/treasure-chests/final-simulation.log。
- Task 2：请求缓存测试 RED（未保留请求）→GREEN；补齐房间设置、建造预览与复活输入组合的字段保留/互斥。Unity 已编译，控制台零错误警告。
- Ruling: 刷新同时避开其他在场箱子，至少间隔 1 米 — 实际延迟输入场景复现重叠箱干扰命中 — 若需要密集堆箱可配置间距，当前初值保证可分辨。
- 验收策略修正：宝箱按实际被回收的种类推进，允许正常钩碰撞先命中另一只箱子；不以测试特判改变正式碰撞顺序。
- Task 2: complete。Unity 编辑器 56/56；开发及发布包零错误警告；原生 1／2／4 人均到第 600 帧，房主与各端完整 checkpoint 逐字节一致。四人均参与大箱拉取、各端呈现四根线，运行日志无异常。见 native-results.json、unity-editmode.json、release-isolation.json。
- 实机目视发现收获提示遮挡生命条及可开箱时商店 E 提示冲突，已接入现有 HUD 反馈区并隐藏被宝箱优先级覆盖的商店提示；重新构建两种包并通过四人客户端复验，截图检查通过。此为低影响表现修正，不增加镜像实现的测试。
- Task 3 全仓库验证：`./scripts/verify.sh` 退出 0，完整钓鱼模拟／UDP 179/179（含宝箱 31 项），其他系统均通过；证据 artifacts/treasure-chests/verify-full.log。
- Task 3 独立最终复核已交付 treasure_final_review；仅复核 baseline.json 以来本任务差异，未将共享工作区其他改动纳入提交。
- 最终复核：Critical 0、Important 2、Minor 0；一次修复轮次，不再请求第二次复核。详见 artifacts/treasure-chests/final-review.md。
- Important 1 RED→GREEN：Unity 未绑定 TreasureSettings 时错误开启宝箱，新增测试预期 false、实际 true；改用 TreasureConfig.Disabled 后完整编辑器 57/57，通过最终开发／发布构建，均零错误警告。
- Important 2 为测试覆盖缺口：补齐抵岸后超长线、满包落地、刷新后旧请求、同帧双开箱、后续参与者统计上限回滚五项验收，完整模拟 165/165。极值 fixture 最初违反材料守恒，被正常恢复校验拒绝；修正为同量地面材料后通过，不把 fixture 错误记作生产代码 RED。
- 最终开发包四人复验通过：四名参与者、四根呈现钓线、房主及四端 5 份完整 checkpoint 逐字节一致，日志无运行异常。最终发布包无宝箱／合钓／房间设置验收类型。开发／发布的模拟和网络 DLL 与 Unity 导入及隔离测试目录相同。
- Ruling: Boss 上岸按用户要求延后；委托、事件、终局、后期商人未在宝箱复核中判定，仍列总缺口 — 当前子系统通过不能表示完整进程完成 — 后续逐项实施、验证。
- Ruling: 本轮仅证明本机 UDP，不能证明晚加入、重连、跨机器、Steam 或公网 — 重连继续留在总目标，环境适配单独验收 — 不用快照一致代替可操作重连。
- Ruling: 未回复的首版数值和主拉等选择仍为实现者初稿 — 配置可调整，规则变化须同步模拟与测试 — 未冒称用户批准全部选择。
- Ruling: 共享工作区的其他修改不属于本任务复核 — 保留原内容与分支，本轮不执行整体提交、合并或清理 — 最终文本差异和生成资产清单分别记录。
- Ruling: 标准全仓库构建与隔离构建的调试路径／构建标识不同 — 用隔离测试 DLL 对齐原生验收，另记全仓库源码回归 — 不声称不同输出目录 DLL 字节相同。
- Task 3: complete。最终隔离程序集钓鱼／真实 UDP 184/184，宝箱专项 36/36；完整仓库入口此前退出 0，最终 Unity 57/57，开发与发布包无错误警告，本机原生 1／2／4 人验收通过。验证汇总 artifacts/treasure-chests/verification-summary.json；最终差异 final-task-diff.patch，生成地图／元数据另列 generated-asset-manifest.json。
- 总目标仍为 active。本轮新增完成宝箱玩法，剩余委托、进度事件、终局、后期商人、重连与整体流程验收保留在 docs/gameplay-gap-audit.md，不以子系统通过替代总目标完成。

# 钓竿功能实现计划

> **For agentic workers:** Use superpowers:executing-plans to implement task by task. 当前用户已要求实现，直接执行，不重复请求设计批准。

**Goal:** 在木平台场景中完成可联机的钓竿购买、收纳、装备、三项属性、鱼挣扎和耐力恢复闭环。

**Architecture:** 玩法和配置放在纯 C# HookIsland.Simulation，输入和快照沿用现有帧同步。Unity 只提供配置入口、菜单、提示及表现。旧岛屿原型保留原规则，木平台启用新装备规则。

**Tech Stack:** C# / netstandard2.1，30 Hz 整数模拟，现有 UDP 房间，Unity 2022.3 Built-in。

**Spec:** `docs/hook-island-game-design.md` 的钓竿、统一耐力、恢复、鱼配置、商品购买部分。

## Global Constraints

- 正式游戏实现：遵循 SRP、LSP、OCP；数值定义、状态、库存布局、商店、装备、耐力分别负责明确职责。使用组合而非无必要继承；扩展钓竿通过配置。UI 不持有权威玩法数据。
- 初始竿 4 米、4 米/秒、耐力 +20；角色基础 100；31 款含赠送竿，30 款售卖竿严格采用文档表。
- 等待 2 秒、恢复 5 点/秒；不满也可行动；使用钓竿期间阻断恢复；换装不补耐力。
- 鱼的 K×实际重量×等级扣耐力，耗尽逃脱无奖励；成功按重量同时发金币与鱼骨。
- 购买使用个人金币，装备属于购买者；旧竿保留；空闲才能换竿。
- 600 秒整批换货、共享有限库存；M1 只出 T1，M1–M5 概率按表。
- 帧同步核心保持独立；保留工作区已有平台及浮木改动，不覆盖或提交已有工作。
- 本轮包含钓竿所需的 4×4 收纳格和钓竿商品；完整材料背包、背包升级、其他装备与游泳另属后续系统。

## Review Focus

- 同帧争抢、过期库存、重复请求必须只扣款发货一次。
- 换竿／卸竿／重新装备不能补耐力，满包换装必须原子处理。
- 恢复等待边界、零耐力、取消、拖拽和收鱼不能重复恢复或重复奖励。
- 自定义配置不同的客户端不能进入同一房间，快照重放保留进度和归属。
- 真实初始慢竿必须能命中移动鱼；购买提升能实际影响出钩。

### Task 1: 配置与耐力捕鱼

Files: 新建 `samples/HookIsland.Simulation/Logic/Fishing/FishingEquipment.cs`、`FishingCatalog.cs`、`Data/*.csv`；修改 `HookState.cs`、`HookGame.cs`、`IslandMap.cs`；测试 `tools/HookIsland.Tests/RodTests.cs`。

Interfaces: `FishingConfig` 提供全局参数；`RodCatalog.Get(int)` 提供三项属性和占格；`FishingRules.Range(HookState,Fisher)`、`Speed(...)`、`MaxStamina(...)` 供模拟及 Unity 共用。

- [x] 先写初始实际飞行速度断言：30 帧走 4000 毫米而不是旧 9000；运行过滤测试见失败。
- [x] 写入表配置、整数耐力与等待、鱼种／重量／等级及奖励，完成消耗、逃脱、取消和恢复。
- [x] 验证 2 秒等待、5 点/秒、空钩阻断、轻重鱼消耗差异、成功／逃脱唯一结算。

### Task 2: 商品、收纳、装备和联机

Files: 新建 `GridInventory.cs`、`RodShop.cs`、`RodEquipmentSystem.cs`、`FishingStateCodec.cs`；修改 `HookInput.cs`、`HookGame.cs`、`HookState.cs`、`RoomProtocol.cs` 和协议注释。

Interfaces: `RodAction` + `RodTarget` + `RodBatch` + `RodRequestId` 输入；每个物品独立 ID 和归属；`TackleState` 存商品批次、下一次补货、装备物品和格子坐标。

- [x] 写购买去重、两人争抢、补货过期、满包不扣费及换装裁切测试，运行见失败。
- [x] 实现格子占用、旋转自动放置、购买并装备／收入包／换装／卸竿，限定近商人购买。
- [x] 扩充输入与快照及兼容版本，验证状态重放和真实四端 UDP 一致性。

### Task 3: Unity 可玩接入

Files: 修改 `PlatformSettings.cs`、`IslandFishingController.cs`、`FishingOverlay.cs`、`FishingPresentation.cs`、`PlatformConstruction.cs`；新建 `FishingEquipmentOverlay.cs` 与自动验收输入。

- [x] 接入配置资产、平台商人入口、8×6 商品格、钓竿属性比较、4×4 收纳与右手装备。
- [x] 显示耐力、恢复状态、鱼重量／等级／挣脱力、脱钩提示与装备外观；更新瞄准范围。
- [x] 同一请求持续发送至确认，防止网络输入超时丢失购买；互斥建造、购物和装备面板输入。
- [x] 使用正式输入完成捕鱼赚钱、购买换竿及升级后捕鱼；Unity 编译和 EditMode 验证。

### Task 4: 集成验收与文档

- [x] `./scripts/verify.sh` 完整验证；同步 DLL；运行四个 Unity 进程，比较服务器与客户端最终状态及表现。
- [x] 新鲜上下文审查本轮变更，修复重要问题并回归。
- [x] 更新可玩说明、设计实现状态及验证记录；产物保存 `artifacts/fishing-rods/`。

## 决策与执行记录

- 原工作区有未提交的平台功能，用户指定该正式目录；直接在此基础上增量实现，不创建缺少这些改动的分支或提交他人工作。
- 钓竿的格子收纳先独立承接全部备用竿，材料仍沿用现有个人计数；后续完整背包接入时合并格子占用。
- 回收速度尚未定值，本轮采用可配置 2 米/秒收鱼、12 米/秒空钩／浮木回收；拉队友沿用现有规则。
- 默认平台商人为 M1；后期货档通过配置启用，地图中的后期商人布置仍待后续。
- 同帧到岸与耐力耗尽优先结算消耗，耗尽则挣脱；这是本轮明确的结算顺序。

## 背包协作接口

状态：**接口稳定，可接入**。钓竿实现、最终审查修复和验收均已完成，背包任务可接手下列共享文件并增量集成。本任务不再修改这些共享实现文件或占用 Unity 验收。

- `RodCatalog.Get(type)` 返回 Id/Name/Tier/Range(mm)/Speed(mm/s)/StaminaBonus/Price/Width/Height；CSV 为 `Data/rods.csv`。
- `RodItem` 当前是独立实例（Id、Owner、Type、X、Y、Rotated）；装备物品 X=Y=-1，右手引用 `Fisher.EquippedRod`。后续统一库存可迁移此身份，勿复制双份权威物品。
- `GridInventory.TryPlace` 与 `GridRect` 为纯尺寸算法，可共用；当前备用竿背包尺寸 4×4。
- `RodEquipmentSystem` 为内部交易服务：Apply(map, tiles, ref player, command, ref coinsTotal)，事务包括旧竿入包、新竿装备、扣款、库存售罄、耐力裁切。tiles 必须传当前已建地块；购买使用 player.Coins，总计仅为派生诊断。未来背包接入在这里替换收纳事务。
- `RodRequestBuffer` 位于 Network，保持点击至权威回执，Compose/Observe/Reset；`RodAction` 为 Buy/BuyAndEquip/Equip/Unequip，输入含 Target/Batch/RequestId。
- `Fisher.Stamina` 30000 单位=1点；`StaminaSystem` 管理消费/恢复。换装保留当前值，只裁切超出新上限的部分；不得借换包重置耐力。
- `TackleState` 为只读快照：Config、Items、Stock、Batch、NextRestock、NextId；`FishingStateCodec` 验证身份、归属、格子、等级和钱包守恒。
- 现可接入的共享文件：HookGame.cs、HookState.cs、HookInput.cs、FishingEquipment.cs、RodEquipmentSystem.cs、FishingStateCodec.cs、IslandMap.cs、RoomProtocol.cs、Unity IslandFishingController/FishingOverlay/FishingPresentation/PlatformSettings/FishingEquipmentController/FishingEquipmentOverlay、平台场景与 builder、测试入口及项目文件、sync/验收脚本。
- 本轮 Unity 新增 FishingSettings（资产配置）、FishingEquipmentController（意图/回执）、FishingEquipmentOverlay（只读装备界面）。背包任务后续可复用装备入口，不再新建第二套互相竞争的 I 键面板。
- 本任务未编辑背包任务的 InventoryCatalog/InventoryState/InventorySystem/InventoryStateCodec 或 Data/backpacks.csv。统一库存接入后，保留唯一物品身份；按 InventoryState.SpentCoins 的个人支出账本替换当前“已购物品总价”钱包校验，以支持转交物品。

执行记录：第一轮 rod_ 8/8、扩展模拟测试 63/63 通过；实际慢竿、权重消耗、耗尽逃脱、奖励取整、换装裁切、购买争抢、补货过期及快照重放已覆盖。Unity 接入和真实四端验证进行中。

个人金币协作更新：已采纳最新用户指示并在钓竿侧实施。Fisher.Coins=个人可花余额；Fisher.CoinsEarned=累计收入（新增，随 Tackle 快照序列化）；HookState.Coins=各玩家余额总和，仅保留诊断兼容，绝不作为新装备购买资金。RodEquipmentSystem 检查/扣减 player.Coins，同时更新总计；FishingStateCodec 检查每人 earned - 个人物品买入价之和 = balance。旧岛屿模式暂保留旧钱包，仅木平台正式规则启用个人余额。背包接入需把个人库存的物品总购买支出纳入守恒校验，或引入显式 CoinsSpent 账本。钓竿 UI 已改为“我的金币”。已先复现“玩家花队友的钱”失败用例，再修改。

协作注意：PlatformSmokePilot.cs 已观察到并行修改（Move 新增 lead、速度改为 FishingRules）。本轮保留这些更新，只修复合并产生的局部变量重复声明。该文件仍列为钓竿验收依赖，请其他任务在接口稳定标记前避免继续改动。

### 验收进度

- Task 1：完成。实际出钩 30 帧/4 米、鱼种和重量、挣脱消耗、落岸结算、耗尽优先级、休息恢复与换装裁切均通过。
- Task 2：核心完成。31 支竿、共享货架/个人钱包、原子购买换装、重复请求、批次时钟、快照重放已通过；四端真实 UDP 测试在完整验证中运行。
- Task 3：已接入。平台场景增加商人、FishingSettings 资产、装备控制/显示组件；Unity 编译无错误，StandaloneOSX 构建成功（0 errors / 0 warnings）；四个独立进程正在验收。发现格子恶意坐标会发生整数绕回，新增失败测试后改为 long 边界比较，回归待最终记录。
- Task 4：进行中。完整验证、Unity EditMode 与四进程验收并行；本轮变更不提交，因为原目录含其他任务未提交工作。计划本身作为持续执行记录，详细日志留 `artifacts/fishing-rods/`，无需另建或清理其他任务的 SDD 目录。

### 最终验收与交接（以上进度为历史记录）

- Task 1～4 全部完成。`./scripts/verify.sh` 最终退出码 0，捕鱼 78/78 通过；Unity EditMode 11/11 通过。开发／发布构建均零错误、零警告。
- 新鲜审查发现的商店地块判定已先复现失败，再修为共享 `FishingRules.NearShop(map, currentTiles, player)`；覆盖扩建地块购买、购物保护与快照恢复。建造按钮互斥移入 Toggle 本身，四端实际面板切换已验证。
- 格子坐标溢出先失败后修为 long 比较，定向与完整测试通过。商店层级遮挡已修为 FishingOverlay 统一绘制顺序，并关闭模态窗口下的建造按钮，已检查最终截图。
- 四个 Unity 进程实际捕鱼、花个人金币购竿、再次捕鱼并结束保存。最终目录 `artifacts/hook-island-playable/rod-smoke-20261021-144729-b66b2b`，帧 1489、服务器与四客户端 hash `4d2663c76f2532ed2f911dfd00b58b4e140ccef82e6882f7e5b6f5ba7462fa2f`；四人均观察到耐力消费与恢复，面板互斥通过。
- 发布程序集元数据验证通过：保留装备运行类，排除 RodSmokeDriver/RodSmokePilot。发布产物 `artifacts/hook-island-playable/HookPlatform-Release.app`。
- 使用、配置、职责、实现边界及复现方法见 `docs/fishing-rods-implementation.md`。最终日志在 `artifacts/fishing-rods/final-verify.log`、`final-unity-party.log`、`release-isolation.json`。
- 不改并行任务的设计正文；用专门实现文档与可玩说明链接记录当前完成范围。没有提交工作区其他任务的变更。
- **接口稳定，可接入。** 当前钓竿任务停止修改共享文件；背包后续集成需重新验证统一库存、材料及个人钱包守恒。协议为 room 6 / rules 6 / input 5 / state 6，输入 40 字节，扩展时同步更新版本与检查点格式。

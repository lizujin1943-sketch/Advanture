# 外围补给船与独立商店实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 玩家用真实捕鱼／浮木材料铺路到外围四家商人，各店独立库存与计时，单人和多人交易可重放。

**Architecture:** MerchantConfig/State/Rules/System 管位置、距离、货架与时钟；EquipmentSystem 保持唯一库存购买事务。配置、命令、快照传递明确商人 ID，Unity 只读状态并绑定打开的商店。

**Tech Stack:** C# netstandard2.1、.NET 8、Unity 2022.3、现有可靠 UDP。

**Spec:** docs/superpowers/specs/2026-10-22-merchant-routes-design.md

## Global Constraints

- ID 0 保留原算法与位置 (0,1000 mm)，四家额外商人 ID 1～4、等级 2～5、格子 (5,0)/(0,7)/(-9,0)/(0,-11)。正式初值，配置可改；额外商人默认关闭，正式资产启用。
- 交互 3 m 且脚下合法平台；船体半宽 800 mm，占一格保留水域，不能登船／穿船／建造。最少路格 3/5/7/9，每格现有 4 材料，不赠送资源。
- 初期商人激活帧 0；外围首次可行动岸上角色靠近即激活，不因潜游／拖拽／倒地／离线激活。每家激活后独立 600 秒换货；先换货后成交。
- 成交同时扣个人金币、交付物品和置售出；失败与成功收据均去重。命令明确 ID、批次、索引；旧 RodAction 只用 ID 0。不同商人可同帧分别成交。
- 每家不升级、不要求委托、不成为胜利门槛；不改商品池、经济、终局、Boss 上岸或地图航行。
- 规则／状态／房间 21、输入 10、装备地图 HIMF；不迁移旧档。帧同步核心不加玩法依赖。
- 自动验证只在显式开发参数启用，发布程序集剔除。共享目录保留其他修改，不自动提交或切工作树。

## Review Focus

- 同样批次／索引的不同商人不能串货，切店与换货必须清除旧选择。
- 走入水中、受击／倒地／离线不能继续购物或激活外围货架；购物快照不能被旧原点校验拒绝。
- 恢复时不能重新抽货或重置换货计时，非法活动／未活动状态和重复 ID 必须拒绝。
- 地图保留格重复归一化不可改变摘要；船体边界在游泳、击退、拖拽、建造与生成规则一致。
- 原生验收不能只验证注入资源后的成交，要有自然资源、铺路、实际到达所有船和至少一次跨档换竿。

### Task 1: 独立商人配置、状态、位置和时钟

**Files:** 创建 samples/HookIsland.Simulation/Data/Configuration/MerchantConfig.cs、MerchantState.cs、MerchantRules.cs、MerchantSystem.cs；修改 FishingEquipment.cs、RodShop.cs；创建 tools/HookIsland.Tests/MerchantTests.cs，注册 Program.cs。

**Interfaces:** `MerchantDefinition(int id,int level,int cellX,int cellZ)`，X/Z 为格心毫米；`MerchantConfig(IEnumerable<MerchantDefinition>? definitions=null)`，只含外围，Empty/Default。`MerchantState(int id,bool active,ulong activatedAt,uint batch,ulong nextRestock,IEnumerable<RodStock> stock)`。`MerchantRules.Position(FishingConfig,int)`、`Near(IslandMap,IReadOnlyList<PlatformTile>,Fisher,int)`、`Nearest(...)`、`Obstacle(FishingConfig,int,int)`。`MerchantSystem(FishingConfig,IEnumerable<MerchantState>?=null)`，`Capture()`、`Get(int)`、`Advance(IslandMap,tiles,players,ulong)`、`Sell(int,int)`。`RodShop.Generate(FishingConfig,uint,int merchantId=0)`；FishingConfig 末尾 merchants 参数。

- [x] 首先写 M1 首批兼容、外围首次靠近激活与独立 18000 帧时钟、不同 ID 商品独立的失败用例；最小可编译接口先失败，不以导入错误作 RED。Run: `dotnet run --project tools/HookIsland.Tests -c Release -- --simulation-only --filter=merchant_`。
- [x] 实现不可变配置／状态、整数空间规则与时钟；离开不重置，ID 0 不盐化原随机序列，其余加入商人 ID。预期首批转绿。
- [x] 增加非法配置、等距 ID 决胜、水中／拖拽／离线／倒地／受击不激活、到期换货、移走不影响库存的回归；预期 merchant_ 全通过。

### Task 2: 交易、编码与共享空间集成

**Files:** 创建 MerchantStateCodec.cs；修改 EquipmentSystem.cs、FishingStateCodec.cs、InventoryCommand.cs、HookGame.cs、IslandMap.cs、SwimmingTerrain.cs、RoomProtocol.cs、proto/hook-island-room.proto；新增 MerchantGameTests.cs 与必要旧版本测试。

**Interfaces:** InventoryCommand 新增末尾 `int merchantId=0` 和 MerchantId 属性，24 字节；仅购买可非零，WithRequest 保留。TackleState 新构造 `(FishingConfig,InventoryState,IEnumerable<MerchantState>)`；Stock/Batch/NextRestock 投影 ID 0，旧构造只用于无外围配置。MerchantStateCodec Write/Read/Validate 保存全店。EquipmentSystem.Advance 接收 map/tiles/players/step；删除旧独立货架字段。IslandMap 将额外商人格子幂等并入 PlatformConfig.BlockedCells；SwimmingTerrain.Obstacle 查询船体。购物位置校验延后到可读配置后。

- [x] 写实际模型同店竞争、跨店并发、远程指定店拒绝、失败请求不重放、旧 RodAction 固定原店、到期旧批次失败的 RED 测试；Run merchant_。
- [x] 接入模型激活和购买、24 字节命令、版本、完整快照；离线处理先于激活。买失败不改库存或钱包，保留稳定席位顺序。
- [x] 写独立时钟／售出恢复逐帧一致、损坏状态拒绝、命令去重 ID 保留、地图反复编码一致、船格不可建／邻格可建／游泳击退拖拽不可穿测试；预期 merchant_ 全通过。
- [x] Run: `dotnet run --project tools/HookIsland.Tests -c Release -- --simulation-only`；预期全套通过。

### Task 3: 正式资产、船体与指定店面交互

**Files:** 新建 Unity Runtime/Data/Settings/MerchantSettings.cs、MerchantPresentation.cs、MerchantHud.cs、Tests/Editor/MerchantSceneTests.cs；修改 FishingSettings.cs、HookPlatformSceneBuilder.cs、IslandFishingController.cs、FishingEquipmentOverlay.cs、BackpackOverlay.cs、GameplayHud.cs 和正式配置／地图。

**Interfaces:** MerchantSettings.CreateConfig；IslandFishingController.ShopMerchantId 未打开为 -1，打开绑定最近可交互 ID；BackpackOverlay.Draw 追加 merchantId=0，按该 MerchantState 读取选择／标题／货架／倒计时。MerchantHud 相机方向、距离和地标名，只读并避开既有 HUD。MerchantPresentation 创建四个船体、摊位、旗帜，无物理权威。

- [x] 写正式导出四船、配置回读、切店／换批选择清除、商店位置和屏幕外引导的 Editor RED。
- [x] 实现配置导出、场景组件、交互绑定、UI 请求显式 merchantId；关闭距离／落水／倒地／胜利／离房时清 ID。
- [x] 同步程序集、正确选择 Unity 项目、导出并导入地图；Editor 全套通过，控制台无错误；查看多店布局与方向距离。

### Task 4: 普通输入、联机、原生及最终复核

**Files:** 新建 MerchantSmokePilot/Driver（Unity Runtime，条件编译）、tools/HookIsland.Tests/MerchantNetworkTests.cs；增补 Restart.Tests，run-island-party.py；文档 merchant-routes-implementation.md。

**Interfaces:** `MerchantSmokePilot.CreateMap()`、`Sample(map,state,slot,tick)`、`Complete(state)`；`--merchants` 显式启动普通输入场景。四条正式位置的支路，以正常捕获获得钱物、正常建造／移动到达、实际购买更高档竿并回程捕鱼。

- [x] 空驱动 RED；实现单人、2／4 人普通输入到达全部四船和一次跨档换竿。受控关闭敌人／终局，可缩短换货但另测正式 600 秒；不直接写金币、资源、位置或交易结果。
- [x] 真实 UDP 1／2／4 完整 checkpoint 一致；同店竞争、独立货架、保存与原房间重开全部清回初态。
- [x] 最新开发／发布严格构建，发布无验收类型；原生 1／2／4 人，查看船地标、店名／货架／计时、售出回执与回程。保存完整证据和范围。
- [x] `./scripts/verify.sh` 等待完整退出；一次最强可用模型独立最终复核，重要问题 RED→GREEN 修复后做对应复验；不做第二轮复核。
- [x] 更新主设计、实施说明、兼容版本、总缺口清单与 artifacts/merchant-routes/verification-summary.json；本阶段完成不等于总目标完成。

## 执行记录与裁定

- Builder check: merchant_design_check → nothing important left。
- Ruling: 继续已授权总目标与内联执行；四船布局是可配置实现者初值，未回复的偏好题不算用户批准。成本若不合适需调布局／经济并复验，不伪称已平衡。
- Ruling: 共用保留格阻挡生成，实际船半宽 800 mm 加角色 Footprint 200 mm 阻挡穿越；相切允许，船体不新增钩索／水球阻挡用途。
- Ruling: 不自动提交共享未提交修改；任务前文本基线保存在 artifacts/merchant-routes/baseline.json，最终复核据此而非仅比较 HEAD。
- Pre-flight: activation 先处理成员离线事件，再换货，然后同帧成交。快照地图编码幂等，配置无外围时旧调用仍可用；仅兼容 API，不兼容旧存档。
- Self-review: 空间、独立随机／时钟、交易原子性、命令去重、UI 绑定、真实输入到达、原生与重开均有任务；五项 Review Focus 分别映射 Tasks 1～4。

- Task 1 complete: rules-red.log 首批 1/3（首次激活和独立货架缺失），实现后 3/3；补充不可交互状态、等距与精确 3 m、重复靠近和非法布局，共 7 项。task1-simulation.log 完整模拟 283/283，退出 0。M1 随机算法保持，外围尚未接到正式交易／UI。

- Task 2 complete: 交易 RED 0/3 → 3/3；总商人专项 19/19，完整模拟 295/295（task2-simulation.log，退出 0）。覆盖同店抢购／跨店同批索引、失败请求换店不重放、激活帧／到期帧、独立快照与损坏拒绝、实际拖拽／击退／游泳阻挡。
- Task 2 Ruling: 快照头随本次不兼容变化由 HISC 改为 HISD，配合规则／状态／房间 21、输入 10、地图 HIMF 明确拒绝旧档。
- Task 2 geometry: 首次边界测试发现 IsMonsterWater 只看平台、没有读取保留格；初始水怪位于船体的回归先失败，再加入共用保留格判定转绿。另两项首次失败属于夹具：北邻格被真实浮木占据，改用空的南邻格；友方拖拽进入水中后因找不到安全回岸路径在 8.8 m 释放，合法早于船碰撞的 9 m，保留原救援行为并修正断言。

- Task 3 complete: Editor RED 1/9 → GREEN 9/9，补充布局及船体检查后全套 92/92；最终编译后再次 92/92（unity-final.json）。正确实例及工程路径已核实，正式资产／地图已导出；单人原生截图已查看地标、店名、货架、独立计时和回程道路，控制台错误／警告均为 0。
- Task 3 Ruling: MerchantPresentation 由控制器持有和释放，而非常驻场景组件，避免场景对象与确定性配置重复维护；成本若不合适为调整表现生命周期，不影响权威数据。
- Task 3 Ruling: 相机跟随读取确认态，移出仅在可输入时运行的 CaptureInput。后台客户端、商店打开或受击退时仍跟随本人，避免离岸后画面停在旧位置；不修改权威状态。
- Task 4 progress: 空操作者 RED 0/1 → GREEN；跨档断言发现东船此种子只有 T1，先失败再改为北船实际 T2 购买。最终普通输入矩阵 9/9（input-matrix.log），保持正式价格、600 秒和位置；一人/多人都实际支付 96 材料并回程捕鱼。
- Task 4 Ruling: 普通输入验收选择北船跨档，不强制东船出 T2。受控鱼群设在西南侧、敌人和终局关闭，缩短重复积累时间；不能据此宣称正式战斗与经济已平衡。
- Task 4 progress: 开发／发布严格构建成功且均零错误／警告，发布验收类型剔除、四处模拟和网络程序集摘要一致（builds.json、release-isolation.json）；原生单人完成，全量与多人正在运行。

- Final review: merchant_final_review 完成唯一一次独立复核（artifacts/merchant-routes/final-review.md）。复核后保留 1 项 Important/P2：旧小岛无装备系统，生命值为默认 0，新增无条件 CanInteract 误判倒地并封锁旧商店。无 Critical 或 Minor；一次修复通道处理中。
- Final RED: ShopEligibilityUsesTheMapsActualCharacterSystem(False) 用真实单人 UDP 初始化的旧模型和实际控制器复现 NearestMerchantId=-1；正式装备地图配对用例通过，结果 1/2（legacy-shop-red.json）。修复仅将装备生命值资格限制到 Fishing 非空的地图，保留正式地图倒地检查。
- Final: Ruling: 正式战斗与经济节奏不由受控商人路线验收证明 — 本阶段保留正式价格、路线与刷新，敌人关闭的验证只证明到达和交易闭环；整体任务第 8 项仍需实测 — 若节奏不合适需调整配置并复验。
- Final: Ruling: Boss 上岸、商人航行、原岛连接与通用 NPC 对话不在本阶段新增 — 上岸为用户明确后续设计，其余仍是后续世界设计边界 — 若未来需要则独立接入，不用当前商店冒充完整系统。
- Final: Ruling: 晚加入／重连接入保留为下一任务 — 商人快照与回执不等同于可操作重连 — 若恢复流程失效将阻止总目标完成，不能据本阶段先行宣称支持。
- Final: Ruling: 验证结论限定本机真实 UDP 与独立客户端 — 不推断公网丢包和跨机器长时间可靠性 — 上线范围扩大前须增加相应压力验收。
- Final: fixed 旧小岛商店被装备生命值检查封锁 — ShopEligibilityUsesTheMapsActualCharacterSystem(False) RED→GREEN；旧地图发现、打开、保持及购买钱包检查恢复，正式地图倒地仍拒绝。Editor 全套 94/94（legacy-shop-green.json）；没有第二次独立复核。

- Task 4 complete: 最新原生 1／2／4 人分别 tick 4264／3928／7009，房主加全部客户端的完整 checkpoint 逐字节一致，运行日志无异常；实际 UI 请求回执与截图已核对。旧小岛原生升级并再次捕鱼通过，旧驱动仅比较状态 hash。开发、发布、旧小岛最终严格构建均零错误／警告，发布类型隔离与四处程序集一致。
- Task 4 full suite: `./scripts/verify.sh` → exit 0，钓鱼 335/335、重开 18/18、框架 131/131 及其他既有子套件均通过（verify-full.log）；证据汇总 verification-summary.json。此验证冻结于实时恢复框架改动之前，恢复功能将重新验收。
- Final: no deferred minors。保留共享未提交内容和 artifacts 证据，不删除其他任务工作文件。商人阶段完成，总目标继续晚加入／断线恢复与整体体验验收。

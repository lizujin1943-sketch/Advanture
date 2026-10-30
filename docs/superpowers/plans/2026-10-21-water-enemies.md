# 食人鱼与水怪实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 在正式平台玩法中加入食人鱼、水怪、水球、钩索打断与 10 米击退，并验证单人和多人一致性。

**Architecture:** 纯 C# 敌人配置、状态机、碰撞和击退通过组合接入 HookGame。Unity 只读取确认状态并展示敌人、前摇、水球与受击；完整状态和配置参与兼容检查及存档。

**Tech Stack:** .NET 8 验证工具、netstandard2.1 模拟、30 Hz 整数毫米、Unity 2022.3、既有 UDP 联机。

**Spec:** `docs/hook-island-game-design.md` 的食人鱼、水怪章节；本轮用户已确认沿用最新防御减伤，不恢复可消耗护盾。

## Global Constraints

- 仅在正式仓库开发，不覆盖角色衣装任务的未提交改动；不把 Unity、敌人或具体玩法依赖加入通用帧同步核心。
- 单人与多人使用同一模拟；配置不一致拒绝入房，恢复保留发射、打断、命中与击退进度。
- 自动验收仅在编辑器／开发构建编译且显式启用，不作为正式场景常驻组件。
- 初始正式海域 3 条食人鱼、1 只 50 kg 水怪。食人鱼 1～4 kg、4 HP/kg、1.5 秒咬击间隔、2.4 m/s、20 m 索敌、0.8 m 咬击范围；仅攻击水中未倒地玩家，同距按玩家槽位选取。
- 水球 20 HP、8 m/s、半径 0.3 m、24 m 射程；命中首个有效玩家后消失。水怪 2 秒前摇、6 秒攻击间隔、首次等待 8 秒、打断后等待 3 秒，前摇开始时锁定最近有效玩家方向，水球不追踪。
- 水球命中触发 10 m、10 m/s 的外部击退，沿飞行方向，障碍截断，击退期间停止主动移动；打断与发射同帧时先处理钩索。新水球重新开始一次击退，解除冲突救援连接。
- 食人鱼及水怪的捕获／击杀／奖励未定，本轮只实现攻击和水怪打断；水怪中钩后空钩返回。
- 战斗伤害调用现有 `CharacterSystem.Damage(..., Combat, armor)`，防御不抵挡击退；无圈环境伤害照旧。

## Review Focus

- 最大合法配置、非法状态和配置错配不能溢出、挂起或静默恢复旧数据。
- 高速水球与钩子使用连续碰撞，不能穿越角色或把后方目标当成最近命中。
- 同帧打断／发射／受击／倒地／被救有固定顺序，权威与回滚一致。
- 水怪攻击期间与水球飞行、击退中途存读档后既不漏算也不重复结算。
- 正式场景默认启用敌人，旧场景和无敌人配置仍可独立运行，开发验证逻辑不进入发布程序集。

## Task 1: 敌人规则与独立状态

**Files:** 新增 `samples/HookIsland.Simulation/Data/Configuration/EnemyConfig.cs`、`EnemyState.cs`、`EnemySystem.cs`、`EnemyStateCodec.cs`、`KnockbackSystem.cs`；新增 `tools/HookIsland.Enemy.Tests`。

**Interfaces:** EnemyConfig 为不可变房间规则；EnemySystem 创建／捕获／恢复敌人状态、推进一帧并返回命中事件；KnockbackSystem 处理连续位移；EnemyStateCodec 保存并验证全部进度。

- [x] 先写行为测试：重量伤害、防御减伤、只选落水者、前摇可打断、高速水球最近命中、击退精确距离／障碍／落水。
- [x] 运行失败测试，记录缺失行为，再实现上述服务并转绿。

## Task 2: 模拟与持久化接入

**Files:** 修改 `HookGame.cs`、`HookState.cs`、`FishingEquipment.cs`、`IslandMap.cs`、`RoomProtocol.cs` 与协议注释。

**Interfaces:** HookState 增加敌人快照，Fisher 增加击退状态；FishingConfig.Enemies 序列化规则。旧未启用敌人的配置使用显式禁用规则，正式 Unity 配置开启。

- [x] 增加并验证失败集成测试：出钩打断、伤害触发落水、救援冲突、倒地／离线排除、断点恢复与配置错配拒绝。
- [x] 按移动→钩索→敌人攻击→环境扣血顺序接入，升级格式版本；运行领域与集成测试。
- [x] 实际 UDP 单人及四客户端正常输入验收，比较完整 checkpoint hash；不注入资源或角色状态作为联机验收。

## Task 3: Unity 配置、表现与真实客户端验收

**Files:** 新增 `EnemySettings.cs`、`EnemyPresentation.cs`、`EnemySmokePilot.cs`、`EnemySmokeDriver.cs`；修改配置安装、控制器和启动脚本；新增编辑器测试。

**Interfaces:** EnemySettings.CreateConfig() 与模拟规则一致；EnemyPresentation 只读 HookState；SmokePilot 仅提交普通移动／出钩输入。

- [x] 接入可配置资产和正式平台默认配置，加入可辨识食人鱼、水怪、前摇方向提示、水球与受击反馈。
- [x] Unity 编译、编辑器测试、开发构建；单人和四个独立客户端经真实 UDP 验收伤害、落水、打断和一致性，检查截图及日志。
- [x] 构建发布包并核对开发驱动隔离；运行 `./scripts/verify.sh`。
- [x] 独立最终代码审阅并处理重要发现，更新设计／实现说明与验证证据。

## 进度与决定

- Ruling: 用户已要求直接实现，按本计划连续执行，不再次索要执行许可。
- Ruling: 复用当前工作目录的衣装／游泳基线并保留所有未提交工作；不创建不含这些依赖的新工作树，不批量提交其他任务的改动。修改前快照保存在 `artifacts/enemies/baseline/`。
- Ruling: 默认构造的旧 FishingConfig 保留无敌人的独立场景能力；正式 Unity 的 EnemySettings 默认开启敌人，敌人验证使用同一开启配置。没有测试特判。
- Task 1: 先观察新增测试缺失 EnemyConfig/EnemySystem 等类型，再实现；6/6 领域测试通过。
- Task 2: 先观察缺失 FishingConfig.enemies 参数导致集成失败，再接入；13/13 规则与集成测试通过。真实 UDP 单人及四客户端第 600 帧均有打断 1、发射 1、命中 1、咬击 5；全部 checkpoint 哈希一致，总计 16/16 通过，见 artifacts/enemies/logic-network-tests.log。
- Task 2: 沙箱禁止 Socket.Bind 后，经工具自动审批允许在沙箱外运行本机 UDP 测试；没有绕过权限或省略联机验收。

- Task 3: Unity 编辑器 23/23 通过；开发／发布构建均 0 错误、0 警告。原生单人及四客户端均在第 600 帧通过，房主和各客户端完整 checkpoint hash 一致；所有客户端观察到前摇、水球、击退、落水、食人鱼咬击和受击反馈。
- Review: 独立审阅发现水球编号／球龄合法性、最低速度斜向移动以及打断冷却少一帧，均先补充失败回归再修复；最终限定复核通过，无遗留发现。敌人专项总计 22/22 通过。
- Validation: 原生验收首次发现 PlatformSmokeDriver 与 EnemySmokeDriver 同时启动，已互斥并增加运行时单驱动验收。截图等待水球飞出嘴部后保存，方便人工核对飞行表现。
- Complete: 最终全量 `verify.sh` 退出码 0，敌人 22/22、Unity 23/23；原生单人与四人证据、截图、发布隔离和构建路径见 `docs/water-enemies-implementation.md`。没有剩余审阅发现或待执行验证。

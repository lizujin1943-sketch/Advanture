# 食人鱼捕获与水怪换位实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 食人鱼平时游动、可捕获并降低伤害；水怪按等级击退，潜水冷却并随机换位浮出攻击。

**Architecture:** 扩展纯 C# 敌人配置与快照；食人鱼通过独占钩索连接复用普通鱼收益、耐力和原子结算。水怪潜水／浮出、位置、换位序列和水球击退量全部由确定性状态驱动，Unity 只读展示。

**Tech Stack:** C# netstandard2.1、.NET 8、Unity 2022.3、30 Hz、整数毫米、现有 UDP。

**Spec:** 用户本轮规则及确认：捕获获得金币／鱼骨并延迟刷新；Lv1 击退 4 m、每级增加 2 m、初始 Lv1；食人鱼攻击降低。基于 docs/hook-island-game-design.md 与现有敌人实施说明。

## Global Constraints

- 保留 v12 复活接入和所有共享未提交改动；不停止用户当前试玩进程、不覆盖正在运行的旧包，先构建独立新包。
- 攻击降为 3 HP/kg；食人鱼无人落水时巡游、落水者优先，被钩住停止攻击。收至岸上才结算，取消／断线／倒地／耐力耗尽释放。
- 首版捕获收益使用普通蓝纹鲭鱼同档：6 金币/kg、每向上取整 kg 2 鱼骨、1.2 点耐力/kg/s，10 秒后刷新。均可配置，收益和回执不可重复。
- 水怪等级 1～10，击退 `4+(等级-1)*2` m；其他伤害不随等级变化。首次沿用初始位置，之后选择存活在线玩家，在射程内不同的开放水面随机换位，不能出现在地板上。无合法位置则继续潜水等待。
- 前摇 2 秒保持可打断，发射或打断后潜回；潜水不可中钩。随机序列、所有者、捕获刷新、飞行起点和击退总距离都写入快照。

## Review Focus

- 两人同帧抢钩、一人倒地／离线及中途存档不能遗留归属、重复掉落或扣耐力。
- 最大等级和配置边界，新增地块覆盖预定出水点，全部目标离线／倒地时应有合法等待行为。
- 不同浮出位置的多个水球共存时，不可用当前水怪位置验证旧弹道。
- 潜水不挡钩，前摇最后一帧仍可打断；随机换位不能落在上次位置或陆地。
- 旧格式明确拒绝，配置不一致无法入房；单人与多人使用同一规则。

## Task 1: 规则和快照

**Files:** EnemyConfig.cs、EnemyState.cs、EnemySystem.cs、EnemyStateCodec.cs、KnockbackSystem.cs；新增 PiranhaFishing.cs、EnemyPlacement.cs；tools/HookIsland.Enemy.Tests。

**Interfaces:** EnemySystem 提供 TryHook／Release／Follow／CompleteCapture，玩家保存 one-based HookedPiranha；Monster 保存位置、等级、出水序列和潜入进度；Advance 接受合法水面谓词。

- [x] 写并观察巡游、捕获归属、等级击退、潜水换位及恢复测试失败。
- [x] 实现配置／状态／规则并运行专项测试转绿。

## Task 2: 捕获和 Unity 接入

**Files:** HookGame.cs、HookState.cs、FishingCatalog.cs、EnemySettings.cs、EnemyPresentation.cs、IslandFishingController.cs、EnemySceneTests.cs、协议版本文件。

- [x] 写并观察真实出钩捕获、岸上结算、耐力／掉线释放、快照恢复测试失败。
- [x] 接入捕获、自动瞄准候选、等级／浮出／潜入表现，更新配置资产与格式。
- [x] 专项和 Unity 编辑器验证；正常输入驱动验证单人与四人捕获／打断／换位／落水。

## Task 3: 验收与交付

- [x] 全量 verify.sh、开发与发布构建、开发驱动隔离、原生客户端与截图检查。
- [x] 技能要求的独立最终审阅，重要发现用失败回归修复；更新设计／实施文档。

## 决定与进度

- 用户已确认执行方向与两项主要细则，继续当前任务，不另设执行审批。复用共享目录并保存本轮基线于 artifacts/enemies-v2/baseline，不批量提交其他任务改动。
- 自行选定的首版调参：伤害降低 25%；捕获收益／消耗对齐普通鱼同档，刷新 10 秒；等级上限暂为 10。后续可改配置，代价为重新导出房间配置和开新局。

- Task 1: 完成规则及快照；初始 6 个演化测试 RED→GREEN，新增多起点弹道和所有权恢复验证。
- Task 2: 捕获接入测试先 0/4，再通过正常出钩、取消、耗尽、掉线和倒地用例；Unity 配置/隐藏表现先 2 条失败，调整后整套 33/33。
- Ruling: 共享工作区存在其他任务改动，本轮不创建提交，以 artifacts/enemies-v2/baseline 与限定文件差异做最终复核；避免混入未验收内容，代价是尚无独立提交可回滚。
- Ruling: 同期房间重开任务独占网络协议并升至 14；本轮规则/状态 13、输入 7、HIM9。旧客户端和存档明确不兼容，代价是旧局不能迁入。
- Ruling: 随机近距离选点失败时，遍历整个射程内的地块中心，避免大平台永久消除水怪攻击。代价是扩建后部分出水点会超过初始 4 m 钓竿距离，可以躲避或升级钓竿。
- 最终独立审阅：无 Critical；Important 八方向遗漏水面缺口与 Minor 身体擦地板边缘，均用 5 条失败回归修复并转绿；失败重试也保存随机进度。详情见 artifacts/enemies-v2/review-resolution.md。
- Task 2 最终结果：敌人专项 45/45，Unity 33/33；最终原生单人第 711 帧、四人第 600 帧通过，房主与客户端完整状态一致。开发/发布包均 0 错误、0 警告，发布隔离通过。
- Task 3 完成：完整 verify.sh 退出 0（artifacts/enemies-v2/full-verify-final.log），包含复活 26/26 和重开网络 10/10。最终原生包截图与日志已核对，详情见 docs/water-enemies-implementation.md。本轮验收为规则/状态 v13；共享目录后续新增功能不计入这份结果。

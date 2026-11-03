# 沙漠之鹰实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 接入可购买、装备、快捷切换的沙漠之鹰，包含移动散布、50% 友伤、换弹及动画，保持联机、恢复与存档一致。

**Architecture:** 玩法与弹道留在纯 C# 示例层；库存独占弹匣与备用弹事务。网络传递开火／换弹意图，确认事件驱动命中和声音，Unity 单独负责输入、姿态与 HUD。

**Tech Stack:** C# / .NET 8 测试宿主 / netstandard2.1 模拟与网络 / Unity 2022.3.62f1。

**Spec:** `docs/superpowers/specs/2026-11-02-desert-eagle-design.md`。用户已于同日明确授权按此设计实现。

## Global Constraints

- 仅在 `/Users/lizujin/Documents/project/git_project/Advanture` 开发；Unity 项目必须核实为其 `samples/TacticalGame.Unity`。
- 不修改通用帧同步核心来耦合枪械；不使用 Unity Physics／Random 作为权威命中或散布。
- 35 伤害、12 m、7 发、18 帧间隔、54 帧换弹、9 帧举枪、6 帧收势；友伤 50%，与护甲合并后一次向上取整。
- 散布 0.05／1.5／3／4.5 m/s 对应 0.5／2／6／10°；扩大 60°/s、恢复 20°/s。
- 弹药 14 发／30 G，99 堆叠；枪 300 G；商店每批 2 枪／6 份弹药。
- 新增物品类型与协议必须显式编解码和校验；失败请求不重放、交易不部分提交。
- 持枪不覆盖现有快捷栏绑定，不新增常驻十字准星；R 优先有效救援。
- 当前分支 `f_1`，保留已有设计文档修改，当前工程内直接完成集成；不操作其他 Unity 项目，不自动提交或推送。

## Review Focus

1. 换弹完成帧同时换枪／有效救援／倒地：先取消，再判断转移，不凭空补弹。
2. 弹药购买在背包空间不足、重复请求、多人争抢最后库存时全部原子结算。
3. 移动散布、切枪、重连与读取检查点不重新抽取同一发随机结果。
4. 友伤关闭推搡时仍扣血；落水／施救／被钩救时伤害与位移资格分离。
5. 保存等待举枪、收势及换弹；纯冷却与散布恢复保存剩余值，不用动画事件结算。

## Task 1：目录、库存与商店事务

**Files:** `Data/Catalogs/FirearmCatalog.cs`、`Data/firearms.csv`、`Data/ammunition.csv`、`InventoryCatalog.cs`、`InventoryState.cs`、`InventorySystem.Firearms.cs`、既有库存／商人／货架与编解码文件。

**Interfaces:** 追加 `ItemKind.Firearm`、`ItemKind.Ammunition`，枪实例 `LoadedAmmo`；`FirearmCatalog.Get(int)` 与 `AmmunitionCatalog.Get(int)`。`InventorySystem.TryFireRound(int owner,int weaponId)` 原子消耗弹匣；`ReloadFirearm(int owner,int weaponId)` 返回实际装填发数；`AmmunitionBalance(int owner,int type)` 查本人背包。

- [x] 写库存购买、部分弹匣、转交、99 堆叠、售罄与事务失败测试并运行。
- [x] 实现目录、库存及消耗账本、商店批次库存、快捷栏引用与严格序列化。
- [x] 运行对应测试，检查无空间购买与恢复后的数量守恒。

## Task 2：确定性射击、散布、换弹与存档

**Files:** `Logic/Combat/FirearmConfig.cs`、`FirearmRules.cs`、`FirearmSpread.cs`、`Data/State/FirearmState.cs`、`Serialization/FirearmStateCodec.cs`、`Logic/Simulation/HookGame.Firearms.cs`；既有 HookInput／HookGame／角色移动／事件／版本及 IslandSaveSafety。

**Interfaces:** `Fisher.Firearm` 动作状态；`HookInput.Reload` 边沿；`FirearmRules.Equipped(HookState,Fisher)`、`FirearmRules.CanAct(...)`；`FirearmShot`／`FirearmReloadStarted`／`FirearmReloaded` 确认事件。

- [x] 先验证散布曲线、恢复帧数、确定性随机、近障碍和友伤公式。
- [x] 射线沿采样方向查询最近实体；两发击倒食人鱼、前摇水怪打断、不侵占鱼获结算。
- [x] 将动作互斥、R 救援优先、受击取消与所有库存入口接入主循环。
- [x] 保存检查点包含弹匣、弹药、散布与动作阶段；主动保存等待安全点；更新版本／地图生成入口。
- [x] 运行领域测试、四客户端与恢复一致性检查。

## Task 3：Unity 表现、输入和商店 UI

**Files:** `Presentation/Characters/FirearmAvatarView.cs`、`Presentation/Art/DesertEagleArt.cs`、枪械效果与散布提示类；既有 FishingPresentation、输入／救援控制器、背包、商店、快捷栏、设置与图标。

- [x] 创建厚实银灰枪体、可动弹匣与套筒，复用角色动画层实现双手持枪、射击、换弹和收枪。
- [x] 输入使用当前视角瞄准、单击开火、R 动作归属；购买与背包切装均使用现有事务通道。
- [x] 确认事件驱动弹道／枪声／枪焰／命中，散布短弧及弹药读数只读显示。
- [x] Unity 编译、专项编辑器检查与截图验证，确认正确工程和场景，验证第三人称／俯视。

## Task 4：回归、独立审查与交付

- [x] 运行枪械专项、现有近战／库存／食物／救援回归，及四客户端真实网络比较。
- [x] 运行完整 `./scripts/verify.sh`：原宝箱四客户端用例首次超时，其余已到达项目通过；保持断言重跑该项通过，剩余套件逐项补跑通过。详见实施记录，不宣称完整入口一次全绿。
- [x] 对全部新增规则与 Unity 接入做独立审查，修复实质问题后复验相关项。
- [x] 写实施与验证记录；将设计主文档更新为真实接入范围，明确未覆盖的实机条件。

## 工作记录

- 已确认 Unity MCP 工程路径，实例 `TacticalGame.Unity@c147ce230fd064b2`。
- 读过既有输入编码、库存、商店、近战动作和保存安全点实现；按以上边界分工，所有共享文件由指定负责人单独修改。

- 实施完成，枪械 20 项、Unity 表现 5 项、库存 47 项及相关回归通过；独立复审无剩余实质问题。
- 实机截图验证购买、背包、快捷栏、持枪／射击／换弹和两种视角；临时工具、资金与验收存档均隔离于忽略的 artifacts 目录。
- 验证范围及旧档格式变化见 `docs/desert-eagle-implementation.md`；未对长期手感、Steam／外网或四台实体设备作完成声明。

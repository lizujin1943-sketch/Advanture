# 手榴弹首版实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 接通每人一颗、鼠标落点投掷、落地延时爆炸的联机手榴弹。

**Architecture:** 物品、命令、整数轨迹、伤害和快照属于 HookIsland.Simulation；请求交付属于 HookIsland.Network；Unity 只采集操作和显示状态。通用 FrameSync 代码不加入投掷物概念。

**Tech Stack:** C# / .NET 8 验证、netstandard2.1 模拟、Unity 2022.3。

**Spec:** [手榴弹设计](../specs/2026-11-04-grenade-design.md)

## Global Constraints

- 全部产出在正式仓库内；沿用当前功能分支与已连接的 Unity 工程，保留既有 packages-lock 修改。
- 30 Hz；最远 18000、最近 1500、误差 350、爆炸半径 4500 毫米。
- 落地后 24 帧起爆，内圈 1500 毫米伤害 120，边缘伤害 20；包含自己与队友。
- 扣物品与生成同帧完成；成功失败均去重；飞行和落地状态可保存恢复。
- 镜头只沿当前方向后拉 2 米，释放鼠标选地面；取消、失焦及 UI 中断不扔出。
- 中文代码注释、中文提交说明；不提交生成 DLL、测试结果与临时验证脚本。

## Review Focus

- 取消后松手、失焦后恢复：不会延后投掷。
- 请求重复或比当前回执旧：不补扣、不重新生成；目标首次提交后冻结。
- 生成帧不移动，落地与爆炸帧边界准确；断线后已生成手雷继续存在。
- 空中、落地等待和出手前恢复存档：状态及最终结果一致。
- 第一／第三人称及俯视的镜头与人物可见性恢复；第四槽不会被既有上限拒绝。

## 文件职责

- `Data/Configuration/GrenadeConfig.cs`、`Data/State/GrenadeState.cs`：配置、待出手动作、飞行对象。
- `Commands/GrenadeCommand.cs`、`Serialization/GrenadeStateCodec.cs`：冻结目标的请求与快照校验。
- `Logic/Throwables/GrenadeRules.cs`、`Logic/Simulation/HookGame.Grenades.cs`：共享预览规则与权威推进、伤害。
- 既有 InventoryCatalog / WeaponLoadout / InventorySystem / EquipmentSystem：真实库存与第四槽。
- HookInput 和各个输入组合器：完整保留或互斥投掷请求；同步更新兼容版本。
- Network 的 GrenadeRequestBuffer：同一请求重发至最终确认。
- Unity 的 GrenadeController / GrenadeView 及现有输入、镜头、HUD：本地瞄准状态与确认反馈。
- `tools/HookIsland.Grenade.Tests`、`scripts/verify-hook-grenades.sh`：规则、恢复、输入重放与四客户端真实 UDP。

### Task 1: 确定性手雷与库存

**Interfaces:** `GrenadeCommand(RequestId, ItemId, TargetX, TargetZ)`；`HookInput.Grenade`；`HookState.Grenades`；`Fisher.Grenade`。规则预览返回起终点、飞行帧与路径有效性，供 Unity 与模拟共同调用。

- [x] 新增测试：初始每人一颗、第四槽、命令往返与非规范字节拒绝；运行得到缺少接口的失败。
- [x] 实现配置／物品／输入和快照，接通请求、固定出手帧、原武器回切；旧档通过版本拒绝。
- [x] 新增并运行轨迹、误差、距离／遮挡、落地 24 帧、衰减、自伤友伤、部位去重、失败不扣及中断测试。
- [x] 新增空中／落地／出手前快照恢复和重复输入测试，确认字节一致。

### Task 2: Unity 操作与表现

**Interfaces:** 消费 Task 1 的共享规则及状态；`GrenadeRequestBuffer` 保留首次目标。控制器只提交命令，不写模拟状态。

- [x] 实现请求交付，检查所有 HookInput 复制与组合路径并补对应测试。
- [x] 接入 4 拿出、左键按住／松开、右键取消、换武器取消、UI／失焦中断与重新按键限制。
- [x] 镜头保留模式与方向并后拉，释放鼠标；共享整数规则生成轨迹和范围预览。
- [x] 根据状态显示手持／飞行手雷与落地警示，确认事件只播放一次爆炸、声音和震动；HUD 纯文字数量。
- [x] 同步 DLL，Unity 编译、针对性 EditMode 测试和运行截图验收。

### Task 3: 联机验收与交付

**Interfaces:** 使用正常 IslandHost / IslandClient、正式输入和 IslandSaveStore；不注入发布玩法组件。

- [x] 四客户端真实 UDP 同时投掷、重试和空中／落地保存恢复；结束比较完整 checkpoint，断线后寿命以正式成员停用事件验证。
- [x] 执行手雷、库存、枪械及相关回归；按改动范围运行仓库验证入口，修复旧夹具后按组完成剩余验收。
- [x] 独立复查全部改动，修复影响正确性的问题并复验。
- [x] 补实现文档与实际验证边界，中文提交并推送 GitHub。

## 实际验证边界

手雷专项 15 项，战斗占位专项 19 项，Unity 表现专项 11 项已通过。按仓库入口范围完成分组回归，钓鱼主测试 423 个用例逐名核对均通过，背包、角色、游泳、敌人、近战、枪械、螃蟹、复活、重开和 BOSS 各组均通过；详见实现文档。运行场景用普通输入接口完成一次投掷；桌面锁定，实际键鼠选点手感没有验收。独立复查发现的问题均已修复并复验，无剩余阻塞项。验证环境为本机 macOS，本轮未做 Windows 实机验证。

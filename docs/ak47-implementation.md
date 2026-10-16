# 第一把武器：AK-47

本次按用户确认实现持枪、连射、换弹、命中扣血、死亡与重生，接入现有双人帧同步试玩。

## 玩法默认值

这是试玩平衡参数，不是现实枪械参数模拟：默认装备 AK-47，30 发弹匣、90 发备弹；每秒 10 发，换弹 2.4 秒；100 点生命、每次命中 25 点伤害；死亡后 3 秒在原出生点重生并补满装备。左键按住连射，R 换弹。暂不增加爆头倍率、散布、穿透、拾取、换枪或弹道飞行。

## 实现约定

- 框架不增加任何枪械或地图类型。所有规则在 TacticalGame.Simulation，输入与显示在 Unity 示例。
- 射击是瞬时射线：从角色眼睛位置出发，用冻结的水平和俯仰角计算。固定整数三角表、整数射线交点，避免各端使用不同浮点物理结果。
- 地图仍在二维平面移动，但障碍增加高度区间供子弹遮挡。模型碰撞盒由编辑器导出；低掩体可从上方射击。角色使用固定长方体命中范围。
- 一帧先完成所有角色移动与计时，再收集所有射击，最后统一结算伤害，允许同帧互相击杀。
- 弹药、冷却结束帧、换弹结束帧、重生帧、生命、战绩、射击序号和最近射线都进入完整状态，参与校验、回滚与保存恢复。
- 第一版枪声、闪光、弹道线和命中提示根据确认状态播放，以射击序号去重，预测重算不重复播放声音。
- 升级游戏规则、输入、状态、地图和房间兼容标识，旧版试玩不能与新版混用；不迁移旧试玩快照。

## 验收

覆盖开火标志、射速、空弹匣、换弹与弹药守恒、高低障碍、俯仰、射程、死亡禁用操作、自动重生、同帧互杀、战斗状态恢复重放、双客户端一致性与 Unity 画面。

## 代码阅读顺序

| 文件 | 职责 |
| --- | --- |
| `samples/TacticalGame.Simulation/TacticalInput.cs` | 保持 9 字节输入，增加持续开火和一次换弹请求。 |
| `samples/TacticalGame.Simulation/CombatState.cs` | 可保存的生命、弹药、计时、战绩和射线；内部工作副本不会泄漏给 Unity。 |
| `samples/TacticalGame.Simulation/TacticalGame.cs` | 先移动和更新计时，再调用战斗结算；编码、解码和恢复完整状态。 |
| `samples/TacticalGame.Simulation/TacticalGame.Combat.cs` | 射速、换弹、最近命中、遮挡、批量扣血、死亡重生。 |
| `samples/TacticalGame.Simulation/FixedAim.cs` | 固定整数正弦表与方向换算，运行时不用浮点三角函数。 |
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/Ak47Model.cs` | 原创程序化低多边形枪模，只有网格与材质，没有物理碰撞体。 |
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/Ak47View.cs` | 两种持枪视角、合成枪声、闪光、射线和表现去重；本地武器相机使用预留 31 层防止贴墙穿模。 |
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/NetworkInputSource.cs` | 左键持续开火，R 换弹；重新锁定鼠标的第一次点击不直接开枪。 |
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/NetworkRoomOverlay.cs` | 确认生命、弹药、击杀/死亡、命中与受伤反馈、重生倒计时。 |

本地相机位置继续使用预测状态；弹药、生命和枪声使用确认状态。正常网络下射击反馈会包含输入提前量和网络确认延迟。当前只保存最近一枪用于画面表现，积压多帧时不会补播整串过期枪声。此版不提供独立 FBX，模型由可读 C# 代码生成，可在后续替换美术资源而保留射击规则。

## 本次实际结果（2026-10-16）

- 完整 `./scripts/verify.sh` 退出码 0；包含 23 项庭院/战斗/真实 UDP 测试。
- Unity EditMode：22 项通过，无失败、无跳过；macOS Mono Development Build 成功，0 错误、0 警告。
- 两个独立客户端自动对战至第 360 帧，双方各开 5 枪、各击杀与死亡一次，均已重生；验证了换弹和本地/远端射击表现。
- 服务器与 A/B 完整检查点摘要相同，3 份自动诊断均正常结束，没有状态分歧。
- [对战报告](../artifacts/tactical-network/smoke-20261016-181012-b67fbdb0/result.json)、[A 画面](../artifacts/tactical-network/smoke-20261016-181012-b67fbdb0/host.json.png)、[B 画面](../artifacts/tactical-network/smoke-20261016-181012-b67fbdb0/join.json.png)。
- 验证为同一台 Mac 的两个独立进程；本次未新增 Windows 或跨电脑验证。自动对战走正式输入冻结与 UDP 通路，使用开发构建的自动输入源；键鼠量化另有 Unity 测试，枪声听感和鼠标射击手感仍可继续试玩调整。

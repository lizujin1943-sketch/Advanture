# 第一把武器：AK-47

本次按用户确认实现持枪、连射、换弹、命中扣血、死亡与重生，接入现有双人帧同步试玩。

## 玩法默认值

这是试玩平衡参数，不是现实枪械参数模拟：默认装备 AK-47，30 发弹匣、360 发备弹；每秒 10 发，换弹 2.4 秒；100 点生命、每次命中 25 点伤害；死亡后 3 秒在原出生点重生并补满装备。左键按住连射，R 换弹。要塞的西侧军火库和西北装备库可按 E 补满个人备弹。暂不增加爆头倍率、散布、穿透、拾取、换枪或弹道飞行。

## 实现约定

- 框架不增加任何枪械或地图类型。所有规则在 TacticalGame.Simulation，输入与显示在 Unity 示例。
- 射击是瞬时射线：从角色眼睛位置出发，用冻结的水平和俯仰角计算。固定整数三角表、整数射线交点，避免各端使用不同浮点物理结果。
- 地图仍在二维平面移动，但障碍增加高度区间供子弹遮挡。模型碰撞盒由编辑器导出；低掩体可从上方射击。角色使用固定长方体命中范围。
- 一帧先完成所有角色移动与计时，再收集所有射击，最后统一结算伤害，允许同帧互相击杀。
- 弹药、冷却结束帧、换弹结束帧、重生帧、生命、战绩、射击序号和最近射线都进入完整状态，参与校验、回滚与保存恢复。
- 枪声、枪口焰和命中提示只在收到新的确认射击序号时播放。曳迹和尘屑取自同一快照的射段，换弹姿态和动作音按确认的结束帧对齐。
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
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/Ak47Model.cs` | 加载近景和远景 FBX、共享材质以及手部动作节点。 |
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/Ak47View.cs` | 驱动两种持枪视角、确认事件去重和本地武器相机的 31 层。 |
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/Ak47Motion.cs` | 上场、持枪、走动、起落、后坐和换弹动作，闭合手型跟随美术资源中的握持点。 |
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/Ak47ShotEffects.cs` | 枪口焰、余烟、短曳迹、环境尘屑及角色命中反馈。 |
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/Ak47Audio.cs` | 选择原创音效变体，按听者位置混合近场与远场，播放三个换弹动作音。 |
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/NetworkInputSource.cs` | 左键持续开火，R 换弹；重新锁定鼠标的第一次点击不直接开枪。 |
| `samples/TacticalGame.Unity/Assets/TacticalGame/Network/NetworkRoomOverlay.cs` | 确认生命、弹药、击杀/死亡、命中与受伤反馈、重生倒计时。 |

本地相机位置继续使用预测状态；弹药、生命和枪声使用确认状态。正常网络下射击反馈会包含输入提前量和网络确认延迟。当前只保存最近一枪用于画面表现，积压多帧时不会补播整串过期枪声。

两套 FBX 共用一套 2048 像素 PBR 贴图。可编辑 Blender 源、贴图和导出流程见[模型目录](../source-assets/props/ak47/README.md)。音效制作和试听见[声音目录](../source-assets/audio/ak47/README.md)。

## 要塞基地表现验收（2026-11-20）

- 将枪身侧视渲染与参考照片按可见全长对齐。初版、上一版和本版的轮廓交并比分别为 0.661、0.872、0.901。照片未展示的厚度、背面和端面沿用项目美术设定，并用多角度灰模检查。[侧面对照](../artifacts/fortified-compound-game/ak47/AK47_ProfileCompare.jpg) · [多角度灰模](../artifacts/fortified-compound-game/ak47/AK47_Turnaround.jpg)
- 用同一机位重渲染修改前后的导出网格，对齐用户提供的局部照片。握把顶端与机匣的缝隙已闭合，握把下缘和护圈的圆角按照片重塑。握把轮廓的交并比从 0.791 提高到 0.946，护圈内侧留白从 0.630 提高到 0.904。[照片与新版](../artifacts/fortified-compound-game/ak47/AK47_ReceiverCompare.jpg) · [修改前后](../artifacts/fortified-compound-game/ak47/AK47_ReceiverIterations.jpg) · [斜侧和背面](../artifacts/fortified-compound-game/ak47/AK47_ReceiverAngles.jpg)
- 核对了近景和远景模型的材质、握持点及换弹时的手部接触。独立 GLB 内含枪身网格及三张贴图，可单独旋转查看。[枪身模型](../artifacts/fortified-compound-game/ak47/AK47.glb) · [材质近景](../artifacts/fortified-compound-game/ak47/AK47_Armory.jpg)
- 在 macOS 发布版的中央广场录制了点射、连续开火、完整换弹和换弹后的第一发，同时采集游戏窗口的画面和应用的立体声输出。[实机录像](../artifacts/fortified-compound-game/ak47/AK47_Gameplay.mp4)
- 本轮只修改游戏美术资源，复验了两档 FBX 导入、持枪画面和双人实机。Unity EditMode 的 41 项测试全部通过，服务器和两个 macOS 客户端在第 360 帧的完整状态一致。上轮已通过战术示例的 44 项测试，本轮未重跑全仓回归。[Unity 测试](../artifacts/fortified-compound-game/ak47/UnityEditMode.xml) · [双人对战报告](../artifacts/tactical-network/smoke-20261120-172651-ae4018ab/result.json)
- 全仓回归中，钩索小岛的四人装备切换用例在单独重跑后通过。该示例的四人水怪战斗用例重跑后仍然失败，最后一次停在食人鱼击杀结算。[本次重跑记录](../.build/ak47-studio/slim/crab-repeat.log)

## 初版验收（2026-10-16）

- 完整 `./scripts/verify.sh` 退出码 0；包含 23 项庭院/战斗/真实 UDP 测试。
- Unity EditMode：22 项通过，无失败、无跳过；macOS Mono Development Build 成功，0 错误、0 警告。
- 两个独立客户端自动对战至第 360 帧，双方各开 5 枪、各击杀与死亡一次，均已重生；验证了换弹和本地/远端射击表现。
- 服务器与 A/B 完整检查点摘要相同，3 份自动诊断均正常结束，没有状态分歧。
- [对战报告](../artifacts/tactical-network/smoke-20261016-181012-b67fbdb0/result.json)、[A 画面](../artifacts/tactical-network/smoke-20261016-181012-b67fbdb0/host.json.png)、[B 画面](../artifacts/tactical-network/smoke-20261016-181012-b67fbdb0/join.json.png)。
- 验证为同一台 Mac 的两个独立进程；本次未新增 Windows 或跨电脑验证。自动对战走正式输入冻结与 UDP 通路，使用开发构建的自动输入源；键鼠量化另有 Unity 测试，枪声听感和鼠标射击手感仍可继续试玩调整。

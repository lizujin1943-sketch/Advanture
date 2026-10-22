# 落水、游泳圈与救援

状态：已完成共享模拟、Unity 场景接入和本节列出的验收。完成记录见 `superpowers/plans/2026-10-21-swimming.md`。

## 规则与配置

`SwimmingConfig` 随房间配置序列化。默认无圈速度为 1 米/秒、游泳消耗 10 耐力/秒、落水伤害 5 HP/秒；生命和耐力的配置精度为千分之一点/秒。伤害直接作用于生命，绕过防御（角色 v10 已取消护盾资源）。

`Data/swim-rings.csv` 提供设计文档中的 10 级泳圈。圈占 2×2、不堆叠，只在游泳圈槽装备时生效，不提供耐力上限或岸上速度加成。备用圈仍属于统一库存。

出钩当帧至钩索完全收回前锁定主动行走和游泳，取消后仍等空钩返回。队友拖拽可移动角色，被锁定的游泳输入不扣游泳耐力；自己的收鱼消耗继续。无效出钩不锁定移动。水中鱼或浮木到达身边后保留钩索，须由队友拖回有效地面才结算；可右键放弃后收钩自救。

游泳和收鱼共用一个耐力预算。末帧不足时，按剩余耐力能够支持的时间移动；耐力耗尽仍未上岸的鱼挣脱。无耐力时按住游泳方向不触发自然恢复；松开方向、结束自己钓竿动作后，沿用钓竿配置中的等待与恢复速度。外部钩救不消耗游泳耐力。

落水是环境状态，倒地是生命状态，二者分别保存。HP 为零者不能自主移动、出竿或恢复耐力，仍可以被钩回地面；救援不会自动复活、回血或补满耐力。

## 职责

- `SwimmingSystem`：权限、整数伤害、共享耐力预算与位移余数。
- `SwimmingTerrain`：水陆和障碍查询、登岸占位检查、救援落脚点及完整移动路径。
- `SwimmingStateCodec`：水中状态、活动标志、伤害和位移余数的保存与边界检查。
- `SwimRingCatalog`：只读等级表；不另存物品实例。
- `SwimmingSettings`、`SwimmingAppearance`：Unity 配置与泳圈/水面涟漪表现；不修改权威状态。

规则／状态／房间协议版本为 9，输入格式仍为 6，配置化地图格式为 5。旧版带装备配置的地图、存档和房间不能混用；旧岛屿无装备模式继续保留。地块新增后，在钩索和伤害结算前刷新每名角色的水陆状态，倒地或出钩锁定者也会立即上岸。

## 验证入口

`./scripts/verify-hook-swimming.sh` 包含领域、游戏集成、四客户端 UDP 验证，日志输出到 `artifacts/swimming/`。单独运行时支持用例名称子串筛选，例如 `./scripts/verify-hook-swimming.sh domain`。

联机沿用当前房间生命周期：成员断线会结束整场会话，不把单帧的钩索释放验证解释为房间断线续玩或重连支持。

复苏、单人/全队倒地后的脱困、食物和敌人伤害不由本次新增规则定义；不引入计时传送来替代这些尚未设计的流程。

## Unity 操作与验收

场景为 `samples/TacticalGame.Unity/Assets/HookIsland/Scenes/HookPlatform.unity`。WASD 移动/游泳，鼠标左键出钩，右键放弃目标并收回空钩；钩子完全收回后恢复移动。E 打开商店，Tab / I 打开角色/背包页，泳圈装备到“游泳圈”槽后立即生效。水中可从背包装卸泳圈，不能向海面丢装备或卸下背包。

配置位置：`Assets/HookIsland/Data/SwimmingSettings.asset` 中设置掉血、无圈泳速与游泳消耗；`FishingSettings.asset` 中设置休息等待与恢复速率。10 级泳圈表位于 `samples/HookIsland.Simulation/Data/swim-rings.csv`。配置随房间兼容信息同步，配置不一致的客户端会拒绝加入。

2026-10-21 的实际验收：

- 完整 `./scripts/verify.sh` 成功退出（0），所有测试组通过；落水/泳圈/移动锁专组 **27/27**，含精确边界铺地快照往返和延迟输入用例。完整日志：`artifacts/swimming/full-verify.log`。
- Unity EditMode **40/40**，包括角色面板原生拖拽回归、泳圈网格朝向、配置与地图字节一致性；`artifacts/swimming/unity-editmode.json`。
- 四个独立 macOS 客户端通过普通输入完成捕鱼赚取 171 G、支付 150 G 购买/穿戴 Lv1 泳圈、水中出钩锁移动及队友救援。最终 tick **600**，主机与四端快照哈希均为 `01d117fc81d0b7f37b50f4b54509407c915be74dad0e566e8728312cfc125fcd`。
- 四端 `lockObserved=true`。P1 有圈水中 HP100；P3 被救上平台后 HP93；P4 无圈水中 HP83。奖励和伤害均由正式模拟结算。
- 四端日志、购物与水中画面：`artifacts/hook-island-playable/swimming-smoke-20261021-155459-4d74ca/`；未发现运行异常。汇总为 `artifacts/swimming/unity-party.log`。
- 开发、发布构建均 0 错误、0 警告。发布产物 `artifacts/swimming/release/HookPlatform.app`；程序集元数据核实泳圈/钓竿/背包/平台自动验收组件及输入驱动均未进入发布程序集，真实配置和表现类保留，见 `artifacts/swimming/release-isolation.json`。

重复四端验收（先构建 Development 版本 `artifacts/hook-island-playable/HookPlatform.app`）：

```sh
python3 scripts/run-island-party.py --swimming --players 4
```

该流程只在开发构建且显式传入参数时运行，不是正式场景的常驻组件，不注入金币或装备。延迟输入用例也链接到正式 `HookIsland.Swimming.Tests`，不会被预处理静默排除。

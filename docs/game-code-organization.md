# 钓鱼游戏代码目录

目录按“职责 → 玩法或显示功能”组织。查数值先看 `Data`，查结算看 `Logic`，查画面看 `Presentation`，查房间与存档看 `Network` 和 Unity 的 `Application`。

此次整理只搬迁文件，没有改变类名、命名空间、程序集、方法内容、配置数值或存档格式。目录便于定位职责；编译边界仍由现有 `.csproj` 和 `.asmdef` 决定，没有新增程序集隔离。

## 纯 C# 游戏数据与逻辑

位置：[samples/HookIsland.Simulation](../samples/HookIsland.Simulation)。不依赖 Unity。

```text
HookIsland.Simulation/
├── Data/
│   ├── Configuration/    角色、连击、敌人、商人等配置
│   ├── Catalogs/         鱼、钓竿、装备和物品表的读取
│   ├── State/            角色、库存、任务、世界等确认状态
│   └── *.csv             原有数值表，保留内嵌资源路径
├── Commands/             输入与库存、复活、房间规则等操作意图
├── Events/               游戏结算事件及其负载定义
├── Logic/
│   ├── Simulation/       HookGame 的确定性更新与各 partial 文件
│   ├── Fishing/          钓鱼、钓具、挣脱、协作、连击
│   ├── Characters/       属性、移动、伤害与耐力
│   ├── Inventory/        背包布局、装备和材料事务
│   ├── Combat/           敌人、击退与近战
│   ├── Construction/     平台铺设与连接规则
│   ├── World/            地形查询、浮木与海域机会
│   ├── Merchants/        商品、库存、补货与商人规则
│   ├── Progression/      委托与终局目标
│   ├── Revival/          倒地、救援与复活
│   ├── Swimming/         落水、游泳及救上岸查询
│   └── Treasure/         宝箱运动
└── Serialization/        状态编解码、恢复校验与不变量检查
```

入口示例：[整体状态](../samples/HookIsland.Simulation/Data/State/HookState.cs)、[主模拟循环](../samples/HookIsland.Simulation/Logic/Simulation/HookGame.cs)、[库存事务](../samples/HookIsland.Simulation/Logic/Inventory/InventorySystem.cs)、[结算事件](../samples/HookIsland.Simulation/Events/HookGameplayEvent.cs)。

部分历史文件同时包含小型数据类型和规则，例如 `FishingEquipment.cs`、`PlatformRules.cs`、`MeleeConfig.cs`，按主要职责放在对应 `Logic` 功能目录。`Data/Catalogs/FishingCatalog.cs` 则保留与目录表相关的奖励计算、耐力消耗和确定性生成辅助逻辑，因此当前 `Data` 不是严格禁止逻辑的边界。本次保持文件内容不变，没有借目录整理再次拆类；后续新增规则仍按下方放置规则归类。

## 联机与存档

位置：[samples/HookIsland.Network](../samples/HookIsland.Network)。独立于 Unity 输入和画面。

| 目录 | 内容 |
| --- | --- |
| `Session` | 客户端、房主和会话推进 |
| `Room` | 房间协议、席位、重连凭据与重开屏障 |
| `Commands` | 操作请求缓冲、重发与确认回执 |
| `Events` | 交付已确认的游戏事件 |
| `Persistence` | 存档数据、编码、安全保存条件、原子文件提交和本机身份 |
| `Properties` | 程序集内部访问声明 |

入口示例：[客户端](../samples/HookIsland.Network/Session/IslandClient.cs)、[确认事件](../samples/HookIsland.Network/Events/ConfirmedGameplayEvent.cs)、[存档文件服务](../samples/HookIsland.Network/Persistence/IslandSaveStore.cs)。

## Unity 输入、装配与显示

位置：[Assets/HookIsland/Runtime](../samples/TacticalGame.Unity/Assets/HookIsland/Runtime)。

```text
Runtime/
├── Composition/          主控制器与场景装配
├── Application/
│   ├── Commands/         购物、拾取、建造、复活和宝箱的交互入口
│   ├── Room/             本地房间生命周期
│   └── Saves/            手动/自动保存排队与继续游戏
├── Input/                设备输入、镜头输入、瞄准与按钮暂存
├── Data/
│   ├── Settings/         Unity 可编辑的玩法配置类型
│   └── Visuals/          角色、鱼、图标和水花的美术配置类型
├── Presentation/
│   ├── Camera/           镜头视图及显示用阴影
│   ├── Characters/       角色模型、动画、换装与近战外观
│   ├── Fish/             鱼模型、鱼影及挣脱表现
│   ├── Water/            水花、游动尾浪和粒子
│   ├── World/            海面、平台、商人、浮木、宝箱和敌人外观
│   ├── Feedback/         确认事件消费、音效、飘字和条形图动画
│   └── UI/
│       ├── Hud/          血条、体力、连击、任务和获得物品显示
│       └── Inventory/    商店、背包、装备、角色面板与只读预览
└── Development/
    ├── Drivers/          显式启用的开发验证入口
    └── Pilots/           自动操作脚本，部分被命令行测试显式引用
```

入口示例：[主控制器](../samples/TacticalGame.Unity/Assets/HookIsland/Runtime/Composition/IslandFishingController.cs)、[房间管理](../samples/TacticalGame.Unity/Assets/HookIsland/Runtime/Application/Room/IslandRoomSession.cs)、[保存协调](../samples/TacticalGame.Unity/Assets/HookIsland/Runtime/Application/Saves/IslandSaveCoordinator.cs)、[反馈消费](../samples/TacticalGame.Unity/Assets/HookIsland/Runtime/Presentation/Feedback/FishingFeedback.cs)、[物品 HUD](../samples/TacticalGame.Unity/Assets/HookIsland/Runtime/Presentation/UI/Hud/AcquisitionHud.cs)。

`Editor` 分为 `Scenes`（构建场景）、`Art`（安装和生成美术）、`Previews`（预览窗口）。现有 `Tests/Editor` 仍保留独立的测试程序集。

配置实例仍在 `Assets/HookIsland/Data`，动态加载资源仍在 `Resources`，模型材质仍在 `Art`，场景仍在 `Scenes`。这与 `Runtime/Data` 中的 C# 配置类型不同。所有移动脚本保留原 `.meta` 和 GUID，场景、Prefab 和 ScriptableObject 无需重新绑定。

## 新文件放置规则

- 配置和状态类型放 `Data`；涉及扣款、奖励、伤害、命中和状态推进的规则放纯 C# `Logic`。
- 输入和 UI 操作只表达意图，命令投递放网络 `Commands`；确认事件通过 `Events` 交给表现。
- 图标、HUD、音效、动画、粒子和插值放 Unity `Presentation`；不能直接写权威状态。
- 场景服务装配放 `Composition`，房间和存档流程放 `Application`；不要把新功能重新堆进主控制器。
- 验证入口放 `Development`，继续遵守现有编译条件与显式开关；移动目录不会自动改变发布构建的包含规则。

## 整理与验证范围

来源为现有项目引用、程序集定义、文件内容和 `scan_structure.py tree` 的实际扫描。本次搬迁 240 个源码文件：模拟 96、网络 19、Unity 运行时 116、编辑器 9；125 个 Unity 脚本的 `.meta` 和源码与搬迁前逐字节一致。六个测试工程的显式源码引用、相关文档路径已同步更新。

搬迁与验证证据保存在本地 `artifacts/code-organization/`。9 个工程编译通过，既有模拟用例 360/360 通过。Unity 编译无错误，编辑器测试 156/157 通过；唯一失败仍是搬迁前已有的左手与躯干穿模检查（sample 24、vertex 2245）。125 个移动脚本的 GUID 均指向新路径，当前场景及 15 个预制体没有缺失组件，15 个配置对象及鱼、角色 Resources 正常加载。

本次没有改动规则、协议或事务实现，因此不重跑前一轮已经完成的长时间全量 UDP 流程。最终结果见该目录的 `verification-summary.json`，文件变更范围见 `change-summary.json`。

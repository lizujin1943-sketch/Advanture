# 平台地块实现

2026-10-21。对应《钩索小岛》0.10 的初始平台、木板／鱼骨地块、个人材料结算与相邻铺设规则。工程按正式游戏维护；本文说明本次地块模块的交付范围，不代表整份玩法设计已经实现。

## 入口与操作

Unity 工程：`samples/TacticalGame.Unity`。打开 `Assets/HookIsland/Scenes/HookPlatform.unity` 后 Play，选择单人或创建／加入 2～4 人房间。旧 `HookIsland.unity` 继续保留原岛屿和商店。

| 操作 | 按键 |
| --- | --- |
| 移动、瞄准、出钩 | WASD、鼠标、左键 |
| 打开／关闭铺地面板 | B |
| 选择木板块／鱼骨块 | 1／2，或面板按钮 |
| 确认铺设 | 鼠标指向目标格，左键 |
| 放弃尚未确认的预览 | 右键，或 Esc 关闭建造模式 |
| 缩放视角 | 滚轮 |

面板展示自己的木材、鱼骨和各自配方；绿色预览表示当前可建，红色显示无效位置。确认后显示等待回执与成功／失败原因。已确认请求不会因关闭面板而撤销；未确认的预览从不扣料。建造面板不暂停世界，不给予免拖拽保护，移动仍然可用。

初始平台为 9 块免费的 2×2 米木板，3×3 连续排列。四个出生点分散在平台上。角色约 1 米高，平台初始线长 4 米、移动速度 3 米／秒。平台顶面暂选 0.45 米、整体厚度约 0.30 米；设计未锁定这两个美术参数。

## 已实现规则

- 木板和鱼骨块共用 2 米整数网格；必须与已有平台有一条完整边相邻。对角连接、重复格、预留建筑占地和可见实体重叠均被拒绝。
- 默认每块木板消耗建造者 4 木材，每块鱼骨消耗建造者 4 鱼骨，不使用队友材料，也不消耗金币。金币按最新确认规则属于各玩家，钱包改造由装备／背包接入统一处理。
- 服务器与客户端使用同一份确定性规则，预览也是同一校验入口。四人同帧争抢一格按固定玩家槽位顺序处理，只生成一块，只扣成功者一次费用。
- 成功生成与扣料在同一模拟事务中完成。失败同样保存请求回执；重复网络输入不能再次扣料，也不能更改旧请求的目标格重新尝试。
- 新地块立即进入全体玩家的行走和队友拖拽边界判定。木板与鱼骨接缝可通行，鱼骨外观空隙不形成碰撞漏洞；每块预制体只有一个完整 2×2 米行走碰撞盒。
- 快照保存地块种类、坐标、建造者、历史材料支出、个人余额与请求回执。恢复时校验连通性、唯一占格、初始赠送地块及个人收支守恒。
- 同一平面最多 512 块，格坐标范围为 −24～24；这是当前同步状态与地图容量边界，不是承重或耐久机制。没有拆除、退款、多层、旋转及结构损坏。

## 配置与材料来源

`Assets/HookIsland/Data/PlatformSettings.asset` 提供 `wood_tile_cost`、`fishbone_tile_cost` 及预留建筑格。两种材料价格独立配置，范围 1～1000。场景初始化时复制成不可变规则；修改后重新进入 Play／新建房间。房间校验地图与规则散列，配置不同的客户端不能加入同一局。

已铺地块记录当时支付的价格。调整价格再恢复历史状态不会追扣或返还，下一次铺设才使用新价格。`PlatformMap.bytes` 是场景创建时导出的地图参考；Unity 的配置来源是上述资产。

海面浮木持续移动，小／中／大规格完整回收后分别给实际收获者 4／6／10 木材。只有到达收获位置才入账，取消、尚在回收和多人争抢不会重复给材料。初始 4 米线长能取得第一批浮木。

鱼骨也在鱼完成回收时进入实际收获者库存。地块模块接收个人材料入账，不决定鱼种、重量或等级配置；当前平台已接入钓竿模块的 `FishCatalog.Bones`，按实际鱼重与等级计算。旧规则的 2／4／6 鱼骨仅保留在兼容场景及隔离测试中，不作为平台的最终鱼种配置。

本次材料容器是个人数量库存。设计中的有限格子背包、20 个材料一堆、溢出材料包由独立背包模块继续接入；没有把无限容量当成最终背包规则。商人与钓竿耐力由独立装备模块负责，不在地块中复制规则。本次场景保留平台边缘行走约束，暂不能落水。

## 职责与扩展边界

| 模块 | 职责 |
| --- | --- |
| `HookIsland.Simulation/PlatformRules.cs` | 网格、相邻连接、占用与通行校验，无 Unity 依赖 |
| `HookIsland.Simulation/PlatformBuildSystem.cs` | 按同步顺序执行建造事务，保存成功和失败回执 |
| `HookIsland.Simulation/PersonalMaterialInventory.cs` | 个人材料的读取、完整入账与扣除；后续背包接入的集中边界 |
| `HookIsland.Simulation/PlatformStateCodec.cs` | 地块、浮木的状态读写与不变量验证 |
| `HookIsland.Simulation/DriftwoodMotion.cs` | 浮木的确定性漂流、归属与刷新状态 |
| `HookIsland.Network/PlatformBuildRequestBuffer.cs` | 等待权威回执前重发同一请求，保留移动等其他输入 |
| `Runtime/PlatformConstruction.cs` | 本地输入与建造交互，协调请求和表现，不直接扣材料 |
| `Runtime/PlatformPresentation.cs` | 已确认地块、候选格和预览显示；通过预制体映射获取外观 |
| `Runtime/PlatformShoreline.cs` | 根据已确认地块生成水边距离纹理，为海水提供泡沫和向外扩散的波纹 |
| `Runtime/DriftwoodPresentation.cs` | 浮木显示与纯视觉起伏 |
| `Runtime/PlatformSettings.cs` | Unity 配置资产到不可变模拟配置的转换 |

前六项路径位于 `samples/`；Unity 的 `Runtime/` 位于 `samples/TacticalGame.Unity/Assets/HookIsland/`。`HookGame` 只在移动后调用建造事务，不再兼管其扣料与回执细节。新价格与预制体配置不应改动网络运输或帧同步核心。需要新增材料种类时，扩展材料／配方定义及外观注册；相邻连接、命令去重及移动规则保持共用。没有为两种地块建立不同的移动子类，以免破坏相同通行行为的契约。

自动游玩驱动只在 `UNITY_EDITOR` 或 `DEVELOPMENT_BUILD` 下编译，且只有显式 `-island-report` 参数才自动挂载。正式场景不序列化自动玩家，发布构建不包含平台自动游玩类。验证驱动通过真实输入收集材料、竞争格子并走上新地块，不修改模拟状态或发放免费资源。

## 平台边缘波纹与浪花（2026-10-21）

`Platform Ocean` 上的 `PlatformShoreline` 使用已确认地块的并集生成距离纹理。`PlatformPresentation` 在地块改变时通知重建，海水着色器按本地时间播放往外传播的断续水纹和淡出。按用户最新反馈，贴边泡沫浪花暂时关闭（`foamStrength=0`），外围波纹保留。扩建、还原状态或重新开局后轮廓随地块更新；没有新增网络命令或权威状态。

| Inspector 配置 | 初值 | 含义 |
| --- | --- | --- |
| Foam Width | 0.5 m | 预留泡沫带宽度，当前泡沫已关闭 |
| Ripple Reach | 1.6 m | 水纹向外扩散的可见范围 |
| Ripple Spacing | 0.65 m | 相邻水纹间距 |
| Ripple Speed | 0.28 m/s | 水纹向外传播速度 |
| Foam Strength | 0 | 接触泡沫强度；0 关闭浪花，不影响外扩波纹 |

颜色沿用 `IslandOcean.foamColor`，不新增独立材质实例。纹理在地块或外观配置变化时重建，每米 8 个像素；显示动画没有逐帧距离扫描。木板/鱼骨的内部接缝不发泡沫，外凸拐角呈圆弧扩散，凹角和水洞按真实水边覆盖。平台下的海水继续渲染，旧小岛场景仍使用原来的陆地遮罩和岸边浅水规则。生成纹理不存入场景，并在禁用组件或离开场景时释放。

泡沫浪花暂时关闭后，以下验证和早期截图保留为首次实现记录。各客户端读取同样的已确认平台轮廓，装饰性动画相位使用本地时间，不要求逐帧一致。

验证：新增 3 项边界/生命周期检查，覆盖内缝、凹角、外凸拐角、水洞、扩建、恢复初始边界、远端地块、空平台、海水动画属性共存及纹理释放；与现有 Unity 测试一起 41/41 通过。单人 Play 检查确认 9 块权威地块与 9 块显示地块一致，水纹距离纹理正常绑定，无玩法错误。另以只改显示对象的临时夹具检查混合材料扩建和水洞，已清理；512 块跨越网格两端的显示夹具生成 818×818 纹理，本机一次重建约 9.5 ms（单次采样，不作为设备性能保证）。着色器编译无错误或警告。

截图与记录保存在 `artifacts/platform-shoreline/`。本轮为水面表现修改，未重新运行纯模拟完整验收，不将画面检查等同于完整游戏验收。

## 验证与复现

```sh
./scripts/verify.sh
./scripts/sync-hook-island.sh
dotnet run --project tools/HookIsland.Tests -- --filter=platform_
python3 scripts/run-island-party.py --platform --players 4
```

最后一项需要先构建 `HookPlatform.unity` 为 macOS Development Player，输出至 `artifacts/hook-island-playable/HookPlatform.app`。发布构建输出另放 `HookPlatform-Release.app`，不支持自动游玩参数。

规则覆盖相邻性、占用、不同材料配方、同格竞争、缺料、去重、失败回执、个人归属、价格修改、快照恢复、浮木回收和混合地块通行。网络验证让四名玩家实际收集材料、铺出各自的木板和鱼骨块并走到扩建区域；最后比较服务器与全部客户端状态散列，并检查 Unity 显示地块数。

本次验证记录位于 `artifacts/hook-island-playable/`：`platform-final-rules-network.log`、`platform-unity-tests.json`、`platform-full-verification.log`，以及 `platform-smoke-*` 中的报告、截图和客户端日志。生成内容不提交 Git。

地块验收结果：15 项模拟／交互规则用例通过，2 项真实 UDP 平台用例通过，Unity 场景检查 9/9 通过。最终四独立客户端记录为 `platform-smoke-20261021-143552-d7ddec/result.json`：到第 617 帧，服务器与四客户端散列均为 `68e49834c87629c545056dfb5cf7083c7f9139d0c503d0422e28a686f7d6cf2b`，生成及显示均为 17 块，四人均走到自己建造的鱼骨块，无玩法异常日志。

`platform-release-isolation.json` 记录发布程序集仍保留地块运行类，但不包含 `PlatformSmokeDriver`／`PlatformSmokePilot`。并行装备集成期间，全量日志曾捕获个人钱包和装备格坐标的新增失败用例；这些用例在最新代码的纯模拟回归中已通过，结果为 68/68，见 `platform-final-simulation-regression.log`。坐标边界定向记录另见 `rod-grid-handoff-check.log`。背包最终接入后仍需由集成任务再跑完整入口，不将地块验收等同于完整游戏验收。

## 并行接入交接

地块规则、`PlatformBuildSystem`、`PersonalMaterialInventory` 和 `PlatformBuildRequestBuffer` 的职责拆分已完成。背包可从个人材料读写边界接入，同时更新快照的收支守恒校验；不要在 Unity 面板里另扣一次材料。钓竿命令与建造命令互斥，待确认的建造优先，其余系统保留自己的回执并在后续帧重试。

地块收尾现已完成，可交接上述代码、`Runtime/PlatformSmokePilot.cs`、`Runtime/PlatformSmokeDriver.cs`、`tools/HookIsland.Tests/PlatformTests.cs` 和 `scripts/run-island-party.py`；地块任务不再修改这些文件或占用 Unity 验收。共享 `HookGame`／`HookState`／`HookInput` 的地块接入已完成，`Inventory*` 和背包计划由背包模块维护。

后续将数量库存替换为格子堆叠／溢出包时，需同步调整 `PlatformStateCodec` 的守恒公式与材料显示；当前公式是“累计获得 − 当前余额 = 历史建造支出”，未计入地上包与主动丢弃。保留建造事务的唯一扣料入口，以及成功／失败请求的持久回执。验收驾驶员按 `FishingRules.Range/Speed` 读取实际钓竿参数，支持输入延迟；材料获取由真实捕获完成，接入背包后应继续沿这条流程验收。

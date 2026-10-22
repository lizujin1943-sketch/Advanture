# 钩索小岛角色、装备与属性实现

2026-10-21，当前为 v10。接续游泳 v9，并按用户纠正取消护盾资源，防御（护甲）是持续生效的属性。运行入口为 `samples/TacticalGame.Unity/Assets/HookIsland/Scenes/HookPlatform.unity`。

## 玩家操作与属性

- `Tab` 或 `I` 打开角色／背包，首次进入角色页。八个槽位分别为帽子、左手、右手、衣服、鞋子、背包、饰品、游泳圈。
- 商店出售轻便鞋（岸上移速 +0.3 米/秒）、加固外套（生命上限 +25）、护额帽（防御 +20），当前均为 150 金币。首批优先鞋子，之后衣装随整批货轮换；保留初期四支 T1 竿、Lv2 背包和泳圈，再放入衣装及其他混排商品。
- 衣装定义在 `samples/HookIsland.Simulation/Data/apparel.csv`；头部／鞋子占 2×2，衣服占 2×3。配置进入房间内容校验。新增同类商品通过配置扩展，不增加按具体物品结算的分支。
- 商店可购买入包或直接装备；角色页可点击查看加成与前后属性，点击装卸或在对应槽位与备用区间拖拽。只有本人已穿戴的装备参与计算；放入背包、落地和别人的装备不生效。
- 头部装备增加防御，衣服增加生命上限，鞋子增加岸上移速。鞋不改变游速、出钩／回收速度、队友拖拽速度。头像周围槽位、角色详情、商店、HUD 与场景衣装标记读取确认状态。
- 换装增加上限时不回血、不补耐力；降低上限时裁切超出部分，保留耐力恢复等待。满包换装失败时库存、属性、金币均保留。卸背包必须先清空，并明确放到平台。
- 查看角色不会暂停世界或获得购物保护。出钩后至完全收回之前无法主动移动；被拖拽、倒地、落水时沿用现有装卸限制。

出生值为生命 100、岸上移速 3 米/秒、防御 0、饥饿 100、基础耐力 100；出生旧木竿提供 20 耐力上限，出生 120/120。默认服饰只是外观，空槽不提供隐含加成。

## 防御与职责边界

攻击的整数伤害为 `ceil(原伤害 × 100 / (100 + 防御))`：零伤害保持零、正伤害至少 1 点。100 防御使 20 点攻击伤害变为 10；20 防御使 60 点变为 50。连续受击不会消耗防御；溺水等直接伤害绕过防御。没有“当前防御”资源，不需要充能或恢复。

| 文件／组件 | 职责 |
| --- | --- |
| `CharacterConfig` | 房间基础生命、岸上移速、防御、饥饿配置 |
| `CharacterStats`、`CharacterModifier` | 组合基础与穿戴加成；计算并限制上限／最终属性 |
| `CharacterDamage` | 独立、纯整数的防御减伤规则 |
| `CharacterVitals`、`CharacterSystem` | 当前生命／饥饿、出生、裁切、扣血与显式恢复；耐力沿用 `Fisher.Stamina` |
| `CharacterMovement` | 30 Hz 整数位移与有符号余数 |
| `ApparelCatalog` | 衣装定义与字段校验，输出通用 `InventoryDefinition.Modifiers` |
| `CharacterEquipment` | 从唯一库存计算属性，候选库存中的正式装卸预览 |
| `EquipmentSystem`、`InventorySystem` | 既有的事务、回执、钱包、去重与装卸后刷新 |
| `CharacterStateCodec` | 当前资源、位移余数的快照与校验；防御由配置与库存重算 |
| `CharacterPanel`、`ApparelDisplay`、`CharacterEquipmentAppearance` | 只读界面与确认后的穿戴表现；不改权威状态 |

通用帧同步核心未添加玩法／Unity 依赖。既有 `InventoryCommand` 交付衣装意图，购买失败不扣款，重复请求不重复生成。防御伤害接口已就绪，当前尚未加入敌人战斗、食物、饥饿下降或完整倒地复苏流程；不以空槽物品替代未定义的左手和饰品玩法。

## 配置与兼容性

`CharacterSettings.asset` 配置生命、岸上速度、防御、饥饿，由 `FishingSettings.asset` 引用。基础耐力、恢复等待和恢复速度仍以 `FishingSettings` 为唯一配置来源。

房间／规则／状态版本为 **10**，输入格式仍为 6（60 字节），带钓具地图为 6。因为移除了当前护盾字段，并改变防御含义及商品内容，旧房间和旧快照明确拒绝，不能混用或静默把旧护盾解释为防御。存读档保留生命、饥饿、耐力、移动余数和库存请求结果，重算防御但不会重新出生或治疗。

## 当前验证入口

```sh
./scripts/verify-hook-character.sh
./scripts/verify-hook-character.sh --simulation-only
./scripts/verify.sh
```

v10 的验证记录集中在 `artifacts/character-apparel/`：

- 角色 **28/28** 通过，包含 25 项领域／集成检查及 3 项本机真实 UDP 联机检查。新增四端用正式捕鱼收入购买鞋子、卸下和重装备，第 473 帧哈希一致，购价只扣除一次；记录为 `character-network.log`。四端哈希为 `8ccaf2864b21a5265e4c2a3fc35898396cdf0edb456029f323354cd83903cef3`。
- 完整 `./scripts/verify.sh` 退出码 **0**：框架 131、持久化 23、CLI 24、Sync 6、UDP 30+16、Tactical 32、Hook Island 78、背包 26、角色 28、游泳 27 项通过，并完成录制重放及预测一致性检查；记录为 `full-verify.log`。
- Unity 编辑器测试 **42/42**，包括三类衣装真实 IMGUI 双向拖拽及确认后的外观归属验证，结果为 `unity-editmode.json`。最后清理发布包的旧验收组件后，本玩法测试程序集再次 **20/20** 通过，结果为 `unity-package-regression.json`。
- 编辑器实际单人房间通过正常出钩输入捕获 4 条鱼，收入 171 金币，经正式购买命令花 150 金币装备鞋子，余额 21，角色页显示 3.3 米/秒；截图为 `live-earned-shoes.png`、`live-shop.png`。临时验收输入仅在编辑器内存中执行，检查后已移除。`character-panel.png` 是独立编辑器界面夹具，用于查看三类衣装及属性排版，不作为实际挣钱购买的证据。
- macOS **非开发构建** `HookPlatform.app` 成功，0 错误、0 警告，任务 `build-619ecc343b`，记录为 `build.json`。包内模拟 DLL 与已验证 Unity 插件一致，SHA-256 为 `81ff1249ef2b848b301a7f7b6725130a7ef8e87dc00c3bbf84fecc10ef7b572b`。
- 发布程序集检查 `release-audit.json` 通过：保留正式角色、衣装及游泳类型，不含任何 Smoke 或编辑器测试类型；当前资源仅生命、饥饿，防御是派生属性。首次检查发现旧 `FishingSmokeDriver` 的空类型残留，已将整个类型移出发布编译，并移除旧岛场景及构建器的常驻引用。两个正式场景均无自动试玩组件，记录为 `scene-audit.txt`。旧岛的开发验收只在显式提供报告参数时临时挂载。

验收后已退出 Play，恢复编辑器窗口布局，保留 `HookPlatform.unity` 为当前场景且无未保存改动。本轮联机测试使用同机四客户端真实 UDP；未将跨电脑、公网或 Windows 构建列为已验证。

## 历史验收（v8，以下构建及哈希不代表当前版本）


```sh
# 含真实四客户端 UDP，需要本地端口权限
./scripts/verify-hook-character.sh

# 仅确定性领域和集成逻辑
./scripts/verify-hook-character.sh --simulation-only

# 仓库回归，已纳入角色检查
./scripts/verify.sh
```

角色测试项目 `tools/HookIsland.Character.Tests` 的 19 项检查已通过，记录为 `artifacts/character/logic-network-debug.log`：

- 默认／自定义基础属性、固定加成、仅本人穿戴生效、极端加成边界。
- 装卸不治疗、不补耐力、不重置等待；满包和使用中失败不改库存，预览不写权威状态。
- 普通／直接伤害、恢复边界、倒地主动行动限制；倒地施钩者释放队友，倒地目标仍可被拖动且不回血、耐力或金币。
- 精确移速、负向／小数余数、受损属性与回执保存恢复、非法快照和不同房间规则拒绝。
- 四个真实 UDP 客户端装卸初始钓竿，确认八件出生物品没有重复或丢失，四端与房主最终完整快照哈希一致。

Unity 侧新增 `CharacterSceneTests` 三项检查：资源引用与打包地图、配置参与兼容校验、确认后的钓竿／背包显隐及材质。三项均通过，所在编辑器整轮测试为 **36/36**（job `3440574fa2ca4720a62ffec9d50fe2e4`），控制台没有编译错误，配置资源及平台地图已保存。

`./scripts/verify.sh` 本轮退出码 0，框架 131、持久化 23、CLI 24、Sync 6、UDP 30+16、Tactical 32、Hook Island 78、背包 26、角色 19 项全部通过；完整记录为 `artifacts/backpack/full-verify.log`，编辑器测试摘要为 `artifacts/backpack/editor-tests.json`。

macOS 开发构建成功，0 错误、0 警告。四个独立 Unity 客户端经真实捕鱼、购买／整理背包与建造后，分别通过正式请求接口卸下并重新装备钓竿：卸下后耐力上限回到基础 100，再装备不补满当前耐力。四端 `characterChecked`、面板互斥检查均通过。第 **785** 帧房主及四端完整快照哈希均为 `5effd2e0ea94a1071075123caee04e172f8cf88ada8e86f4a093495fdde5411a`，客户端日志无运行异常。

已人工检查角色页截图，八槽、五属性、穿戴的 Lv2 背包与备用旧背包布局正常；当前耐力 **68/120** 及恢复提示符合实际状态。记录目录为 `artifacts/hook-island-playable/inventory-smoke-20261021-152900-e9314b/`，角色截图为各端的 `client*.json.character.png`。此验收是同机 macOS 四个独立进程，经真实 UDP 与正式命令路径运行；不将跨电脑、公网或 Windows 列为已验证。

### 最后界面复验与固定构建

实际点击通过了选择钓竿、卸下、重新装备。原生鼠标拖拽复验发现 `GUI.Button` 消费输入后，`Event.rawType` 也变成 `Used`，面板尾部漏掉拖动与松开。现在在绘制控件前保留输入类型，装卸仍只提交原有库存意图。新增仅编辑器测试 `CharacterPanelTests`，用真实 IMGUI 控件测试“装备槽 → 备用区 → 装备槽”，每个方向只提交一次请求；修复前失败、修复后通过。测试窗口不会进入场景或发布构建。

修复后通过原生鼠标再次完成双向拖拽：卸下后 `EquippedRod=0`、请求号 1、耐力上限 100；重新装备后 `EquippedRod=1`、请求号 2、上限 120。截图为 `artifacts/character/drag-unequip-verified.png` 和 `drag-equip-verified.png`。最后整轮 Unity 编辑器测试 **37/37**（job `562b9865cf4845458ce3cec3a53a4a63`），记录为 `artifacts/character/editor-tests-final.json`；失败复现及修复结果分别为 `character-drag-red.json`、`character-drag-green.json`。

包含拖拽修复的 macOS 正式构建为 `artifacts/character/HookPlatform-Character.app`，构建任务 `build-0fe29374f8` 成功，0 错误、0 警告。此产物继续使用已通过上述四端验收的 v8 模拟 DLL（SHA-256 `c16bc07c60471415cee4731403903fcc24c79f5e38a5d0ff889048489b2cd41a`），没有同步并行开发中的下一版游泳逻辑。原背包四端验收产物保持独立。验收结束已退出 Play 并恢复编辑器窗口布局。

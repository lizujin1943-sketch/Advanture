# 玩法需求实施清单

目标：实现需求文档中已经描述、但尚未实现的玩法相关功能。依据为 `hook-island-game-design.md`，并保留用户明确要求“上岸逻辑后续设计”的边界。此清单不是缩小后的完成定义；只有代码、可操作入口和对应验证共同成立，才将一项标为完成。

2026-10-21 首轮核对：完整阅读设计文档，检查当前模拟、网络与 Unity 入口。上一轮完成合钓并复验，属于进展。下面的“已有”表示找到了实现及既有验证；最终目标完成前仍需逐项复核当前集成版本。

## 已有实现及核对入口

| 需求族 | 当前证据 | 状态 |
| --- | --- | --- |
| 初始 3×3 平台、2 m 地块、4 m 钓竿、分散出生 | `IslandMap`、`PlatformRules`、`PlatformSceneTests` | 已有 |
| 两种材料相邻铺地、竞争只扣一次、有效建造预览 | `PlatformBuildSystem`、`PlatformConstruction`、`PlatformNetworkTests` | 已有 |
| 浮木漂流、4/6/10 木材、捕获一次、满包溢出 | `DriftwoodMotion`、`HookGame`、库存及平台测试 | 已有；水中收获和抵岸边界继续核对 |
| 八槽装备、五项角色属性、持续防御减伤 | `CharacterSystem`、`CharacterStats`、`CharacterPanel`、角色测试 | 已有；饥饿当前仅状态和面板，见待设计项 |
| 统一耐力、使用钩索锁定自主移动、等待恢复与换装不补满 | `StaminaSystem`、`HookGame`、角色/游泳/合钓回归 | 已有 |
| 无圈落水掉血、有圈水中出竿、10 级泳圈、游回与钩救 | `SwimmingSystem`、`SwimmingTerrain`、`SwimRingCatalog`、游泳测试 | 已有 |
| 倒地 60 秒、免费 R 救援、个人付费复活、全员无力复活才失败 | `RevivalSystem`、`RevivalController`、复活测试 | 已有 |
| 房主原房间重开、旧局输入隔离、所有客户端确认初态 | `RoomRestartBarrier`、`IslandHost`、重开局测试 | 已有 |
| 背包 10 级、占格旋转、拆分/合并、掉落/拾取、换包原子转移 | `GridInventory`、`InventorySystem`、`BackpackOverlay`、库存测试 | 已有 |
| 30 款钓竿、衣装、共享有限货架、个人钱包、600 秒整批换货 | `FishingCatalog`、`RodShop`、`EquipmentSystem`、装备界面及网络测试 | 已有 |
| 商人 M1～M5 商品池与隐藏概率 | `RodShop`、装备配置与抽取测试、四艘外围补给船与独立货架 | 已完成可达路径；实际铺路、跨档购买、1／2／4 人原生通过 |
| 10 鱼种、重量/等级/收益/挣脱力、潜游前跃、远近分布 | `FishMotion`、`FishingCatalog`、`FishAppearance`、鱼难度/挣脱测试 | 已有 |
| 食人鱼巡游、攻击、捕获、刷新；水怪随机出水、前摇、打断、水球击退 | `EnemySystem`、`EnemyPlacement`、`EnemyPresentation`、敌人测试 | 已有 |
| 普通鱼及食人鱼合钓、抢钓、换主、耐力均摊、最终挂线者分奖 | `HookGame.Cooperative`、合钓模拟/UDP/原生客户端验收 | 已有；39 项专项复验通过 |
| 友方拖拽保护、购物保护、救援例外与房主开关 | `RoomRulesState`、`CanHookPlayer`、`PullAcrossWater`、菜单及规则测试 | 房主开关已完成；规则 24 项与原生 1／2／4 人通过，见 `room-rules-implementation.md` |
| 小箱／稀有箱回收、大箱合钩抵岸开启、冻结名单与原子分奖 | `TreasureMotion`、`HookGame.Treasure`、`TreasureController`、宝箱规则及真实客户端验收 | 已完成；宝箱模拟及 UDP 36 项、Unity 57 项集成检查、原生 1／2／4 人通过，见 `treasure-chests-implementation.md` |
| 共享顺序委托、普通／稀有替代条件、独立团队诱饵 | `CommissionSystem`、捕获成功事务、`CommissionHud`、委托规则及真实客户端验收 | 已完成；28 项委托检查、63 项编辑器检查、原生 1／2／4 人及真实 UDP 重开通过，见 `commissions-implementation.md` |
| 委托进度开放鱼群、长期无鱼获／连续危险后的恢复窗口与真实近岸鱼 | `OceanSystem`、`OceanPlacement`、成功捕获通知、确认态 HUD | 已完成；25 项规则、10 项普通输入、3 项真实 UDP、2 项重开、Unity 68 项及原生 1／2／4 人通过，见 `ocean-progression-implementation.md` |
| 平台边缘波纹、无贴边浪花、钓线颜色、命中/捕获反馈 | `PlatformShoreline`、`IslandOcean`、`FishingPresentation` 及编辑器测试 | 已有；整体可读性须在后续集成试玩复核 |

## 仍需实现或补齐的玩法

| 顺序 | 需求 | 当前缺口与接入依赖 | 完成证据要求 |
| --- | --- | --- | --- |
| 1（完成） | 房主可关闭岸上友方整蛊，落水救援始终允许 | 已接入确认态命令、菜单、快照和重开第零帧保留；水中施救路径首次登岸即释放 | 规则专项 24 项、重开 12 项、Unity 54 项及原生 1／2／4 人通过；`artifacts/room-rules/verification-summary.json` |
| 2（完成） | 小宝箱回收、稀有箱漂走、大宝箱两人合钩并开启 | 已接入独立配置、状态、钩索、抵岸、开启与原子奖励；正式平台可操作，宝箱不套用鱼的抢钓规则 | 单人小箱、多人双钩门槛、漂流、旧请求去重、保存恢复与原生 1／2／4 人通过；`artifacts/treasure-chests/verification-summary.json` |
| 3（完成） | 共享委托及替代目标、诱饵进度 | 三份顺序目标按成功捕获一次计数；合钓不重复贡献、失败事务不推进，HUD 读取确认态 | 28 项专项、Unity 63 项、原生 1／2／4 人和全量回归通过；默认普通输入 30 条鱼完成三份，真实 UDP 重开清零；`artifacts/commissions/verification-summary.json` |
| 4（完成） | 按委托进度推进鱼群/事件，给落后队伍普通鱼机会 | 阶段开放、两次轻鱼机会、无鱼获／压力恢复窗口均接入；不直接加资源，已挂临时鱼不被到期吞掉 | 普通输入与原生 1／2／4 人、完整状态保存／重开、Unity 68 项、全仓库回归通过；`artifacts/ocean-progression/verification-summary.json` |
| 5（完成） | 终局灯笼巨鱼、三岸合钩、单人适配、胜利与下一局 | 共享诱饵一次消费、三轮换岸、胜利冻结与原房间重开已接入；巨鱼不涉及上岸逻辑 | 模拟 276/276、Unity 79/79、原生 1／2／4 人和重开 16/16；全量初次测试错误及修复复验如实记录于 `artifacts/finale/verification-summary.json` |
| 6（完成） | 后期商人提供更高档装备的实际可达路径 | 四条可建造路线、独立激活/库存/计时、指定店购买与导引已接入；旧小岛商店回归修复 | 19 项规则、普通输入 9/9、原生 1／2／4 人、Unity 94/94、全仓库回归和真实 UDP 重开通过；`artifacts/merchant-routes/verification-summary.json` |
| 7 | 晚加入/断线恢复继承既有玩法状态 | 当前 `IslandHost` 开局后拒绝加入且掉线结束房间；快照恢复不等于可操作重连流程 | 真实断线重连保留钱包/物品/生命/耐力/商店/攻击进度，无重发奖励或免费回岸 |
| 8 | 整体体验验收与遗漏复核 | 逐条核对设计末尾试玩表及所有建议细则；已观察玩家紧贴时头顶耐力／主辅提示相互覆盖；受控默认流程关闭敌人，尚未验收正式战斗和经济平衡 | 单人与四人全流程实测，记录可读性/成长节奏/机会密度及缺口 |

这些内容保留在总目标中。每个子系统单独记录实施计划和可验证结果；房主开关、宝箱、共享委托、海域进度、终局巨鱼及后期商人已完成，下一项为晚加入／断线恢复，总目标仍未完成。

## 明确待后续设计的边界

- 可上岸鱼/Boss 的上岸移动、战斗和奖励时机：用户明确暂不做。螃蟹上岸提案依赖此边界，不能擅自用普通抵岸结算替代。
- 饥饿下降、食物/烹饪、冲刺、左手辅助物、饰品效果：文档明确待定；现有字段或空槽不代表完整玩法。
- 水怪牵引、击败与收益，后续海域数量/刷新：未定义，不把打断当捕获。
- 原岛与平台关系、开放海域航行、多层建筑、拆除、永久数值成长、装备词条：明确后续内容，不凭占位字段宣称完成。

宝箱奖励、委托条目与海域事件已给出可配置初值；终局与后期商人已实现。恢复设计和计划见 `superpowers/specs/2026-10-22-live-room-recovery-design.md` 与相应实施计划。记录由实现者作出的初值，并对影响玩法方向的选择向用户澄清。未落地部分持续保留为未完成。

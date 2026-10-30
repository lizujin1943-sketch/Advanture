# 房主友方整蛊开关实施说明

2026-10-21：房主可在游戏内 Esc 菜单中开关友方整蛊。默认开启；关闭后不能钩走岸上队友，落水救援始终可用。队友菜单显示确认后的开关状态，只有房主可以修改。

## 规则与边界

- 槽位 0 拥有修改权。房主倒地、死亡或正在被拖拽时仍可管理规则；已离开房间的槽位无权修改。
- 关闭当帧先释放正在被拖拽的岸上玩家，再处理动作、移动和新命中。在途钩子同样不能挂住岸上队友。
- 关闭后仍能钩起落水者，包括无圈、零耐力、倒地或刚刚被拖拽过的玩家。被救者登岸后立即释放，不转成岸上拖拽。
- 普通鱼、食人鱼的合钓与反向抢钓不受影响；敌方伤害与击退沿用原规则。
- 同房间重开保留上局最后确认的开关，新局从第零帧即采用该值；请求序号和本地待发请求归零。退出后创建新房间默认开启。

## 同步、重试和保存

`RoomRulesCommand` 使用明确的目标值和单调递增的请求序号。重复或旧请求不再次生效，不使用会因重发反复翻转的 toggle。非房主的请求不会改变规则，也不会占用房主序号。

`RoomRulesRequestBuffer` 保留待发请求，直到权威状态确认。它在背包、购买、建造与复活输入组合之后加入，界面等待期间显示“同步中…”并防止重复点击；收到确认以前不乐观修改显示状态。

规则与最新序号存入快照。此功能引入时规则、状态、房间协议版本为 **16**，输入版本为 **8**，地图 **HIMA**，快照增加 **RUL1**；后续[宝箱功能](treasure-chests-implementation.md)将当前版本推进到 **17／9／HIMB**。旧客户端及旧版本存档明确拒绝，没有迁移器。重开使用确认的布尔值创建新模拟，再通过既有初态快照屏障发送给全队。

## 代码入口

- `samples/HookIsland.Simulation/Logic/Simulation/HookGame.RoomRules.cs`：权限、请求去重、关闭时释放岸上拖拽。
- `RoomRulesCommand.cs`、`RoomRulesState.cs`、`RoomRulesStateCodec.cs`：命令、确认状态和规范编码。
- `samples/HookIsland.Network/Commands/RoomRulesRequestBuffer.cs`：重发与确认；`IslandHost.CreateSession` 保留重开规则。
- `IslandFishingController`、`FishingOverlay`：请求排队、瞄准辅助过滤、房主按钮及队友只读状态。
- `RoomRulesSmokePilot`、`RoomRulesSmokeDriver`：显式开发验收，只提交普通输入与菜单请求，不直接修改模拟状态，发布版本不编译。

## 验证

```sh
dotnet run --project tools/HookIsland.Tests -c Release -- --filter=room_rules_
dotnet run --project tools/HookIsland.Restart.Tests -c Release -- room_rules
./scripts/verify.sh
python3 scripts/run-island-party.py --app artifacts/room-rules/HookPlatform.app --players 2 --room-rules
```

专项包含权限、重发乱序、同帧关闭、在途钩子、拖拽中关闭、重新开启、零耐力和倒地救援、快照拒绝非法状态、重开第零帧保持规则，以及 1／2／4 人、0／3／6 帧输入延迟的普通操作。原生验收通过房主菜单使用的请求队列操作开关，抓取房主与队友菜单截图，并比较各端完整 checkpoint。

验收记录保存在 `artifacts/room-rules/`，汇总为 `verification-summary.json`：

| 验证 | 结果与记录 |
| --- | --- |
| 确定性模拟 | 132/132 通过，含输入延迟和双方在水中跨平台角救援；`review-green.log` |
| 最终钓鱼模拟／UDP | 148/148 通过，其中房间规则 24 项、合钓 39 项；`game-final.log` |
| 仓库完整回归 | `./scripts/verify.sh` 退出码 0；背包 26/26、角色 28/28、游泳 27/27、敌人 45/45、复活 26/26、重开 12/12；框架、回放和底层 UDP 同样通过；`verify.log` |
| Unity 编辑器 | 最终 54/54 通过，控制台无错误／警告；`unity-editor-tests-final.json` |
| 原生房间规则 | 最终 1／2／4 人均在第 600 帧完成，所有端完整 checkpoint 逐字节一致；`native-final-results.json` |
| 合钓集成回归 | 最终原生 1／2／4 人房间分别于第 600／719／721 帧完成；多人观察到普通鱼合钓、抢钓和食人鱼合钓，所有端完整 checkpoint 一致 |
| 构建及发布隔离 | 开发／发布均成功，零构建错误和警告；验收类型不在发布程序集；`final-builds.json`、`release-isolation.json` |
| 独立代码复核 | 平台转角救援问题先失败再修复，未延期 minor；`final-review.md`、`review-red.log` |

完整仓库回归开始于转角修复之前；该修复后重新执行完整钓鱼 148 项、Unity 测试、开发／发布构建及上述六组原生房间。最终钓鱼验证与 Unity 客户端使用相同模拟／网络 DLL（`library-consistency.json`）。房主按钮、队友只读状态和普通鱼／食人鱼的双线表现均已目视检查。

联机验证为本机真实 UDP，不代表跨机器、互联网或丢包压力验收。四人合钓房间由前两名操作、另外两名观察同步；三人／四人同时挂线的耐力、速度和分配由模拟测试覆盖。

该功能是 `docs/gameplay-gap-audit.md` 总目标中的一项；后续宝箱实现见对应文档，共享委托、终局玩法等仍未完成。可上岸鱼／Boss 的上岸流程按用户要求继续留待后续设计。

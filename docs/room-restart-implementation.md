# 失败后全队重新开始

游戏失败页由房主点击“全队重新开始”，队友自动进入同一新局。多人只有全部玩家最终死亡且无人能支付个人复活费用时开放重开；倒地等待救援、仍能付费复活或正常游玩不会出现可用重开入口。2026-10-23 起，单人血量归零即自动尝试付费复活，余额不足当帧失败，按钮显示“重新开始”；原房重置规则保持一致，见[即时复活记录](immediate-revival-implementation.md)。

## 本局重置

保留 UDP 连接、房间端口、房间身份、每位成员的身份和位置编号。使用同一地图与配置重新初始化平台、角色生命／耐力、装备和背包、金币与材料、商人库存、鱼群、浮木、敌人、请求回执与本局计时。已有初始钓竿、Lv.1 背包及 3×3 平台仍由普通开局规则生成。

## 同步与权限

- 房间协议版本 14 新增 RestartRound 和 RestartAck。房主本地 IslandHost.TryRestart 检查权威 GameOver，客户端没有发起权限；伪造通知会被房主拒绝。
- 同一房间递增 SessionDescriptor.Epoch。确认的旧局模拟停止推进，控制通道继续工作；旧局消息队列持续排空，避免堵塞确认通知。
- 所有已入房成员都确认切局后，房主建立新的 ServerSession；核心原有的初态快照、Ready、Start 屏障保证所有端准备好后开局。
- 相同通知只重发确认，不清空已开始的新局；过期确认不产生新局。旧输入、快照、确认帧与关闭消息由 epoch 隔离。
- 客户端的 RoundChanged 在第一帧采样前清理本地冻结输入、输入时钟、交易／铺地／装备／复活请求、窗口和旧表现。钓鱼耐力条提示沿用 FishingEquipmentOverlay.ResetSession。
- 房间控制确认等待上限 30 秒；底层 UDP 断线检测和初态同步超时继续生效。断线或超时会明确结束连接，不让部分队伍自行进入新局。不会自动换房主或中途补人。

## 维护边界

重开是游戏房间生命周期行为，全部代码在 samples/HookIsland.Network 和 Unity 适配层。没有向纯 C# 帧同步核心增加 Unity、Steam 或小岛玩法依赖。重开不改变输入／模拟存档格式，规则版本与房间版本可以独立演进。

## 验证入口

`./scripts/verify-hook-restart.sh`：真实单人／四人连续重开、完整初态、同帧 checkpoint、身份端口不变、旧消息隔离、重复通知、权限、等待成员及断线。已接入 `./scripts/verify.sh`。

开发构建的原生验收需显式传入 `-island-restart-smoke 1`；正常游玩与发布构建不会挂载自动输入驱动。最终执行结果见本轮 artifacts/restart 下的日志及验收记录。

联机专项还验证了有实际进度的失败局：先通过正常钓鱼获得金币和鱼骨、消耗鱼骨扩建，再死亡并重新开始，资源、额外地块和交易回执均不保留。协议故障测试单独覆盖：队友仍回 UDP 心跳但应用没有确认换局时，房间在确认期限到达后明确失败。

## 本次验收（2026-10-21）

| 检查 | 结果 |
| --- | --- |
| 完整仓库验证 `./scripts/verify.sh` | 退出码 0，包含重开专项 10/10 |
| Unity Editor | 35/35，包含旧输入与请求清理、无房间权限限制 |
| 独立 Development / Release 构建 | 均 0 错误、0 警告 |
| 原生单人 / 四人客户端 | 失败后留在原房重开，epoch 2 第 121 帧已重新捕鱼获币 |
| 状态一致性 | 单人 2 份、四人 5 份完整 checkpoint 字节一致；端口与成员身份不变 |
| 发布隔离 | 正式游戏组件保留，不含任何 SmokeDriver / SmokePilot |

本次证据基于规则/状态 13、输入 7、房间 14、地图 HIM9。全量验证使用两个已协调任务共享的最终日志 `artifacts/enemies-v2/full-verify-final.log`；本任务的 Unity 界面与原生重开另行实测，完整索引见 `artifacts/restart/verification-summary.json`。后续玩法修改需要自己的验证。

独立包在 `artifacts/restart/development/HookPlatform.app` 与 `artifacts/restart/release/HookPlatform.app`，未覆盖其他正在运行的试玩包。可复现四人验收：

```sh
python3 scripts/run-island-party.py --app artifacts/restart/development/HookPlatform.app --players 4 --restart
```

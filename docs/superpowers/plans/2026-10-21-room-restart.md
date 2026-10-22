# 游戏失败后全队重新开始

> 使用 superpowers:executing-plans 在当前任务顺序实现，最终做一次独立审查。

**Goal:** 全队失败后房主一键重新开局，所有成员保留连接和席位，同步恢复开局状态。

**Architecture:** 房间层在同一 UDP 连接上递增 SessionDescriptor.Epoch，等待全部成员确认换局后建立全新的服务器模拟；沿用核心的快照／Ready／Start 屏障。Unity 清理本地输入、请求、窗口与表现，不修改权威玩法状态。

**Spec:** 用户本轮请求及已确认的失败条件：全部最终死亡且无人能支付个人复活费用。

## 规则与边界

- 只有房主可在权威 GameOver 后发起；普通倒地、仍可复活、正常游玩、重开处理中均不可发起。
- 保留房间 ID、端口、连接、玩家身份和席位；平台、装备、金币、背包、商店、鱼群／敌人、死亡账本及模拟计时全部重新初始化。
- 房主可靠发送新局号与地图摘要；全部成员确认后初始化新局。旧局输入／确认帧由 epoch 隔离，不能跨局消费。
- 重复点击／重复通知幂等；未准备好不启动。换局确认等待默认 30 秒，断线或超时沿用明确失败并释放房间的策略，不静默踢人后部分开局。
- 房主退出、主机迁移、失败后的中途加人不在本轮范围。
- 纯 C# 核心不加入具体玩法或引擎依赖；自动验收仅 Editor／Development 且显式启用。

## Review Focus

1. 各通道乱序、旧局消息积压与通知重发不能污染新局或使控制队列饿死。
2. 只有权威失败状态允许房主重开，客户端不能伪造权限；双击和过期确认不能跳过局号。
3. 输入采样器、冻结输入和购买／建造／复活请求必须在新局第一次采样之前清空。
4. 新局完整状态、身份与时钟正确；多个连续重开、等待成员、掉线与超时都可收敛。
5. Unity 状态、窗口、旧对象与开发验收驱动不会跨局泄漏或进入发布构建。

### Task 1: 房间重开协议与确定性验证

**Files:** IslandHost、IslandClient、RoomProtocol、proto/hook-island-room.proto；新增 RoomRestartBarrier、tools/HookIsland.Restart.Tests、scripts/verify-hook-restart.sh。
**Interfaces:** Host.CanRestart/TryRestart/Restarting/RoundEpoch；Client.RoundChanged/Restarting/RoundEpoch；内部确认屏障。

- [x] 先写失败测试，验证正常局拒绝、全部成员确认、局号和协议校验、重复／过期消息、真实单人和四端连续重开。
- [x] 最小实现并确认测试由失败到通过；保留网络连接、身份；逐项检查完整初态与旧局输入隔离。
- [x] 将验证接入 ./scripts/verify.sh。

### Task 2: Unity 入口与整局输入清理

**Files:** IslandFishingController、RevivalOverlay、FishingOverlay；新增 Unity 测试与显式开发验收驱动。
**Interfaces:** 新局通知先清空本地状态；失败页只为房主显示按钮，队友显示等待；重开中显示同步进度。

- [x] UI 清理和权限测试先失败再通过，保留 FishingEquipmentOverlay.ResetSession 调用。
- [x] 与敌人任务交接 Unity 后核对已同步库并运行编辑器测试。
- [x] 独立路径构建 Development／Release，真实多进程完成失败→重开→继续游戏。

### Task 3: 验证、审查与文档

- [x] 完整 ./scripts/verify.sh 通过，记录原生多端同帧完整 checkpoint 和端口不变证据。
- [x] 一位 fresh-context reviewer 审查全部本轮变更；无 Critical / Important / Minor。
- [x] 更新设计／实现说明与验收记录；不提交或回退其它任务文件。

## 执行记录

- Ruling: 在已授权的共享正式目录小块修改，不复制缺少未提交玩法的工作树，不做 Git 提交；基线另存 .build/restart/baseline。代价是需要协调同文件编辑，已明确划分房间协议由本任务独占、敌人任务只改控制器瞄准段。
- Pre-flight: Task 1 的 RoundChanged 必须在采样前调用，Task 2 只清空本地状态；Task 3 的原生验收依赖两者且使用独立产物目录。
- 协调：敌人任务使用 rules/state 13、input 7、HIM9；本任务房间协议升至 14。暂不占用 Unity 或同步 DLL，等待敌人任务交接。

- Task 1 RED: 真实 UDP 行为测试因缺少 TryRestart 失败，见 artifacts/restart/behavior-red.log；初次文件缺 restore 与沙箱 UDP 拒绝属于环境问题，未当成行为 RED。
- Task 1 GREEN: 单人与四人均连续重开至 epoch 3，端口／身份不变；完整初态等于正常初始化，全端同帧 checkpoint 一致。加入迟到旧输入与 Close 的用例时发现验证需显式 Poll 才能把 core outbox 发出，已修正测试后通过。
- Task 1: 屏障、协议校验、重复通知／旧确认、越权请求已覆盖；直接 Dispose 不发送 UDP 关闭包，断线测试需推进到既有 10 秒 idle timeout，不能用半秒无异常误判失败。

- Task 1 complete: 8/8 网络／协议用例通过；新增应用不响应但 UDP 心跳仍活跃的确认超时用例 1/1 通过。证据 network-final.log、barrier-timeout.log；源哈希见 network-source-hashes.json。
- Task 2 prepared: 清理交易队列与窗口的 Unity 测试、最小生命周期补丁、原生失败→重开→钓鱼驱动暂存 .build/restart/unity，尚未导入共享编辑器。
- 额外覆盖已发展的一局：使用正常输入捕鱼赚币、获得鱼骨并铺出一块鱼骨板，再最终死亡和重开，检查资源／扩建／回执归零；没有修改运行中的权威状态或发放测试资源。

- Task 2 RED→GREEN: 两个 Unity 生命周期契约先因缺少重开/清理入口失败，接入后 35/35 Editor 检查通过；最初测试 asmdef 缺少 Network 引用已补齐，该编译错误不计入行为 RED。证据 unity-red.json、unity-green.json。
- Task 2 build: 独立 Development build-8d23c5b515 与 Release build-54b5d99481 均 0 error/0 warning；发布程序集确认不含任何 SmokeDriver/SmokePilot。未覆盖旧试玩包。
- Full verification: 已授权协调的敌人任务确认 ./scripts/verify.sh exit 0，artifacts/enemies-v2/full-verify-final.log 包含当前房间 v14 重开 10/10、复活 26/26、敌人 45/45；验证基于 simulation v13。随后新功能另行验证，不把该证据用于未来修改。

- Task 2 complete: 原生单人及四人均在同一房间 epoch 2 第 121 帧完成重新捕鱼，所有端完整 checkpoint 一致，连接/身份保留、3×3 平台和交易回执恢复初始；四端均仅 4 个角色及 4 个游泳尾迹，无旧对象泄漏。日志 native-1.log/native-4.log；人工查看房主失败页、队友等待页和重开后截图通过。

- Task 3 complete: fresh reviewer restart_final_review 审查全部限定差异，结论 Ready to merge，Critical/Important/Minor 均无，Declined to judge 为空；无需修复或复审。建议未来增加队列饱和专项用例记录在 artifacts/restart/final-review.md，不扩大本轮验证范围。
- 收尾保留本工作目录、未提交改动、基线与验收产物；本轮没有 Git 分支集成、推送或清理其它任务。

- 最后源检查：本任务网络/Unity/协议/启动脚本未变；编辑器当前 HookIsland 程序集与验收包一致。并行鱼挣脱动画任务随后改动 5 个 Simulation 源文件，尚不属于本次 v13 产物；其后续编译和玩法验收由该任务完成。验收索引已明确记录边界，不复写或撤销这些新改动。

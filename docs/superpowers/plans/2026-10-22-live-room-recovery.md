# 晚加入与断线恢复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 房主可提前开始，后来玩家加入空位，掉线玩家在同一应用内恢复原席位与当前世界，不重置个人状态或重复交易。

**Architecture:** 纯 C# 会话层提供固定名单的连接重绑定、非零帧快照和有界追帧；房间层负责恢复凭据、临时/保留席位和在线重开屏障；玩法层区分未出场与已出场掉线；Unity 只提交请求、保存本地凭据并读确认态。

**Tech Stack:** C# netstandard2.1、.NET 8、Unity 2022.3、现有可靠 UDP / Protobuf。

**Spec:** docs/superpowers/specs/2026-10-22-live-room-recovery-design.md

## Global Constraints

- 容量 1～4 固定名单；人数到齐自动开始，已准备的房主可提前开始。默认框架构造器仍要求完整绑定，部分绑定必须显式选择。
- 新连接只接入未活跃席位；完成 K 帧快照和有界追帧后，统一 Rebind 增加 generation。旧连接/旧 generation/旧 epoch 不可操作。
- 同步连接不进入旧玩家共享发送背压；MaxFutureFrames 128、PrepareTimeout 10 秒，消息和字节上限沿用 SessionOptions。失败只丢新连接，缓存释放。
- 原席位凭据至少随机 128 位，验证房间 ID 与槽位，在线席位不可抢占；不打印凭据。首次激活后才永久保留席位。
- 未出场角色不可见、不碰撞、不受伤、不参加救援或失败/终局人数；已出场掉线者留在世界继续危险和自然恢复，自己断钩、停止主动输入，队友可救。
- 重连不回血、不回岸、不补发收益、不重放未确认操作；已确认交易及去重回执来自完整快照。其他玩家的本地待发请求不受影响。
- 终态允许原席位恢复，不允许新人；重开屏障使用当前在线成员，离线席位回来进入当前局。屏障期间断开仍明确失败。
- 规则/状态/房间 22，状态 HISE，输入 10，地图 HIMF；拒绝旧构建和存档，不提供迁移。
- 同应用内恢复，不做主机迁移、账号或退出应用后的自动凭据持久化。Boss 上岸仍后续设计。
- 共享工作目录，不切换分支或自动提交其他任务修改；证据/基线/进度在 artifacts/live-room-recovery 和本计划。

## Review Focus

- Welcome 丢失、未激活同步失败不能永久耗尽席位；已有恢复凭据不能被另一个在线连接抢占。
- 同步中 Send 背压、伪造 Progress 或小窗口不能堵住旧玩家，不能提前获得输入控制权。
- 断开发生于水球击退、合钓抵岸、救援及购买确认边界，所有端必须保持同一份资源和成员结果。
- 终态成员变化与重开屏障须保留身份，但上一局状态与旧本地动作不能带进新局。
- 原始完整绑定会话、旧无装备小岛、正式平台三者仍可独立运行，UI 不能把未出场默认生命当成全员死亡。

### Task 1: 框架有界实时接入

**Files:** 修改 framework/src/FrameSync.Sessions/ServerSession.cs、ClientSession.cs；新增 ServerSession.Live.cs；新增 framework/tests/FrameSync.Tests/LiveSessionTests.cs 并注册 Program.cs。

**Interfaces:** ServerSession 构造尾参 `bool allowUnboundParticipants=false`；只允许省略初始非活跃成员。`bool TryBind(PeerBinding binding)` 在 Running 为确认态非活跃席位捕获当前 checkpoint；`bool IsConnected(ulong connectionId)` 供房间处理同步淘汰。ClientSession 尾参 `bool allowLiveBootstrap=false`。每席位 Peer 持独立 snapshot/hash/tick/offset 与 bounded final queue；正常帧只广播给已追上的 Peer。

- [x] 写 `live_session.partial_roster_starts_and_late_peer_catches_up`：两席位只绑定 0，先执行 5 帧，晚加入 1，继续输入直到 Rebind，断言两个客户端和服务器完整 checkpoint 相等、加入者 generation=2；先运行得到功能缺失失败。Run: `dotnet run --project framework/tests/FrameSync.Tests -c Release -- live_session.`。
- [x] 实现显式部分名单、动态绑定、非零初态、独立发送与增量补帧；Ready 摘要绑定各自快照，等待客户端确认执行追上后才转正常流控。不得用虚构连接补名单。
- [x] 增加断开/多次恢复、旧连接及旧 generation、重复 Ready、低发送预算、已在线/仍活跃拒绝、慢同步超限/超时、新连接发送堵塞/伪造进度、默认严格构造与默认非零快照拒绝的回归。检查同一空位反复恢复不积累旧连接或缓存。
- [x] Run: `dotnet run --project framework/tests/FrameSync.Tests -c Release`；Expected: 全套退出 0，既有预测/保存/重开行为保持。

### Task 2: 未出场与掉线世界行为

**Files:** 修改 HookState.cs、HookGame.cs、EnemyStateCodec.cs、EnemySystem.cs、EnemyPlacement.cs、RevivalSystem.cs、SwimmingSystem.cs 及涉及角色碰撞/拖拽/终局的消费者；新增 tools/HookIsland.Tests/PresenceTests.cs，更新对应版本断言。

**Interfaces:** Fisher 新字段 `bool WaitingToJoin`，默认 false；初始化来自 `!Participant.IsActive`，同时不可操作；Activate/Rebind 清除，Deactivate 只关闭本人主动动作与钩线，保留位置/生命/背包/击退。所有成员更新先于终态早退。状态编码 HISE 保存该标记。

- [x] 先写未出场无伤害/碰撞/救援/失败和终局人数的 RED；正式新角色仅首次成员激活后参与世界。Run: `dotnet run --project tools/HookIsland.Tests -c Release -- --simulation-only --filter=presence_`。
- [x] 接入标记与消费者，区分输入资格和身体在世界中存在；无装备示例不得依赖生命值判断资格。
- [x] 写水中掉线继续扣血、击退完成、队友可钩救/倒地救起、自然恢复无瞬间重置、合钓交接和最终奖励排除、终态 Rebind 与完整恢复 RED→GREEN。
- [x] Run: `dotnet run --project tools/HookIsland.Tests -c Release -- --simulation-only` 及角色/游泳/敌人/复活专项；Expected: 全部退出 0。

### Task 3: 房间席位与同应用恢复

**Files:** 新建 samples/HookIsland.Network/Room/RoomResumeTicket.cs、RoomSeatRegistry.cs；修改 IslandHost.cs、IslandClient.cs、RoomProtocol.cs、proto/hook-island-room.proto；新增 tools/HookIsland.Tests/RecoveryNetworkTests.cs 和 Restart.Tests 对应回归。

**Interfaces:** `RoomResumeTicket` 不可变保存 RoomId/Slot/私有凭据，可安全交回构造器，不含日志格式化。IslandClient 增加可选 ticket 入参和只在激活后可读 `ResumeTicket`。IslandHost `CanStart` / `TryStart()`；Registry 持 1～4 席位的候选/已出场身份、当前连接、凭据，解除临时失败但不释放既有身份。Welcome 增加 epoch/resume_token；Join 增加 room_id/resume_token，原字段不改含义。

- [x] 写真实 UDP 空位晚加入、断开后原位恢复 RED；Use 正常输入积累状态后真实关闭客户端连接，不向运行状态注入资源。Run: `dotnet run --project tools/HookIsland.Tests -c Release -- --filter=recovery_`。
- [x] 实现空位预留、提前开局、凭据校验、Welcome/Ack 幂等与非零帧会话；Host 检查 Session.IsConnected 关闭超限者；Client 仅自身确认活跃后采样输入，按当前帧重设调度器。
- [x] 写错凭据、在线抢占、首次同步失败释放、旧会话/地图拒绝、既有回执不重复扣/奖和 1→2/4 人、2/4 人恢复完整 checkpoint 用例。
- [x] 重开屏障固定在线名单；离线身份保留但新局未出场。写胜利后原席位恢复/新人拒绝、离线后重开与旧局输入拒绝。Run Restart.Tests，Expected: 全部退出 0，包括原屏障断线明确失败。

### Task 4: 可操作入口、原生客户端与最终验收

**Files:** 修改 Unity IslandFishingController.cs、FishingOverlay.cs、FishingPresentation.cs、相关角色 HUD；新增 RecoverySmokePilot/Driver（开发条件编译）、Tests/Editor/RecoverySceneTests.cs；修改 scripts/run-island-party.py 和 verify.sh；新增 docs/live-room-recovery-implementation.md。

**Interfaces:** Controller `CanStartRoom` / `StartRoomNow()` / `CanReconnect` / `Reconnect()`；留存当前连接地址和已激活 ticket；自身断线后清本地面板/未确认命令，重新连接建立新 IslandClient，继续保留独立 Host。UI 文案“现在开始”“重新连接”“正在同步”；未出场不绘制，掉线角色标记“断线”。

- [x] Editor RED 覆盖提前开局、断线/同步按钮资格、未出场隐藏、已出场断线可见及旧请求清除；实现对应输入和只读表现，确认其他人加入不清本地队列。
- [ ] RecoverySmokePilot 只使用正常游戏输入；赚取/消耗资源后实际断开 UDP，驱动调用正式 Reconnect 入口，恢复后继续钓鱼；另测四人陆续加入。显式 --recovery/--late-join 参数启用，发布剔除。
- [ ] 真实独立 1／2／4 客户端，查看恢复/断线和角色表现，逐字节比对完整 checkpoint；Unity 全套及开发/发布严格构建，校验发布无验收类型和程序集一致。
- [ ] `./scripts/verify.sh` 完整退出 0；执行一次独立最终复核，Important/Critical 用 RED→GREEN 修复并复验，不进行第二轮复核。
- [ ] 更新主设计/兼容说明/缺口清单及 artifacts/live-room-recovery/verification-summary.json，明确本机 UDP、同应用凭据边界；继续整体体验任务。

## 执行记录与裁定

- Builder check: live_room_recovery_design_check 只提出掉线危险策略，未发现其他必须补答项；已采用持续世界危险的实现者默认值，尚非用户确认。成本若用户选择隐藏冻结，需修改伤害/救援/碰撞及相应用例。
- Ruling: 继续已授权总目标，内联实现；新计划不另求重复批准。不自动提交共享修改，保存文本基线与证据。
- Pre-flight: Task 1 提供 TryBind/nonzero bootstrap → Task 3 使用；Task 2 WaitingToJoin 与成员事件 → Task 3 初始预留名单/Task 4 隐藏表现；Task 3 只公开已激活 ticket → Task 4 恢复入口。generation/epoch 与交易请求号保持分别隔离，不互相替代。
- Ruling: 同步连接使用独立发送，避免共享 Outbox 队首背压影响已有玩家；身份在首次激活前属临时，失败可释放。成本为新增有限生命周期状态，必须覆盖失败边界。
- Self-review: spec 的提前开始、空位、恢复身份、世界危险、追帧限额、重开、终态与发布隔离分别映射 Tasks 1～4；五项 Review Focus 均有对应失败输入。保持协议与玩法分层，不引入账号/持久化/上岸扩展。

- Task 1 complete: 首批两个真实会话测试 RED 0/2（Bindings must cover the roster）→ GREEN 2/2。补充边界共 16 项全部通过；框架全套 147/147，退出 0（framework-task1-final.log）。测试覆盖四人同时接入、预测从非零快照开始、十次重绑定、旧输入拒绝、单消息预算、小窗口、快照背压、帧/消息/字节限额及超时；原有 131 项均通过。沙箱中 dotnet 启动报告 CSSM_ModuleLoad 系统提示，未产生编译警告或测试失败。
- Task 1 Ruling: 初始部分名单至少保留一个实际连接，不允许没有任何参与者的房间自动开始；动态接入仅接受尚未绑定且确认态已停用的席位。宿主仍负责凭据认证，框架不存玩法或账号资料。

- Task 2 complete: 首批 presence RED 0/7 → 扩展 10/10；胜利后全员离线存档另经 RED→GREEN。玩法模拟 314/314、角色模拟 25/25、游泳含 UDP 27/27、敌人含 UDP 45/45、复活含 UDP 26/26 通过。原有合钓离线交接与不恢复旧线的用例继续通过。证据为 presence-*-full.log。
- Task 2 Ruling: WaitingToJoin 使用单独 PRS1 编码段，保持敌人状态职责单一；HISE/22 拒绝旧存档。胜利记录不再依赖当前在线人数，断线更新成员但冻结玩法时钟和结果。
- Task 2 Ruling: 初版恢复测试把正常回复上限误写为 1000；现按默认 5 点/秒、30000 定点单位/点、30 Hz，验证每帧恰好 +5000。旧敌人用例的“掉线免疫”按新设计改为“未入场不受攻击、掉线身体仍受攻击”。首次误给游泳专项 --simulation-only 导致 0/0 退出 1，随后用其正式无参数入口完整运行通过，不将零用例算为验证。

- Task 3 complete: recovery 过滤器 17/17（含 3 项既有海域恢复用例），重开全套 18/18。真实 UDP 覆盖提前开始、四人逐个接入、错误/在线凭据拒绝、重复 Welcome/Join/Ack、未完成同步释放、2/4 人购买回执与完整状态保留、恢复后继续捕鱼、终态恢复及离线身份进入新局。证据 room-task3-final.log、restart-full.log。
- Task 3 RED→GREEN: 首次激活后的重复 Join 被误拒绝，限定同一连接的幂等重试后协议 8/8；同步旧房主积压导致新玩家追帧超窗，TryBind 暂缓直到截止帧处理完毕，框架全套 148/148（framework-backlog-green.log）。
- Task 3 RED→GREEN: 4 人连续接入暴露 UDP ACK 持续耗光控制预算，Retry 无法积累发送额度。独立测试先失败，再为握手包分出有界 2400 B 突发、12000 B/s 预算，仍受总带宽/数据报预算约束。UDP 全套 31/31，四人逐个接入通过；wire/options 不变（udp-handshake-red.log、udp-full.log、room-late-four-green.log）。
- Task 3 Ruling: 测试中的中断从客户端实际发送 Close，宿主沿真实事件流程停用成员；不能直接关闭宿主底层 node 却跳过其会话停用。首次沙箱 UDP 权限错误不计作代码失败，授权环境复验通过。
- Task 4 progress: 3 项表现/入口 Editor RED 0/3 后刷新全部资源转绿；最初 scripts-only 刷新未导入新 partial 文件，旧域结果不计为新实现验证。第四项本地待发请求测试初值缺少 Upgrade，修正为合法请求后复验。

- Task 4 Editor 98/98 全套通过，包含独立房主继续运行、本地失效队列清除、别人加入不清自己的请求、原位表现与旧奖励不重播。新开发驱动显式开关并排除默认平台驱动，避免双输入源。
- Task 4 native diagnostic: 首次单人恢复通过；双人静止状态比较失败，记录差异后确认唯一区别为生命 80→66，角色在平台外沿落水，断线期间按设计继续失血。验收现用正常移动先回平台内，再进行静止状态比较，不改落水规则，不放宽资源或身份断言。对应失败日志 native-2.log、native-2-diagnostic.log；待最终复验。
- Task 4 build consistency: 全量构建后发现 Unity 插件与最新 Network DLL 摘要不同，已重新 sync 全部框架／游戏库并重建；最终摘要检查与原生验收使用同步后的产物，不沿用旧产物作为最终依据。

- Task 4 closing-boundary RED→GREEN: 四人原生恢复暴露 CloseAck 被控制预算阻塞时 Transport 已 Closing、最终 Disconnected 尚未通知，Session 发帧导致整房失败。确定性 UDP 用例先复现 NotConnected 且尚无事件，再由 UdpNode.IsConnected / IslandHost 发帧前停用解决；close-pending-red.log 0/1 → close-pending-green.log 1/1，UDP 全套 31/31。
- Final-review fix: 正常行走落水构造失败后，未出场身体 Rebind 会逆转失败。现胜利/失败都先冻结世界；原跨局身份可以查看终态，未出场身体不激活。presence terminal RED→11/11；真实 UDP 失败→离线→重开→再次失败→返回用例通过（terminal-epoch-green.log）。
- Final-review fix: 首次入场服务端已确认、客户端未收到激活帧的窗口丢失公开凭据。新增 RecoveryTicket 保存待确认证明；ResumeTicket 仍仅代表确认入场，输入门槛不变。临时席位释放后可重试，但新人可占用并替换旧证明，终态不接受未入场证明。激活前后两个真实断线用例 RED 0/2 → GREEN 2/2（entry-proof-*.log），规则/文档同步。
- Task 4 Editor final: 新增首次入房失败仍有显式恢复入口，全部 99/99 通过（job c8131ac434f8459f99ad6777936b86ae）；待更新构建后原生复验。

# 已确认事件与控制器职责拆分

**Goal:** 在保持现有玩法、多人一致性和存档兼容性的前提下，以已确认结算事件驱动奖励、伤害飘字和音效，并把主控制器中的房间、存档和输入职责提取为独立对象。

**Architecture:** 纯 C# 模拟在事务成功时产生游戏事件；既有 FrameSync 确认帧携带稳定的 session/epoch/tick/index 标识；IslandClient 仅交付确认事件；Unity 每次 Update 消费，GUI 只绘制。持续状态与本地移动预测保持现有读取边界。

**Tech Stack:** C# / .NET 8 测试、netstandard2.1 游戏模块、Unity、现有 UDP 帧同步。

**Spec:** 用户本轮授权；仓库 AGENTS.md；docs/agent-workflow.md；现有存档等待全队收杆、钩救和救援结束的规则。

**Global constraints:** 不改变游戏数值、结算、状态编码和存档格式；不使用预测事件播放奖励；失败交易不发收入事件；重连/读档/重开不补播旧事件；无常驻验证组件；保留现有控制器对 UI 的接口和场景序列化字段。仓库已有大量未提交工作，本次在当前正式工作目录维护，不迁走 Unity 工程，不提交无关改动。改动前快照存于 artifacts/presentation-separation/baseline，作为审查基线。

**Review focus:** 同帧收入和支出、同帧伤害和治疗、多个确认帧一次交付、重复 GUI/轮询、重连/读档/新一局事件基线、保存后离开仅发生在落盘成功后、房间切换清理、预测不污染反馈。

### Task 1: 纯模拟事件与确认交付

- [x] 创建纯 C# 的游戏事件类型、负载编解码及明确的事件种类，事件只表达已发生的结果，不含 Unity 引用。
- [x] 在成功的事务/伤害/行为发生点产生事件，覆盖金币、材料和装备收入、扣血、出钩、成功收鱼、队友钩中、连击增加、升级、近战挥动/命中；以现有表现使用需求为准，不能通过渲染帧余额差推测收入。背包失败/地面溢出/重复请求不得虚报，拾取只报实际收入。
- [x] HookGame.Step 返回事件负载，经现有 FrameSync 稳定标识交付。事件收集不进入存档/状态哈希，不改变游戏结算。
- [x] IslandClient.Poll 收集两个 Session.Poll 调用返回的确认帧，提供 IReadOnlyList<ConfirmedGameplayEvent> ConfirmedEvents（仅本次 Poll 的有序批次）。每个 ConfirmedGameplayEvent 包含 SessionId/Epoch/Tick/Index 与 HookGameplayEvent Event。清空旧批次、有界存储、按完整帧标识去重，重开/重连/读档不补播历史。严禁使用 LastPredictionEvents。
- [x] 增加针对性纯 C# 测试：事件与事务一致、失败和重试不重复、同帧赚取并消耗、伤害后治疗、多个确认帧、预测/重复轮询、四客户端确认事件和状态一致、恢复后新事件正常。
- [x] 仅修改 samples/HookIsland.Simulation、samples/HookIsland.Network、tools/HookIsland.Tests（需要额外测试项目时先说明）。不修改 Unity，不运行全量 verify.sh，不改框架，不提交 Git。
- [x] 输出 artifacts/presentation-separation/task1-report.md，说明状态、接口、文件、测试精确命令/结果与局限。提供事件字段和用法以便 Unity 接入。

### Task 2: 控制器组合与独立职责

- [x] 将房间连接、重连、开始/重开、轮询和生命周期提取为房间会话对象；保留主控制器只读代理接口。
- [x] 将保存请求、等待安全点、自动保存、读取校验、本地身份和成功后离开提取为保存服务，显式依赖与时钟，保留原有事务顺序。
- [x] 将输入采集、相机输入、瞄准辅助与待发指令提取为独立输入对象；控制器保留场景装配和每帧协调，避免移到另一个大类。
- [x] 扩展必要的 Unity 集成测试，验证会话重置、保存/继续、原有镜头和输入行为。

### Task 3: 表现事件消费者

- [x] Unity 单一入口每个 Update 消费确认事件，生命周期变化重置；OnGUI 只读取展示状态，不产生反馈。
- [x] AcquisitionFeedback 和 HealthDamageFeedback 使用明确事件，保留图标、排队、消隐与伤害合并视觉效果。
- [x] 音效与连击跳动使用确认事件；连续血条/耐力条、角色姿态等继续按状态插值；本地移动/挥杆预测不发结算音效。
- [x] 针对重复交付、已加载状态无补播、多个事件、满包/交易等添加表现测试。

### Task 4: 审查与验证

- [x] 分任务独立代码审查，修复确证问题；最终审查任务边界与跨层接口。
- [x] 跑针对性事件/存档/联机测试，Unity 重新编译、相关 EditMode 集成测试。
- [x] 最终执行一次 ./scripts/verify.sh 并保存报告；避免并发占用同一测试构建输出或 Unity 编辑器。
- [x] 更新架构说明和实现范围，记录完成证据与未验证边界。

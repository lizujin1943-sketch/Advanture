# 初版剩余核心功能实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. 独立的文件存储子任务按 dispatching-parallel-agents 分工；共享协议、成员和预测集成由主实现者维护。步骤使用复选框记录。

**Goal:** 完成用户确认的初版剩余功能：成员事件、客户端预测回滚、确认状态存读档，以及无引擎验证工具。
**Architecture:** 保留服务器最终确认规则与 ITransport 边界；在不可变帧中加入成员事件，在完整检查点中保存动态成员状态；客户端增加有上限的预测历史和事件交付。存档外壳与文件系统适配独立，服务器停止后先保存成功再关闭。
**Tech Stack:** C# 10、核心 netstandard2.1、控制台与测试 net8.0，无第三方包。
**Spec:** ../specs/2026-10-10-initial-core-scope-design.md；../specs/2026-10-10-frame-sync-core-design.md。

## Global Constraints

- 所有产出在 /Users/lizujin/Documents/project/git_project/Advanture，保持 Unity、Steam 和玩法无关。
- 本次范围由用户再次确认：初版剩余内容；中途加入、完整重连、自动调节提前量、平台接入仍是后续工作。
- 服务器迟到拒绝、缺失补中性输入、最终帧不可改写；客户端回滚不恢复服务器已拒绝的操作。
- 默认保留只执行确认帧的兼容模式；预测必须显式开启。
- 所有改变模拟的调用串行；输入采集、网络驱动、文件系统由宿主/适配层提供。
- 每个新行为先添加失败测试；不依赖 Unity、外部服务或 NuGet。
- 协议/检查点/录制若字段改变显式升级版本；旧格式明确拒绝，不假装兼容。
- 复用已指定目录和当前非主分支，不创建目录外 worktree；不自动推送或发布。

## Review Focus

1. 本地输入迟到、输入字节相同但来源不同，都必须修正完整框架状态。
2. 回滚跨多次 Poll 时，确认事件不得重复，显示状态不得暴露尚未完成的追赶。
3. 成员停用、控制权变更必须随帧执行，旧连接和旧代数输入无效，检查点/重放保持完整。
4. 写入失败保留旧档；坏档/过大文件/不匹配版本读入失败，不更改运行中状态。
5. 慢客户端、历史/字节预算、关闭时未确认预测、读档旧消息，均有有界且可验证的行为。

## Task 1: 成员事件与完整快照

**Files:** Abstractions 的成员/帧/上下文；Runtime 的驱动器、检查点、输入格式、录制；Sessions 的 Wire/ServerSession；核心成员测试。
**Interfaces:** MemberEvent(Sequence, Kind, Slot, Generation)；MembershipState；InputFrame.MemberEvents；FrameContext.MemberEvents；Checkpoint.Membership/HistoryStartMembership；ServerSession.Deactivate(connectionId)。
**Decisions:** 固定稳定身份名单，动态状态记录激活和控制代数。每帧先成员事件，再有效参与者输入；历史开始前的成员状态随检查点保存，允许验证截断历史。新开局可将成员初始化为未激活，由第一帧激活；已有全激活调用继续有效。Rebind 的核心转换要求代数恰好加一，完整联网重连后做。

- [x] 先测停用后输入不执行、重绑定拒绝旧代数、恢复后重放含成员变化、非法事件不污染旧状态。
- [x] 运行新增测试，确认失败来自缺失功能。
- [x] 实现有序成员转换、输入校验、历史/缺失计数恢复；更新协议版本和预算。
- [x] 服务器接受宿主停用请求，在下一允许帧发布事件，移除断线连接的发送和进度等待。
- [x] 运行核心及现有 A/B/E 回归。

## Task 2: 预测回滚与事件交付

**Files:** Sessions/PredictionOptions.cs、ClientPrediction.cs、PredictionEvents.cs、ClientSession.cs、核心预测测试。
**Interfaces:** ClientSession 构造可选预测配置和中性输入工厂；Poll(predictionTarget)；PredictedTick、IsReplaying、InspectPredictedState、LastPredictionEvents、RollbackCount；CaptureCheckpoint 保持只返回确认状态。
**Decisions:** 最多预测 12 帧，至少保留 V..P 完整检查点；真实输入、成员事件和输入来源共同参与比较。恢复最早差异之前一帧，重算可跨 Poll；确认进度单调。事件通知提供预测、失效和确认，按会话/epoch/帧/序号标识；确认副作用只通过原 Poll 返回结果交付一次。

- [x] 先测本地预测即时生效、100..104 纠正恢复 99、同字节不同来源、迟到输入纠正、窗口/内存限制。
- [x] 先测重算分批、第二次纠正、预测事件替换与确认去重、关闭丢弃未确认预测。
- [x] 运行失败测试，再实现有界历史、最早差异检测、预算驱动和事件对比。
- [x] 增加固定输入提前调度工具，明确采样与发送独立于模拟推进，不声称自动估计单程延迟。
- [x] 与现有只确认模式一起验证，旧调用保持行为。

## Task 3: 独立存档外壳与文件适配

**Files:** 新 FrameSync.Persistence 和 FrameSync.Storage.FileSystem 工程、独立持久化测试。
**Interfaces:** SaveDocument / SaveCodec；ISaveStore；SaveService；FileSaveStore。
**Decisions:** SaveDocument 包含存档 UUID、完整检查点和完整性校验；只支持当前格式。文件适配同目录临时文件、刷新、回读校验、原子替换，失败保留旧档，同路径写入串行或明确拒绝并发。文件大小先检查再读入。核心不读取路径或系统时钟。

- [x] 独立子任务先测损坏/截断/超限、保存覆盖成功、故障保留旧档、并发写保护。
- [x] 运行失败测试后实现存档编码与文件适配。
- [x] 主实现者集成 CheckpointRestart：新 SessionId、累计帧保留、成员身份保留且控制权更新、清除旧输入与历史。
- [x] ServerSession 接受载入的 S[0]；停止保存失败可重试，成功才关闭。
- [x] 两个新进程保存/读档，继续运行并核对状态与身份。

## Task 4: 可运行验证、示例和文档

**Files:** tools 下独立预测/恢复演示；UdpHarness；samples/CounterHeadless；scripts/verify.sh；README 和 docs。
**Decisions:** 框架每玩家每帧一份不变输入；示例增加有序多操作输入，宿主采样合并后提交。真实 UDP 使用同一会话实现。

- [x] 测试 Counter 同帧多操作顺序及非法批量数据，完善中文注释。
- [x] 两个独立模型验证预测回滚、确认事件、最终完整状态一致。
- [x] 真实多进程 UDP 验证预测和保存/重开，至少一例断线后其余成员继续。
- [x] scripts/verify.sh 纳入新增验收，输出产物保存在 artifacts/initial-core。
- [x] 更新阶段 C/D 与成员实现说明、代码阅读顺序、当前限制。
- [x] 完整验收后独立代码审查，修复有证据的问题并重新运行相关测试。

## 验证命令

```sh
PATH=/usr/local/share/dotnet:$PATH ./scripts/verify.sh
```

测试是控制台入口，dotnet test 不执行这些断言。Windows 无可用执行环境时如实保留待验证状态，不把 macOS 结果当作跨平台实测。

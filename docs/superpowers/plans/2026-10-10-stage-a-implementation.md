# Stage A Deterministic Simulation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 实现独立于引擎与玩法的模拟驱动、完整检查点、有限输入录制与重放，并提供两个控制台模型。

**Architecture:** 核心接口与运行库为两个纯 C# 工程。游戏提供模拟、状态编码和输入格式验证；核心只处理帧、参与者、输入字节和状态。样例及工具向内引用核心，核心测试使用自有测试模型。

**Tech Stack:** 本机 .NET SDK 8.0.403；核心 netstandard2.1；控制台与测试 net8.0；无外部包。

**Spec:** [核心技术方案](../specs/2026-10-10-frame-sync-core-design.md)、[初版范围：阶段 A](../specs/2026-10-10-initial-core-scope-design.md)。

## Global Constraints

- 框架没有 Unity、Steam、Socket、具体坐标或玩法依赖。
- 帧号为 ulong；S[0] 是初始状态；输入按位置固定排序，接受后不可修改。
- 状态包含游戏数据和当前阶段的框架数据；恢复任意保存点后可重放。
- 参与者稳定身份、位置及控制权编号分开。阶段 A 使用固定成员表；成员事件调度属于 B。
- 帧、输入、历史、事件、检查点和录制均限制数量或字节。
- 不实现服务器收集、实时预测回滚、网络、原子文件存档或冷启动身份恢复。
- 只修改 Advanture 目录；当前父仓库有其他工作，不提交、不切换父仓库分支。
- 跨系统验证目标 Windows/macOS；本次只能据实报告实际运行环境。

## Review Focus

- 调用者修改原输入数组、快照输出数组或恢复得到的对象，不能污染已保存记录。
- 重复／跳号／旧会话／旧控制权输入，在调用游戏前拒绝，不能改变当前进度。
- 快照、录制长度被伪造、截断、版本错误或摘要损坏时，不能无界分配或破坏当前模拟。
- 游戏 Step 部分修改后抛异常时，停止继续推进；仅显式恢复有效检查点才能重新使用。
- 快照遗漏随机状态或框架输入来源历史时，尾段重放必须能发现差异。

---

### Task 1: 纯 C# 接口与确定性推进

**Files:** 根目录 Directory.Build.props、NuGet.Config；framework/FrameSync.sln；src/FrameSync.Abstractions/*.cs；src/FrameSync.Runtime/SimulationRunner.cs；tests/FrameSync.Tests/{Program,Assert,TestSimulation,RunnerTests}.cs。

**Interfaces:**
- ISimulationFactory<TState>.Create()、IDeterministicSimulation<TState>.Initialize/Step/Capture/Restore。
- IStateCodec<TState>.Encode/Decode，IInputValidator.ValidateCanonical。
- SimulationRunner<TState>(module, session, limits, initialization)，Advance(InputFrame)、Tick、InspectState()。
- 不可变 SessionDescriptor、Participant、InputFrame、RecordedInput、ByteString；Step 只看到 SimulationInput，不暴露预测标记。

- [x] 先写真实整数测试模型与轻量控制台断言程序，最小骨架使测试能编译。
- [x] 写并运行推进、顺序、冻结与拒绝测试，确认失败由缺失行为导致。例如：
```csharp
runner.Advance(Frame(1, Input(slot: 1, value: 7), Input(slot: 0, value: 3)));
Assert.Equal(37L, runner.InspectState().Value);
Assert.Equal(1UL, runner.Tick);
```
这里测试模型按顺序做十进制拼接；固定预期 37 能发现排序错误。
- [x] 实现固定帧上下文、输入检查、实例隔离、有界事件与故障状态。
- [x] Run: dotnet run --project framework/tests/FrameSync.Tests -- runner
  Expected: 所有 runner 测试通过；无游戏代码依赖。

### Task 2: 完整检查点与有限录制重放

**Files:** src/FrameSync.Runtime/{Checkpoint,CheckpointCodec,ReplayRecording,ReplayCodec,ReplayPlayer,BinaryFormat}.cs；tests/FrameSync.Tests/{CheckpointTests,ReplayTests}.cs。

**Interfaces:**
- runner.CaptureCheckpoint() / Restore(Checkpoint)。
- CheckpointCodec.Encode/Decode；版本化小端编码、SHA-256、规范框架记录、受限解码。
- ReplayRecorder(initialCheckpoint, limits).Append(frame).Build()。
- ReplayPlayer.Play(runner, recording)；ReplayCodec.Encode/Decode。
- 每个检查点携带成员、累计帧、有限输入历史、连续缺失次数和独立游戏状态字节。

- [x] 检查点骨架配套恢复测试，先看到缺失恢复行为导致的失败：
```csharp
var saved = runner.CaptureCheckpoint();
runner.Advance(next);
var expected = runner.CaptureCheckpoint();
runner.Restore(saved);
runner.Advance(next);
Assert.Bytes(CheckpointCodec.Encode(expected), CheckpointCodec.Encode(runner.CaptureCheckpoint()));
```
- [x] 增加字节外部修改、不同规则版本、坏摘要、截断、长度上限和 Restore 失败不改现有状态的测试，逐项 RED→GREEN。
- [x] 实现规范编码与事务式恢复：在新模拟实例验证完整性后再替换当前实例。
- [x] 增加初始／中间检查点输入重放、录制上限、帧缺口和错误格式测试，先失败再实现。
- [x] Run: dotnet run --project framework/tests/FrameSync.Tests
  Expected: 全部测试通过；录制中只存不可变数据，重放不执行外部操作。

### Task 3: 两个独立样例与控制台验收

**Files:** samples/{CounterHeadless,GridHeadless}/*.cs；tools/HeadlessHarness/*.cs；tools/HeadlessHarness.Tests/*.cs；README.md；scripts/verify.sh；docs/stage-a-implementation.md。

**Interfaces:**
- 两个样例各自实现 Task 1 的公开接口，不改变 Runtime。
- Counter：离散整数操作及可恢复的随机进度；Grid：各参与者的二维整数状态与持续输入。
- 控制台参数 --frames、--output、--replay、--report。输出录制与检查点验证结果；错误以非零退出码返回。

- [x] 为样例写手算状态、输入合法性与快照独立性测试，先失败再实现。
- [x] 工具录制指定输入，取多个中间检查点，继续执行后分别恢复、重放尾段，与基准状态及事件比较。
- [x] 两个样例分别跑至少 100000 帧，记录周期摘要；导出录制后在新进程重放。
- [x] Run: ./scripts/verify.sh
  Expected: 核心测试、样例测试、两模型控制台验证、跨进程重放通过。
- [x] 核心和核心测试单独构建，不引用 samples；文档提供 Windows/macOS 通用 dotnet 命令。
- [x] 根据最终代码做一次独立审查；重要问题用失败测试重现后修正。
- [x] 更新已实现范围、执行证据与剩余阶段；不将 macOS 实测写成 Windows 已通过。

## 执行记录

用户已明确要求开始实现阶段 A，按当前授权直接执行，不另设设计审批步骤。过程中采用红→绿测试循环。详细结果记录在 [stage-a-progress.md](stage-a-progress.md)。

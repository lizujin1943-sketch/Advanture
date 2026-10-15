# 可复用帧同步框架

供自己的不同游戏共用的纯 C# 框架。当前已完成阶段 A，并实现 **阶段 B 的首个通信闭环：一个服务器和 1～4 个客户端通过内存字节消息同步输入、分别执行并核对完整状态**。已进一步接入 **阶段 E：自研可靠 UDP 与独立服务器/客户端进程**。C（预测回滚）和 D（正式存档）按当前决定暂缓。

模拟、会话与可靠传输算法没有 Unity、Steam 或具体玩法依赖；Socket 仅存在于独立接入工程。两个示例分别提供自己的规则、输入格式和状态，框架不需要随示例切换而修改。

## 先体验两个客户端同步

```bash
dotnet run --project tools/SyncHarness -c Release
# 手动输入 a 5、b 3，观察另一客户端执行后的结果：
dotnet run --project tools/SyncHarness -c Release -- --interactive
```

默认演示中 A +5 后，服务器、A、B 都是 5；B 再 +3 后，三端都是 8。这组内存演示在一个进程中运行多个独立世界；真实 UDP 多进程启动方法见 [阶段 E 实现说明](docs/stage-e-implementation.md)。实现与阅读顺序见 [阶段 B 实现说明](docs/stage-b-implementation.md)。

## 运行

需要 .NET 8 SDK。工程不使用第三方 NuGet 包，`NuGet.Config` 关闭外部包源。请在本目录执行命令。

macOS / Linux / Git Bash 一键验收：

```bash
./scripts/verify.sh
```

它会构建核心和工具，执行测试，运行两个模型各 100000 帧，验证多个中间检查点恢复后的状态和事件，最后在两个新进程中读取录制文件并核对最终摘要。还会运行阶段 B 的会话与交互测试，以及两个模型各 10000 帧的四客户端输入同步、完整状态和事件校验。还会运行阶段 E 的故障网络测试和真实 UDP 多进程自动/交互输入验收。任何失败都会返回非零退出码。

Windows PowerShell、macOS 终端均可逐条执行：

```text
dotnet build framework/FrameSync.sln -c Release --disable-build-servers -m:1
dotnet framework/tests/FrameSync.Tests/bin/Release/net8.0/FrameSync.Tests.dll
dotnet build tools/HeadlessHarness.Tests -c Release --disable-build-servers -m:1
dotnet tools/HeadlessHarness.Tests/bin/Release/net8.0/HeadlessHarness.Tests.dll
dotnet tools/HeadlessHarness/bin/Release/net8.0/HeadlessHarness.dll --frames 100000 --output artifacts/stage-a --report artifacts/stage-a/report.json
```

测试入口是上面的控制台程序，**`dotnet test` 不会运行这些断言**。可先用 `--frames 300` 快速体验，再执行完整验收。

导出后，重新开启终端运行：

```text
dotnet tools/HeadlessHarness/bin/Release/net8.0/HeadlessHarness.dll --replay artifacts/stage-a/counter.fsrp
dotnet tools/HeadlessHarness/bin/Release/net8.0/HeadlessHarness.dll --replay artifacts/stage-a/grid.fsrp
```

加上 `--expected-hash <摘要>` 可以让结果不一致时直接失败；摘要见同名 `.sha256` 文件。`--help` 查看完整参数。

## 目录与职责

| 目录 | 内容 |
| --- | --- |
| `framework/src/FrameSync.Abstractions` | 游戏要实现的接口、不可变输入、参与者与帧信息 |
| `framework/src/FrameSync.Runtime` | 按帧推进、检查点、编码校验、输入录制和重放 |
| `framework/src/FrameSync.Transport` | 只收发字节的传输接口、有界内存网络 |
| `framework/src/FrameSync.Sessions` | 服务器/客户端会话、分块开局、确认输入广播、进度反馈与一致性检查 |
| `framework/src/ReliableUdp.Core` | 独立可靠 UDP：连接、确认重发、分片、流控、拥塞退让，无框架/游戏依赖 |
| `framework/src/FrameSync.Transport.Udp` | 非阻塞 Socket 和 ITransport 适配器 |
| `framework/tests/ReliableUdp.Tests` | 仅依赖 UDP 核心的故障与边界测试 |
| `framework/tests/FrameSync.Tests` | 核心行为测试；不引用两个示例 |
| `samples/CounterHeadless` | 整数操作示例，演示保存随机数的当前进度 |
| `samples/GridHeadless` | 网格示例，演示多个参与者各自拥有状态 |
| `tools/HeadlessHarness` | 控制台验收与文件导出，具体文件操作留在这里 |
| `tools/HeadlessHarness.Tests` | 示例手算结果、输入验证、命令行与坏文件测试 |
| `tools/SyncHarness` | 一个服务器与多个客户端的自动/手动同步演示 |
| `tools/SyncHarness.Tests` | 同步演示的命令行、交互输入与人数验证 |
| `tools/UdpHarness` | 独立服务器与客户端进程，支持真实 UDP 自动/交互输入 |
| `tools/UdpHarness.Tests` | 启动多进程并核对完整状态和输入记录 |

核心和两个示例均以 `netstandard2.1` 为目标；工具与测试以 `net8.0` 为目标。框架可单独构建：`dotnet build framework/FrameSync.sln`，不需要 samples、Unity 或游戏工程。

```mermaid
flowchart BT
    Sessions[FrameSync.Sessions] --> Runtime[FrameSync.Runtime]
    Sessions --> Transport[FrameSync.Transport]
    Runtime --> Contracts[FrameSync.Abstractions]
    SyncHarness[同步控制台] --> Sessions
    Counter[Counter 示例] --> Contracts
    Grid[Grid 示例] --> Contracts
    Harness[控制台验收] --> Runtime
    Harness --> Counter
    Harness --> Grid
    Tests[核心测试] --> Runtime
```

## 保存与重放如何使用

“检查点”是一帧计算结束后的完整状态。`S[0]` 表示尚未执行任何输入；保存 `S[100]` 后，下一帧必须是 101。

```csharp
// module 由游戏提供；session 描述固定参与者和逻辑帧率。
var runner = new SimulationRunner<MyState>(module, session);
var recorder = new ReplayRecorder(runner.CaptureCheckpoint());

runner.Advance(frame1);
recorder.Append(frame1);                  // 推进成功之后，记录这一帧输入
byte[] saved = CheckpointCodec.Encode(runner.CaptureCheckpoint());

runner.Advance(frame2);
recorder.Append(frame2);

runner.Restore(CheckpointCodec.Decode(saved)); // 恢复到 frame1 结束后的状态
runner.Advance(frame2);                       // 重新计算 frame2

byte[] replayBytes = ReplayCodec.Encode(recorder.Build());
var replay = ReplayCodec.Decode(replayBytes);
var fresh = new SimulationRunner<MyState>(module, replay.Initial.Session,
    replay.Initial.Limits, replay.Initial.InitializationData);
Checkpoint final = ReplayPlayer.Play(fresh, replay);
```

这是接口用法示意；可运行的调用见 [HeadlessHarness](tools/HeadlessHarness/HarnessApp.cs)。示例中的 `MyState`、`module` 和两帧输入由具体游戏定义。

每个游戏实现四类约定：创建独立模拟实例、按输入计算一帧、完整捕获/恢复状态、将状态和输入编码为稳定字节。随机数进度也必须进入状态。完整解释见 [阶段 A 实现说明](docs/stage-a-implementation.md)。

## 当前边界

阶段 A 的调用者直接提供一整帧输入，帧率只是固定逻辑时间信息，没有网络收集、等待超时或真实时间调度。阶段 B 已在它外面增加独立的服务器和客户端运行流程，采用宿主传入的单调时间收集输入和确认帧。当前名单固定，可选择可靠有序内存传输或自研可靠 UDP。暂未实现预测回滚和途中加入。

现在可以生成和恢复检查点字节，以及导出用于验证的 `.fscp` / `.fsrp` 文件。正式玩家存档所需的原子替换、备份、旧版本迁移、重新开房身份处理留到阶段 D。文件格式暂定版本 1；后续升级必须显式处理版本兼容。

本次实测环境是 **macOS arm64、.NET SDK 8.0.403**。已通过两个模型各 10 万帧及跨进程重放；另已通过本机真实 UDP 多进程一致性验证；Windows 和 Unity 尚未运行验证。Windows 验证时可用同一录制运行并比较 `report.json` 中所有对应帧的 `PeriodicHashes`。

完整目标和后续阶段见 [文档入口](docs/README.md)。

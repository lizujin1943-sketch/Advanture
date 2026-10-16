# 初版核心补齐：预测回滚、成员变化与正式存读档

日期：2026-10-15。本文说明本次新增的实际接口和阅读顺序。框架继续保持纯 C#；Unity、Steam、角色、武器等不进入核心。完整验收结果由 `scripts/verify.sh` 输出，Windows 实测仍待完成。

## 1. 现在这条流程怎样运行

服务器收集未来帧的输入，到截止时间后形成最终输入并计算。客户端先逐帧冻结自己的输入，没有操作也明确记录中性输入，再用这些已确定的本地输入和其他人的预测中性输入计算；收到最终输入后，如果之前用的内容不同，就恢复旧状态重新计算。最终状态摘要一致，才把这一帧标记为确认。

举例：客户端已经算到 104，服务器告诉它第 100 帧另一位玩家提交了操作。客户端恢复的是第 99 帧结束的状态，然后重新算 100、101、102、103、104。核心测试 `prediction.frame_104_restores_99_when_frame_100_changes` 验证了这个过程。

服务器仍然拒绝超过截止时间的输入。客户端预测出的操作若没有被服务器接受，会在纠正后消失；框架不会偷偷将它转移到下一帧。

## 2. 先理解这几个数据

| 代码名称 | 含义 |
| --- | --- |
| `ConfirmedTick` | 已经用服务器最终输入执行，并通过完整状态校验的帧号 |
| `PredictedTick` | 客户端内部当前计算到的帧号，回滚时可以暂时倒退 |
| `PresentedTick` | 当前可供画面显示的状态帧号；分批重算期间保持上一份完整结果 |
| `IsClosing` | 已收到关闭通知；停止提交输入，继续接收并确认最后一批帧 |
| `Checkpoint` | 某一帧结束时的完整状态，包含游戏数据、成员状态、近期输入和缺失计数 |
| `MemberEvent` | 服务器指定在哪一帧生效的成员启用、停用或控制权变更 |
| `Generation` | 一个位置的控制权版本。旧版本输入不能控制新版本的位置 |
| `DesyncReport` | 状态校验失败时保留的确认起点、问题输入、预期摘要和实际摘要 |

`S[0]` 是开局状态；`S[100]` 是第 100 帧执行后的状态。确认点之外的预测可能改变，因此存档只取确认状态。

## 3. 客户端如何使用预测

已有不传预测配置的调用继续只执行确认帧。启用方式：

```csharp
var client = new ClientSession<MyState>(
    module, sessionId, epoch, transport, serverConnectionId,
    prediction: new PredictionOptions(maxPredictionFrames: 12),
    neutralInput: participant => gameNeutralInput(participant));

// 以下在 Running 且 !client.IsClosing 时调用；targetTick 之前待预测的本地帧也必须逐帧冻结。
// encodedInput 来自一次采样；没有操作时，明确编码成游戏定义的中性输入。
bool frozen = client.TrySubmit(targetTick, encodedInput);
// frozen == false：保存原帧号与原字节，下次重试，不重新采样覆盖。

// 即使提交失败，也继续 Poll 收发消息，给发送队列腾出空间。
// 遇到尚未冻结的活跃本地输入会停帧，不会用本地中性值偷偷预测过去。
var confirmedResults = client.Poll(targetTick);

var displayState = client.InspectPredictedState();
var confirmedState = client.InspectState();
var saveCandidate = client.CaptureCheckpoint(); // 始终是确认状态
```

这是接口示意，`MyState` 和输入编码由游戏实现。可运行例子是 `tools/UdpHarness` 和 `tools/PredictionHarness`。

宿主每轮按这个顺序运行：

1. 根据固定时钟确定本次采样到哪一帧，保留“下一帧待提交位置”，不能根据会回退的 `PredictedTick` 重新采样。
2. 为每个待提交帧确定一次输入；没有操作也生成明确的中性输入。`TrySubmit` 成功后就不可改写。
3. 队列满时暂存原帧号和原字节；后来的操作排到尚未采样的未来帧。如果重试前旧帧已确认，丢弃过期样本，不把它改号重发。
4. 调用 `Poll(target)`。框架使用本地冻结输入，预测远端输入，再计算。哪怕本地缺输入，网络收发和服务器最终帧的执行仍然继续。

例如已经确认到 100：冻结 101 的移动、102 的中性输入、103 的攻击后，可以预测到 103。请求到 104 但尚未采样 104，就停在 103；显式提交 104 的中性输入后才继续。后来读到的攻击不能改写 104，应分配到下一次尚未冻结的采样帧。

| 输入情况 | 框架处理 |
| --- | --- |
| 本地活跃，既没有冻结输入，也没有最终帧 | 停止预测，等待宿主采样或服务器最终帧 |
| 本地已明确提交中性输入 | 视为确定输入，允许预测推进 |
| 远端输入未知 | 暂猜该玩家提交了中性输入，来源暂记 `Submitted` |
| 收到服务器最终帧 | 整帧服从服务器，即使本地从未提交也可执行 |
| 本地成员未启用或已停用 | 该成员没有输入项，不因缺少它的输入阻塞 |

预测中的 `Submitted` 只是临时假设，不代表服务器已经接受。所有客户端每帧都提交中性输入时，空闲帧可以直接确认；如果服务器实际使用 `MissingSubstitute`（截止时间到了仍未收到输入），即使字节同为中性值，也要回滚校正缺失计数和输入历史。原先 `_dirtyLocalTicks` / `LocalInputAdded` 的本地补猜重算路径已删除。


- `Poll(target)` 记住最大的目标；之后调用 `Poll()` 仍可继续重算或追赶。
- 最多领先确认点 12 帧，达到窗口就暂停预测；消息接收继续。
- 一次 `Poll` 的工作预算包含计算与确认，重算可以跨多次调用。
- 分批重算期间 `IsReplaying=true`，展示状态保持完整；新输入仍然可以采集、排队。
- 检查点保留预算按最坏编码大小预检查，默认 128 MiB。编码预算不是 CLR 托管堆峰值；目前使用完整复制，尚未优化分配。
- 收到关闭通知后 `IsClosing=true`；立即停止采样提交，继续 `Poll` 接收并确认最后一批帧。等待期间 `Status` 仍可为 `Running`，不能只检查它。确认到关闭点后丢弃多余预测，展示回到最后确认状态。

`FixedInputScheduler` 用宿主传入的单调经过时间估算当前帧，默认提前 3 帧提交。它不依赖模拟是否正在回滚，也不自动测量网络延迟。收到确认进度只会向前校准；高延迟环境需要宿主配置更大的固定提前量。

逐帧采样时省略 `InputTick` 的 `lastSubmittedTick` 参数，把返回值作为采样上界。反复调用主循环时，如果时钟目标未变化，就不再冻结新帧；不要每次 `Poll` 都把目标强行加一。控制台 `CounterInputBuffer` 展示了具体写法：手动模式一个采样帧取一条整数命令，多条排队；启动或卡顿后补出的更早帧明确填中性输入。游戏的多操作编码仍可由自己的采样器一次合并后提交。

## 4. 事件怎样避免重复

游戏 `Step` 返回事件数据，不能在里面播放声音、写存档或发成就。相同事件的身份由会话、运行编号、帧号和帧内序号组成。

`Poll` 返回的 `FrameResult` 只含确认帧，每帧交付一次。原有使用方式保持不变。

启用预测后，`LastPredictionEvents` 另外提供这一轮的表现通知：

| 通知 | 表现层该怎样处理 |
| --- | --- |
| `Predicted` | 暂时显示可以撤销的效果 |
| `Invalidated` | 原预测消失或内容改变，撤销旧效果 |
| `Confirmed` | 该事件已通过最终输入和完整状态校验 |

同一个身份如果内容变了，会先撤销旧内容，再确认或显示新内容。业务的不可撤销副作用应只选择一个确认出口：`Poll` 的返回结果，或通知里的 `Confirmed`；不要把两者都当作新的业务事件执行。通知列表每次 `Poll` 都替换，使用者应及时消费。

## 5. 成员变化怎样保持一致

固定名单描述稳定身份和位置，`Checkpoint.Membership` 保存当前启用状态和控制权版本。

本帧的处理顺序是：应用 `InputFrame.MemberEvents` → 校验启用成员输入 → 调用游戏 `Step`。游戏从 `FrameContext.MemberEvents` 读到同一组变化，自己决定停用后角色如何表现。

新 UDP 房间的成员在开局状态中未启用，首个最终帧统一激活。宿主确认连接断开后调用：

```csharp
server.Deactivate(connectionId);
```

框架移除该连接的发送等待，并在下一确认帧记录停用；此前收到的该成员未来输入会被丢弃。其余成员继续。成员重绑定的核心规则已实现并测试，要求停用位置的控制权严格加一；完整重新认证、补状态和联网重连仍未实现。

`Activate` 保留既有控制者的版本，核心状态转换允许同一控制者停用后再启用；它不能用于换人。更换控制者必须使用 `Rebind` 并提高版本。当前 `ServerSession` 只在第一帧发送 `Activate`，断开后的本轮连接始终无效，尚未开放运行中重新激活或重连 API。

历史窗口可能已经丢掉更早的启用事件，所以检查点同时保留“当前成员状态”和“窗口开始前的成员状态”，使恢复、录制和重放仍能验证每一帧。

## 6. 正式存档与读档开房

`FrameSync.Persistence` 只定义存档格式、校验和存储接口；文件操作在 `FrameSync.Storage.FileSystem`。完整格式与失败保护见 [存储层说明](stage-d-storage.md)。

```csharp
// 目录由宿主创建。
var saves = new SaveService(new FileSaveStore(savePath));
server.SaveAndClose(checkpoint => saves.Save(saveId, checkpoint));

// 稍后在新进程载入；新身份由宿主产生。
var saved = saves.Load();
var initial = CheckpointRestart.Create(saved.Checkpoint, Guid.NewGuid());
var server2 = new ServerSession<MyState>(
    module, initial.Session, transport2, verifiedBindings,
    gameNeutralInput, initialCheckpoint: initial);
```

保存失败时 `SaveAndClose` 抛出异常并保持 `Saving`，服务器不再推进；修复存储后可重试同一确认点。只有保存成功才进入关闭流程。低层 `Close()` 仍用于测试或宿主明确选择直接关闭，不包含保存。

读档后保留游戏数据、稳定成员身份和累计帧数；更换会话身份，控制权版本加一，本轮帧号从 0 开始，旧输入历史清空。旧会话数据包会被拒绝。游戏永久计时应使用 `PersistentTick` 或自身保存的计时状态。

真实 UDP 宿主正常结束默认写入 `artifacts/initial-core/udp-world.fssv`，可用 `--save` 指定路径；`--load` 从正式存档开房，人数和逻辑帧率采用存档配置。保存失败时控制台保留冻结会话并定时重试，直到成功或宿主超时。

## 7. 控制台体验

在仓库根目录先构建一次，再打开三个终端，避免同时构建共享依赖：

```bash
dotnet build tools/UdpHarness -c Release
```

```bash
# 服务器
dotnet tools/UdpHarness/bin/Release/net8.0/UdpHarness.dll server --manual --frames 100000 --timeout 3600 --save artifacts/initial-core/demo.fssv

# 客户端 A：输入 5，再观察 B；输入 q 保存后关房。
dotnet tools/UdpHarness/bin/Release/net8.0/UdpHarness.dll client --slot 0 --interactive --timeout 3600

# 客户端 B
dotnet tools/UdpHarness/bin/Release/net8.0/UdpHarness.dll client --slot 1 --timeout 3600
```

重新运行服务器时增加 `--load artifacts/initial-core/demo.fssv`，然后重新启动两位客户端。此宿主演示用整数操作，框架并不知道这个玩法。

`client --leave-at 10` 可在确认第 10 帧后主动离房，用于验证其余成员继续。服务器和客户端日志会显示输入、确认帧和回滚位置。正式存档在整个房间正常结束时生成。

## 8. 按这个顺序读代码

| 顺序 | 文件 | 阅读重点 |
| --- | --- | --- |
| 1 | `Abstractions/MembershipState.cs`、`InputFrame.cs` | 不可变输入、成员事件和游戏收到的帧上下文 |
| 2 | `Runtime/SimulationRunner.cs`、`SimulationRunner.Checkpoints.cs` | 一帧如何执行，恢复为什么在新实例验证后才替换 |
| 3 | `Sessions/ServerSession.cs` | 输入截止、最终帧、停用、保存后关闭 |
| 4 | `Sessions/ClientSession.cs` | 消息入口、输入冻结、确认出口和可选预测模式 |
| 5 | `Sessions/ClientPrediction.cs` | 本地输入门槛、远端预测、找最早差异、恢复重算、事件比较 |
| 6 | `Sessions/FixedInputScheduler.cs` | 采样目标帧与模拟推进分离 |
| 7 | `Runtime/CheckpointRestart.cs` | 老存档怎样变成新会话起点 |
| 8 | `Persistence/SaveService.cs`、`Storage.FileSystem/FileSaveStore.cs` | 格式校验与磁盘提交的边界 |
| 9 | `tools/UdpHarness/UdpApp.cs`、`CounterInputBuffer.cs` | 先逐帧采样与冻结，再 Poll；发送背压、空闲输入与过期处理 |

表中的核心文件均在 `framework/src/FrameSync.*` 对应工程。测试可以按名称直接运行：

```bash
dotnet build framework/FrameSync.sln -c Release --disable-build-servers -m:1
dotnet framework/tests/FrameSync.Tests/bin/Release/net8.0/FrameSync.Tests.dll prediction
dotnet framework/tests/FrameSync.Tests/bin/Release/net8.0/FrameSync.Tests.dll restart
dotnet framework/tests/FrameSync.Persistence.Tests/bin/Release/net8.0/FrameSync.Persistence.Tests.dll
dotnet run --project tools/PredictionHarness -c Release -- --output artifacts/initial-core/prediction
```

## 9. 验证与当前边界

`scripts/verify.sh` 汇总核心测试、存储测试、两个模型的长期录制重放、故障数据报上的预测，以及真实独立进程测试。不是使用 `dotnet test`。

2026-10-15 在 macOS arm64 / .NET SDK 8.0.403 运行完整脚本，退出码为 0，构建无警告和错误：

| 验证组 | 结果 |
| --- | --- |
| 核心模拟、成员、会话、预测、重开 | 100 项通过 |
| 正式存档和文件失败保护 | 23 项通过 |
| 示例及输入/文件/命令行 | 24 项通过 |
| 内存同步控制台 | 6 项通过 |
| 独立可靠 UDP | 30 项通过 |
| Socket、真实多进程与输入采样 | 16 项通过 |
| 故障网络注入器自检 | 3 项通过 |

此外，两模型各 10 万帧恢复与新进程重放、各四客户端 1 万帧同步通过。新增预测工具对两模型分别运行 1/2/4 客户端，共 6 组含丢包、重复、乱序的场景；每个确认帧比较完整摘要、最终输入与事件，导出录制后再由独立进程重放。所有客户端每帧都明确冻结并提交输入，两个单人场景均为零回滚；四人场景最终均收敛到第 240 帧，具体回滚次数见当前报告。查看 [工具说明](../tools/PredictionHarness/README.md) 了解这些数字的含义和内存测量边界。

真实 UDP 验证包含：保存后退出全部进程，再读档并继续；累计帧从 20 到 40、数值从 15 到 30，新会话身份不同。空闲双客户端用例还验证每个本地帧都被冻结，最后 10 帧的服务器历史全部为实际提交的中性输入。另一用例中 B 第 10 帧离开，A 与服务器继续到第 40 帧，状态一致且存档记录 B 停用。关闭时序用例验证 `Close` 先于最后一批帧到达时，客户端停止采样提交、继续确认，最终关闭状态与服务器一致。本地输入冻结改造后的完整日志保存在 `artifacts/local-input-freeze/full-verification.log`，机器可读摘要在 `artifacts/local-input-freeze/verification.json`。

发生状态摘要分歧时 `ClientSession.Desync` 保留确认起点与问题帧。UDP 客户端导出 `.failure.json` 和 `.desync.fsrp`，可用重放工具复现；其他协议或连接错误会给出明确原因。常规报告包含确认帧、累计帧、摘要、回滚次数和托管内存观察。

C/D 初次实现已将检查点、录制、会话消息升级为版本 2；旧版本明确拒绝。这次本地输入冻结调整没有再次改变这些格式。正式存档外壳从版本 1 开始，内含版本 2 检查点。尚无旧存档迁移工具。

初版代码与 macOS 验证不等于商业发行验证：Windows 跨平台实测、实际游戏负载和长期联机仍需完成。运行中加入、完整掉线重连、Steam/Unity 适配、自动更换房主、自动调整提前量和快照性能优化不在本次范围。

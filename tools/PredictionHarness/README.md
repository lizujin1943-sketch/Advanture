# 预测与回滚网络验收工具

此工具连接真实的 `UdpNode → UdpTransport → ServerSession / ClientSession`，将可复现的故障注入在 `IDatagramPort` 层。握手、ACK、重传、排序、去重和流量控制仍由可靠 UDP 实现处理。

从仓库根目录运行：

```sh
dotnet build tools/PredictionHarness -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/PredictionHarness/bin/Release/net8.0/PredictionHarness.dll --output artifacts/initial-core/prediction
```

默认执行 `Counter / Grid × 1 / 2 / 4 客户端` 六个用例，分别跑 60、90、240 帧。任一检查失败，进程返回非零，控制台和 `report.json` 会记录用例、失败阶段、服务器帧与各客户端确认/预测帧。每个用例结束即写报告，后续失败不会丢失之前的结果。

## 执行规则

- 游戏固定 30Hz，网络每 5ms 推进一次逻辑时间；执行过程不睡眠，也不把机器实际耗时作为游戏输入。
- 每 5 帧由一个玩家轮流提交单条操作。Counter 混合普通加法和随机加法，Grid 轮流沿四个方向移动。所有客户端逐帧提交输入，未安排操作的帧也显式提交中性输入。
- 客户端预先冻结并提交未来 24 帧窗口内的所有本地输入，再调用预测。预测目标为当前逻辑帧加 6，不能越过本地已冻结位置；预测本身最多领先确认点 12 帧。
- 所有人完成初态恢复与 Start 后，保持服务器逻辑时间为零并预热网络 500ms，给首批输入留下传输时间。网络时钟在准备与预热阶段持续前进，UDP 超时和重传正常运行。
- 每次会话 `Poll` 最多确认或重算一帧，因此外部能够核对每一帧对应的完整检查点摘要、最终输入与事件。此设置也覆盖跨多次 `Poll` 才完成的回滚重算。
- 每个玩家的每一帧，包括中性输入，都必须按时成为服务器来源为 `Submitted` 的最终输入；迟到补齐或字节变化会明确失败，避免仅比较游戏结果而掩盖客户端没有提交输入的问题。
- 关闭后，所有客户端必须确认到目标帧，预测帧和展示帧收敛到同一确认点，不再重算，最终摘要与服务器相等。确认事件不能重复，数量必须与服务器一致；随后再正常关闭 UDP 连接。

`chaos-v1` 使用已接受数据报的固定发送序号决定故障：序号为 17 的倍数时丢包；为 11 的倍数且没有丢包时增加一个重复副本；基础延迟为 5..35ms，5 的倍数额外延迟 55ms，重复副本再延迟 5ms。规则与报文中的随机握手密钥无关。报告分别计数实际丢包、重复副本以及同一链路中不同原始数据报的乱序交付，重复副本不计入乱序数。

默认的每个用例都要求实际出现三种扰动；2 人和 4 人用例还必须出现回滚。1 人用例如果所有本地输入按时到达，允许零回滚。预测窗口达到上限会暂停继续预测，仍须继续收取确认结果。

## 输出与独立进程重放

输出目录内包含：

| 文件 | 内容 |
| --- | --- |
| `report.json` | 各端确认/预测/展示帧、冻结输入总数和中性帧数、服务器接受的中性帧数、回滚次数、事件通知、传输重发、耗时、内存观察与最终摘要 |
| `<game>-<count>-<profile>-initial.fscp` | 录制的完整初始检查点 |
| `<game>-<count>-<profile>.fsrp` | 起点与全部服务器最终输入，采用公共 `ReplayCodec` 格式 |
| `<game>-<count>-<profile>.sha256` | 最终完整检查点摘要 |

录制可直接交给已有 HeadlessHarness，在全新进程中重新计算：

```sh
dotnet build tools/HeadlessHarness -c Release --disable-build-servers -m:1 -v:quiet
dotnet tools/HeadlessHarness/bin/Release/net8.0/HeadlessHarness.dll --replay artifacts/initial-core/prediction/counter-4-chaos.fsrp --expected-hash "$(cat artifacts/initial-core/prediction/counter-4-chaos.sha256)" --report artifacts/initial-core/prediction/counter-4-replay.json
```

报告格式版本为 2，增加 `FrozenInputs`、`FrozenNeutralInputs` 与 `AcceptedNeutralInputs`；这不改变网络消息、检查点或录制的格式版本。输入语义或格式升级后，应重新生成验收录制。工具使用固定游戏身份、成员和脚本，因此相同配置在同一实现下具有稳定的最终摘要及故障计数；实际耗时和内存数字会变化。

## 调试入口和边界

```sh
# 只验证故障网络本身的固定延迟、字节所有权、确定性故障和容量限制。
dotnet tools/PredictionHarness/bin/Release/net8.0/PredictionHarness.dll --self-test

# 缩小到一个失败场景。
dotnet tools/PredictionHarness/bin/Release/net8.0/PredictionHarness.dll --game grid --clients 4 --frames 120 --output artifacts/initial-core/prediction/grid-debug

# 不注入故障、保持每个数据报固定 5ms 延迟的基线。
dotnet tools/PredictionHarness/bin/Release/net8.0/PredictionHarness.dll --network clean --output artifacts/initial-core/prediction/clean
```

帧数可配置为 30..1000，客户端仅支持 1、2、4。整个用例最多运行 60 秒模拟网络时间，单次握手、初态准备和会话关闭分别有 10 秒模拟预算，UDP 关闭有 3 秒预算。数据报不超过 1200 字节；网络总队列最多 4096 份，已到达但尚未被节点读取的报文也计入容量。用例要求没有触及这一容量背压。所有循环都有时间或有限帧数边界。

`ManagedAllocatedBytesDuringRun` 是用例 `Run` 期间整个进程累计托管分配量的增量，包含验收检查、状态编码和录制输出，排除用例对象构造及总报告 JSON 编码。`MaximumSampledManagedHeapBytes` 是每轮宿主循环调用 `GC.GetTotalMemory(false)` 观察到的绝对最大值；同一进程顺序运行多个用例，前案尚未回收的数据可能仍计入。它不是 RSS，也不保证捕获真正的瞬时堆峰值。工具没有为了美化结果而强制垃圾回收。

这是小型示例和逻辑时间驱动下的正确性验收。实际耗时不能直接用作真实游戏的性能预算；真实 Socket、操作系统调度、跨机器时钟、场景规模和引擎表现层由其他验证入口覆盖。此目录不修改核心协议、共享验证脚本或解决方案。

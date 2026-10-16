# 联机自动诊断：测试后怎样判断是否不同步

每次创建或加入房间都会自动记录，无需按 F3。记录器不修改模拟状态、输入或回滚规则。普通试玩和自动联机验收使用同一套记录流程。

## 日志在哪里

从仓库根目录运行：

```sh
python3 scripts/run-yard-pair.py
```

启动时会打印本次目录，格式为 `artifacts/tactical-network/manual-日期-时间-随机编号/`。每次都会新建目录，之前的 `manual/` 历史日志也不会被覆盖。目录中有 `host.log`、`join.log`，以及 `diagnostics/`。

`diagnostics/` 下有 `server/`、`client-A/`、`client-B/`。每个角色内部按每次进入房间建立独立子目录；即使在同一窗口离开后重新加入，也不会覆盖上一局。对照文件中的 `sessionId` 和 `epoch`，可以判断记录是否属于同一局。

在 Unity 编辑器或直接双击仓库内的试玩程序时，诊断默认写到仓库的 `artifacts/tactical-network/diagnostics/`。完整路径同时输出到 Unity 程序日志，标记为 `YARD_DIAGNOSTICS`。启动参数 `-yard-diagnostics <目录>` 可以指定路径。程序发布到仓库以外时，默认使用应用的可写数据目录。

下次测试后，保留整份本次目录，直接告诉我“检查刚才的联机日志”即可。不会将这些诊断文件上传到服务器。

## 先看 summary.json

| 字段 | 含义 |
| --- | --- |
| `role` | 服务器、客户端 A 或客户端 B |
| `sessionId` / `epoch` | 房间会话身份与本次运行编号 |
| `sessionStatus` | 最后观察到的核心状态；尚未建立会话时是 `Connecting` |
| `confirmedTick` | 最后完成的确认帧。客户端到这一帧为止已逐帧通过服务器摘要校验 |
| `confirmedHash` | 这一帧的完整状态摘要；没有收到初态时为 `null` |
| `predictedTick` | 客户端提前推算到的位置，不一定已得到服务器确认 |
| `rollbackCount` / `lastRestoreTick` | 回滚总次数，以及最近一次恢复到的帧号 |
| `finished` | 是否成功写入明确的结束记录 |
| `endState` / `reason` | 首次结束方式与原因，后续清理不会覆盖它 |
| `desyncDetected` / `problemTick` | 是否发现真实状态分歧，以及对应的问题帧 |

常见的 `endState`：

- `ApplicationQuit`：正常关闭程序。
- `LeaveRoom`：点击离开房间，或切换房间前结束旧连接。
- `SceneDestroyed`：场景被关闭，包括停止编辑器试玩。
- `SessionClosed`：收到框架正常关闭通知，已排空应处理的最终帧。
- `ConnectionFailed`：会话建立前握手失败，例如地图不一致。
- `TransportFailure`：已经建立会话后连接中断。
- `SessionFailed`：核心执行或协议处理失败，具体原因见 `reason`。
- `RuntimeException`：试玩驱动捕获到异常，`reason` 包含异常堆栈。
- `Desync`：同一个最终输入帧执行后，本机完整状态摘要与服务器不同。

**回滚次数增加并不代表不同步。** 客户端猜测其他玩家输入，真实输入到达后修正，这是正常机制。应关注 `desyncDetected` 和 `Desync`。

不同窗口通常不是同一时刻关闭，结束帧可能不同。只有 `sessionId`、`epoch`、`confirmedTick` 都相同，才能直接比较 `confirmedHash`。帧号不同不能仅凭摘要不同就判定出错。

`finished: false` 表示运行中，或退出记录缺失。例如进程被强制结束、系统崩溃、磁盘写入失败。它不能作为本局通过的证明。

## 每秒记录与快照

- `progress.jsonl`：每行一份 JSON。启动、首次观察、状态变化、正常结束立即记录，运行中每秒记录一次；包括确认帧、预测帧、回滚次数、确认摘要和时间。
- `summary.json`：最新一份记录，退出后保留结束状态。使用同目录临时文件原子替换，避免写一半的摘要覆盖上一份有效数据。
- `latest.checkpoint`：最近一次记录时的完整确认快照，可恢复；包括游戏状态、近期输入、成员信息和初始化数据。
- `final.checkpoint`：结束时的最新确认快照。没有收到初态就失败时，不会生成空的假快照。

周期日志是采样进度，不是逐帧日志或整局录像。核心内部依然检查每一个最终帧。强制结束前最后一小段信息可能没有来得及写入。

如果诊断写入失败，界面显示“诊断日志保存失败”，程序日志记录 `YARD_DIAGNOSTICS_WRITE_FAILED`。停止重复写入以免每帧异常，不影响游戏同步继续运行。

## 发生真正不同步时

出错客户端会立即建立 `desync/`，不等待下一秒，也不等待关闭程序：

| 文件 | 用途 |
| --- | --- |
| `report.json` | 问题帧、最后正确帧、服务器预期摘要和本机实际摘要 |
| `frame.json` | 问题帧完整输入：玩家在房间中的编号、控制权版本、真实提交/默认补齐标记、原始输入，以及成员事件 |
| `last-confirmed.checkpoint` | 问题帧之前最后一次校验通过的完整状态 |
| `candidate.checkpoint` | 本机执行问题帧后产生的状态；**未通过校验，不是有效游戏存档** |
| `problem.fsrp` | 包含最后正确起点及问题帧输入的标准重放文件 |

`frame.json` 的 `payloadBase64` 是输入原始字节的 Base64 编码；通用框架不解释移动、技能或其他游戏指令，具体含义由游戏输入编码器决定。

服务器当前协议发送摘要，不发送每帧完整状态，所以客户端报告包含服务器**预期摘要**，以及本机**实际快照**，不把本机候选状态冒充服务器快照。使用同版本的正常游戏模块重放 `problem.fsrp`，可以重新计算预期结果并进行比较。

重放时，用 `ReplayCodec.Decode` 读取文件，按 `recording.Initial` 中的会话、容量和初始化数据创建对应游戏的 `SimulationRunner`，然后调用 `ReplayPlayer.Play`。需要保留产生问题的游戏版本；框架的 Counter/Grid 命令行工具不能直接解释庭院游戏的输入。

## 代码放在哪里

- `framework/src/FrameSync.Diagnostics/SessionDiagnostics.cs`：独立的可选文件诊断模块，观察通用客户端/服务器会话，记录进度并导出分歧证据。
- `framework/src/FrameSync.Diagnostics/DiagnosticRecords.cs`：JSON 文件字段，不包含游戏类型。
- `samples/TacticalGame.Unity/Assets/TacticalGame/Network/YardDiagnostics.cs`：把试玩房间的断线/握手失败接到记录器，分别处理服务器和客户端。
- `NetworkYardController.cs`：在 Poll 后、失败提前返回前、释放会话前记录，并提供默认目录。
- `scripts/run-yard-pair.py`：为每次测试建立独立目录，自动验收也检查这套普通诊断的结束状态和摘要。

引用方向是 `FrameSync.Diagnostics → FrameSync.Sessions → Runtime`。核心会话不依赖文件记录器；新游戏可以独立选择是否启用记录器。模块是 .NET Standard 2.1，不引用 Unity、Steam 或庭院玩法。

# SDD ledger — plan: docs/superpowers/plans/2026-10-13-stage-e-implementation.md

BASE: 08bb6bf9ce14e5bc31ce77b356931d3030d74b0e。A/B 原有工作树全部保留，不自动提交或推送。

Pre-flight: Core→Adapter 仅传字节和连接句柄；Adapter→Sessions 复用 ITransport；Harness→Sessions 使用现有确认帧与检查点；C/D 不是传输依赖。

Ruling: 按用户指定路径直接实施，不创建另一个工作目录；测试产物全部留在 artifacts/stage-e。
Ruling: E 集成继续采用四个可靠有序通道；B 尚无输入冗余与补帧机制，改为不可靠会破坏语义；以后切换必须同时升级会话协议。
Ruling: 首个线协议实现每包 0 或 1 条记录，严格拒绝其他数量，通道参数要求精确匹配；减少批量接收的半提交问题，代价是额外报文开销和暂不协商降级。
Ruling: 传输释放以 TryReceive 将不可变消息所有权转移给宿主为界；消息回执对象报告成功/失败，避免无限累计发送完成事件。

Task 1: in progress.

Task 1: complete — Codec 三个测试从 0/3 到 3/3；标准 CRC32C 向量、固定端序、截断/随机字节拒绝已验证。
Task 2: in progress.

Task 2 diagnostic: 流控测试失败由测试 Until 对有副作用条件多调用一次造成，重复提交第三条消息；改为缓存条件结果。协议其余 13 项已通过。

Task 3 diagnostic: macOS Socket.DontFragment 抛 OperationNotSupported，堆栈指向选项设置；Socket.RealDatagrams 和多进程测试均已观察失败。现在仅捕获不支持该选项的错误，公开 DontFragmentEnabled，仍严格限制 1200 字节。参见 https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.dontfragment 。

Task 2 diagnostic: Boundary.LateAckOfLostCopyCompletesReceipt 红灯；超时删除在途记录后原副本 ACK 无法关联片。加入最多 256 项失包→分片引用，迟到确认完成片但不二次扣除在途字节，仍保持有界历史。
Task 3: real sockets and 2/4-client processes passed on macOS; client B log explicitly observed A +5 and all checkpoint hashes matched.

Task 2: complete — 独立 UDP 测试 26/26，通过 9 组 RTT/丢包/带宽变化矩阵；迟到 ACK 已红→绿。
Task 3: implementation complete — Socket、ITransport 和多进程宿主已完成；新增手动标准输入场景纳入全量验收。
Task 4: in progress — 文档和总验证入口已更新；全量回归及最终独立审查待执行。

Task 3: complete — 6/6 真实集成测试通过；手工输入经过独立进程 A→server→B，最终总值 5，三端检查点一致。
Task 4 implementation: complete — ./scripts/verify.sh 通过：70 核心 + 13 A 工具 + 6 B 工具 + 26 UDP + 6 UDP 集成 = 121 项；A 双模型各 100000 帧及重放、B 双模型四客户端各 10000 帧全部通过。XML 注释构建 0 警告。待最终独立审查。


Final review: completed once by fresh independent reviewer (gpt-6-astra/high). Result: With fixes; 0 Critical, 3 Important, 0 Minor. No second review requested.
- Important 1: 可靠消息先耗尽预算导致不可靠消息饥饿。`Liveness.MixedChannelsWithOnePacketBudget` 先失败；改为每通道独立队列 + 所有模式共享轮转，大包等待预算时保留位置；修复后通过。另覆盖默认预算下不同大小报文。
- Important 2: 前两次 Echo 丢失后 Cookie 过期，握手无法恢复。`Liveness.HandshakeRecoversAfterCookieExpires` 先失败；服务器验证 HMAC 后对过期凭证重发更小的挑战，不分配连接、不重置总握手期限；修复后通过。
- Important 3: 宿主每轮限量消费时第一个连接独占读取。`Liveness.BoundedConsumerRotatesConnections` 先失败；按上次交付的连接句柄循环选择，避免字典顺序依赖和临时数组分配；修复后通过。
- Regression evidence: `artifacts/stage-e/review-red.log`（1 passed / 3 failed）→ `review-green.log`（4 passed / 0 failed）；并非声称四项都曾失败。

Ruling: Cookie 的过期恢复由服务器验证自身时钟和签名后重新挑战；不让客户端直接比较不同机器的单调时间。原 10 秒握手总期限不变。
Task 4 verification after review fixes: `./scripts/verify.sh` exit 0; 70 + 13 + 6 + 30 + 6 = 125 tests. A 双模型各 100000 帧及恢复重放、B 双模型四客户端各 10000 帧继续通过。完整日志已覆盖更新为修复后版本。

Final: fixed mixed-channel starvation — Liveness.MixedChannelsWithOnePacketBudget RED→GREEN, full suite 125/125.
Final: fixed expired-cookie handshake — Liveness.HandshakeRecoversAfterCookieExpires RED→GREEN, full suite 125/125.
Final: fixed receive starvation across connections — Liveness.BoundedConsumerRotatesConnections RED→GREEN, full suite 125/125.
Final: no deferred minor findings. Cross-platform/LAN/public security/multi-hour validation remain explicitly outside current verified scope.
Task 4: complete — 修复后 5000 帧 / 五进程 / 每端 1000 条输入逐项一致，总值 3008，hash=aa18833f8501773a9251c16dc68907a8de5659ce98191e2fcd12e89e2325e095；XML 注释构建 0 警告/0 错误；git diff --check 通过。C/D 仍暂缓，未提交或推送。

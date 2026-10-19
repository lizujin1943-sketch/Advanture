using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Protocol.Protobuf;
using FrameSync.Transport;

namespace FrameSync.Sessions
{
    /// <summary>帧同步客户端；默认只执行确认帧，可选择启用有界预测和完整状态回滚。</summary>
    /// <typeparam name="TState">游戏定义的状态类型。</typeparam>
    /// <remarks>所有方法必须串行调用。输入提交只冻结字节；推进、预测与重算均由 Poll 驱动。</remarks>
    public sealed class ClientSession<TState>
    {
        private readonly SimulationModule<TState> _module;
        private readonly Guid _sessionId;
        private readonly ulong _epoch, _serverConnectionId;
        private readonly ITransport _transport;
        private readonly RuntimeLimits _receiveLimits;
        private readonly SessionOptions _options;
        private readonly Outbox _outbox;
        // 成功提交后不可改写的本地帧输入；显式中性输入也占一项，确认后释放。
        private readonly Dictionary<ulong, ByteString> _localInputs = new Dictionary<ulong, ByteString>();
        private readonly Dictionary<ulong, BufferedFrame> _frames = new Dictionary<ulong, BufferedFrame>();
        private readonly Dictionary<ulong, FinalFrame> _recent = new Dictionary<ulong, FinalFrame>();
        private readonly Queue<ulong> _recentOrder = new Queue<ulong>();
        private SimulationRunner<TState>? _runner;
        private Checkpoint? _confirmed;
        private Participant? _participant;
        private byte[]? _snapshot;
        private int _receivedBytes;
        private ushort _assignedSlot;
        private string? _initialHash;
        private ulong? _closeAt;
        private ulong _lastProgressQueued;
        private bool _busy;
        private readonly PredictionOptions? _predictionOptions;
        private readonly Func<Participant, ByteString>? _neutralInput;
        private ClientPrediction<TState>? _prediction;
        private ulong _predictionTarget;
        private DesyncReport? _desync;

        /// <summary>创建等待初始检查点的客户端，尚未创建游戏实例。</summary>
        /// <param name="module">与服务器兼容的游戏模块。</param>
        /// <param name="sessionId">宿主已确认的会话身份，拒收其他会话消息。</param>
        /// <param name="epoch">本次运行编号，隔离旧运行消息。</param>
        /// <param name="transport">客户端独占的字节传输端点。</param>
        /// <param name="serverConnectionId">宿主已经验证的服务器连接编号。</param>
        /// <param name="receiveLimits">允许接收的检查点/游戏输入容量上限。</param>
        /// <param name="options">消息缓存和单次工作量限制。</param>
        /// <param name="prediction">可选预测窗口与检查点预算；省略时只执行确认帧。</param>
        /// <param name="neutralInput">用于预测远端玩家的游戏中性输入；不会替宿主补齐本地未采样帧。</param>
        public ClientSession(SimulationModule<TState> module, Guid sessionId, ulong epoch, ITransport transport,
            ulong serverConnectionId, RuntimeLimits? receiveLimits = null, SessionOptions? options = null,
            PredictionOptions? prediction = null, Func<Participant, ByteString>? neutralInput = null)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            if (sessionId == Guid.Empty) throw new ArgumentException("Missing session identity.");
            if (epoch == 0 || serverConnectionId == 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            _sessionId = sessionId; _epoch = epoch; _serverConnectionId = serverConnectionId;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _receiveLimits = receiveLimits ?? new RuntimeLimits(); _options = options ?? new SessionOptions();
            _options.Validate(transport, _receiveLimits, 1); _outbox = new Outbox(_options);
            _predictionOptions = prediction; _neutralInput = neutralInput;
            if (prediction != null)
            {
                if (neutralInput == null) throw new ArgumentNullException(nameof(neutralInput));
                prediction.Validate(_receiveLimits, _options);
            }
        }

        /// <summary>当前生命周期；Ready 仍须等待服务器 Start。</summary>
        public SessionStatus Status { get; private set; } = SessionStatus.Preparing;
        /// <summary>已收到服务器关闭通知；此时停止提交输入，继续 Poll 直到剩余最终帧完成确认。</summary>
        /// <remarks>Close 与 Final 分属不同通道，关闭通知可能先到；等待最后一帧时 Status 仍可为 Running。</remarks>
        public bool IsClosing => _closeAt.HasValue;
        /// <summary>已经执行且与服务器完整状态摘要一致的最后一帧。</summary>
        public ulong ConfirmedTick => _confirmed?.Tick ?? 0;
        /// <summary>服务器分配给此连接的参与者；初态恢复前为空。</summary>
        public Participant? LocalParticipant => _participant == null ? null : _confirmed!.Membership.Find(_participant.Slot);
        /// <summary>当前计算到的预测帧；重算过程中可能临时倒退。</summary>
        public ulong PredictedTick => _prediction?.PredictedTick ?? ConfirmedTick;
        /// <summary>展示快照的帧号；分批重算完成前保持上一份完整结果。</summary>
        public ulong PresentedTick => _prediction?.Published.Tick ?? ConfirmedTick;
        /// <summary>是否正在分多次 Poll 重算；此时仍能收发消息和提交新输入。</summary>
        public bool IsReplaying => _prediction?.IsReplaying ?? false;
        /// <summary>实际发生的回滚次数。</summary>
        public ulong RollbackCount => _prediction?.RollbackCount ?? 0;
        /// <summary>最近一次回滚恢复的状态帧号，例如第 100 帧有差异时为 99。</summary>
        public ulong LastRestoreTick => _prediction?.LastRestoreTick ?? 0;
        /// <summary>状态摘要不一致时保留的首个问题帧和重放起点。</summary>
        public DesyncReport? Desync => _prediction?.Desync ?? _desync;
        /// <summary>尚未执行确认的最终帧数量。</summary>
        public int BufferedFinalFrameCount => _frames.Count;
        /// <summary>尚未释放的本地输入数量。</summary>
        public int PendingLocalInputCount => _localInputs.Count;
        /// <summary>本次 Poll 的预测、撤销和确认通知；每次调用替换，不建立无界事件队列。</summary>
        public IReadOnlyList<PredictionEvent> LastPredictionEvents { get; private set; } = Array.Empty<PredictionEvent>();
        /// <summary>最近执行成功的完整确认输入帧，可用于诊断其他玩家的输入。</summary>
        public InputFrame? LastFrame { get; private set; }
        /// <summary>协议、模拟或一致性检查失败的原因。</summary>
        public string? Failure { get; private set; }
        /// <summary>忽略的非服务器或旧会话消息数量。</summary>
        public ulong RejectedMessages { get; private set; }

        /// <summary>为明确帧号冻结并排队输入；true 只表示已进入本地队列，不表示服务器接受。</summary>
        /// <param name="tick">目标帧，必须大于最后确认帧且位于未来窗口内。</param>
        /// <param name="input">游戏的标准输入字节；本方法持有副本。</param>
        /// <returns>队列容量不足时返回 false，此帧尚未冻结，调用方应保留帧号和原输入稍后重试。</returns>
        /// <remarks>
        /// 仅在 Running 且 IsClosing 为 false 时采样提交；关闭中仍须继续 Poll 排空最终帧。
        /// 预测前先逐帧调用本方法，包括没有操作的帧也提交游戏定义的中性输入。
        /// true 表示输入已冻结；同帧同内容重试会去重，改写会抛异常。后来采到的操作分配到尚未冻结的未来帧。
        /// 如果重试前该帧已确认，应丢弃过期输入，不能偷偷换帧重发；最终结果以服务器为准。
        /// </remarks>
        public bool TrySubmit(ulong tick, ReadOnlySpan<byte> input)
        {
            EnsureNotBusy();
            if (Status != SessionStatus.Running || _participant == null || _runner == null || _closeAt.HasValue)
                throw new InvalidOperationException("Client must be running to submit input.");
            if (tick <= ConfirmedTick || tick - ConfirmedTick > (ulong)_options.MaxFutureFrames)
                throw new ArgumentOutOfRangeException(nameof(tick));
            if (input.Length > _runner.Limits.MaxInputBytes) throw new ArgumentOutOfRangeException(nameof(input));
            _busy = true;
            try
            {
                _module.InputValidator.ValidateCanonical(input);
                if (_localInputs.TryGetValue(tick, out var existing))
                {
                    if (!existing.Span.SequenceEqual(input)) throw new InvalidOperationException("Already submitted different input for this tick.");
                    return true;
                }
                var member = LocalParticipant!;
                if (!member.IsActive && ConfirmedTick != 0) throw new InvalidOperationException("Local member is inactive.");
                var value = new RecordedInput(member.Slot, member.Generation, input);
                byte[] bytes = Wire.Input(_sessionId, _epoch, tick, value, _options.MaxRealtimeBytes);
                if (!_outbox.CanAdd(1, bytes.Length)) return false;
                _outbox.Add(_serverConnectionId, MessageKind.Input, bytes); _localInputs.Add(tick, value.Payload); return true;
            }
            finally { _busy = false; }
        }

        /// <summary>驱动有限次数的收发和连续帧执行，返回本次确认帧事件。</summary>
        /// <returns>只有完成状态核对的帧才出现在结果中；宿主应在方法返回后处理事件。</returns>
        public IReadOnlyList<FrameResult> Poll() => PollCore();

        /// <summary>请求预测到目标逻辑帧；遇到未冻结的活跃本地输入就停止，另外受预测窗口和单次预算限制。</summary>
        /// <remarks>框架不读取系统时间。目标应由宿主固定帧时钟提供；可远大于窗口，届时只暂停预测。</remarks>
        public IReadOnlyList<FrameResult> Poll(ulong predictionTarget)
        {
            EnsureNotBusy();
            if (_predictionOptions == null) throw new InvalidOperationException("Prediction is not enabled.");
            _predictionTarget = Math.Max(_predictionTarget, predictionTarget);
            return PollCore();
        }
        private IReadOnlyList<FrameResult> PollCore()
        {
            EnsureNotBusy(); var results = new List<FrameResult>();
            var predictionEvents = new List<PredictionEvent>();
            LastPredictionEvents = predictionEvents.AsReadOnly();
            if (Status == SessionStatus.Failed || Status == SessionStatus.Closed) return results.AsReadOnly();
            _busy = true;
            try
            {
                int sendBudget = _options.MaxMessagesPerPoll;
                _outbox.Flush(_transport, ref sendBudget);
                for (int i = 0; i < _options.MaxMessagesPerPoll && _transport.TryReceive(out var message); i++)
                {
                    Receive(message!);
                    // Close 可能先于其他通道的旧消息到达。终态一旦生效就停止处理，
                    // 与“旧消息在下一次 Poll 才到达”的入口行为一致，不把正常关闭改成失败。
                    if (Status == SessionStatus.Closed) break;
                }
                if (Status == SessionStatus.Running)
                {
                    if (_prediction != null)
                    {
                        _prediction.Poll(_predictionTarget, _closeAt, _frames, _localInputs, _options.MaxFramesPerPoll,
                            (checkpoint, frame, result) => { CommitConfirmation(checkpoint, frame); results.Add(result); }, predictionEvents);
                    }
                    else for (int i = 0; i < _options.MaxFramesPerPoll && _frames.TryGetValue(checked(ConfirmedTick + 1), out var next); i++)
                    {
                        if (_closeAt.HasValue && next.Record.Frame.Tick > _closeAt.Value) throw new InvalidDataException("Frame exceeds closing tick.");
                        FrameResult result = _runner!.Advance(next.Record.Frame);
                        Checkpoint checkpoint = _runner.CaptureCheckpoint();
                        string actualHash = CheckpointCodec.Hash(checkpoint);
                        if (actualHash != next.Record.Hash)
                        {
                            _desync = new DesyncReport(_confirmed!, next.Record.Frame, checkpoint, next.Record.Hash, actualHash);
                            throw new InvalidDataException("State hash mismatch at tick " + checkpoint.Tick + ".");
                        }
                        // 计算和摘要检查均成功后，才能把帧暴露为已确认并交付事件。
                        CommitConfirmation(checkpoint, next.Record.Frame);
                        results.Add(result);
                    }
                    if (_closeAt == ConfirmedTick) Status = SessionStatus.Closed;
                }
                if (Status == SessionStatus.Closed && _prediction != null)
                    _prediction.DiscardTail(predictionEvents);
                // 只反馈真正执行且摘要一致的进度；每次 Poll 合并为一条，不逐帧堆积 ACK。
                if (ConfirmedTick > _lastProgressQueued)
                {
                    byte[] progress = Wire.Terminal(MessageKind.Progress, _sessionId, _epoch, ConfirmedTick);
                    if (_outbox.CanAdd(1, progress.Length))
                    { _outbox.Add(_serverConnectionId, MessageKind.Progress, progress); _lastProgressQueued = ConfirmedTick; }
                }
                _outbox.Flush(_transport, ref sendBudget);
            }
            catch (Exception error) { Status = SessionStatus.Failed; Failure = error.Message; }
            finally { _busy = false; }
            return new ReadOnlyCollection<FrameResult>(results);
        }

        /// <summary>取得最后确认的完整检查点；尚未恢复初态时抛异常。</summary>
        public Checkpoint CaptureCheckpoint()
        { EnsureNotBusy(); return _confirmed ?? throw new InvalidOperationException("Initial state has not been restored."); }
        /// <summary>返回确认状态的独立副本，不暴露尚未核对或已出错的模拟状态。</summary>
        public TState InspectState() { return _module.Codec.Decode(CaptureCheckpoint().ApplicationState.Span); }

        /// <summary>返回可显示的预测状态副本；部分重算不会暴露尚未追赶完成的状态。</summary>
        public TState InspectPredictedState()
        {
            EnsureNotBusy();
            var checkpoint = _prediction?.Published ?? CaptureCheckpoint();
            return _module.Codec.Decode(checkpoint.ApplicationState.Span);
        }

        // 两种执行模式共用确认出口，保证历史去重、输入释放和事件交付规则一致。
        private void CommitConfirmation(Checkpoint checkpoint, InputFrame frame)
        {
            var buffered = _frames[checkpoint.Tick];
            _confirmed = checkpoint; LastFrame = frame;
            _frames.Remove(checkpoint.Tick); _localInputs.Remove(checkpoint.Tick);
            _recent.Add(checkpoint.Tick, buffered.Record); _recentOrder.Enqueue(checkpoint.Tick);
            if (_recentOrder.Count > _options.MaxFutureFrames) _recent.Remove(_recentOrder.Dequeue());
        }

        private void Receive(TransportMessage message)
        {
            if (message.ConnectionId != _serverConnectionId) { RejectedMessages++; return; }
            if (message.Payload.Length > Math.Max(_options.MaxRealtimeBytes, Wire.MaxSnapshotMessageBytes(_options.SnapshotChunkBytes)))
                throw new InvalidDataException("Server message too large.");
            var packet = Wire.Decode(message.Payload, Math.Max(_options.MaxRealtimeBytes, Wire.MaxSnapshotMessageBytes(_options.SnapshotChunkBytes)),
                out var kind, out var id, out var epoch);
            if (id != _sessionId || epoch != _epoch) { RejectedMessages++; return; }
            if (message.Channel != Wire.Channel(kind) || message.Delivery != DeliveryMode.ReliableOrdered)
                throw new InvalidDataException("Unexpected channel or delivery mode.");
            if (kind != MessageKind.Snapshot && message.Payload.Length > _options.MaxRealtimeBytes)
                throw new InvalidDataException("Realtime message too large.");
            switch (kind)
            {
                case MessageKind.Snapshot: ReceiveSnapshot(packet.Snapshot); break;
                case MessageKind.Start:
                    string hash = Wire.ReadHash(packet.Start.InitialHash);
                    if ((Status != SessionStatus.Ready && Status != SessionStatus.Running) || hash != _initialHash)
                        throw new InvalidDataException("Start without matching restored initial state.");
                    Status = SessionStatus.Running; break;
                case MessageKind.Final:
                    if (_runner == null || (Status != SessionStatus.Ready && Status != SessionStatus.Running)) throw new InvalidDataException("Final frame before initial state.");
                    FinalFrame record = Wire.ReadFinal(packet.Final, id, epoch, _runner.Limits);
                    BufferFrame(record); break;
                case MessageKind.Close:
                    ulong closeTick = packet.Close.Tick;
                    if (closeTick < ConfirmedTick || closeTick - ConfirmedTick > (ulong)_options.MaxFutureFrames ||
                        (_closeAt.HasValue && _closeAt.Value != closeTick)) throw new InvalidDataException("Invalid closing tick.");
                    _closeAt = closeTick;
                    if (closeTick == ConfirmedTick) Status = SessionStatus.Closed;
                    break;
                case MessageKind.Fault:
                    throw new InvalidDataException("Server simulation or session failed.");
                default: throw new InvalidDataException("Unexpected server message.");
            }
        }

        private void ReceiveSnapshot(SnapshotMessage snapshot)
        {
            if (Status != SessionStatus.Preparing) throw new InvalidDataException("Initial snapshot has already been restored.");
            // 声明的总长、偏移和分块大小先验证，再分配完整检查点，避免整数截断和超量分配。
            if (snapshot.TotalLength > (uint)_receiveLimits.MaxCheckpointBytes || snapshot.Offset > snapshot.TotalLength ||
                snapshot.Data.Length > _options.SnapshotChunkBytes) throw new InvalidDataException("Snapshot size exceeds capacity.");
            ushort slot = Wire.ReadSlot(snapshot.Slot); int total = (int)snapshot.TotalLength;
            int offset = (int)snapshot.Offset; ReadOnlySpan<byte> bytes = snapshot.Data.Span;
            if (total == 0 || bytes.Length == 0 || bytes.Length > total - offset) throw new InvalidDataException("Invalid snapshot chunk range.");
            if (_snapshot == null)
            {
                if (offset != 0) throw new InvalidDataException("Initial snapshot must start at offset zero.");
                _snapshot = new byte[total]; _assignedSlot = slot;
            }
            if (_snapshot.Length != total || slot != _assignedSlot) throw new InvalidDataException("Snapshot metadata changed during transfer.");
            if (offset < _receivedBytes)
            {
                if (bytes.Length > _receivedBytes - offset || !_snapshot.AsSpan(offset, bytes.Length).SequenceEqual(bytes))
                    throw new InvalidDataException("Conflicting duplicate snapshot chunk.");
                return;
            }
            if (offset != _receivedBytes) throw new InvalidDataException("Out of order snapshot chunk on ordered channel.");
            bytes.CopyTo(_snapshot.AsSpan(offset)); _receivedBytes += bytes.Length;
            if (_receivedBytes != total) return;
            Checkpoint checkpoint = CheckpointCodec.Decode(_snapshot, _receiveLimits);
            if (checkpoint.Session.SessionId != _sessionId || checkpoint.Session.Epoch != _epoch || checkpoint.Tick != 0 ||
                !checkpoint.Manifest.Equals(_module.Manifest)) throw new InvalidDataException("Incompatible initial checkpoint.");
            _options.Validate(_transport, checkpoint.Limits, checkpoint.Session.Participants.Count);
            foreach (var p in checkpoint.Session.Participants) if (p.Slot == _assignedSlot) _participant = p;
            if (_participant == null) throw new InvalidDataException("Assigned slot missing from the roster.");
            var runner = new SimulationRunner<TState>(_module, checkpoint.Session, checkpoint.Limits, checkpoint.InitializationData);
            runner.Restore(checkpoint); // 在独立实例恢复并验证成功后，才把它作为客户端当前世界。
            _initialHash = CheckpointCodec.Hash(checkpoint);
            byte[] ready = Wire.Ready(_sessionId, _epoch, _initialHash, _options.MaxFutureFrames);
            if (!_outbox.CanAdd(1, ready.Length)) throw new InvalidOperationException("No room for Ready message.");
            _runner = runner; _confirmed = checkpoint; _snapshot = null;
            if (_predictionOptions != null)
            {
                _predictionOptions.Validate(checkpoint.Limits, _options);
                _prediction = new ClientPrediction<TState>(runner, checkpoint, _predictionOptions, _assignedSlot, _neutralInput!, _module.InputValidator);
            }
            Status = SessionStatus.Ready;
            _outbox.Add(_serverConnectionId, MessageKind.Ready, ready);
        }

        private void BufferFrame(FinalFrame record)
        {
            ulong tick = record.Frame.Tick;
            if (tick <= ConfirmedTick)
            {
                if (_recent.TryGetValue(tick, out var old) && !old.SameAs(record)) throw new InvalidDataException("Conflicting confirmed frame.");
                return; // 早于保留窗口的旧帧不会再次计算。
            }
            if (tick - ConfirmedTick > (ulong)_options.MaxFutureFrames) throw new InvalidDataException("Confirmed frame exceeds receive window.");
            if (_frames.TryGetValue(tick, out var existing))
            { if (!existing.Record.SameAs(record)) throw new InvalidDataException("Conflicting buffered frame."); return; }
            _frames.Add(tick, new BufferedFrame(record));
        }
        private void EnsureNotBusy() { if (_busy) throw new InvalidOperationException("Reentrant session calls are not allowed."); }
    }
}

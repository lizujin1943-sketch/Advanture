using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Transport;

namespace FrameSync.Sessions
{
    /// <summary>固定名单的帧同步服务器：发初态、收输入、按时限组帧、执行并广播确认结果。</summary>
    /// <typeparam name="TState">游戏自己定义的完整状态。</typeparam>
    /// <remarks>
    /// 宿主负责验证连接并提供绑定。所有方法由同一线程串行调用；不创建线程，不读取真实时间。
    /// 服务器也持有独立模拟。阶段 B 不预测输入，缺失时使用游戏提供的中性输入。
    /// </remarks>
    public sealed partial class ServerSession<TState>
    {
        private readonly SimulationModule<TState> _module;
        private readonly SimulationRunner<TState> _runner;
        private readonly ITransport _transport;
        private readonly SessionOptions _options;
        private readonly Outbox _outbox;
        private readonly Peer[] _peers;
        private readonly Dictionary<ulong, Peer> _connections = new Dictionary<ulong, Peer>();
        private readonly Dictionary<ulong, Dictionary<ushort, RecordedInput>> _pending = new Dictionary<ulong, Dictionary<ushort, RecordedInput>>();
        private readonly RecordedInput[] _neutral;
        private byte[] _initialBytes;
        private readonly string _initialHash;
        private Checkpoint _confirmed;
        private long _prepareAt, _startedAt, _lastTime;
        private bool _hasTime, _busy;

        /// <summary>创建服务器和独立游戏实例，冻结初始检查点；尚不推进任何帧。</summary>
        /// <param name="module">游戏的确定性计算模块。</param>
        /// <param name="session">会话身份、帧率和固定名单。</param>
        /// <param name="transport">服务器拥有的字节传输端点。</param>
        /// <param name="peers">经过宿主验证的连接绑定；默认恰好覆盖全部名单，显式允许预留时仍须至少一人。</param>
        /// <param name="neutralInput">返回玩家未提交时使用的中性输入；构造时校验并复制一次。</param>
        /// <param name="limits">模拟和检查点容量限制。</param>
        /// <param name="initialization">分发给全部模拟实例的游戏初始化参数。</param>
        /// <param name="options">通信容量、单次工作量和准备超时配置。</param>
        /// <param name="initialCheckpoint">可选的读档开局状态，必须已经转换为本会话的第零帧。</param>
        /// <param name="allowUnboundParticipants">显式允许初始非活跃席位没有连接，供运行期 TryBind 接入。</param>
        public ServerSession(SimulationModule<TState> module, SessionDescriptor session, ITransport transport,
            IReadOnlyList<PeerBinding> peers, Func<Participant, ByteString> neutralInput,
            RuntimeLimits? limits = null, ByteString? initialization = null, SessionOptions? options = null, Checkpoint? initialCheckpoint = null, bool allowUnboundParticipants = false)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (peers == null || peers.Count < 1 || peers.Count > session.Participants.Count ||
                (!allowUnboundParticipants && peers.Count != session.Participants.Count))
                throw new ArgumentException("Bindings must cover the roster unless inactive unbound slots are explicitly allowed.");
            if (neutralInput == null) throw new ArgumentNullException(nameof(neutralInput));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _options = options ?? new SessionOptions(); var runtimeLimits = limits ?? initialCheckpoint?.Limits ?? new RuntimeLimits();
            _options.Validate(transport, runtimeLimits, session.Participants.Count);
            _peers = new Peer[session.Participants.Count]; _neutral = new RecordedInput[_peers.Length];
            var bindings = new Dictionary<ushort, PeerBinding>(); var connections = new HashSet<ulong>();
            foreach (var binding in peers)
            {
                if (binding == null || !session.Participants.Any(p => p.Slot == binding.Slot) ||
                    bindings.ContainsKey(binding.Slot) || !connections.Add(binding.ConnectionId))
                    throw new ArgumentException("Unknown slot, duplicate slot or duplicate connection.");
                bindings.Add(binding.Slot, binding);
            }
            for (int i = 0; i < _peers.Length; i++)
            {
                var participant = session.Participants[i];
                bool bound = bindings.TryGetValue(participant.Slot, out var binding);
                if (!bound && participant.IsActive) throw new ArgumentException("An active member must have a connection.");
                var peer = new Peer(bound ? binding!.ConnectionId : 0, participant) { Connected = bound, Live = bound };
                _peers[i] = peer;
                if (bound) _connections.Add(peer.ConnectionId, peer);
            }
            // 按 Session 的位置顺序冻结补齐输入，而不是按连接到达的顺序。
            for (int i = 0; i < _neutral.Length; i++)
            {
                var participant = session.Participants[i]; var bytes = neutralInput(participant);
                if (bytes == null || bytes.Length > runtimeLimits.MaxInputBytes) throw new ArgumentException("Invalid neutral input size.");
                _module.InputValidator.ValidateCanonical(bytes.Span);
                _neutral[i] = new RecordedInput(participant.Slot, participant.Generation, bytes.Span, InputOrigin.MissingSubstitute);
            }
            _runner = new SimulationRunner<TState>(module, session, runtimeLimits, initialization ?? initialCheckpoint?.InitializationData);
            if (initialCheckpoint != null)
            {
                if (initialCheckpoint.Tick != 0) throw new ArgumentException("Restart checkpoint must be a new session at tick zero.");
                _runner.Restore(initialCheckpoint);
            }
            _confirmed = _runner.CaptureCheckpoint(); _initialBytes = CheckpointCodec.Encode(_confirmed);
            _initialHash = CheckpointCodec.Hash(_confirmed); _outbox = new Outbox(_options);
            foreach (var peer in _peers)
            {
                if (!peer.Connected && _confirmed.Membership.Find(peer.Participant.Slot).IsActive)
                    throw new ArgumentException("An active restored member must have a connection.");
                peer.Snapshot = _initialBytes; peer.SnapshotLength = _initialBytes.Length; peer.SnapshotHash = _initialHash;
            }
        }

        /// <summary>当前生命周期；Failed 表示需要宿主处理错误并创建新会话。</summary>
        public SessionStatus Status { get; private set; } = SessionStatus.Preparing;
        /// <summary>已计算、捕获检查点并进入可靠发送队列的最后一帧。</summary>
        public ulong ConfirmedTick => _confirmed.Tick;
        /// <summary>最后确认帧的完整输入，包含输入来源。</summary>
        public InputFrame? LastFrame { get; private set; }
        /// <summary>失败原因；不把可能包含应用细节的异常消息发给其他机器。</summary>
        public string? Failure { get; private set; }
        /// <summary>被拒绝的消息数量，包含过期、越权、冲突和格式错误。</summary>
        public ulong RejectedMessages { get; private set; }
        /// <summary>最近一次拒绝原因，避免为攻击或故障流量建立无界日志。</summary>
        public string? LastRejection { get; private set; }
        /// <summary>内容完全相同、被去重的有效输入消息数量。</summary>
        public ulong DuplicateInputs { get; private set; }
        /// <summary>最近一次保存失败原因；失败后保持 Saving，允许宿主修复存储并重试。</summary>
        public string? LastSaveFailure { get; private set; }

        /// <summary>冻结最后确认状态，调用宿主存储函数，只有保存成功才关闭房间。</summary>
        /// <remarks>同步调用；函数不得重入会话。抛出保存异常后保持原确认点，后续 Poll 仍可收发但不推进。</remarks>
        public void SaveAndClose(Action<Checkpoint> save)
        {
            EnsureNotBusy();
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (Status != SessionStatus.Running && Status != SessionStatus.Saving)
                throw new InvalidOperationException("Save-and-close requires a running or paused-for-save session.");
            Status = SessionStatus.Saving; _busy = true;
            try { save(_confirmed); LastSaveFailure = null; Status = SessionStatus.Closed; }
            catch (Exception error) { LastSaveFailure = error.Message; throw; }
            finally { _busy = false; }
        }

        /// <summary>驱动一次收发与计算；返回本次成功确认帧的事件，宿主在返回后处理副作用。</summary>
        /// <param name="now">宿主的单调经过时间；不能倒退。输入以服务器处理它时的 now 判断是否迟到。</param>
        /// <returns>最多 MaxFramesPerPoll 个结果，按帧号递增；没有确认帧时为空。</returns>
        public IReadOnlyList<FrameResult> Poll(TimeSpan now)
        {
            EnsureNotBusy();
            if (now.Ticks < 0 || (_hasTime && now.Ticks < _lastTime)) throw new ArgumentOutOfRangeException(nameof(now), "Time must be monotonic.");
            if (!_hasTime) { _prepareAt = now.Ticks; _hasTime = true; }
            _lastTime = now.Ticks; _busy = true; var results = new List<FrameResult>();
            try
            {
                int sendBudget = _options.MaxMessagesPerPoll;
                _outbox.Flush(_transport, ref sendBudget);
                for (int i = 0; i < _options.MaxMessagesPerPoll && _transport.TryReceive(out var message); i++)
                    Receive(message!, now.Ticks);
                if (Status == SessionStatus.Preparing)
                {
                    if (now.Ticks - _prepareAt >= _options.PrepareTimeout.Ticks) Fail("Timed out waiting for all clients to restore the initial state.");
                    else
                    {
                        SendInitial(ref sendBudget);
                        bool allReady = true; foreach (var peer in _peers) if (peer.Connected) allReady &= peer.Ready;
                        if (allReady)
                        {
                            byte[] start = Wire.HashMessage(MessageKind.Start, _runner.Session.SessionId, _runner.Session.Epoch, _initialHash);
                            int connected = _peers.Count(p => p.Connected);
                            if (_outbox.CanAdd(connected, checked(connected * start.Length)))
                            {
                                Broadcast(MessageKind.Start, start); _startedAt = now.Ticks;
                                Status = SessionStatus.Running; _initialBytes = Array.Empty<byte>();
                                foreach (var peer in _peers) peer.Snapshot = Array.Empty<byte>();
                            }
                        }
                    }
                }
                if (Status == SessionStatus.Running)
                {
                    SendLiveBootstrap(ref sendBudget, now.Ticks);
                    for (int i = 0; i < _options.MaxFramesPerPoll; i++)
                    {
                        ulong next = checked(ConfirmedTick + 1);
                        if (now.Ticks < Deadline(next) || !TryFinalize(next, results)) break;
                    }
                }
                QueueTerminal(); _outbox.Flush(_transport, ref sendBudget);
            }
            catch (Exception error)
            {
                // 包括游戏 Step/Capture 和传输适配器抛出的异常。停止会话，不能重试可能已部分计算的帧。
                Fail(error.Message);
            }
            finally { _busy = false; }
            return new ReadOnlyCollection<FrameResult>(results);
        }

        /// <summary>由宿主确认连接断开后调用；下一确认帧写入停用事件，不直接修改游戏。</summary>
        /// <remarks>固定名单的准备期断线导致开局失败；运行期其余成员继续，停用帧确认后可 TryBind。</remarks>
        public void Deactivate(ulong connectionId)
        {
            EnsureNotBusy();
            if (!_connections.TryGetValue(connectionId, out var peer)) throw new ArgumentException("Unknown connection.");
            if (!peer.Connected) return;
            DisconnectPeer(peer);
            if (Status == SessionStatus.Preparing) Fail("A participant disconnected before start.");
        }

        /// <summary>停止接受和计算新帧；后续继续 Poll，把已确认帧及关闭消息送完。</summary>
        public void Close()
        { EnsureNotBusy(); if (Status != SessionStatus.Failed && Status != SessionStatus.Closed) Status = SessionStatus.Closed; }
        /// <summary>返回最后确认的完整检查点；失败时也不会暴露尚未提交的状态。</summary>
        public Checkpoint CaptureCheckpoint() { EnsureNotBusy(); return _confirmed; }
        /// <summary>从确认检查点解码独立状态副本，供宿主显示或测试。</summary>
        public TState InspectState() { EnsureNotBusy(); return _module.Codec.Decode(_confirmed.ApplicationState.Span); }

        private void Receive(TransportMessage message, long now)
        {
            if (!_connections.TryGetValue(message.ConnectionId, out var peer)) { Reject("Unknown connection."); return; }
            if (Status == SessionStatus.Failed || Status == SessionStatus.Closed || !peer.Connected) return;
            try
            {
                if (message.Payload.Length > _options.MaxRealtimeBytes) throw new InvalidDataException("Client message too large.");
                var packet = Wire.Decode(message.Payload, _options.MaxRealtimeBytes, out var kind, out var id, out var epoch);
                if (id != _runner.Session.SessionId || epoch != _runner.Session.Epoch) { Reject("Stale session or epoch."); return; }
                if (message.Channel != Wire.Channel(kind) || message.Delivery != DeliveryMode.ReliableOrdered)
                    throw new InvalidDataException("Unexpected channel or delivery mode.");
                if (kind == MessageKind.Ready)
                {
                    string hash = Wire.ReadHash(packet.Ready.InitialHash); uint window = packet.Ready.ReceiveWindow;
                    if (window < 1 || window > 1024) { Reject("Invalid client receive window."); return; }
                    if (hash != peer.SnapshotHash || peer.Offset != peer.SnapshotLength) { Reject("Initial state not ready or hash mismatch."); return; }
                    peer.ReceiveWindow = Math.Min((int)window, _options.MaxFutureFrames); peer.Ready = true; return;
                }
                if (kind == MessageKind.Progress)
                {
                    ulong executed = packet.Progress.Tick;
                    if (Status != SessionStatus.Running || !peer.Ready || executed > ConfirmedTick ||
                        (!peer.Live && executed > peer.SentTick)) { Reject("Invalid executed frame acknowledgement."); return; }
                    if (executed > peer.ExecutedTick) peer.ExecutedTick = executed;
                    return;
                }
                if (kind != MessageKind.Input) { Reject("Unexpected client message."); return; }
                var input = Wire.ReadInput(packet.Input, _runner.Limits.MaxInputBytes, out ulong tick);
                if (Status != SessionStatus.Running || !peer.Live) { Reject("Connection has not finished synchronizing."); return; }
                var member = _confirmed.Membership.Find(peer.Participant.Slot);
                if (input.Slot != member.Slot || input.Generation != member.Generation || (!member.IsActive && ConfirmedTick != 0)) { Reject("Input does not belong to this connection."); return; }
                if (tick <= ConfirmedTick || tick - ConfirmedTick > (ulong)_options.MaxFutureFrames) { Reject("Input outside the accepted frame window."); return; }
                // 等于截止时间也算迟到；不依赖客户端声称的时间，不转移到下一帧。
                if (now >= Deadline(tick)) { Reject("Input deadline has passed."); return; }
                _module.InputValidator.ValidateCanonical(input.Payload.Span);
                if (!_pending.TryGetValue(tick, out var frame)) { frame = new Dictionary<ushort, RecordedInput>(); _pending.Add(tick, frame); }
                if (frame.TryGetValue(input.Slot, out var existing))
                {
                    if (existing.Payload.Span.SequenceEqual(input.Payload.Span)) DuplicateInputs++;
                    else Reject("Conflicting duplicate input; the first accepted bytes are immutable.");
                    return;
                }
                frame.Add(input.Slot, input);
            }
            catch (Exception error) { Reject(error.Message); }
        }

        private void SendInitial(ref int budget)
        {
            foreach (var peer in _peers) if (peer.Connected) SendSnapshot(peer, ref budget);
        }

        private bool TryFinalize(ulong tick, List<FrameResult> results)
        {
            // 执行进度反馈提供流量控制：不让任何客户端的待执行帧超出其声明窗口。
            // 传输已接收消息不等于游戏已执行，因此这里不能用 TrySend 的成功替代进度。
            foreach (var peer in _peers) if (peer.Connected && peer.Live && tick - peer.ExecutedTick > (ulong)peer.ReceiveWindow) return false;
            var events = new List<MemberEvent>(); ulong sequence = _confirmed.Membership.LastSequence;
            foreach (var p in _confirmed.Membership.Participants)
            {
                var peer = _peers.First(x => x.Participant.Slot == p.Slot);
                if (p.IsActive && !peer.Connected)
                    events.Add(new MemberEvent(checked(++sequence), MemberEventKind.Deactivate, p.Slot, p.Generation));
                else if (!p.IsActive && peer.Connected && peer.Live)
                {
                    if (peer.RebindPending)
                        events.Add(new MemberEvent(checked(++sequence), MemberEventKind.Rebind, p.Slot, checked(p.Generation + 1)));
                    else if (ConfirmedTick == 0)
                        events.Add(new MemberEvent(checked(++sequence), MemberEventKind.Activate, p.Slot, p.Generation));
                }
            }
            var membership = _confirmed.Membership.Apply(events);
            _pending.TryGetValue(tick, out var submitted); var inputs = new List<RecordedInput>();
            for (int i = 0; i < _neutral.Length; i++)
            {
                var p = membership.Participants[i]; if (!p.IsActive) continue;
                inputs.Add(submitted != null && submitted.TryGetValue(p.Slot, out var input) ? input :
                    new RecordedInput(p.Slot, p.Generation, _neutral[i].Payload.Span, InputOrigin.MissingSubstitute));
            }
            var frame = new InputFrame(_runner.Session.SessionId, _runner.Session.Epoch, tick, inputs, events);
            // 先验证编码和广播容量，再推进模拟。摘要固定 32 字节，可以提前准确预算。
            int messageSize = Wire.Final(frame, new string('0', 64), _options.MaxRealtimeBytes).Length;
            int connected = _peers.Count(p => p.Connected && p.Live);
            if (!_outbox.CanAdd(connected, checked(messageSize * connected))) return false;
            FrameResult result = _runner.Advance(frame);
            Checkpoint checkpoint = _runner.CaptureCheckpoint();
            byte[] bytes = Wire.Final(frame, CheckpointCodec.Hash(checkpoint), _options.MaxRealtimeBytes);
            Broadcast(MessageKind.Final, bytes);
            _confirmed = checkpoint; LastFrame = frame; _pending.Remove(tick); results.Add(result);
            foreach (var memberEvent in events)
                if (memberEvent.Kind == MemberEventKind.Rebind)
                    _peers.First(p => p.Participant.Slot == memberEvent.Slot).RebindPending = false;
            BufferLiveFrame(tick, bytes);
            return true;
        }

        private long Deadline(ulong tick)
        {
            // ceil(tick * 每秒整数刻度 / 帧率)，拆成商和余数以避免中间乘法过早溢出。
            ulong rate = _runner.Session.TickRate;
            ulong seconds = tick / rate, remainder = tick % rate;
            ulong offset = checked(seconds * (ulong)TimeSpan.TicksPerSecond +
                (remainder * (ulong)TimeSpan.TicksPerSecond + rate - 1) / rate);
            return checked(_startedAt + checked((long)offset));
        }
        private void Broadcast(MessageKind kind, byte[] bytes)
        { foreach (var peer in _peers) if (peer.Connected && peer.Live) _outbox.Add(peer.ConnectionId, kind, bytes); }
        private void QueueTerminal()
        {
            if (Status != SessionStatus.Closed && Status != SessionStatus.Failed) return;
            MessageKind kind = Status == SessionStatus.Closed ? MessageKind.Close : MessageKind.Fault;
            foreach (var peer in _peers)
            {
                if (peer.TerminalQueued || !peer.Connected) continue;
                byte[] bytes = Wire.Terminal(kind, _runner.Session.SessionId, _runner.Session.Epoch, ConfirmedTick);
                if (!_outbox.CanAdd(1, bytes.Length)) break;
                _outbox.Add(peer.ConnectionId, kind, bytes); peer.TerminalQueued = true;
            }
        }
        private void Fail(string reason) { Status = SessionStatus.Failed; Failure ??= reason; }
        private void Reject(string reason) { RejectedMessages++; LastRejection = reason; }
        private void EnsureNotBusy() { if (_busy) throw new InvalidOperationException("Reentrant session calls are not allowed."); }
    }
}

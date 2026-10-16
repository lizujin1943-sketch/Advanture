using System;
using System.Collections.ObjectModel;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    /// <summary>模拟驱动器：校验完整输入，调用游戏计算一帧，并维护可恢复的框架状态。</summary>
    /// <typeparam name="TState">由游戏定义的完整状态类型。</typeparam>
    /// <remarks>
    /// 所有调用应由同一线程串行执行；重入检查不等于线程安全。
    /// 阶段 A 接受已准备好的完整、连续输入，不收集网络消息、不等待输入，也不按现实时间调度。
    /// 保存恢复的另一半实现位于 SimulationRunner.Checkpoints.cs，两个 partial 文件组成同一个类。
    /// </remarks>
    public sealed partial class SimulationRunner<TState>
    {
        // 接入方提供的工厂、编码器和输入检查器；运行器不依赖具体游戏类型。
        private readonly SimulationModule<TState> _module;
        // 必须进入检查点；恢复时新实例仍需要地图参数、种子等初始化上下文。
        private SimulationInitialization _initialization;
        // 当前实际持有游戏可变状态的实例，成功恢复后整体替换。
        private IDeterministicSimulation<TState> _simulation;
        // 阻止游戏回调在 Step/Capture/Restore 尚未结束时再次调用运行器，不是并发锁。
        private bool _busy;

        /// <summary>建立独立模拟实例并初始化；创建后 Tick 为 0。</summary>
        /// <param name="module">游戏提供的接入模块。</param>
        /// <param name="session">固定的会话、参与者名单和时间配置。</param>
        /// <param name="limits">容量限制；为空时采用默认配置。</param>
        /// <param name="initialization">游戏初始化参数；为空时传递空字节。</param>
        /// <remarks>
        /// 构造时就会调用游戏 Initialize。如果游戏必须使用非空参数，
        /// 创建用于加载的运行器时也应传入检查点里的 InitializationData。
        /// </remarks>
        public SimulationRunner(SimulationModule<TState> module, SessionDescriptor session,
            RuntimeLimits? limits = null, ByteString? initialization = null)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Limits = limits ?? new RuntimeLimits();
            if (session.Participants.Count > Limits.MaxParticipants)
                throw new ArgumentException("Participant count exceeds the configured limit.");
            if (initialization != null && initialization.Length > Limits.MaxCheckpointBytes)
                throw new ArgumentException("Initialization data exceeds the configured limit.");
            _initialization = new SimulationInitialization(session, initialization ?? ByteString.Empty);
            _simulation = module.Factory.Create() ?? throw new InvalidOperationException("Factory returned null.");
            _simulation.Initialize(_initialization);
            _history = new System.Collections.Generic.Queue<InputFrame>(Limits.HistoryFrames);
            _missingCounts = new ulong[session.Participants.Count];
            _membership = new MembershipState(session.Participants); _historyStartMembership = _membership;
        }

        /// <summary>本运行器固定使用的会话配置。</summary>
        public SessionDescriptor Session { get; }
        /// <summary>本运行器固定使用的容量限制。</summary>
        public RuntimeLimits Limits { get; }
        /// <summary>最后一次成功推进的帧号；初始为 0，恢复后变为检查点帧号。</summary>
        public ulong Tick { get; private set; }
        /// <summary>游戏计算或输出处理失败后置为 true；必须成功 Restore 才能继续使用。</summary>
        public bool IsFaulted { get; private set; }

        /// <summary>检查输入并推进一帧；输入帧号必须恰好等于当前 Tick 加 1。</summary>
        /// <param name="frame">每位参与者恰好一个输入的完整帧。</param>
        /// <returns>成功提交后的帧号和带有稳定身份的事件列表。</returns>
        /// <remarks>
        /// 输入校验失败发生在游戏计算之前，不会修改游戏状态。
        /// 一旦进入 Step，异常可能意味着游戏已被部分修改；此时标记故障，
        /// 不自动回滚，也不允许直接重试 Advance，调用者应显式恢复已有检查点。
        /// </remarks>
        public FrameResult Advance(InputFrame frame)
        {
            EnsureAvailable();
            _busy = true;
            try
            {
                // 1. 在调用游戏前检查完整帧，并先算好可能溢出的框架计数。
                // checked 让帧号和计数到达极限时失败，避免整数回绕成旧帧号。
                var membership = ValidateFrame(frame, checked(Tick + 1), _membership);
                ulong persistentTick = checked(Session.BaseTick + frame.Tick);
                var missingCounts = new ulong[_missingCounts.Length];
                for (int i = 0; i < membership.Participants.Count; i++)
                {
                    var p = membership.Participants[i];
                    if (!p.IsActive) continue;
                    var input = System.Linq.Enumerable.First(frame.Inputs, x => x.Slot == p.Slot);
                    bool reset = System.Linq.Enumerable.Any(frame.MemberEvents, e => e.Slot == p.Slot);
                    missingCounts[i] = input.Origin == InputOrigin.MissingSubstitute
                        ? checked((reset ? 0 : _missingCounts[i]) + 1) : 0;
                }
                // 在本帧成员事件应用后配对输入；停用位置不再有输入。
                var inputs = new SimulationInput[frame.Inputs.Count];
                for (int i = 0; i < inputs.Length; i++)
                    inputs[i] = new SimulationInput(membership.Find(frame.Inputs[i].Slot), frame.Inputs[i].Payload);
                try
                {
                    // 3. 唯一推进游戏规则的入口；游戏应只修改自身状态并返回事件数据。
                    var output = _simulation.Step(new FrameContext(frame.Tick, persistentTick, Session.TickRate, frame.MemberEvents),
                        new ReadOnlyCollection<SimulationInput>(inputs));
                    if (output == null || output.Count > Limits.MaxEventsPerFrame)
                        throw new InvalidOperationException("Game event count exceeds the configured limit.");
                    var events = new SimulationEvent[output.Count];
                    for (int i = 0; i < events.Length; i++)
                    {
                        var payload = output[i];
                        if (payload == null || payload.Length > Limits.MaxEventBytes)
                            throw new InvalidOperationException("Invalid or oversized game event.");
                        // 相同会话、运行和输入重算时，帧号与事件序号保持一致。
                        // 此处只建立事件身份；预测事件替换与确认去重由会话层负责。
                        events[i] = new SimulationEvent(Session.SessionId, Session.Epoch, frame.Tick, i,
                            new ByteString(payload.Span));
                    }
                    // 4. 游戏计算和事件检查成功后才提交框架状态。
                    // 近期历史达到上限时淘汰最旧帧，长录制由 ReplayRecorder 单独维护。
                    if (_history.Count == Limits.HistoryFrames)
                        _historyStartMembership = _historyStartMembership.Apply(_history.Dequeue().MemberEvents);
                    _history.Enqueue(frame); _missingCounts = missingCounts; _membership = membership;
                    Tick = frame.Tick;
                    return new FrameResult(Tick, new ReadOnlyCollection<SimulationEvent>(events));
                }
                // 不能假设 Step 抛异常前没有改变游戏；故障标记强制调用者先恢复。
                catch { IsFaulted = true; throw; }
            }
            finally { _busy = false; }
        }

        /// <summary>获取用于测试或调试的独立游戏状态副本。</summary>
        /// <returns>经过编码再解码得到的状态，不暴露运行中实例的可变对象。</returns>
        /// <remarks>此方法会分配和复制数据，不是为大量实体逐帧渲染优化的读取接口。</remarks>
        public TState InspectState()
        {
            EnsureAvailable();
            _busy = true;
            try { return _module.Codec.Decode(CaptureApplication()); }
            finally { _busy = false; }
        }

        /// <summary>调用游戏捕获与编码状态，并检查游戏字节本身的容量。</summary>
        /// <remarks>完整检查点还包含头部、历史和摘要，其总大小由 CheckpointCodec 再检查。</remarks>
        private byte[] CaptureApplication()
        {
            var bytes = _module.Codec.Encode(_simulation.Capture());
            if (bytes == null || bytes.Length > Limits.MaxCheckpointBytes)
                throw new InvalidOperationException("Application state exceeds the checkpoint limit.");
            return bytes;
        }

        /// <summary>拒绝同一调用链中的重入，以及尚未恢复的故障实例。</summary>
        /// <param name="allowFaulted">仅恢复入口允许操作故障实例，以便修复其状态。</param>
        private void EnsureAvailable(bool allowFaulted = false)
        {
            if (_busy) throw new InvalidOperationException("Reentrant runtime calls are not allowed.");
            if (IsFaulted && !allowFaulted)
                throw new InvalidOperationException("The simulation is faulted; restore a valid checkpoint before continuing.");
        }

        /// <summary>在触碰游戏状态前核对帧身份、参与者、控制权、大小和格式。</summary>
        /// <param name="frame">待验证输入。</param>
        /// <param name="expectedTick">调用者期待的帧号；推进时为下一帧，恢复历史时使用该历史帧号。</param>
        private MembershipState ValidateFrame(InputFrame frame, ulong expectedTick, MembershipState previous)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (frame.SessionId != Session.SessionId || frame.Epoch != Session.Epoch || frame.Tick != expectedTick)
                throw new ArgumentException("Frame belongs to a different session/epoch or is not consecutive.");
            var next = previous.Apply(frame.MemberEvents);
            next.ValidateInputs(frame.Inputs, Limits.MaxInputBytes);
            foreach (var input in frame.Inputs) _module.InputValidator.ValidateCanonical(input.Payload.Span);
            return next;
        }
    }
}

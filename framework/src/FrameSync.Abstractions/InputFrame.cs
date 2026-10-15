using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FrameSync.Abstractions
{
    /// <summary>输入的来源标记，仅供框架记录历史和连续缺失次数。</summary>
    /// <remarks>游戏计算只接收输入内容；阶段 A 不决定缺失时应该补什么输入。</remarks>
    public enum InputOrigin : byte
    {
        /// <summary>参与者实际提交的输入。</summary>
        Submitted = 0,
        /// <summary>输入缺失时由外部模块补齐的输入。</summary>
        MissingSubstitute = 1
    }

    /// <summary>一名参与者在一帧中的输入记录，包含控制权版本和来源。</summary>
    /// <remarks>内容在构造时复制，后续修改原始数组不会改变录制和重放结果。</remarks>
    public sealed class RecordedInput
    {
        /// <summary>复制输入内容，并验证控制权版本和来源枚举。</summary>
        /// <param name="slot">本帧输入所属的参与者位置。</param>
        /// <param name="generation">该位置的控制权版本，必须大于零。</param>
        /// <param name="payload">游戏自行规定的输入字节，长度和内容由运行器进一步检查。</param>
        /// <param name="origin">实际提交或外部补齐；默认表示实际提交。</param>
        public RecordedInput(ushort slot, ulong generation, ReadOnlySpan<byte> payload,
            InputOrigin origin = InputOrigin.Submitted)
        {
            if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (origin != InputOrigin.Submitted && origin != InputOrigin.MissingSubstitute)
                throw new ArgumentOutOfRangeException(nameof(origin));
            Slot = slot; Generation = generation; Payload = new ByteString(payload); Origin = origin;
        }
        /// <summary>输入所属的位置，也是输入排序依据。</summary>
        public ushort Slot { get; }
        /// <summary>发送此输入时的控制权版本，必须与会话名单一致。</summary>
        public ulong Generation { get; }
        /// <summary>游戏输入的不可变字节内容。</summary>
        public ByteString Payload { get; }
        /// <summary>用于恢复框架缺失计数的来源信息，不传入游戏计算。</summary>
        public InputOrigin Origin { get; }
    }

    /// <summary>带有会话身份和帧号的一组输入，构造后按参与者位置保持稳定顺序。</summary>
    /// <remarks>
    /// 构造函数检查基本结构、排序和重复位置；是否覆盖全部参与者、帧号是否连续，
    /// 由持有会话配置的运行器检查。第一次执行的输入帧号是 1，0 留给初始状态。
    /// </remarks>
    public sealed class InputFrame
    {
        /// <summary>复制输入列表并按 Slot 升序排列，拒绝空元素和重复位置。</summary>
        /// <param name="sessionId">输入所属的会话身份，不能为 Guid.Empty。</param>
        /// <param name="epoch">会话中的运行编号，必须大于零。</param>
        /// <param name="tick">将要执行的逻辑帧号，必须大于零。</param>
        /// <param name="inputs">各参与者的输入；原始列表顺序不影响最终执行顺序。</param>
        public InputFrame(Guid sessionId, ulong epoch, ulong tick, IReadOnlyList<RecordedInput> inputs)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("Missing session identity.");
            if (epoch == 0 || tick == 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (inputs.Count > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(inputs));
            var copy = new RecordedInput[inputs.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = inputs[i] ?? throw new ArgumentException("Null input.");
            // 网络到达顺序不应影响游戏结果；在进入计算前统一为位置顺序。
            Array.Sort(copy, (a, b) => a.Slot.CompareTo(b.Slot));
            for (int i = 1; i < copy.Length; i++)
                if (copy[i - 1].Slot == copy[i].Slot) throw new ArgumentException("Duplicate slot.");
            SessionId = sessionId; Epoch = epoch; Tick = tick;
            Inputs = new ReadOnlyCollection<RecordedInput>(copy);
        }
        /// <summary>输入所属的会话身份。</summary>
        public Guid SessionId { get; }
        /// <summary>输入所属的运行编号，防止旧运行的数据进入当前模拟。</summary>
        public ulong Epoch { get; }
        /// <summary>此组输入要推进到的逻辑帧号。</summary>
        public ulong Tick { get; }
        /// <summary>按 Slot 升序排列的只读输入列表。</summary>
        public IReadOnlyList<RecordedInput> Inputs { get; }
    }

    /// <summary>交给游戏计算的输入：参与者身份和内容，不包含框架的来源标记。</summary>
    public sealed class SimulationInput
    {
        /// <summary>组合已经过运行器验证的参与者和输入内容。</summary>
        /// <param name="participant">输入对应的会话参与者。</param>
        /// <param name="payload">游戏规定的输入字节。</param>
        public SimulationInput(Participant participant, ByteString payload)
        { Participant = participant; Payload = payload; }
        /// <summary>本次输入所属的参与者。</summary>
        public Participant Participant { get; }
        /// <summary>供游戏解释的输入内容。</summary>
        public ByteString Payload { get; }
    }

    /// <summary>传给游戏的一帧时间信息；由整数帧号表示逻辑时间。</summary>
    /// <remarks>不依赖渲染帧率或系统时钟，控制台可连续快速执行大量逻辑帧。</remarks>
    public readonly struct FrameContext
    {
        /// <summary>保存运行器已经验证过的帧号和逻辑帧率。</summary>
        /// <param name="tick">本次运行将执行的帧号。</param>
        /// <param name="persistentTick">累计帧号，等于会话 BaseTick 加 tick。</param>
        /// <param name="tickRate">每秒逻辑帧数。</param>
        public FrameContext(ulong tick, ulong persistentTick, uint tickRate)
        { Tick = tick; PersistentTick = persistentTick; TickRate = tickRate; }
        /// <summary>本次运行正在执行的帧号。</summary>
        public ulong Tick { get; }
        /// <summary>包含之前运行时间的累计帧号。</summary>
        public ulong PersistentTick { get; }
        /// <summary>每帧逻辑时长精确为 1 / TickRate 秒，不使用浮点数累加时钟。</summary>
        public uint TickRate { get; }
    }

    /// <summary>一帧计算产生的事件数据，例如通知表现层某个数值发生了变化。</summary>
    /// <remarks>
    /// 会话、运行编号、帧号和帧内序号共同构成事件身份，方便后续实现重放去重。
    /// 本类型只保存数据，不播放音效、不调用平台成就，也不负责预测事件的确认或撤销。
    /// </remarks>
    public sealed class SimulationEvent
    {
        /// <summary>组合事件的身份信息和游戏定义的内容。</summary>
        /// <param name="sessionId">产生事件的会话身份。</param>
        /// <param name="epoch">产生事件的运行编号。</param>
        /// <param name="tick">产生事件的帧号。</param>
        /// <param name="index">事件在本帧输出列表中的位置，从 0 开始。</param>
        /// <param name="payload">游戏自定义事件内容。</param>
        public SimulationEvent(Guid sessionId, ulong epoch, ulong tick, int index, ByteString payload)
        { SessionId = sessionId; Epoch = epoch; Tick = tick; Index = index; Payload = payload; }
        /// <summary>产生事件的会话身份。</summary>
        public Guid SessionId { get; }
        /// <summary>产生事件的运行编号。</summary>
        public ulong Epoch { get; }
        /// <summary>产生事件的逻辑帧号。</summary>
        public ulong Tick { get; }
        /// <summary>本帧内从 0 开始的事件序号。</summary>
        public int Index { get; }
        /// <summary>由游戏解释的事件字节内容。</summary>
        public ByteString Payload { get; }
    }

    /// <summary>运行器成功推进一帧后返回的帧号与事件列表。</summary>
    public sealed class FrameResult
    {
        /// <summary>建立一帧的执行结果。</summary>
        /// <param name="tick">已经完成计算的帧号。</param>
        /// <param name="events">顺序稳定的事件列表；构造函数保留其引用，调用者应保证内容不再变化。</param>
        /// <remarks>运行器会提供由它创建的只读集合。</remarks>
        public FrameResult(ulong tick, IReadOnlyList<SimulationEvent> events)
        { Tick = tick; Events = events; }
        /// <summary>本次已完成计算的帧号。</summary>
        public ulong Tick { get; }
        /// <summary>按照游戏输出顺序排列的事件。</summary>
        public IReadOnlyList<SimulationEvent> Events { get; }
    }
}

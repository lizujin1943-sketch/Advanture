using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FrameSync.Abstractions
{
    /// <summary>一个参与者在本次运行中的身份、输入位置和控制权版本。</summary>
    /// <remarks>不包含连接、Socket 或平台账号，避免把模拟身份与具体联网方式绑定。</remarks>
    public sealed class Participant
    {
        /// <summary>建立参与者记录。</summary>
        /// <param name="slot">输入排序使用的位置编号；同一会话内唯一，不要求连续。</param>
        /// <param name="participantId">参与者的稳定身份，不能为 Guid.Empty。</param>
        /// <param name="generation">位置的控制权版本，从 1 开始，用于识别过期输入。</param>
        public Participant(ushort slot, Guid participantId, ulong generation = 1)
        {
            if (participantId == Guid.Empty) throw new ArgumentException("Participant identity must be nonempty.");
            if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
            Slot = slot; ParticipantId = participantId; Generation = generation;
        }
        /// <summary>本次运行中的输入位置；框架按此值从小到大处理输入。</summary>
        public ushort Slot { get; }
        /// <summary>参与者的稳定身份，区别于临时网络连接编号。</summary>
        public Guid ParticipantId { get; }
        /// <summary>控制权版本；输入必须携带相同版本。阶段 A 不负责切换控制者。</summary>
        public ulong Generation { get; }
    }

    /// <summary>描述一次模拟运行的固定配置：身份、时间基准和参与者名单。</summary>
    /// <remarks>
    /// 阶段 A 的名单在运行期间固定。参与者按 Slot 排序，后续输入也采用同样顺序，
    /// 因此不同机器即使收到输入的先后顺序不同，也能以同一顺序交给游戏。
    /// </remarks>
    public sealed class SessionDescriptor
    {
        /// <summary>检查并复制参与者名单，建立只读的会话描述。</summary>
        /// <param name="sessionId">本次会话身份，不能为 Guid.Empty。</param>
        /// <param name="epoch">会话中的运行编号，从 1 开始，用于拒绝旧运行的输入。</param>
        /// <param name="tickRate">每秒逻辑帧数，范围为 1～1000；不表示自动按现实时间调度。</param>
        /// <param name="participants">非空名单；参与者身份和位置分别唯一。实际人数还受运行器限制。</param>
        /// <param name="baseTick">本次运行之前已累计的帧数；累计帧号为 baseTick 加当前帧号。</param>
        public SessionDescriptor(Guid sessionId, ulong epoch, uint tickRate,
            IReadOnlyList<Participant> participants, ulong baseTick = 0)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("Session identity must be nonempty.");
            if (epoch == 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (tickRate == 0 || tickRate > 1000) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (participants == null) throw new ArgumentNullException(nameof(participants));
            if (participants.Count < 1 || participants.Count > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(participants));
            // 复制后再排序，既固定执行顺序，也不改变调用者传入的列表。
            var copy = new Participant[participants.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = participants[i] ?? throw new ArgumentException("Null participant.");
            Array.Sort(copy, (a, b) => a.Slot.CompareTo(b.Slot));
            if (copy.Select(p => p.Slot).Distinct().Count() != copy.Length ||
                copy.Select(p => p.ParticipantId).Distinct().Count() != copy.Length)
                throw new ArgumentException("Slots and participant identities must be unique.");
            SessionId = sessionId; Epoch = epoch; TickRate = tickRate; BaseTick = baseTick;
            Participants = new ReadOnlyCollection<Participant>(copy);
        }
        /// <summary>本次会话的身份；不同会话的输入和检查点不能直接混用。</summary>
        public Guid SessionId { get; }
        /// <summary>会话中的运行编号，用于区分同一会话的不同运行。</summary>
        public ulong Epoch { get; }
        /// <summary>逻辑帧率；每次 Step 对应精确的 1 / TickRate 秒。</summary>
        public uint TickRate { get; }
        /// <summary>累计时间的起始帧偏移，为以后读档后重新运行保留时间基准。</summary>
        public ulong BaseTick { get; }
        /// <summary>按 Slot 升序排列的只读参与者名单。</summary>
        public IReadOnlyList<Participant> Participants { get; }
    }

    /// <summary>传给游戏初始化方法的会话配置和游戏自定义参数。</summary>
    /// <remarks>例如随机种子、地图参数；框架只保存字节，不解释其游戏含义。</remarks>
    public sealed class SimulationInitialization
    {
        /// <summary>组合初始化所需的数据；调用者应提供有效的非空对象。</summary>
        /// <param name="session">固定会话配置。</param>
        /// <param name="data">游戏自定义初始化数据；没有参数时使用 ByteString.Empty。</param>
        public SimulationInitialization(SessionDescriptor session, ByteString data)
        { Session = session; Data = data; }
        /// <summary>本次模拟使用的会话配置。</summary>
        public SessionDescriptor Session { get; }
        /// <summary>游戏自定义初始化数据的不可变字节表示。</summary>
        public ByteString Data { get; }
    }
}

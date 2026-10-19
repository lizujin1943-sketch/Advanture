using System;
using FrameSync.Runtime;
using FrameSync.Transport;

namespace FrameSync.Sessions
{
    /// <summary>会话层的容量和单次工作量限制；时间由宿主传入，不依赖 Unity 或系统时钟。</summary>
    public sealed class SessionOptions
    {
        /// <summary>建立会话配置；超出实现支持的范围时立即拒绝。</summary>
        /// <param name="maxFutureFrames">最多缓存多少帧未来输入或确认帧。</param>
        /// <param name="maxMessagesPerPoll">一次 Poll 最多收/发多少条消息，避免无休止处理网络。</param>
        /// <param name="maxFramesPerPoll">一次 Poll 最多计算多少帧；落后时分多次追赶。</param>
        /// <param name="maxOutboxMessages">等待传输接收的消息数上限。</param>
        /// <param name="maxOutboxBytes">等待发送的总字节上限。</param>
        /// <param name="maxRealtimeBytes">输入和确认帧的最大消息大小。</param>
        /// <param name="snapshotChunkBytes">初始检查点每块的数据字节数，不含消息头。</param>
        /// <param name="prepareTimeout">服务器从首次 Poll 到全员 Ready 的最长时间。</param>
        public SessionOptions(int maxFutureFrames = 128, int maxMessagesPerPoll = 64, int maxFramesPerPoll = 8,
            int maxOutboxMessages = 128, int maxOutboxBytes = 2097152, int maxRealtimeBytes = 900,
            int snapshotChunkBytes = 16384, TimeSpan? prepareTimeout = null)
        {
            if (maxFutureFrames < 1 || maxFutureFrames > 1024) throw new ArgumentOutOfRangeException(nameof(maxFutureFrames));
            if (maxMessagesPerPoll < 1 || maxMessagesPerPoll > 4096) throw new ArgumentOutOfRangeException(nameof(maxMessagesPerPoll));
            if (maxFramesPerPoll < 1 || maxFramesPerPoll > 128) throw new ArgumentOutOfRangeException(nameof(maxFramesPerPoll));
            if (maxOutboxMessages < 1 || maxOutboxMessages > 65536) throw new ArgumentOutOfRangeException(nameof(maxOutboxMessages));
            if (maxRealtimeBytes < 128 || maxRealtimeBytes > 65536) throw new ArgumentOutOfRangeException(nameof(maxRealtimeBytes));
            if (maxOutboxBytes < maxRealtimeBytes) throw new ArgumentOutOfRangeException(nameof(maxOutboxBytes));
            if (snapshotChunkBytes < 1 || snapshotChunkBytes > 16384) throw new ArgumentOutOfRangeException(nameof(snapshotChunkBytes));
            TimeSpan timeout = prepareTimeout ?? TimeSpan.FromSeconds(10);
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(prepareTimeout));
            MaxFutureFrames = maxFutureFrames; MaxMessagesPerPoll = maxMessagesPerPoll; MaxFramesPerPoll = maxFramesPerPoll;
            MaxOutboxMessages = maxOutboxMessages; MaxOutboxBytes = maxOutboxBytes; MaxRealtimeBytes = maxRealtimeBytes;
            SnapshotChunkBytes = snapshotChunkBytes; PrepareTimeout = timeout;
        }
        /// <summary>相对最后确认帧允许领先的帧数。</summary>
        public int MaxFutureFrames { get; }
        /// <summary>单次调用的消息处理预算。</summary>
        public int MaxMessagesPerPoll { get; }
        /// <summary>单次调用的计算帧数预算。</summary>
        public int MaxFramesPerPoll { get; }
        /// <summary>待发送消息数上限。</summary>
        public int MaxOutboxMessages { get; }
        /// <summary>待发送负载总字节上限。</summary>
        public int MaxOutboxBytes { get; }
        /// <summary>完整输入/确认帧的消息大小上限。</summary>
        public int MaxRealtimeBytes { get; }
        /// <summary>检查点分块的数据大小。</summary>
        public int SnapshotChunkBytes { get; }
        /// <summary>服务器等待全员准备的最长时间。</summary>
        public TimeSpan PrepareTimeout { get; }

        // 开局之前检查最坏情况：不允许运行到一半才发现完整帧装不进传输或队列。
        internal void Validate(ITransport transport, RuntimeLimits limits, int participants)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (participants < 1 || participants > 4) throw new ArgumentException("Sessions currently support one to four participants.");
            if (!transport.Supports(DeliveryMode.ReliableOrdered) || transport.ChannelCount < 4 ||
                transport.MaxMessageBytes < Math.Max(MaxRealtimeBytes, Wire.MaxSnapshotMessageBytes(SnapshotChunkBytes)))
                throw new ArgumentException("Transport capabilities do not meet this session's requirements.");
            int frameSize = Wire.MaxFinalMessageBytes(participants, limits.MaxInputBytes);
            if (frameSize > MaxRealtimeBytes) throw new ArgumentException("A complete frame would exceed the realtime message budget.");
            if (MaxOutboxMessages < participants || MaxOutboxBytes < checked(participants * frameSize))
                throw new ArgumentException("Outbox must hold at least one complete broadcast.");
        }
    }
}

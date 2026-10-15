using System;

namespace FrameSync.Runtime
{
    /// <summary>运行器和检查点格式允许的容量上限，避免历史和外部数据无限增长。</summary>
    /// <remarks>
    /// 限制会随检查点一起保存，恢复到已有运行器时必须完全一致。
    /// 这些限制约束框架数据，不能代替游戏自行限制 Step、Capture 和 Decode 的内存使用。
    /// </remarks>
    public sealed class RuntimeLimits
    {
        /// <summary>建立运行容量配置；超出支持范围时拒绝创建。</summary>
        /// <param name="maxParticipants">最多参与者数量，默认 4，范围 1～65535；当前验证覆盖 1～4 人。</param>
        /// <param name="maxInputBytes">单人单帧输入的最大字节数，默认 128，范围 1 字节～1 MiB。</param>
        /// <param name="maxCheckpointBytes">整个检查点的最大字节数，含头部和校验摘要，默认 8 MiB，至少 256 字节。</param>
        /// <param name="historyFrames">保留的近期输入帧数，范围 1～128，默认 128。</param>
        /// <param name="maxEventsPerFrame">每帧最多输出多少事件，默认 128；允许为零。</param>
        /// <param name="maxEventBytes">单个事件的最大字节数，默认 1024；允许为零。</param>
        public RuntimeLimits(int maxParticipants = 4, int maxInputBytes = 128,
            int maxCheckpointBytes = 8 * 1024 * 1024, int historyFrames = 128,
            int maxEventsPerFrame = 128, int maxEventBytes = 1024)
        {
            if (maxParticipants < 1 || maxParticipants > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(maxParticipants));
            if (maxInputBytes < 1 || maxInputBytes > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
            if (maxCheckpointBytes < 256) throw new ArgumentOutOfRangeException(nameof(maxCheckpointBytes));
            if (historyFrames < 1 || historyFrames > 128) throw new ArgumentOutOfRangeException(nameof(historyFrames));
            if (maxEventsPerFrame < 0 || maxEventBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxEventsPerFrame));
            MaxParticipants = maxParticipants; MaxInputBytes = maxInputBytes;
            MaxCheckpointBytes = maxCheckpointBytes; HistoryFrames = historyFrames;
            MaxEventsPerFrame = maxEventsPerFrame; MaxEventBytes = maxEventBytes;
        }
        /// <summary>本运行最多允许的参与者数量。</summary>
        public int MaxParticipants { get; }
        /// <summary>单名参与者每帧的输入字节上限。</summary>
        public int MaxInputBytes { get; }
        /// <summary>完整检查点的编码字节上限，包含游戏状态、框架信息和校验摘要。</summary>
        public int MaxCheckpointBytes { get; }
        /// <summary>运行器保留的近期输入窗口；不代表完整录制或自动回滚的快照数量。</summary>
        public int HistoryFrames { get; }
        /// <summary>游戏每帧可返回的事件数量上限。</summary>
        public int MaxEventsPerFrame { get; }
        /// <summary>单个游戏事件的内容字节上限。</summary>
        public int MaxEventBytes { get; }
    }
}

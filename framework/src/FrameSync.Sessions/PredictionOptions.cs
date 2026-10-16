using System;
using FrameSync.Runtime;

namespace FrameSync.Sessions
{
    /// <summary>可选的客户端预测限制；没有传入本配置时仍只执行确认帧。</summary>
    public sealed class PredictionOptions
    {
        /// <summary>设置最多领先确认进度的帧数，以及保留检查点的编码字节预算。</summary>
        /// <remarks>默认 12 帧。预算还包含确认基点和重算期间保持不变的展示快照；
        /// 这是编码数据预算，不等于 CLR 托管堆峰值。快照复制的性能优化留待后续。</remarks>
        public PredictionOptions(int maxPredictionFrames = 12, long maxRetainedBytes = 128L * 1024 * 1024)
        {
            if (maxPredictionFrames < 1 || maxPredictionFrames > 128) throw new ArgumentOutOfRangeException(nameof(maxPredictionFrames));
            if (maxRetainedBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxRetainedBytes));
            MaxPredictionFrames = maxPredictionFrames; MaxRetainedBytes = maxRetainedBytes;
        }
        /// <summary>达到窗口后暂停预测，继续收取服务器结果，不扩大缓存。</summary>
        public int MaxPredictionFrames { get; }
        /// <summary>最坏情况下保留的完整检查点编码总大小上限。</summary>
        public long MaxRetainedBytes { get; }
        internal void Validate(RuntimeLimits limits, SessionOptions session)
        {
            if (MaxPredictionFrames > session.MaxFutureFrames ||
                checked((MaxPredictionFrames + 2L) * limits.MaxCheckpointBytes) > MaxRetainedBytes)
                throw new ArgumentException("Prediction window or checkpoint budget is insufficient.");
        }
    }
}

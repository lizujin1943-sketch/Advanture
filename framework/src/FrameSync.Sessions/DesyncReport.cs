using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace FrameSync.Sessions
{
    /// <summary>首个状态分歧的有界诊断材料，由宿主选择写文件或显示；核心不访问文件系统。</summary>
    public sealed class DesyncReport
    {
        internal DesyncReport(Checkpoint lastConfirmed, InputFrame frame, Checkpoint candidate, string expectedHash, string actualHash)
        { LastConfirmed=lastConfirmed; Frame=frame; Candidate=candidate; ExpectedHash=expectedHash; ActualHash=actualHash; }
        /// <summary>分歧之前的最后确认状态，重放问题帧的起点。</summary>
        public Checkpoint LastConfirmed { get; }
        /// <summary>产生分歧的服务器最终输入；尚未确认为本地成功执行。</summary>
        public InputFrame Frame { get; }
        /// <summary>本机执行问题帧后的状态，只用于诊断，不能当作确认存档。</summary>
        public Checkpoint Candidate { get; }
        /// <summary>服务器提供的完整状态摘要。</summary>
        public string ExpectedHash { get; }
        /// <summary>本机实际完整状态摘要。</summary>
        public string ActualHash { get; }
    }
}

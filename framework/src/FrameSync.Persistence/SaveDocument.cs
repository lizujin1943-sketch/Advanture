using System;
using FrameSync.Runtime;

namespace FrameSync.Persistence
{
    /// <summary>一份正式存档的稳定身份和完整确认检查点。</summary>
    public sealed class SaveDocument
    {
        /// <summary>组合由宿主分配的存档身份与确认检查点。</summary>
        /// <param name="saveId">跨多次覆盖保存保持不变的非空存档身份。</param>
        /// <param name="checkpoint">从权威确认状态捕获的完整检查点。</param>
        public SaveDocument(Guid saveId, Checkpoint checkpoint)
        {
            if (saveId == Guid.Empty) throw new ArgumentException("Save identity must not be empty.", nameof(saveId));
            SaveId = saveId;
            Checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        }
        /// <summary>存档身份，独立于检查点中的会话身份。</summary>
        public Guid SaveId { get; }
        /// <summary>包含游戏及框架状态的不可变检查点。</summary>
        public Checkpoint Checkpoint { get; }
    }
}

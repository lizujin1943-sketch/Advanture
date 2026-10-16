using System.Collections.Generic;
using System.Collections.ObjectModel;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    /// <summary>某一帧执行结束后的完整检查点，包含游戏状态和继续运行所需的框架信息。</summary>
    /// <remarks>
    /// Tick 为 0 表示尚未执行输入；恢复 Tick 为 100 的检查点后，下一帧必须是 101。
    /// 检查点负责内存中的数据表达，持久化文件的原子替换、备份和版本迁移由后续存档层负责。
    /// </remarks>
    public sealed class Checkpoint
    {
        /// <summary>由运行器捕获或编码器解码后构造，复制可变容器与字节内容。</summary>
        /// <param name="manifest">游戏兼容清单。</param>
        /// <param name="session">固定会话配置。</param>
        /// <param name="limits">本运行使用的容量限制。</param>
        /// <param name="tick">已经执行结束的帧号。</param>
        /// <param name="history">截止 tick 的有限近期输入，按帧号递增。</param>
        /// <param name="missingCounts">按会话名单顺序排列的连续缺失次数。</param>
        /// <param name="state">游戏完整状态的编码字节。</param>
        /// <param name="initializationData">新建实例后恢复状态所需的初始化参数。</param>
        internal Checkpoint(CompatibilityManifest manifest, SessionDescriptor session, RuntimeLimits limits,
            ulong tick, IReadOnlyList<InputFrame> history, IReadOnlyList<ulong> missingCounts, byte[] state, ByteString initializationData,
            MembershipState membership, MembershipState historyStartMembership)
        {
            Manifest = manifest; Session = session; Limits = limits; Tick = tick;
            Membership = membership; HistoryStartMembership = historyStartMembership;
            // InputFrame 自身不可变，只需复制列表；数组和计数也不能借用运行中的可写容器。
            InputHistory = new ReadOnlyCollection<InputFrame>(new List<InputFrame>(history));
            MissingInputCounts = new ReadOnlyCollection<ulong>(new List<ulong>(missingCounts));
            ApplicationState = new ByteString(state);
            InitializationData = new ByteString(initializationData.Span);
        }
        /// <summary>生成检查点的游戏及规则版本信息。</summary>
        public CompatibilityManifest Manifest { get; }
        /// <summary>当前帧结束时的成员控制权和启用状态。</summary>
        public MembershipState Membership { get; }
        /// <summary>近期历史第一帧之前的成员状态，供校验与重放历史使用。</summary>
        public MembershipState HistoryStartMembership { get; }
        /// <summary>恢复时必须匹配的会话、时间基准和参与者名单。</summary>
        public SessionDescriptor Session { get; }
        /// <summary>捕获时采用的运行容量限制。</summary>
        public RuntimeLimits Limits { get; }
        /// <summary>已经计算完成的本次运行帧号。</summary>
        public ulong Tick { get; }
        /// <summary>累计帧号，即会话 BaseTick 加 Tick；溢出时显式失败。</summary>
        public ulong PersistentTick => checked(Session.BaseTick + Tick);
        /// <summary>截止当前帧的近期输入；仅保存有限窗口，不是整个会话的录制。</summary>
        public IReadOnlyList<InputFrame> InputHistory { get; }
        /// <summary>各参与者截至当前帧连续使用补齐输入的次数，与会话名单一一对应。</summary>
        public IReadOnlyList<ulong> MissingInputCounts { get; }
        /// <summary>由游戏编码器生成的完整状态；框架不解释其字段。</summary>
        public ByteString ApplicationState { get; }
        /// <summary>恢复新实例时先交给 Initialize 的原始游戏参数。</summary>
        public ByteString InitializationData { get; }
    }
}

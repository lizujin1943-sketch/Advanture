using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    /// <summary>单份输入录制允许的帧数和完整编码大小上限。</summary>
    /// <remarks>录制不会自动淘汰旧输入；超过限制就失败，以免丢失从起点重放所需的数据。</remarks>
    public sealed class ReplayLimits
    {
        /// <summary>建立录制容量配置。</summary>
        /// <param name="maxFrames">允许追加的帧数，默认 100000；允许为零，表示只保存起点。</param>
        /// <param name="maxBytes">整份录制的最大字节数，默认 64 MiB，至少 256 字节。</param>
        public ReplayLimits(int maxFrames = 100000, int maxBytes = 64 * 1024 * 1024)
        {
            if (maxFrames < 0 || maxBytes < 256) throw new ArgumentOutOfRangeException(nameof(maxFrames));
            MaxFrames = maxFrames; MaxBytes = maxBytes;
        }
        /// <summary>初始检查点之后最多保存的输入帧数。</summary>
        public int MaxFrames { get; }
        /// <summary>包含文件头、初始检查点、全部输入和校验摘要的总字节上限。</summary>
        public int MaxBytes { get; }
    }

    /// <summary>可重放的数据：一个完整起点，以及紧接起点的连续输入序列。</summary>
    /// <remarks>输入本身不可变，列表也被复制，因此后续追加录制不会改变已生成的对象。</remarks>
    public sealed class ReplayRecording
    {
        /// <summary>由录制器生成一份列表独立、内容只读的录制。</summary>
        /// <param name="initial">开始执行录制输入之前的完整检查点。</param>
        /// <param name="frames">紧接起点、按帧号递增的输入列表。</param>
        /// <param name="limits">构建这份录制时使用的容量限制。</param>
        internal ReplayRecording(Checkpoint initial, IReadOnlyList<InputFrame> frames, ReplayLimits limits)
        {
            Initial = initial; Limits = limits;
            Frames = new ReadOnlyCollection<InputFrame>(new List<InputFrame>(frames));
        }
        /// <summary>重放前要恢复的起点；允许是非零帧的检查点。</summary>
        public Checkpoint Initial { get; }
        /// <summary>从 Initial.Tick 加 1 开始的连续输入序列。</summary>
        public IReadOnlyList<InputFrame> Frames { get; }
        /// <summary>当前录制对象使用的容量限制。</summary>
        public ReplayLimits Limits { get; }
    }

    /// <summary>逐帧构建录制的容器，只保存输入，不负责推进游戏。</summary>
    /// <remarks>
    /// 调用顺序应为成功 Advance 后再 Append，避免录下没有成功执行的帧。
    /// 推进与追加不是一个原子操作；如果录制不可缺失，调用层应在达到容量前协调轮换，
    /// 不能忽略追加失败。此类没有游戏模块，也不替代游戏输入格式检查。
    /// </remarks>
    public sealed class ReplayRecorder
    {
        // 所有输入都必须紧接这个完整起点，不能独立删除开头的旧输入。
        private readonly Checkpoint _initial;
        // 帧数和字节数同时受限，防止小输入很多或少量输入很大造成无限增长。
        private readonly ReplayLimits _limits;
        // 按帧号递增积累；Build 时复制列表，不把可追加容器暴露给录制对象。
        private readonly List<InputFrame> _frames = new List<InputFrame>();
        // 按实际文件布局累计大小，不需要每追加一帧就重新编码整段录制。
        private long _encodedBytes;
        private MembershipState _membership;

        /// <summary>从完整检查点开始一段新录制，并预先核对起点所需容量。</summary>
        /// <param name="initial">录制起点；可以是初始状态，也可以是中途检查点。</param>
        /// <param name="limits">录制容量限制；为空时使用默认值。</param>
        public ReplayRecorder(Checkpoint initial, ReplayLimits? limits = null)
        {
            _initial = initial ?? throw new ArgumentNullException(nameof(initial));
            _limits = limits ?? new ReplayLimits();
            _membership = initial.Membership;
            // 6 = 标识 4 + 版本 2；两个 4 分别是起点字节长度和输入帧数量。
            // 初始检查点已经有自己的校验摘要，录制外层还需要一个独立摘要。
            _encodedBytes = 6L + 4 + CheckpointCodec.Encode(initial).Length + 4 + DataEnvelope.DigestLength;
            if (_encodedBytes > _limits.MaxBytes)
                throw new InvalidOperationException("Initial checkpoint exceeds the recording budget.");
        }

        /// <summary>追加已成功执行的一帧输入，验证连续性、参与者和容量。</summary>
        /// <param name="frame">下一帧输入；帧号必须紧接录制起点和已有输入。</param>
        /// <remarks>
        /// 格式和容量检查通过后才加入列表。此方法不调用游戏，
        /// 也无法证明外部已经执行 Advance，这一调用约定由使用方遵守。
        /// </remarks>
        public void Append(InputFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (_frames.Count >= _limits.MaxFrames) throw new InvalidOperationException("Recording frame limit reached.");
            // 起点可能是中间帧，不能把每份录制都假设成从帧 1 开始。
            ulong expected = checked(_initial.Tick + (ulong)_frames.Count + 1);
            var session = _initial.Session; var limits = _initial.Limits;
            if (frame.SessionId != session.SessionId || frame.Epoch != session.Epoch || frame.Tick != expected)
                throw new ArgumentException("Recording requires consecutive frames from its initial session.");
            var membership = _membership.Apply(frame.MemberEvents);
            membership.ValidateInputs(frame.Inputs, limits.MaxInputBytes);
            _ = checked(session.BaseTick + frame.Tick);
            // 先计算完整录制的预计大小，再修改列表，拒绝无声截断或淘汰历史。
            long nextBytes = checked(_encodedBytes + InputFormat.EncodedSize(frame));
            if (nextBytes > _limits.MaxBytes) throw new InvalidOperationException("Recording byte limit reached.");
            _frames.Add(frame); _encodedBytes = nextBytes; _membership = membership;
        }

        /// <summary>生成当前录制的只读副本；录制器之后仍可在限制内继续追加。</summary>
        /// <returns>包含当前起点和输入列表的新录制对象。</returns>
        public ReplayRecording Build() => new ReplayRecording(_initial, _frames, _limits);
    }
}

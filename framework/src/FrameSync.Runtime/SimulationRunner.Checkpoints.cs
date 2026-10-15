using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;

namespace FrameSync.Runtime
{
    // SimulationRunner 的保存恢复部分，与另一个同名 partial 声明共享字段和方法。
    public sealed partial class SimulationRunner<TState>
    {
        // 只保存最近 HistoryFrames 帧；保存顺序是从旧到新。
        private Queue<InputFrame> _history = null!;
        // 下标对应 Session.Participants；一次实际提交会把该参与者的连续缺失次数清零。
        private ulong[] _missingCounts = null!;

        /// <summary>捕获当前帧结束后的完整检查点，不向磁盘写文件。</summary>
        /// <returns>与运行中可变数据分离的检查点；Tick 为 0 时表示初始状态。</returns>
        /// <remarks>
        /// 除游戏状态外，还保存初始化参数、近期输入和连续缺失次数。
        /// 捕获时编码一次以验证完整大小；容量超限会立即失败，不返回无法保存的检查点。
        /// </remarks>
        public Checkpoint CaptureCheckpoint()
        {
            EnsureAvailable();
            _busy = true;
            try
            {
                var checkpoint = new Checkpoint(_module.Manifest, Session, Limits, Tick,
                    _history.ToArray(), _missingCounts, CaptureApplication(), _initialization.Data);
                // 容量限制覆盖全部框架字段和校验摘要，不能只检查游戏状态字节。
                _ = CheckpointCodec.Encode(checkpoint);
                return checkpoint;
            }
            finally { _busy = false; }
        }

        /// <summary>在独立的新实例中恢复检查点，检查成功后整体替换当前运行状态。</summary>
        /// <param name="checkpoint">与游戏版本、会话配置和运行限制完全匹配的检查点。</param>
        /// <remarks>
        /// 允许用于故障实例；成功后清除故障标记。恢复失败时保留当前实例，
        /// 前提是游戏工厂和状态方法遵守实例隔离、无外部副作用的接入约定。
        /// 此处恢复的是同一个会话；读档后建立新会话属于后续存档层的职责。
        /// </remarks>
        public void Restore(Checkpoint checkpoint)
        {
            EnsureAvailable(allowFaulted: true);
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            _busy = true;
            try
            {
                // 1. 先检查框架身份与格式，防止把另一个游戏、会话或配置的状态装进来。
                if (!_module.Manifest.Equals(checkpoint.Manifest) ||
                    !CheckpointCodec.SameSession(Session, checkpoint.Session) ||
                    !CheckpointCodec.SameLimits(Limits, checkpoint.Limits))
                    throw new ArgumentException("Checkpoint is incompatible with this module or session.");
                foreach (var frame in checkpoint.InputHistory) ValidateFrame(frame, frame.Tick);
                _ = CheckpointCodec.Encode(checkpoint); // 验证整个检查点的字节预算。
                // 2. 解码后重新编码必须完全一致，拒绝同一状态存在多种字节表达。
                var state = _module.Codec.Decode(checkpoint.ApplicationState.Span);
                if (!_module.Codec.Encode(state).AsSpan().SequenceEqual(checkpoint.ApplicationState.Span))
                    throw new InvalidDataException("Application state is not canonically encoded.");
                // 3. 新实例先初始化，再装入保存状态；任何失败都不会直接改动当前实例。
                var candidate = _module.Factory.Create() ?? throw new InvalidOperationException("Factory returned null.");
                if (ReferenceEquals(candidate, _simulation))
                    throw new InvalidOperationException("Factory must create an isolated simulation.");
                var initialization = new SimulationInitialization(Session, checkpoint.InitializationData);
                candidate.Initialize(initialization);
                candidate.Restore(state);
                // 4. 回读验证已编码字段确实恢复一致。Capture 本身漏掉的游戏字段，
                // 框架无法自动发现，仍需游戏实现和恢复后继续计算的测试共同保证。
                if (!_module.Codec.Encode(candidate.Capture()).AsSpan().SequenceEqual(checkpoint.ApplicationState.Span))
                    throw new InvalidDataException("Restore did not reproduce the complete application state.");
                // 5. 先准备历史和计数的独立容器，最后才提交实例、帧号和故障标记。
                var history = new Queue<InputFrame>(Limits.HistoryFrames);
                foreach (var frame in checkpoint.InputHistory) history.Enqueue(frame);
                var counts = checkpoint.MissingInputCounts.ToArray();
                _simulation = candidate; _history = history; _missingCounts = counts; _initialization = initialization;
                Tick = checkpoint.Tick; IsFaulted = false;
            }
            finally { _busy = false; }
        }
    }
}

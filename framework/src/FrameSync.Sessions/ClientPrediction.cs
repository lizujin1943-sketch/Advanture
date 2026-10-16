using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace FrameSync.Sessions
{
    /// <summary>客户端内部的预测时间线。网络和确认字节去重仍由 ClientSession 管理。</summary>
    /// <remarks>
    /// S[f] 表示第 f 帧执行后的完整状态。第 m 帧有差异时恢复 S[m-1]，重算到原预测位置。
    /// 预测前必须已有该帧的本地冻结输入；无操作也由宿主明确提交中性输入，不能把“尚未采样”当作“无操作”。
    /// 只有其他玩家的未知输入需要预测。服务器最终帧优先，其内容可以与本地已提交的输入不同。
    /// 每次 Poll 同时限制重算和确认工作量；重算跨 Poll 时仍保留上一份完整展示状态。
    /// </remarks>
    internal sealed class ClientPrediction<TState>
    {
        // 当前正在计算的世界；回滚会改变它的帧号，因此不能用它决定何时重新采样本地输入。
        private readonly SimulationRunner<TState> _runner;
        private readonly PredictionOptions _options;
        private readonly ushort _localSlot;
        // 游戏提供的中性输入副本。预测时只用来补远端输入，本地输入始终读取冻结记录。
        private readonly Dictionary<ushort, ByteString> _neutral = new Dictionary<ushort, ByteString>();
        // 只保留 (Confirmed.Tick, runner.Tick]，确认基点另存；最多 MaxPredictionFrames 项。
        private readonly SortedDictionary<ulong, Entry> _executed = new SortedDictionary<ulong, Entry>();
        // 已通知表现层的预测事件；重算后据此决定哪些效果需要撤销或确认。
        private readonly Dictionary<(ulong Tick, int Index), SimulationEvent> _visible = new Dictionary<(ulong, int), SimulationEvent>();
        private Checkpoint _confirmed; // 已用服务器最终输入执行并通过完整摘要校验的状态。
        private Checkpoint _published; // 最近一次完整计算结果；分批重算时保持不变，避免显示半途状态。
        private ulong? _replayTarget; // 回滚后需要重新追赶的位置；缺本地输入或预算不足时留到下次 Poll。

        /// <summary>从已验证的开局状态建立预测时间线，并复制、校验游戏提供的中性输入。</summary>
        internal ClientPrediction(SimulationRunner<TState> runner, Checkpoint initial, PredictionOptions options,
            ushort localSlot, Func<Participant, ByteString> neutralInput, IInputValidator validator)
        {
            _runner = runner; _confirmed = initial; _published = initial; _options = options; _localSlot = localSlot;
            foreach (var p in initial.Membership.Participants)
            {
                var bytes = neutralInput(p);
                if (bytes == null || bytes.Length > initial.Limits.MaxInputBytes) throw new ArgumentException("Invalid neutral prediction input.");
                validator.ValidateCanonical(bytes.Span); _neutral.Add(p.Slot, new ByteString(bytes.Span));
            }
        }
        /// <summary>已确认的完整状态；存档和确认进度以此为准。</summary>
        internal Checkpoint Confirmed => _confirmed;
        /// <summary>可以交给表现层的完整状态，可能暂时领先正在回滚的计算位置。</summary>
        internal Checkpoint Published => _published;
        /// <summary>内部模拟当前计算到的帧号，回滚时允许倒退。</summary>
        internal ulong PredictedTick => _runner.Tick;
        /// <summary>是否仍需重算到回滚前的位置；等待本地冻结输入时也可能为 true。</summary>
        internal bool IsReplaying => _replayTarget.HasValue;
        /// <summary>发现最终帧与已执行帧有差异，并实际恢复旧状态的累计次数。</summary>
        internal ulong RollbackCount { get; private set; }
        /// <summary>最近一次回滚恢复到的帧末位置，等于最早差异帧减一。</summary>
        internal ulong LastRestoreTick { get; private set; }
        /// <summary>最终输入一致但状态摘要不同的诊断信息；这不是可用普通预测回滚掩盖的错误。</summary>
        internal DesyncReport? Desync { get; private set; }

        /// <summary>先纠正差异，再确认或推进帧；单次工作量受 budget 限制。</summary>
        /// <remarks>
        /// localInputs 由 ClientSession 在 TrySubmit 成功后写入，每帧内容不可改写，本类只读取。
        /// 活跃本地玩家缺少冻结输入时，仅暂停尚无最终结果的模拟帧；外层会话仍继续收发网络消息。
        /// 已收到最终帧时直接采用服务器结果；本地未启用或已停用时，不要求它提供输入。
        /// </remarks>
        internal void Poll(ulong target, ulong? closeAt, Dictionary<ulong, BufferedFrame> finalFrames,
            Dictionary<ulong, ByteString> localInputs, int budget,
            Action<Checkpoint, InputFrame, FrameResult> confirm, List<PredictionEvent> events)
        {
            // 只比较已经执行的帧与服务器最终帧。本地输入先冻结再执行，不再存在本地补交后标脏重算的路径。
            // 即使游戏输入字节相同，输入来源或成员事件不同也会影响检查点，仍需从差异前一帧恢复。
            ulong? mismatch = null;
            foreach (var pair in _executed)
                if (finalFrames.TryGetValue(pair.Key, out var final) && !SameFrame(pair.Value.Frame, final.Record.Frame))
                { mismatch = pair.Key; break; }
            if (mismatch.HasValue)
            {
                ulong restore = mismatch.Value - 1;
                ulong previousTarget = Math.Max(_runner.Tick, _replayTarget ?? 0);
                _runner.Restore(restore == _confirmed.Tick ? _confirmed : _executed[restore].Checkpoint);
                foreach (ulong tick in _executed.Keys.Where(t => t > restore).ToArray()) _executed.Remove(tick);
                _replayTarget = closeAt.HasValue ? Math.Min(previousTarget, closeAt.Value) : previousTarget;
                LastRestoreTick = restore; RollbackCount++;
            }
            while (budget > 0)
            {
                ulong nextConfirmed = checked(_confirmed.Tick + 1);
                if (_executed.TryGetValue(nextConfirmed, out var executed) && finalFrames.TryGetValue(nextConfirmed, out var final))
                {
                    if (!SameFrame(executed.Frame, final.Record.Frame)) throw new InvalidOperationException("Unresolved prediction difference.");
                    string actualHash = CheckpointCodec.Hash(executed.Checkpoint);
                    if (actualHash != final.Record.Hash)
                    {
                        Desync = new DesyncReport(_confirmed, final.Record.Frame, executed.Checkpoint, final.Record.Hash, actualHash);
                        throw new InvalidDataException("State hash mismatch at tick " + nextConfirmed + ".");
                    }
                    // 完整状态核对成功后才发布确认事件；不让音效、成就等因重算重复触发。
                    ConfirmEvents(executed.Result, events);
                    _confirmed = executed.Checkpoint; _executed.Remove(nextConfirmed);
                    confirm(_confirmed, executed.Frame, executed.Result); budget--; continue;
                }

                ulong next = checked(_runner.Tick + 1);
                if (closeAt.HasValue && next > closeAt.Value) break;
                if (_runner.Tick - _confirmed.Tick >= (ulong)_options.MaxPredictionFrames) break;
                bool hasFinal = finalFrames.TryGetValue(next, out var authoritative);
                ulong desired = Math.Max(target, _replayTarget ?? 0);
                // 后续最终帧可以提高追赶目标，但填补中间缺口时仍必须满足下方的本地输入门槛。
                // 确认进度不能跨越缺失的最终帧，只能按顺序核对。
                if (finalFrames.Count > 0) desired = Math.Max(desired, finalFrames.Keys.Max());
                if (!hasFinal && next > desired) break;
                var before = _runner.Tick == _confirmed.Tick ? _confirmed : _executed[_runner.Tick].Checkpoint;
                // 宿主必须明确采样这一帧；字典没有记录不代表玩家没有操作。
                // 即使更晚的本地输入已冻结，也不能越过当前缺口。收到最终帧则直接执行服务器结果。
                // 使用当前模拟位置的成员状态判断，保证跨启用/停用事件回滚时也遵循同一规则。
                if (!hasFinal && before.Membership.Find(_localSlot).IsActive && !localInputs.ContainsKey(next))
                    break;
                var frame = hasFinal ? authoritative!.Record.Frame : Predict(next, before.Membership, localInputs);
                var result = _runner.Advance(frame); var checkpoint = _runner.CaptureCheckpoint();
                _executed.Add(next, new Entry(frame, result, checkpoint)); budget--;
            }
            if (_replayTarget.HasValue && _runner.Tick >= _replayTarget.Value) _replayTarget = null;
            if (closeAt == _confirmed.Tick) { DiscardTail(events); return; }
            if (!IsReplaying) Publish(events);
        }

        /// <summary>正常关闭只保留最后确认状态，撤销尚未确认的表现。</summary>
        internal void DiscardTail(List<PredictionEvent> events)
        {
            _runner.Restore(_confirmed); _executed.Clear(); _replayTarget = null;
            foreach (var e in _visible.Values.OrderBy(e => e.Tick).ThenBy(e => e.Index))
                events.Add(new PredictionEvent(PredictionEventKind.Invalidated, e));
            _visible.Clear(); _published = _confirmed;
        }

        /// <summary>组合本地冻结输入与远端预测输入；调用前已由 Poll 检查本地输入是否齐备。</summary>
        private InputFrame Predict(ulong tick, MembershipState membership, Dictionary<ulong, ByteString> localInputs)
        {
            var inputs = new List<RecordedInput>();
            foreach (var p in membership.Participants)
            {
                if (!p.IsActive) continue; // 不预测成员变更，等待服务器明确的事件。
                // 本地直接取已冻结的字节，没有“找不到就补中性”的兜底；远端才使用中性输入预测。
                ByteString bytes = p.Slot == _localSlot ? localInputs[tick] : _neutral[p.Slot];
                // 暂猜远端也正常提交了中性输入，避免所有人空闲时仅因来源不同而回滚。
                // Submitted 在这里是预测假设，不是服务器确认；最终若为 MissingSubstitute，
                // SameFrame 仍会发现来源差异并回滚，纠正缺失计数与输入历史。
                inputs.Add(new RecordedInput(p.Slot, p.Generation, bytes.Span, InputOrigin.Submitted));
            }
            return new InputFrame(_confirmed.Session.SessionId, _confirmed.Session.Epoch, tick, inputs);
        }

        /// <summary>撤销与最终结果不符的已显示事件，再交付本帧的确认通知。</summary>
        private void ConfirmEvents(FrameResult result, List<PredictionEvent> events)
        {
            var actual = result.Events.ToDictionary(e => (e.Tick, e.Index));
            foreach (var key in _visible.Keys.Where(k => k.Tick == result.Tick).ToArray())
            {
                var old = _visible[key];
                if (!actual.TryGetValue(key, out var value) || !SameEvent(old, value))
                    events.Add(new PredictionEvent(PredictionEventKind.Invalidated, old));
                _visible.Remove(key);
            }
            foreach (var e in result.Events) events.Add(new PredictionEvent(PredictionEventKind.Confirmed, e));
        }
        /// <summary>计算追赶完成后，一次性更新表现状态以及新增/失效的预测事件。</summary>
        private void Publish(List<PredictionEvent> events)
        {
            var current = _executed.Values.SelectMany(x => x.Result.Events).ToDictionary(e => (e.Tick, e.Index));
            foreach (var key in _visible.Keys.OrderBy(k => k.Tick).ThenBy(k => k.Index).ToArray())
                if (!current.TryGetValue(key, out var value) || !SameEvent(_visible[key], value))
                {
                    events.Add(new PredictionEvent(PredictionEventKind.Invalidated, _visible[key])); _visible.Remove(key);
                }
            foreach (var e in current.Values.OrderBy(e => e.Tick).ThenBy(e => e.Index))
                if (!_visible.ContainsKey((e.Tick, e.Index)))
                { _visible.Add((e.Tick, e.Index), e); events.Add(new PredictionEvent(PredictionEventKind.Predicted, e)); }
            _published = _runner.Tick == _confirmed.Tick ? _confirmed : _executed[_runner.Tick].Checkpoint;
        }
        // 事件身份相同但内容改变时，也必须撤销旧效果，不能只比较帧号和序号。
        private static bool SameEvent(SimulationEvent a, SimulationEvent b) =>
            a.SessionId == b.SessionId && a.Epoch == b.Epoch && a.Tick == b.Tick && a.Index == b.Index && a.Payload.Span.SequenceEqual(b.Payload.Span);
        /// <summary>比较整帧执行依据，包括输入字节、来源、控制权版本和成员事件。</summary>
        private static bool SameFrame(InputFrame a, InputFrame b)
        {
            if (a.Tick != b.Tick || a.SessionId != b.SessionId || a.Epoch != b.Epoch ||
                a.Inputs.Count != b.Inputs.Count || a.MemberEvents.Count != b.MemberEvents.Count) return false;
            for (int i = 0; i < a.Inputs.Count; i++)
            {
                var x = a.Inputs[i]; var y = b.Inputs[i];
                if (x.Slot != y.Slot || x.Generation != y.Generation || x.Origin != y.Origin || !x.Payload.Span.SequenceEqual(y.Payload.Span)) return false;
            }
            for (int i = 0; i < a.MemberEvents.Count; i++)
            {
                var x = a.MemberEvents[i]; var y = b.MemberEvents[i];
                if (x.Sequence != y.Sequence || x.Kind != y.Kind || x.Slot != y.Slot || x.Generation != y.Generation) return false;
            }
            return true;
        }
        /// <summary>同一帧的执行输入、事件结果与帧末检查点，供差异比较及恢复重算使用。</summary>
        private sealed class Entry
        {
            internal Entry(InputFrame frame, FrameResult result, Checkpoint checkpoint) { Frame = frame; Result = result; Checkpoint = checkpoint; }
            internal InputFrame Frame { get; }
            internal FrameResult Result { get; }
            internal Checkpoint Checkpoint { get; }
        }
    }
}

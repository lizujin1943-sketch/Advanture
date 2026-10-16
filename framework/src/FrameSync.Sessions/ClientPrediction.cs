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
    /// 每次 Poll 同时限制重算和确认工作量；重算跨 Poll 时仍保留上一份完整展示状态。
    /// </remarks>
    internal sealed class ClientPrediction<TState>
    {
        private readonly SimulationRunner<TState> _runner;
        private readonly PredictionOptions _options;
        private readonly ushort _localSlot;
        private readonly Dictionary<ushort, ByteString> _neutral = new Dictionary<ushort, ByteString>();
        // 只保留 (Confirmed.Tick, runner.Tick]，确认基点另存；最多 MaxPredictionFrames 项。
        private readonly SortedDictionary<ulong, Entry> _executed = new SortedDictionary<ulong, Entry>();
        private readonly Dictionary<(ulong Tick, int Index), SimulationEvent> _visible = new Dictionary<(ulong, int), SimulationEvent>();
        private Checkpoint _confirmed, _published;
        private ulong? _replayTarget;
        private readonly HashSet<ulong> _dirtyLocalTicks = new HashSet<ulong>();

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
        internal Checkpoint Confirmed => _confirmed;
        internal Checkpoint Published => _published;
        internal ulong PredictedTick => _runner.Tick;
        internal bool IsReplaying => _replayTarget.HasValue;
        internal ulong RollbackCount { get; private set; }
        internal ulong LastRestoreTick { get; private set; }
        internal DesyncReport? Desync { get; private set; }
        // 输入已经冻结但对应帧曾按中性值预测过，也必须从该帧前重算。
        internal void LocalInputAdded(ulong tick)
        {
            if (tick <= _runner.Tick && tick > _confirmed.Tick)
                _dirtyLocalTicks.Add(tick);
        }

        internal void Poll(ulong target, ulong? closeAt, Dictionary<ulong, BufferedFrame> finalFrames,
            Dictionary<ulong, ByteString> localInputs, int budget,
            Action<Checkpoint, InputFrame, FrameResult> confirm, List<PredictionEvent> events)
        {
            // 先找最早分歧。即使游戏输入字节相同，来源或成员事件不同也会影响检查点。
            ulong? mismatch = null;
            foreach (var pair in _executed)
                if (finalFrames.TryGetValue(pair.Key, out var final) && !SameFrame(pair.Value.Frame, final.Record.Frame))
                { mismatch = pair.Key; break; }
            foreach (ulong tick in _dirtyLocalTicks)
                if (tick > _confirmed.Tick && tick <= _runner.Tick && !finalFrames.ContainsKey(tick))
                    mismatch = !mismatch.HasValue ? tick : Math.Min(mismatch.Value, tick);
            _dirtyLocalTicks.Clear();
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
                // 收到后续确认帧时允许预测填补缺口；最终仍须等待缺失确认帧才能推进确认点。
                if (finalFrames.Count > 0) desired = Math.Max(desired, finalFrames.Keys.Max());
                if (!hasFinal && next > desired) break;
                var before = _runner.Tick == _confirmed.Tick ? _confirmed : _executed[_runner.Tick].Checkpoint;
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
            _runner.Restore(_confirmed); _executed.Clear(); _replayTarget = null; _dirtyLocalTicks.Clear();
            foreach (var e in _visible.Values.OrderBy(e => e.Tick).ThenBy(e => e.Index))
                events.Add(new PredictionEvent(PredictionEventKind.Invalidated, e));
            _visible.Clear(); _published = _confirmed;
        }

        private InputFrame Predict(ulong tick, MembershipState membership, Dictionary<ulong, ByteString> localInputs)
        {
            var inputs = new List<RecordedInput>();
            foreach (var p in membership.Participants)
            {
                if (!p.IsActive) continue; // 不预测成员变更，等待服务器明确的事件。
                bool local = p.Slot == _localSlot && localInputs.TryGetValue(tick, out _);
                ByteString bytes = local ? localInputs[tick] : _neutral[p.Slot];
                inputs.Add(new RecordedInput(p.Slot, p.Generation, bytes.Span,
                    local ? InputOrigin.Submitted : InputOrigin.MissingSubstitute));
            }
            return new InputFrame(_confirmed.Session.SessionId, _confirmed.Session.Epoch, tick, inputs);
        }

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
        private static bool SameEvent(SimulationEvent a, SimulationEvent b) =>
            a.SessionId == b.SessionId && a.Epoch == b.Epoch && a.Tick == b.Tick && a.Index == b.Index && a.Payload.Span.SequenceEqual(b.Payload.Span);
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
        private sealed class Entry
        {
            internal Entry(InputFrame frame, FrameResult result, Checkpoint checkpoint) { Frame = frame; Result = result; Checkpoint = checkpoint; }
            internal InputFrame Frame { get; }
            internal FrameResult Result { get; }
            internal Checkpoint Checkpoint { get; }
        }
    }
}

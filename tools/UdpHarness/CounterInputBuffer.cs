using System;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Samples.Counter;
using FrameSync.Sessions;

namespace FrameSync.Tools.UdpHarness
{
    /// <summary>控制台示例的输入采样器；整数命令与 Counter 编码都属于宿主，不进入帧同步核心。</summary>
    /// <remarks>
    /// 宿主按固定时钟给出本次采样目标，逐帧补齐至该目标。手动模式每次新采样帧取一个命令，
    /// 启动或卡顿后补出的更早帧使用明确的中性输入。已采样但暂时提交失败的帧原样保留。
    /// 所有方法只由宿主主循环串行调用；后台键盘读取不直接操作此对象或会话。
    /// </remarks>
    internal sealed class CounterInputBuffer
    {
        private readonly bool _manual;
        private readonly int _clients, _slot;
        private readonly Queue<int> _commands = new Queue<int>();
        private ulong _nextTick = 1;
        private ByteString? _pending;
        private bool _pendingIsNeutral;

        internal CounterInputBuffer(bool manual, int clients, int slot)
        {
            if (clients < 1 || clients > 4 || slot < 0 || slot >= clients) throw new ArgumentOutOfRangeException(nameof(slot));
            _manual = manual; _clients = clients; _slot = slot;
        }
        /// <summary>未分配到帧的手动命令队列；满时由调用方保留原命令，避免无界积压。</summary>
        internal bool TryEnqueue(int amount)
        {
            if (!_manual || amount < -1000 || amount > 1000) throw new ArgumentOutOfRangeException(nameof(amount));
            if (_commands.Count >= 128) return false;
            _commands.Enqueue(amount); return true;
        }
        /// <summary>会话已成功冻结的帧数；包括没有操作的帧，不把提交失败计为成功。</summary>
        internal ulong FrozenInputCount { get; private set; }
        /// <summary>成功冻结的中性输入数量，供空闲客户端的集成验收使用。</summary>
        internal ulong FrozenNeutralInputCount { get; private set; }
        /// <summary>因对应帧已经确认而丢弃的待提交样本数；这些样本绝不改派到新帧。</summary>
        internal ulong ExpiredInputCount { get; private set; }

        /// <summary>连接实际会话的宿主入口；成功冻结后才发出宿主日志通知。</summary>
        internal void SubmitThrough(ClientSession<CounterState> client, ulong horizon, Action<ulong, ByteString>? submitted = null)
        {
            // 关闭通知可能先于最终帧到达：此时仍 Running，但不能再采样/提交，宿主只继续 Poll。
            if (client.Status != SessionStatus.Running || client.IsClosing || (client.ConfirmedTick != 0 && !client.LocalParticipant!.IsActive)) return;
            SubmitThrough(client.ConfirmedTick, horizon, (tick, input) =>
            {
                if (!client.TrySubmit(tick, input.Span)) return false;
                submitted?.Invoke(tick, input); return true;
            });
        }

        /// <summary>从尚未冻结的位置连续提交到 horizon，遇到发送队列背压就暂停，之后原样重试。</summary>
        /// <param name="confirmedTick">会话的最后确认帧，不能使用会回退的 PredictedTick。</param>
        /// <param name="horizon">固定时钟给出的采样目标；相同目标重复调用不会多采样，也不会覆盖输入。</param>
        /// <param name="submit">接入 ClientSession.TrySubmit；true 表示该输入已被会话冻结。</param>
        internal void SubmitThrough(ulong confirmedTick, ulong horizon, Func<ulong, ByteString, bool> submit)
        {
            if (horizon > confirmedTick && horizon - confirmedTick > 128) throw new ArgumentOutOfRangeException(nameof(horizon));
            if (submit == null) throw new ArgumentNullException(nameof(submit));
            if (_nextTick <= confirmedTick)
            {
                // 服务器已确定旧帧：撤销尚未成功提交的旧样本。未采样的新命令仍留在队列里。
                if (_pending != null) { ExpiredInputCount++; _pending = null; }
                _nextTick = checked(confirmedTick + 1);
            }
            while (_nextTick <= horizon)
            {
                if (_pending == null)
                {
                    int amount = 0;
                    if (_manual)
                    {
                        // 只把当前采样命令放到当前目标帧；更早的补齐帧明确记录无操作。
                        if (_nextTick == horizon && _commands.Count > 0) amount = _commands.Dequeue();
                    }
                    else if (_nextTick % 5 == 0 && (_nextTick / 5 - 1) % (ulong)_clients == (ulong)_slot)
                        amount = ScriptAmount(checked((int)(_nextTick / 5)));
                    _pending = new ByteString(CounterGame.Input(0, amount)); _pendingIsNeutral = amount == 0;
                }
                // _pending 不会因下一次键盘采样、时钟前进或模拟回滚被改写。
                if (!submit(_nextTick, _pending)) break;
                FrozenInputCount++; if (_pendingIsNeutral) FrozenNeutralInputCount++;
                _pending = null; _nextTick = checked(_nextTick + 1);
            }
        }
        internal static int ScriptAmount(int index) => index == 1 ? 5 : index == 2 ? 3 : index % 7;
    }
}

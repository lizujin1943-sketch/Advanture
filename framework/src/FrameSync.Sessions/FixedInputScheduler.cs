using System;

namespace FrameSync.Sessions
{
    /// <summary>宿主提供单调时间的固定帧时钟；估算服务器进度，并选择提前固定帧数的输入目标。</summary>
    /// <remarks>初版不估计网络往返时间。Start 到达延迟可能造成低估，ObserveConfirmed 只向前校准。
    /// 网络较慢时宿主应选择更大的固定提前量；服务器最终仍按自身截止时间判断迟到。</remarks>
    public sealed class FixedInputScheduler
    {
        private readonly uint _tickRate;
        private readonly ulong _lead;
        private long _anchorTime, _lastTime;
        private ulong _anchorTick;
        private bool _started;
        /// <summary>设置每秒逻辑帧数和输入提前量；默认提前 3 帧。</summary>
        public FixedInputScheduler(uint tickRate, int inputLeadFrames = 3)
        {
            if (tickRate < 1 || tickRate > 1000 || inputLeadFrames < 1 || inputLeadFrames > 128)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            _tickRate = tickRate; _lead = (ulong)inputLeadFrames;
        }
        /// <summary>收到开局后建立本地时间基准。一个实例只服务一次会话。</summary>
        public void Start(TimeSpan now, ulong referenceTick = 0)
        {
            if (_started || now.Ticks < 0) throw new InvalidOperationException("Scheduler already started or time is invalid.");
            _anchorTime = _lastTime = now.Ticks; _anchorTick = referenceTick; _started = true;
        }
        /// <summary>把确认进度作为服务器已经到达的下界，必要时将时间基准前移。</summary>
        public void ObserveConfirmed(TimeSpan now, ulong confirmedTick)
        {
            ulong estimate = EstimateTick(now);
            if (confirmedTick > estimate) { _anchorTime = now.Ticks; _anchorTick = confirmedTick; }
        }
        /// <summary>估算当前服务器帧号，使用整数计算，不依赖渲染帧数或本地模拟是否正在回滚。</summary>
        public ulong EstimateTick(TimeSpan now)
        {
            if (!_started) throw new InvalidOperationException("Start the scheduler first.");
            if (now.Ticks < _lastTime) throw new ArgumentOutOfRangeException(nameof(now), "Time must be monotonic.");
            _lastTime = now.Ticks; ulong elapsed = (ulong)(now.Ticks - _anchorTime);
            return checked(_anchorTick + elapsed / (ulong)TimeSpan.TicksPerSecond * _tickRate +
                elapsed % (ulong)TimeSpan.TicksPerSecond * _tickRate / (ulong)TimeSpan.TicksPerSecond);
        }
        /// <summary>选择未来输入帧；至少超过确认点和调用方指定的上次冻结帧，不能覆盖已提交内容。</summary>
        /// <remarks>
        /// 逐帧采样的宿主通常省略 lastSubmittedTick，以返回值作为本次采样上界，并保存自己的 nextInputTick。
        /// 相同时间重复 Poll 时不应强行把上界加一，否则会按渲染循环速度抢先冻结尚未采样的未来输入。
        /// 从 nextInputTick 连续提交到上界；无操作也显式提交中性输入，再请求预测到该上界。
        /// </remarks>
        public ulong InputTick(TimeSpan now, ulong confirmedTick, ulong lastSubmittedTick = 0)
        {
            ObserveConfirmed(now, confirmedTick);
            return Math.Max(checked(EstimateTick(now) + _lead),
                Math.Max(checked(confirmedTick + 1), checked(lastSubmittedTick + 1)));
        }
    }
}

using System;

namespace FrameSync.Runtime
{
    /// <summary>使用正常的推进逻辑重新计算一段录制，不需要专门的游戏重放实现。</summary>
    public static class ReplayPlayer
    {
        /// <summary>恢复录制起点，逐帧执行输入，最后捕获重放结果。</summary>
        /// <typeparam name="TState">运行器所使用的游戏状态类型。</typeparam>
        /// <param name="runner">兼容的运行器；当前状态会先被录制起点替换。</param>
        /// <param name="recording">完整起点和连续输入序列。</param>
        /// <returns>全部录制输入执行结束后的完整检查点，便于与原运行结果比较。</returns>
        /// <remarks>
        /// 事件会随 Step 重新计算，但此方法不派发它们或执行外部回调。
        /// 空录制只恢复起点；中途失败会保留当时的执行进度或故障状态，
        /// 不保证整段播放原子完成，也不会自动退回播放前的状态。需要隔离时使用独立运行器。
        /// </remarks>
        public static Checkpoint Play<TState>(SimulationRunner<TState> runner, ReplayRecording recording)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (recording == null) throw new ArgumentNullException(nameof(recording));
            // 重放只依赖完整起点和输入，不依赖原进程的内存或每帧保存一份游戏状态。
            runner.Restore(recording.Initial);
            foreach (var frame in recording.Frames) runner.Advance(frame);
            return runner.CaptureCheckpoint();
        }
    }
}

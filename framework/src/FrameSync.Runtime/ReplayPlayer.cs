using System;

namespace FrameSync.Runtime
{
    public static class ReplayPlayer
    {
        /// <summary>Restores the start and re-executes inputs. No external event callbacks are invoked.</summary>
        public static Checkpoint Play<TState>(SimulationRunner<TState> runner, ReplayRecording recording)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (recording == null) throw new ArgumentNullException(nameof(recording));
            runner.Restore(recording.Initial);
            foreach (var frame in recording.Frames) runner.Advance(frame);
            return runner.CaptureCheckpoint();
        }
    }
}

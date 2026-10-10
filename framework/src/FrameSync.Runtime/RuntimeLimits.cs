using System;

namespace FrameSync.Runtime
{
    public sealed class RuntimeLimits
    {
        public RuntimeLimits(int maxParticipants = 4, int maxInputBytes = 128,
            int maxCheckpointBytes = 8 * 1024 * 1024, int historyFrames = 128,
            int maxEventsPerFrame = 128, int maxEventBytes = 1024)
        {
            if (maxParticipants < 1 || maxParticipants > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(maxParticipants));
            if (maxInputBytes < 1 || maxInputBytes > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
            if (maxCheckpointBytes < 256) throw new ArgumentOutOfRangeException(nameof(maxCheckpointBytes));
            if (historyFrames < 1 || historyFrames > 128) throw new ArgumentOutOfRangeException(nameof(historyFrames));
            if (maxEventsPerFrame < 0 || maxEventBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxEventsPerFrame));
            MaxParticipants = maxParticipants; MaxInputBytes = maxInputBytes;
            MaxCheckpointBytes = maxCheckpointBytes; HistoryFrames = historyFrames;
            MaxEventsPerFrame = maxEventsPerFrame; MaxEventBytes = maxEventBytes;
        }
        public int MaxParticipants { get; }
        public int MaxInputBytes { get; }
        public int MaxCheckpointBytes { get; }
        public int HistoryFrames { get; }
        public int MaxEventsPerFrame { get; }
        public int MaxEventBytes { get; }
    }
}

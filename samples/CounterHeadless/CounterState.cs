namespace FrameSync.Samples.Counter
{
    public sealed class CounterState
    {
        public CounterState(long total, uint randomState, ulong steps)
        { Total = total; RandomState = randomState; Steps = steps; }
        public long Total { get; }
        public uint RandomState { get; }
        public ulong Steps { get; }
    }
}

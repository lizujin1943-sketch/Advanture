using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FrameSync.Abstractions
{
    public enum InputOrigin : byte { Submitted = 0, MissingSubstitute = 1 }

    public sealed class RecordedInput
    {
        public RecordedInput(ushort slot, ulong generation, ReadOnlySpan<byte> payload,
            InputOrigin origin = InputOrigin.Submitted)
        {
            if (generation == 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (origin != InputOrigin.Submitted && origin != InputOrigin.MissingSubstitute)
                throw new ArgumentOutOfRangeException(nameof(origin));
            Slot = slot; Generation = generation; Payload = new ByteString(payload); Origin = origin;
        }
        public ushort Slot { get; }
        public ulong Generation { get; }
        public ByteString Payload { get; }
        public InputOrigin Origin { get; }
    }

    public sealed class InputFrame
    {
        public InputFrame(Guid sessionId, ulong epoch, ulong tick, IReadOnlyList<RecordedInput> inputs)
        {
            if (sessionId == Guid.Empty) throw new ArgumentException("Missing session identity.");
            if (epoch == 0 || tick == 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (inputs.Count > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(inputs));
            var copy = new RecordedInput[inputs.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = inputs[i] ?? throw new ArgumentException("Null input.");
            Array.Sort(copy, (a, b) => a.Slot.CompareTo(b.Slot));
            for (int i = 1; i < copy.Length; i++)
                if (copy[i - 1].Slot == copy[i].Slot) throw new ArgumentException("Duplicate slot.");
            SessionId = sessionId; Epoch = epoch; Tick = tick;
            Inputs = new ReadOnlyCollection<RecordedInput>(copy);
        }
        public Guid SessionId { get; }
        public ulong Epoch { get; }
        public ulong Tick { get; }
        public IReadOnlyList<RecordedInput> Inputs { get; }
    }

    public sealed class SimulationInput
    {
        public SimulationInput(Participant participant, ByteString payload)
        { Participant = participant; Payload = payload; }
        public Participant Participant { get; }
        public ByteString Payload { get; }
    }

    public readonly struct FrameContext
    {
        public FrameContext(ulong tick, ulong persistentTick, uint tickRate)
        { Tick = tick; PersistentTick = persistentTick; TickRate = tickRate; }
        public ulong Tick { get; }
        public ulong PersistentTick { get; }
        /// <summary>One step is exactly 1 / TickRate seconds. No floating-point accumulated clock.</summary>
        public uint TickRate { get; }
    }

    public sealed class SimulationEvent
    {
        public SimulationEvent(Guid sessionId, ulong epoch, ulong tick, int index, ByteString payload)
        { SessionId = sessionId; Epoch = epoch; Tick = tick; Index = index; Payload = payload; }
        public Guid SessionId { get; }
        public ulong Epoch { get; }
        public ulong Tick { get; }
        public int Index { get; }
        public ByteString Payload { get; }
    }

    public sealed class FrameResult
    {
        public FrameResult(ulong tick, IReadOnlyList<SimulationEvent> events)
        { Tick = tick; Events = events; }
        public ulong Tick { get; }
        public IReadOnlyList<SimulationEvent> Events { get; }
    }
}

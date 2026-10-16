using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace FrameSync.Tests
{
    internal sealed class TestState
    {
        public long Value { get; set; }
        public ulong LastTick { get; set; }
        public ulong LastPersistentTick { get; set; }
        public uint TickRate { get; set; }
    }

    internal sealed class TestModule : ISimulationFactory<TestState>, IStateCodec<TestState>, IInputValidator
    {
        public bool ThrowOnStep { get; set; }
        public bool ThrowOnRestore { get; set; }
        public Action? OnStep { get; set; }
        public Action<FrameContext>? OnFrame { get; set; }
        public Action<SimulationInitialization>? OnInitialize { get; set; }
        public int EventCount { get; set; } = 1;
        public SimulationModule<TestState> Module(string version = "1") =>
            new SimulationModule<TestState>(new CompatibilityManifest("test-decimal", version), this, this, this);
        public IDeterministicSimulation<TestState> Create() => new Model(this);
        public void ValidateCanonical(ReadOnlySpan<byte> bytes)
        { if (bytes.Length != 1 || bytes[0] > 9) throw new ArgumentException("Expected one decimal digit."); }
        public byte[] Encode(TestState state)
        {
            var bytes = new byte[28];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, state.Value);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), state.LastTick);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), state.LastPersistentTick);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), state.TickRate);
            return bytes;
        }
        public TestState Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 28) throw new ArgumentException("Invalid test state.");
            return new TestState { Value = BinaryPrimitives.ReadInt64LittleEndian(bytes),
                LastTick = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8)),
                LastPersistentTick = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16)),
                TickRate = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(24)) };
        }
        private sealed class Model : IDeterministicSimulation<TestState>
        {
            private readonly TestModule _owner;
            private TestState _state = new TestState();
            public Model(TestModule owner) { _owner = owner; }
            public void Initialize(SimulationInitialization initialization)
            { _owner.OnInitialize?.Invoke(initialization); _state = new TestState(); }
            public IReadOnlyList<ByteString> Step(FrameContext context, IReadOnlyList<SimulationInput> inputs)
            {
                foreach (var input in inputs) _state.Value = checked(_state.Value * 10 + input.Payload.Span[0]);
                _state.LastTick = context.Tick; _state.LastPersistentTick = context.PersistentTick;
                _state.TickRate = context.TickRate;
                _owner.OnFrame?.Invoke(context);
                _owner.OnStep?.Invoke();
                if (_owner.ThrowOnStep) throw new InvalidOperationException("Intentional game fault.");
                var events = new ByteString[_owner.EventCount];
                for (int i = 0; i < events.Length; i++) events[i] = new ByteString(new byte[] { (byte)i });
                return events;
            }
            public TestState Capture() => _owner.Decode(_owner.Encode(_state));
            public void Restore(TestState state)
            {
                if (_owner.ThrowOnRestore) throw new InvalidOperationException("Intentional restore fault.");
                _state = _owner.Decode(_owner.Encode(state));
            }
        }
    }

    internal static class Fixture
    {
        public static readonly Guid SessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        public static readonly Participant[] Members = {
            new Participant(0, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")),
            new Participant(1, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002")) };
        public static SessionDescriptor Session(ulong baseTick = 0) => new SessionDescriptor(SessionId, 1, 30, Members, baseTick);
        public static InputFrame Frame(ulong tick, params RecordedInput[] inputs) => new InputFrame(SessionId, 1, tick, inputs);
        public static RecordedInput Input(ushort slot, byte value, InputOrigin origin = InputOrigin.Submitted) =>
            new RecordedInput(slot, 1, new byte[] { value }, origin);
        public static SimulationRunner<TestState> Runner(TestModule? module = null, RuntimeLimits? limits = null) =>
            new SimulationRunner<TestState>((module ?? new TestModule()).Module(), Session(), limits);
    }
}

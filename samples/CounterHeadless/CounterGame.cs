using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FrameSync.Abstractions;

namespace FrameSync.Samples.Counter
{
    public sealed class CounterGame : ISimulationFactory<CounterState>, IStateCodec<CounterState>, IInputValidator
    {
        public static SimulationModule<CounterState> CreateModule()
        {
            var game = new CounterGame();
            return new SimulationModule<CounterState>(new CompatibilityManifest("sample-counter", determinismHash: "xorshift32-v1"), game, game, game);
        }
        public static byte[] Input(byte command, int amount = 0)
        {
            var bytes = new byte[5]; bytes[0] = command;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), amount); return bytes;
        }
        public IDeterministicSimulation<CounterState> Create() => new Model();
        public void ValidateCanonical(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 5) throw new ArgumentException("Counter input must contain five bytes.");
            int amount = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(1));
            if (bytes[0] > 1 || (bytes[0] == 1 && amount != 0) || amount < -1000 || amount > 1000)
                throw new ArgumentException("Expected Add(-1000..1000) or RandomAdd(0).");
        }
        public byte[] Encode(CounterState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.RandomState == 0) throw new InvalidDataException("Random state cannot be zero.");
            var bytes = new byte[20];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, state.Total);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), state.RandomState);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12), state.Steps);
            return bytes;
        }
        public CounterState Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 20) throw new InvalidDataException("Counter state must contain twenty bytes.");
            uint random = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8));
            if (random == 0) throw new InvalidDataException("Random state cannot be zero.");
            return new CounterState(BinaryPrimitives.ReadInt64LittleEndian(bytes), random,
                BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(12)));
        }
        private sealed class Model : IDeterministicSimulation<CounterState>
        {
            private long _total;
            private uint _random;
            private ulong _steps;
            public void Initialize(SimulationInitialization initialization)
            {
                if (initialization.Data.Length != 0 && initialization.Data.Length != 4)
                    throw new ArgumentException("Initialization must be empty or a four-byte seed.");
                _random = initialization.Data.Length == 0 ? 0x12345678U : BinaryPrimitives.ReadUInt32LittleEndian(initialization.Data.Span);
                if (_random == 0) throw new ArgumentException("Seed cannot be zero.");
                _total = 0; _steps = 0;
            }
            public IReadOnlyList<ByteString> Step(FrameContext context, IReadOnlyList<SimulationInput> inputs)
            {
                long before = _total;
                foreach (var input in inputs)
                {
                    if (input.Payload.Span[0] == 0)
                        _total = checked(_total + BinaryPrimitives.ReadInt32LittleEndian(input.Payload.Span.Slice(1)));
                    else
                    {
                        // Explicit uint arithmetic makes the sequence independent of engine and platform.
                        _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
                        _total = checked(_total + _random % 10);
                    }
                }
                _steps = checked(_steps + 1);
                if (_total == before) return Array.Empty<ByteString>();
                var bytes = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(bytes, _total);
                return new[] { new ByteString(bytes) };
            }
            public CounterState Capture() => new CounterState(_total, _random, _steps);
            public void Restore(CounterState state)
            {
                if (state.RandomState == 0) throw new InvalidDataException("Random state cannot be zero.");
                _total = state.Total; _random = state.RandomState; _steps = state.Steps;
            }
        }
    }
}

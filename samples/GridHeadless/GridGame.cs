using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;

namespace FrameSync.Samples.Grid
{
    public sealed class GridGame : ISimulationFactory<GridState>, IStateCodec<GridState>, IInputValidator
    {
        public static SimulationModule<GridState> CreateModule()
        {
            var game = new GridGame();
            return new SimulationModule<GridState>(new CompatibilityManifest("sample-grid"), game, game, game);
        }
        public static byte[] Input(sbyte x, sbyte y) => new byte[] { unchecked((byte)x), unchecked((byte)y) };
        public IDeterministicSimulation<GridState> Create() => new Model();
        public void ValidateCanonical(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 2) throw new ArgumentException("Grid input must contain two signed bytes.");
            int x = unchecked((sbyte)bytes[0]), y = unchecked((sbyte)bytes[1]);
            if (x < -1 || x > 1 || y < -1 || y > 1) throw new ArgumentException("Each direction must be -1, 0 or 1.");
        }
        public byte[] Encode(GridState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var bytes = new byte[checked(10 + 24 * state.Points.Count)];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, state.Steps);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), checked((ushort)state.Points.Count));
            int offset = 10;
            foreach (var point in state.Points)
            {
                point.ParticipantId.ToByteArray().CopyTo(bytes, offset);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 16), point.X);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 20), point.Y);
                offset += 24;
            }
            return bytes;
        }
        public GridState Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 10) throw new InvalidDataException("Truncated grid state.");
            int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8));
            if (count == 0 || bytes.Length != 10 + 24 * count) throw new InvalidDataException("Invalid grid state size.");
            var points = new GridPoint[count];
            Guid previous = Guid.Empty;
            for (int i = 0; i < count; i++)
            {
                int offset = 10 + 24 * i;
                var id = new Guid(bytes.Slice(offset, 16));
                if (id == Guid.Empty || (i > 0 && previous.CompareTo(id) >= 0))
                    throw new InvalidDataException("Grid identities must be unique and sorted.");
                points[i] = new GridPoint(id, BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 16)),
                    BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset + 20)));
                previous = id;
            }
            return new GridState(BinaryPrimitives.ReadUInt64LittleEndian(bytes), points);
        }
        private sealed class Model : IDeterministicSimulation<GridState>
        {
            private Dictionary<Guid, GridPoint> _points = new Dictionary<Guid, GridPoint>();
            private ulong _steps;
            public void Initialize(SimulationInitialization initialization)
            {
                if (initialization.Data.Length != 0) throw new ArgumentException("Grid has no initialization data.");
                _points = initialization.Session.Participants.ToDictionary(p => p.ParticipantId, p => new GridPoint(p.ParticipantId, 0, 0));
                _steps = 0;
            }
            public IReadOnlyList<ByteString> Step(FrameContext context, IReadOnlyList<SimulationInput> inputs)
            {
                var events = new List<ByteString>();
                foreach (var input in inputs)
                {
                    var before = _points[input.Participant.ParticipantId];
                    int dx = unchecked((sbyte)input.Payload.Span[0]), dy = unchecked((sbyte)input.Payload.Span[1]);
                    var after = new GridPoint(before.ParticipantId, checked(before.X + dx), checked(before.Y + dy));
                    _points[after.ParticipantId] = after;
                    if (dx == 0 && dy == 0) continue;
                    var bytes = new byte[24]; after.ParticipantId.ToByteArray().CopyTo(bytes, 0);
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), after.X);
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), after.Y);
                    events.Add(new ByteString(bytes));
                }
                _steps = checked(_steps + 1);
                return events;
            }
            public GridState Capture() => new GridState(_steps, _points.Values.ToArray());
            public void Restore(GridState state)
            {
                if (state.Points.Count != _points.Count || state.Points.Any(p => !_points.ContainsKey(p.ParticipantId)))
                    throw new InvalidDataException("Grid state belongs to different participants.");
                _points = state.Points.ToDictionary(p => p.ParticipantId); _steps = state.Steps;
            }
        }
    }
}

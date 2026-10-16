using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;
using FrameSync.Transport;

namespace FrameSync.Tests
{
    internal static class SessionTests
    {
        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("session.waits_for_every_ready_client", WaitForReady);
            yield return ("session.a_input_executes_on_b", InputRoundTrip);
            yield return ("session.same_frame_inputs_have_canonical_order", InputOrder);
            yield return ("session.deadline_equal_is_late", Deadline);
            yield return ("session.submission_does_not_advance_any_world", NoPrediction);
            yield return ("session.initialization_bytes_reach_each_world", InitialState);
            yield return ("session.close_stops_at_same_frame", Close);
        }
        private static void WaitForReady()
        {
            var f = new SyncFixture();
            for (int i = 0; i < 20; i++) { f.Server.Poll(TimeSpan.Zero); f.A.Poll(); }
            Assert.Equal(SessionStatus.Preparing, f.Server.Status);
            Assert.Equal(0UL, f.Server.ConfirmedTick);
            Assert.Equal(SessionStatus.Ready, f.A.Status);
            f.Start(); Assert.Equal(SessionStatus.Running, f.B.Status);
        }
        private static void InputRoundTrip()
        {
            var f = new SyncFixture(); f.Start();
            Assert.True(f.A.TrySubmit(1, new byte[] { 5 }));
            f.Accept(); f.Finish(1);
            Assert.Equal(5L, f.B.InspectState());
            Assert.Equal((byte)5, f.B.LastFrame!.Inputs[0].Payload.Span[0]);
            Assert.Equal(InputOrigin.Submitted, f.B.LastFrame.Inputs[0].Origin);
            Assert.Equal(InputOrigin.MissingSubstitute, f.B.LastFrame.Inputs[1].Origin);
            f.AssertEqual(1, 5);
            Assert.True(f.B.TrySubmit(2, new byte[] { 3 })); f.Accept(); f.Finish(2); f.AssertEqual(2, 8);
        }
        private static void InputOrder()
        {
            var f = new SyncFixture(); f.Start();
            Assert.True(f.B.TrySubmit(1, new byte[] { 7 })); Assert.True(f.A.TrySubmit(1, new byte[] { 2 }));
            f.Accept(); f.Finish(1); f.AssertEqual(1, 9);
            Assert.Equal((ushort)0, f.B.LastFrame!.Inputs[0].Slot);
            Assert.Equal((byte)2, f.B.LastFrame.Inputs[0].Payload.Span[0]);
        }
        private static void Deadline()
        {
            var f = new SyncFixture(); f.Start();
            Assert.True(f.A.TrySubmit(1, new byte[] { 5 })); f.A.Poll();
            f.Finish(1); f.AssertEqual(1, 0); // 100ms 才处理到输入，已等于截止时间。
            Assert.Equal(InputOrigin.MissingSubstitute, f.B.LastFrame!.Inputs[0].Origin);
            Assert.True(f.A.TrySubmit(2, new byte[] { 3 })); f.Accept(); f.Finish(2); f.AssertEqual(2, 3);
        }
        private static void NoPrediction()
        {
            var f = new SyncFixture(); f.Start();
            Assert.True(f.A.TrySubmit(1, new byte[] { 5 })); f.Accept();
            f.AssertEqual(0, 0); Assert.True(f.A.LastFrame == null);
            f.Finish(1); f.AssertEqual(1, 5);
        }
        private static void InitialState()
        {
            var f = new SyncFixture(initial: 42); f.Start(); f.AssertEqual(0, 42);
            Assert.True(f.A.TrySubmit(1, new byte[] { 5 })); f.Accept(); f.Finish(1); f.AssertEqual(1, 47);
        }
        private static void Close()
        {
            var f = new SyncFixture(); f.Start(); f.Finish(1);
            f.Server.Close(); f.Server.Poll(TimeSpan.FromSeconds(5)); f.A.Poll(); f.B.Poll();
            Assert.Equal(SessionStatus.Closed, f.Server.Status); Assert.Equal(SessionStatus.Closed, f.B.Status);
            f.AssertEqual(1, 0);
        }
    }

    // 测试自己的极小求和规则；框架测试不引用 samples，更不引用 Unity。
    internal sealed class SyncTestGame : ISimulationFactory<long>, IStateCodec<long>, IInputValidator
    {
        public bool ThrowOnStep { get; set; }
        public bool ThrowOnCaptureOfFive { get; set; }
        public long Bias { get; set; }
        public bool EventFromValue { get; set; }
        public SimulationModule<long> Module() => new SimulationModule<long>(new CompatibilityManifest("sync-test"), this, this, this);
        public IDeterministicSimulation<long> Create() => new Model(this);
        public byte[] Encode(long state) { if (ThrowOnCaptureOfFive && state == 5) throw new InvalidOperationException("Intentional codec failure."); var b = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, state); return b; }
        public long Decode(ReadOnlySpan<byte> bytes) { if (bytes.Length != 8) throw new ArgumentException("Eight bytes required."); return BinaryPrimitives.ReadInt64LittleEndian(bytes); }
        public void ValidateCanonical(ReadOnlySpan<byte> bytes) { if (bytes.Length != 1 || bytes[0] > 10) throw new ArgumentException("Input must be 0..10."); }
        private sealed class Model : IDeterministicSimulation<long>
        {
            private long _value;
            private readonly SyncTestGame _owner;
            public Model(SyncTestGame owner) { _owner = owner; }
            public void Initialize(SimulationInitialization initialization) { _value = initialization.Data.Length == 0 ? 0 : BinaryPrimitives.ReadInt64LittleEndian(initialization.Data.Span); }
            public IReadOnlyList<ByteString> Step(FrameContext context, IReadOnlyList<SimulationInput> inputs)
            {
                foreach (var input in inputs) _value += input.Payload.Span[0];
                _value += _owner.Bias;
                if (_owner.ThrowOnStep) throw new InvalidOperationException("Intentional Step failure after mutation.");
                return new[] { new ByteString(new byte[] { _owner.EventFromValue ? (byte)_value : (byte)42 }) };
            }
            public long Capture() => _value;
            public void Restore(long state) { _value = state; }
        }
    }
    internal sealed class SyncFixture
    {
        public readonly InMemoryNetwork Network;
        public readonly ITransport ServerTransport, ATransport, BTransport;
        public readonly ServerSession<long> Server;
        public readonly ClientSession<long> A, B;
        public TimeSpan Now;
        public SyncFixture(long initial = 0, SessionOptions? options = null, InMemoryNetwork? network = null, SessionOptions? clientOptions = null, SyncTestGame? serverGame = null, SyncTestGame? clientBGame = null, bool initiallyActive = true, PredictionOptions? prediction = null, SyncTestGame? clientAGame = null)
        {
            Network = network ?? new InMemoryNetwork(); ServerTransport = Network.CreateEndpoint(1);
            ATransport = Network.CreateEndpoint(2); BTransport = Network.CreateEndpoint(3);
            var game = serverGame ?? new SyncTestGame(); var members = Array.ConvertAll(Fixture.Members, p => new Participant(p.Slot, p.ParticipantId, p.Generation, initiallyActive));
            var session = new SessionDescriptor(Fixture.SessionId, 1, 10, members);
            Server = new ServerSession<long>(game.Module(), session, ServerTransport,
                new[] { new PeerBinding(2, 0), new PeerBinding(3, 1) }, _ => new ByteString(new byte[] { 0 }),
                initialization: new ByteString(game.Encode(initial)), options: options);
            A = new ClientSession<long>((clientAGame ?? new SyncTestGame()).Module(), session.SessionId, session.Epoch, ATransport, 1, options: clientOptions ?? options, prediction: prediction,
                neutralInput: prediction == null ? null : _ => new ByteString(new byte[] { 0 }));
            B = new ClientSession<long>((clientBGame ?? new SyncTestGame()).Module(), session.SessionId, session.Epoch, BTransport, 1, options: clientOptions ?? options);
        }
        public void Start()
        {
            for (int i = 0; i < 100 && (A.Status != SessionStatus.Running || B.Status != SessionStatus.Running); i++)
            { Server.Poll(TimeSpan.Zero); A.Poll(); B.Poll(); }
            Assert.Equal(SessionStatus.Running, Server.Status);
            Assert.Equal(SessionStatus.Running, A.Status); Assert.Equal(SessionStatus.Running, B.Status);
        }
        public void Accept() { A.Poll(); B.Poll(); Server.Poll(Now); }
        public void Finish(ulong tick)
        { Now = TimeSpan.FromTicks(checked((long)tick * TimeSpan.TicksPerSecond / 10)); Server.Poll(Now); A.Poll(); B.Poll(); }
        public void AssertEqual(ulong tick, long value)
        {
            Assert.Equal(tick, Server.ConfirmedTick); Assert.Equal(tick, A.ConfirmedTick); Assert.Equal(tick, B.ConfirmedTick);
            Assert.Equal(value, Server.InspectState()); Assert.Equal(value, A.InspectState()); Assert.Equal(value, B.InspectState());
            byte[] expected = CheckpointCodec.Encode(Server.CaptureCheckpoint());
            Assert.Bytes(expected, CheckpointCodec.Encode(A.CaptureCheckpoint())); Assert.Bytes(expected, CheckpointCodec.Encode(B.CaptureCheckpoint()));
        }
    }
}

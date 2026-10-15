using System;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;
using FrameSync.Transport;

namespace FrameSync.Tests
{
    internal static class SessionBoundaryTests
    {
        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("session.boundary.ready_requires_sent_snapshot", PrematureReady);
            yield return ("session.boundary.slow_client_limits_server_progress", SlowClient);
            yield return ("session.boundary.duplicates_freeze_first_input", InputDuplicates);
            yield return ("session.boundary.rejects_wrong_identity_and_window", Identity);
            yield return ("session.boundary.local_input_is_copied_and_immutable", LocalInput);
            yield return ("session.boundary.clock_is_monotonic_and_work_is_bounded", Clock);
            yield return ("session.boundary.initial_snapshot_is_chunked_under_backpressure", Chunked);
            yield return ("session.boundary.prepare_timeout_fails_room", Timeout);
            yield return ("session.boundary.conflicting_final_fails_without_reexecution", ConflictingFinal);
            yield return ("session.boundary.hash_mismatch_does_not_confirm_or_publish_events", HashMismatch);
            yield return ("session.boundary.out_of_order_final_waits_for_gap", FrameGap);
            yield return ("session.boundary.close_waits_for_final_frame", CloseBeforeFinal);
            yield return ("session.boundary.truncated_packet_cannot_crash_server", Malformed);
        }
        private static void PrematureReady()
        {
            var f = new SyncFixture(); string hash = CheckpointCodec.Hash(f.Server.CaptureCheckpoint());
            Send(f.ATransport, 1, MessageKind.Ready, Wire.Ready(Fixture.SessionId, 1, hash, 128));
            Send(f.BTransport, 1, MessageKind.Ready, Wire.Ready(Fixture.SessionId, 1, hash, 128));
            f.Server.Poll(TimeSpan.Zero);
            Assert.Equal(SessionStatus.Preparing, f.Server.Status);
            Assert.Equal(2UL, f.Server.RejectedMessages);
        }
        private static void SlowClient()
        {
            var f = new SyncFixture(options: new SessionOptions(maxFutureFrames: 8), clientOptions: new SessionOptions(maxFutureFrames: 2));
            f.Start();
            // A 继续轮询，B 暂停。服务器最多领先 B 两帧，不能靠无限排队维持运行。
            for (int i = 1; i <= 10; i++) { f.Server.Poll(TimeSpan.FromMilliseconds(i * 100)); f.A.Poll(); }
            Assert.Equal(2UL, f.Server.ConfirmedTick); Assert.Equal(0UL, f.B.ConfirmedTick);
            for (int i = 0; i < 20; i++) { f.A.Poll(); f.B.Poll(); f.Server.Poll(TimeSpan.FromSeconds(1)); }
            f.A.Poll(); f.B.Poll(); f.AssertEqual(10, 0);
        }
        private static void InputDuplicates()
        {
            var f = new SyncFixture(); f.Start();
            RawInput(f, 1, 0, 1, 5); RawInput(f, 1, 0, 1, 5); RawInput(f, 1, 0, 1, 7);
            f.Server.Poll(TimeSpan.Zero); Assert.Equal(1UL, f.Server.DuplicateInputs); Assert.Equal(1UL, f.Server.RejectedMessages);
            f.Finish(1); f.AssertEqual(1, 5);
        }
        private static void Identity()
        {
            var f = new SyncFixture(); f.Start();
            RawInput(f, 1, 1, 1, 5); RawInput(f, 1, 0, 2, 5); RawInput(f, 129, 0, 1, 5);
            RawInput(f, 1, 0, 1, 5, epoch: 2); RawInput(f, 1, 0, 1, 5, id: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"));
            f.Server.Poll(TimeSpan.Zero); Assert.Equal(5UL, f.Server.RejectedMessages); f.Finish(1); f.AssertEqual(1, 0);
        }
        private static void LocalInput()
        {
            var f = new SyncFixture(); f.Start(); byte[] bytes = { 5 };
            Assert.True(f.A.TrySubmit(1, bytes)); bytes[0] = 7;
            Assert.True(f.A.TrySubmit(1, new byte[] { 5 }));
            Assert.Throws<InvalidOperationException>(() => f.A.TrySubmit(1, new byte[] { 7 }));
            Assert.Throws<ArgumentException>(() => f.A.TrySubmit(2, new byte[] { 99 }));
            f.Accept(); f.Finish(1); f.AssertEqual(1, 5);
        }
        private static void Clock()
        {
            var f = new SyncFixture(options: new SessionOptions(maxFramesPerPoll: 2)); f.Start();
            var results = f.Server.Poll(TimeSpan.FromSeconds(1)); Assert.Equal(2, results.Count); Assert.Equal(2UL, f.Server.ConfirmedTick);
            Assert.Throws<ArgumentOutOfRangeException>(() => f.Server.Poll(TimeSpan.Zero));
        }
        private static void Chunked()
        {
            var f = new SyncFixture(initial: 42, options: new SessionOptions(snapshotChunkBytes: 32),
                network: new InMemoryNetwork(maxQueuedMessages: 1));
            f.Start(); f.AssertEqual(0, 42);
            f.A.TrySubmit(1, new byte[] { 5 }); f.Accept(); f.Finish(1);
            for (int i = 0; i < 10; i++) { f.Server.Poll(f.Now); f.A.Poll(); f.B.Poll(); }
            f.AssertEqual(1, 47);
        }
        private static void Timeout()
        {
            var f = new SyncFixture(); f.Server.Poll(TimeSpan.Zero); f.A.Poll();
            f.Server.Poll(TimeSpan.FromSeconds(10)); Assert.Equal(SessionStatus.Failed, f.Server.Status);
            Assert.Equal(0UL, f.Server.ConfirmedTick);
        }
        private static void ConflictingFinal()
        {
            var f = new SyncFixture(); f.Start(); f.Finish(1);
            byte[] good = Wire.Final(f.Server.LastFrame!, CheckpointCodec.Hash(f.Server.CaptureCheckpoint()), 900);
            Send(f.ServerTransport, 3, MessageKind.Final, good); f.B.Poll();
            Assert.Equal(SessionStatus.Running, f.B.Status); Assert.Equal(1UL, f.B.ConfirmedTick);
            good[good.Length - 1] ^= 1;
            Send(f.ServerTransport, 3, MessageKind.Final, good); f.B.Poll();
            Assert.Equal(SessionStatus.Failed, f.B.Status); Assert.Equal(1UL, f.B.ConfirmedTick);
        }
        private static void HashMismatch()
        {
            var f = new SyncFixture(); f.Start();
            var frame = new InputFrame(Fixture.SessionId, 1, 1, new[] {
                new RecordedInput(0, 1, new byte[] { 5 }), new RecordedInput(1, 1, new byte[] { 0 }) });
            Send(f.ServerTransport, 3, MessageKind.Final, Wire.Final(frame, new string('0', 64), 900));
            var results = f.B.Poll(); Assert.Equal(0, results.Count);
            Assert.Equal(SessionStatus.Failed, f.B.Status); Assert.Equal(0UL, f.B.ConfirmedTick); Assert.Equal(0L, f.B.InspectState());
        }
        private static void FrameGap()
        {
            var f = new SyncFixture(); f.Start();
            f.Server.Poll(TimeSpan.FromMilliseconds(100));
            Assert.True(f.BTransport.TryReceive(out var first));
            f.Server.Poll(TimeSpan.FromMilliseconds(200));
            f.B.Poll(); Assert.Equal(0UL, f.B.ConfirmedTick);
            Send(f.ServerTransport, 3, MessageKind.Final, first!.Payload.ToArray());
            f.B.Poll(); Assert.Equal(2UL, f.B.ConfirmedTick);
            Assert.Bytes(CheckpointCodec.Encode(f.Server.CaptureCheckpoint()), CheckpointCodec.Encode(f.B.CaptureCheckpoint()));
        }
        private static void CloseBeforeFinal()
        {
            var f = new SyncFixture(); f.Start(); f.Server.Poll(TimeSpan.FromMilliseconds(100));
            Assert.True(f.BTransport.TryReceive(out var frame));
            Send(f.ServerTransport, 3, MessageKind.Close, Wire.Terminal(MessageKind.Close, Fixture.SessionId, 1, 1));
            f.B.Poll(); Assert.Equal(SessionStatus.Running, f.B.Status);
            Send(f.ServerTransport, 3, MessageKind.Final, frame!.Payload.ToArray());
            f.B.Poll(); Assert.Equal(SessionStatus.Closed, f.B.Status); Assert.Equal(1UL, f.B.ConfirmedTick);
        }
        private static void Malformed()
        {
            var f = new SyncFixture(); f.Start();
            Assert.True(f.ATransport.TrySend(1, 0, DeliveryMode.ReliableOrdered, new byte[] { 1, 2, 3 }));
            f.Server.Poll(TimeSpan.Zero); Assert.Equal(SessionStatus.Running, f.Server.Status); Assert.Equal(1UL, f.Server.RejectedMessages);
            f.Finish(1); f.AssertEqual(1, 0);
        }
        internal static void RawInput(SyncFixture f, ulong tick, ushort slot, ulong generation, byte value, ulong epoch = 1, Guid? id = null)
        { Send(f.ATransport, 1, MessageKind.Input, Wire.Input(id ?? Fixture.SessionId, epoch, tick, new RecordedInput(slot, generation, new byte[] { value }), 900)); }
        internal static void Send(ITransport transport, ulong destination, MessageKind kind, byte[] bytes)
        { Assert.True(transport.TrySend(destination, Wire.Channel(kind), DeliveryMode.ReliableOrdered, bytes)); }
    }
}

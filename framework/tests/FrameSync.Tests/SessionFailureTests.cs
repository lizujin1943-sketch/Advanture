using System;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;
using FrameSync.Transport;

namespace FrameSync.Tests
{
    internal static class SessionFailureTests
    {
        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("session.failure.close_before_snapshot_same_poll", () => CloseBeforeSnapshot(false));
            yield return ("session.failure.close_before_snapshot_separate_polls", () => CloseBeforeSnapshot(true));
            yield return ("session.failure.partial_step_never_confirms", StepFailure);
            yield return ("session.failure.checkpoint_failure_never_confirms", CaptureFailure);
            yield return ("session.failure.divergent_client_keeps_last_good_checkpoint", Divergence);
            yield return ("session.failure.corrupt_initial_snapshot_does_not_ready", CorruptSnapshot);
            yield return ("session.failure.oversized_snapshot_declaration_is_rejected", OversizedSnapshot);
            yield return ("session.failure.unknown_sender_and_old_session_are_ignored", UnknownSender);
            yield return ("session.failure.bad_capacity_rejected_before_start", Capacity);
            yield return ("session.flow.one_through_four_clients", Participants);
            yield return ("session.flow.events_delivered_once_after_confirmation", Events);
            yield return ("session.flow.backpressure_preserves_confirmed_inputs", Backpressure);
        }
        private static void CloseBeforeSnapshot(bool separatePolls)
        {
            var f = new SyncFixture(); f.Server.Poll(TimeSpan.Zero);
            Assert.True(f.ATransport.TryReceive(out var snapshot));
            f.Server.Close(); f.Server.Poll(TimeSpan.Zero);
            // 把通道 3 的快照延后到通道 1 的 Close 后；每个通道内部仍然有序。
            if (separatePolls)
            {
                Assert.Equal(0, f.A.Poll().Count);
                Assert.Equal(SessionStatus.Closed, f.A.Status);
            }
            SessionBoundaryTests.Send(f.ServerTransport, 2, MessageKind.Snapshot, snapshot!.Payload.ToArray());
            Assert.Equal(0, f.A.Poll().Count);
            Assert.Equal(SessionStatus.Closed, f.A.Status);
            Assert.True(f.A.Failure == null, "Normal close must not become a protocol failure.");
            Assert.Equal(0UL, f.A.ConfirmedTick);
        }
        private static void StepFailure()
        {
            var f = new SyncFixture(serverGame: new SyncTestGame { ThrowOnStep = true }); f.Start();
            f.A.TrySubmit(1, new byte[] { 5 }); f.Accept();
            var results = f.Server.Poll(TimeSpan.FromMilliseconds(100));
            Assert.Equal(0, results.Count); Assert.Equal(SessionStatus.Failed, f.Server.Status);
            Assert.Equal(0UL, f.Server.ConfirmedTick); Assert.Equal(0L, f.Server.InspectState());
            f.Server.Poll(TimeSpan.FromMilliseconds(100)); f.A.Poll(); f.B.Poll();
            Assert.Equal(SessionStatus.Failed, f.B.Status); Assert.Equal(0UL, f.B.ConfirmedTick);
        }
        private static void CaptureFailure()
        {
            var f = new SyncFixture(serverGame: new SyncTestGame { ThrowOnCaptureOfFive = true }); f.Start();
            f.A.TrySubmit(1, new byte[] { 5 }); f.Accept();
            Assert.Equal(0, f.Server.Poll(TimeSpan.FromMilliseconds(100)).Count);
            Assert.Equal(SessionStatus.Failed, f.Server.Status); Assert.Equal(0UL, f.Server.ConfirmedTick);
            Assert.Equal(0L, f.Server.InspectState());
        }
        private static void Divergence()
        {
            var f = new SyncFixture(clientBGame: new SyncTestGame { Bias = 1 }); f.Start();
            f.A.TrySubmit(1, new byte[] { 5 }); f.Accept(); f.Finish(1);
            Assert.Equal(5L, f.Server.InspectState()); Assert.Equal(SessionStatus.Failed, f.B.Status);
            Assert.True(f.B.Failure!.Contains("hash mismatch", StringComparison.Ordinal));
            Assert.Equal(0UL, f.B.ConfirmedTick); Assert.Equal(0L, f.B.InspectState());
        }
        private static void CorruptSnapshot()
        {
            var f = new SyncFixture(); f.Server.Poll(TimeSpan.Zero);
            Assert.True(f.ATransport.TryReceive(out var packet)); byte[] corrupt = packet!.Payload.ToArray(); corrupt[corrupt.Length - 1] ^= 1;
            SessionBoundaryTests.Send(f.ServerTransport, 2, MessageKind.Snapshot, corrupt);
            f.A.Poll(); f.B.Poll(); f.Server.Poll(TimeSpan.Zero);
            Assert.Equal(SessionStatus.Failed, f.A.Status); Assert.Equal(SessionStatus.Preparing, f.Server.Status);
        }
        private static void OversizedSnapshot()
        {
            var f = new SyncFixture();
            var p = FrameSync.Protocol.Protobuf.SessionPacket.Parser.ParseFrom(Wire.Snapshot(Fixture.SessionId, 1, 0, new byte[] { 0 }, 0, 1));
            p.Snapshot.TotalLength = uint.MaxValue;
            SessionBoundaryTests.Send(f.ServerTransport, 2, MessageKind.Snapshot, FrameSync.Protocol.Protobuf.ProtobufCodec.Encode(p, 900));
            f.A.Poll(); Assert.Equal(SessionStatus.Failed, f.A.Status);
            Assert.Throws<InvalidOperationException>(() => f.A.CaptureCheckpoint());
        }
        private static void UnknownSender()
        {
            var f = new SyncFixture(); f.Start(); var other = f.Network.CreateEndpoint(99);
            SessionBoundaryTests.Send(other, 3, MessageKind.Fault, Wire.Terminal(MessageKind.Fault, Fixture.SessionId, 1, 0));
            SessionBoundaryTests.Send(f.ServerTransport, 3, MessageKind.Fault, Wire.Terminal(MessageKind.Fault, Fixture.SessionId, 2, 0));
            f.B.Poll(); Assert.Equal(SessionStatus.Running, f.B.Status); Assert.Equal(2UL, f.B.RejectedMessages);
        }
        private static void Capacity()
        {
            Assert.Throws<ArgumentException>(() => new SyncFixture(options: new SessionOptions(maxOutboxMessages: 1)));
            var network = new InMemoryNetwork(maxMessageBytes: 512); var transport = network.CreateEndpoint(1);
            Assert.Throws<ArgumentException>(() => new ClientSession<long>(new SyncTestGame().Module(), Fixture.SessionId, 1, transport, 2));
        }
        private static void Participants()
        {
            for (int count = 1; count <= 4; count++)
            {
                var network = new InMemoryNetwork(); var transport = network.CreateEndpoint(1);
                var members = new Participant[count]; var peers = new PeerBinding[count]; var clients = new ClientSession<long>[count];
                for (int i = 0; i < count; i++)
                {
                    byte[] id = new byte[16]; id[0] = (byte)(i + 1);
                    members[i] = new Participant((ushort)(i * 2), new Guid(id)); peers[i] = new PeerBinding((ulong)i + 2, members[i].Slot);
                    clients[i] = new ClientSession<long>(new SyncTestGame().Module(), Fixture.SessionId, 1, network.CreateEndpoint((ulong)i + 2), 1);
                }
                var server = new ServerSession<long>(new SyncTestGame().Module(), new SessionDescriptor(Fixture.SessionId, 1, 10, members), transport,
                    peers, _ => new ByteString(new byte[] { 0 }));
                for (int n = 0; n < 10; n++) { server.Poll(TimeSpan.Zero); foreach (var client in clients) client.Poll(); }
                for (int i = 0; i < count; i++) { Assert.True(clients[i].TrySubmit(1, new byte[] { (byte)(i + 1) })); clients[i].Poll(); }
                server.Poll(TimeSpan.Zero); server.Poll(TimeSpan.FromMilliseconds(100));
                Assert.Equal((long)(count * (count + 1) / 2), server.InspectState());
                foreach (var client in clients)
                { client.Poll(); Assert.Bytes(CheckpointCodec.Encode(server.CaptureCheckpoint()), CheckpointCodec.Encode(client.CaptureCheckpoint())); }
            }
        }
        private static void Events()
        {
            var f = new SyncFixture(); f.Start(); f.A.TrySubmit(1, new byte[] { 5 }); f.Accept();
            var s = f.Server.Poll(TimeSpan.FromMilliseconds(100)); var a = f.A.Poll(); var b = f.B.Poll();
            Assert.Equal(1, s.Count); Assert.Equal(1, a.Count); Assert.Equal(1, b.Count); Assert.Equal(1, b[0].Events.Count);
            Assert.Equal(s[0].Events[0].Tick, b[0].Events[0].Tick);
            Assert.Equal(s[0].Events[0].Index, b[0].Events[0].Index);
            Assert.Bytes(new byte[] { 42 }, b[0].Events[0].Payload.ToArray());
            Assert.Equal(0, f.B.Poll().Count);
        }
        private static void Backpressure()
        {
            var f = new SyncFixture(options: new SessionOptions(maxOutboxMessages: 2), network: new InMemoryNetwork(maxQueuedMessages: 1));
            f.Start(); f.A.TrySubmit(1, new byte[] { 5 }); f.A.Poll(); f.Server.Poll(TimeSpan.Zero);
            // 一个旧会话消息占住 B 的收件箱，验证 Final 会保留并重试。
            SessionBoundaryTests.Send(f.ServerTransport, 3, MessageKind.Fault, Wire.Terminal(MessageKind.Fault, Fixture.SessionId, 2, 0));
            f.Server.Poll(TimeSpan.FromMilliseconds(100)); Assert.Equal(1UL, f.Server.ConfirmedTick); Assert.Equal(0UL, f.B.ConfirmedTick);
            for (int i = 0; i < 10; i++) { f.A.Poll(); f.B.Poll(); f.Server.Poll(TimeSpan.FromMilliseconds(100)); }
            f.A.Poll(); f.B.Poll(); f.AssertEqual(1, 5);
        }
    }
}

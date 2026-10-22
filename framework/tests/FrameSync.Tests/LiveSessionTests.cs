using System;
using System.Collections.Generic;
using System.Linq;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;
using FrameSync.Transport;

namespace FrameSync.Tests
{
    internal static class LiveSessionTests
    {
        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("live_session.partial_roster_starts_and_late_peer_catches_up", LateJoin);
            yield return ("live_session.rebind_discards_old_connection_and_pending_input", Reconnect);
            yield return ("live_session.blocked_snapshot_cannot_stall_existing_player", BlockedSnapshot);
            yield return ("live_session.catchup_window_overflow_drops_only_joining_peer", CatchupOverflow);
            yield return ("live_session.prepare_timeout_clears_binding_and_allows_retry", Timeout);
            yield return ("live_session.small_window_and_single_message_budget_catch_up", SmallBudget);
            yield return ("live_session.progress_cannot_skip_unsent_catchup_or_enable_input", ForgedProgress);
            yield return ("live_session.duplicate_ready_does_not_reactivate_or_reset_state", DuplicateReady);
            yield return ("live_session.default_client_rejects_nonzero_bootstrap", StrictClient);
            yield return ("live_session.default_server_requires_full_roster", StrictServer);
            yield return ("live_session.unbound_active_member_is_rejected", ActiveUnbound);
            yield return ("live_session.online_unknown_and_duplicate_connections_cannot_bind", InvalidBinding);
            yield return ("live_session.repeated_disconnects_do_not_reapply_old_inputs", RepeatedReconnect);
            yield return ("live_session.catchup_message_and_byte_limits_release_the_peer", QueueLimits);
            yield return ("live_session.three_simultaneous_joins_receive_identical_membership", FourPlayers);
            yield return ("live_session.prediction_starts_from_the_live_checkpoint", Prediction);
            yield return ("live_session.rebind_waits_for_server_deadline_backlog", Backlog);
        }

        // Removing live bootstrap, catch-up, or membership rebinding must fail this ordinary-input flow.
        private static void LateJoin()
        {
            var f = new LiveFixture(); f.Advance(5);
            Assert.Equal(5L, f.Server.InspectState());
            Assert.True(!f.Server.CaptureCheckpoint().Membership.Find(1).IsActive);
            f.Join(3); f.Advance(3, pollJoining: false);
            Assert.Equal(8UL, f.Server.ConfirmedTick); // the existing player continues during snapshot transfer
            f.Pump(); f.Advance(1);
            Assert.True(f.B!.LocalParticipant!.IsActive);
            Assert.Equal(2UL, f.B.LocalParticipant.Generation);
            Assert.True(f.B.TrySubmit(10, new byte[] { 7 })); f.B.Poll();
            f.Advance(1); f.AssertEqual(10, 17);
        }

        private static void Reconnect()
        {
            var f = new LiveFixture(); f.Advance(1); f.Join(3); f.Pump(); f.Advance(1);
            var oldTransport = f.BTransport!;
            Assert.True(f.B!.TrySubmit(5, new byte[] { 9 })); f.Pump();
            f.Server.Deactivate(3);
            Assert.True(!f.Server.TryBind(new PeerBinding(4, 1))); // Deactivate must first be a confirmed event
            f.B = null; f.Advance(1); f.Join(4); f.Pump(); f.Advance(1);
            Assert.Equal(3UL, f.B!.LocalParticipant!.Generation);
            ulong rejected = f.Server.RejectedMessages;
            SessionBoundaryTests.Send(oldTransport, 1, MessageKind.Input,
                Wire.Input(Fixture.SessionId, 1, 5, new RecordedInput(1, 2, new byte[] { 9 }), 900));
            SessionBoundaryTests.Send(f.BTransport!, 1, MessageKind.Input,
                Wire.Input(Fixture.SessionId, 1, 5, new RecordedInput(1, 2, new byte[] { 9 }), 900));
            f.Advance(1);
            Assert.Equal(rejected + 2, f.Server.RejectedMessages);
            Assert.True(!f.Server.IsConnected(3)); f.AssertEqual(5, 5);
        }

        private static void BlockedSnapshot()
        {
            var f = new LiveFixture(options: new SessionOptions(snapshotChunkBytes: 16, maxFutureFrames: 4),
                network: new InMemoryNetwork(maxQueuedMessages: 1));
            f.Advance(1); f.Join(3); f.Advance(8, pollJoining: false);
            Assert.True(!f.Server.IsConnected(3)); f.B = null; f.AssertEqual(9, 9);
            f.Join(4, new SessionOptions(snapshotChunkBytes: 16)); f.Pump(); f.Advance(1);
            Assert.True(f.B!.LocalParticipant!.IsActive); f.AssertEqual(10, 10);
        }

        private static void CatchupOverflow()
        {
            var f = new LiveFixture(new SessionOptions(maxFutureFrames: 3)); f.Advance(1); f.Join(3);
            f.Advance(3, pollJoining: false); Assert.True(f.Server.IsConnected(3));
            f.Advance(1, pollJoining: false); Assert.True(!f.Server.IsConnected(3));
            f.B = null; f.AssertEqual(5, 5);
        }

        private static void Timeout()
        {
            var f = new LiveFixture(new SessionOptions(prepareTimeout: TimeSpan.FromMilliseconds(500)));
            f.Advance(1); f.Join(3); f.Advance(4, pollJoining: false); Assert.True(f.Server.IsConnected(3));
            f.Advance(1, pollJoining: false); Assert.True(!f.Server.IsConnected(3));
            f.Join(4); f.Pump(); f.Advance(1); f.AssertEqual(7, 7);
        }

        private static void SmallBudget()
        {
            var f = new LiveFixture(new SessionOptions(maxMessagesPerPoll: 1, snapshotChunkBytes: 32, maxOutboxMessages: 2));
            f.Advance(3); f.Join(3, new SessionOptions(maxFutureFrames: 1, maxMessagesPerPoll: 1, snapshotChunkBytes: 32));
            f.Advance(1, pollJoining: false); f.Pump(); f.Advance(1);
            Assert.Equal(2UL, f.B!.LocalParticipant!.Generation); f.AssertEqual(5, 5);
        }

        private static void ForgedProgress()
        {
            var f = new LiveFixture(); f.Advance(2);
            string snapshotHash = CheckpointCodec.Hash(f.Server.CaptureCheckpoint());
            f.Join(3); f.Advance(3, pollJoining: false);
            SessionBoundaryTests.Send(f.BTransport!, 1, MessageKind.Ready, Wire.Ready(Fixture.SessionId, 1, snapshotHash, 128));
            SessionBoundaryTests.Send(f.BTransport!, 1, MessageKind.Progress, Wire.Terminal(MessageKind.Progress, Fixture.SessionId, 1, 5));
            SessionBoundaryTests.Send(f.BTransport!, 1, MessageKind.Input,
                Wire.Input(Fixture.SessionId, 1, 6, new RecordedInput(1, 2, new byte[] { 9 }), 900));
            ulong rejected = f.Server.RejectedMessages; f.Server.Poll(f.Now);
            Assert.Equal(rejected + 2, f.Server.RejectedMessages);
            Assert.True(!f.Server.CaptureCheckpoint().Membership.Find(1).IsActive);
            // The untrusted acknowledgements must not create a second snapshot or modify earned state.
            Assert.Equal(5L, f.Server.InspectState());
        }

        private static void DuplicateReady()
        {
            var f = new LiveFixture(); f.Advance(2);
            string snapshotHash = CheckpointCodec.Hash(f.Server.CaptureCheckpoint());
            f.Join(3); f.Pump(); f.Advance(1);
            ulong sequence = f.Server.CaptureCheckpoint().Membership.LastSequence;
            for (int i = 0; i < 3; i++)
                SessionBoundaryTests.Send(f.BTransport!, 1, MessageKind.Ready, Wire.Ready(Fixture.SessionId, 1, snapshotHash, 128));
            f.Advance(2); f.AssertEqual(5, 5);
            Assert.Equal(sequence, f.Server.CaptureCheckpoint().Membership.LastSequence);
            Assert.Equal(2UL, f.B!.LocalParticipant!.Generation);
        }

        private static void StrictClient()
        {
            var f = new LiveFixture(); f.Advance(1); f.Join(3, allowLive: false); f.Pump();
            Assert.Equal(SessionStatus.Failed, f.B!.Status);
            Assert.Equal(SessionStatus.Running, f.Server.Status);
        }

        private static void StrictServer() => CheckUnbound(false, false);
        private static void ActiveUnbound() => CheckUnbound(true, true);
        private static void CheckUnbound(bool optIn, bool active)
        {
            var n = new InMemoryNetwork(); var transport = n.CreateEndpoint(1);
            var roster = Array.ConvertAll(Fixture.Members, p => new Participant(p.Slot, p.ParticipantId, isActive: active));
            Assert.Throws<ArgumentException>(() => new ServerSession<long>(new SyncTestGame().Module(),
                new SessionDescriptor(Fixture.SessionId, 1, 10, roster), transport,
                new[] { new PeerBinding(2, 0) }, _ => new ByteString(new byte[] { 0 }), allowUnboundParticipants: optIn));
        }

        private static void InvalidBinding()
        {
            var f = new LiveFixture(); f.Advance(1);
            Assert.True(!f.Server.TryBind(new PeerBinding(3, 0)));
            Assert.True(!f.Server.TryBind(new PeerBinding(2, 1)));
            Assert.True(!f.Server.TryBind(new PeerBinding(3, 4)));
            f.Join(3); Assert.True(!f.Server.TryBind(new PeerBinding(4, 1)));
            f.Pump(); f.Advance(1); f.AssertEqual(2, 2);
        }

        private static void RepeatedReconnect()
        {
            var f = new LiveFixture(); f.Advance(1);
            for (ulong id = 3; id <= 12; id++)
            {
                f.Join(id); f.Pump(); f.Advance(1);
                Assert.Equal(id - 1, f.B!.LocalParticipant!.Generation);
                Assert.True(f.B.TrySubmit(f.Server.ConfirmedTick + 2, new byte[] { 9 })); f.Pump();
                f.Server.Deactivate(id); f.B = null; f.Advance(1);
                Assert.True(!f.Server.IsConnected(id));
            }
            f.AssertEqual(21, 21);
        }

        private static void QueueLimits()
        {
            foreach (var options in new[] {
                new SessionOptions(maxOutboxMessages: 2, maxFutureFrames: 1024),
                new SessionOptions(maxOutboxMessages: 1024, maxOutboxBytes: 900, maxFutureFrames: 1024) })
            {
                var f = new LiveFixture(options); f.Advance(1); f.Join(3);
                f.Advance(40, pollJoining: false); Assert.True(!f.Server.IsConnected(3));
                f.B = null; f.AssertEqual(41, 41);
            }
        }

        private static void FourPlayers()
        {
            var f = new LiveFixture(capacity: 4); f.Advance(3); f.Join(3);
            var cTransport = f.Network.CreateEndpoint(4); var dTransport = f.Network.CreateEndpoint(5);
            Assert.True(f.Server.TryBind(new PeerBinding(4, 2))); Assert.True(f.Server.TryBind(new PeerBinding(5, 3)));
            var c = new ClientSession<long>(new SyncTestGame().Module(), Fixture.SessionId, 1, cTransport, 1, allowLiveBootstrap: true);
            var d = new ClientSession<long>(new SyncTestGame().Module(), Fixture.SessionId, 1, dTransport, 1, allowLiveBootstrap: true);
            f.Advance(2, pollJoining: false);
            for (int i = 0; i < 100; i++) { f.Server.Poll(f.Now); f.A.Poll(); f.B!.Poll(); c.Poll(); d.Poll(); }
            f.Advance(1); c.Poll(); d.Poll();
            foreach (var client in new[] { f.B!, c, d })
            {
                Assert.True(client.LocalParticipant!.IsActive); Assert.Equal(2UL, client.LocalParticipant.Generation);
                Assert.Bytes(CheckpointCodec.Encode(f.Server.CaptureCheckpoint()), CheckpointCodec.Encode(client.CaptureCheckpoint()));
            }
            f.B!.TrySubmit(7, new byte[] { 2 }); c.TrySubmit(7, new byte[] { 3 }); d.TrySubmit(7, new byte[] { 4 });
            f.B.Poll(); c.Poll(); d.Poll(); f.Advance(1); c.Poll(); d.Poll();
            f.AssertEqual(7, 16);
            Assert.Bytes(CheckpointCodec.Encode(f.Server.CaptureCheckpoint()), CheckpointCodec.Encode(c.CaptureCheckpoint()));
            Assert.Bytes(CheckpointCodec.Encode(f.Server.CaptureCheckpoint()), CheckpointCodec.Encode(d.CaptureCheckpoint()));
        }

        private static void Prediction()
        {
            var f = new LiveFixture(); f.Advance(5); f.Join(3, prediction: true); f.Pump(); f.Advance(1);
            f.B!.TrySubmit(7, new byte[] { 3 }); f.B.Poll(7);
            f.Advance(1); f.AssertEqual(7, 10);
            Assert.Equal(7UL, f.B.ConfirmedTick);
        }
        private static void Backlog()
        {
            var f=new LiveFixture();f.Advance(1);
            // A lost peer stops acknowledging. The server eventually fills that peer's flow-control window.
            f.Now+=TimeSpan.FromSeconds(30);
            for(int i=0;i<30;i++)f.Server.Poll(f.Now);
            f.Server.Deactivate(2);f.Server.Poll(f.Now);
            Assert.True(!f.Server.CaptureCheckpoint().Membership.Find(0).IsActive);
            Assert.True(!f.Server.TryBind(new PeerBinding(3,0)));
            for(int i=0;i<50;i++)f.Server.Poll(f.Now);
            var transport=f.Network.CreateEndpoint(3);
            Assert.True(f.Server.TryBind(new PeerBinding(3,0)));
            var c=new ClientSession<long>(new SyncTestGame().Module(),Fixture.SessionId,1,transport,1,allowLiveBootstrap:true);
            for(int i=0;i<100;i++){f.Server.Poll(f.Now);c.Poll();}
            f.Now+=TimeSpan.FromMilliseconds(100);
            for(int i=0;i<100;i++){f.Server.Poll(f.Now);c.Poll();}
            Assert.True(c.LocalParticipant!.IsActive);Assert.Equal(2UL,c.LocalParticipant.Generation);
            Assert.Bytes(CheckpointCodec.Encode(f.Server.CaptureCheckpoint()),CheckpointCodec.Encode(c.CaptureCheckpoint()));
        }
    }

    internal sealed class LiveFixture
    {
        public readonly InMemoryNetwork Network;
        public readonly ITransport ServerTransport, ATransport;
        public readonly ServerSession<long> Server;
        public readonly ClientSession<long> A;
        public ITransport? BTransport;
        public ClientSession<long>? B;
        public TimeSpan Now;
        public LiveFixture(SessionOptions? options = null, InMemoryNetwork? network = null, int capacity = 2)
        {
            Network = network ?? new InMemoryNetwork();
            ServerTransport = Network.CreateEndpoint(1); ATransport = Network.CreateEndpoint(2);
            var roster = Enumerable.Range(0, capacity).Select(i => new Participant((ushort)i, Guid.NewGuid(), isActive: false)).ToArray();
            Server = new ServerSession<long>(new SyncTestGame().Module(), new SessionDescriptor(Fixture.SessionId, 1, 10, roster), ServerTransport,
                new[] { new PeerBinding(2, 0) }, _ => new ByteString(new byte[] { 0 }), options: options, allowUnboundParticipants: true);
            A = new ClientSession<long>(new SyncTestGame().Module(), Fixture.SessionId, 1, ATransport, 1, options: options);
            Pump(); Assert.Equal(SessionStatus.Running, Server.Status); Assert.Equal(SessionStatus.Running, A.Status);
        }
        public void Join(ulong connection, SessionOptions? options = null, bool allowLive = true, bool prediction = false)
        {
            BTransport = Network.CreateEndpoint(connection);
            Assert.True(Server.TryBind(new PeerBinding(connection, 1)));
            B = new ClientSession<long>(new SyncTestGame().Module(), Fixture.SessionId, 1, BTransport, 1, options: options, allowLiveBootstrap: allowLive,
                prediction: prediction ? new PredictionOptions() : null, neutralInput: prediction ? _ => new ByteString(new byte[] { 0 }) : null);
        }
        public void Pump(bool pollJoining = true)
        {
            for (int i = 0; i < 100; i++)
            {
                Server.Poll(Now); A.Poll(); if (pollJoining) B?.Poll();
            }
        }
        public void Advance(int count, bool pollJoining = true)
        {
            for (int i = 0; i < count; i++)
            {
                Assert.True(A.TrySubmit(Server.ConfirmedTick + 1, new byte[] { 1 })); A.Poll(); Server.Poll(Now);
                Now += TimeSpan.FromMilliseconds(100); Pump(pollJoining);
                Assert.Equal(SessionStatus.Running, Server.Status);
            }
        }
        public void AssertEqual(ulong tick, long value)
        {
            Assert.Equal(tick, Server.ConfirmedTick); Assert.Equal(value, Server.InspectState());
            var bytes = CheckpointCodec.Encode(Server.CaptureCheckpoint());
            Assert.Bytes(bytes, CheckpointCodec.Encode(A.CaptureCheckpoint()));
            if (B != null) { Assert.Equal(SessionStatus.Running, B.Status); Assert.Bytes(bytes, CheckpointCodec.Encode(B.CaptureCheckpoint())); }
        }
    }
}

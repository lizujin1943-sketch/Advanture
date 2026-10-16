using System;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;

namespace FrameSync.Tests
{
    internal static class SessionMembershipTests
    {
        public static IEnumerable<(string, Action)> Cases()
        {
            yield return ("session.membership.disconnect_removes_slow_peer_and_discards_future_input", () => {
                var f = new SyncFixture(clientOptions: new SessionOptions(maxFutureFrames: 2)); f.Start();
                f.B.TrySubmit(2, new byte[] {9}); f.Accept();
                f.Server.Deactivate(3); f.Server.Deactivate(3); // 重复通知幂等。
                for (int i = 1; i <= 8; i++) { f.Now = TimeSpan.FromMilliseconds(i * 100); f.Server.Poll(f.Now); f.A.Poll(); }
                Assert.Equal(8UL, f.Server.ConfirmedTick); Assert.Equal(8UL, f.A.ConfirmedTick);
                Assert.Equal(0L, f.A.InspectState());
                Assert.True(!f.Server.CaptureCheckpoint().Membership.Find(1).IsActive);
                Assert.Equal(1UL, f.Server.CaptureCheckpoint().Membership.LastSequence);
                Assert.Bytes(CheckpointCodec.Encode(f.Server.CaptureCheckpoint()), CheckpointCodec.Encode(f.A.CaptureCheckpoint()));
            });
            yield return ("session.membership.initial_activation_is_broadcast", () => {
                var f = new SyncFixture(initiallyActive: false); f.Start();
                f.A.TrySubmit(1, new byte[] {5}); f.Accept(); f.Finish(1); f.AssertEqual(1, 5);
                Assert.Equal(2, f.Server.LastFrame!.MemberEvents.Count);
                Assert.Equal(MemberEventKind.Activate, f.B.LastFrame!.MemberEvents[0].Kind);
            });
            yield return ("session.membership.disconnect_before_first_frame_keeps_other_member_active", () => {
                var f = new SyncFixture(initiallyActive: false); f.Start();
                f.Server.Deactivate(3); f.Finish(1);
                Assert.Equal(1, f.Server.LastFrame!.MemberEvents.Count);
                Assert.Equal((ushort)0, f.Server.LastFrame.MemberEvents[0].Slot);
                Assert.True(!f.A.CaptureCheckpoint().Membership.Find(1).IsActive);
            });
        }
    }
}

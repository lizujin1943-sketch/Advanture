using System;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace FrameSync.Tests
{
    // 成员变化必须随输入帧重放，不能由 Socket 回调直接修改模拟。
    internal static class MembershipTests
    {
        public static IEnumerable<(string, Action)> Cases()
        {
            yield return ("membership.deactivate_rebind_and_restore_are_deterministic", () => {
                var runner = Fixture.Runner(limits: new RuntimeLimits(historyFrames: 2));
                runner.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                runner.Advance(Frame(2, new[] { Fixture.Input(0, 3) }, new MemberEvent(1, MemberEventKind.Deactivate, 1, 1)));
                Assert.Equal(123L, runner.InspectState().Value);
                var saved = CheckpointCodec.Decode(CheckpointCodec.Encode(runner.CaptureCheckpoint()));
                Assert.True(!saved.Membership.Find(1).IsActive);
                var tail = Frame(3, new[] { Fixture.Input(0, 4), new RecordedInput(1, 2, new byte[] {5}) },
                    new MemberEvent(2, MemberEventKind.Rebind, 1, 2));
                runner.Advance(tail);
                var expected = CheckpointCodec.Encode(runner.CaptureCheckpoint());
                runner.Restore(saved); runner.Advance(tail);
                Assert.Bytes(expected, CheckpointCodec.Encode(runner.CaptureCheckpoint()));
                Assert.Equal(12345L, runner.InspectState().Value);
                Assert.Equal(2UL, runner.CaptureCheckpoint().Membership.Find(1).Generation);
                // 历史窗口已跨过停用帧；新实例仍能恢复完整成员状态。
                var loaded = CheckpointCodec.Decode(expected);
                var fresh = Fixture.Runner(limits: new RuntimeLimits(historyFrames: 2)); fresh.Restore(loaded);
                Assert.Bytes(expected, CheckpointCodec.Encode(fresh.CaptureCheckpoint()));
            });
            yield return ("membership.activation_is_delivered_to_game_at_fixed_tick", () => {
                var game = new TestModule(); int events = 0; game.OnFrame = c => events += c.MemberEvents.Count;
                var inactive = new Participant(0, Fixture.Members[0].ParticipantId, isActive: false);
                var session = new SessionDescriptor(Fixture.SessionId, 1, 30, new[] {inactive});
                var runner = new SimulationRunner<TestState>(game.Module(), session);
                runner.Advance(Frame(1, new[] { Fixture.Input(0, 7) }, new MemberEvent(1, MemberEventKind.Activate, 0, 1)));
                Assert.Equal(1, events); Assert.Equal(7L, runner.InspectState().Value);
            });
            yield return ("membership.rejects_old_generation_duplicate_and_out_of_order_events", () => {
                var runner = Fixture.Runner();
                runner.Advance(Frame(1, new[] {Fixture.Input(0, 0)}, new MemberEvent(1, MemberEventKind.Deactivate, 1, 1)));
                var before = CheckpointCodec.Encode(runner.CaptureCheckpoint());
                Assert.Throws<ArgumentException>(() => runner.Advance(Frame(2, new[] {Fixture.Input(0, 0), Fixture.Input(1, 1)},
                    new MemberEvent(2, MemberEventKind.Rebind, 1, 2))));
                Assert.Throws<ArgumentException>(() => runner.Advance(Frame(2, new[] {Fixture.Input(0, 0)},
                    new MemberEvent(3, MemberEventKind.Deactivate, 0, 1))));
                Assert.Throws<ArgumentException>(() => Frame(2, Array.Empty<RecordedInput>(),
                    new MemberEvent(2, MemberEventKind.Deactivate, 0, 1), new MemberEvent(3, MemberEventKind.Activate, 0, 1)));
                Assert.Bytes(before, CheckpointCodec.Encode(runner.CaptureCheckpoint()));
            });
            yield return ("membership.all_inactive_frames_replay_and_missing_counts_reset", () => {
                var runner = Fixture.Runner(limits: new RuntimeLimits(historyFrames: 2));
                var recorder = new ReplayRecorder(runner.CaptureCheckpoint());
                var frames = new[] {
                    Fixture.Frame(1, Fixture.Input(0, 0, InputOrigin.MissingSubstitute), Fixture.Input(1, 0, InputOrigin.MissingSubstitute)),
                    Frame(2, Array.Empty<RecordedInput>(), new MemberEvent(1, MemberEventKind.Deactivate, 0, 1), new MemberEvent(2, MemberEventKind.Deactivate, 1, 1)),
                    Frame(3, Array.Empty<RecordedInput>()), Frame(4, Array.Empty<RecordedInput>()) };
                foreach (var f in frames) { runner.Advance(f); recorder.Append(f); }
                var saved = CheckpointCodec.Decode(CheckpointCodec.Encode(runner.CaptureCheckpoint()));
                Assert.Equal(0UL, saved.MissingInputCounts[0]); Assert.Equal(0UL, saved.MissingInputCounts[1]);
                var recording = ReplayCodec.Decode(ReplayCodec.Encode(recorder.Build()));
                var fresh = Fixture.Runner(limits: new RuntimeLimits(historyFrames: 2));
                fresh.Restore(recording.Initial); foreach(var f in recording.Frames) fresh.Advance(f);
                Assert.Bytes(CheckpointCodec.Encode(saved), CheckpointCodec.Encode(fresh.CaptureCheckpoint()));
            });
        }
        private static InputFrame Frame(ulong tick, RecordedInput[] inputs, params MemberEvent[] events) =>
            new InputFrame(Fixture.SessionId, 1, tick, inputs, events);
    }
}

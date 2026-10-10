using System;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace FrameSync.Tests
{
    internal static class RunnerTests
    {
        public static IEnumerable<(string, Action)> Cases()
        {
            yield return ("runner.orders_inputs_and_passes_fixed_time", () => {
                var r = new SimulationRunner<TestState>(new TestModule().Module(), Fixture.Session(900));
                r.Advance(Fixture.Frame(1, Fixture.Input(1, 7), Fixture.Input(0, 3)));
                Assert.Equal(37L, r.InspectState().Value); Assert.Equal(1UL, r.Tick);
                Assert.Equal(901UL, r.InspectState().LastPersistentTick); Assert.Equal(30U, r.InspectState().TickRate);
            });
            yield return ("runner.instances_are_isolated", () => {
                var module = new TestModule().Module();
                var a = new SimulationRunner<TestState>(module, Fixture.Session());
                var b = new SimulationRunner<TestState>(module, Fixture.Session());
                a.Advance(Fixture.Frame(1, Fixture.Input(0, 2), Fixture.Input(1, 4)));
                Assert.Equal(24L, a.InspectState().Value); Assert.Equal(0L, b.InspectState().Value);
            });
            yield return ("runner.supports_one_through_four_participants", () => {
                long[] expected = { 1, 12, 123, 1234 };
                for (int count = 1; count <= 4; count++)
                {
                    var members = new Participant[count]; var inputs = new RecordedInput[count];
                    for (int i = 0; i < count; i++)
                    {
                        members[i] = new Participant((ushort)i, new Guid(i + 1, 0, 0, new byte[8]));
                        inputs[count - i - 1] = Fixture.Input((ushort)i, (byte)(i + 1));
                    }
                    var session = new SessionDescriptor(Fixture.SessionId, 1, 30, members);
                    var r = new SimulationRunner<TestState>(new TestModule().Module(), session);
                    r.Advance(Fixture.Frame(1, inputs));
                    Assert.Equal(expected[count - 1], r.InspectState().Value);
                    var fresh = new SimulationRunner<TestState>(new TestModule().Module(), session);
                    fresh.Restore(CheckpointCodec.Decode(CheckpointCodec.Encode(r.CaptureCheckpoint())));
                    Assert.Equal(expected[count - 1], fresh.InspectState().Value);
                }
            });
            yield return ("runner.freezes_input_bytes", () => {
                var bytes = new byte[] { 3 };
                var input = new RecordedInput(0, 1, bytes);
                var frame = Fixture.Frame(1, input, Fixture.Input(1, 7));
                bytes[0] = 9; input.Payload.ToArray()[0] = 8;
                var r = Fixture.Runner(); r.Advance(frame); Assert.Equal(37L, r.InspectState().Value);
            });
            yield return ("runner.inspection_cannot_mutate_live_state", () => {
                var r = Fixture.Runner(); r.Advance(Fixture.Frame(1, Fixture.Input(0, 3), Fixture.Input(1, 7)));
                r.InspectState().Value = -1; Assert.Equal(37L, r.InspectState().Value);
            });
            yield return ("runner.rejects_nonconsecutive_frames_before_step", () => {
                var r = Fixture.Runner();
                Assert.Throws<ArgumentException>(() => r.Advance(Fixture.Frame(2, Fixture.Input(0, 1), Fixture.Input(1, 1))));
                Assert.Equal(0UL, r.Tick); Assert.Equal(0L, r.InspectState().Value);
                r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 1)));
                Assert.Throws<ArgumentException>(() => r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 1))));
                Assert.Equal(11L, r.InspectState().Value);
            });
            yield return ("runner.rejects_old_session_and_epoch", () => {
                var r = Fixture.Runner(); var inputs = new[] { Fixture.Input(0, 1), Fixture.Input(1, 1) };
                Assert.Throws<ArgumentException>(() => r.Advance(new InputFrame(Guid.NewGuid(), 1, 1, inputs)));
                Assert.Throws<ArgumentException>(() => r.Advance(new InputFrame(Fixture.SessionId, 2, 1, inputs)));
                Assert.Equal(0L, r.InspectState().Value);
            });
            yield return ("runner.rejects_missing_or_stale_control", () => {
                var r = Fixture.Runner();
                Assert.Throws<ArgumentException>(() => r.Advance(Fixture.Frame(1, Fixture.Input(0, 1))));
                Assert.Throws<ArgumentException>(() => r.Advance(Fixture.Frame(1,
                    new RecordedInput(0, 2, new byte[] { 1 }), Fixture.Input(1, 1))));
                Assert.Equal(0UL, r.Tick);
            });
            yield return ("runner.rejects_input_format_and_size_before_step", () => {
                var r = Fixture.Runner(limits: new RuntimeLimits(maxInputBytes: 1));
                Assert.Throws<ArgumentException>(() => r.Advance(Fixture.Frame(1, Fixture.Input(0, 99), Fixture.Input(1, 1))));
                Assert.Throws<ArgumentException>(() => r.Advance(Fixture.Frame(1,
                    new RecordedInput(0, 1, new byte[] { 1, 2 }), Fixture.Input(1, 1))));
                Assert.Equal(0L, r.InspectState().Value);
            });
            yield return ("runner.overflow_does_not_partially_advance", () => {
                var r = new SimulationRunner<TestState>(new TestModule().Module(), Fixture.Session(ulong.MaxValue));
                Assert.Throws<OverflowException>(() => r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 1))));
                Assert.Equal(0UL, r.Tick); Assert.Equal(0L, r.InspectState().Value);
            });
            yield return ("runner.stops_after_partial_game_failure", () => {
                var module = new TestModule { ThrowOnStep = true }; var r = Fixture.Runner(module);
                var frame = Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 1));
                Assert.Throws<InvalidOperationException>(() => r.Advance(frame));
                Assert.True(r.IsFaulted); Assert.Equal(0UL, r.Tick);
                module.ThrowOnStep = false;
                Assert.Throws<InvalidOperationException>(() => r.Advance(frame));
                Assert.Throws<InvalidOperationException>(() => r.InspectState());
            });
            yield return ("runner.assigns_stable_event_identity", () => {
                var r = Fixture.Runner();
                var result = r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                Assert.Equal(1, result.Events.Count);
                var e = result.Events[0]; Assert.Equal(Fixture.SessionId, e.SessionId);
                Assert.Equal(1UL, e.Epoch); Assert.Equal(1UL, e.Tick); Assert.Equal(0, e.Index);
                Assert.Bytes(new byte[] { 0 }, e.Payload.ToArray());
            });
            yield return ("runner.bounds_game_event_output", () => {
                var r = Fixture.Runner(new TestModule { EventCount = 2 }, new RuntimeLimits(maxEventsPerFrame: 1));
                Assert.Throws<InvalidOperationException>(() => r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 1))));
                Assert.True(r.IsFaulted); Assert.Equal(0UL, r.Tick);
            });
            yield return ("runner.rejects_reentrant_step", () => {
                var module = new TestModule(); var r = Fixture.Runner(module);
                module.OnStep = () => r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 1)));
                Assert.Throws<InvalidOperationException>(() => r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 1))));
                Assert.True(r.IsFaulted);
            });
        }
    }
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace FrameSync.Tests
{
    internal static class ReplayTests
    {
        public static IEnumerable<(string, Action)> Cases()
        {
            yield return ("replay.records_and_replays_from_initial_state", () => {
                var r = Fixture.Runner(); var recorder = new ReplayRecorder(r.CaptureCheckpoint());
                var f1 = Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2));
                var f2 = Fixture.Frame(2, Fixture.Input(0, 3), Fixture.Input(1, 4));
                recorder.Append(f1); r.Advance(f1); recorder.Append(f2); r.Advance(f2);
                var expected = r.CaptureCheckpoint();
                var fresh = Fixture.Runner(); var result = ReplayPlayer.Play(fresh, recorder.Build());
                Assert.Equal(1234L, fresh.InspectState().Value); Assert.Equal(2UL, result.Tick);
                Assert.Bytes(CheckpointCodec.Encode(expected), CheckpointCodec.Encode(result));
            });
            yield return ("replay.roundtrips_a_nonzero_start_checkpoint", () => {
                var r = Fixture.Runner();
                r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                var recorder = new ReplayRecorder(r.CaptureCheckpoint());
                recorder.Append(Fixture.Frame(2, Fixture.Input(0, 3), Fixture.Input(1, 4)));
                var bytes = ReplayCodec.Encode(recorder.Build());
                var loaded = ReplayCodec.Decode(bytes); var fresh = Fixture.Runner();
                var result = ReplayPlayer.Play(fresh, loaded);
                Assert.Equal(1UL, loaded.Initial.Tick); Assert.Equal(2UL, result.Tick);
                Assert.Equal(1234L, fresh.InspectState().Value); Assert.Bytes(bytes, ReplayCodec.Encode(loaded));
            });
            yield return ("replay.built_recordings_do_not_change_on_append", () => {
                var recorder = new ReplayRecorder(Fixture.Runner().CaptureCheckpoint());
                recorder.Append(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                var frozen = recorder.Build();
                recorder.Append(Fixture.Frame(2, Fixture.Input(0, 3), Fixture.Input(1, 4)));
                Assert.Equal(1, frozen.Frames.Count); Assert.Equal(2, recorder.Build().Frames.Count);
            });
            yield return ("replay.rejects_frame_gaps_and_wrong_control", () => {
                var recorder = new ReplayRecorder(Fixture.Runner().CaptureCheckpoint());
                Assert.Throws<ArgumentException>(() => recorder.Append(Fixture.Frame(2, Fixture.Input(0, 1), Fixture.Input(1, 2))));
                Assert.Throws<ArgumentException>(() => recorder.Append(Fixture.Frame(1,
                    new RecordedInput(0, 8, new byte[] { 1 }), Fixture.Input(1, 2))));
                recorder.Append(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                Assert.Equal(1, recorder.Build().Frames.Count);
            });
            yield return ("replay.enforces_frame_and_byte_budgets", () => {
                var initial = Fixture.Runner().CaptureCheckpoint();
                var recorder = new ReplayRecorder(initial, new ReplayLimits(maxFrames: 1));
                recorder.Append(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                Assert.Throws<InvalidOperationException>(() => recorder.Append(Fixture.Frame(2, Fixture.Input(0, 3), Fixture.Input(1, 4))));
                var emptyBytes = ReplayCodec.Encode(new ReplayRecorder(initial).Build());
                Assert.True(emptyBytes.Length >= 256);
                var tight = new ReplayRecorder(initial, new ReplayLimits(maxBytes: emptyBytes.Length));
                Assert.Throws<InvalidOperationException>(() => tight.Append(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2))));
                Assert.Equal(0, tight.Build().Frames.Count);
            });
            yield return ("replay.empty_recording_restores_its_start", () => {
                var r = Fixture.Runner(); var recording = new ReplayRecorder(r.CaptureCheckpoint()).Build();
                r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                ReplayPlayer.Play(r, recording); Assert.Equal(0UL, r.Tick); Assert.Equal(0L, r.InspectState().Value);
            });
            yield return ("replay.rejects_bad_envelopes_and_declared_counts", () => {
                var recorder = new ReplayRecorder(Fixture.Runner().CaptureCheckpoint());
                recorder.Append(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                var bytes = ReplayCodec.Encode(recorder.Build()); Assert.True(bytes.Length > 256);
                var corrupt = (byte[])bytes.Clone(); corrupt[20] ^= 1;
                Assert.Throws<InvalidDataException>(() => ReplayCodec.Decode(corrupt));
                Assert.Throws<InvalidDataException>(() => ReplayCodec.Decode(bytes.AsSpan(0, bytes.Length - 1)));
                var hugeCount = (byte[])bytes.Clone();
                int checkpointSize = BinaryPrimitives.ReadInt32LittleEndian(hugeCount.AsSpan(6));
                BinaryPrimitives.WriteInt32LittleEndian(hugeCount.AsSpan(10 + checkpointSize), int.MaxValue);
                CheckpointTests.Resign(hugeCount);
                Assert.Throws<InvalidDataException>(() => ReplayCodec.Decode(hugeCount));
                var wrongVersion = (byte[])bytes.Clone(); wrongVersion[4] = 99; CheckpointTests.Resign(wrongVersion);
                Assert.Throws<InvalidDataException>(() => ReplayCodec.Decode(wrongVersion));
            });
        }
    }
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FrameSync.Abstractions;
using FrameSync.Runtime;

namespace FrameSync.Tests
{
    internal static class CheckpointTests
    {
        public static IEnumerable<(string, Action)> Cases()
        {
            yield return ("checkpoint.restores_an_intermediate_frame_and_replays_tail", () => {
                var r = Fixture.Runner();
                r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                var saved = r.CaptureCheckpoint();
                var tail = Fixture.Frame(2, Fixture.Input(0, 3), Fixture.Input(1, 4));
                r.Advance(tail); var expected = r.CaptureCheckpoint();
                Assert.Equal(1234L, r.InspectState().Value);
                r.Restore(saved); Assert.Equal(1UL, r.Tick); Assert.Equal(12L, r.InspectState().Value);
                r.Advance(tail);
                Assert.Bytes(CheckpointCodec.Encode(expected), CheckpointCodec.Encode(r.CaptureCheckpoint()));
            });
            yield return ("checkpoint.roundtrips_into_a_new_runtime", () => {
                var r = Fixture.Runner(); r.Advance(Fixture.Frame(1, Fixture.Input(0, 5), Fixture.Input(1, 6)));
                var bytes = CheckpointCodec.Encode(r.CaptureCheckpoint());
                var loaded = CheckpointCodec.Decode(bytes);
                var fresh = Fixture.Runner(); fresh.Restore(loaded);
                Assert.Equal(56L, fresh.InspectState().Value); Assert.Equal(1UL, fresh.Tick);
                Assert.Bytes(bytes, CheckpointCodec.Encode(fresh.CaptureCheckpoint()));
            });
            yield return ("checkpoint.preserves_initialization_data_for_new_instances", () => {
                var module = new TestModule(); byte seen = 0;
                module.OnInitialize = initialization => seen = initialization.Data.Length == 0 ? (byte)0 : initialization.Data.Span[0];
                var source = new SimulationRunner<TestState>(module.Module(), Fixture.Session(), initialization: new ByteString(new byte[] { 7 }));
                var bytes = CheckpointCodec.Encode(source.CaptureCheckpoint());
                var fresh = Fixture.Runner(module); Assert.Equal((byte)0, seen);
                fresh.Restore(CheckpointCodec.Decode(bytes));
                Assert.Equal((byte)7, seen);
                Assert.Bytes(bytes, CheckpointCodec.Encode(fresh.CaptureCheckpoint()));
                Assert.True(CheckpointCodec.Hash(source.CaptureCheckpoint()) != CheckpointCodec.Hash(Fixture.Runner().CaptureCheckpoint()));
            });
            yield return ("checkpoint.restores_bounded_history_and_missing_counts", () => {
                var r = Fixture.Runner(limits: new RuntimeLimits(historyFrames: 2));
                var zero = Fixture.Input(0, 0, InputOrigin.MissingSubstitute);
                r.Advance(Fixture.Frame(1, zero, Fixture.Input(1, 0))); var saved = r.CaptureCheckpoint();
                r.Advance(Fixture.Frame(2, zero, Fixture.Input(1, 0)));
                r.Advance(Fixture.Frame(3, zero, Fixture.Input(1, 0)));
                var end = r.CaptureCheckpoint();
                Assert.Equal(2, end.InputHistory.Count); Assert.Equal(2UL, end.InputHistory[0].Tick);
                Assert.Equal(3UL, end.MissingInputCounts[0]);
                r.Restore(saved); var restored = r.CaptureCheckpoint();
                Assert.Equal(1, restored.InputHistory.Count); Assert.Equal(1UL, restored.MissingInputCounts[0]);
                r.Advance(Fixture.Frame(2, Fixture.Input(0, 0), Fixture.Input(1, 0)));
                Assert.Equal(0UL, r.CaptureCheckpoint().MissingInputCounts[0]);
            });
            yield return ("checkpoint.hash_includes_framework_input_source", () => {
                var a = Fixture.Runner(); var b = Fixture.Runner();
                a.Advance(Fixture.Frame(1, Fixture.Input(0, 0), Fixture.Input(1, 0)));
                b.Advance(Fixture.Frame(1, Fixture.Input(0, 0, InputOrigin.MissingSubstitute), Fixture.Input(1, 0)));
                Assert.Equal(a.InspectState().Value, b.InspectState().Value);
                var ah = CheckpointCodec.Hash(a.CaptureCheckpoint()); var bh = CheckpointCodec.Hash(b.CaptureCheckpoint());
                Assert.Equal(64, ah.Length); Assert.True(ah != bh, "Framework history must affect the state hash.");
            });
            yield return ("checkpoint.saved_data_is_not_aliased", () => {
                var r = Fixture.Runner(); r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                var saved = r.CaptureCheckpoint(); var encoded = CheckpointCodec.Encode(saved);
                saved.ApplicationState.ToArray()[0] = 99; encoded[0] ^= 1;
                r.Advance(Fixture.Frame(2, Fixture.Input(0, 3), Fixture.Input(1, 4)));
                r.Restore(saved); r.InspectState().Value = -1;
                Assert.Equal(12L, r.InspectState().Value);
            });
            yield return ("checkpoint.rejects_incompatible_restore_without_mutation", () => {
                var source = Fixture.Runner(); source.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                var target = new SimulationRunner<TestState>(new TestModule().Module("different"), Fixture.Session());
                Assert.Throws<ArgumentException>(() => target.Restore(source.CaptureCheckpoint()));
                Assert.Equal(0UL, target.Tick); Assert.Equal(0L, target.InspectState().Value);
            });
            yield return ("checkpoint.rejects_changed_session_config", () => {
                var saved = Fixture.Runner().CaptureCheckpoint();
                var target = new SimulationRunner<TestState>(new TestModule().Module(),
                    new SessionDescriptor(Fixture.SessionId, 1, 60, Fixture.Members));
                Assert.Throws<ArgumentException>(() => target.Restore(saved)); Assert.Equal(0UL, target.Tick);
            });
            yield return ("checkpoint.restore_failure_preserves_existing_runtime", () => {
                var module = new TestModule(); var r = Fixture.Runner(module);
                var saved = r.CaptureCheckpoint(); r.Advance(Fixture.Frame(1, Fixture.Input(0, 3), Fixture.Input(1, 4)));
                module.ThrowOnRestore = true;
                Assert.Throws<InvalidOperationException>(() => r.Restore(saved));
                Assert.Equal(1UL, r.Tick); Assert.Equal(34L, r.InspectState().Value);
            });
            yield return ("checkpoint.explicit_restore_recovers_a_faulted_runtime", () => {
                var module = new TestModule(); var r = Fixture.Runner(module);
                var saved = r.CaptureCheckpoint(); module.ThrowOnStep = true;
                Assert.Throws<InvalidOperationException>(() => r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2))));
                Assert.Throws<InvalidOperationException>(() => r.CaptureCheckpoint());
                module.ThrowOnStep = false; r.Restore(saved); Assert.True(!r.IsFaulted);
                r.Advance(Fixture.Frame(1, Fixture.Input(0, 1), Fixture.Input(1, 2)));
                Assert.Equal(12L, r.InspectState().Value);
            });
            yield return ("checkpoint.rejects_corruption_truncation_and_trailing_data", () => {
                var bytes = CheckpointCodec.Encode(Fixture.Runner().CaptureCheckpoint());
                Assert.True(bytes.Length > 32, "The checkpoint must have an actual envelope.");
                var bad = (byte[])bytes.Clone(); bad[10] ^= 1;
                Assert.Throws<InvalidDataException>(() => CheckpointCodec.Decode(bad));
                Assert.Throws<InvalidDataException>(() => CheckpointCodec.Decode(bytes.AsSpan(0, bytes.Length - 1)));
                var extended = new byte[bytes.Length + 1]; bytes.CopyTo(extended, 0);
                Assert.Throws<InvalidDataException>(() => CheckpointCodec.Decode(extended));
            });
            yield return ("checkpoint.rejects_forged_lengths_even_with_valid_checksum", () => {
                var bytes = CheckpointCodec.Encode(Fixture.Runner().CaptureCheckpoint());
                Assert.True(bytes.Length > 42);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6), int.MaxValue);
                Resign(bytes);
                Assert.Throws<InvalidDataException>(() => CheckpointCodec.Decode(bytes));
            });
            yield return ("checkpoint.rejects_unsupported_format_and_byte_limit", () => {
                var bytes = CheckpointCodec.Encode(Fixture.Runner().CaptureCheckpoint());
                Assert.True(bytes.Length > 256);
                Assert.Throws<InvalidDataException>(() => CheckpointCodec.Decode(bytes, new RuntimeLimits(maxCheckpointBytes: 256)));
                bytes[4] = 99; Resign(bytes);
                Assert.Throws<InvalidDataException>(() => CheckpointCodec.Decode(bytes));
                Assert.Throws<InvalidOperationException>(() => Fixture.Runner(limits: new RuntimeLimits(maxCheckpointBytes: 256)).CaptureCheckpoint());
            });
            yield return ("checkpoint.rejects_envelope_larger_than_its_embedded_limit", () => {
                var bytes = CheckpointCodec.Encode(Fixture.Runner().CaptureCheckpoint());
                int offset = 6;
                for (int i = 0; i < 6; i++) offset += 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
                offset += 16 + 8 + 4 + 8 + 8; // Session fields, then MaxParticipants and MaxInputBytes.
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + 8), bytes.Length - 1);
                Resign(bytes);
                Assert.Throws<InvalidDataException>(() => CheckpointCodec.Decode(bytes));
            });
        }
        internal static void Resign(byte[] bytes)
        {
            byte[] hash = SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32));
            hash.CopyTo(bytes, bytes.Length - 32);
        }
    }
}

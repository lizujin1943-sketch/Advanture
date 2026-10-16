using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Samples.Counter;

namespace FrameSync.HarnessTests
{
    /// <summary>验证示例自行封装的一帧多操作，不让核心认识 Counter 命令。</summary>
    internal static class CounterBatchTests
    {
        private static readonly SessionDescriptor Session = new SessionDescriptor(
            Guid.Parse("33333333-2222-3333-4444-555555555555"), 1, 30,
            new[] { new Participant(0, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003")) });

        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("counter.batch_encoder_preserves_canonical_bytes_and_detaches_sources", () => {
                var first = CounterGame.Input(0, 37); var third = CounterGame.Input(0, -5);
                var batch = CounterGame.InputBatch(first, CounterGame.Input(1), third, CounterGame.Input(1));
                first[1] = 99; third[1] = 0;
                Equal(true, MixedBatch().AsSpan().SequenceEqual(batch));
                new CounterGame().ValidateCanonical(batch);
                var runner = NewRunner(); runner.Advance(Frame(1, batch));
                Equal(40L, runner.InspectState().Total);
            });
            yield return ("counter.batch_encoder_uses_single_operation_encoding_without_aliasing", () => {
                var source = CounterGame.Input(0, -1000);
                var batch = CounterGame.InputBatch(source); source[1] = 0;
                Equal(true, new byte[] { 0, 24, 252, 255, 255 }.AsSpan().SequenceEqual(batch));
                var runner = NewRunner(); runner.Advance(Frame(1, batch));
                Equal(-1000L, runner.InspectState().Total);
            });
            yield return ("counter.batch_encoder_rejects_invalid_or_nested_operations", () => {
                Throws<ArgumentNullException>(() => CounterGame.InputBatch(null!));
                Throws<ArgumentException>(() => CounterGame.InputBatch());
                var tooMany = new byte[17][];
                for (int i = 0; i < tooMany.Length; i++) tooMany[i] = CounterGame.Input(0, 1);
                Throws<ArgumentException>(() => CounterGame.InputBatch(tooMany));
                Throws<ArgumentException>(() => CounterGame.InputBatch(new byte[][] { null! }));
                foreach (var operation in new[] { Array.Empty<byte>(), new byte[4], new byte[6],
                    CounterGame.Input(2), CounterGame.Input(1, 1), CounterGame.Input(0, 1001),
                    CounterGame.Input(0, -1001), RawBatch(CounterGame.Input(0, 1), CounterGame.Input(1)) })
                    Throws<ArgumentException>(() => CounterGame.InputBatch(CounterGame.Input(0, 11), operation));
            });
            yield return ("counter.named_helpers_produce_neutral_and_random_single_inputs", () => {
                var neutral = CounterGame.NeutralInput(); var random = CounterGame.RandomInput();
                Equal(true, new byte[5].AsSpan().SequenceEqual(neutral));
                Equal(true, new byte[] { 1, 0, 0, 0, 0 }.AsSpan().SequenceEqual(random));
                var runner = NewRunner();
                Equal(0, runner.Advance(Frame(1, neutral)).Events.Count);
                Equal(0x12345678U, runner.InspectState().RandomState);
                runner.Advance(Frame(2, random));
                Equal(7L, runner.InspectState().Total);
                Equal(2UL, runner.InspectState().Steps);
            });
            yield return ("counter.batch_executes_all_operations_once_per_frame", () => {
                var runner = NewRunner();
                // 手算：37 + 第一次随机值 7 - 5 + 第二次随机值 1 = 40。
                var result = runner.Advance(Frame(1, MixedBatch()));
                Equal(40L, runner.InspectState().Total);
                Equal(358294691U, runner.InspectState().RandomState);
                Equal(1UL, runner.InspectState().Steps);
                Equal(1, result.Events.Count);
                Equal(40L, BinaryPrimitives.ReadInt64LittleEndian(result.Events[0].Payload.Span));
            });
            yield return ("counter.batch_preserves_operation_order", () => {
                var game = new CounterGame(); var model = game.Create();
                model.Initialize(new SimulationInitialization(Session, ByteString.Empty));
                model.Restore(new CounterState(long.MaxValue - 3, 0x12345678U, 0));
                // 先减再加可完成；倒序时第一项溢出，不能排序或合并后再执行。
                var safe = RawBatch(CounterGame.Input(0, -4), CounterGame.Input(0, 4));
                game.ValidateCanonical(safe);
                model.Step(new FrameContext(1, 1, 30), Inputs(safe));
                Equal(long.MaxValue - 3, model.Capture().Total);
                Equal(1UL, model.Capture().Steps);
                var reversed = RawBatch(CounterGame.Input(0, 4), CounterGame.Input(0, -4));
                game.ValidateCanonical(reversed);
                Throws<OverflowException>(() => model.Step(new FrameContext(2, 2, 30), Inputs(reversed)));
            });
            yield return ("counter.batch_accepts_sixteen_operations", () => {
                var operations = new byte[16][];
                for (int i = 0; i < operations.Length; i++) operations[i] = CounterGame.Input(0, 1);
                var runner = NewRunner(); runner.Advance(Frame(1, CounterGame.InputBatch(operations)));
                Equal(16L, runner.InspectState().Total);
                Equal(1UL, runner.InspectState().Steps);
            });
            yield return ("counter.batch_rejects_noncanonical_bytes_before_any_operation", () => {
                var tooMany = new byte[17][];
                for (int i = 0; i < tooMany.Length; i++) tooMany[i] = CounterGame.Input(0, 1);
                var canonical = RawBatch(CounterGame.Input(0, 11), CounterGame.Input(1));
                var extra = new byte[canonical.Length + 1]; canonical.CopyTo(extra, 0);
                var wrongTag = (byte[])canonical.Clone(); wrongTag[0] = 3;
                var wrongCount = (byte[])canonical.Clone(); wrongCount[1] = 3;
                var invalid = new[] {
                    Array.Empty<byte>(), new byte[] { 2 }, new byte[] { 2, 0 },
                    RawBatch(CounterGame.Input(0, 11)), RawBatch(tooMany),
                    canonical.AsSpan(0, canonical.Length - 1).ToArray(), extra, wrongTag, wrongCount,
                    RawBatch(CounterGame.Input(0, 11), CounterGame.Input(2)),
                    RawBatch(CounterGame.Input(0, 11), CounterGame.Input(1, 1)),
                    RawBatch(CounterGame.Input(0, 11), CounterGame.Input(0, 1001)),
                    RawBatch(CounterGame.Input(0, 11), CounterGame.Input(0, -1001))
                };
                var runner = NewRunner(); string before = CheckpointCodec.Hash(runner.CaptureCheckpoint());
                foreach (var bytes in invalid)
                {
                    Throws<ArgumentException>(() => runner.Advance(Frame(1, bytes)));
                    Equal(0UL, runner.Tick); Equal(false, runner.IsFaulted);
                    Equal(before, CheckpointCodec.Hash(runner.CaptureCheckpoint()));
                }
            });
            yield return ("counter.batch_neutral_operations_do_not_consume_randomness", () => {
                var runner = NewRunner();
                var result = runner.Advance(Frame(1, RawBatch(CounterGame.Input(0), CounterGame.Input(0))));
                Equal(0L, runner.InspectState().Total);
                Equal(0x12345678U, runner.InspectState().RandomState);
                Equal(1UL, runner.InspectState().Steps); Equal(0, result.Events.Count);
                // 原有单参数 Input(1) 仍是随机命令；不能被新增 int 重载改变含义。
                runner.Advance(Frame(2, CounterGame.Input(1)));
                Equal(7L, runner.InspectState().Total);
                Equal(2274908837U, runner.InspectState().RandomState);
            });
            yield return ("counter.batch_restores_and_replays_random_progress", () => {
                var runner = NewRunner(); var recorder = new ReplayRecorder(runner.CaptureCheckpoint());
                var first = Frame(1, MixedBatch()); runner.Advance(first); recorder.Append(first);
                var saved = CheckpointCodec.Decode(CheckpointCodec.Encode(runner.CaptureCheckpoint()));
                var second = Frame(2, RawBatch(CounterGame.Input(1), CounterGame.Input(0, -8)));
                runner.Advance(second); recorder.Append(second);
                string expected = CheckpointCodec.Hash(runner.CaptureCheckpoint());
                runner.Restore(saved); runner.Advance(second);
                Equal(expected, CheckpointCodec.Hash(runner.CaptureCheckpoint()));
                var recording = ReplayCodec.Decode(ReplayCodec.Encode(recorder.Build()));
                var replayed = ReplayPlayer.Play(NewRunner(), recording);
                Equal(expected, CheckpointCodec.Hash(replayed));
            });
            yield return ("counter.batch_version_rejects_legacy_input_manifest", () => {
                var game = new CounterGame();
                var legacy = new SimulationModule<CounterState>(
                    new CompatibilityManifest("sample-counter", determinismHash: "xorshift32-v1"), game, game, game);
                var checkpoint = new SimulationRunner<CounterState>(legacy, Session).CaptureCheckpoint();
                Throws<ArgumentException>(() => NewRunner().Restore(checkpoint));
            });
        }

        private static SimulationRunner<CounterState> NewRunner() =>
            new SimulationRunner<CounterState>(CounterGame.CreateModule(), Session);
        private static InputFrame Frame(ulong tick, byte[] bytes) =>
            new InputFrame(Session.SessionId, Session.Epoch, tick, new[] { new RecordedInput(0, 1, bytes) });
        private static SimulationInput[] Inputs(byte[] bytes) =>
            new[] { new SimulationInput(Session.Participants[0], new ByteString(bytes)) };
        private static byte[] MixedBatch() => new byte[] {
            2, 4, 0, 37, 0, 0, 0, 1, 0, 0, 0, 0,
            0, 251, 255, 255, 255, 1, 0, 0, 0, 0
        };
        // 仅为构造合法和畸形网络输入；不能调用被测编码器得到预期字节。
        private static byte[] RawBatch(params byte[][] operations)
        {
            var bytes = new byte[2 + operations.Length * 5]; bytes[0] = 2; bytes[1] = (byte)operations.Length;
            for (int i = 0; i < operations.Length; i++) operations[i].CopyTo(bytes, 2 + i * 5);
            return bytes;
        }
        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"Expected {expected}; actual {actual}.");
        }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FrameSync.Harness;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Samples.Counter;
using FrameSync.Samples.Grid;

namespace FrameSync.HarnessTests
{
    internal static class Program
    {
        private static readonly SessionDescriptor Session = new SessionDescriptor(
            Guid.Parse("11111111-2222-3333-4444-555555555555"), 1, 30,
            new[] { new Participant(0, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")) });
        private static InputFrame Frame(ulong tick, byte[] bytes) =>
            new InputFrame(Session.SessionId, 1, tick, new[] { new RecordedInput(0, 1, bytes) });
        private static void Equal<T>(T expected, T actual)
        { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}."); }
        private static void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

        private static IEnumerable<(string Name, Action Run)> Cases()
        {
            foreach (var test in CounterBatchTests.Cases()) yield return test;
            yield return ("counter.hand_calculated_add_and_random_sequence", () => {
                var r = new SimulationRunner<CounterState>(CounterGame.CreateModule(), Session);
                r.Advance(Frame(1, CounterGame.Input(0, 37)));
                Equal(37L, r.InspectState().Total); Equal(1UL, r.InspectState().Steps);
                r.Advance(Frame(2, CounterGame.Input(1)));
                Equal(44L, r.InspectState().Total); Equal(2274908837U, r.InspectState().RandomState);
            });
            yield return ("counter.restores_random_progress", () => {
                var r = new SimulationRunner<CounterState>(CounterGame.CreateModule(), Session);
                r.Advance(Frame(1, CounterGame.Input(1))); var saved = r.CaptureCheckpoint();
                r.Advance(Frame(2, CounterGame.Input(1)));
                Equal(8L, r.InspectState().Total); Equal(358294691U, r.InspectState().RandomState);
                var hash = CheckpointCodec.Hash(r.CaptureCheckpoint());
                r.Restore(saved); r.Advance(Frame(2, CounterGame.Input(1)));
                Equal(hash, CheckpointCodec.Hash(r.CaptureCheckpoint()));
            });
            yield return ("counter.rejects_invalid_input_before_mutation", () => {
                var r = new SimulationRunner<CounterState>(CounterGame.CreateModule(), Session);
                Throws<ArgumentException>(() => r.Advance(Frame(1, CounterGame.Input(2))));
                Throws<ArgumentException>(() => r.Advance(Frame(1, CounterGame.Input(1, 3))));
                Throws<ArgumentException>(() => r.Advance(Frame(1, CounterGame.Input(0, 1001))));
                Equal(0UL, r.Tick);
            });
            yield return ("counter.rejects_invalid_state_encoding", () => {
                var codec = new CounterGame();
                Throws<InvalidDataException>(() => codec.Decode(new byte[19]));
                Throws<InvalidDataException>(() => codec.Decode(new byte[20]));
            });
            yield return ("grid.hand_calculated_motion_and_restore", () => {
                var r = new SimulationRunner<GridState>(GridGame.CreateModule(), Session);
                var first = r.Advance(Frame(1, GridGame.Input(1, -1))); var saved = r.CaptureCheckpoint();
                Equal(1, r.InspectState().Points.Count); Equal(1, r.InspectState().Points[0].X);
                Equal(-1, r.InspectState().Points[0].Y); Equal(1, first.Events.Count);
                r.Advance(Frame(2, GridGame.Input(-1, 0))); Equal(0, r.InspectState().Points[0].X);
                var hash = CheckpointCodec.Hash(r.CaptureCheckpoint());
                r.Restore(saved); r.Advance(Frame(2, GridGame.Input(-1, 0)));
                Equal(hash, CheckpointCodec.Hash(r.CaptureCheckpoint()));
            });
            yield return ("grid.rejects_out_of_range_input", () => {
                var r = new SimulationRunner<GridState>(GridGame.CreateModule(), Session);
                Throws<ArgumentException>(() => r.Advance(Frame(1, GridGame.Input(2, 0))));
                Throws<ArgumentException>(() => r.Advance(Frame(1, new byte[] { 1 })));
                Equal(0UL, r.Tick);
            });
            yield return ("grid.encoded_state_is_complete_and_detached", () => {
                var codec = new GridGame(); var point = new GridPoint(Session.Participants[0].ParticipantId, 2, -3);
                var source = new[] { point }; var state = new GridState(9, source);
                source[0] = new GridPoint(point.ParticipantId, 99, 99);
                byte[] bytes = codec.Encode(state); var decoded = codec.Decode(bytes);
                Equal(9UL, decoded.Steps); Equal(2, decoded.Points[0].X); Equal(-3, decoded.Points[0].Y);
                Equal(true, bytes.AsSpan().SequenceEqual(codec.Encode(decoded)));
                Throws<InvalidDataException>(() => codec.Decode(bytes.AsSpan(0, bytes.Length - 1)));
            });
            yield return ("samples.cannot_restore_another_games_checkpoint", () => {
                var counter = new SimulationRunner<CounterState>(CounterGame.CreateModule(), Session);
                var grid = new SimulationRunner<GridState>(GridGame.CreateModule(), Session);
                Throws<ArgumentException>(() => grid.Restore(counter.CaptureCheckpoint()));
            });
            yield return ("cli.rejects_bad_or_conflicting_options", () => {
                foreach (var args in new[] { new[] { "--unknown" }, new[] { "--frames", "0" },
                    new[] { "--frames", "100001" }, new[] { "--output" },
                    new[] { "--frames", "12", "--frames", "13" },
                    new[] { "--replay", "unused", "--frames", "10" } })
                    Equal(1, HarnessApp.Run(args, new StringWriter()));
            });
            yield return ("cli.help_is_discoverable", () => {
                var output = new StringWriter(); Equal(0, HarnessApp.Run(new[] { "--help" }, output));
                Equal(true, output.ToString().Contains("--replay", StringComparison.Ordinal));
            });
            yield return ("cli.exports_restores_replays_and_reports", () => InTemporaryDirectory(path => {
                string report = Path.Combine(path, "report.json");
                var output = new StringWriter();
                Equal(0, HarnessApp.Run(new[] { "--frames", "17", "--output", path, "--report", report }, output));
                using var json = JsonDocument.Parse(File.ReadAllText(report));
                Equal(2, json.RootElement.GetProperty("Runs").GetArrayLength());
                foreach (var run in json.RootElement.GetProperty("Runs").EnumerateArray())
                {
                    Equal(17UL, run.GetProperty("Frames").GetUInt64());
                    Equal(true, run.GetProperty("RestoreChecks").GetInt32() >= 3);
                    string file = Path.Combine(path, run.GetProperty("Name").GetString() + ".fsrp");
                    string expected = run.GetProperty("FinalHash").GetString()!;
                    Equal(0, HarnessApp.Run(new[] { "--replay", file, "--expected-hash", expected }, new StringWriter()));
                    Equal(1, HarnessApp.Run(new[] { "--replay", file, "--expected-hash", new string('0', 64) }, new StringWriter()));
                    var recording = ReplayCodec.Decode(File.ReadAllBytes(file));
                    Equal(17, recording.Frames.Count);
                    var bytes = File.ReadAllBytes(file); bytes[0] ^= 1; File.WriteAllBytes(file, bytes);
                    Equal(1, HarnessApp.Run(new[] { "--replay", file }, new StringWriter()));
                }
            }));
            yield return ("cli.rejects_oversized_file_before_loading", () => InTemporaryDirectory(path => {
                string file = Path.Combine(path, "large.fsrp");
                using (var stream = File.Create(file)) stream.SetLength(new ReplayLimits().MaxBytes + 1L);
                Equal(1, HarnessApp.Run(new[] { "--replay", file }, new StringWriter()));
            }));
            yield return ("cli.rejects_report_collisions_before_any_output_write", () => InTemporaryDirectory(path => {
                foreach (string name in new[] { "counter", "grid" })
                foreach (string extension in new[] { ".fsrp", ".fscp", ".sha256" })
                {
                    string target = Path.Combine(path, name + extension);
                    File.WriteAllText(target, "existing content");
                    Equal(1, HarnessApp.Run(new[] { "--frames", "1", "--output", path,
                        "--report", Path.Combine(path, ".", name + extension) }, new StringWriter()));
                    Equal("existing content", File.ReadAllText(target));
                    Equal(1, Directory.GetFiles(path).Length);
                    File.Delete(target);
                }
            }));
        }

        private static void InTemporaryDirectory(Action<string> test)
        {
            string path = Path.Combine(Path.GetTempPath(), "framesync-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try { test(path); } finally { Directory.Delete(path, true); }
        }

        private static int Main()
        {
            int passed = 0, failed = 0;
            foreach (var test in Cases())
            {
                try { test.Run(); Console.WriteLine("PASS " + test.Name); passed++; }
                catch (Exception error) { Console.WriteLine("FAIL " + test.Name + ": " + error.Message); failed++; }
            }
            Console.WriteLine($"Sample/CLI tests: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Samples.Counter;
using FrameSync.Samples.Grid;

namespace FrameSync.Harness
{
    public static class HarnessApp
    {
        public static int Run(string[] args, TextWriter output)
        {
            try
            {
                var options = HarnessOptions.Parse(args);
                if (options.Help)
                {
                    output.WriteLine("HeadlessHarness [--frames 1..100000] [--output directory] [--report report.json]");
                    output.WriteLine("HeadlessHarness --replay file.fsrp [--expected-hash SHA256] [--report report.json]");
                    return 0;
                }
                ValidateReportPath(options);
                var runs = new List<RunReport>();
                if (options.Replay != null)
                {
                    var recording = ReplayCodec.Decode(ReadBounded(options.Replay, new ReplayLimits().MaxBytes));
                    var run = recording.Initial.Manifest.ApplicationId switch
                    {
                        "sample-counter" => Replay("counter", CounterGame.CreateModule(), recording),
                        "sample-grid" => Replay("grid", GridGame.CreateModule(), recording),
                        _ => throw new InvalidDataException("This tool has no module for the recorded application.")
                    };
                    if (options.ExpectedHash != null && options.ExpectedHash != run.FinalHash)
                        throw new InvalidDataException("Final checkpoint hash differs from --expected-hash.");
                    runs.Add(run);
                }
                else
                {
                    runs.Add(Verify("counter", CounterGame.CreateModule(), CounterInput, options));
                    runs.Add(Verify("grid", GridGame.CreateModule(), GridInput, options));
                }
                foreach (var run in runs)
                    output.WriteLine($"PASS {run.Name}: frames={run.Frames}, restores={run.RestoreChecks}, hash={run.FinalHash}");
                if (options.Report != null)
                {
                    EnsureParent(options.Report);
                    File.WriteAllText(options.Report, JsonSerializer.Serialize(new {
                        FormatVersion = 1, Environment = RuntimeInformation.OSDescription,
                        Runtime = RuntimeInformation.FrameworkDescription, Runs = runs
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                return 0;
            }
            catch (Exception error)
            {
                output.WriteLine("FAIL " + error.GetType().Name + ": " + error.Message);
                return 1;
            }
        }

        private static RunReport Verify<TState>(string name, SimulationModule<TState> module,
            Func<ulong, ushort, byte[]> input, HarnessOptions options)
        {
            var runner = new SimulationRunner<TState>(module, CreateSession());
            var initial = runner.CaptureCheckpoint(); var recorder = new ReplayRecorder(initial);
            int count = options.Frames;
            var saved = new List<Checkpoint> { initial };
            var saveTicks = new HashSet<ulong> { 1, (ulong)(count / 3), (ulong)(count / 2), (ulong)(count - 1), (ulong)count };
            var hashes = new SortedDictionary<ulong, string>();
            var eventHashes = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                ulong tick = (ulong)i + 1; var frame = MakeFrame(runner.Session, tick, input);
                eventHashes[i] = EventHash(runner.Advance(frame)); recorder.Append(frame);
                if (tick % 30 == 0 || i == count - 1) hashes.Add(tick, CheckpointCodec.Hash(runner.CaptureCheckpoint()));
                if (saveTicks.Contains(tick)) saved.Add(runner.CaptureCheckpoint());
            }
            var expected = runner.CaptureCheckpoint();
            var recordingBytes = ReplayCodec.Encode(recorder.Build());
            var recording = ReplayCodec.Decode(recordingBytes);
            // Every restoration starts with a new instance, including the initial and final boundaries.
            foreach (var checkpoint in saved)
            {
                var restored = new SimulationRunner<TState>(module, runner.Session, runner.Limits);
                restored.Restore(CheckpointCodec.Decode(CheckpointCodec.Encode(checkpoint)));
                for (int i = checked((int)checkpoint.Tick); i < count; i++)
                {
                    var result = restored.Advance(recording.Frames[i]);
                    if (!EventHash(result).AsSpan().SequenceEqual(eventHashes[i]))
                        throw new InvalidDataException($"{name} events differ after restoring frame {checkpoint.Tick}, at {result.Tick}.");
                    if (hashes.TryGetValue(result.Tick, out string? reference) && CheckpointCodec.Hash(restored.CaptureCheckpoint()) != reference)
                        throw new InvalidDataException($"{name} state differs at frame {result.Tick}.");
                }
                if (!CheckpointCodec.Encode(expected).AsSpan().SequenceEqual(CheckpointCodec.Encode(restored.CaptureCheckpoint())))
                    throw new InvalidDataException(name + " final checkpoint bytes differ.");
            }
            var replayed = ReplayPlayer.Play(new SimulationRunner<TState>(module, runner.Session, runner.Limits), recording);
            string finalHash = CheckpointCodec.Hash(expected);
            if (CheckpointCodec.Hash(replayed) != finalHash) throw new InvalidDataException(name + " full replay differs.");
            if (options.Output != null)
            {
                Directory.CreateDirectory(options.Output);
                File.WriteAllBytes(Path.Combine(options.Output, name + ".fsrp"), recordingBytes);
                File.WriteAllBytes(Path.Combine(options.Output, name + ".fscp"), CheckpointCodec.Encode(expected));
                File.WriteAllText(Path.Combine(options.Output, name + ".sha256"), finalHash + "\n");
            }
            return new RunReport(name, (ulong)count, finalHash, saved.Count, hashes);
        }

        private static RunReport Replay<TState>(string name, SimulationModule<TState> module, ReplayRecording recording)
        {
            var runner = new SimulationRunner<TState>(module, recording.Initial.Session, recording.Initial.Limits);
            runner.Restore(recording.Initial);
            var hashes = new SortedDictionary<ulong, string>();
            foreach (var frame in recording.Frames)
            {
                runner.Advance(frame);
                if (runner.Tick % 30 == 0) hashes.Add(runner.Tick, CheckpointCodec.Hash(runner.CaptureCheckpoint()));
            }
            string hash = CheckpointCodec.Hash(runner.CaptureCheckpoint()); hashes[runner.Tick] = hash;
            return new RunReport(name, (ulong)recording.Frames.Count, hash, 0, hashes);
        }

        private static SessionDescriptor CreateSession() => new SessionDescriptor(
            Guid.Parse("f0000000-0000-0000-0000-000000000001"), 1, 30,
            Enumerable.Range(0, 4).Select(i => new Participant((ushort)i,
                Guid.Parse("a0000000-0000-0000-0000-" + (i + 1).ToString("D12", System.Globalization.CultureInfo.InvariantCulture)))).ToArray());

        private static InputFrame MakeFrame(SessionDescriptor session, ulong tick, Func<ulong, ushort, byte[]> encode) =>
            new InputFrame(session.SessionId, session.Epoch, tick, session.Participants.Reverse().Select(p =>
                new RecordedInput(p.Slot, p.Generation, encode(tick, p.Slot),
                    (tick + p.Slot) % 97 == 0 ? InputOrigin.MissingSubstitute : InputOrigin.Submitted)).ToArray());
        private static byte[] CounterInput(ulong tick, ushort slot) => (tick + slot) % 97 == 0 ? CounterGame.Input(0) :
            (tick + slot) % 17 == 0 ? CounterGame.Input(1) : CounterGame.Input(0, (int)((tick + slot) % 9) - 4);
        private static byte[] GridInput(ulong tick, ushort slot) => (tick + slot) % 97 == 0 ? GridGame.Input(0, 0) :
            GridGame.Input((sbyte)((int)((tick + slot) % 3) - 1), (sbyte)((int)((tick / 3 + slot) % 3) - 1));

        private static byte[] EventHash(FrameResult result)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(result.Tick); writer.Write(result.Events.Count);
                foreach (var item in result.Events)
                {
                    writer.Write(item.SessionId.ToByteArray()); writer.Write(item.Epoch); writer.Write(item.Tick);
                    writer.Write(item.Index); writer.Write(item.Payload.Length); writer.Write(item.Payload.Span);
                }
            }
            return SHA256.HashData(stream.ToArray());
        }

        private static byte[] ReadBounded(string path, int maxBytes)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maxBytes) throw new InvalidDataException("Recording exceeds the file byte limit.");
            var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new InvalidDataException("Recording changed while being read.");
            return bytes;
        }
        private static void ValidateReportPath(HarnessOptions options)
        {
            if (options.Report == null) return;
            string report = Path.GetFullPath(options.Report);
            if (options.Replay != null && string.Equals(Path.GetFullPath(options.Replay), report, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The report cannot replace the input recording.");
            if (options.Output == null) return;
            foreach (string name in new[] { "counter", "grid" })
            foreach (string extension in new[] { ".fsrp", ".fscp", ".sha256" })
                if (string.Equals(Path.GetFullPath(Path.Combine(options.Output, name + extension)), report, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The report cannot replace an exported recording, checkpoint or hash.");
        }
        private static void EnsureParent(string file) => Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);

        private sealed record RunReport(string Name, ulong Frames, string FinalHash, int RestoreChecks,
            SortedDictionary<ulong, string> PeriodicHashes);
    }
}

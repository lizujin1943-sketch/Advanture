using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using FrameSync.Samples.Counter;
using FrameSync.Samples.Grid;

namespace FrameSync.PredictionHarness
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var options = Options.Parse(args);
                if (options.Help)
                {
                    Console.WriteLine("PredictionHarness [--output directory] [--game all|counter|grid] [--clients all|1|2|4] [--frames 30..1000] [--network chaos|clean]");
                    Console.WriteLine("PredictionHarness --self-test");
                    return 0;
                }
                int selfTests = NetworkChecks.Run();
                if (options.SelfTest) return 0;
                string output = Path.GetFullPath(options.Output); Directory.CreateDirectory(output);
                string[] games = options.Game == "all" ? new[] { "counter", "grid" } : new[] { options.Game };
                int[] counts = options.Clients == "all" ? new[] { 1, 2, 4 } : new[] { int.Parse(options.Clients, CultureInfo.InvariantCulture) };
                var reports = new List<CaseReport>(); var elapsed = Stopwatch.StartNew();
                foreach (string game in games)
                foreach (int clients in counts)
                {
                    int frames = options.Frames ?? (clients == 1 ? 60 : clients == 2 ? 90 : 240);
                    bool faults = options.Network == "chaos";
                    CaseReport report = game == "counter"
                        ? new PredictionScenario<CounterState>(game, clients, frames, faults, output, CounterGame.CreateModule(),
                            CounterGame.NeutralInput(), CounterAction).Run()
                        : new PredictionScenario<GridState>(game, clients, frames, faults, output, GridGame.CreateModule(),
                            GridGame.Input(0, 0), GridAction).Run();
                    reports.Add(report);
                    Console.WriteLine(report.Passed
                        ? $"PASS {report.Name}: frames={report.ServerConfirmedTick}, rollbacks=[{string.Join(",", report.Clients.Select(c => c.RollbackCount))}], ms={report.ElapsedMilliseconds:F1}, hash={report.ServerFinalHash}"
                        : "FAIL " + report.Failure);
                    // 每个用例结束就落报告；后续用例失败不会吞掉已完成的证据。
                    File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new {
                        FormatVersion = 2, Passed = reports.Count == games.Length * counts.Length && reports.All(r => r.Passed),
                        Environment = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription,
                        SelfTestsPassed = selfTests, ExpectedCases = games.Length * counts.Length,
                        ElapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
                        Configuration = new {
                            TickRate = 30, NetworkStepMilliseconds = 5, PredictionWindow = 12,
                            PredictionLead = 6, InputSubmissionLead = 24, MaxFramesPerPoll = 1,
                            LocalInputPolicy = "Freeze every local frame before prediction, including explicit neutral input",
                            MaximumNetworkMillisecondsPerCase = 60000, MaximumQueuedDatagrams = 4096,
                            FaultSchedule = options.Network == "chaos"
                                ? "chaos-v1: send sequence %17 drops; %11 duplicates; delay 5..35ms plus 55ms on %5; duplicate +5ms"
                                : "clean-v1: one copy with a fixed 5ms datagram delay; no injected loss, duplication or jitter"
                        },
                        MemoryDefinition = "ManagedAllocatedBytesDuringRun is the process cumulative managed allocation delta during one sequential Run, including checks and artifact output. MaximumSampledManagedHeapBytes is the absolute GC.GetTotalMemory(false) maximum sampled once per host iteration; it is neither RSS nor a guaranteed heap peak. Scenario construction and report JSON serialization are outside the allocation interval.",
                        Determinism = "Game IDs, action sequence, logical time and datagram fault decisions are fixed. UDP handshake secrets are random but never drive fault scheduling. Wall time and managed memory observations vary between runs.",
                        Cases = reports
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                return reports.All(r => r.Passed) ? 0 : 1;
            }
            catch (Exception error)
            {
                Console.WriteLine("FAIL " + error.GetType().Name + ": " + error.Message); return 1;
            }
        }
        private static byte[] CounterAction(ulong tick, ushort slot) => tick % 15 == 0
            ? CounterGame.RandomInput() : CounterGame.Input(0, checked((int)(tick / 5 % 7) + slot + 1));
        private static byte[] GridAction(ulong tick, ushort slot) => (tick / 5 % 4) switch {
            0 => GridGame.Input(1, 0), 1 => GridGame.Input(0, 1),
            2 => GridGame.Input(-1, 0), _ => GridGame.Input(0, -1)
        };

        private sealed class Options
        {
            internal string Output = "artifacts/initial-core/prediction";
            internal string Game = "all", Clients = "all", Network = "chaos";
            internal int? Frames;
            internal bool Help, SelfTest;
            internal static Options Parse(string[] args)
            {
                var options = new Options(); var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < args.Length; i++)
                {
                    string key = args[i];
                    if (!seen.Add(key)) throw new ArgumentException("Repeated option: " + key);
                    if (key == "--help") { options.Help = true; continue; }
                    if (key == "--self-test") { options.SelfTest = true; continue; }
                    if (key != "--output" && key != "--game" && key != "--clients" && key != "--frames" && key != "--network")
                        throw new ArgumentException("Unknown option: " + key);
                    if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("Missing value for " + key);
                    string value = args[i];
                    switch (key)
                    {
                        case "--output": options.Output = value; break;
                        case "--game": options.Game = value; break;
                        case "--clients": options.Clients = value; break;
                        case "--network": options.Network = value; break;
                        case "--frames":
                            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int frames) || frames < 30 || frames > 1000)
                                throw new ArgumentException("Frames must be in 30..1000.");
                            options.Frames = frames; break;
                    }
                }
                if (options.Game != "all" && options.Game != "counter" && options.Game != "grid") throw new ArgumentException("Unknown game.");
                if (options.Clients != "all" && options.Clients != "1" && options.Clients != "2" && options.Clients != "4") throw new ArgumentException("Clients must be all, 1, 2 or 4.");
                if (options.Network != "chaos" && options.Network != "clean") throw new ArgumentException("Network must be chaos or clean.");
                if (options.SelfTest && args.Length != 1) throw new ArgumentException("--self-test runs without other options.");
                return options;
            }
        }
    }
}

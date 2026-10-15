using System;
using System.Collections.Generic;
using System.IO;
using FrameSync.Tools.SyncHarness;

namespace FrameSync.Tools.SyncHarnessTests
{
    internal static class Program
    {
        private static int Main()
        {
            var cases = new List<(string, Action)> {
                ("demo.default_a_then_b", Default),
                ("demo.interactive_input_executes_on_other_client", Interactive),
                ("demo.interactive_eof_closes_without_frame", Eof),
                ("demo.grid_supports_one_through_four_clients", Grid),
                ("demo.invalid_arguments_fail", Invalid),
                ("demo.help", Help)
            };
            int failed = 0;
            foreach (var test in cases)
            {
                try { test.Item2(); Console.WriteLine("PASS " + test.Item1); }
                catch (Exception e) { failed++; Console.WriteLine("FAIL " + test.Item1 + ": " + e.Message); }
            }
            Console.WriteLine($"Sync harness tests: {cases.Count - failed} passed, {failed} failed."); return failed == 0 ? 0 : 1;
        }
        private static string Run(string[] args, string input = "", int code = 0)
        {
            var output = new StringWriter(); int result = DemoApp.Run(args, new StringReader(input), output);
            if (result != code) throw new Exception($"Expected exit {code}; actual {result}. {output}"); return output.ToString();
        }
        private static void Contains(string value, string expected)
        { if (!value.Contains(expected, StringComparison.Ordinal)) throw new Exception("Missing output: " + expected); }
        private static void Default()
        {
            string output = Run(Array.Empty<string>());
            Contains(output, "B 收到 A 输入: +5"); Contains(output, "frame=1 server=5 A=5 B=5 一致");
            Contains(output, "frame=2 server=8 A=8 B=8 一致"); Contains(output, "PASS game=counter clients=2 frames=2");
        }
        private static void Interactive()
        {
            string output = Run(new[] { "--interactive" }, @"bad
a 5
b 3
q
");
            Contains(output, "输入格式"); Contains(output, "frame=2 server=8 A=8 B=8 一致");
        }
        private static void Eof() { Contains(Run(new[] { "--interactive" }), "frames=0"); }
        private static void Grid()
        {
            for (int i = 1; i <= 4; i++) Contains(Run(new[] { "--game", "grid", "--clients", i.ToString(), "--frames", "20" }), $"PASS game=grid clients={i} frames=20");
        }
        private static void Invalid()
        {
            foreach (string[] args in new[] { new[] { "--unknown" }, new[] { "--clients", "5" }, new[] { "--frames", "0" },
                new[] { "--game", "other" }, new[] { "--frames" }, new[] { "--interactive", "--game", "grid" } })
                Contains(Run(args, code: 1), "ERROR");
        }
        private static void Help() { Contains(Run(new[] { "--help" }), "--interactive"); }
    }
}

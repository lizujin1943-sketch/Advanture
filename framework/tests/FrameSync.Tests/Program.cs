using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameSync.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            IEnumerable<(string Name, Action Run)> cases = RunnerTests.Cases()
                .Concat(CheckpointTests.Cases()).Concat(ReplayTests.Cases()).Concat(TransportTests.Cases())
                .Concat(ProtobufSessionTests.Cases()).Concat(SessionTests.Cases()).Concat(SessionBoundaryTests.Cases()).Concat(SessionFailureTests.Cases()).Concat(MembershipTests.Cases()).Concat(SessionMembershipTests.Cases()).Concat(LiveSessionTests.Cases()).Concat(PredictionTests.Cases()).Concat(RestartTests.Cases()).Concat(DiagnosticsTests.Cases());
            if (args.Length > 0) cases = cases.Where(t => t.Name.StartsWith(args[0], StringComparison.Ordinal));
            int passed = 0, failed = 0;
            foreach (var test in cases)
            {
                try { test.Run(); Console.WriteLine("PASS " + test.Name); passed++; }
                catch (Exception e) { Console.WriteLine("FAIL " + test.Name + ": " + e.Message); failed++; }
            }
            Console.WriteLine($"Tests: {passed} passed, {failed} failed.");
            return failed == 0 && passed > 0 ? 0 : 1;
        }
    }
}

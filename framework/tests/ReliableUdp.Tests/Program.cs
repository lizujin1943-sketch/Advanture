using System;
using System.Collections.Generic;
using System.Linq;
namespace ReliableUdp.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            IEnumerable<(string, Action)> cases = CodecTests.Cases().Concat(ProtocolTests.Cases()).Concat(AdversarialTests.Cases()).Concat(LivenessTests.Cases());
            if (args.Length > 0) cases = cases.Where(c => c.Item1.StartsWith(args[0], StringComparison.Ordinal));
            int passed = 0, failed = 0;
            foreach (var (name, run) in cases)
            { try { run(); Console.WriteLine("PASS " + name); passed++; }
              catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e); failed++; } }
            Console.WriteLine($"UDP tests: {passed} passed, {failed} failed.");
            return failed == 0 && passed > 0 ? 0 : 1;
        }
    }
    internal static class Check
    {
        internal static void True(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
        internal static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
        internal static void Bytes(byte[] expected, ReadOnlySpan<byte> actual) { True(actual.SequenceEqual(expected), "Bytes differ"); }
        internal static void Throws<T>(Action run) where T:Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    }
}

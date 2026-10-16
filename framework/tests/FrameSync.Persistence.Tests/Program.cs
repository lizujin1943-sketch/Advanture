using System;
using System.Collections.Generic;
using System.Linq;

namespace FrameSync.Persistence.Tests
{
    /// <summary>独立持久化测试入口；可用首个参数按名称前缀过滤。</summary>
    internal static class Program
    {
        /// <summary>逐个执行行为测试，以非零退出码报告任一失败。</summary>
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--hold-writer-lock")
                return FileSaveStoreTests.HoldWriterLock(args[1]);
            IEnumerable<(string Name, Action Run)> cases = SaveCodecTests.Cases().Concat(SaveServiceTests.Cases()).Concat(FileSaveStoreTests.Cases());
            if (args.Length > 0) cases = cases.Where(test => test.Name.StartsWith(args[0], StringComparison.Ordinal));
            int passed = 0, failed = 0;
            foreach (var test in cases)
            {
                try { test.Run(); Console.WriteLine("PASS " + test.Name); passed++; }
                catch (Exception error) { Console.WriteLine("FAIL " + test.Name + ": " + error.Message); failed++; }
            }
            Console.WriteLine($"Tests: {passed} passed, {failed} failed.");
            return failed == 0 && passed > 0 ? 0 : 1;
        }
    }
}

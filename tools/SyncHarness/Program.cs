using System;
namespace FrameSync.Tools.SyncHarness
{
    internal static class Program
    {
        private static int Main(string[] args) => DemoApp.Run(args, Console.In, Console.Out);
    }
}

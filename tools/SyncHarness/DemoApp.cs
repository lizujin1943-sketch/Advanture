using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;
using FrameSync.Samples.Counter;
using FrameSync.Samples.Grid;

namespace FrameSync.Tools.SyncHarness
{
    /// <summary>阶段 B 控制台演示入口。玩法判断、输入设备和显示全部留在工具层。</summary>
    public static class DemoApp
    {
        /// <summary>解析参数并运行真实字节链路；任何断言或参数错误均返回非零退出码。</summary>
        /// <param name="args">--game、--clients、--frames 或 --interactive 等参数。</param>
        /// <param name="input">交互输入，可替换成测试提供的文本。</param>
        /// <param name="output">诊断与结果的输出位置。</param>
        /// <returns>成功为 0，参数错误或验证失败为 1。</returns>
        public static int Run(string[] args, TextReader input, TextWriter output)
        {
            try
            {
                var options = Options.Parse(args);
                if (options.Help)
                {
                    output.WriteLine("SyncHarness [--game counter|grid] [--clients 1..4] [--frames 1..100000] [--interactive]");
                    output.WriteLine("默认一个服务器、两个客户端，A +5 后 B +3；--interactive 支持 a 5 / b 3 / q。"); return 0;
                }
                output.WriteLine($"启动：1 个服务器 + {options.Clients} 个客户端；独立模拟，进程内字节通信。");
                if (options.Game == "counter") Counter(options, input, output); else Grid(options, output);
                return 0;
            }
            catch (Exception error) { output.WriteLine("ERROR: " + error.Message); return 1; }
        }

        private static void Counter(Options options, TextReader input, TextWriter output)
        {
            var world = new DemoWorld<CounterState>(CounterGame.CreateModule(), options.Clients, new ByteString(CounterGame.Input(0, 0)));
            long expected = 0;
            void Execute(int actor, int amount, bool display)
            {
                world.Step(actor, CounterGame.Input(0, amount)); expected = checked(expected + amount);
                if (world.ServerState.Total != expected || world.ServerState.Steps != world.Tick) throw new InvalidOperationException("Counter hand-calculated result differs.");
                if (!display) return;
                // 读另一个客户端实际收到的确认帧，不能只回显用户刚输入的文本。
                int observer = options.Clients > 1 && actor == 0 ? 1 : 0;
                int received = BinaryPrimitives.ReadInt32LittleEndian(world.LastClientFrame(observer).Inputs[actor].Payload.Span.Slice(1));
                output.WriteLine($"{Label(observer)} 收到 {Label(actor)} 输入: {received:+0;-0;0}");
                output.Write($"frame={world.Tick} server={world.ServerState.Total}");
                for (int i = 0; i < options.Clients; i++) output.Write($" {Label(i)}={world.ClientState(i).Total}");
                output.WriteLine(" 一致");
            }
            if (options.Interactive)
            {
                output.WriteLine("输入格式：a 5 / b 3（每行推进一帧，未输入者使用中性输入）；q 退出。");
                string? line;
                while ((line = input.ReadLine()) != null)
                {
                    if (line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) break;
                    string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    int actor = parts.Length == 2 && parts[0].Length == 1 ? char.ToUpperInvariant(parts[0][0]) - 'A' : -1;
                    if (actor < 0 || actor >= options.Clients || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount < -1000 || amount > 1000)
                    { output.WriteLine("输入格式不正确，请输入已有客户端字母和 -1000..1000 的整数，例如 a 5。"); continue; }
                    Execute(actor, amount, true);
                }
            }
            else
            {
                for (int f = 1; f <= options.Frames; f++)
                    Execute((f - 1) % options.Clients, f == 1 ? 5 : f == 2 ? 3 : f % 7, f <= 2 || f == options.Frames);
            }
            world.Close(); output.WriteLine($"PASS game=counter clients={options.Clients} frames={world.Tick} hash={world.Hash}");
        }

        private static void Grid(Options options, TextWriter output)
        {
            var world = new DemoWorld<GridState>(GridGame.CreateModule(), options.Clients, new ByteString(GridGame.Input(0, 0)));
            var x = new int[options.Clients]; var y = new int[options.Clients];
            for (int f = 1; f <= options.Frames; f++)
            {
                int actor = (f - 1) % options.Clients; sbyte dx = (sbyte)(f % 2), dy = (sbyte)(f % 2 == 0 ? -1 : 0);
                world.Step(actor, GridGame.Input(dx, dy)); x[actor] += dx; y[actor] += dy;
                var state = world.ServerState;
                if (state.Steps != (ulong)f) throw new InvalidOperationException("Grid tick differs.");
                for (int i = 0; i < options.Clients; i++)
                {
                    GridPoint point = state.Points.Single(p => p.ParticipantId == world.Members[i].ParticipantId);
                    if (point.X != x[i] || point.Y != y[i]) throw new InvalidOperationException("Grid hand-calculated position differs.");
                }
                if (f <= 2 || f == options.Frames)
                    output.WriteLine($"frame={f} {Label(actor)} 输入=({dx},{dy}) 位置=({x[actor]},{y[actor]}) 各端完整状态一致");
            }
            world.Close(); output.WriteLine($"PASS game=grid clients={options.Clients} frames={world.Tick} hash={world.Hash}");
        }
        private static char Label(int index) => (char)('A' + index);

        // 命令行参数属于工具，不进入框架或游戏状态。
        private sealed class Options
        {
            public string Game = "counter";
            public int Clients = 2, Frames = 2;
            public bool Interactive, Help;
            public static Options Parse(string[] args)
            {
                var result = new Options(); var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i]; if (!seen.Add(arg)) throw new ArgumentException("Duplicate option: " + arg);
                    if (arg == "--help") { result.Help = true; continue; }
                    if (arg == "--interactive") { result.Interactive = true; continue; }
                    if (arg != "--game" && arg != "--clients" && arg != "--frames") throw new ArgumentException("Unknown option: " + arg);
                    if (++i >= args.Length) throw new ArgumentException("Missing value for " + arg);
                    if (arg == "--game") result.Game = args[i];
                    else
                    {
                        if (!int.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out int value)) throw new ArgumentException("Expected integer for " + arg);
                        if (arg == "--clients") result.Clients = value; else result.Frames = value;
                    }
                }
                if (result.Game != "counter" && result.Game != "grid") throw new ArgumentException("Game must be counter or grid.");
                if (result.Clients < 1 || result.Clients > 4) throw new ArgumentException("Clients must be 1..4.");
                if (result.Frames < 1 || result.Frames > 100000) throw new ArgumentException("Frames must be 1..100000.");
                if (result.Interactive && (result.Game != "counter" || seen.Contains("--frames"))) throw new ArgumentException("Interactive mode uses counter and does not accept --frames.");
                return result;
            }
        }
    }
}

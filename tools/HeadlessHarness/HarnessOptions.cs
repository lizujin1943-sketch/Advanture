using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameSync.Harness
{
    internal sealed class HarnessOptions
    {
        public int Frames { get; private set; } = 100000;
        public string? Output { get; private set; }
        public string? Replay { get; private set; }
        public string? Report { get; private set; }
        public string? ExpectedHash { get; private set; }
        public bool Help { get; private set; }

        public static HarnessOptions Parse(string[] args)
        {
            var options = new HarnessOptions(); var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (!seen.Add(key)) throw new ArgumentException("Duplicate option: " + key);
                if (key == "--help") { options.Help = true; continue; }
                if (key != "--frames" && key != "--output" && key != "--replay" && key != "--report" && key != "--expected-hash")
                    throw new ArgumentException("Unknown option: " + key);
                if (++i == args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[i]))
                    throw new ArgumentException("Missing value for " + key);
                string value = args[i];
                switch (key)
                {
                    case "--frames":
                        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1 || count > 100000)
                            throw new ArgumentException("--frames must be between 1 and 100000.");
                        options.Frames = count; break;
                    case "--output": options.Output = value; break;
                    case "--replay": options.Replay = value; break;
                    case "--report": options.Report = value; break;
                    case "--expected-hash":
                        if (value.Length != 64 || !Array.TrueForAll(value.ToCharArray(), Uri.IsHexDigit))
                            throw new ArgumentException("Expected a 64-character SHA-256 hash.");
                        options.ExpectedHash = value.ToLowerInvariant(); break;
                }
            }
            if (options.Help && args.Length != 1) throw new ArgumentException("Use --help by itself.");
            if (options.Replay != null && (seen.Contains("--frames") || options.Output != null))
                throw new ArgumentException("--replay cannot be combined with --frames or --output.");
            if (options.ExpectedHash != null && options.Replay == null)
                throw new ArgumentException("--expected-hash requires --replay.");
            return options;
        }
    }
}

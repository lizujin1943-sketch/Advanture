using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.InventoryTests;
internal static partial class Program
{
    private static IEnumerable<(string, Action)> NetworkCases()
    {
        yield return ("inventory_four_udp_clients_earn_buy_split_race_build_and_agree", () => {
            var map = IslandMap.CreatePlatform(); using var host = new IslandHost(map, 0, 4);
            var clients = Enumerable.Range(0, 4).Select(i => new IslandClient(map, IPAddress.Loopback, host.Port, i == 0 ? (ushort)0 : ushort.MaxValue)).ToArray();
            var pilots = clients.Select(_ => new InventorySmokePilot()).ToArray();
            var clock = System.Diagnostics.Stopwatch.StartNew(); TimeSpan last = default;
            try
            {
                while (clock.Elapsed < TimeSpan.FromSeconds(150))
                {
                    var now = last = clock.Elapsed; host.Poll(now);
                    for (int i = 0; i < clients.Length; i++)
                    {
                        int index = i; var c = clients[i];
                        c.Poll(now, tick => c.Session?.LocalParticipant == null ? default : pilots[index].Sample(map, c.Session.InspectState(), c.Slot, tick));
                        Require(c.Failure == null, c.Failure ?? "");
                    }
                    Require(host.Failure == null, host.Failure ?? "");
                    if (host.Session != null && pilots[0].Complete(host.Session.InspectState())) break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session != null, "No room session started.");
                for (int i = 0; i < 200; i++) { host.Poll(last); foreach (var c in clients) c.Poll(last, _ => default); }
                var state = host.Session!.InspectState();
                Require(pilots[0].Complete(state), "Four-client backpack flow did not finish: " + string.Join(";", state.Players.Select(p => $"P{p.Slot}: {p.Coins}G receipt {p.LastInventoryRequest} {p.LastInventoryResult}")));
                Require(state.Tackle!.Inventory.SpentCoins.SequenceEqual(new[] { 200, 0, 0, 0 }), "Purchase used another player's wallet.");
                string hash = CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach (var c in clients) Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint()) == hash, "Confirmed backpack checkpoints diverged.");
                Console.WriteLine($"BACKPACK_UDP tick={host.Session.CaptureCheckpoint().Tick} hash={hash}");
            }
            finally { foreach (var c in clients) c.Dispose(); }
        });
    }
}

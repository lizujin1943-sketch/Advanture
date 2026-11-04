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
            var movedIntoExpandedCells = new bool[4];
            bool eligiblePickupRaceSeen = false;
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
                    if (host.Session != null)
                    {
                        var confirmed = host.Session.InspectState();
                        var inventory = confirmed.Tackle!.Inventory;
                        for (int slot = 0; slot < clients.Length; slot++)
                        {
                            var size = inventory.Size(slot);
                            movedIntoExpandedCells[slot] |= inventory.Items.Any(item => item.Owner == slot && item.Kind == ItemKind.Fishbone &&
                                item.Location == ItemLocation.Backpack && item.X == size.Width - 1 && item.Y == size.Height - 1);
                        }
                        // 开抢前四人都须能拾取同一个包；否则 TooFar 也会成为失败回执，掩盖没有真正争抢的夹具问题。
                        if (confirmed.Players.All(p => p.LastInventoryRequest == (uint)(p.Slot == 0 ? 4 : 3)))
                        {
                            var drops = inventory.Items.Where(item => item.Kind == ItemKind.Fishbone && item.Location == ItemLocation.Ground).ToArray();
                            // 同一地格的四份材料会合成一个 8 单位材料包，四人争抢的是同一个真实包。
                            Require(drops.Length == 1 && drops[0].Count == 8 && confirmed.Players.All(p => drops.All(item =>
                                IslandMap.DistanceSquared(p.X,p.Z,item.WorldX,item.WorldZ) <= (long)InventoryCatalog.PickupDistance*InventoryCatalog.PickupDistance)),
                                "Pickup race did not put all four players within reach of every dropped package.");
                            eligiblePickupRaceSeen = true;
                        }
                    }
                    if (host.Session != null && pilots[0].Complete(host.Session.InspectState())) break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session != null, "No room session started.");
                for (int i = 0; i < 200; i++) { host.Poll(last); foreach (var c in clients) c.Poll(last, _ => default); }
                var state = host.Session!.InspectState();
                Require(pilots[0].Complete(state), "Four-client backpack flow did not finish: " + string.Join(";", state.Players.Select(p => $"P{p.Slot}: {p.Coins}G receipt {p.LastInventoryRequest} {p.LastInventoryResult}")));
                Require(state.Tackle!.Inventory.SpentCoins.SequenceEqual(new[] { 200, 0, 0, 0 }), "Purchase used another player's wallet.");
                Require(movedIntoExpandedCells.All(moved => moved), "A client did not move material into the expanded backpack cells.");
                Require(eligiblePickupRaceSeen, "No four-player eligible pickup race was observed.");
                string hash = CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach (var c in clients) Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint()) == hash, "Confirmed backpack checkpoints diverged.");
                Console.WriteLine($"BACKPACK_UDP tick={host.Session.CaptureCheckpoint().Tick} hash={hash}");
            }
            finally { foreach (var c in clients) c.Dispose(); }
        });
    }
}

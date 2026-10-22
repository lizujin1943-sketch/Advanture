using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.CharacterTests;

internal static partial class Program
{
    private static IEnumerable<(string, Action)> NetworkCases() => BaseNetworkCases().Concat(ApparelNetworkCases());
    private static IEnumerable<(string, Action)> ApparelNetworkCases()
    {
        yield return ("apparel_four_udp_clients_earn_purchase_remove_and_reequip_shoes", () => {
            var map = CharacterMap(); using var host = new IslandHost(map, 0, 4);
            var clients = Enumerable.Range(0, 4).Select(i => new IslandClient(map, IPAddress.Loopback, host.Port, i == 0 ? (ushort)0 : ushort.MaxValue)).ToArray();
            var clock = System.Diagnostics.Stopwatch.StartNew(); TimeSpan last = default; bool sawRemoved = false;
            try
            {
                while (clock.Elapsed < TimeSpan.FromSeconds(100))
                {
                    last = clock.Elapsed; host.Poll(last);
                    foreach (var client in clients)
                    {
                        client.Poll(last, tick => {
                            if (client.Session?.LocalParticipant == null || client.Slot != 0) return default;
                            var state = client.Session.InspectState(); var p = state.Players[0];
                            if (p.LastInventoryRequest == 2)
                            {
                                Require(p.LastInventoryResult == InventoryResult.Success && FishingRules.Attributes(state, p).MoveSpeed == 3000, "Shoe removal did not confirm original speed.");
                                sawRemoved = true;
                            }
                            return ApparelInput(map, state, tick);
                        });
                        Require(client.Failure == null, client.Failure ?? "");
                    }
                    Require(host.Failure == null, host.Failure ?? "");
                    if (host.Session != null && host.Session.InspectState().Players[0].LastInventoryRequest == 3) break;
                    System.Threading.Thread.Sleep(1);
                }
                for (int i = 0; i < 300; i++) { host.Poll(last); foreach (var client in clients) client.Poll(last, _ => default); }
                Require(host.Session != null, "Room never started.");
                var s = host.Session!.InspectState(); var player = s.Players[0];
                Require(sawRemoved && player.LastInventoryRequest == 3 && player.LastInventoryResult == InventoryResult.Success && player.Catches > 0, "Normal fishing/buy/remove/re-equip loop incomplete.");
                Require(FishingRules.Attributes(s, player).MoveSpeed == 3300 && s.Players.Skip(1).All(p => FishingRules.Attributes(s, p).MoveSpeed == 3000), "Shoes affected the wrong player.");
                Require(s.Tackle!.Inventory.Items.Count(i => i.Kind == ItemKind.Apparel) == 1 && s.Tackle.Inventory.SpentCoins[0] == 150 && player.CoinsEarned - player.Coins == 150, "Retry duplicated clothing or charged twice.");
                string hash = CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach (var client in clients) Require(CheckpointCodec.Hash(client.Session!.CaptureCheckpoint()) == hash, "Wearables or attributes diverged across peers.");
                Console.WriteLine($"APPAREL_UDP tick={s.Steps} earned={player.CoinsEarned} hash={hash}");
            }
            finally { foreach (var client in clients) client.Dispose(); }
        });
    }

    private static HookInput ApparelInput(IslandMap map, HookState state, ulong tick)
    {
        var p = state.Players[0]; var tackle = state.Tackle!;
        var shoes = tackle.Inventory.Items.FirstOrDefault(i => i.Owner == 0 && i.Kind == ItemKind.Apparel && i.Definition.Slot == EquipmentSlot.Shoes);
        if (shoes.Id != 0)
        {
            if (p.LastInventoryRequest == 1) return EquipCommand(InventoryAction.Unequip, (int)EquipmentSlot.Shoes, 2);
            return EquipCommand(InventoryAction.Equip, shoes.Id, 3);
        }
        if (p.Phase != HookPhase.Ready || p.Cooldown > 0) return default;
        int offer = Enumerable.Range(0, tackle.Stock.Count).FirstOrDefault(i => !tackle.Stock[i].Sold && tackle.Stock[i].Kind == ItemKind.Apparel &&
            InventoryCatalog.Get(tackle.Stock[i].Kind, tackle.Stock[i].Type).Slot == EquipmentSlot.Shoes, -1);
        Require(offer >= 0, "Initial merchant has no shoes.");
        if (p.Coins >= InventoryCatalog.Get(tackle.Stock[offer].Kind, tackle.Stock[offer].Type).Price)
        {
            if (p.Z < -100) return new HookInput(0, 1000);
            return new HookInput(0, 0, shopping: true, inventory: new InventoryCommand(InventoryAction.BuyAndEquip, offer, batch: tackle.Batch, requestId: 1));
        }
        if (p.Z > -2500) return new HookInput(0, -1000);
        int speed = FishingRules.Speed(state, p), range = FishingRules.Range(map, state, p), lead = (int)Math.Min(30, tick > state.Steps ? tick - state.Steps - 1 : 0);
        foreach (var fish in state.Fish)
        {
            if (fish.Owner >= 0) continue; var future = FishMotion.Predict(fish, map.FishSpots[fish.Id], lead);
            for (int n = 1; n <= Math.Min(120, range * 30 / speed + 1); n++)
            {
                FishMotion.Advance(ref future, map.FishSpots[fish.Id]); if (future.Phase != FishPhase.Jumping || future.Y < 450) continue;
                int distance = HookGame.Sqrt(IslandMap.DistanceSquared(p.X, p.Z, future.X, future.Z));
                if (distance <= range && Math.Abs(distance - n * speed / 30) < speed / 60 + 40 &&
                    (long)FishCatalog.DrainPerTick(fish) * (distance * 30 / tackle.Config.ReelSpeed + 4) < p.Stamina)
                    return new HookInput(0, 0, future.X, future.Z, true);
            }
        }
        return default;
    }
}

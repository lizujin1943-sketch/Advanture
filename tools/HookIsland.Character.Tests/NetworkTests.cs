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
    private static IEnumerable<(string, Action)> BaseNetworkCases()
    {
        yield return ("character_four_udp_clients_sync_equip_unequip_attributes_and_receipts", () => {
            var map = CharacterMap(new CharacterConfig(health: 130, moveSpeed: 3125, armor: 15, hunger: 110));
            using var host = new IslandHost(map, 0, 4);
            var clients = Enumerable.Range(0, 4).Select(i => new IslandClient(map, IPAddress.Loopback, host.Port, i == 0 ? (ushort)0 : ushort.MaxValue)).ToArray();
            var sawUnequipped = new bool[4]; var clock = System.Diagnostics.Stopwatch.StartNew(); TimeSpan last = default;
            try
            {
                while (clock.Elapsed < TimeSpan.FromSeconds(20))
                {
                    last = clock.Elapsed; host.Poll(last);
                    for (int i = 0; i < clients.Length; i++)
                    {
                        var client = clients[i]; int index = i;
                        client.Poll(last, _ => {
                            if (client.Session?.LocalParticipant == null) return default;
                            var state = client.Session.InspectState(); var p = state.Players[client.Slot];
                            if (p.LastInventoryRequest == 0) return EquipCommand(InventoryAction.Unequip, (int)EquipmentSlot.RightHand, 1);
                            if (p.LastInventoryRequest == 1)
                            {
                                Require(p.LastInventoryResult == InventoryResult.Success && p.EquippedRod == 0 && FishingRules.MaxStamina(state, p) == 100 * 30000, "Client missed confirmed unequip/attribute update.");
                                sawUnequipped[index] = true;
                            }
                            // Repeat exactly the same equip receipt after success as well.
                            return EquipCommand(InventoryAction.Equip, client.Slot + 1, 2);
                        });
                        Require(client.Failure == null, client.Failure ?? "");
                    }
                    Require(host.Failure == null, host.Failure ?? "");
                    if (host.Session != null && host.Session.ConfirmedTick > 45 && host.Session.InspectState().Players.All(p => p.LastInventoryRequest == 2)) break;
                    System.Threading.Thread.Sleep(1);
                }
                for (int i = 0; i < 200; i++) { host.Poll(last); foreach (var client in clients) client.Poll(last, _ => default); }
                Require(host.Session != null && sawUnequipped.All(x => x), "A client never completed unequip/equip.");
                var s = host.Session!.InspectState();
                Require(s.Players.All(p => p.LastInventoryResult == InventoryResult.Success && p.EquippedRod == p.Slot + 1 &&
                    p.Vitals.Health == 130 && p.Vitals.Hunger == 110 && p.Stamina < 120 * 30000 &&
                    FishingRules.Attributes(s, p).MoveSpeed == 3125 && FishingRules.Attributes(s, p).Armor == 15), "Character resources were refilled or lost during synchronized equip.");
                Require(s.Tackle!.Inventory.Items.Count == 8, "Repeated requests duplicated or dropped starter items.");
                string expected = CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach (var client in clients) Require(CheckpointCodec.Hash(client.Session!.CaptureCheckpoint()) == expected, "Character/equipment checkpoints differ between clients.");
            }
            finally { foreach (var client in clients) client.Dispose(); }
        });
        yield return ("character_lobby_rejects_different_base_attribute_rules", () => {
            using var host = new IslandHost(CharacterMap(), 0, 2);
            using var client = new IslandClient(CharacterMap(new CharacterConfig(health: 150)), IPAddress.Loopback, host.Port, ushort.MaxValue);
            for (int ms = 0; ms < 1500 && client.Failure == null; ms += 2)
            { var now = TimeSpan.FromMilliseconds(ms); host.Poll(now); client.Poll(now, _ => default); }
            Require(client.Failure != null && host.Session == null, "Mismatched character rules joined one room.");
        });
    }
}

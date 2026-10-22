using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland;
using Advanture.HookIsland.Simulation;
namespace Advanture.HookIsland.InventoryTests;
internal static partial class Program
{
    private static IEnumerable<(string, Action)> InteractionCases()
    {
        yield return ("inventory_ground_selection_recovers_bags_behind_three_overlapping_rods", () => {
            var inventory = new InventorySystem(3);
            for (int slot = 0; slot < 3; slot++)
            {
                inventory.Unequip(slot, EquipmentSlot.RightHand, Ground);
                inventory.Drop(slot, slot + 1, 1, Ground);
                inventory.Unequip(slot, EquipmentSlot.Backpack, Ground);
            }
            for (int slot = 0; slot < 3; slot++)
            {
                var s = inventory.Capture(); var player = new Fisher { Slot = (ushort)slot };
                var nearby = GroundPickupSelection.Nearby(s, player);
                Require(nearby.Length == 6 - slot, "Nearby selector hid available ground items.");
                var command = GroundPickupSelection.Command(s, player, nearby[0]);
                Require(command.Action == InventoryAction.PickupAndEquip, "Bagless quick pickup got stuck on an unpickable rod.");
                Require(inventory.PickupAndEquip(slot, command.Target, Ground, false) == InventoryResult.Success, "Player could not recover bag.");
            }
            Require(inventory.Capture().Items.Count(i => i.Location == ItemLocation.Equipped) == 3, "Bag recovery lost or duplicated an item.");
        });
        yield return ("inventory_expensive_layout_preview_is_cached_until_contents_or_intent_change", () => {
            var inventory = new InventorySystem(1); int coins = 100000;
            inventory.Purchase(0, ItemKind.Backpack, 10, true, true, Ground, ref coins);
            for (int i = 0; i < 14; i++) inventory.Purchase(0, ItemKind.Backpack, 1, false, false, Ground, ref coins);
            var cache = new InventoryEquipmentPreview(); var before = InventoryStateCodec.Encode(inventory.Capture());
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var first = cache.Evaluate(inventory.Capture(), 0, coins, Ground, ItemKind.Backpack, 8, 0, true, true);
            Require(first.Result == InventoryResult.NoSpace || first.Result == InventoryResult.LayoutTooComplex, "Impossible 2x2 packing was accepted.");
            for (int i = 0; i < 100; i++)
                Require(ReferenceEquals(first, cache.Evaluate(inventory.Capture(), 0, coins, Ground, ItemKind.Backpack, 8, 0, true, true)), "Unchanged preview reran layout search.");
            Require(clock.Elapsed < TimeSpan.FromSeconds(3), "Cached layout preview exceeded bounded interactive budget.");
            Require(!ReferenceEquals(first, cache.Evaluate(inventory.Capture(), 0, coins, Ground, ItemKind.Backpack, 10, 0, true, true)), "Changed selection kept stale preview.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Read-only preview mutated inventory.");
        });
    }
}

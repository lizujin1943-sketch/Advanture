using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.CharacterTests;

internal static partial class Program
{
    private static IEnumerable<(string, Action)> EquipmentCases()
    {
        yield return ("character_only_equipped_owned_items_contribute_to_stats", () => {
            var inventory = new InventorySystem(2); int coins = 10000;
            var before = CharacterEquipment.Calculate(new CharacterConfig(), 100, inventory.Capture(), 0);
            Require(before.StaminaMax == 120 * StaminaSystem.UnitsPerPoint, "Starter rod bonus missing.");
            inventory.Purchase(0, ItemKind.Rod, 3, false, false, new Point(0, 0), ref coins);
            inventory.Purchase(1, ItemKind.Rod, 3, true, false, new Point(0, 0), ref coins);
            var stats = CharacterEquipment.Calculate(new CharacterConfig(), 100, inventory.Capture(), 0);
            Require(stats.StaminaMax == before.StaminaMax && stats.MoveSpeed == 3000 && stats.HealthMax == 100, "Stored gear, backpack or teammate altered attributes.");
            var rod = inventory.Capture().Items.Single(i => i.Owner == 0 && i.Kind == ItemKind.Rod && i.Location == ItemLocation.Backpack);
            Require(inventory.Equip(0, rod.Id, false, new Point(0, 0)) == InventoryResult.Success, "Equip failed.");
            Require(CharacterEquipment.Calculate(new CharacterConfig(), 100, inventory.Capture(), 0).StaminaMax == 160 * StaminaSystem.UnitsPerPoint, "New equipped bonus not applied.");
        });
        yield return ("character_equip_preview_does_not_mutate_items_and_respects_slots", () => {
            var inventory = new InventorySystem(1); int coins = 10000;
            inventory.Purchase(0, ItemKind.Rod, 3, false, false, new Point(0, 0), ref coins);
            var snapshot = inventory.Capture(); var rod = snapshot.Items.Single(i => i.Kind == ItemKind.Rod && i.Location == ItemLocation.Backpack);
            byte[] before = InventoryStateCodec.Encode(snapshot);
            var result = CharacterEquipment.Preview(new CharacterConfig(), 100, snapshot, 0,
                new InventoryCommand(InventoryAction.Equip, rod.Id), false, false, out var stats);
            Require(result == InventoryResult.Success && stats.StaminaMax == 160 * StaminaSystem.UnitsPerPoint, "Preview did not show proposed bonus.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Preview wrote authority.");
            Require(CharacterEquipment.CanEquipTo(rod, EquipmentSlot.RightHand) && !CharacterEquipment.CanEquipTo(rod, EquipmentSlot.LeftHand), "Rod accepted incompatible slot.");
            Require(CharacterEquipment.Slots.Count == 8 && CharacterEquipment.Slots.Distinct().Count() == 8, "Eight independent slots required.");
        });
        yield return ("character_full_bag_and_busy_rod_previews_fail_atomically", () => {
            var inventory = new InventorySystem(1); inventory.CreditMaterial(0, ItemKind.Wood, 16 * InventoryCatalog.MaterialStackLimit, new Point(0, 0));
            byte[] before = InventoryStateCodec.Encode(inventory.Capture());
            var command = new InventoryCommand(InventoryAction.Unequip, (int)EquipmentSlot.RightHand);
            var result = CharacterEquipment.Preview(new CharacterConfig(), 100, inventory.Capture(), 0, command, false, false, out var stats);
            Require(result == InventoryResult.NoSpace && stats.StaminaMax == 120 * StaminaSystem.UnitsPerPoint, "Failed unequip changed preview attributes.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Preview lost gear or materials.");
            result = CharacterEquipment.Preview(new CharacterConfig(), 100, inventory.Capture(), 0, command, true, false, out _);
            Require(result == InventoryResult.HookBusy, "Busy rod could be unequipped.");
            var empty = new InventorySystem(1).Capture();
            var removeBag = new InventoryCommand(InventoryAction.Unequip, (int)EquipmentSlot.Backpack);
            result = CharacterEquipment.Preview(new CharacterConfig(), 100, empty, 0, removeBag, true, false, out _);
            Require(result == InventoryResult.Success, "Using a rod unnecessarily blocked an empty backpack.");
            result = CharacterEquipment.Preview(new CharacterConfig(), 100, empty, 0, removeBag, false, true, out _);
            Require(result == InventoryResult.HookBusy, "Preview allowed a pulled character to change backpack.");
        });
    }
}

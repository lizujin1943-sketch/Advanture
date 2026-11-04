using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.InventoryTests;

internal static partial class Program
{
    private static IEnumerable<(string, Action)> BackpackCapacityCases()
    {
        yield return ("inventory_every_backpack_holds_four_times_its_previous_capacity", () => {
            var sizes = new[] { (8, 8), (8, 10), (8, 12), (10, 12), (12, 12), (12, 14), (12, 16), (14, 16), (16, 16), (16, 18) };
            for (int level = 1; level <= sizes.Length; level++)
            {
                var inventory = new InventorySystem(1); int coins = 10000;
                if (level > 1)
                    Require(inventory.Purchase(0, ItemKind.Backpack, level, true, true, Ground, ref coins) == InventoryResult.Success, "Cannot equip capacity fixture.");
                var (width, height) = sizes[level - 1]; var size = inventory.Capture().Size(0);
                Require(size.Width == width && size.Height == height, $"Lv{level} capacity did not quadruple.");
                int amount = width * height * InventoryCatalog.MaterialStackLimit;
                Require(inventory.CreditMaterial(0, ItemKind.Wood, amount + 1, Ground) == amount, $"Lv{level} cannot use every cell.");
                var state = inventory.Capture();
                Require(state.Items.Any(i => i.Location == ItemLocation.Backpack && i.X == width - 1 && i.Y == height - 1), "Last cell was not filled.");
                Require(state.Items.Single(i => i.Kind == ItemKind.Wood && i.Location == ItemLocation.Ground).Count == 1, "Overflow lost or duplicated material.");
                Require(InventoryStateCodec.Decode(InventoryStateCodec.Encode(state)).Size(0).Height == height, "Expanded layout cannot be restored.");
            }
        });
        yield return ("inventory_four_owners_use_far_cells_split_restore_and_repack_without_loss", () => {
            var inventory = new InventorySystem(4);
            for (int owner = 0; owner < 4; owner++)
            {
                int coins = 10000;
                Require(inventory.Purchase(owner, ItemKind.Backpack, 10, true, true, Ground, ref coins) == InventoryResult.Success, "Cannot equip largest backpack.");
                inventory.CreditMaterial(owner, ItemKind.Fishbone, 20, Ground);
                var stack = Stored(inventory, ItemKind.Fishbone, owner);
                var move = InventoryCommand.Decode(new InventoryCommand(InventoryAction.Move, stack.Id, x: 15, y: 17, requestId: 1).Encode());
                Require(inventory.Move(owner, move.Target, move.X, move.Y, move.Rotated) == InventoryResult.Success, "Last cell rejected encoded move.");
                var split = InventoryCommand.Decode(new InventoryCommand(InventoryAction.Split, stack.Id, amount: 7, x: 14, y: 17, requestId: 2).Encode());
                Require(inventory.Split(owner, split.Target, split.Amount, split.X, split.Y) == InventoryResult.Success, "Expanded cell rejected encoded split.");
                var before = InventoryStateCodec.Encode(inventory.Capture());
                Require(inventory.Move((owner + 1) % 4, stack.Id, 0, 0, false) == InventoryResult.NotOwned, "New cells bypass ownership.");
                Require(inventory.Move(owner, stack.Id, 16, 17, false) == InventoryResult.InvalidPlacement &&
                    inventory.Split(owner, stack.Id, 1, 15, 18) == InventoryResult.InvalidPlacement, "Out-of-bounds action was accepted.");
                Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Rejected action changed inventory.");
            }
            var restored = new InventorySystem(InventoryStateCodec.Decode(InventoryStateCodec.Encode(inventory.Capture())));
            for (int owner = 0; owner < 4; owner++)
            {
                var stack = Stored(inventory, ItemKind.Fishbone, owner);
                Require(inventory.Move(owner, stack.Id, 14, 17, false) == InventoryResult.Success &&
                    restored.Move(owner, stack.Id, 14, 17, false) == InventoryResult.Success, "Cannot merge far-cell stacks after restore.");
                int coins = 1000, restoredCoins = 1000;
                Require(inventory.Purchase(owner, ItemKind.Backpack, 1, true, true, Ground, ref coins) == InventoryResult.Success &&
                    restored.Purchase(owner, ItemKind.Backpack, 1, true, true, Ground, ref restoredCoins) == InventoryResult.Success, "Cannot repack far cells into smaller backpack.");
                Require(inventory.Balance(owner, ItemKind.Fishbone) == 20, "Repacking lost material.");
            }
            Require(InventoryStateCodec.Encode(inventory.Capture()).SequenceEqual(InventoryStateCodec.Encode(restored.Capture())), "Restored expanded layout diverged.");
        });
    }
}

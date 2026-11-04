using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.InventoryTests;
internal static partial class Program
{
    private static IEnumerable<(ItemKind Kind, int Type)> CatalogItems()
    {
        foreach (ItemKind kind in Enum.GetValues(typeof(ItemKind)))
            for (int type = 0; type < 256; type++)
                if (InventoryCatalog.IsValid(kind, type)) yield return (kind, type);
    }

    private static IEnumerable<(string, Action)> SingleCellCases()
    {
        yield return ("inventory_every_catalog_item_occupies_one_cell_with_existing_stack_limits", () => {
            foreach (var (kind, type) in CatalogItems())
            {
                var definition = InventoryCatalog.Get(kind, type);
                Require(definition.Width == 1 && definition.Height == 1, $"{definition.Name} is not single-cell.");
                int limit = InventoryCatalog.IsMaterial(kind) ? 99 : kind == ItemKind.Ammunition ? AmmunitionCatalog.Get(type).StackLimit :
                    kind == ItemKind.Food ? FoodCatalog.Get(type).StackLimit : 1;
                Require(definition.StackLimit == limit, $"{definition.Name} changed stack limits.");
            }
        });
        yield return ("inventory_every_equipment_fits_last_isolated_cell_and_pickup_preserves_identity", () => {
            foreach (var (kind, type) in CatalogItems().Where(v => !InventoryCatalog.IsStackable(v.Kind) && !(v.Kind == ItemKind.Rod && v.Type == 0)))
            {
                var inventory = new InventorySystem(1); int coins = 100000;
                inventory.CreditMaterial(0, ItemKind.Wood, (StarterCells - 1) * 99, Ground);
                Require(inventory.Purchase(0, kind, type, false, false, Ground, ref coins) == InventoryResult.Success, $"{kind}/{type} rejected a single free cell.");
                var item = Stored(inventory, kind);
                Require(item.X == 7 && item.Y == 7, $"{kind}/{type} did not use the last cell.");
                var before = InventoryStateCodec.Encode(inventory.Capture()); int balance = coins;
                Require(inventory.Purchase(0, kind, type, false, false, Ground, ref coins) == InventoryResult.NoSpace, "Full bag accepted another item.");
                Require(coins == balance && before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Rejected purchase changed state.");
                Require(inventory.Drop(0, item.Id, 1, Ground) == InventoryResult.Success, "Cannot drop equipment.");
                Require(inventory.Pickup(0, item.Id, Ground, out int taken) == InventoryResult.Success && taken == 1, "Cannot recover equipment into one cell.");
                var restored = InventoryStateCodec.Decode(InventoryStateCodec.Encode(inventory.Capture()));
                Require(restored.Items.Single(i => i.Id == item.Id).X == 7 && restored.Items.Single(i => i.Id == item.Id).Y == 7, "Snapshot lost equipment identity or edge placement.");
            }
        });
    }
}

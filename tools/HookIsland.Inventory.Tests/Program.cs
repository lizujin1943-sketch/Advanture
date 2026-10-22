using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.InventoryTests;

internal static partial class Program
{
    private static readonly Point Ground = new Point(0, 0);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static InventoryItem Stored(InventorySystem inventory, ItemKind kind, int owner = 0) =>
        inventory.Capture().Items.First(i => i.Owner == owner && i.Kind == kind && i.Location == ItemLocation.Backpack);
    private static int Main(string[] args)
    {
        var cases = DomainCases().Concat(CommandCases()).Concat(GameCases()).Concat(InteractionCases());
        if (!args.Contains("--simulation-only")) cases = cases.Concat(NetworkCases());
        string? filter = args.FirstOrDefault(a => a.StartsWith("--filter=", StringComparison.Ordinal));
        if (filter != null) cases = cases.Where(c => c.Item1.Contains(filter.Substring(9), StringComparison.Ordinal));
        int passed = 0, total = 0;
        foreach (var (name, run) in cases)
        {
            total++;
            try { run(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); }
        }
        Console.WriteLine($"{passed}/{total} passed");
        return passed == total && total > 0 ? 0 : 1;
    }

    private static IEnumerable<(string, Action)> DomainCases()
    {
        yield return ("inventory_exhausted_ids_fail_atomically_without_stopping_existing_items", () => {
            var original = new InventorySystem(2); original.CreditMaterial(0, ItemKind.Wood, 20, Ground);
            var s = original.Capture(); var inventory = new InventorySystem(new InventoryState(2, s.Items, int.MaxValue, s.SpentCoins));
            var wood = Stored(inventory, ItemKind.Wood); var before = InventoryStateCodec.Encode(inventory.Capture());
            Require(inventory.Split(0, wood.Id, 1, 2, 2) == InventoryResult.LimitReached, "Exhausted split did not fail cleanly.");
            Require(inventory.Drop(0, wood.Id, 1, Ground) == InventoryResult.LimitReached, "Exhausted drop did not fail cleanly.");
            Require(inventory.CreditMaterial(1, ItemKind.Fishbone, 1, Ground) == -1, "Unallocatable harvest must remain unclaimed.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Failed allocation partially changed inventory.");
            Require(inventory.Move(0, wood.Id, 3, 3, false) == InventoryResult.Success, "ID limit disabled actions that need no new ID.");
            original.Drop(0, wood.Id, 10, Ground); s = original.Capture();
            inventory = new InventorySystem(new InventoryState(2, s.Items, int.MaxValue, s.SpentCoins));
            var ground = s.Items.Single(i => i.Location == ItemLocation.Ground); before = InventoryStateCodec.Encode(inventory.Capture());
            Require(inventory.Pickup(1, ground.Id, Ground, out int taken) == InventoryResult.LimitReached && taken == 0, "Pickup leaked an allocation exception or incorrect count.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Failed pickup changed ground material.");
            Require(inventory.Pickup(0, ground.Id, Ground, out taken) == InventoryResult.Success && taken == 10, "ID exhaustion must still permit filling existing stacks.");
        });
        yield return ("inventory_material_adapter_credits_once_and_spends_transferred_stacks", () => {
            var inventory = new InventorySystem(2); var a = new Fisher { Slot = 0 }; var b = new Fisher { Slot = 1 };
            PersonalMaterialInventory.Credit(ref a, TileKind.Wood, 45, inventory);
            Require(a.Wood == 45 && a.WoodEarned == 45, "Harvest did not populate canonical grid and statistics.");
            var item = Stored(inventory, ItemKind.Wood); inventory.Drop(0, item.Id, 5, Ground);
            var drop = inventory.Capture().Items.Single(i => i.Location == ItemLocation.Ground); inventory.Pickup(1, drop.Id, Ground, out _);
            PersonalMaterialInventory.Refresh(ref a, inventory); PersonalMaterialInventory.Refresh(ref b, inventory);
            Require(a.Wood == 40 && b.Wood == 5 && b.WoodEarned == 0, "Transfer was counted as a new harvest.");
            Require(PersonalMaterialInventory.TrySpend(ref b, TileKind.Wood, 4, inventory) && b.Wood == 1, "Construction did not consume transferred material from grid.");
            Require(inventory.Balance(1, ItemKind.Wood) == 1, "Material counter diverged from grid.");
        });
        yield return ("inventory_stacks_fill_existing_before_occupying_another_cell", () => {
            var inventory = new InventorySystem(2);
            Require(inventory.Capture().Size(0).Width == 4 && inventory.Capture().Size(0).Height == 4, "Starter backpack must be equipped and free.");
            inventory.CreditMaterial(0, ItemKind.Wood, 18, Ground);
            inventory.CreditMaterial(0, ItemKind.Wood, 25, Ground);
            var stacks = inventory.Capture().Items.Where(i => i.Kind == ItemKind.Wood).OrderBy(i => i.Id).ToArray();
            Require(stacks.Select(i => i.Count).SequenceEqual(new[] { 20, 20, 3 }), "Material did not fill existing stack first.");
            Require(inventory.Balance(0, ItemKind.Wood) == 43 && inventory.Balance(1, ItemKind.Wood) == 0, "Another player gained materials.");
            Require(inventory.TrySpendMaterial(0, ItemKind.Wood, 41), "Cannot consume across stacks.");
            Require(inventory.Balance(0, ItemKind.Wood) == 2, "Spend did not preserve remainder.");
            Require(!inventory.TrySpendMaterial(0, ItemKind.Wood, 3) && inventory.Balance(0, ItemKind.Wood) == 2, "Failed spend partially consumed inventory.");
        });
        yield return ("inventory_overflow_partial_pickup_and_distance_preserve_materials", () => {
            var inventory = new InventorySystem(2);
            Require(inventory.CreditMaterial(0, ItemKind.Fishbone, 327, Ground) == 320, "4x4 bag must hold exactly 320 stackable bones.");
            var drop = inventory.Capture().Items.Single(i => i.Location == ItemLocation.Ground);
            Require(drop.Count == 7, "Overflow was lost.");
            inventory.CreditMaterial(1, ItemKind.Fishbone, 317, Ground);
            Require(inventory.Pickup(1, drop.Id, new Point(5000, 0), out _) == InventoryResult.TooFar, "Remote pickup succeeded.");
            Require(inventory.Pickup(1, drop.Id, Ground, out int picked) == InventoryResult.Success && picked == 3, "Partial pickup ignored capacity.");
            Require(inventory.Capture().Items.Single(i => i.Id == drop.Id).Count == 4, "Remaining ground material disappeared.");
            Require(inventory.Pickup(0, drop.Id, Ground, out _) == InventoryResult.NoSpace, "Full bag accepted another stack.");
            Require(inventory.Capture().Items.Where(i => i.Kind == ItemKind.Fishbone).Sum(i => i.Count) == 644, "Material conservation failed.");
        });
        yield return ("inventory_move_split_merge_and_drop_are_owned_transactions", () => {
            var inventory = new InventorySystem(2); inventory.CreditMaterial(0, ItemKind.Wood, 20, Ground);
            var wood = Stored(inventory, ItemKind.Wood);
            Require(inventory.Move(1, wood.Id, 2, 2, false) == InventoryResult.NotOwned, "Can move another player's stack.");
            Require(inventory.Split(0, wood.Id, 7, 3, 3) == InventoryResult.Success, "Split failed.");
            var split = inventory.Capture().Items.Single(i => i.Kind == ItemKind.Wood && i.Count == 7);
            Require(inventory.Move(0, split.Id, wood.X, wood.Y, false) == InventoryResult.Success && Stored(inventory, ItemKind.Wood).Count == 20, "Move onto same material did not merge.");
            Require(inventory.Drop(0, wood.Id, 6, Ground) == InventoryResult.Success && inventory.Balance(0, ItemKind.Wood) == 14, "Drop did not take selected amount.");
            var drop = inventory.Capture().Items.Single(i => i.Location == ItemLocation.Ground);
            Require(inventory.Pickup(1, drop.Id, Ground, out int picked) == InventoryResult.Success && picked == 6, "Teammate cannot collect dropped material.");
            Require(inventory.Balance(1, ItemKind.Wood) == 6, "Pickup was credited to the wrong player.");
            var before = InventoryStateCodec.Encode(inventory.Capture());
            Require(inventory.Split(0, wood.Id, 14, 1, 1) == InventoryResult.InvalidQuantity, "Whole stack split should be move, not duplicate.");
            Require(inventory.Move(0, wood.Id, int.MaxValue, 0, false) == InventoryResult.InvalidPlacement, "Overflow grid coordinate accepted.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Failed operation mutated inventory.");
        });
        yield return ("inventory_full_bag_can_buy_equip_larger_bag_and_keep_old_bag", () => {
            var inventory = new InventorySystem(1); inventory.CreditMaterial(0, ItemKind.Wood, 320, Ground);
            int wallet = 200;
            Require(inventory.Purchase(0, ItemKind.Backpack, 2, true, false, Ground, ref wallet) == InventoryResult.Success, "Full bag cannot directly equip purchased larger bag.");
            var s = inventory.Capture();
            Require(wallet == 0 && s.Size(0).Height == 5 && inventory.Balance(0, ItemKind.Wood) == 320, "Purchase lost contents or charged wrong price.");
            Require(s.Items.Count(i => i.Kind == ItemKind.Backpack) == 2 && Stored(inventory, ItemKind.Backpack).Type == 1, "Old bag must be preserved as empty 2x2 item.");
            Require(s.SpentCoins[0] == 200, "Personal purchase ledger missing.");
            var before = InventoryStateCodec.Encode(s); int old = Stored(inventory, ItemKind.Backpack).Id;
            Require(inventory.Equip(0, old, false, Ground) == InventoryResult.NoSpace, "Shrinking full bag silently discarded old equipped bag or contents.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Failed shrink partially applied.");
            Require(inventory.Equip(0, old, true, Ground) == InventoryResult.Success && inventory.Capture().Size(0).Height == 4, "Explicitly dropping outgoing bag should permit full contents transfer.");
            Require(inventory.Capture().Items.Single(i => i.Location == ItemLocation.Ground).Type == 2, "Wrong backpack was put on ground.");
        });
        yield return ("inventory_rods_share_material_space_rotate_and_survive_bag_swaps", () => {
            var inventory = new InventorySystem(1); inventory.CreditMaterial(0, ItemKind.Wood, 260, Ground);
            int wallet = 1000;
            var before = InventoryStateCodec.Encode(inventory.Capture()); int money = wallet;
            // Rod type 1 is 1x4: thirteen 1x1 stacks leave only three cells.
            Require(inventory.Purchase(0, ItemKind.Rod, 1, false, false, Ground, ref wallet) == InventoryResult.NoSpace, "Rod bypassed material occupancy.");
            Require(wallet == money && before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Failed purchase consumed money or item IDs.");
            Require(inventory.TrySpendMaterial(0, ItemKind.Wood, 260), "Material spend failed.");
            Require(inventory.Purchase(0, ItemKind.Rod, 1, false, false, Ground, ref wallet) == InventoryResult.Success, "Rod purchase failed in empty bag.");
            var rod = Stored(inventory, ItemKind.Rod);
            Require(inventory.Move(0, rod.Id, 0, 3, true) == InventoryResult.Success, "1x4 rod cannot rotate to 4x1.");
            Require(inventory.Capture().Items.Single(i => i.Id == rod.Id).Rotated, "Rotation was not stored.");
            Require(inventory.Move(0, rod.Id, 1, 3, true) == InventoryResult.InvalidPlacement, "Rotated rod went past right border.");
            Require(inventory.Equip(0, rod.Id, false, Ground) == InventoryResult.Success, "Equipping should reuse incoming rod's freed cells for old rod.");
            Require(inventory.Capture().Equipped(0, EquipmentSlot.RightHand)!.Value.Id == rod.Id, "Equipped rod identity lost.");
        });
        yield return ("inventory_unequip_empty_bag_zero_capacity_then_pickup_equip", () => {
            var inventory = new InventorySystem(1); inventory.CreditMaterial(0, ItemKind.Wood, 1, Ground);
            Require(inventory.Unequip(0, EquipmentSlot.Backpack, Ground) == InventoryResult.InventoryNotEmpty, "Nonempty bag may not be removed.");
            inventory.TrySpendMaterial(0, ItemKind.Wood, 1);
            Require(inventory.Unequip(0, EquipmentSlot.Backpack, Ground) == InventoryResult.Success && inventory.Capture().Size(0).Width == 0, "Unbagged player still has grid capacity.");
            var bag = inventory.Capture().Items.Single(i => i.Location == ItemLocation.Ground);
            Require(inventory.Pickup(0, bag.Id, Ground, out _) == InventoryResult.NoSpace, "Empty backpack added capacity while on ground.");
            Require(inventory.PickupAndEquip(0, bag.Id, Ground, false) == InventoryResult.Success && inventory.Capture().Size(0).Width == 4, "Cannot recover backpack without an existing bag.");
            Require(inventory.Capture().Equipped(0, EquipmentSlot.RightHand).HasValue, "Removing bag removed unrelated equipped rod.");
        });
        yield return ("inventory_shrink_checks_rectangle_shapes_not_only_area", () => {
            var inventory = new InventorySystem(1); int wallet = 2000;
            Require(inventory.Purchase(0, ItemKind.Backpack, 2, true, false, Ground, ref wallet) == InventoryResult.Success, "Cannot upgrade fixture bag.");
            Require(inventory.Purchase(0, ItemKind.Rod, 3, false, false, Ground, ref wallet) == InventoryResult.Success, "First 2x3 rod not placed.");
            Require(inventory.Purchase(0, ItemKind.Rod, 3, false, false, Ground, ref wallet) == InventoryResult.Success, "Second 2x3 rod not placed.");
            var before = InventoryStateCodec.Encode(inventory.Capture()); var oldBag = Stored(inventory, ItemKind.Backpack);
            // 两件 2x3 加一件 2x2 虽然面积等于 16，仍无法排进 4x4。
            Require(inventory.Equip(0, oldBag.Id, false, Ground) == InventoryResult.NoSpace, "Area-only check accepted an impossible rectangle layout.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Failed shape packing moved existing items.");
        });
        yield return ("inventory_each_player_pays_their_own_wallet", () => {
            var inventory = new InventorySystem(2); int a = 1000, b = 0;
            Require(inventory.Purchase(1, ItemKind.Backpack, 2, true, false, Ground, ref b) == InventoryResult.InsufficientCoins, "Poor player could buy from teammate's funds.");
            Require(a == 1000 && b == 0 && inventory.Capture().Size(1).Height == 4, "Rejected personal purchase changed another player.");
            Require(inventory.Purchase(0, ItemKind.Backpack, 2, true, false, Ground, ref a) == InventoryResult.Success && a == 800 && b == 0, "Personal purchase charged the wrong balance.");
            Require(inventory.Capture().SpentCoins.SequenceEqual(new[] { 200, 0 }), "Spending ledger was shared.");
        });
        yield return ("inventory_corrupt_snapshots_reject_duplicates_overlap_and_missing_bag", () => {
            var inventory = new InventorySystem(1); inventory.CreditMaterial(0, ItemKind.Wood, 21, Ground); var s = inventory.Capture();
            bool Rejected(InventoryState bad) { try { InventoryStateCodec.Encode(bad); return false; } catch (System.IO.InvalidDataException) { return true; } }
            Require(Rejected(new InventoryState(1, s.Items.Concat(s.Items.Take(1)), s.NextId, s.SpentCoins)), "Duplicate item identity accepted.");
            var list = s.Items.ToArray(); var a = Array.FindIndex(list, i => i.Kind == ItemKind.Wood); var b = Array.FindLastIndex(list, i => i.Kind == ItemKind.Wood);
            list[b].X = list[a].X; list[b].Y = list[a].Y;
            Require(Rejected(new InventoryState(1, list, s.NextId, s.SpentCoins)), "Overlapping stacks accepted.");
            Require(Rejected(new InventoryState(1, s.Items.Where(i => i.Kind != ItemKind.Backpack), s.NextId, s.SpentCoins)), "Unbagged player retained hidden grid items.");
            Require(Rejected(new InventoryState(1, s.Items, s.NextId, new[] { -1 })), "Negative spend ledger accepted.");
        });
        yield return ("inventory_snapshot_detaches_and_roundtrips_ground_and_layout", () => {
            var inventory = new InventorySystem(2); inventory.CreditMaterial(0, ItemKind.Wood, 19, Ground);
            var checkpoint = inventory.Capture(); var bytes = InventoryStateCodec.Encode(checkpoint);
            inventory.CreditMaterial(0, ItemKind.Wood, 400, Ground);
            Require(bytes.SequenceEqual(InventoryStateCodec.Encode(checkpoint)), "Capture aliases mutable live arrays.");
            var restored = new InventorySystem(InventoryStateCodec.Decode(InventoryStateCodec.Encode(inventory.Capture())));
            var a = inventory.Capture().Items.Single(i => i.Location == ItemLocation.Ground);
            inventory.Pickup(1, a.Id, Ground, out _); restored.Pickup(1, a.Id, Ground, out _);
            Require(InventoryStateCodec.Encode(inventory.Capture()).SequenceEqual(InventoryStateCodec.Encode(restored.Capture())), "Restoring and replaying pickup diverged.");
        });
    }
}

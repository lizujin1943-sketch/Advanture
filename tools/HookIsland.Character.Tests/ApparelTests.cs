using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.CharacterTests;

internal static partial class Program
{
    private static IEnumerable<(string, Action)> ApparelCases()
    {
        yield return ("apparel_shoes_and_clothes_apply_only_in_their_owners_equipment_slots", () => {
            var inventory = new InventorySystem(2); int coins = 10000;
            var apparel = ItemKind.Apparel;
            Require(inventory.Purchase(0, apparel, 2, false, false, new Point(0, 0), ref coins) == InventoryResult.Success, "Shoes are not purchasable.");
            Require(inventory.Purchase(1, apparel, 1, true, false, new Point(0, 0), ref coins) == InventoryResult.Success, "Clothes are not purchasable.");
            var before = CharacterEquipment.Calculate(new CharacterConfig(), 100, inventory.Capture(), 0);
            Require(before.MoveSpeed == 3000 && before.HealthMax == 100, "Stored or teammate clothes granted attributes.");
            var shoes = inventory.Capture().Items.Single(i => i.Kind == apparel && i.Owner == 0);
            Require(CharacterEquipment.CanEquipTo(shoes, EquipmentSlot.Shoes) && !CharacterEquipment.CanEquipTo(shoes, EquipmentSlot.Hat), "Shoes accepted the wrong slot.");
            Require(inventory.Equip(0, shoes.Id, false, new Point(0, 0)) == InventoryResult.Success, "Cannot equip shoes.");
            Require(CharacterEquipment.Calculate(new CharacterConfig(), 100, inventory.Capture(), 0).MoveSpeed == 3300, "Equipped shoes did not increase speed.");
            Require(CharacterEquipment.Calculate(new CharacterConfig(), 100, inventory.Capture(), 1).HealthMax == 125, "Clothes did not increase max health.");
            Require(inventory.Unequip(0, EquipmentSlot.Shoes, new Point(0, 0)) == InventoryResult.Success, "Cannot remove shoes.");
            Require(CharacterEquipment.Calculate(new CharacterConfig(), 100, inventory.Capture(), 0).MoveSpeed == 3000, "Removing shoes retained their bonus.");
        });
        yield return ("apparel_full_bag_rejects_removal_without_losing_attributes_or_items", () => {
            var inventory = new InventorySystem(1); int coins = 1000;
            Require(inventory.Purchase(0, ItemKind.Apparel, 1, true, false, new Point(0, 0), ref coins) == InventoryResult.Success, "Clothes fixture failed.");
            inventory.CreditMaterial(0, ItemKind.Wood, 320, new Point(0, 0));
            var before = InventoryStateCodec.Encode(inventory.Capture());
            Require(inventory.Unequip(0, EquipmentSlot.Clothes, new Point(0, 0)) == InventoryResult.NoSpace, "Full bag accepted clothes.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Failed unequip changed inventory or wallet.");
            Require(CharacterEquipment.Calculate(new CharacterConfig(), 100, inventory.Capture(), 0).HealthMax == 125, "Failed removal lost health bonus.");
        });
        yield return ("apparel_preview_changes_max_health_without_healing_or_writing_inventory", () => {
            var inventory = new InventorySystem(1); int coins = 1000;
            Require(inventory.Purchase(0, ItemKind.Apparel, 1, false, false, new Point(0, 0), ref coins) == InventoryResult.Success, "Clothes fixture failed.");
            var state = inventory.Capture(); var clothes = state.Items.Single(i => i.Kind == ItemKind.Apparel);
            byte[] before = InventoryStateCodec.Encode(state);
            Require(CharacterEquipment.Preview(new CharacterConfig(), 100, state, 0, new InventoryCommand(InventoryAction.Equip, clothes.Id), false, false, out var stats) == InventoryResult.Success, "Preview failed.");
            Require(stats.HealthMax == 125 && before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Preview wrote authority.");
            var vitals = new CharacterVitals { Health = 63, Hunger = 80 }; int stamina = 60 * 30000;
            CharacterSystem.Clamp(ref vitals, ref stamina, stats);
            Require(vitals.Health == 63 && stamina == 60 * 30000, "Clothes granted free healing.");
        });
        yield return ("apparel_game_shoes_change_land_distance_and_restore_exactly", () => {
            var a = Game(); int shoes = AddApparel(a, 2, true); var before = a.Capture();
            Step(a, new HookInput(1000, 0)); var b = Game(); b.Restore(new HookGame().Decode(new HookGame().Encode(a.Capture())));
            for (int n = 1; n < 30; n++) { Step(a, new HookInput(1000, 0)); Step(b, new HookInput(1000, 0)); }
            Require(a.Capture().Players[0].X - before.Players[0].X == 3300, "Shoes did not change real movement.");
            Require(new HookGame().Encode(a.Capture()).SequenceEqual(new HookGame().Encode(b.Capture())), "Restored shoe movement diverged.");
            Step(a, EquipCommand(InventoryAction.Unequip, (int)EquipmentSlot.Shoes, 1));
            var s = a.Capture(); Require(FishingRules.Attributes(s, s.Players[0]).MoveSpeed == 3000, "Confirmed shoe removal retained speed.");
            Step(a, EquipCommand(InventoryAction.Equip, shoes, 2));
            Require(FishingRules.Attributes(a.Capture(), a.Capture().Players[0]).MoveSpeed == 3300, "Confirmed re-equip did not restore speed.");
        });
        yield return ("apparel_game_clothing_clamps_health_and_repeated_requests_never_heal", () => {
            var a = Game(); int clothes = AddApparel(a, 1, true); var s = a.Capture();
            Require(s.Players[0].Vitals.Health == 100 && FishingRules.Attributes(s, s.Players[0]).HealthMax == 125, "Equipping clothes healed.");
            var players = s.Players.ToArray(); players[0].Vitals.Health = 117; a.Restore(WithPlayers(s, players));
            Step(a, EquipCommand(InventoryAction.Unequip, (int)EquipmentSlot.Clothes, 1));
            Require(a.Capture().Players[0].Vitals.Health == 100, "Clothing removal did not cap current health.");
            var b = Game(); b.Restore(a.Capture()); var equip = EquipCommand(InventoryAction.Equip, clothes, 2);
            for (int n = 0; n < 12; n++) { Step(a, equip); Step(b, equip); }
            s = a.Capture(); Require(s.Players[0].Vitals.Health == 100 && FishingRules.Attributes(s, s.Players[0]).HealthMax == 125, "Repeated clothing command healed.");
            Require(s.Tackle!.Inventory.Items.Count == 3 && new HookGame().Encode(s).SequenceEqual(new HookGame().Encode(b.Capture())), "Clothing receipt replay duplicated items or diverged.");
        });
        yield return ("apparel_headgear_defense_is_derived_and_water_damage_still_applies", () => {
            var game = Game(); AddApparel(game, 0, true); var s = game.Capture();
            var stats = FishingRules.Attributes(s, s.Players[0]);
            Require(stats.Armor == 20 && CharacterDamage.Resolve(60, stats.Armor, DamageKind.Combat) == 50, "Hat defense is not used in damage rules.");
            var p = s.Players.ToArray(); p[0].X = -4000; p[0].Swimming.InWater = true; game.Restore(WithPlayers(s, p));
            for (int n = 0; n < 30; n++) Step(game);
            s = game.Capture(); Require(s.Players[0].Vitals.Health == 95 && FishingRules.Attributes(s, s.Players[0]).Armor == 20, "Defense blocked drowning or was consumed.");
            Step(game, EquipCommand(InventoryAction.Unequip, (int)EquipmentSlot.Hat, 1));
            Require(FishingRules.Attributes(game.Capture(), game.Capture().Players[0]).Armor == 0, "Headgear removal did not remove defense.");
        });
        yield return ("apparel_shop_keeps_starter_supply_and_rotates_all_wearables", () => {
            var original = IslandMap.CreatePlatform(); var map = new IslandMap(original.Shore, original.Pier, original.Spawns, original.FishSpots, original.Platform, new FishingConfig(restockSeconds: 1));
            var game = new HookGame().Create();
            game.Initialize(new SimulationInitialization(new SessionDescriptor(SessionId, 1, 30, new[] { new Participant(0, Guid.NewGuid()) }), new ByteString(map.Encode())));
            var seen = new HashSet<int>();
            for (int batch = 0; batch < 12; batch++)
            {
                var stock = game.Capture().Tackle!.Stock;
                Require(stock.Count(i => i.Kind == ItemKind.Rod) >= 4 && stock.Any(i => i.Kind == ItemKind.Backpack && i.Type == 2) && stock.Any(i => i.Kind == ItemKind.SwimRing), "Apparel displaced starter rods, backpack or ring.");
                foreach (var item in stock.Where(i => i.Kind == ItemKind.Apparel)) seen.Add(item.Type);
                new HookGame().Decode(new HookGame().Encode(game.Capture()));
                for (int tick = 0; tick < 30; tick++) Step(game);
            }
            Require(seen.SetEquals(Enumerable.Range(0, ApparelCatalog.All.Count)), "A wearable cannot be acquired from the rotating shop.");
        });
        yield return ("apparel_v10_rejects_old_shield_state_and_map_versions", () => {
            byte[] state = new HookGame().Encode(Game().Capture()); BitConverter.GetBytes(0x39534948).CopyTo(state, 0);
            Throws<InvalidDataException>(() => new HookGame().Decode(state));
            byte[] map = CharacterMap().Encode(); BitConverter.GetBytes(0x354D4948).CopyTo(map, 0);
            Throws<InvalidDataException>(() => IslandMap.Decode(map));
        });
    }

    // Test fixture uses the real purchase transaction and preserves the personal purchase ledger.
    private static int AddApparel(IDeterministicSimulation<HookState> game, int type, bool equip)
    {
        var s = game.Capture(); var p = s.Players.ToArray(); var inventory = new InventorySystem(s.Tackle!.Inventory);
        int price = ApparelCatalog.Get(type).Item.Price; p[0].Coins += price; p[0].CoinsEarned += price;
        Require(inventory.Purchase(0, ItemKind.Apparel, type, equip, false, new Point(0, 0), ref p[0].Coins) == InventoryResult.Success, "Apparel purchase fixture failed.");
        var captured = inventory.Capture(); game.Restore(new HookState(s.Steps, p, s.Fish, p.Sum(v => v.Coins), s.Tiles, s.Driftwood,
            new TackleState(s.Tackle.Config, captured, s.Tackle.Stock, s.Tackle.Batch, s.Tackle.NextRestock)));
        return captured.Items.Last(i => i.Kind == ItemKind.Apparel).Id;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.InventoryTests;

internal static partial class Program
{
    private static IEnumerable<(string, Action)> MaterialStackCases()
    {
        yield return ("material_stack99_fills_splits_merges_and_restores_without_loss", () => {
            foreach (var kind in new[] { ItemKind.Wood, ItemKind.Fishbone })
            {
                var inventory = new InventorySystem(2);
                inventory.CreditMaterial(0, kind, 98, Ground);
                inventory.CreditMaterial(0, kind, 1, Ground);
                var first = Stored(inventory, kind);
                Require(first.Count == 99 && inventory.Capture().Items.Count(i => i.Kind == kind) == 1,
                    "98 + 1 must occupy exactly one 99-item stack.");
                inventory.CreditMaterial(0, kind, 100, Ground);
                var stacks = inventory.Capture().Items.Where(i => i.Kind == kind).ToArray();
                Require(stacks.Select(i => i.Count).SequenceEqual(new[] { 99, 99, 1 }), "199 materials must use three stacks.");
                Require(inventory.Split(0, first.Id, 30, 3, 3) == InventoryResult.Success, "Cannot split a large stack.");
                Require(inventory.Move(0, stacks[1].Id, first.X, first.Y, false) == InventoryResult.Success,
                    "Partial merge failed.");
                Require(inventory.Find(first.Id)!.Value.Count == 99 && inventory.Find(stacks[1].Id)!.Value.Count == 69,
                    "Merge must cap the target at 99 and retain the remainder at its source.");
                var before = InventoryStateCodec.Encode(inventory.Capture());
                Require(inventory.Move(0, stacks[2].Id, first.X, first.Y, false) == InventoryResult.NoSpace &&
                    before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())), "Merging into 99 changed inventory.");
                inventory = new InventorySystem(InventoryStateCodec.Decode(before));
                Require(inventory.Drop(0, first.Id, 99, Ground) == InventoryResult.Success, "Cannot drop a complete 99 stack.");
                var dropped = inventory.Capture().Items.Single(i => i.Location == ItemLocation.Ground);
                Require(inventory.Pickup(1, dropped.Id, Ground, out int taken) == InventoryResult.Success && taken == 99,
                    "Teammate did not receive the complete stack.");
                Require(inventory.Balance(0, kind) == 100 && inventory.Balance(1, kind) == 99, "Transfer lost or duplicated materials.");
                var snapshot = inventory.Capture(); var invalid = snapshot.Items.ToArray();
                int index = Array.FindIndex(invalid, i => i.Kind == kind && i.Owner == 1); invalid[index].Count = 100;
                bool rejected = false;
                try { InventoryStateCodec.Encode(new InventoryState(2, invalid, snapshot.NextId, snapshot.SpentCoins)); }
                catch (InvalidDataException) { rejected = true; }
                Require(rejected, "A 100-item backpack stack was accepted.");
            }
        });
        yield return ("material_stack99_large_commands_roundtrip_and_overlimit_rejected", () => {
            foreach (var command in new[] {
                new InventoryCommand(InventoryAction.Drop, 7, amount: 99, requestId: 1),
                new InventoryCommand(InventoryAction.Split, 7, amount: 98, x: 3, y: 3, requestId: 2) })
                Require(InventoryCommand.Decode(command.Encode()).Amount == command.Amount, "Large material quantity was truncated.");
            foreach (var command in new[] {
                new InventoryCommand(InventoryAction.Drop, 7, amount: 100, requestId: 1),
                new InventoryCommand(InventoryAction.Split, 7, amount: 99, requestId: 2) })
            {
                bool rejected = false; try { command.Encode(); } catch (InvalidDataException) { rejected = true; }
                Require(rejected, "Out-of-range material command was accepted.");
            }
        });
        yield return ("material_stack99_transfer_receipt_survives_game_snapshot_and_retries", () => {
            var game = Game(4, 0, 199); var wood = game.Capture().Tackle!.Inventory.Items.First(i => i.Kind == ItemKind.Wood);
            var drop = Command(InventoryAction.Drop, wood.Id, 1, amount: 99);
            GameStep(game, drop); var state = game.Capture();
            var ground = state.Tackle!.Inventory.Items.Single(i => i.Location == ItemLocation.Ground);
            var pickup = Command(InventoryAction.Pickup, ground.Id, 1);
            GameStep(game, drop, pickup); state = game.Capture();
            var codec = new HookGame(); game.Restore(codec.Decode(codec.Encode(state)));
            for (int i = 0; i < 8; i++) GameStep(game, drop, pickup);
            state = game.Capture();
            Require(state.Players[0].Wood == 100 && state.Players[1].Wood == 99 && state.Players.Skip(2).All(p => p.Wood == 0),
                "Restored/retried transfer changed quantities or credited spectators.");
            Require(state.Players[0].WoodEarned == 199 && state.Players.Skip(1).All(p => p.WoodEarned == 0),
                "Transfer was counted as a fresh harvest.");
        });
    }
}

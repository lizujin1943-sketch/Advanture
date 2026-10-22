using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.InventoryTests;

internal static partial class Program
{
    private static readonly Guid SessionId = Guid.Parse("cc45aee4-98a3-467e-ae7a-7fe08da5dccd");
    private static IDeterministicSimulation<HookState> Game(int players = 2, int money = 1000, int wood = 0)
    {
        var game = new HookGame().Create();
        var session = new SessionDescriptor(SessionId, 1, HookGame.Rate, Enumerable.Range(0, players)
            .Select(i => new Participant((ushort)i, new Guid(i + 1, 0, 0, new byte[8]))).ToArray());
        game.Initialize(new SimulationInitialization(session, new ByteString(IslandMap.CreatePlatform().Encode())));
        var s = game.Capture(); var p = s.Players.ToArray(); var inventory = new InventorySystem(s.Tackle!.Inventory);
        for (int i = 0; i < players; i++) { p[i].X = i * 500; p[i].Z = 0; }
        p[0].Coins = p[0].CoinsEarned = money;
        if (wood > 0) PersonalMaterialInventory.Credit(ref p[0], TileKind.Wood, wood, inventory);
        game.Restore(WithInventory(s, inventory.Capture(), p, money)); return game;
    }
    private static HookState WithInventory(HookState s, InventoryState inventory, Fisher[]? players = null, int? money = null) =>
        new HookState(s.Steps, players ?? s.Players, s.Fish, money ?? s.Coins, s.Tiles, s.Driftwood,
            new TackleState(s.Tackle!.Config, inventory, s.Tackle.Stock, s.Tackle.Batch, s.Tackle.NextRestock));
    private static void GameStep(IDeterministicSimulation<HookState> game, params HookInput[] inputs)
    {
        var s = game.Capture(); game.Step(new FrameContext(s.Steps + 1, s.Steps + 1, HookGame.Rate), s.Players.Select((p, i) =>
            new SimulationInput(new Participant(p.Slot, new Guid(i + 1, 0, 0, new byte[8])),
                new ByteString((i < inputs.Length ? inputs[i] : default).Encode()))).ToArray());
    }
    private static HookInput Command(InventoryAction action, int target, uint id, int amount = 0, int x = 0, int y = 0, bool rotated = false, bool dropOld = false, uint batch = 0) =>
        new HookInput(0, 0, inventory: new InventoryCommand(action, target, amount, x, y, rotated, dropOld, batch, id));
    private static int BagOffer(HookState s) => Enumerable.Range(0, s.Tackle!.Stock.Count).First(i => s.Tackle.Stock[i].Kind == ItemKind.Backpack && s.Tackle.Stock[i].Type == 2);
    private static IEnumerable<(string, Action)> GameCases()
    {
        yield return ("game_wallet_limit_holds_catch_without_partial_material_reward", () => {
            foreach (int count in new[] { 1, 2 })
            {
                var game = Game(count, int.MaxValue); var pilot = new Advanture.HookIsland.InventorySmokePilot();
                var map = IslandMap.CreatePlatform(); int slot = count - 1; bool checkedLanding = false;
                for (int tick = 0; tick < 3000; tick++)
                {
                    var s = game.Capture(); var p = s.Players[slot];
                    // 普通鱼抵达平台即结算；钱包已满时会停在岸边，不再拉到角色脚下。
                    if (p.FishId >= 0 && map.CanStand(s, p.HookX, p.HookZ))
                    {
                        var before = InventoryStateCodec.Encode(s.Tackle!.Inventory); GameStep(game);
                        var after = game.Capture(); var actual = after.Players[slot];
                        Require(actual.FishId == p.FishId && actual.Catches == p.Catches && actual.CoinsEarned == p.CoinsEarned && actual.FishbonesEarned == p.FishbonesEarned, "Unpayable catch was partially consumed or credited.");
                        Require(before.SequenceEqual(InventoryStateCodec.Encode(after.Tackle!.Inventory)), "Unpayable catch leaked fishbone reward.");
                        new HookGame().Decode(new HookGame().Encode(after));
                        // 同一真实上岸状态再验证随机代数边界；循环不应导致部分结算或无法取消。
                        var players = s.Players.ToArray(); players[0].Coins = players[0].CoinsEarned = 0;
                        var fish = s.Fish.ToArray(); fish[p.FishId].Generation = uint.MaxValue;
                        game.Restore(new HookState(s.Steps, players, fish, 0, s.Tiles, s.Driftwood, s.Tackle));
                        GameStep(game); after = game.Capture();
                        Require(after.Fish[p.FishId].Generation == 0 && after.Players[slot].Catches == p.Catches + 1 && after.Players[slot].Coins > 0, "Fish generation boundary interrupted landing.");
                        new HookGame().Decode(new HookGame().Encode(after)); checkedLanding = true; break;
                    }
                    var inputs = new HookInput[count]; inputs[slot] = pilot.Sample(map, s, slot, s.Steps + 1); GameStep(game, inputs);
                }
                Require(checkedLanding, "Fixture did not reach a real fish landing.");
            }
        });
        yield return ("game_four_players_earn_buy_split_drop_pickup_and_build", () => {
            var game = Game(4, 0); var map = IslandMap.CreatePlatform(); var pilot = new Advanture.HookIsland.InventorySmokePilot();
            for (int tick = 0; tick < 10000 && !pilot.Complete(game.Capture()); tick++)
            { var s = game.Capture(); GameStep(game, s.Players.Select(p => pilot.Sample(map, s, p.Slot, s.Steps + 1)).ToArray()); }
            var final = game.Capture();
            Require(pilot.Complete(final), "Normal-input backpack flow incomplete: " + string.Join(";", final.Players.Select(p => $"P{p.Slot}: {p.Coins}/{p.CoinsEarned} G bones={p.Fishbones} request={p.LastInventoryRequest}/{p.LastInventoryResult} build={p.LastBuildResult}")));
            Require(final.Players[0].CoinsEarned - final.Players[0].Coins == 200 && final.Players.Skip(1).All(p => p.Coins == p.CoinsEarned), "Backpack purchase used another player's income.");
            new HookGame().Decode(new HookGame().Encode(final)); Console.WriteLine("BACKPACK_ACCEPTANCE_TICKS " + final.Steps);
        });
        yield return ("game_full_backpack_buy_equips_and_replay_preserves_personal_receipts", () => {
            var a = Game(2, 1000, 320); var s = a.Capture(); int offer = BagOffer(s);
            GameStep(a, Command(InventoryAction.BuyAndEquip, offer, 1, batch: s.Tackle!.Batch), Command(InventoryAction.BuyAndEquip, offer, 1, batch: s.Tackle.Batch));
            s = a.Capture();
            Require(s.Players[0].LastInventoryResult == InventoryResult.Success && s.Players[1].LastInventoryResult == InventoryResult.SoldOut, "Contested bag did not issue one winner and one rejection.");
            Require(s.Players[0].Coins == 800 && s.Players[1].Coins == 0 && s.Players[0].Wood == 320 && s.Tackle!.Inventory.Size(0).Height == 5, "Purchase lost contents or shared personal money.");
            var b = Game(); var codec = new HookGame(); b.Restore(codec.Decode(codec.Encode(s)));
            var duplicate = Command(InventoryAction.BuyAndEquip, offer, 1, batch: s.Tackle!.Batch);
            for (int i = 0; i < 50; i++) { GameStep(a, duplicate); GameStep(b, duplicate); }
            Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())) && a.Capture().Players[0].Coins == 800, "Saved receipt replay bought a second bag or diverged.");
        });
        yield return ("game_teammate_material_transfer_can_build_and_survives_restore", () => {
            var game = Game(2, 0, 45); var wood = game.Capture().Tackle!.Inventory.Items.First(i => i.Kind == ItemKind.Wood);
            GameStep(game, Command(InventoryAction.Drop, wood.Id, 1, amount: 7));
            var dropped = game.Capture().Tackle!.Inventory.Items.Single(i => i.Location == ItemLocation.Ground);
            GameStep(game, default, Command(InventoryAction.Pickup, dropped.Id, 1));
            var s = game.Capture(); Require(s.Players[0].Wood == 38 && s.Players[1].Wood == 7 && s.Players[1].WoodEarned == 0, "Transfer duplicated harvest.");
            var p = s.Players.ToArray(); p[1].X = 2000; p[1].Z = 0; game.Restore(WithInventory(s, s.Tackle!.Inventory, p));
            s = game.Capture();
            // 浮木会占据候选地块，选一个实际允许建造的位置，测试重点是转交后的库存扣料。
            var tile = Enumerable.Range(-2, 5).SelectMany(z => Enumerable.Range(-2, 5).Select(x => new Point(x, z)))
                .First(cell => PlatformRules.Check(IslandMap.CreatePlatform(), s, s.Players[1], TileKind.Wood, cell.X, cell.Z) == BuildResult.Success);
            GameStep(game, default, new HookInput(0, 0, build: TileKind.Wood, buildX: (short)tile.X, buildZ: (short)tile.Z, buildId: 1));
            s = game.Capture(); Require(s.Players[1].LastBuildResult == BuildResult.Success && s.Players[1].Wood == 3, "Builder did not consume received material.");
            var codec = new HookGame(); game.Restore(codec.Decode(codec.Encode(s)));
            Require(game.Capture().Players[0].WoodEarned == 45 && game.Capture().Players[1].Wood == 3, "Cross-player material conservation rejected valid save.");
        });
        yield return ("game_failed_request_is_final_and_cannot_replay_after_space_freed", () => {
            var game = Game(1, 1000, 320); var s = game.Capture();
            var failed = Command(InventoryAction.Buy, 0, 1, batch: s.Tackle!.Batch); GameStep(game, failed);
            Require(game.Capture().Players[0].LastInventoryResult == InventoryResult.NoSpace, "Full bag purchase did not fail.");
            s = game.Capture(); var inventory = new InventorySystem(s.Tackle!.Inventory); var p = s.Players.ToArray();
            inventory.Drop(0, inventory.Capture().Items.First(i => i.Kind == ItemKind.Wood).Id, 20, Ground);
            PersonalMaterialInventory.Refresh(ref p[0], inventory); game.Restore(WithInventory(s, inventory.Capture(), p));
            GameStep(game, failed); s = game.Capture();
            Require(!s.Tackle!.Stock[0].Sold && s.Players[0].Coins == 1000 && s.Players[0].LastInventoryResult == InventoryResult.NoSpace, "Failed intent replayed after its context changed.");
        });
        yield return ("game_ground_race_transfers_once_and_gear_trade_does_not_move_wallet", () => {
            var game = Game(3, 1000); var s = game.Capture();
            GameStep(game, Command(InventoryAction.Buy, 0, 1, batch: s.Tackle!.Batch));
            var bought = game.Capture().Tackle!.Inventory.Items.Single(i => i.Kind == ItemKind.Rod && i.Location == ItemLocation.Backpack);
            GameStep(game, Command(InventoryAction.Drop, bought.Id, 2, amount: 1));
            GameStep(game, default, Command(InventoryAction.Pickup, bought.Id, 1), Command(InventoryAction.Pickup, bought.Id, 1));
            s = game.Capture(); Require(s.Players[1].LastInventoryResult == InventoryResult.Success && s.Players[2].LastInventoryResult != InventoryResult.Success, "Two pickups obtained the same item.");
            Require(s.Players[1].Coins == 0 && s.Tackle!.Inventory.SpentCoins[1] == 0 && s.Tackle.Inventory.SpentCoins[0] > 0, "Equipment trading moved money ledger.");
            var codec = new HookGame(); game.Restore(codec.Decode(codec.Encode(s)));
            GameStep(game, Command(InventoryAction.Move, bought.Id, 3));
            Require(game.Capture().Players[0].LastInventoryResult == InventoryResult.NotOwned, "Former owner moved a transferred item.");
        });
        yield return ("game_input_rejects_two_transaction_families_and_inventory_suppresses_cast", () => {
            bool rejected = false;
            try { new HookInput(0, 0, build: TileKind.Wood, buildId: 1, inventory: new InventoryCommand(InventoryAction.Drop, 1, amount: 1, requestId: 1)).Encode(); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Inventory/build ambiguity was accepted.");
            var game = Game(1, 0, 1); int id = game.Capture().Tackle!.Inventory.Items.First(i => i.Kind == ItemKind.Wood).Id;
            GameStep(game, new HookInput(0, 0, -50000, 0, fire: true, inventory: new InventoryCommand(InventoryAction.Drop, id, amount: 1, requestId: 1)));
            Require(game.Capture().Players[0].Casts == 0 && game.Capture().Players[0].LastInventoryResult == InventoryResult.Success, "An inventory click also cast the hook.");
        });
        yield return ("game_checkpoint_rejects_offshore_ground_and_counter_wallet_tampering", () => {
            var game = Game(1, 500, 340); var s = game.Capture(); var codec = new HookGame();
            bool Rejected(HookState bad) { try { codec.Decode(codec.Encode(bad)); return false; } catch (InvalidDataException) { return true; } }
            var p = s.Players.ToArray(); p[0].Wood++;
            Require(Rejected(WithInventory(s, s.Tackle!.Inventory, p)), "Summary material counter can disagree with inventory.");
            p = s.Players.ToArray(); p[0].Coins++; Require(Rejected(WithInventory(s, s.Tackle.Inventory, p, s.Coins + 1)), "Wallet accepted unexplained income.");
            var items = s.Tackle.Inventory.Items.ToArray(); int drop = Array.FindIndex(items, i => i.Location == ItemLocation.Ground); items[drop].WorldX = 45000;
            Require(Rejected(WithInventory(s, new InventoryState(1, items, s.Tackle.Inventory.NextId, s.Tackle.Inventory.SpentCoins))), "Offshore material package accepted.");
        });
    }
}

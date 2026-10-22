using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.CharacterTests;

internal static partial class Program
{
    private static readonly Guid SessionId = Guid.Parse("cab61fa0-bcb0-47a4-9f4a-9f6dc86ca4c1");
    private static IslandMap CharacterMap(CharacterConfig? config = null)
    {
        var map = IslandMap.CreatePlatform();
        return new IslandMap(map.Shore, map.Pier, map.Spawns, map.FishSpots, map.Platform, new FishingConfig(character: config));
    }
    private static IDeterministicSimulation<HookState> Game(int count = 1, CharacterConfig? config = null)
    {
        var game = new HookGame().Create();
        game.Initialize(new SimulationInitialization(new SessionDescriptor(SessionId, 1, 30, Enumerable.Range(0, count)
            .Select(i => new Participant((ushort)i, new Guid(i + 1, 0, 0, new byte[8]))).ToArray()), new ByteString(CharacterMap(config).Encode())));
        return game;
    }
    private static void Step(IDeterministicSimulation<HookState> game, params HookInput[] inputs)
    {
        var s = game.Capture(); game.Step(new FrameContext(s.Steps + 1, s.Steps + 1, 30), s.Players.Select((p, i) =>
            new SimulationInput(new Participant(p.Slot, new Guid(i + 1, 0, 0, new byte[8])), new ByteString((i < inputs.Length ? inputs[i] : default).Encode()))).ToArray());
    }
    private static HookState WithPlayers(HookState s, Fisher[] players) => new HookState(s.Steps, players, s.Fish, s.Coins, s.Tiles, s.Driftwood, s.Tackle);
    private static HookInput EquipCommand(InventoryAction action, int target, uint id) =>
        new HookInput(0, 0, inventory: new InventoryCommand(action, target, requestId: id));
    private static IEnumerable<(string, Action)> GameCases()
    {
        yield return ("character_game_initializes_each_players_resources_and_saved_config", () => {
            var game = Game(4, new CharacterConfig(130, 3125, 20, 110)); var s = game.Capture();
            Require(s.Players.All(p => p.Vitals.Health == 130 && p.Vitals.Hunger == 110 && p.Stamina == 120 * 30000), "Character bases lost during room startup.");
            Require(s.Tackle!.Config.Character.Armor == 20 && s.Tackle.Config.Character.MoveSpeed == 3125, "Room did not preserve character settings.");
        });
        yield return ("character_game_movement_uses_configured_speed_and_saved_remainders", () => {
            var config = new CharacterConfig(moveSpeed: 3125); var a = Game(config: config);
            var s = a.Capture(); int start = s.Players[0].X;
            Step(a, new HookInput(1000, 0)); var b = Game(config: config); b.Restore(a.Capture());
            for (int i = 1; i < 30; i++) { Step(a, new HookInput(1000, 0)); Step(b, new HookInput(1000, 0)); }
            Require(a.Capture().Players[0].X - start == 3125, "World movement ignored exact configured m/s.");
            Require(new HookGame().Encode(a.Capture()).SequenceEqual(new HookGame().Encode(b.Capture())), "Fractional movement diverged after restore.");
        });
        yield return ("character_game_equip_preserves_partial_resources_regen_wait_and_receipts", () => {
            var a = Game(); var s = a.Capture(); var p = s.Players.ToArray();
            p[0].Vitals.Health = 63; p[0].Vitals.Hunger = 44; p[0].Stamina = 108 * 30000; p[0].RegenDelay = 40;
            a.Restore(WithPlayers(s, p)); Step(a, EquipCommand(InventoryAction.Unequip, (int)EquipmentSlot.RightHand, 1));
            s = a.Capture(); p = s.Players.ToArray();
            Require(p[0].LastInventoryResult == InventoryResult.Success && p[0].Stamina == 100 * 30000 && p[0].RegenDelay == 39, "Unequip refilled stamina or reset recovery clock.");
            var b = Game(); b.Restore(s); int rod = s.Tackle!.Inventory.Items.Single(i => i.Kind == ItemKind.Rod && i.Owner == 0).Id;
            var equip = EquipCommand(InventoryAction.Equip, rod, 2);
            for (int i = 0; i < 10; i++) { Step(a, equip); Step(b, equip); }
            s = a.Capture();
            Require(s.Players[0].Vitals.Health == 63 && s.Players[0].Vitals.Hunger == 44 && s.Players[0].Stamina == 100 * 30000 && s.Players[0].RegenDelay == 29, "Repeated equipment request healed or restarted rest timer.");
            Require(new HookGame().Encode(s).SequenceEqual(new HookGame().Encode(b.Capture())), "Restore/equipment replay diverged.");
        });
        yield return ("character_zero_health_blocks_autonomous_actions_and_recovery", () => {
            var game = Game(); var s = game.Capture(); var p = s.Players.ToArray(); p[0].Vitals.Health = 0; p[0].Stamina = 50 * 30000;
            game.Restore(WithPlayers(s, p));
            Step(game, new HookInput(1000, 0, -10000, -10000, true));
            var after = game.Capture().Players[0];
            Require(after.X == p[0].X && after.Z == p[0].Z && after.Casts == 0 && after.Stamina == p[0].Stamina, "Downed player moved, cast or regenerated.");
            Step(game, EquipCommand(InventoryAction.Unequip, (int)EquipmentSlot.RightHand, 1));
            Require(game.Capture().Players[0].LastInventoryResult != InventoryResult.Success && game.Capture().Players[0].EquippedRod == 1, "Downed character changed equipment.");
        });
        yield return ("character_game_rejects_overcap_and_incompatible_config_checkpoints", () => {
            var game = Game(); var s = game.Capture(); var p = s.Players.ToArray(); p[0].Vitals.Health = 101;
            Throws<InvalidDataException>(() => new HookGame().Decode(new HookGame().Encode(WithPlayers(s, p))));
            var other = Game(config: new CharacterConfig(health: 150)); Throws<InvalidDataException>(() => other.Restore(s));
        });
        yield return ("character_downed_caster_releases_teammate_and_downed_teammate_remains_pullable", () => {
            var game = Game(2); var s = game.Capture(); var target = s.Players[1];
            Step(game, new HookInput(0, 0, target.X, target.Z, true));
            for (int n = 0; n < 30 && game.Capture().Players[0].HookedPlayer < 0; n++) Step(game);
            s = game.Capture(); Require(s.Players[0].HookedPlayer == 1, "Fixture never hooked teammate.");
            var p = s.Players.ToArray(); p[0].Vitals.Health = 0;
            game.Restore(WithPlayers(s, p)); Step(game);
            s = game.Capture();
            Require(s.Players[0].Phase == HookPhase.Ready && s.Players[0].HookedPlayer == -1 && s.Players[1].PulledBy == -1,
                "Downed caster retained a teammate connection.");

            game = Game(2); s = game.Capture(); p = s.Players.ToArray();
            p[1].Vitals.Health = 0; p[1].Stamina = 50 * StaminaSystem.UnitsPerPoint;
            game.Restore(WithPlayers(s, p));
            Step(game, new HookInput(0, 0, p[1].X, p[1].Z, true));
            for (int n = 0; n < 40; n++) Step(game);
            s = game.Capture();
            Require(s.Players[1].TimesHooked == 1 && s.Players[1].X < p[1].X && s.Players[1].PulledBy == -1,
                "Downed teammate could not be pulled and released.");
            Require(s.Players[1].Vitals.Downed && s.Players[1].Stamina == p[1].Stamina && s.Coins == 0,
                "Pulling revived a downed teammate, regenerated stamina or granted a reward.");
            new HookGame().Decode(new HookGame().Encode(s));
        });
    }
}

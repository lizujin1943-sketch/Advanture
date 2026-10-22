using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.CharacterTests;

internal static partial class Program
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static int Main(string[] args)
    {
        var cases = DomainCases().Concat(EquipmentCases()).Concat(CodecCases()).Concat(GameCases()).Concat(ApparelCases());
        if (!args.Contains("--simulation-only")) cases = cases.Concat(NetworkCases());
        string? filter = args.FirstOrDefault(a => a.StartsWith("--filter=", StringComparison.Ordinal));
        if (filter != null) cases = cases.Where(c => c.Item1.Contains(filter.Substring(9), StringComparison.Ordinal));
        int passed = 0, total = 0;
        foreach (var (name, test) in cases)
        {
            total++;
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); }
        }
        Console.WriteLine($"{passed}/{total} passed"); return passed == total && total > 0 ? 0 : 1;
    }
    private static CharacterStats Stats(params CharacterModifier[] mods) => CharacterStats.Calculate(new CharacterConfig(), 100, mods);
    private static IEnumerable<(string, Action)> DomainCases()
    {
        yield return ("character_spawn_uses_base_values_and_equipped_modifiers", () => {
            var stats = Stats(new CharacterModifier(stamina: 20)); var vitals = CharacterSystem.Spawn(stats);
            Require(vitals.Health == 100 && vitals.Hunger == 100, "Incorrect spawn vitals.");
            Require(stats.MoveSpeed == 3000 && stats.StaminaMax == 120 * StaminaSystem.UnitsPerPoint, "Incorrect final stats.");
            var boosted = Stats(new CharacterModifier(health: 30, moveSpeed: 150, armor: 15, hunger: 10, stamina: 40));
            Require(boosted.HealthMax == 130 && boosted.MoveSpeed == 3150 && boosted.Armor == 15 && boosted.HungerMax == 110 && boosted.StaminaMax == 140 * StaminaSystem.UnitsPerPoint, "Fixed equipment bonuses ignored.");
        });
        yield return ("character_swap_clamps_caps_without_refilling_any_pool", () => {
            var vitals = new CharacterVitals { Health = 75, Hunger = 61 }; int stamina = 108 * StaminaSystem.UnitsPerPoint;
            CharacterSystem.Clamp(ref vitals, ref stamina, Stats(new CharacterModifier(health: 50, armor: 30, hunger: 20, stamina: 80)));
            Require(vitals.Health == 75 && vitals.Hunger == 61 && stamina == 108 * StaminaSystem.UnitsPerPoint, "Equipping better gear refilled a resource.");
            CharacterSystem.Clamp(ref vitals, ref stamina, Stats(new CharacterModifier(health: -50, armor: 5, hunger: -50)));
            Require(vitals.Health == 50 && vitals.Hunger == 50 && stamina == 100 * StaminaSystem.UnitsPerPoint, "Unequip did not cap resources.");
            CharacterSystem.Clamp(ref vitals, ref stamina, Stats(new CharacterModifier(health: 50, armor: 30, stamina: 80)));
            Require(vitals.Health == 50 && stamina == 100 * StaminaSystem.UnitsPerPoint, "Repeated swap regenerated resources.");
        });
        yield return ("character_defense_reduces_every_combat_hit_without_being_consumed", () => {
            var v = new CharacterVitals { Health = 100, Hunger = 80 };
            var stats = Stats(new CharacterModifier(armor: 100));
            CharacterSystem.Damage(ref v, 20, DamageKind.Combat, stats.Armor);
            Require(v.Health == 90, "100 defense should halve a combat hit.");
            CharacterSystem.Damage(ref v, 20, DamageKind.Combat, stats.Armor);
            Require(v.Health == 80 && stats.Armor == 100, "Defense was consumed by the first hit.");
            CharacterSystem.Damage(ref v, 10, DamageKind.Direct, stats.Armor);
            Require(v.Health == 70, "Direct damage must bypass defense.");
            Require(CharacterDamage.Resolve(0, 20000, DamageKind.Combat) == 0 && CharacterDamage.Resolve(1, 20000, DamageKind.Combat) == 1, "Rounding created damage or immunity.");
            Require(CharacterDamage.Resolve(10, 20, DamageKind.Combat) == 9, "Combat damage must round up deterministically.");
            Require(CharacterDamage.Resolve(int.MaxValue, 0, DamageKind.Combat) == int.MaxValue, "Large damage overflowed.");
            CharacterSystem.Damage(ref v, int.MaxValue, DamageKind.Direct, stats.Armor);
            Require(v.Downed && v.Health == 0, "Lethal damage must clamp at zero.");
            Throws<ArgumentOutOfRangeException>(() => CharacterDamage.Resolve(-1, 0, DamageKind.Combat));
            Throws<ArgumentOutOfRangeException>(() => CharacterDamage.Resolve(10, -1, DamageKind.Combat));
            Throws<ArgumentOutOfRangeException>(() => CharacterDamage.Resolve(10, 0, (DamageKind)2));
        });
        yield return ("character_resource_restore_is_bounded_and_does_not_implicitly_revive", () => {
            var v = new CharacterVitals { Health = 80, Hunger = 20 }; var s = Stats(new CharacterModifier(armor: 10));
            CharacterSystem.RestoreResources(ref v, s, health: int.MaxValue, hunger: 45);
            Require(v.Health == 100 && v.Hunger == 65, "Resource restore overflowed or targeted wrong resource.");
            v.Health = 0; CharacterSystem.RestoreResources(ref v, s, health: 100);
            Require(v.Downed, "Generic healing must not implement an undefined revival.");
        });
        yield return ("character_fractional_movement_accumulates_exactly_in_both_directions", () => {
            int rx = 0, rz = 0, x = 0, z = 0;
            for (int i = 0; i < 30; i++) { x += CharacterMovement.Delta(1000, 3125, ref rx); z += CharacterMovement.Delta(-1000, 3125, ref rz); }
            Require(x == 3125 && z == -3125 && rx == 0 && rz == 0, "30 Hz discarded speed fractions.");
            x = 0; rx = 0; for (int i = 0; i < 300; i++) x += CharacterMovement.Delta(707, 3001, ref rx);
            Require(x == 21217, "Diagonal speed lost precision.");
            int before = rx; Require(CharacterMovement.Delta(0, 3001, ref rx) == 0 && rx == before, "Idle input caused movement.");
        });
        yield return ("character_config_and_snapshot_reject_invalid_resources", () => {
            Throws<InvalidDataException>(() => new CharacterConfig(health: 0));
            Throws<InvalidDataException>(() => new CharacterConfig(moveSpeed: int.MaxValue));
            Throws<InvalidDataException>(() => new CharacterConfig(armor: -1));
            var stats = Stats(); var v = CharacterSystem.Spawn(stats);
            CharacterStateCodec.Validate(v, 0, 0, stats);
            v.Health = 101; Throws<InvalidDataException>(() => CharacterStateCodec.Validate(v, 0, 0, stats));
            v.Health = 100; Throws<InvalidDataException>(() => CharacterStateCodec.Validate(v, 30000, 0, stats));
            var config = new CharacterConfig(130, 3456, 15, 110);
            using var ms = new MemoryStream(); using var w = new BinaryWriter(ms); config.Write(w); w.Flush(); ms.Position = 0;
            using var r = new BinaryReader(ms); var restored = CharacterConfig.Read(r);
            Require(restored.Health == 130 && restored.MoveSpeed == 3456 && restored.Armor == 15 && restored.Hunger == 110, "Config snapshot lost values.");
        });
    }
}

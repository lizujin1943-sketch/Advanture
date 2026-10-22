using System;
using System.Collections.Generic;
using System.IO;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.CharacterTests;

internal static partial class Program
{
    private static IEnumerable<(string, Action)> CodecCases()
    {
        yield return ("character_checkpoint_keeps_partial_resources_and_signed_movement_remainders", () => {
            var input = new CharacterVitals { Health = 53, Hunger = 39 };
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            CharacterStateCodec.Write(writer, input, 12345, -23456); writer.Flush(); stream.Position = 0;
            using var reader = new BinaryReader(stream); var output = CharacterStateCodec.Read(reader, out int x, out int z);
            CharacterStateCodec.Validate(output, x, z, Stats(new CharacterModifier(armor: 10)));
            Require(output.Health == 53 && output.Hunger == 39 && x == 12345 && z == -23456, "Checkpoint reset partial resources or motion.");
        });
        yield return ("character_extreme_modifiers_stay_within_safe_fixed_point_limits", () => {
            var stats = Stats(new CharacterModifier(health: int.MaxValue, moveSpeed: int.MaxValue, armor: int.MaxValue, hunger: int.MaxValue, stamina: int.MaxValue),
                new CharacterModifier(health: int.MaxValue, stamina: int.MaxValue));
            Require(stats.HealthMax == CharacterConfig.MaximumPoints && stats.StaminaMax == CharacterConfig.MaximumPoints * StaminaSystem.UnitsPerPoint && stats.MoveSpeed == CharacterConfig.MaximumSpeed, "Attribute arithmetic overflowed.");
            stats = Stats(new CharacterModifier(health: int.MinValue, moveSpeed: int.MinValue, armor: int.MinValue, hunger: int.MinValue, stamina: int.MinValue));
            Require(stats.HealthMax == 1 && stats.MoveSpeed == 0 && stats.Armor == 0 && stats.HungerMax == 1 && stats.StaminaMax == StaminaSystem.UnitsPerPoint, "Negative modifiers produced invalid upper limits.");
        });
    }
}

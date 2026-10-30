using System;

namespace Advanture.HookIsland.Simulation;

/// <summary>Deterministic damage reduction. Armor is a derived defense value, never a resource pool.</summary>
public static class CharacterDamage
{
    public const int DefenseScale = 100;

    public static int Resolve(int incoming, int armor, DamageKind kind)
    {
        if (incoming < 0 || armor < 0 || armor > CharacterConfig.MaximumPoints || kind > DamageKind.Direct)
            throw new ArgumentOutOfRangeException(nameof(incoming));
        if (kind == DamageKind.Direct || incoming == 0) return incoming;
        int denominator = DefenseScale + armor;
        return (int)(((long)incoming * DefenseScale + denominator - 1) / denominator);
    }
}

using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>Fixed additive equipment bonuses. Point values are converted to stamina units only at aggregation.</summary>
public readonly struct CharacterModifier
{
    public CharacterModifier(int health = 0, int moveSpeed = 0, int armor = 0, int hunger = 0, int stamina = 0)
    { Health = health; MoveSpeed = moveSpeed; Armor = armor; Hunger = hunger; Stamina = stamina; }
    public int Health { get; }
    public int MoveSpeed { get; }
    public int Armor { get; }
    public int Hunger { get; }
    public int Stamina { get; }
}

/// <summary>Derived values, never a second saved authority. Any equipped item can supply the same modifier contract.</summary>
public readonly struct CharacterStats
{
    private CharacterStats(long health, long speed, long armor, long hunger, long stamina)
    {
        HealthMax = Bound(health, 1, CharacterConfig.MaximumPoints);
        MoveSpeed = Bound(speed, 0, CharacterConfig.MaximumSpeed);
        Armor = Bound(armor, 0, CharacterConfig.MaximumPoints);
        HungerMax = Bound(hunger, 1, CharacterConfig.MaximumPoints);
        StaminaMax = Bound(stamina, 1, CharacterConfig.MaximumPoints) * StaminaSystem.UnitsPerPoint;
    }
    public int HealthMax { get; }
    public int MoveSpeed { get; }
    public int Armor { get; }
    public int HungerMax { get; }
    public int StaminaMax { get; }
    public static CharacterStats Calculate(CharacterConfig config, int staminaBase, IEnumerable<CharacterModifier> equipment)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (staminaBase < 1 || staminaBase > 10000) throw new ArgumentOutOfRangeException(nameof(staminaBase));
        long health = config.Health, speed = config.MoveSpeed, armor = config.Armor, hunger = config.Hunger, stamina = staminaBase;
        foreach (var modifier in equipment)
        {
            health += modifier.Health; speed += modifier.MoveSpeed; armor += modifier.Armor;
            hunger += modifier.Hunger; stamina += modifier.Stamina;
        }
        return new CharacterStats(health, speed, armor, hunger, stamina);
    }
    private static int Bound(long value, int min, int max) => (int)Math.Max(min, Math.Min(max, value));
}

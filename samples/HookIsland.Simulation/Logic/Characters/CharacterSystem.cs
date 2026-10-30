using System;

namespace Advanture.HookIsland.Simulation;

/// <summary>Current resources only. Stamina remains in Fisher.Stamina, shared by all activities.</summary>
public struct CharacterVitals
{
    public int Health, Hunger;
    public bool Downed => Health == 0;
}
public enum DamageKind : byte { Combat, Direct }

/// <summary>Resource rules independent of networking, input, inventory transactions and Unity.</summary>
public static class CharacterSystem
{
    /// <summary>Only a new character is filled. Restoring a session or equipping an item never calls this.</summary>
    public static CharacterVitals Spawn(CharacterStats stats) =>
        new CharacterVitals { Health = stats.HealthMax, Hunger = stats.HungerMax };

    public static void Clamp(ref CharacterVitals vitals, ref int stamina, CharacterStats stats)
    {
        vitals.Health = Limit(vitals.Health, stats.HealthMax);
        vitals.Hunger = Limit(vitals.Hunger, stats.HungerMax);
        stamina = Limit(stamina, stats.StaminaMax);
    }
    /// <summary>Pass the current derived Armor for combat; direct environmental damage ignores it.</summary>
    public static void Damage(ref CharacterVitals vitals, int amount, DamageKind kind, int armor)
    {
        int resolved = CharacterDamage.Resolve(amount, armor, kind);
        vitals.Health = Math.Max(0, vitals.Health - resolved);
    }
    /// <summary>Explicit health/hunger grants. Defense is not a consumable resource; this does not revive.</summary>
    public static void RestoreResources(ref CharacterVitals vitals, CharacterStats stats, int health = 0, int hunger = 0)
    {
        if (health < 0 || hunger < 0) throw new ArgumentOutOfRangeException(nameof(health));
        if (!vitals.Downed) vitals.Health = (int)Math.Min(stats.HealthMax, (long)vitals.Health + health);
        vitals.Hunger = (int)Math.Min(stats.HungerMax, (long)vitals.Hunger + hunger);
    }
    private static int Limit(int value, int maximum) => Math.Max(0, Math.Min(value, maximum));
}

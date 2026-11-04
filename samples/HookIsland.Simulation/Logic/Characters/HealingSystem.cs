using System;

namespace Advanture.HookIsland.Simulation;

/// <summary>Commits health and charge consumption together after every check succeeds.</summary>
internal static class HealingSystem
{
    internal static void Refill(ref Fisher player,HealingConfig config)
    {player.HealingCharges=config.Charges;}

    internal static InventoryResult Use(ref Fisher player,CharacterStats stats,HealingConfig config,
        bool grounded,bool rescuing,Action<HookGameplayEvent>? emit)
    {
        if(player.Unavailable || player.WaitingToJoin || player.Vitals.Downed || player.Revival.Dead || !grounded || player.Swimming.InWater)
            return InventoryResult.Disabled;
        if(player.Phase!=HookPhase.Ready || player.Melee.Active || player.PulledBy>=0 || player.HookedPlayer>=0 || player.Knockback.Active || rescuing)
            return InventoryResult.HookBusy;
        if(player.Vitals.Health>=stats.HealthMax)return InventoryResult.HealthFull;
        if(player.HealingCharges==0)return InventoryResult.NoHealingCharges;
        int restored=Math.Min(config.Amount(stats.HealthMax),stats.HealthMax-player.Vitals.Health);
        player.HealingCharges--;player.Vitals.Health+=restored;
        emit?.Invoke(new HookGameplayEvent(HookGameplayEventKind.Healed,player.Slot,amount:restored,
            value:player.HealingCharges,x:player.X,z:player.Z));
        return InventoryResult.Success;
    }
}

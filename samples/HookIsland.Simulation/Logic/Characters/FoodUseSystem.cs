using System;

namespace Advanture.HookIsland.Simulation;

/// <summary>Instant confirmed transaction; eating animation is never an authoritative intermediate state.</summary>
internal static class FoodUseSystem
{
    internal static InventoryResult Use(InventorySystem inventory,ref Fisher player,int id,CharacterStats stats,
        int cooldownTicks,bool grounded,bool rescuing)
    {
        if(player.Unavailable || player.WaitingToJoin || player.Vitals.Downed || player.Revival.Dead || !grounded || player.Swimming.InWater)
            return InventoryResult.Disabled;
        if(player.Phase!=HookPhase.Ready || player.Melee.Active || player.PulledBy>=0 || player.HookedPlayer>=0 || player.Knockback.Active || rescuing)
            return InventoryResult.HookBusy;
        var item=inventory.Find(id);
        if(!item.HasValue || item.Value.Owner!=player.Slot || item.Value.Location!=ItemLocation.Backpack)return InventoryResult.NotOwned;
        if(item.Value.Kind!=ItemKind.Food)return InventoryResult.InvalidItem;
        if(player.Vitals.Health>=stats.HealthMax)return InventoryResult.HealthFull;
        if(player.FoodCooldownTicks>0)return InventoryResult.CoolingDown;
        int restored=Math.Min(FoodCatalog.Get(item.Value.Type).Health,stats.HealthMax-player.Vitals.Health);
        var result=inventory.ConsumeFood(player.Slot,id);
        if(result!=InventoryResult.Success)return result;
        player.Vitals.Health+=restored;player.FoodCooldownTicks=cooldownTicks;
        inventory.Events?.Invoke(new HookGameplayEvent(HookGameplayEventKind.FoodConsumed,player.Slot,amount:restored,
            x:player.X,z:player.Z,itemKind:ItemKind.Food,itemType:item.Value.Type));
        return InventoryResult.Success;
    }
}

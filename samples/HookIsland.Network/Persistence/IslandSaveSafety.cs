using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

/// <summary>Confirmed-state eligibility. Requests wait; they never cancel a player's current action.</summary>
public static class IslandSaveSafety
{
    public static bool IsSafe(HookState state)=>state!=null && state.Players.All(p=>
        p.Phase==HookPhase.Ready && p.FishId<0 && p.DriftwoodId<0 && p.HookedPlayer<0 && p.PulledBy<0 &&
        p.HookedPiranha==0 && p.HookedChest==0 && !p.HookedFinale &&
        !p.Melee.Active && !p.Knockback.Active && p.Revival.Rescuer==0 && p.Revival.RescueTicks==0);
}

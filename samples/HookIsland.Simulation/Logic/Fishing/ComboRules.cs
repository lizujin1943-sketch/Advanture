using System;

namespace Advanture.HookIsland.Simulation;

public struct ComboState
{
    public int Count;
    // False after a settled harvest or a teammate hit: the empty return must not break the streak.
    public bool PendingCast;
}

public static class ComboRules
{
    public static int NextCount(ComboConfig? config,int count)=>config?.Enabled==true?count==int.MaxValue?count:count+1:0;
    public static long Gold(ComboConfig? config,int baseGold,int nextCount)=>
        baseGold+(long)baseGold*(config?.BonusPercent(nextCount)??0)/100;
    public static void Begin(ref ComboState state,ComboConfig? config)=>state.PendingCast=config?.Enabled==true;
    public static void Succeed(ref ComboState state,ComboConfig? config)
    {state.Count=NextCount(config,state.Count);state.PendingCast=false;}
    public static void Preserve(ref ComboState state)=>state.PendingCast=false;
    public static void FailPending(ref ComboState state)
    {if(state.PendingCast)state.Count=0;state.PendingCast=false;}
    public static void FailHarvest(ref ComboState state)=>state=default;
    // Entering water resets the streak, not a still-valid hooked fish. It may later start a new streak.
    public static void Interrupt(ref ComboState state)=>state.Count=0;
}

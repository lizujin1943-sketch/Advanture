using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>Integer-only rules shared by ordinary fish, piranhas and read-only presentation.</summary>
public static class CooperativeFishing
{
    public static bool Attached(Fisher p)=>p.FishId>=0 || p.HookedPiranha>0;
    public static bool SameTarget(Fisher a,Fisher b)=>Attached(a) && a.FishId==b.FishId && a.HookedPiranha==b.HookedPiranha;
    public static int Count(IReadOnlyList<Fisher> players,Fisher target)
    {int count=0;foreach(var p in players)if(SameTarget(p,target))count++;return count;}
    public static bool IsOpposite(Point fish,Point lead,Point joining)=>
        ((long)lead.X-fish.X)*(joining.X-fish.X)+((long)lead.Z-fish.Z)*(joining.Z-fish.Z)<0;
    public static int Speed(int singleSpeed,int count,int bonusPermille)=>
        checked((int)((long)singleSpeed*(1000+(count-1)*bonusPermille)/1000));
    // Twelfths exactly represent division among 1–4 anglers. Carry sub-unit costs between ticks.
    public static int Drain(int total,int count,ref int remainder)
    {long amount=(long)total*12/count+remainder;remainder=(int)(amount%12);return checked((int)(amount/12));}
    public static int Share(int reward,int count,bool lead)=>reward/count+(lead?reward%count:0);
    public static int Lead(HookState s,Fisher p)=>p.FishId>=0?s.Fish[p.FishId].Owner:
        p.HookedPiranha>0?s.Enemies.Piranhas[p.HookedPiranha-1].Owner:-1;
}

using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

public static class KnockbackSystem
{
    public const int Distance=4000,Speed=10000;
    public static void Start(ref Fisher p,int directionX,int directionZ,int distance=Distance)
    {
        if(distance<1000 || distance>46000)throw new ArgumentOutOfRangeException(nameof(distance));
        EnemyGeometry.Direction(directionX,directionZ,out int dx,out int dz);
        p.Knockback=new KnockbackState {OriginX=p.X,OriginZ=p.Z,DirectionX=dx,DirectionZ=dz,TotalDistance=distance,Active=true};
        p.Shopping=false;p.Swimming.Active=false;
    }
    public static void Advance(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,ref Fisher p)
    {
        if(!p.Knockback.Active)return;
        var k=p.Knockback;
        k.Distance+=Math.Min(FishingRules.Travel(Speed,ref k.TravelRemainder),k.TotalDistance-k.Distance);
        var goal=new Point(k.OriginX+(int)((long)k.DirectionX*k.Distance/EnemyGeometry.DirectionScale),
            k.OriginZ+(int)((long)k.DirectionZ*k.Distance/EnemyGeometry.DirectionScale));
        var next=SwimmingTerrain.Move(map,tiles,players,p.Slot,new Point(p.X,p.Z),goal,p.Swimming.InWater,false,out bool water);
        p.X=next.X;p.Z=next.Z;p.Swimming.InWater=water;p.Swimming.Active=false;p.Shopping=false;
        k.Active=k.Distance<k.TotalDistance && next.X==goal.X && next.Z==goal.Z;p.Knockback=k;
    }
}

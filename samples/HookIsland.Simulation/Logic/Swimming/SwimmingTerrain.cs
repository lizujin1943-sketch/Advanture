using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>Deterministic water/ground traversal and rescue landing queries, with no Unity physics dependency.</summary>
public static class SwimmingTerrain
{
    public const int WorldLimit=50000,BodySpacing=PlatformRules.Footprint*2;
    public static bool Ground(IslandMap map,IReadOnlyList<PlatformTile> tiles,int x,int z)=>
        map.Platform==null?map.CanStand(x,z):PlatformRules.CanStand(tiles,x,z);
    public static bool Obstacle(IslandMap map,int x,int z)=>
        Math.Abs((long)x)>WorldLimit || Math.Abs((long)z)>WorldLimit ||
        (map.Platform==null && IslandMap.Blocked(x,z,330)) ||
        (map.Fishing!=null && MerchantRules.Obstacle(map.Fishing,x,z));
    public static bool Landing(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,int slot,int x,int z)
    {
        if(Obstacle(map,x,z) || !Ground(map,tiles,x,z))return false;
        foreach(var p in players)
            if(!p.WaitingToJoin && p.Slot!=slot && IslandMap.DistanceSquared(x,z,p.X,p.Z)<(long)BodySpacing*BodySpacing)return false;
        return true;
    }
    /// <summary>Sample the entire segment. Water may approach a low deck lip, but cannot occupy an obstructed landing.</summary>
    public static Point Move(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,int slot,
        Point from,Point to,bool wasWater,bool stopOnLanding,out bool inWater)
    {
        int dx=to.X-from.X,dz=to.Z-from.Z,length=HookGame.Sqrt((long)dx*dx+(long)dz*dz);
        int segments=Math.Max(1,(length+49)/50);var result=from;inWater=wasWater;
        for(int i=1;i<=segments;i++)
        {
            int x=from.X+(int)((long)dx*i/segments),z=from.Z+(int)((long)dz*i/segments);
            if(Obstacle(map,x,z))break;
            bool ground=Ground(map,tiles,x,z);
            if(inWater && ground && !Landing(map,tiles,players,slot,x,z))break;
            bool landed=inWater && ground;result=new Point(x,z);inWater=!ground;
            if(landed && stopOnLanding)break;
        }
        return result;
    }
    /// <summary>Select a reachable free landing near the rescuer; never use proximity to declare rescue success.</summary>
    public static bool RescueLanding(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,
        Fisher owner,Fisher target,out Point destination)
    {
        destination=default;long best=long.MaxValue;
        // Fixed quarter-metre offsets give identical candidate order and tie breaks on every peer.
        for(int dz=-1500;dz<=1500;dz+=250)for(int dx=-1500;dx<=1500;dx+=250)
        {
            int x=owner.X+dx,z=owner.Z+dz;
            if(!Landing(map,tiles,players,target.Slot,x,z))continue;
            long distance=IslandMap.DistanceSquared(target.X,target.Z,x,z);
            if(distance>=best)continue;
            var reached=Move(map,tiles,players,target.Slot,new Point(target.X,target.Z),new Point(x,z),true,true,out bool water);
            if(water || !Landing(map,tiles,players,target.Slot,reached.X,reached.Z))continue;
            best=distance;destination=new Point(x,z);
        }
        return best!=long.MaxValue;
    }
}

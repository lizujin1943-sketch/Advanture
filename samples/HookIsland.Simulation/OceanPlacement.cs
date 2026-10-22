using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Bounded, integer-only placement of full fish lanes beside the current platform.</summary>
public static class OceanPlacement
{
    private static readonly Point[] Directions={new Point(0,-1),new Point(1,0),new Point(0,1),new Point(-1,0)};
    public static bool Find(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,
        IReadOnlyList<Point> existing,uint sequence,out Point home)
    {
        home=default;
        var onLand=players.Where(p=>!p.Unavailable && !p.Vitals.Downed && PlatformRules.CanStand(tiles,p.X,p.Z)).ToArray();
        if(onLand.Length==0 || map.Platform==null)return false;
        var occupied=Occupancy(map,tiles);
        var edges=tiles.SelectMany(t=>Enumerable.Range(0,4).Select(d=>(t,d)))
            .Where(e=>!occupied.Contains((e.t.X+Directions[e.d].X,e.t.Z+Directions[e.d].Z)))
            .OrderBy(e=>onLand.Min(p=>IslandMap.DistanceSquared(p.X,p.Z,e.t.X*2000,e.t.Z*2000)))
            .ThenBy(e=>e.t.X).ThenBy(e=>e.t.Z).ThenBy(e=>(e.d+(int)(sequence%4))%4);
        foreach(var edge in edges)
        {
            var d=Directions[edge.d];
            var anchor=new Point(edge.t.X*2000+d.X*700,edge.t.Z*2000+d.Z*700);
            var candidate=new Point(edge.t.X*2000+d.X*2800,edge.t.Z*2000+d.Z*2800);
            if(!PlatformRules.CanStand(tiles,anchor.X,anchor.Z) ||
                existing.Any(p=>IslandMap.DistanceSquared(p.X,p.Z,candidate.X,candidate.Z)<2500L*2500) || !LaneWater(occupied,candidate))continue;
            home=candidate;return true;
        }
        return false;
    }
    public static bool LaneWater(IslandMap map,IReadOnlyList<PlatformTile> tiles,Point home)=>LaneWater(Occupancy(map,tiles),home);
    private static HashSet<(int,int)> Occupancy(IslandMap map,IReadOnlyList<PlatformTile> tiles)
    {
        var cells=new HashSet<(int,int)>(tiles.Select(t=>(t.X,t.Z)));
        if(map.Platform!=null)foreach(var p in map.Platform.BlockedCells)cells.Add((p.X,p.Z));
        return cells;
    }
    private static bool LaneWater(HashSet<(int,int)> occupied,Point home)
    {
        var direction=FishMotion.Direction(new Fish{Heading=1},home);
        // 100 mm extra covers the half-step between samples, beyond the 720 mm fish radius.
        const int margin=820;
        for(int along=-FishMotion.HalfLane;along<=FishMotion.HalfLane;along+=200)
        {
            int x=home.X+direction.X*along/1000,z=home.Z+direction.Z*along/1000;
            if(Math.Abs(x)>50000-margin || Math.Abs(z)>50000-margin)return false;
            for(int cx=PlatformRules.CellAt(x-margin);cx<=PlatformRules.CellAt(x+margin);cx++)
                for(int cz=PlatformRules.CellAt(z-margin);cz<=PlatformRules.CellAt(z+margin);cz++)
                    if(occupied.Contains((cx,cz)))return false;
        }
        return true;
    }
}

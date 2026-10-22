using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public readonly struct FinaleSite
{
    public FinaleSite(Point position,int direction){Position=position;Direction=direction;}
    public Point Position {get;}
    public int Direction {get;}
}

/// <summary>Chooses a reachable bank and a body entirely in water, with a bounded deterministic scan.</summary>
public static class FinalePlacement
{
    public static bool Find(IslandMap map,FinaleConfig config,IReadOnlyList<PlatformTile> tiles,
        IReadOnlyList<Fisher> players,int completed,Point previous,out FinaleSite site)
    {
        site=default;
        var ready=players.Where(p=>!p.Unavailable && !p.Vitals.Downed && !p.Revival.Dead && PlatformRules.CanStand(tiles,p.X,p.Z)).ToArray();
        if(ready.Length==0 || map.Platform==null)return false;
        var occupied=new HashSet<(int,int)>(tiles.Select(t=>(t.X,t.Z)));
        foreach(var p in map.Platform.BlockedCells)occupied.Add((p.X,p.Z));
        var candidates=tiles.SelectMany(t=>Enumerable.Range(0,4).Select(d=>(tile:t,direction:d)))
            .Where(e=>{var d=FinaleRules.Direction(e.direction);return !occupied.Contains((e.tile.X+d.X,e.tile.Z+d.Z));})
            .OrderBy(e=>(e.direction-completed+4)%4)
            .ThenBy(e=>ready.Min(p=>IslandMap.DistanceSquared(p.X,p.Z,e.tile.X*2000,e.tile.Z*2000)))
            .ThenBy(e=>e.tile.X).ThenBy(e=>e.tile.Z).ToArray();
        // First prefer a meaningful move. A legal alternate shore is better than an impossible distance gate.
        for(int pass=0;pass<2;pass++)foreach(var e in candidates)
        {
            var d=FinaleRules.Direction(e.direction);
            var position=new Point(e.tile.X*2000+d.X*config.EdgeOffset,e.tile.Z*2000+d.Z*config.EdgeOffset);
            long shift=IslandMap.DistanceSquared(position.X,position.Z,previous.X,previous.Z);
            if(completed>0 && (shift==0 || pass==0 && shift<(long)config.PhaseShift*config.PhaseShift))continue;
            var anchor=new Point(e.tile.X*2000+d.X*700,e.tile.Z*2000+d.Z*700);
            if(!PlatformRules.CanStand(tiles,anchor.X,anchor.Z) || !Water(occupied,config,position))continue;
            site=new FinaleSite(position,e.direction);return true;
        }
        return false;
    }
    public static bool Water(IslandMap map,FinaleConfig config,IReadOnlyList<PlatformTile> tiles,Point position)
    {
        var occupied=new HashSet<(int,int)>(tiles.Select(t=>(t.X,t.Z)));
        if(map.Platform!=null)foreach(var p in map.Platform.BlockedCells)occupied.Add((p.X,p.Z));
        return Water(occupied,config,position);
    }
    private static bool Water(HashSet<(int,int)> occupied,FinaleConfig config,Point p)
    {
        int r=config.BodyRadius;
        if(Math.Abs((long)p.X)>50000-r || Math.Abs((long)p.Z)>50000-r)return false;
        for(int x=PlatformRules.CellAt(p.X-r);x<=PlatformRules.CellAt(p.X+r);x++)
            for(int z=PlatformRules.CellAt(p.Z-r);z<=PlatformRules.CellAt(p.Z+r);z++)
                if(occupied.Contains((x,z)) && FinaleRules.BodyOverlapsCell(config,p,x,z))return false;
        return true;
    }
}

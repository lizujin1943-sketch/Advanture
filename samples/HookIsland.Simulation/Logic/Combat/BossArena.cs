using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>初始主平台不变；北侧窄桥接入海面环形战场。所有地格与建造使用同一坐标网格。</summary>
public static class BossArena
{
    private static readonly PlatformTile[] RingTiles=CreateRing();
    public static PlatformTile[] InitialTiles(FishingConfig? config=null)
    {
        var tiles=PlatformRules.InitialTiles().ToList();
        tiles.Add(new PlatformTile(0,2,TileKind.Wood));tiles.Add(new PlatformTile(0,3,TileKind.Wood));
        foreach(var tile in RingTiles)
            if(config==null || !config.Merchants.Definitions.Any(m=>m.CellX==tile.X && m.CellZ==tile.Z))tiles.Add(tile);
        return tiles.OrderBy(t=>t.Z).ThenBy(t=>t.X).ToArray();
    }
    private static PlatformTile[] CreateRing()
    {
        var tiles=new List<PlatformTile>();
        for(int z=-PlatformRules.GridLimit;z<=PlatformRules.GridLimit;z++)
            for(int x=-PlatformRules.GridLimit;x<=PlatformRules.GridLimit;x++)
            {
                long d=IslandMap.DistanceSquared(x*PlatformRules.TileSize,z*PlatformRules.TileSize,BossConfig.CenterX,BossConfig.CenterZ);
                if(d>=(long)BossConfig.ArenaInnerRadius*BossConfig.ArenaInnerRadius && d<=(long)BossConfig.ArenaOuterRadius*BossConfig.ArenaOuterRadius)
                    tiles.Add(new PlatformTile(x,z,TileKind.Wood));
            }
        return tiles.ToArray();
    }
    public static bool Destructible(int x,int z)=>!(Math.Abs(x)<=1 && Math.Abs(z)<=1) &&
        Enumerable.Range(0,4).Any(id=>BossGeometry.SlamContainsXZ(id,x*PlatformRules.TileSize,z*PlatformRules.TileSize));
    public static bool NearBeacon(Fisher p)=>IslandMap.DistanceSquared(p.X,p.Z,BossConfig.BeaconX,BossConfig.BeaconZ)<=2500L*2500;
}

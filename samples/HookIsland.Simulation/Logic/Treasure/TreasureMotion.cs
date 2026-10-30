using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>Deterministic opportunities around the current platform, independent of fish movement.</summary>
public static class TreasureMotion
{
    public const int Radius=450;
    public static TreasureChest Waiting(int id,TreasureKind kind,int delay,uint generation=0)=>
        new TreasureChest { Id=id,Kind=kind,Phase=TreasurePhase.Waiting,Owner=-1,Timer=delay,Generation=generation };
    private static uint Mix(uint x)
    {unchecked{x^=x>>16;x*=0x7feb352d;x^=x>>15;x*=0x846ca68b;return x^(x>>16);}}
    public static bool Water(IReadOnlyList<PlatformTile> tiles,int x,int z)
    {
        if(Math.Abs((long)x)+Radius>SwimmingTerrain.WorldLimit || Math.Abs((long)z)+Radius>SwimmingTerrain.WorldLimit)return false;
        foreach(var t in tiles)if(Math.Abs((long)x-t.X*PlatformRules.TileSize)<PlatformRules.HalfTile+Radius &&
            Math.Abs((long)z-t.Z*PlatformRules.TileSize)<PlatformRules.HalfTile+Radius)return false;
        return true;
    }
    public static bool Spawn(ref TreasureChest c,TreasureConfig config,IReadOnlyList<PlatformTile> tiles,int seed,IReadOnlyList<TreasureChest>? others=null)
    {
        if(tiles.Count==0)return false;
        uint generation=c.Generation==uint.MaxValue?1:c.Generation+1;
        uint random=Mix(unchecked((uint)seed+(uint)c.Id*11731+generation*104729));
        // Examine all tile edges in stable order from a seeded start. A failed placement retries later.
        int first=(int)(random%(uint)(tiles.Count*4));
        for(int attempt=0;attempt<tiles.Count*4;attempt++)
        {
            int edge=(first+attempt)%(tiles.Count*4),side=edge%4;var tile=tiles[edge/4];
            int dx=side==0?-1:side==1?1:0,dz=side==2?-1:side==3?1:0;
            int offset=(int)(Mix(random+(uint)edge)%1001)-500;
            int x=tile.X*PlatformRules.TileSize+dx*2800+(dx==0?offset:0);
            int z=tile.Z*PlatformRules.TileSize+dz*2800+(dz==0?offset:0);
            if(!Water(tiles,x,z))continue;
            bool occupied=false;
            if(others!=null)foreach(var other in others)
                if(other.Id!=c.Id && other.Phase!=TreasurePhase.Waiting && IslandMap.DistanceSquared(x,z,other.X,other.Z)<1000L*1000)
                {occupied=true;break;}
            if(occupied)continue;
            // The approach to the chosen edge must not cross another tile.
            if(!Water(tiles,x-dx*1000,z-dz*1000))continue;
            c.X=x;c.Z=z;c.DirectionX=dx*1000;c.DirectionZ=dz*1000;c.Generation=generation;
            c.Phase=TreasurePhase.Floating;c.Timer=config.FloatLifetimeTicks;c.Owner=-1;c.TravelRemainder=0;
            Rewards(config,c.Kind,c.Id,generation,seed,out c.Coins,out c.Wood,out c.Bones);return true;
        }
        return false;
    }
    public static void Rewards(TreasureConfig config,TreasureKind kind,int id,uint generation,int seed,out int coins,out int wood,out int bones)
    {
        coins=kind==TreasureKind.Small?config.SmallCoins:kind==TreasureKind.Rare?config.RareCoins:config.LargeCoins;
        wood=kind==TreasureKind.Small?config.SmallWood:kind==TreasureKind.Rare?config.RareWood:config.LargeWood;
        bones=kind==TreasureKind.Small?config.SmallBones:kind==TreasureKind.Rare?config.RareBones:config.LargeBones;
        switch(Mix(unchecked((uint)seed+(uint)id*31337+generation*7919))%3)
        {case 0:coins+=coins/2;break;case 1:wood+=wood/2;break;default:bones+=bones/2;break;}
    }
    public static void Advance(ref TreasureChest c,TreasureConfig config,IReadOnlyList<PlatformTile> tiles,int seed,IReadOnlyList<TreasureChest>? others=null)
    {
        if(c.Phase==TreasurePhase.Hooked || c.Phase==TreasurePhase.Landed)return;
        if(c.Phase==TreasurePhase.Waiting)
        {
            if(c.Timer>0)c.Timer--;
            if(c.Timer==0 && !Spawn(ref c,config,tiles,seed,others))c.Timer=HookGame.Rate;
            return;
        }
        int distance=FishingRules.Travel(c.Kind==TreasureKind.Rare?config.RareDriftSpeed:config.DriftSpeed,ref c.TravelRemainder);
        int x=c.X+c.DirectionX*distance/1000,z=c.Z+c.DirectionZ*distance/1000;
        if(--c.Timer==0 || !Water(tiles,x,z))c=Waiting(c.Id,c.Kind,config.RespawnTicks,c.Generation);
        else {c.X=x;c.Z=z;}
    }
}

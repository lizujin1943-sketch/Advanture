using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public enum TileKind : byte { None, Wood, Fishbone }
public enum BuildResult : byte { None, Success, Disabled, OutsideGrid, Occupied, NotAdjacent, InsufficientMaterials, Busy, LimitReached }

public readonly struct PlatformTile
{
    public PlatformTile(int x,int z,TileKind kind,int builder=-1,int paid=0)
    { X=x;Z=z;Kind=kind;Builder=builder;Paid=paid; }
    public int X { get; }
    public int Z { get; }
    public TileKind Kind { get; }
    public int Builder { get; }
    // Retain the historical price; changing recipes must not reprice existing tiles on restore.
    public int Paid { get; }
}

public sealed class PlatformConfig
{
    public PlatformConfig(int woodTileCost=4,int fishboneTileCost=4,IEnumerable<Point>? blockedCells=null)
    {
        if(woodTileCost<1 || woodTileCost>1000 || fishboneTileCost<1 || fishboneTileCost>1000)
            throw new InvalidDataException("Tile costs must be between 1 and 1000.");
        WoodTileCost=woodTileCost;FishboneTileCost=fishboneTileCost;
        BlockedCells=Array.AsReadOnly((blockedCells??Array.Empty<Point>()).ToArray());
        if(BlockedCells.Count>64 || BlockedCells.Any(p=>!PlatformRules.InBounds(p.X,p.Z)))
            throw new InvalidDataException("Invalid reserved building cells.");
    }
    public int WoodTileCost { get; }
    public int FishboneTileCost { get; }
    public IReadOnlyList<Point> BlockedCells { get; }
    public int Cost(TileKind kind)=>kind==TileKind.Wood?WoodTileCost:
        kind==TileKind.Fishbone?FishboneTileCost:throw new ArgumentOutOfRangeException(nameof(kind));
}

/// <summary>2 m grid in integer millimetres. Unity previews and the authority use the same rules.</summary>
public static class PlatformRules
{
    public const int TileSize=2000,HalfTile=1000,GridLimit=24,MaxTiles=512,Footprint=200;
    public static bool InBounds(int x,int z)=>x>=-GridLimit && x<=GridLimit && z>=-GridLimit && z<=GridLimit;
    // Floor division (including negative world coordinates); centres are integer multiples of two metres.
    public static int CellAt(int millimetres)
    {long shifted=millimetres+1000L;return (int)(shifted>=0?shifted/2000:(shifted-1999)/2000);}
    public static PlatformTile[] InitialTiles()=>Enumerable.Range(-1,3)
        .SelectMany(z=>Enumerable.Range(-1,3).Select(x=>new PlatformTile(x,z,TileKind.Wood))).ToArray();
    public static bool Contains(IReadOnlyList<PlatformTile> tiles,int x,int z)=>tiles.Any(t=>t.X==x && t.Z==z);
    public static bool OnPlatform(IReadOnlyList<PlatformTile> tiles,int x,int z)=>Contains(tiles,CellAt(x),CellAt(z));
    public static bool CanStand(IReadOnlyList<PlatformTile> tiles,int x,int z)
    {
        // Check the entire footprint, so concave corners and seams between mixed materials are safe.
        for(int cz=CellAt(z-Footprint);cz<=CellAt(z+Footprint);cz++)
            for(int cx=CellAt(x-Footprint);cx<=CellAt(x+Footprint);cx++)
                if(!Contains(tiles,cx,cz))return false;
        return true;
    }
    public static BuildResult Check(IslandMap map,HookState state,Fisher player,TileKind kind,int x,int z)
        => state.Won || map.Fishing!=null && player.Vitals.Downed?BuildResult.Busy:Check(map.Platform,state.Tiles,state.Players,state.Fish,state.Driftwood,player,kind,x,z,state.Treasure.Chests,map.Fishing?.Finale,state.Finale);

    internal static BuildResult Check(PlatformConfig? config,IReadOnlyList<PlatformTile> tiles,
        IReadOnlyList<Fisher> players,IReadOnlyList<Fish> fish,IReadOnlyList<Driftwood> driftwood,
        Fisher player,TileKind kind,int x,int z,IReadOnlyList<TreasureChest>? treasure=null,FinaleConfig? finaleConfig=null,FinaleState? finale=null)
    {
        if(config==null || kind<TileKind.Wood || kind>TileKind.Fishbone)return BuildResult.Disabled;
        if(!InBounds(x,z))return BuildResult.OutsideGrid;
        if(Contains(tiles,x,z) || config.BlockedCells.Any(p=>p.X==x && p.Z==z))return BuildResult.Occupied;
        if(finaleConfig!=null && finale!=null && FinaleRules.BlocksBuild(finaleConfig,finale,x,z))return BuildResult.Occupied;
        if(!tiles.Any(t=>Math.Abs(t.X-x)+Math.Abs(t.Z-z)==1))return BuildResult.NotAdjacent;
        if(tiles.Count>=MaxTiles)return BuildResult.LimitReached;
        if(player.PulledBy>=0 || player.Shopping || player.Phase!=HookPhase.Ready)return BuildResult.Busy;
        int wx=x*TileSize,wz=z*TileSize;
        if(players.Any(p=>!p.WaitingToJoin && Overlaps(wx,wz,p.X,p.Z,Footprint)) ||
            driftwood.Any(d=>d.Phase!=DriftwoodPhase.Waiting && Overlaps(wx,wz,d.X,d.Z,DriftwoodMotion.Radius(d.Id))) ||
            treasure!=null && treasure.Any(c=>c.Phase!=TreasurePhase.Waiting && Overlaps(wx,wz,c.X,c.Z,TreasureMotion.Radius)) ||
            fish.Any(f=>(f.Phase==FishPhase.Jumping || f.Phase==FishPhase.Hooked) && Overlaps(wx,wz,f.X,f.Z,720)))
            return BuildResult.Occupied;
        return PersonalMaterialInventory.Balance(player,kind)>=config.Cost(kind)?BuildResult.Success:BuildResult.InsufficientMaterials;
    }
    private static bool Overlaps(int x,int z,int px,int pz,int radius)=>
        Math.Abs(px-x)<HalfTile+radius && Math.Abs(pz-z)<HalfTile+radius;
}

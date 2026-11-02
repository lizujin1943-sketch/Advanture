using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

public readonly struct PlatformBuildPreview
{
    public PlatformBuildPreview(PlatformTile tile,BuildResult result){Tile=tile;Result=result;}
    public PlatformTile Tile {get;}
    public BuildResult Result {get;}
}

/// <summary>Local drag intent and read-only material projection. Never mutates a captured game state.</summary>
public sealed class PlatformBuildDraft
{
    public const int MaxCells=PlatformRules.MaxTiles;
    private readonly List<PlatformTile> candidates=new List<PlatformTile>();
    private readonly HashSet<(int,int)> visited=new HashSet<(int,int)>();
    private readonly List<PlatformBuildPreview> cells=new List<PlatformBuildPreview>();
    private readonly List<PlatformTile> projected=new List<PlatformTile>();
    private TileKind kind;
    private int lastX,lastZ;
    private bool hasLast;
    public PlatformBuildDraft(){Cells=cells.AsReadOnly();}
    public IReadOnlyList<PlatformBuildPreview> Cells {get;}
    public bool Active {get;private set;}
    public int ValidCount {get;private set;}
    public int MaterialCost {get;private set;}
    public bool Full=>candidates.Count>=MaxCells;

    public void Begin(TileKind tileKind,int x,int z)
    {
        Cancel();
        if(tileKind<TileKind.Wood || tileKind>TileKind.Fishbone)return;
        Active=true;kind=tileKind;Visit(x,z);
    }

    public void Visit(int x,int z)
    {
        if(!Active)return;
        // Outside-world rays are still red candidates, but must never interpolate millions of cells.
        if(hasLast && PlatformRules.InBounds(lastX,lastZ) && PlatformRules.InBounds(x,z))
        {
            int dx=Math.Abs(x-lastX),dz=Math.Abs(z-lastZ),sx=Math.Sign(x-lastX),sz=Math.Sign(z-lastZ);
            int ix=0,iz=0,cx=lastX,cz=lastZ;
            while((ix<dx || iz<dz) && !Full)
            {
                // A corner tie crosses X first: each consecutive candidate shares a complete edge.
                if((1+2*ix)*dz<=(1+2*iz)*dx){cx+=sx;ix++;}
                else{cz+=sz;iz++;}
                Add(cx,cz);
            }
        }
        else Add(x,z);
        lastX=x;lastZ=z;hasLast=true;
    }

    public void BreakStroke()=>hasLast=false;

    private void Add(int x,int z)
    {
        if(!Full && visited.Add((x,z)))candidates.Add(new PlatformTile(x,z,kind));
    }

    public void Evaluate(IslandMap map,HookState state,Fisher player)
    {
        cells.Clear();projected.Clear();ValidCount=MaterialCost=0;
        if(!Active)return;
        projected.AddRange(state.Tiles);
        // Fisher is a value copy; no InventorySystem or authoritative collection is passed to spending.
        var remaining=player;
        foreach(var tile in candidates)
        {
            var result=PlatformRules.CheckPreview(map,state,remaining,projected,tile.Kind,tile.X,tile.Z);
            cells.Add(new PlatformBuildPreview(tile,result));
            if(result!=BuildResult.Success)continue;
            int cost=map.Platform!.Cost(tile.Kind);
            PersonalMaterialInventory.TrySpend(ref remaining,tile.Kind,cost);
            projected.Add(tile);ValidCount++;MaterialCost+=cost;
        }
    }

    public BuildResult ResultAt(int x,int z)
    {
        foreach(var cell in cells)if(cell.Tile.X==x && cell.Tile.Z==z)return cell.Result;
        return Full?BuildResult.LimitReached:BuildResult.None;
    }

    public void Cancel()
    {
        Active=hasLast=false;ValidCount=MaterialCost=0;
        candidates.Clear();visited.Clear();cells.Clear();projected.Clear();
    }
}

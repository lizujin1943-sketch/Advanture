using System.Collections.Generic;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Network;

/// <summary>Delivers an accepted local draft as ordered, individually atomic build transactions.
/// Only one receipt can be outstanding; a failed tile is final for this batch.</summary>
public sealed class PlatformBuildBatch
{
    private readonly Queue<PlatformTile> queued=new Queue<PlatformTile>();
    private readonly PlatformBuildRequestBuffer request=new PlatformBuildRequestBuffer();
    private PlatformTile current;
    public bool Pending=>request.Pending || queued.Count>0;
    public int Remaining=>queued.Count+(request.Pending?1:0);
    public int Succeeded {get;private set;}
    public int Rejected {get;private set;}
    public int Cancelled {get;private set;}
    public BuildResult LastFailure {get;private set;}
    public IEnumerable<PlatformTile> PendingTiles
    {
        get
        {
            if(request.Pending)yield return current;
            foreach(var tile in queued)yield return tile;
        }
    }

    public bool TryStart(IReadOnlyList<PlatformBuildPreview> cells)
    {
        if(Pending)return false;
        Reset();
        foreach(var cell in cells)
        {
            if(queued.Count==PlatformBuildDraft.MaxCells)break;
            if(cell.Result==BuildResult.Success)queued.Enqueue(cell.Tile);
        }
        return Pending;
    }

    /// <returns>True only on transition from pending to completed.</returns>
    public bool Advance(IslandMap map,HookState state,Fisher player)
    {
        if(!Pending)return false;
        if(request.Pending)
        {
            if(!request.Observe(player,out var receipt))return false;
            if(receipt==BuildResult.Success)Succeeded++;
            else Reject(receipt);
        }
        while(queued.Count>0)
        {
            var tile=queued.Dequeue();
            // Use the newest confirmed state: a teammate may have built here or a fish moved into it.
            var result=PlatformRules.Check(map,state,player,tile.Kind,tile.X,tile.Z);
            if(result!=BuildResult.Success){Reject(result);continue;}
            if(request.TryQueue(player.LastBuildId,tile.Kind,tile.X,tile.Z)){current=tile;return false;}
            Reject(BuildResult.Disabled); // Includes exhausted receipt IDs; never wrap or replay.
        }
        return true;
    }

    public HookInput Compose(HookInput input,bool buildMode)=>request.Compose(input,buildMode || Pending);

    public void CancelUnsent()
    {
        Cancelled+=queued.Count;queued.Clear();
        // The in-flight transaction may already have settled: retain its identity until acknowledged.
    }

    private void Reject(BuildResult result){Rejected++;LastFailure=result;}

    public void Reset()
    {
        queued.Clear();request.Reset();Succeeded=Rejected=Cancelled=0;LastFailure=BuildResult.None;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static HookState ConstructionPreviewState(int wood=8,int bones=8)
    {
        var player=FundedPlatform(wood,bones).Capture().Players[0];
        return new HookState(0,new[]{player},Array.Empty<Fish>(),0,PlatformRules.InitialTiles());
    }

    private static IEnumerable<(string,Action)> PlatformDragCases()
    {
        yield return ("platform_drag_reserves_materials_and_connects_planned_tiles_without_mutating_state",()=>{
            var map=LegacyPlatformMap();var state=ConstructionPreviewState();var draft=new PlatformBuildDraft();
            draft.Begin(TileKind.Wood,2,0);draft.Visit(4,0);draft.Evaluate(map,state,state.Players[0]);
            Require(draft.Cells.Select(c=>c.Result).SequenceEqual(new[]{BuildResult.Success,BuildResult.Success,BuildResult.InsufficientMaterials}),"Budget must colour only the first two tiles green.");
            Require(draft.ValidCount==2 && draft.MaterialCost==8,"Draft did not reserve the entire green path's recipe cost.");
            Require(state.Tiles.Count==9 && state.Players[0].Wood==8,"A preview changed confirmed state.");
            draft.Visit(2,0);draft.Evaluate(map,state,state.Players[0]);
            Require(draft.Cells.Count==3 && draft.MaterialCost==8,"Retracing a stroke reserved the same tile twice.");
        });
        yield return ("platform_drag_fast_diagonal_is_edge_connected_and_hud_gap_is_not_bridged",()=>{
            var draft=new PlatformBuildDraft();var state=ConstructionPreviewState(40);var map=LegacyPlatformMap();
            draft.Begin(TileKind.Wood,2,0);draft.Visit(4,2);draft.Evaluate(map,state,state.Players[0]);
            Require(draft.Cells.Select(c=>(c.Tile.X,c.Tile.Z)).SequenceEqual(new[]{(2,0),(3,0),(3,1),(4,1),(4,2)}),"A fast diagonal skipped grid cells or produced disconnected corners.");
            Require(draft.ValidCount==5,"The continuous diagonal should build five connected tiles.");
            draft.BreakStroke();draft.Visit(-2,0);draft.Evaluate(map,state,state.Players[0]);
            Require(draft.Cells.Count==6,"Crossing HUD painted an unintended bridge through the world.");
            draft.Cancel();Require(!draft.Active && draft.Cells.Count==0 && draft.MaterialCost==0,"Cancelled preview retained a reservation.");
        });
        yield return ("platform_drag_invalid_cells_do_not_connect_or_reserve_and_live_blockers_recolour",()=>{
            var map=LegacyPlatformMap();var state=ConstructionPreviewState(8);var draft=new PlatformBuildDraft();
            draft.Begin(TileKind.Wood,2,2);draft.Visit(2,3);draft.BreakStroke();draft.Visit(2,0);draft.Visit(3,0);
            draft.Evaluate(map,state,state.Players[0]);
            Require(draft.Cells.Select(c=>c.Result).SequenceEqual(new[]{BuildResult.NotAdjacent,BuildResult.NotAdjacent,BuildResult.Success,BuildResult.Success}),"Invalid cells reserved materials or provided false adjacency.");
            var blocked=new HookState(1,state.Players,new[]{new Fish{X=4000,Z=0,Phase=FishPhase.Jumping,Y=800}},0,state.Tiles);
            draft.Evaluate(map,blocked,state.Players[0]);
            Require(draft.Cells[2].Result==BuildResult.Occupied && draft.Cells[3].Result==BuildResult.NotAdjacent && draft.ValidCount==0,"Moving blockers did not invalidate the dependent path.");
            draft.Evaluate(map,state,state.Players[0]);Require(draft.ValidCount==2,"Departing blockers permanently invalidated the draft.");
        });
        yield return ("platform_drag_respects_bone_recipe_and_bounds_and_caps_long_strokes",()=>{
            var map=LegacyPlatformMap(6,3);var state=ConstructionPreviewState(100,7);var draft=new PlatformBuildDraft();
            draft.Begin(TileKind.Fishbone,-2,0);draft.Visit(-4,0);draft.Evaluate(map,state,state.Players[0]);
            Require(draft.ValidCount==2 && draft.MaterialCost==6 && draft.Cells[2].Result==BuildResult.InsufficientMaterials,"Bone preview used wood or the default recipe price.");
            draft.BreakStroke();draft.Visit(int.MaxValue,int.MinValue);draft.Evaluate(map,state,state.Players[0]);
            Require(draft.Cells.Count==4 && draft.Cells[3].Result==BuildResult.OutsideGrid,"Out-of-world rays caused unbounded interpolation or a green tile.");
            for(int x=-24;x<=24;x++)for(int z=-24;z<=24;z++){draft.BreakStroke();draft.Visit(x,z);}
            draft.Evaluate(map,state,state.Players[0]);
            Require(draft.Cells.Count==PlatformBuildDraft.MaxCells,"The preview pool must be bounded.");
        });
        yield return ("platform_drag_batch_waits_for_receipts_and_does_not_replay_failed_cells",()=>{
            var map=LegacyPlatformMap();var model=FundedPlatform(8,0);var state=model.Capture();
            var draft=new PlatformBuildDraft();draft.Begin(TileKind.Wood,2,0);draft.Visit(4,0);draft.Evaluate(map,state,state.Players[0]);
            var batch=new PlatformBuildBatch();Require(batch.TryStart(draft.Cells),"Could not submit green draft tiles.");
            draft.Cancel();batch.Advance(map,state,state.Players[0]);
            var first=batch.Compose(default,false);Require(first.BuildId==1 && first.BuildX==2 && batch.Remaining==2,"Batch did not copy just the two green candidates.");
            batch.Advance(map,state,state.Players[0]);Require(batch.Compose(default,false).BuildId==1,"A stale frame advanced the batch.");
            ShopStep(model,first.Encode());state=model.Capture();batch.Advance(map,state,state.Players[0]);
            var second=batch.Compose(default,false);Require(second.BuildId==2 && second.BuildX==3,"Next tile was not sent after the receipt.");
            // Another input arriving first can make a committed request fail. Its ID stays final.
            var refused=state.Players[0];refused.LastBuildId=2;refused.LastBuildResult=BuildResult.Busy;
            Require(batch.Advance(map,state,refused) && !batch.Pending && batch.Succeeded==1 && batch.Rejected==1,"Failure receipt did not complete the batch.");
            Require(batch.Compose(default,false).Build==TileKind.None,"A rejected cell was implicitly replayed.");
        });
        yield return ("platform_drag_batch_rechecks_contested_cells_and_cancellation_keeps_inflight_identity",()=>{
            var map=LegacyPlatformMap();var state=ConstructionPreviewState(12);var draft=new PlatformBuildDraft();
            draft.Begin(TileKind.Wood,2,0);draft.Visit(4,0);draft.Evaluate(map,state,state.Players[0]);
            var batch=new PlatformBuildBatch();batch.TryStart(draft.Cells);batch.Advance(map,state,state.Players[0]);
            var first=batch.Compose(default,false);batch.CancelUnsent();
            Require(batch.Pending && batch.Remaining==1 && batch.Cancelled==2,"Cancellation lost an in-flight request or kept unsubmitted tiles.");
            Require(batch.Compose(new HookInput(1000,0,fire:true),false).BuildId==first.BuildId && !batch.Compose(new HookInput(0,0,fire:true),false).Fire,"Closing build mode changed a committed transaction or fired a hook during it.");
            var receipt=state.Players[0];receipt.LastBuildId=first.BuildId;receipt.LastBuildResult=BuildResult.Success;
            Require(batch.Advance(map,state,receipt) && !batch.Pending,"Cancellation failed to drain the current receipt.");
            batch.TryStart(draft.Cells);
            var contested=new HookState(2,state.Players,state.Fish,0,state.Tiles.Concat(new[]{new PlatformTile(2,0,TileKind.Wood,1,4)}));
            batch.Advance(map,contested,receipt);
            Require(batch.Rejected==1 && batch.Compose(default,false).BuildX==3,"Contested tile was charged again or prevented building the next connected cell.");
            batch.Reset();Require(!batch.Pending && batch.Succeeded==0 && batch.Rejected==0 && batch.Cancelled==0,"New round retained a batch.");
        });
        yield return ("platform_drag_batch_save_restore_retains_committed_tiles_and_deduplicates_receipt",()=>{
            var map=LegacyPlatformMap();var model=FundedPlatform(12,0);var state=model.Capture();
            var draft=new PlatformBuildDraft();draft.Begin(TileKind.Wood,2,0);draft.Visit(4,0);draft.Evaluate(map,state,state.Players[0]);
            var batch=new PlatformBuildBatch();batch.TryStart(draft.Cells);batch.Advance(map,state,state.Players[0]);
            var input=batch.Compose(default,false);ShopStep(model,input.Encode());
            var restored=PullModel(map,1);var codec=new HookGame();restored.Restore(codec.Decode(codec.Encode(model.Capture())));
            ShopStep(restored,input.Encode());state=restored.Capture();
            Require(state.Tiles.Count==10 && state.Players[0].Wood==8,"Restore duplicated the settled build debit.");
            for(int i=0;i<8 && batch.Pending;i++){
                state=restored.Capture();batch.Advance(map,state,state.Players[0]);ShopStep(restored,batch.Compose(default,false).Encode());
            }
            state=restored.Capture();Require(!batch.Pending && state.Tiles.Count==12 && state.Players[0].Wood==0,"Restored confirmed receipts could not drain the batch safely.");
        });
    }
}

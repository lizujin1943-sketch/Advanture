using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using Advanture.HookIsland.Network;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IDeterministicSimulation<HookState> FundedPlatform(int wood=12,int bones=8,int count=1,IslandMap? map=null)
    {
        var model=PullModel(map??LegacyPlatformMap(),count);var s=model.Capture();var players=s.Players.ToArray();
        for(int i=0;i<players.Length;i++)
        {players[i].Wood=players[i].WoodEarned=wood;players[i].Fishbones=players[i].FishbonesEarned=bones;}
        // Construction fixtures keep mobile resources off the selected cells; overlap is tested separately.
        var woodObjects=s.Driftwood.Select(d=>DriftwoodMotion.Create(d.Id,300)).ToArray();
        model.Restore(new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,woodObjects));return model;
    }
    private static byte[] Build(TileKind kind,int x,int z,uint id=1)=>
        new HookInput(0,0,build:kind,buildX:(short)x,buildZ:(short)z,buildId:id).Encode();
    private static IEnumerable<(string,Action)> PlatformCases()
    {
        yield return ("platform_configured_rods_collect_real_fish_materials_and_build_both_tile_types",()=>{
            var map=IslandMap.CreatePlatform();var model=PullModel(map,4);
            for(int frame=0;frame<5000 && !PlatformSmokePilot.Complete(model.Capture());frame++)
            {
                var state=model.Capture();
                ShopStep(model,state.Players.Select(p=>PlatformSmokePilot.Sample(map,state,p.Slot,state.Steps+1).Encode()).ToArray());
            }
            var result=model.Capture();
            Require(PlatformSmokePilot.Complete(result),"Configured fishing/building loop failed: "+string.Join("; ",result.Players.Select(p=>$"P{p.Slot} wood={p.WoodEarned} bones={p.FishbonesEarned} catches={p.Catches} stamina={p.Stamina}")));
            Require(result.Players.All(p=>p.Catches>=1 && p.WoodEarned-p.Wood==4 && p.FishbonesEarned-p.Fishbones==4),"Construction did not use each collector's actual materials.");
        });
        yield return ("platform_acceptance_pilot_reaches_built_tiles_with_four_frames_of_input_delay",()=>{
            var map=LegacyPlatformMap();var model=FundedPlatform(4,4,4,map);
            var delayed=new Queue<byte[][]>();
            for(int i=0;i<4;i++)delayed.Enqueue(Enumerable.Repeat(default(HookInput).Encode(),4).ToArray());
            for(int frame=0;frame<1800 && !PlatformSmokePilot.Complete(model.Capture());frame++)
            {
                var state=model.Capture();
                delayed.Enqueue(state.Players.Select(p=>PlatformSmokePilot.Sample(map,state,p.Slot,state.Steps+5).Encode()).ToArray());
                ShopStep(model,delayed.Dequeue());
            }
            Require(PlatformSmokePilot.Complete(model.Capture()),"Delayed input made the acceptance pilot oscillate instead of reaching its tile: "+
                string.Join("; ",model.Capture().Players.Select(p=>$"P{p.Slot} ({p.X},{p.Z})")));
        });
        yield return ("platform_build_request_retries_until_authoritative_receipt_and_preserves_other_input",()=>{
            var pending=new PlatformBuildRequestBuffer();
            Require(pending.TryQueue(8,TileKind.Fishbone,-2,1),"Could not queue a valid construction request.");
            Require(!pending.TryQueue(8,TileKind.Wood,-3,1),"A second click overwrote an unacknowledged request.");
            var first=pending.Compose(default,false);var retry=pending.Compose(new HookInput(500,0),false);
            Require(first.BuildId==9 && retry.BuildId==9 && retry.BuildX==-2 && retry.BuildZ==1 && retry.MoveX==500,"A retry changed identity, position or movement.");
            Require(!pending.Observe(new Fisher{LastBuildId=8},out _),"Stale state acknowledged a future command.");
            Require(pending.Observe(new Fisher{LastBuildId=9,LastBuildResult=BuildResult.Occupied},out var result) && result==BuildResult.Occupied,"Failure receipt was not delivered.");
            Require(pending.Compose(default,false).Build==TileKind.None,"Failed requests continued being resent.");
            Require(!pending.TryQueue(uint.MaxValue,TileKind.Wood,2,0),"Request IDs wrapped to zero.");
            var other=new HookInput(0,0,rodAction:RodAction.Equip,rodTarget:1,rodRequestId:3);
            Require(pending.Compose(other,false).Encode().SequenceEqual(other.Encode()),"An idle construction adapter removed another subsystem's command.");
            pending.TryQueue(9,TileKind.Wood,2,0);
            Require(HookInput.Decode(pending.Compose(other,true).Encode()).BuildId==10,"Concurrent construction and equipment emitted an invalid combined packet.");
            pending.Reset();Require(!pending.Pending,"Leaving the room retained a pending debit.");
        });
        yield return ("platform_material_accounting_is_atomic_for_shortage_and_overflow",()=>{
            var player=new Fisher{Wood=3,WoodEarned=3,Fishbones=7,FishbonesEarned=7};
            Require(!PersonalMaterialInventory.TrySpend(ref player,TileKind.Wood,4) && player.Wood==3 && player.Fishbones==7,"Shortage partly changed an inventory.");
            player.WoodEarned=int.MaxValue;
            bool credited=PersonalMaterialInventory.Credit(ref player,TileKind.Wood,1);
            Require(!credited && player.Wood==3 && player.WoodEarned==int.MaxValue,"Overflow left a partial material credit.");
            var map=IslandMap.CreatePlatform();var state=Runner(1,map).InspectState();
            Require(PlatformRules.Check(map,state,state.Players[0],(TileKind)255,2,0)==BuildResult.Disabled,"Unknown tile kind reached material accounting.");
        });
        yield return ("platform_starts_with_nine_free_connected_wood_tiles",()=>{
            var map=LegacyPlatformMap();
            var state=Runner(4,map).InspectState();
            Require(state.Tiles.Count==9,"The initial platform must contain exactly nine free tiles.");
            Require(state.Players.Select(p=>(p.X,p.Z)).Distinct().Count()==4,"Platform spawns overlap.");
            Require(state.Tiles.All(t=>t.Kind==TileKind.Wood && t.Builder==-1 && t.Paid==0),"Initial tiles charged a builder.");
            Require(state.Players.All(p=>p.Wood==0 && p.Fishbones==0),"Unexpected free construction resources.");
        });
        yield return ("platform_build_uses_only_builders_corresponding_material_and_deduplicates",()=>{
            var model=FundedPlatform(count:2);
            ShopStep(model,Build(TileKind.Wood,2,0));
            var s=model.Capture();Require(s.Tiles.Count==10 && s.Players[0].Wood==8 && s.Players[0].Fishbones==8,"Wood recipe did not apply atomically.");
            Require(s.Players[1].Wood==12 && s.Coins==0,"Construction used someone else's materials or coins.");
            ShopStep(model,Build(TileKind.Fishbone,2,1,2));
            for(int i=0;i<5;i++)ShopStep(model,Build(TileKind.Fishbone,2,1,2));
            s=model.Capture();Require(s.Tiles.Count==11 && s.Players[0].Fishbones==4 && s.Players[0].Wood==8,"Bone recipe or receipt replay charged twice.");
        });
        yield return ("platform_rejects_diagonal_occupied_outside_and_insufficient_without_charging",()=>{
            var model=FundedPlatform(4,0);
            ShopStep(model,Build(TileKind.Wood,2,2));Require(model.Capture().Players[0].LastBuildResult==BuildResult.NotAdjacent,"Diagonal placement accepted.");
            ShopStep(model,Build(TileKind.Wood,0,0,2));Require(model.Capture().Players[0].LastBuildResult==BuildResult.Occupied,"Overlapping tile accepted.");
            ShopStep(model,Build(TileKind.Wood,-25,0,3));Require(model.Capture().Players[0].LastBuildResult==BuildResult.OutsideGrid,"Out-of-world placement accepted.");
            ShopStep(model,Build(TileKind.Fishbone,-2,0,4));Require(model.Capture().Players[0].LastBuildResult==BuildResult.InsufficientMaterials,"Wood was substituted for bones.");
            ShopStep(model,Build(TileKind.Wood,-2,0,5));ShopStep(model,Build(TileKind.Wood,-3,0,6));
            var s=model.Capture();Require(s.Tiles.Count==10 && s.Players[0].Wood==0 && s.Players[0].LastBuildResult==BuildResult.InsufficientMaterials,"Invalid placement charged or inventory went negative.");
        });
        yield return ("platform_simultaneous_same_cell_has_one_winner_and_failed_receipt_stays_failed",()=>{
            var model=FundedPlatform(4,4,4);var same=Build(TileKind.Wood,-2,0);
            ShopStep(model,same,same,same,same);var s=model.Capture();
            Require(s.Tiles.Count==10 && s.Tiles.Last().Builder==0,"Contested cell must resolve in slot order.");
            Require(s.Players[0].Wood==0 && s.Players.Skip(1).All(p=>p.Wood==4 && p.LastBuildResult==BuildResult.Occupied),"A losing player paid for the tile.");
            ShopStep(model,default(HookInput).Encode(),Build(TileKind.Wood,-3,0));
            Require(model.Capture().Tiles.Count==10,"Failed receipt was replayed with another position.");
        });
        yield return ("platform_shared_edges_and_bone_lattice_are_walkable_but_open_sea_is_not",()=>{
            var model=FundedPlatform();
            ShopStep(model,Build(TileKind.Wood,2,-1));ShopStep(model,Build(TileKind.Fishbone,3,-1,2));
            for(int i=0;i<110;i++)ShopStep(model,new HookInput(1000,0).Encode());
            var s=model.Capture();var p=s.Players[0];
            Require(p.X>6500 && p.X<=6800 && p.Z==-1400,"Player could not traverse both seams, fell through bones, or walked into sea.");
            Require(!PlatformRules.CanStand(s.Tiles,3000,-3000),"Concave platform corner should reject a partially unsupported footprint.");
        });
        yield return ("platform_config_prices_are_independent_and_saved_tiles_keep_historical_price",()=>{
            var map=LegacyPlatformMap(6,3);var model=FundedPlatform(12,8,map:map);
            ShopStep(model,Build(TileKind.Wood,-2,0));ShopStep(model,Build(TileKind.Fishbone,-2,1,2));
            var s=model.Capture();Require(s.Players[0].Wood==6 && s.Players[0].Fishbones==5,"Custom recipes were ignored.");
            var restored=PullModel(LegacyPlatformMap(2,7),1);restored.Restore(new HookGame().Decode(new HookGame().Encode(s)));
            ShopStep(restored,Build(TileKind.Wood,-3,0,3));
            var after=restored.Capture();Require(after.Players[0].Wood==4 && after.Tiles[9].Paid==6 && after.Tiles[10].Paid==3,"Price change retroactively changed old debits.");
        });
        yield return ("platform_blocks_building_footprints_and_visible_entities",()=>{
            var original=LegacyPlatformMap();
            var map=new IslandMap(original.Shore,original.Pier,original.Spawns,original.FishSpots,new PlatformConfig(4,4,new[]{new Point(2,0)}));
            var model=FundedPlatform(map:map);ShopStep(model,Build(TileKind.Wood,2,0));
            Require(model.Capture().Players[0].LastBuildResult==BuildResult.Occupied,"Reserved building footprint was overwritten.");
            var s=model.Capture();var fish=s.Fish.ToArray();fish[0].X=-4000;fish[0].Z=0;fish[0].Phase=FishPhase.Jumping;fish[0].Y=600;
            var occupied=new HookState(s.Steps,s.Players,fish,s.Coins,s.Tiles,s.Driftwood);
            Require(PlatformRules.Check(map,occupied,s.Players[0],TileKind.Wood,-2,0)==BuildResult.Occupied,"Visible fish entity was overwritten by placement preview.");
        });
        yield return ("platform_snapshot_rejects_disconnected_duplicate_or_free_paid_tiles",()=>{
            var model=FundedPlatform();ShopStep(model,Build(TileKind.Wood,-2,0));var s=model.Capture();
            foreach(var bad in new[]{new PlatformTile(-5,0,TileKind.Wood,0,4),new PlatformTile(0,0,TileKind.Wood,0,4),new PlatformTile(-2,0,TileKind.Wood,0,0)})
            {
                var tiles=s.Tiles.ToArray();tiles[9]=bad;bool rejected=false;
                try{new HookGame().Decode(new HookGame().Encode(new HookState(s.Steps,s.Players,s.Fish,s.Coins,tiles,s.Driftwood)));}catch(System.IO.InvalidDataException){rejected=true;}
                Require(rejected,"Malformed platform snapshot accepted.");
            }
        });
        yield return ("platform_floating_wood_awards_collector_once_only_after_reeling",()=>{
            var map=LegacyPlatformMap();var model=PullModel(map,1);bool attached=false;
            for(int i=0;i<220;i++)
            {
                var s=model.Capture();var p=s.Players[0];var d=s.Driftwood[0];
                ShopStep(model,new HookInput(0,0,d.X,d.Z,!attached && p.Phase==HookPhase.Ready).Encode());
                s=model.Capture();if(s.Players[0].DriftwoodId>=0){attached=true;Require(s.Players[0].Wood==0,"Wood paid before arriving on the platform.");}
            }
            var result=model.Capture();Require(attached && result.Players[0].Wood==4 && result.Players[0].WoodEarned==4 && result.Coins==0,"Floating wood reward duplicated, missing or credited as coins.");
        });
        yield return ("platform_input_validates_build_receipt_and_roundtrips_negative_cells",()=>{
            var c=HookInput.Decode(Build(TileKind.Fishbone,-2,-1,7));Require(c.BuildX==-2 && c.BuildZ==-1 && c.BuildId==7 && c.Build==TileKind.Fishbone,"Build command changed over wire.");
            foreach(var bytes in new[]{new byte[18],new HookInput(0,0).Encode()})
            {
                if(bytes.Length==HookInput.ByteCount)bytes[18]=1;
                bool rejected=false;try{HookInput.Decode(bytes);}catch(System.IO.InvalidDataException){rejected=true;}
                Require(rejected,"Old input or missing build receipt accepted.");
            }
            Require(PlatformRules.CellAt(-1001)==-1 && PlatformRules.CellAt(-999)==0 && PlatformRules.CellAt(-3001)==-2,"Negative grid snapping is asymmetric.");
        });
        yield return ("platform_four_players_earn_materials_expand_and_walk_onto_their_bone_tiles",()=>{
            var map=LegacyPlatformMap();var model=PullModel(map,4);int restoredAt=0;
            var restored=PullModel(map,4);var codec=new HookGame();
            for(int i=0;i<3300 && !PlatformSmokePilot.Complete(model.Capture());i++)
            {
                var s=model.Capture();var commands=s.Players.Select(p=>PlatformSmokePilot.Sample(map,s,p.Slot,s.Steps+1).Encode()).ToArray();
                ShopStep(model,commands);
                if(restoredAt==0 && model.Capture().Tiles.Count>9)
                {restored.Restore(codec.Decode(codec.Encode(model.Capture())));restoredAt=i+1;}
                else if(restoredAt>0)ShopStep(restored,commands);
            }
            var end=model.Capture();
            Require(PlatformSmokePilot.Complete(end),"Platform loop incomplete: "+string.Join("; ",end.Players.Select(p=>$"P{p.Slot} ({p.X},{p.Z}) wood={p.Wood}/{p.WoodEarned} bones={p.Fishbones}/{p.FishbonesEarned} build={p.LastBuildId}:{p.LastBuildResult} casts={p.Casts} catches={p.Catches}")));
            Require(end.Tiles.Count==17 && end.Players.Sum(p=>p.WoodEarned-p.Wood)==16 && end.Players.Sum(p=>p.FishbonesEarned-p.Fishbones)==16,"Earned resources were not conserved through four wood and four bone tiles.");
            Require(end.Players.All(p=>p.Catches>=2) && end.Coins==end.Players.Sum(p=>p.Coins),"Fish did not award both coins and personal bones.");
            Require(restoredAt>0 && codec.Encode(end).SequenceEqual(codec.Encode(restored.Capture())),"Platform snapshot replay diverged.");
        });
    }
}

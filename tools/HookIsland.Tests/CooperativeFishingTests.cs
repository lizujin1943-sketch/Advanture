using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    // Valid equipped swimmers let angle fixtures cover both sides of a fish without fake terrain rules.
    private static IDeterministicSimulation<HookState> CoopModel(int count=2,bool piranha=false,bool monster=false,CommissionConfig? commissions=null)
    {
        var basis=IslandMap.CreatePlatform();
        var config=new FishingConfig(enemies:piranha || monster?new EnemyConfig(piranhaCount:piranha?1:0,monsterEnabled:monster,patrolSpeed:30,initialDelayTicks:18000):EnemyConfig.Disabled,commissions:commissions);
        var map=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,config);
        var model=PullModel(map,count);var s=model.Capture();var players=s.Players.ToArray();
        var inventory=new InventorySystem(s.Tackle!.Inventory);
        for(int i=0;i<count;i++)
        {
            players[i].X=-2600;players[i].Z=i*700;players[i].Coins=players[i].CoinsEarned=10000;
            Require(inventory.Purchase(i,ItemKind.SwimRing,1,true,false,new Point(-2600,0),ref players[i].Coins)==InventoryResult.Success,"Ring fixture purchase failed.");
        }
        var fish=s.Fish.ToArray();var enemies=s.Enemies;
        players[0].Phase=HookPhase.Returning;players[0].HookX=-5200;players[0].HookZ=0;players[0].Distance=2600;
        if(piranha)
        {
            var f=enemies.Piranhas.ToArray();f[0].X=-5200;f[0].Z=0;f[0].Owner=0;
            players[0].HookedPiranha=1;
            enemies=new EnemyState(f,enemies.Monster,enemies.Waterballs);
        }
        else
        {
            fish[0].X=-5200;fish[0].Z=0;fish[0].Y=1250;fish[0].Owner=0;fish[0].Phase=FishPhase.Hooked;
            fish[0].Species=0;fish[0].WeightGrams=1000;fish[0].Grade=1;players[0].FishId=0;
        }
        var tackle=new TackleState(config,inventory.Capture(),s.Tackle.Stock,s.Tackle.Batch,s.Tackle.NextRestock);
        model.Restore(new HookState(s.Steps,players,fish,players.Sum(p=>p.Coins),s.Tiles,s.Driftwood,tackle,enemies,s.RoomRules,s.Treasure,s.Commissions));
        return model;
    }
    private static void CoopEdit(IDeterministicSimulation<HookState> model,Action<Fisher[]> edit,Action<Fish[]>? editFish=null)
    {
        var s=model.Capture();var players=s.Players.ToArray();var fish=s.Fish.ToArray();edit(players);editFish?.Invoke(fish);
        model.Restore(new HookState(s.Steps,players,fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions));
    }
    private static void CoopJoin(IDeterministicSimulation<HookState> model,int slot,Point position,int? stamina=null)
    {
        var s=model.Capture();var players=s.Players.ToArray();bool piranha=players[0].HookedPiranha>0;
        int x=piranha?s.Enemies.Piranhas[0].X:s.Fish[0].X,z=piranha?s.Enemies.Piranhas[0].Z:s.Fish[0].Z;
        ref var p=ref players[slot];p.X=position.X;p.Z=position.Z;p.Swimming.InWater=!PlatformRules.CanStand(s.Tiles,p.X,p.Z);
        p.Phase=HookPhase.Flying;p.HookX=x-150;p.HookZ=z;p.DirectionX=1000;p.DirectionZ=0;p.Distance=1000;
        if(stamina.HasValue)p.Stamina=stamina.Value;
        model.Restore(new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions));
        ShopStep(model);
    }
    private static IEnumerable<(string,Action)> CooperativeFishingCases()
    {
        yield return ("coop_held_catch_does_not_block_aimed_water_rescue",()=>{
            foreach(bool piranha in new[]{false,true})
            {
                var m=CoopModel(2,piranha);var s=m.Capture();var p=s.Players.ToArray();var fish=s.Fish.ToArray();var enemies=s.Enemies;
                p[0].X=p[0].HookX=0;p[0].Z=p[0].HookZ=-3200;p[0].Swimming.InWater=true;
                p[1].X=0;p[1].Z=-2400;
                if(piranha)
                {
                    var f=enemies.Piranhas.ToArray();f[0].X=0;f[0].Z=-3200;
                    enemies=new EnemyState(f,enemies.Monster,enemies.Waterballs);
                }
                else {fish[0].X=0;fish[0].Z=-3200;}
                int value=piranha?PiranhaFishing.Coins(s.Tackle!.Config.Enemies,enemies.Piranhas[0]):FishCatalog.Coins(fish[0]);
                m.Restore(new HookState(s.Steps,p,fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,enemies));
                ShopStep(m,default(HookInput).Encode(),new HookInput(0,0,0,-3200,true).Encode());
                for(int i=0;i<40;i++)ShopStep(m);
                var after=m.Capture();
                Require(after.Players[0].TimesHooked==1 && !after.Players[0].Swimming.InWater,"Held catch intercepted the hook aimed at its swimmer.");
                Require(after.Players[0].Catches==1 && after.Players[1].Catches==0 && after.Players[0].Coins==p[0].Coins+value && after.Coins==s.Coins+value,"Rescue changed catch ownership or awarded twice.");
            }
        });
        yield return ("coop_same_side_shares_one_fish",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-2600,700));var s=m.Capture();
            Require(s.Players.All(p=>p.FishId==0) && s.Fish[0].Owner==0,"Same-side hook did not join current lead.");
            Require(s.Players.All(p=>p.HookX==s.Fish[0].X && p.HookZ==s.Fish[0].Z),"Lines disagree on fish position.");
        });
        yield return ("coop_exact_right_angle_joins",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-5200,2000));
            Require(m.Capture().Players.All(p=>p.FishId==0) && m.Capture().Fish[0].Owner==0,"Exactly 90 degrees stole or rejected the fish.");
        });
        yield return ("coop_opposite_hook_atomically_steals",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-7000,1000));var s=m.Capture();
            Require(s.Players[0].FishId==-1 && s.Players[1].FishId==0 && s.Fish[0].Owner==1,"Reverse hook did not replace all old lines.");
            Require(!s.Fish[0].Escape.Active && s.Fish[0].Generation==0,"Handover escaped or respawned the fish.");
        });
        yield return ("coop_zero_stamina_cannot_steal",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-7000,1000),0);var s=m.Capture();
            Require(s.Players[0].FishId==0 && s.Players[1].FishId==-1 && s.Fish[0].Owner==0,"Zero stamina modified existing ownership.");
        });
        yield return ("coop_reverse_hook_cuts_all_three_old_lines",()=>{
            foreach(bool piranha in new[]{false,true})
            {
                var m=CoopModel(4,piranha);CoopJoin(m,1,new Point(-2600,700));CoopJoin(m,2,new Point(-2600,1400));CoopJoin(m,3,new Point(-7000,1000));var s=m.Capture();
                Require(s.Players.Take(3).All(p=>!CooperativeFishing.Attached(p)) && CooperativeFishing.Attached(s.Players[3]) && CooperativeFishing.Lead(s,s.Players[3])==3,"Stealing did not atomically remove all old lines.");
                Require(s.Players[3].FishingJoinOrder==0,"Stealing retained old join ranks.");
            }
        });
        yield return ("coop_overlong_incoming_line_cannot_cut_existing_hooks",()=>{
            foreach(bool piranha in new[]{false,true})
            {
                var m=CoopModel(2,piranha);CoopJoin(m,1,new Point(-9500,0));var s=m.Capture();
                Require(CooperativeFishing.Attached(s.Players[0]) && !CooperativeFishing.Attached(s.Players[1]) && CooperativeFishing.Lead(s,s.Players[0])==0,"Out-of-range contact cut a valid existing line before being rejected.");
                Require(piranha?!s.Enemies.Piranhas[0].Escape.Active:!s.Fish[0].Escape.Active,"Out-of-range hook made the target escape.");
            }
        });
        yield return ("coop_offline_lead_hands_off_without_restoring_line_on_rejoin",()=>{
            foreach(bool piranha in new[]{false,true})
            {
                var m=CoopModel(2,piranha);CoopJoin(m,1,new Point(-2600,700));var s=m.Capture();
                m.Step(new FrameContext(s.Steps+1,s.Steps+1,30,new[]{new MemberEvent(s.Steps+1,MemberEventKind.Deactivate,0,1)}),Array.Empty<SimulationInput>());s=m.Capture();
                Require(s.Players[0].Unavailable && !CooperativeFishing.Attached(s.Players[0]) && CooperativeFishing.Lead(s,s.Players[1])==1,"Offline lead retained a line or released the entire target.");
                m.Step(new FrameContext(s.Steps+1,s.Steps+1,30,new[]{new MemberEvent(s.Steps+1,MemberEventKind.Activate,0,2)}),Array.Empty<SimulationInput>());s=m.Capture();
                Require(!s.Players[0].Unavailable && !CooperativeFishing.Attached(s.Players[0]) && CooperativeFishing.Attached(s.Players[1]),"Rejoin silently recreated an old hook.");
            }
        });
        yield return ("coop_configured_speed_changes_map_and_snapshot_compatibility",()=>{
            var map=IslandMap.CreatePlatform();var custom=new IslandMap(map.Shore,map.Pier,map.Spawns,map.FishSpots,map.Platform,new FishingConfig(cooperativeSpeedBonusPermille:250));
            var restored=IslandMap.Decode(custom.Encode());Require(restored.Fishing!.CooperativeSpeedBonusPermille==250 && !map.Encode().SequenceEqual(custom.Encode()),"Cooperative speed absent from map identity.");
            var m=PullModel(custom,2);var s=m.Capture();Require(new HookGame().Decode(new HookGame().Encode(s)).Tackle!.Config.CooperativeSpeedBonusPermille==250,"Checkpoint lost configurable speed.");
        });
        yield return ("coop_two_players_reel_once_at_150_percent_and_split_drain",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-2600,700));var before=m.Capture();ShopStep(m);var after=m.Capture();
            Require(after.Fish[0].X-before.Fish[0].X==100 && after.Fish[0].Z==0,"Two lines did not reel once at 3 m/s.");
            for(int i=0;i<2;i++)Require(before.Players[i].Stamina-after.Players[i].Stamina==500,"Fish drain was not shared equally.");
        });
        yield return ("coop_piranha_accepts_assistant",()=>{
            var m=CoopModel(piranha:true);CoopJoin(m,1,new Point(-2600,700));var s=m.Capture();
            Require(s.Players.All(p=>p.HookedPiranha==1) && s.Enemies.Piranhas[0].Owner==0,"Piranha still has exclusive hook ownership.");
        });
        yield return ("coop_three_and_four_speed_and_exact_long_term_drain",()=>{
            foreach(int count in new[]{3,4})
            {
                var m=CoopModel(count);for(int i=1;i<count;i++)CoopJoin(m,i,new Point(-2600,i*700));
                CoopEdit(m,p=>p[0].ReelRemainder=0);var before=m.Capture();
                for(int i=0;i<3;i++)ShopStep(m);var after=m.Capture();
                Require(after.Fish[0].X-before.Fish[0].X==(count==3?400:500),"Speed was applied per player or rounded away.");
                for(int i=0;i<count;i++)Require(before.Players[i].Stamina-after.Players[i].Stamina==3000/count,"Shared drain lost fractional units.");
            }
        });
        yield return ("coop_assistant_exit_reselects_highest_current_stamina",()=>{
            var m=CoopModel(3);CoopJoin(m,1,new Point(-2600,700));CoopJoin(m,2,new Point(-2600,1400));
            CoopEdit(m,p=>{p[0].Stamina=300000;p[1].Stamina=600000;p[2].Stamina=200000;});
            ShopStep(m,default(HookInput).Encode(),default(HookInput).Encode(),new HookInput(0,0,cancel:true).Encode());
            var s=m.Capture();Require(s.Fish[0].Owner==1 && s.Players[0].FishId==0 && s.Players[2].FishId==-1,"Assistant exit failed to reselect lead.");
        });
        yield return ("coop_lead_tie_keeps_incumbent_then_earliest_join",()=>{
            var m=CoopModel(4);CoopJoin(m,2,new Point(-2600,1400));CoopJoin(m,1,new Point(-2600,700));CoopJoin(m,3,new Point(-2600,2100));
            CoopEdit(m,p=>{for(int i=0;i<4;i++)p[i].Stamina=900000;});
            ShopStep(m,default(HookInput).Encode(),default(HookInput).Encode(),default(HookInput).Encode(),new HookInput(0,0,cancel:true).Encode());
            Require(m.Capture().Fish[0].Owner==0,"Tied incumbent was replaced.");
            ShopStep(m,new HookInput(0,0,cancel:true).Encode());
            Require(m.Capture().Fish[0].Owner==2,"Tie ignored actual join order in favor of slot order.");
        });
        yield return ("coop_fish_motion_keeps_reverse_assistant_until_lead_changes",()=>{
            var m=CoopModel(3);CoopJoin(m,1,new Point(-5200,2000));CoopJoin(m,2,new Point(-2600,1400));
            CoopEdit(m,p=>{p[0].Stamina=300000;p[1].Stamina=900000;});
            for(int i=0;i<3;i++)ShopStep(m);
            Require(m.Capture().Players[1].FishId==0,"Fish movement continuously pruned an assistant by angle.");
            ShopStep(m,default(HookInput).Encode(),default(HookInput).Encode(),new HookInput(0,0,cancel:true).Encode());
            var s=m.Capture();Require(s.Fish[0].Owner==1 && s.Players[0].FishId==-1 && s.Players[1].FishId==0,"New lead did not remove reverse old line.");
        });
        yield return ("coop_exhaustion_removes_only_exhausted_members",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-2600,700));CoopEdit(m,p=>p[0].Stamina=500);ShopStep(m);
            var s=m.Capture();Require(s.Players[0].FishId==-1 && s.Players[1].FishId==0 && s.Fish[0].Owner==1 && !s.Fish[0].Escape.Active,"One exhausted line released entire fish.");
            Require(s.Players[0].Escapes==1,"Exhaustion feedback missing.");
        });
        yield return ("coop_simultaneous_exhaustion_escapes_once_without_reward",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-2600,700));CoopEdit(m,p=>{p[0].Stamina=p[1].Stamina=500;});var before=m.Capture();ShopStep(m);var s=m.Capture();
            Require(s.Players.All(p=>p.FishId==-1 && p.Escapes==1) && s.Fish[0].Escape.Active && s.Fish[0].Owner==-1,"Simultaneous exhaustion left an orphan group.");
            Require(s.Coins==before.Coins && s.Players.All(p=>p.Catches==0),"Exhausted group earned a reward.");
        });
        yield return ("coop_overlong_line_detaches_but_exact_length_is_valid",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-5200,2000));var s=m.Capture();
            CoopEdit(m,p=>{p[1].X=s.Fish[0].X-4000;p[1].Z=s.Fish[0].Z;p[1].Swimming.InWater=true;});
            ShopStep(m);Require(m.Capture().Players[1].FishId==-1 && m.Capture().Players[0].FishId==0,"Fish movement failed to detach overlong auxiliary line.");
            Require(!CooperativeFishing.IsOpposite(new Point(0,0),new Point(1000,0),new Point(0,1000)),"90 degree integer boundary broken.");
        });
        yield return ("coop_final_hooked_players_split_reward_remainder_to_lead",()=>{
            var m=CoopModel(3);CoopJoin(m,1,new Point(-2600,700));CoopJoin(m,2,new Point(-2600,1400));var before=m.Capture();
            for(int i=0;i<40 && m.Capture().Players[0].Catches==0;i++)ShopStep(m);var s=m.Capture();
            Require(s.Coins-before.Coins==4,"One fish was rewarded more than once.");
            Require(s.Players[0].Coins-before.Players[0].Coins==2 && s.Players[1].Coins-before.Players[1].Coins==1 && s.Players[2].Coins-before.Players[2].Coins==1,"Coin remainder not assigned to lead.");
            Require(s.Players[0].Fishbones==2 && s.Players[1].Fishbones==0 && s.Players[2].Fishbones==0,"Small integer bone reward did not keep remainder on lead.");
            Require(s.Players.All(p=>p.Catches==1 && p.FishId==-1),"Successful participant stats or cleanup invalid.");
            var codec=new HookGame();m.Restore(codec.Decode(codec.Encode(s)));for(int i=0;i<40;i++)ShopStep(m);
            Require(m.Capture().Coins==s.Coins,"Restore replayed reward.");
        });
        yield return ("coop_detached_contributor_gets_no_reward",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-2600,700));var before=m.Capture();
            ShopStep(m,default(HookInput).Encode(),new HookInput(0,0,cancel:true).Encode());
            for(int i=0;i<50 && m.Capture().Players[0].Catches==0;i++)ShopStep(m);var s=m.Capture();
            Require(s.Players[0].Coins-before.Players[0].Coins==4 && s.Players[1].Coins==before.Players[1].Coins && s.Players[1].Catches==0,"Former contributor retained a reward claim.");
        });
        yield return ("coop_material_failure_rolls_back_all_participants",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-2600,700));var before=m.Capture();var tackle=before.Tackle!;
            // Exactly one allocation remains: the lead could receive bones, but the assistant cannot.
            var inventory=new InventoryState(2,tackle.Inventory.Items,int.MaxValue-1,tackle.Inventory.SpentCoins);
            m.Restore(new HookState(before.Steps,before.Players,before.Fish,before.Coins,before.Tiles,before.Driftwood,
                new TackleState(tackle.Config,inventory,tackle.Stock,tackle.Batch,tackle.NextRestock),before.Enemies));
            for(int i=0;i<40;i++)ShopStep(m);var s=m.Capture();
            Require(s.Coins==before.Coins && s.Players.All(p=>p.Catches==0 && p.FishbonesEarned==0 && p.FishId==0),"Failed group credit partially paid coins or statistics.");
            Require(s.Tackle!.Inventory.NextId==int.MaxValue-1 && s.Tackle.Inventory.Items.Count==inventory.Items.Count && s.Fish[0].Generation==0,"Failed group credit consumed IDs, inventory or fish.");
        });
        yield return ("coop_water_assistant_overflow_lands_on_capture_platform",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-4500,1000));var before=m.Capture();var tackle=before.Tackle!;
            var inventory=new InventorySystem(tackle.Inventory);var size=tackle.Inventory.Size(1);int wood=size.Width*size.Height*InventoryCatalog.MaterialStackLimit;
            Require(inventory.CreditMaterial(1,ItemKind.Wood,wood,new Point(0,0))==wood,"Could not fill assistant backpack.");
            var players=before.Players.ToArray();players[1].Wood=players[1].WoodEarned=wood;
            m.Restore(new HookState(before.Steps,players,before.Fish,before.Coins,before.Tiles,before.Driftwood,
                new TackleState(tackle.Config,inventory.Capture(),tackle.Stock,tackle.Batch,tackle.NextRestock),before.Enemies));
            for(int i=0;i<40 && m.Capture().Players[0].Catches==0;i++)ShopStep(m);var s=m.Capture();
            var ground=s.Tackle!.Inventory.Items.Where(i=>i.Kind==ItemKind.Fishbone && i.Location==ItemLocation.Ground).ToArray();
            Require(s.Players.All(p=>p.Catches==1) && s.Players[1].Swimming.InWater && s.Players[1].FishbonesEarned==1 && s.Players[1].Fishbones==0,"Water assistant did not receive their harvest share.");
            Require(ground.Length==1 && ground[0].Count==1 && PlatformRules.CanStand(s.Tiles,ground[0].WorldX,ground[0].WorldZ),"Overflow material was dropped offshore or duplicated.");
            new HookGame().Decode(new HookGame().Encode(s));
        });
        yield return ("coop_shore_captures_before_reaching_player",()=>{
            var m=CoopModel(1);CoopEdit(m,p=>{p[0].X=0;p[0].Distance=2800;p[0].HookX=-2800;},f=>f[0].X=-2800);
            for(int i=0;i<5;i++)ShopStep(m);var s=m.Capture();
            Require(s.Players[0].Catches==1 && Math.Abs(s.Players[0].HookX-s.Players[0].X)>1000,"Shore arrival still waits for player's body.");
        });
        yield return ("coop_checkpoint_keeps_join_order_and_fractional_drain",()=>{
            var a=CoopModel(3);CoopJoin(a,2,new Point(-2600,1400));CoopJoin(a,1,new Point(-2600,700));ShopStep(a);
            var b=CoopModel(3);var codec=new HookGame();b.Restore(codec.Decode(codec.Encode(a.Capture())));
            for(int i=0;i<90;i++)
            {
                var command=i==1?new HookInput(0,0,cancel:true).Encode():default(HookInput).Encode();ShopStep(a,command);ShopStep(b,command);
                Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Restored cooperation diverged at "+i);
            }
        });
        yield return ("coop_codec_rejects_duplicate_rank_or_target_position",()=>{
            var m=CoopModel();CoopJoin(m,1,new Point(-2600,700));var s=m.Capture();
            for(int n=0;n<3;n++)
            {
                var players=s.Players.ToArray();if(n==0)players[1].FishingJoinOrder=0;if(n==1)players[1].HookX++;if(n==2)players[1].FishingDrainRemainder=12;
                bool rejected=false;try{new HookGame().Decode(new HookGame().Encode(new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies)));}catch(InvalidDataException){rejected=true;}
                Require(rejected,"Malformed cooperation checkpoint accepted: "+n);
            }
        });
        yield return ("coop_waterball_immediately_detaches_hit_player",()=>{
            var m=CoopModel(monster:true);CoopJoin(m,1,new Point(-2600,700));var s=m.Capture();var e=s.Enemies;
            var ball=new Waterball{Id=1,OriginX=-3500,OriginZ=0,X=-3234,Z=0,DirectionX=1000000,Distance=266,TravelRemainder=20,KnockbackDistance=4000};
            m.Restore(new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,new EnemyState(e.Piranhas,e.Monster,new[]{ball},1,0,1)));
            ShopStep(m);s=m.Capture();
            Require(s.Players[0].Knockback.Active && s.Players[0].FishId==-1 && s.Players[1].FishId==0 && s.Fish[0].Owner==1,"Waterball knockback kept hit player's line.");
        });
        yield return ("coop_piranha_keeps_hold_until_last_line_then_captures_once",()=>{
            var m=CoopModel(piranha:true);CoopJoin(m,1,new Point(-2600,700));ShopStep(m,new HookInput(0,0,cancel:true).Encode());var before=m.Capture();
            Require(before.Players[1].HookedPiranha==1 && before.Enemies.Piranhas[0].Owner==1 && !before.Enemies.Piranhas[0].Escape.Active,"Piranha resumed after one line exited.");
            for(int i=0;i<80 && m.Capture().Enemies.Captures==0;i++)ShopStep(m);var s=m.Capture();
            Require(s.Enemies.Captures==1 && s.Enemies.Piranhas[0].RespawnTicks>0 && s.Players[0].Catches==0 && s.Players[1].Catches==1,"Piranha captured multiple times or awarded former lead.");
            Require(s.Enemies.Bites==0,"Held piranha bit a player.");
        });
    }
}

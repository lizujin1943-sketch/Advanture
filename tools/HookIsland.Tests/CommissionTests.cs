using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IDeterministicSimulation<HookState> CommissionModel(int count=1,CommissionConfig? commissions=null,bool piranha=false)
    {
        var basis=IslandMap.CreatePlatform();
        var config=new FishingConfig(commissions:commissions??new CommissionConfig(),
            enemies:piranha?new EnemyConfig(piranhaCount:1,monsterEnabled:false,initialDelayTicks:18000):EnemyConfig.Disabled);
        return PullModel(new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,config),count);
    }
    private static void CommissionHold(IDeterministicSimulation<HookState> model,int species=0,int grade=1,bool piranha=false)
    {
        var s=model.Capture();var players=s.Players.ToArray();var fish=s.Fish.ToArray();var enemies=s.Enemies;
        for(int i=0;i<players.Length;i++)
        {
            ref var p=ref players[i];p.X=-2400;p.Z=i*300;p.HookX=-3200;p.HookZ=0;
            p.Phase=HookPhase.Returning;p.FishId=piranha?-1:0;p.HookedPiranha=piranha?1:0;
            p.FishingJoinOrder=i;p.FishingDrainRemainder=0;p.Stamina=FishingRules.MaxStamina(s,p);
        }
        if(piranha)
        {
            var p=enemies.Piranhas.ToArray();p[0].X=-3200;p[0].Z=0;p[0].Owner=0;p[0].RespawnTicks=0;
            enemies=new EnemyState(p,enemies.Monster,enemies.Waterballs,enemies.Serial,enemies.Bites,enemies.Shots,enemies.Interruptions,enemies.WaterHits,enemies.Captures);
        }
        else
        {ref var f=ref fish[0];f.X=-3200;f.Z=0;f.Y=1250;f.Owner=0;f.Phase=FishPhase.Hooked;f.Species=species;f.Grade=grade;f.WeightGrams=1000;}
        model.Restore(new HookState(s.Steps,players,fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean));
    }
    private static HookState CommissionCatch(IDeterministicSimulation<HookState> model,int species=0,int grade=1,bool piranha=false)
    {
        int before=model.Capture().Players[0].Catches;CommissionHold(model,species,grade,piranha);
        for(int i=0;i<30 && model.Capture().Players[0].Catches==before;i++)ShopStep(model);
        var result=model.Capture();Require(result.Players[0].Catches==before+1,"Capture fixture did not settle a real fish.");return result;
    }
    private static IEnumerable<(string,Action)> CommissionCases()
    {
        yield return ("commission_successful_capture_advances_shared_target",()=>{
            var m=CommissionModel();var s=CommissionCatch(m);
            Require(s.Commissions.Entries[0].Fish==1 && s.Commissions.Bait==0,"Successful capture did not advance the active commission once.");
        });
        yield return ("commission_cooperative_capture_counts_target_once",()=>{
            var m=CommissionModel(4,new CommissionConfig(new[]{new CommissionDefinition(1,1)}));var s=CommissionCatch(m);
            Require(s.Players.All(p=>p.Catches==1),"All four anglers must share the successful capture.");
            Require(s.Commissions.Entries[0].Fish==1 && s.Commissions.Completed==1 && s.Commissions.Bait==1,"Cooperative capture did not award one shared completion.");
        });
        yield return ("commission_rare_species_completes_alternative_without_spending_fish_income",()=>{
            var m=CommissionModel(commissions:new CommissionConfig(new[]{new CommissionDefinition(6,1)}));var s=CommissionCatch(m,5);
            Require(s.Commissions.Completed==1 && s.Commissions.Bait==1 && s.Commissions.Entries[0].Rare==1,"Rare species did not satisfy the alternative target.");
            Require(s.Players[0].Coins==25 && s.Players[0].Fishbones==2,"Commission consumed normal fishing income.");
        });
        yield return ("commission_three_sequential_targets_do_not_roll_over_or_repeat_bait",()=>{
            var m=CommissionModel(commissions:new CommissionConfig(new[]{new CommissionDefinition(1,1,2),new CommissionDefinition(2,1,3),new CommissionDefinition(1,1,4)}));
            var s=CommissionCatch(m);Require(s.Commissions.Completed==1 && s.Commissions.Bait==2 && s.Commissions.Entries[1].Fish==0,"Completing fish rolled over into next target.");
            s=CommissionCatch(m);Require(s.Commissions.Completed==1 && s.Commissions.Entries[1].Fish==1,"Second target did not track new captures.");
            s=CommissionCatch(m);Require(s.Commissions.Completed==2 && s.Commissions.Bait==5 && s.Commissions.Entries[2].Fish==0,"Second reward repeated or missing.");
            s=CommissionCatch(m);Require(s.Commissions.Completed==3 && s.Commissions.Bait==9,"Three commissions did not complete in order.");
            s=CommissionCatch(m,9);Require(s.Commissions.Bait==9 && s.Commissions.Entries[2].Fish==1,"Finished commissions awarded extra bait.");
        });
        yield return ("commission_elite_common_fish_is_not_rare",()=>{
            var s=CommissionCatch(CommissionModel(),0,3);
            Require(s.Commissions.Entries[0].Fish==1 && s.Commissions.Entries[0].Rare==0,"Boss grade incorrectly changed species rarity.");
        });
        yield return ("commission_piranha_cooperative_capture_counts_once_as_common",()=>{
            var s=CommissionCatch(CommissionModel(2,piranha:true),piranha:true);
            Require(s.Enemies.Captures==1 && s.Players.All(p=>p.Catches==1),"Piranha capture did not settle normally.");
            Require(s.Commissions.Entries[0].Fish==1 && s.Commissions.Entries[0].Rare==0,"Piranha missing or counted per angler.");
        });
        yield return ("commission_failed_wallet_transaction_does_not_advance",()=>{
            var m=CommissionModel(2);CommissionHold(m);var s=m.Capture();var p=s.Players.ToArray();p[1].Coins=p[1].CoinsEarned=int.MaxValue;
            m.Restore(new HookState(s.Steps,p,s.Fish,int.MaxValue,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions));
            for(int i=0;i<20;i++)ShopStep(m);s=m.Capture();
            Require(s.Players.All(x=>x.Catches==0) && s.Commissions.Entries.All(e=>e.Fish==0) && s.Commissions.Bait==0,"Failed reward transaction advanced commission.");
        });
        yield return ("commission_failed_material_transaction_does_not_commit_prepared_progress",()=>{
            var m=CommissionModel(2,commissions:new CommissionConfig(new[]{new CommissionDefinition(1,1)}));CommissionHold(m);
            var before=m.Capture();var tackle=before.Tackle!;
            var inventory=new InventoryState(2,tackle.Inventory.Items,int.MaxValue-1,tackle.Inventory.SpentCoins);
            m.Restore(new HookState(before.Steps,before.Players,before.Fish,before.Coins,before.Tiles,before.Driftwood,
                new TackleState(tackle.Config,inventory,tackle.Stock,tackle.Batch,tackle.NextRestock),before.Enemies,before.RoomRules,before.Treasure,before.Commissions));
            for(int i=0;i<20;i++)ShopStep(m);var s=m.Capture();
            Require(s.Players.All(p=>p.Catches==0) && s.Commissions.Bait==0 && s.Commissions.Entries[0].Fish==0 && s.Fish[0].Generation==0,"Prepared commission committed before failed material credit.");
        });
        yield return ("commission_two_distinct_targets_in_one_frame_advance_in_order",()=>{
            var m=CommissionModel(2,new CommissionConfig(new[]{new CommissionDefinition(1,1),new CommissionDefinition(1,1),new CommissionDefinition(1,1)}));CommissionHold(m);
            var s=m.Capture();var p=s.Players.ToArray();var f=s.Fish.ToArray();
            for(int i=0;i<2;i++)
            {p[i].FishId=i;p[i].FishingJoinOrder=0;p[i].HookX=-2400;p[i].HookZ=i*300;f[i]=f[0];f[i].Id=i;f[i].Owner=i;f[i].X=-2400;f[i].Z=i*300;}
            m.Restore(new HookState(s.Steps,p,f,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions));
            ShopStep(m);s=m.Capture();
            Require(s.Players.All(x=>x.Catches==1) && s.Commissions.Completed==2 && s.Commissions.Bait==2 && s.Commissions.Entries[2].Fish==0,"Distinct same-frame catches were dropped or counted twice.");
        });
        yield return ("commission_reverse_steal_counts_only_successful_final_target",()=>{
            var m=CoopModel(commissions:new CommissionConfig());CoopJoin(m,1,new Point(-6500,0));
            Require(m.Capture().Fish[0].Owner==1 && m.Capture().Players[0].FishId<0,"Reverse-steal fixture did not transfer ownership.");
            CoopEdit(m,p=>{p[1].X=-2400;p[1].Z=0;p[1].Swimming.InWater=false;p[1].HookX=-3200;p[1].HookZ=0;},f=>{f[0].X=-3200;f[0].Z=0;});
            for(int i=0;i<20;i++)ShopStep(m);var s=m.Capture();
            Require(s.Players[0].Catches==0 && s.Players[1].Catches==1 && s.Commissions.Entries[0].Fish==1,"Stealing or old membership created extra contribution.");
        });
        yield return ("commission_cancelled_catch_never_contributes",()=>{
            var m=CommissionModel();CommissionHold(m);ShopStep(m,new HookInput(0,0,cancel:true).Encode());
            for(int i=0;i<100;i++)ShopStep(m);var s=m.Capture();
            Require(s.Players[0].Catches==0 && s.Commissions.Entries[0].Fish==0 && s.Commissions.Bait==0,"Escape/cancel counted as capture.");
        });
        yield return ("commission_restore_preserves_completed_reward_and_replays_next_capture",()=>{
            var config=new CommissionConfig(new[]{new CommissionDefinition(1,1),new CommissionDefinition(2,1)});
            var m=CommissionModel(commissions:config);var captured=CommissionCatch(m);var game=new HookGame();
            var restored=CommissionModel(commissions:config);restored.Restore(game.Decode(game.Encode(captured)));
            var a=CommissionCatch(m);var b=CommissionCatch(restored);
            Require(game.Encode(a).SequenceEqual(game.Encode(b)) && b.Commissions.Bait==1 && b.Commissions.Entries[1].Fish==1,"Restore lost progress or awarded bait twice.");
            var fresh=CommissionModel(commissions:config).Capture();
            Require(fresh.Commissions.Completed==0 && fresh.Commissions.Bait==0 && fresh.Commissions.Entries.All(e=>e.Fish==0),"New round retained old commissions.");
        });
        yield return ("commission_absent_config_stays_disabled",()=>{
            var m=CommissionModel(commissions:CommissionConfig.Disabled);var s=CommissionCatch(m);
            Require(!s.Tackle!.Config.Commissions.Enabled && s.Commissions.Entries.Count==0 && s.Commissions.Bait==0,"Disabled commissions progressed.");
            var basis=IslandMap.CreatePlatform();Require(!basis.Fishing!.Commissions.Enabled,"Missing configuration enabled quests implicitly.");
        });
        yield return ("commission_codec_rejects_unearned_bait_future_progress_and_invalid_counts",()=>{
            var s=CommissionModel().Capture();var game=new HookGame();
            var invalid=new[]{
                new CommissionState(0,1,new CommissionProgress[3]),
                new CommissionState(1,1,new CommissionProgress[3]),
                new CommissionState(-1,0,new CommissionProgress[3]),
                new CommissionState(4,4,new CommissionProgress[3]),
                new CommissionState(0,0,new[]{new CommissionProgress(0,1),default,default}),
                new CommissionState(0,0,new[]{new CommissionProgress(-1,0),default,default}),
                new CommissionState(0,0,new[]{new CommissionProgress(7,0),default,default}),
                new CommissionState(0,0,new[]{default(CommissionProgress),new CommissionProgress(1,0),default}),
                new CommissionState(1,1,new[]{new CommissionProgress(6,0),default,default})
            };
            foreach(var progress in invalid)
            {
                bool rejected=false;
                try {game.Decode(game.Encode(new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,progress)));}
                catch(InvalidDataException){rejected=true;}
                Require(rejected,"Corrupt commission checkpoint was accepted.");
            }
        });
        yield return ("commission_invalid_config_and_mismatched_restore_rejected",()=>{
            var invalid=new Action[]{()=>new CommissionDefinition(0,1),()=>new CommissionDefinition(1,1001),()=>new CommissionDefinition(1,1,0),
                ()=>new CommissionConfig(Array.Empty<CommissionDefinition>()),()=>new CommissionConfig(new CommissionDefinition[17]),
                ()=>new CommissionConfig(new[]{default(CommissionDefinition)}),()=>new CommissionConfig(new[]{new CommissionDefinition(1,1)},false)};
            foreach(var create in invalid){bool rejected=false;try{create();}catch(InvalidDataException){rejected=true;}Require(rejected,"Invalid commission configuration accepted.");}
            var a=CommissionModel();var b=CommissionModel(commissions:new CommissionConfig(new[]{new CommissionDefinition(1,1)}));
            bool incompatible=false;try{a.Restore(b.Capture());}catch(InvalidDataException){incompatible=true;}
            Require(incompatible,"Snapshot with different commission rules restored.");
        });
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IslandMap OceanMap(OceanConfig? ocean=null,EnemyConfig? enemies=null)
    {
        var basis=IslandMap.CreatePlatform();
        var config=new FishingConfig(ocean:ocean??new OceanConfig(new[]{0,1,2}),enemies:enemies,
            commissions:new CommissionConfig(new[]{new CommissionDefinition(1,1),new CommissionDefinition(1,1),new CommissionDefinition(10,4)}));
        return OceanRules.CreateMap(new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots.Take(3),basis.Platform),config);
    }
    private static IEnumerable<(string,Action)> OceanCases()
    {
        yield return ("ocean_unlocks_by_commissions_not_time",()=>{
            var m=PullModel(OceanMap(),1);for(int i=0;i<400;i++)ShopStep(m);
            var s=m.Capture();
            Require(s.Fish[1].Phase==FishPhase.Waiting && s.Fish[2].Phase==FishPhase.Waiting,"Time unlocked advanced fish before commission completion.");
            Require(s.Fish[0].Phase!=FishPhase.Waiting && s.Fish[0].Grade==1 && s.Fish[0].WeightGrams<=3000,"Opening light fish missing.");
            CommissionCatch(m);for(int i=0;i<10;i++)ShopStep(m);s=m.Capture();
            Require(s.Commissions.Completed==1 && s.Fish[1].Phase!=FishPhase.Waiting && s.Fish[2].Phase==FishPhase.Waiting,"First commission did not unlock only the middle band.");
            CommissionCatch(m);for(int i=0;i<10;i++)ShopStep(m);s=m.Capture();
            Require(s.Commissions.Completed==2 && s.Fish[2].Phase!=FishPhase.Waiting && FishCatalog.All[s.Fish[2].Species].Rarity>=FishRarity.Rare,"Second commission did not open rare fish.");
        });
        yield return ("ocean_stall_spawns_real_fish_without_rewards",()=>{
            var m=PullModel(OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:60)),1);
            for(int i=0;i<60;i++)ShopStep(m);var s=m.Capture();
            Require(s.Ocean.QuietUntil>s.Steps && s.Ocean.Visits.Count(v=>v.Active)>0,"Stalled team received no recovery window or real fish.");
            Require(s.Coins==0 && s.Players.All(p=>p.Catches==0) && s.Commissions.Completed==0,"Recovery injected rewards or progress.");
            Require(s.Fish.Skip(3).Any(f=>f.Phase==FishPhase.Swimming),"Recovery opportunity did not enter real fish motion.");
        });
        yield return ("ocean_recovery_preserves_windup_and_projectiles",()=>{
            var players=PullModel(OceanMap(),1).Capture().Players;
            var config=new EnemyConfig(piranhaCount:0,initialDelayTicks:0,windupTicks:2,waterballSpeed:1000);
            var enemies=new EnemySystem(config);
            enemies.Advance(players,_=>{});Require(enemies.Monster.Phase==MonsterPhase.Windup,"Fixture did not start a windup.");
            enemies.Advance(players,_=>{},allowNewAttacks:false);enemies.Advance(players,_=>{},allowNewAttacks:false);
            var fired=enemies.Capture();Require(fired.Shots==1 && fired.Waterballs.Count==1,"Recovery cancelled existing windup or shot.");
            int cooldown=enemies.Monster.Timer;
            enemies.Advance(players,_=>{},allowNewAttacks:false);
            Require(enemies.Monster.Timer==cooldown && enemies.Capture().Waterballs[0].Distance>fired.Waterballs[0].Distance,"Cooldown did not pause independently of the in-flight projectile.");
            enemies.Advance(players,_=>{});Require(enemies.Monster.Timer==cooldown-1,"Cooldown did not resume from its remaining time.");
        });
        yield return ("ocean_same_frame_success_cancels_stall",()=>{
            var control=PullModel(OceanMap(),1);int captureTick=(int)CommissionCatch(control).Steps;
            var m=PullModel(OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:captureTick)),1);
            var s=CommissionCatch(m);
            Require(s.Ocean.LastCatchTick==s.Steps && s.Ocean.LastEvent!=OceanEvent.Recovery,"Successful capture was ignored by the same-frame stall check.");
        });
        yield return ("ocean_failed_capture_does_not_reset_stall",()=>{
            var m=PullModel(OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:10)),1);CommissionHold(m);
            var s=m.Capture();var p=s.Players.ToArray();p[0].Coins=p[0].CoinsEarned=int.MaxValue;
            m.Restore(OceanCopy(s,players:p));for(int i=0;i<10;i++)ShopStep(m);s=m.Capture();
            Require(s.Players[0].Catches==0 && s.Ocean.LastCatchTick==0 && s.Ocean.LastEvent==OceanEvent.Recovery,"Failed wallet transaction counted as successful fishing.");
            Require(s.Fish[0].Owner==0,"Failed settlement removed the original catch.");
        });
        yield return ("ocean_piranha_capture_resets_stall_once",()=>{
            var m=PullModel(OceanMap(enemies:new EnemyConfig(piranhaCount:1,monsterEnabled:false)),2);
            for(int i=0;i<100;i++)ShopStep(m);var s=CommissionCatch(m,piranha:true);
            Require(s.Ocean.LastCatchTick==s.Steps && s.Commissions.Completed==1 && s.Enemies.Captures==1,"Shared piranha catch did not update team progress exactly once.");
        });
        yield return ("ocean_pressure_uses_attacks_window_and_interval",()=>{
            var map=OceanMap(new OceanConfig(new[]{0,1,2},pressureWindowTicks:10,pressureCount:3,recoveryTicks:5,recoveryCooldownTicks:20));
            var s=PullModel(map,1).Capture();var system=new OceanSystem(map.Fishing!.Ocean);var f=s.Fish.ToArray();
            system.Advance(map,s.Tiles,s.Players,f,0,OceanAttacks(1,0),1);
            system.Advance(map,s.Tiles,s.Players,f,0,OceanAttacks(1,1),9);
            Require(system.Capture().Sequence==0,"One shot and one interruption prematurely triggered recovery.");
            system.Advance(map,s.Tiles,s.Players,f,0,OceanAttacks(2,1),10);
            Require(system.Capture().QuietUntil==15 && system.Capture().ReliefReadyTick==30,"Three handled attacks did not start one recovery.");
            system.Advance(map,s.Tiles,s.Players,f,0,OceanAttacks(2,1),11);
            Require(system.Capture().Sequence==1 && system.Capture().ThreatCount==0,"Re-reading counters counted the same attack twice.");
            system.Advance(map,s.Tiles,s.Players,f,0,OceanAttacks(5,1),29);
            Require(system.Capture().Sequence==1,"Recovery ignored its minimum interval.");
            system.Advance(map,s.Tiles,s.Players,f,0,OceanAttacks(5,1),30);
            Require(system.Capture().Sequence==2,"Pending recent pressure was lost at the interval boundary.");
        });
        yield return ("ocean_pressure_window_excludes_expired_attacks",()=>{
            var map=OceanMap(new OceanConfig(new[]{0,1,2},pressureWindowTicks:10,pressureCount:2));
            var s=PullModel(map,1).Capture();var system=new OceanSystem(map.Fishing!.Ocean);var f=s.Fish.ToArray();
            system.Advance(map,s.Tiles,s.Players,f,0,OceanAttacks(1,0),1);
            system.Advance(map,s.Tiles,s.Players,f,0,OceanAttacks(2,0),11);
            Require(system.Capture().Sequence==0 && system.Capture().ThreatCount==1,"Attack on the window boundary included an expired attack.");
        });
        yield return ("ocean_optional_pause_leaves_monster_schedule_running",()=>{
            var map=OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1,pauseMonster:false));var s=PullModel(map,1).Capture();
            var system=new OceanSystem(map.Fishing!.Ocean);var f=s.Fish.ToArray();system.Advance(map,s.Tiles,s.Players,f,0,s.Enemies,1);
            Require(system.Capture().Visits.Any(v=>v.Active) && system.AllowNewAttacks(2),"Disabling the pause removed real fish or still paused attacks.");
        });
        yield return ("ocean_quiet_interruption_keeps_configured_cooldown",()=>{
            var players=PullModel(OceanMap(),1).Capture().Players;
            var e=new EnemySystem(new EnemyConfig(piranhaCount:0,initialDelayTicks:0,interruptDelayTicks:7));
            e.Advance(players,_=>{});Require(e.Interrupt(),"Fixture missed active windup.");
            e.Advance(players,_=>{},allowNewAttacks:false);
            Require(e.Monster.Timer==7 && e.Capture().Shots==0,"Quiet interruption retained the synthetic extra frame or fired a shot.");
            e.Advance(players,_=>{},allowNewAttacks:false);Require(e.Monster.Timer==7,"Interruption cooldown advanced during quiet.");
        });
        yield return ("ocean_visit_coop_expiry_waits_for_atomic_capture",()=>{
            var m=PullModel(OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1,visitTicks:2)),2);ShopStep(m);OceanHoldVisit(m);
            var before=m.Capture();int reward=FishCatalog.Coins(before.Fish[3]),bones=FishCatalog.Bones(before.Fish[3]);
            for(int i=0;i<12;i++)ShopStep(m);var s=m.Capture();
            Require(s.Players.All(p=>p.Catches==1) && s.Coins==reward && s.Players.Sum(p=>p.Fishbones)==bones,"Expiring shared fish was swallowed or paid per line.");
            Require(s.Commissions.Completed==1 && !s.Ocean.Visits[0].Active && s.Fish[3].Phase==FishPhase.Waiting,"Visit catch did not consume one slot and one commission capture.");
            Require(s.Players[0].Coins==reward/2+reward%2 && s.Players[1].Coins==reward/2,"Visit fish did not use final-line reward sharing.");
        });
        yield return ("ocean_failed_visit_capture_keeps_line_and_slot",()=>{
            var m=PullModel(OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1,visitTicks:2)),1);ShopStep(m);OceanHoldVisit(m);
            var s=m.Capture();var p=s.Players.ToArray();p[0].Coins=p[0].CoinsEarned=int.MaxValue;m.Restore(OceanCopy(s,players:p));
            for(int i=0;i<15;i++)ShopStep(m);s=m.Capture();
            Require(s.Ocean.Visits[0].Active && s.Fish[3].Owner==0 && s.Players[0].Catches==0 && s.Ocean.LastCatchTick==0,"Failed visit settlement removed the expired held fish or reset stall.");
            _=new HookGame().Decode(new HookGame().Encode(s));
        });
        yield return ("ocean_expired_held_visit_can_be_stolen_without_respawn",()=>{
            var m=PullModel(OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1,visitTicks:1)),2);ShopStep(m);
            var s=m.Capture();var p=s.Players.ToArray();var f=s.Fish.ToArray();var inventory=new InventorySystem(s.Tackle!.Inventory);
            p[1].Coins=p[1].CoinsEarned=10000;
            Require(inventory.Purchase(1,ItemKind.SwimRing,1,true,false,new Point(-2600,0),ref p[1].Coins)==InventoryResult.Success,"Ring setup failed.");
            p[0].X=-2400;p[0].Z=0;p[0].HookX=-5000;p[0].HookZ=0;p[0].Phase=HookPhase.Returning;p[0].FishId=3;
            f[3].X=-5000;f[3].Z=0;f[3].Y=1250;f[3].Owner=0;f[3].Phase=FishPhase.Hooked;
            p[1].X=-7000;p[1].Z=700;p[1].Swimming.InWater=true;p[1].Phase=HookPhase.Flying;
            p[1].HookX=-5150;p[1].HookZ=0;p[1].DirectionX=1000;p[1].DirectionZ=0;p[1].Distance=1000;
            var tackle=new TackleState(s.Tackle.Config,inventory.Capture(),s.Tackle.Stock,s.Tackle.Batch,s.Tackle.NextRestock);
            m.Restore(new HookState(s.Steps,p,f,p.Sum(v=>v.Coins),s.Tiles,s.Driftwood,tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean));
            ShopStep(m);s=m.Capture();
            Require(s.Fish[3].Owner==1 && s.Players[0].FishId==-1 && s.Players[1].FishId==3 && s.Ocean.Visits[0].Active,"Expiry blocked reverse stealing or removed an existing line.");
            Require(s.Fish[3].Generation==1 && !s.Fish[3].Escape.Active && s.Ocean.Sequence==1,"Reverse stealing respawned the visit or repeated an event.");
        });
        yield return ("ocean_released_visit_finishes_escape_before_retiring",()=>{
            var m=PullModel(OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1,visitTicks:2)),1);ShopStep(m);OceanHoldVisit(m);
            ShopStep(m,new HookInput(0,0,cancel:true).Encode());var s=m.Capture();
            Require(s.Fish[3].Phase==FishPhase.Escaping && s.Ocean.Visits[0].Active,"Cancel removed the visit before escape animation.");
            for(int i=0;i<45;i++)ShopStep(m);s=m.Capture();
            Require(!s.Ocean.Visits[0].Active && s.Fish[3].Phase==FishPhase.Waiting && s.Players[0].Catches==0,"Escaped visit respawned or paid rewards.");
        });
        yield return ("ocean_no_new_events_without_living_players",()=>{
            var map=OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1));var s=PullModel(map,1).Capture();
            foreach(bool unavailable in new[]{false,true})
            {
                var p=s.Players.ToArray();p[0].Unavailable=unavailable;if(!unavailable)p[0].Vitals.Health=0;
                var system=new OceanSystem(map.Fishing!.Ocean);system.Advance(map,s.Tiles,p,s.Fish.ToArray(),2,s.Enemies,1);
                Require(system.Capture().Sequence==0 && !system.Capture().Visits.Any(v=>v.Active),"Unavailable or downed team generated opportunities.");
            }
        });
        yield return ("ocean_living_swimmer_gets_quiet_without_land_spawn",()=>{
            var map=OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1));var s=PullModel(map,1).Capture();var p=s.Players.ToArray();
            p[0].X=10000;p[0].Z=10000;p[0].Swimming.InWater=true;
            var system=new OceanSystem(map.Fishing!.Ocean);system.Advance(map,s.Tiles,p,s.Fish.ToArray(),2,s.Enemies,1);
            Require(system.Capture().QuietUntil==601 && !system.Capture().Visits.Any(v=>v.Active) && system.Capture().RewardedStage==0,"Swimmer lost recovery or generated an unreachable land opportunity.");
        });
        yield return ("ocean_recovery_priority_defers_latest_stage_until_slots_free",()=>{
            var map=OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1,recoveryTicks:3,recoveryCooldownTicks:100,visitTicks:6));
            var s=PullModel(map,1).Capture();var system=new OceanSystem(map.Fishing!.Ocean);var fish=s.Fish.ToArray();
            system.Advance(map,s.Tiles,s.Players,fish,2,s.Enemies,1);
            Require(system.Capture().RewardedStage==0 && system.Capture().LastEvent==OceanEvent.Recovery,"Stage wave preempted recovery.");
            system.Advance(map,s.Tiles,s.Players,fish,2,s.Enemies,5);
            Require(system.Capture().RewardedStage==0,"Stage wave replaced occupied visits.");
            system.BeforeStep(map,s.Tiles,fish,7);system.Advance(map,s.Tiles,s.Players,fish,2,s.Enemies,7);
            Require(system.Capture().RewardedStage==2 && system.Capture().Sequence==2 && system.Capture().LastEvent==OceanEvent.RareSchool,"Latest deferred stage did not skip an obsolete middle wave.");
            Require(fish.Skip(3).All(f=>FishCatalog.All[f.Species].Rarity==FishRarity.Rare && f.Grade==1 && f.WeightGrams<=2000),"Stage opportunity used wrong fish profile.");
        });
        yield return ("ocean_full_held_slots_survive_next_recovery",()=>{
            var map=OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1,recoveryTicks:3,recoveryCooldownTicks:5,visitTicks:2));
            var s=PullModel(map,1).Capture();var system=new OceanSystem(map.Fishing!.Ocean);var fish=s.Fish.ToArray();system.Advance(map,s.Tiles,s.Players,fish,0,s.Enemies,1);
            for(int i=3;i<fish.Length;i++){fish[i].Phase=FishPhase.Hooked;fish[i].Owner=0;}
            var before=system.Capture();system.BeforeStep(map,s.Tiles,fish,6);system.Advance(map,s.Tiles,s.Players,fish,0,s.Enemies,6);
            var after=system.Capture();Require(after.Sequence==2 && after.QuietUntil==9,"Full slots prevented the quiet recovery window.");
            Require(after.Visits.SequenceEqual(before.Visits) && fish.Skip(3).All(f=>f.Owner==0 && f.Generation==1),"Recovery overwrote held visit fish.");
        });
        yield return ("ocean_expansion_retires_blocked_unheld_lane",()=>{
            var map=OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1));var s=PullModel(map,1).Capture();var fish=s.Fish.ToArray();
            var system=new OceanSystem(map.Fishing!.Ocean);system.Advance(map,s.Tiles,s.Players,fish,0,s.Enemies,1);
            var visit=system.Capture().Visits[0];var tiles=s.Tiles.Concat(new[]{new PlatformTile(PlatformRules.CellAt(visit.HomeX),PlatformRules.CellAt(visit.HomeZ),TileKind.Wood,0,4)}).ToArray();
            system.BeforeStep(map,tiles,fish,2);Require(!system.Capture().Visits[0].Active && fish[3].Phase==FishPhase.Waiting,"Expanded deck left an unheld fish swimming through its tiles.");
        });
        yield return ("ocean_placement_checks_full_lane_and_world_boundary",()=>{
            var map=OceanMap();var s=PullModel(map,1).Capture();var homes=new List<Point>();
            for(uint i=0;i<2;i++)
            {
                Require(OceanPlacement.Find(map,s.Tiles,s.Players,homes,i,out var home),"No nearshore candidate for initial platform.");homes.Add(home);
                var direction=FishMotion.Direction(new Fish{Heading=1},home);
                for(int along=-3600;along<=3600;along+=100)
                {
                    int x=home.X+direction.X*along/1000,z=home.Z+direction.Z*along/1000;
                    Require(!s.Tiles.Any(t=>Math.Abs(x-t.X*2000)<1720 && Math.Abs(z-t.Z*2000)<1720),"Fish lane intersects deck including body radius.");
                }
            }
            Require(!OceanPlacement.LaneWater(map,s.Tiles,new Point(49500,49000)),"World-boundary lane was accepted.");
            var p=s.Players.ToArray();p[0].X=p[0].Z=48000;
            var corner=new[]{new PlatformTile(24,24,TileKind.Wood)};
            if(OceanPlacement.Find(map,corner,p,Array.Empty<Point>(),0,out var edge))Require(OceanPlacement.LaneWater(map,corner,edge),"Boundary search returned invalid water.");
        });
        yield return ("ocean_blocked_edges_defer_stage_without_rewards",()=>{
            var basis=IslandMap.CreatePlatform();var blocked=Enumerable.Range(-2,5).SelectMany(x=>Enumerable.Range(-2,5).Where(z=>Math.Abs(x)==2 || Math.Abs(z)==2).Select(z=>new Point(x,z)));
            var map=OceanRules.CreateMap(new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,new PlatformConfig(blockedCells:blocked)),
                new FishingConfig(commissions:new CommissionConfig(),ocean:new OceanConfig()));
            var s=PullModel(map,1).Capture();var system=new OceanSystem(map.Fishing!.Ocean);system.Advance(map,s.Tiles,s.Players,s.Fish.ToArray(),2,s.Enemies,1);
            Require(system.Capture().Sequence==0 && system.Capture().RewardedStage==0 && s.Coins==0,"Failed placement lost the pending opportunity or injected rewards.");
        });
        yield return ("ocean_restore_continues_without_duplicate_waves",()=>{
            var map=OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:80,recoveryTicks:20,recoveryCooldownTicks:100,visitTicks:50));
            var a=PullModel(map,1);CommissionCatch(a);for(int i=0;i<100;i++)ShopStep(a);
            var codec=new HookGame();var b=PullModel(map,1);b.Restore(codec.Decode(codec.Encode(a.Capture())));
            for(int i=0;i<250;i++)
            {
                ShopStep(a);ShopStep(b);var bytes=codec.Encode(a.Capture());_=codec.Decode(bytes);
                Require(bytes.SequenceEqual(codec.Encode(b.Capture())),"Restored ocean schedule diverged or reissued an event.");
            }
        });
        yield return ("ocean_restore_keeps_held_visit_after_platform_covers_its_home",()=>{
            var map=OceanMap(new OceanConfig(new[]{0,1,2},noCatchTicks:1));var m=PullModel(map,1);ShopStep(m);OceanHoldVisit(m);
            var s=m.Capture();var home=s.Ocean.Visits[0];var p=s.Players.ToArray();p[0].WoodEarned+=4;
            var tiles=s.Tiles.Concat(new[]{new PlatformTile(PlatformRules.CellAt(home.HomeX),PlatformRules.CellAt(home.HomeZ),TileKind.Wood,0,4)}).ToArray();
            var expanded=new HookState(s.Steps,p,s.Fish,s.Coins,tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean);
            var codec=new HookGame();var bytes=codec.Encode(expanded);var restored=PullModel(map,1);restored.Restore(codec.Decode(bytes));
            Require(bytes.SequenceEqual(codec.Encode(restored.Capture())),"Expanded historical visit could not round-trip while hooked.");
            ShopStep(restored);var after=restored.Capture();
            Require(after.Ocean.Visits[0].Active && after.Fish[3].Phase==FishPhase.Hooked && after.Players[0].FishId==3,
                "Expansion retired an already hooked temporary fish after restore.");
        });
        yield return ("ocean_corrupt_state_and_configuration_rejected",()=>{
            var m=PullModel(OceanMap(),1);var s=m.Capture();var codec=new HookGame();
            var invalid=new[]{new OceanState(),new OceanState(lastCatchTick:1,visits:s.Ocean.Visits),new OceanState(rewardedStage:1,visits:s.Ocean.Visits),
                new OceanState(threatCount:1,visits:s.Ocean.Visits),new OceanState(handledAttacks:1,visits:s.Ocean.Visits),
                new OceanState(sequence:1,lastEvent:OceanEvent.Recovery,lastEventTick:1,quietUntil:601,reliefReadyTick:1801,visits:s.Ocean.Visits)};
            foreach(var ocean in invalid)OceanReject(()=>codec.Decode(codec.Encode(OceanCopy(s,ocean:ocean))));
            var fish=s.Fish.ToArray();fish[2].Phase=FishPhase.Swimming;OceanReject(()=>m.Restore(OceanCopy(s,fish:fish)));
            var bad=new Action[]{()=>new OceanConfig(new[]{1,2}),()=>new OceanConfig(new[]{0,3}),()=>new OceanConfig(new[]{0},visitSlots:0),
                ()=>new OceanConfig(new[]{0},recoveryTicks:50,recoveryCooldownTicks:49),()=>new FishSchoolProfile(FishRarity.Rare,FishRarity.Common,1000,2000),
                ()=>new FishSchoolProfile(FishRarity.Common,FishRarity.Common,1001,2000),()=>new FishingConfig(ocean:new OceanConfig()),
                ()=>OceanRules.CreateMap(IslandMap.CreatePlatform(),new FishingConfig(commissions:new CommissionConfig(),ocean:new OceanConfig(new[]{0,1,2})))};
            foreach(var action in bad)OceanReject(action);
            var disabled=PullModel(IslandMap.CreatePlatform(),1).Capture();OceanReject(()=>codec.Decode(codec.Encode(OceanCopy(disabled,ocean:s.Ocean))));
        });
        yield return ("ocean_opening_band_stays_light_across_generations",()=>{
            var m=PullModel(OceanMap(),1);
            for(int i=0;i<12;i++)
            {
                var s=CommissionCatch(m);var f=s.Fish[0];
                Require(FishCatalog.All[f.Species].Rarity==FishRarity.Common && f.Grade==1 && f.WeightGrams<=3000,"Later ordinary generation lost the starter catch path.");
            }
        });
    }
    private static HookState OceanCopy(HookState s,OceanState? ocean=null,Fisher[]? players=null,Fish[]? fish=null)
        =>new HookState(s.Steps,players??s.Players.ToArray(),fish??s.Fish.ToArray(),players?.Sum(p=>p.Coins)??s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,ocean??s.Ocean);
    private static EnemyState OceanAttacks(int shots,int interruptions)=>new EnemyState(Array.Empty<Piranha>(),default,Array.Empty<Waterball>(),shots:shots,interruptions:interruptions);
    private static void OceanReject(Action action)
    {bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Require(rejected,"Invalid ocean input was accepted.");}
    private static void OceanHoldVisit(IDeterministicSimulation<HookState> m)
    {
        var s=m.Capture();var players=s.Players.ToArray();var fish=s.Fish.ToArray();Require(s.Ocean.Visits[0].Active,"Fixture has no visit.");
        for(int i=0;i<players.Length;i++)
        {
            ref var p=ref players[i];p.X=-2400;p.Z=i*300;p.HookX=-3200;p.HookZ=0;p.Phase=HookPhase.Returning;p.FishId=3;
            p.FishingJoinOrder=i;p.FishingDrainRemainder=0;p.Stamina=FishingRules.MaxStamina(s,p);
        }
        fish[3].X=-3200;fish[3].Z=0;fish[3].Y=1250;fish[3].Owner=0;fish[3].Phase=FishPhase.Hooked;
        m.Restore(OceanCopy(s,players:players,fish:fish));
    }
}

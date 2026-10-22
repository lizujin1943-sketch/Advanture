using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IslandMap FinaleRuleMap()
    {
        var b=IslandMap.CreatePlatform();
        return new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,
            new FishingConfig(commissions:new CommissionConfig(new[]{new CommissionDefinition(1,1),new CommissionDefinition(1,1),new CommissionDefinition(1,1)})));
    }
    private static CommissionState FinaleCommissions(IslandMap map,int count=3)
    {
        var c=CommissionSystem.Create(map.Fishing!.Commissions);
        for(int i=0;i<count;i++)c=CommissionSystem.RecordCatch(map.Fishing.Commissions,c,false);
        return c;
    }
    private static Fisher[] FinalePlayers(IslandMap map,int count)=>PullModel(map,count).Capture().Players.ToArray();
    private static void FinaleAttach(Fisher[] players)
    {
        for(int i=0;i<players.Length;i++)
        {players[i].HookedFinale=true;players[i].Phase=HookPhase.Returning;players[i].Stamina=3000000;}
    }
    private static IEnumerable<(string,Action)> FinaleCases()
    {
        // Missing eligibility or repeated charging would spend shared bait before/after the one transition.
        yield return ("finale_consumes_bait_once_after_all_commissions",()=>{
            var map=FinaleRuleMap();var players=FinalePlayers(map,1);var system=new FinaleSystem(new FinaleConfig());
            system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map,2),1);
            Require(system.Capture().Phase==FinalePhase.Dormant && system.Capture().SpentBait==0,"Incomplete commissions spent bait.");
            system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),2);
            Require(system.Capture().Phase==FinalePhase.Summoning && system.Capture().SpentBait==3 && system.Capture().Timer==300,"Completed commissions did not summon once with three bait.");
            for(ulong t=3;t<302;t++)system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),t);
            Require(system.Capture().Phase==FinalePhase.Summoning && system.Capture().Timer==1,"Summon warning ended before ten seconds.");
            system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),302);
            var restored=new FinaleSystem(new FinaleConfig(),system.Capture());
            restored.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),303);
            Require(restored.Capture().Phase==FinalePhase.Surfaced && restored.Capture().SpentBait==3,"Restoring summoned fish spent bait again or lost target.");
        });
        // A wrong threshold, lost reset, or per-player total charge breaks the manually calculated cost.
        yield return ("finale_requires_continuous_hooks_and_shares_stamina",()=>{
            var map=FinaleRuleMap();var players=FinalePlayers(map,2);FinaleAttach(players);
            var system=new FinaleSystem(new FinaleConfig(),new FinaleState(FinalePhase.Surfaced,spentBait:3,x:0,z:-5100));
            for(ulong t=1;t<=10;t++)system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),t);
            Require(system.Capture().Progress==10 && players.All(p=>p.Stamina==2940000),"Two lines did not share 12 stamina per second.");
            players[1].HookedFinale=false;
            system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),11);
            Require(system.Capture().Progress==0 && players[0].Stamina==2940000,"Losing required line kept progress or charged while waiting.");
            players[1].HookedFinale=true;
            for(ulong t=12;t<161;t++)system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),t);
            Require(system.Capture().Completed==0 && system.Capture().Progress==149,"Partial previous hold leaked into next attempt.");
            system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),161);
            Require(system.Capture().Completed==1 && system.Capture().Phase==FinalePhase.Submerged && players.All(p=>!p.HookedFinale),"Five-second continuous hold did not finish exactly one round and clear lines.");
        });
        // Failing to change location, requiring three extra bait each round, or skipping a round fails this path.
        yield return ("finale_changes_shores_and_wins_after_three_rounds",()=>{
            var map=FinaleRuleMap();var players=FinalePlayers(map,4);var config=new FinaleConfig(summonTicks:2,relocateTicks:2,holdTicks:3);
            var system=new FinaleSystem(config);ulong tick=0;var sites=new List<Point>();
            for(int round=0;round<3;round++)
            {
                for(int wait=0;wait<10 && system.Capture().Phase!=FinalePhase.Surfaced;wait++)
                    system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),++tick);
                var s=system.Capture();Require(s.Phase==FinalePhase.Surfaced,"Next shore did not appear.");sites.Add(new Point(s.X,s.Z));
                FinaleAttach(players);
                for(int i=0;i<3;i++)system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),++tick);
                Require(system.Capture().Completed==round+1,"Wrong number of completed rounds.");
            }
            var won=system.Capture();Require(won.Phase==FinalePhase.Victory && won.VictoryTick==tick && won.SpentBait==3,"Third round did not record one victory.");
            Require(sites[0].Z<0 && sites[1].X>0 && sites[2].Z>0 && sites.Zip(sites.Skip(1),(a,b)=>IslandMap.DistanceSquared(a.X,a.Z,b.X,b.Z)).All(d=>d>=16000000),"Giant fish did not move between distinct shore directions.");
            Require(players.All(p=>!p.HookedFinale && p.Coins==0 && p.Catches==0),"Finale kept hooks or fabricated ordinary catch income.");
        });
        foreach(var item in new (int players,int needed,int cost)[]{(1,1,12000),(2,2,6000),(3,2,4000),(4,3,3000)})
        {
            var test=item;
            yield return ($"finale_threshold_and_cost_for_{test.players}_players",()=>{
                var map=FinaleRuleMap();var players=FinalePlayers(map,test.players);FinaleAttach(players);
                var system=new FinaleSystem(new FinaleConfig(),new FinaleState(FinalePhase.Surfaced,spentBait:3,x:0,z:-5100));
                for(int i=test.needed-1;i<players.Length;i++)players[i].HookedFinale=false;
                system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),1);
                Require(system.Capture().Progress==0 && players.All(p=>p.Stamina==3000000),"Insufficient lines made progress or spent stamina.");
                FinaleAttach(players);system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),2);
                Require(system.Capture().Progress==1 && players.All(p=>p.Stamina==3000000-test.cost),"Wrong adaptive hook requirement or shared cost.");
            });
        }
        yield return ("finale_downed_still_counts_and_revived_player_restores_threshold",()=>{
            var map=FinaleRuleMap();var players=FinalePlayers(map,4);FinaleAttach(players);
            players[2].HookedFinale=players[3].HookedFinale=false;players[3].Vitals.Health=0;
            var system=new FinaleSystem(new FinaleConfig(),new FinaleState(FinalePhase.Surfaced,spentBait:3,x:0,z:-5100));
            system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),1);
            Require(system.Capture().Progress==0,"Downing a teammate reduced the requirement.");
            players[3].Revival.Dead=true;system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),2);
            Require(system.Capture().Progress==1,"Final death kept an impossible extra line requirement.");
            players[3].Revival.Dead=false;players[3].Vitals.Health=100;
            system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),3);
            Require(system.Capture().Progress==0,"Paid revival did not restore the living roster requirement.");
        });
        yield return ("finale_exhaustion_on_last_frame_prevents_completion",()=>{
            var map=FinaleRuleMap();var players=FinalePlayers(map,2);FinaleAttach(players);players[0].Stamina=6000;
            var system=new FinaleSystem(new FinaleConfig(holdTicks:2),new FinaleState(FinalePhase.Surfaced,progress:1,spentBait:3,x:0,z:-5100));
            system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),1);
            Require(system.Capture().Completed==0 && system.Capture().Progress==0 && !players[0].HookedFinale && players[0].Stamina==0,"Exhausted line completed a round.");
            Require(players[0].FinaleDrainRemainder==0 && players[0].Phase==HookPhase.Returning,"Exhaustion did not release the empty hook.");
        });
        yield return ("finale_subunit_drain_and_restore_preserve_exact_cost",()=>{
            var map=FinaleRuleMap();var players=FinalePlayers(map,3);FinaleAttach(players);var config=new FinaleConfig(staminaMilliPerSecond:1);
            var system=new FinaleSystem(config,new FinaleState(FinalePhase.Surfaced,spentBait:3,x:0,z:-5100));
            system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),1);
            var other=new FinaleSystem(config,system.Capture());var copies=players.ToArray();
            for(ulong t=2;t<=36;t++)
            {
                system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),t);
                other.Advance(map,PlatformRules.InitialTiles(),copies,FinaleCommissions(map),t);
            }
            Require(players.All(p=>p.Stamina==2999988 && p.FinaleDrainRemainder==0),"Three-way fractional cost was lost or rounded up each frame.");
            Require(other.Capture().Progress==36 && copies.All(p=>p.Stamina==2999988 && p.FinaleDrainRemainder==0),"Restoring progress lost a held frame or drain remainder.");
        });
        yield return ("finale_extra_hook_shares_cost_without_accelerating_round",()=>{
            var map=FinaleRuleMap();var three=FinalePlayers(map,4);var four=FinalePlayers(map,4);FinaleAttach(three);FinaleAttach(four);three[3].HookedFinale=false;
            var config=new FinaleConfig(holdTicks:5);var start=new FinaleState(FinalePhase.Surfaced,spentBait:3,x:0,z:-5100);
            var a=new FinaleSystem(config,start);var b=new FinaleSystem(config,start);
            for(ulong t=1;t<=4;t++){a.Advance(map,PlatformRules.InitialTiles(),three,FinaleCommissions(map),t);b.Advance(map,PlatformRules.InitialTiles(),four,FinaleCommissions(map),t);}
            Require(a.Capture().Progress==4 && b.Capture().Progress==4 && b.Capture().Completed==0,"Fourth hook accelerated stage completion.");
            Require(three[0].Stamina==2984000 && four[0].Stamina==2988000,"Extra line did not reduce each participant's share.");
        });
        yield return ("finale_invalid_holders_detach_before_progress",()=>{
            foreach(var invalid in new Action<Fisher[]>[]{p=>p[0].Unavailable=true,p=>p[0].Vitals.Health=0,p=>p[0].Swimming.InWater=true,
                p=>p[0].Knockback.Active=true,p=>p[0].PulledBy=1,p=>p[0].Shopping=true,p=>p[0].Stamina=0})
            {
                var map=FinaleRuleMap();var players=FinalePlayers(map,2);FinaleAttach(players);invalid(players);
                var system=new FinaleSystem(new FinaleConfig(),new FinaleState(FinalePhase.Surfaced,progress:2,spentBait:3,x:0,z:-5100));
                system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),1);
                Require(!players[0].HookedFinale && players[0].FinaleDrainRemainder==0,"Invalid holder remained on the target.");
                Require(system.Capture().Completed==0,"Invalid line completed a stage.");
            }
        });
        yield return ("finale_no_survivors_never_progress_or_win",()=>{
            var map=FinaleRuleMap();var players=FinalePlayers(map,1);FinaleAttach(players);players[0].Revival.Dead=true;
            var system=new FinaleSystem(new FinaleConfig(holdTicks:1),new FinaleState(FinalePhase.Surfaced,completed:2,spentBait:3,x:0,z:5100));
            system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),1);
            Require(system.Capture().Phase==FinalePhase.Surfaced && system.Capture().Progress==0 && !players[0].HookedFinale,"Zero-person requirement awarded victory.");
        });
        yield return ("finale_no_land_or_no_site_waits_without_another_bait_charge",()=>{
            var map=FinaleRuleMap();var players=FinalePlayers(map,1);players[0].X=players[0].Z=0;
            var blocked=Enumerable.Range(-1,3).SelectMany(x=>Enumerable.Range(-1,3).Select(z=>new Point(x,z))).Where(p=>p.X!=0 || p.Z!=0);
            var sealedMap=new IslandMap(map.Shore,map.Pier,map.Spawns,map.FishSpots,new PlatformConfig(blockedCells:blocked),map.Fishing);
            var tiny=new[]{new PlatformTile(0,0,TileKind.Wood)};
            var system=new FinaleSystem(new FinaleConfig(),new FinaleState(FinalePhase.Summoning,timer:1,spentBait:3));
            for(ulong t=1;t<=10;t++)system.Advance(sealedMap,tiny,players,FinaleCommissions(map),t);
            Require(system.Capture().Phase==FinalePhase.Summoning && system.Capture().Timer==0 && system.Capture().SpentBait==3,"No placement caused escape, repeated consumption or overflow.");
            players[0].X=players[0].Z=10000;system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),11);
            Require(system.Capture().Phase==FinalePhase.Summoning,"Summoned without an actionable player on land.");
            players[0].X=players[0].Z=0;system.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),12);
            Require(system.Capture().Phase==FinalePhase.Surfaced && system.Capture().SpentBait==3,"Placement did not resume once a legal bank became available.");
        });
        yield return ("finale_placement_is_inside_world_and_reachable_with_starter_rod",()=>{
            var map=FinaleRuleMap();var config=new FinaleConfig();var players=FinalePlayers(map,4);
            foreach(var layout in new[]{PlatformRules.InitialTiles(),new[]{new PlatformTile(24,24,TileKind.Wood)},new[]{new PlatformTile(-24,-24,TileKind.Wood)}})
            {
                for(int p=0;p<players.Length;p++){players[p].X=layout[0].X*2000;players[p].Z=layout[0].Z*2000;}
                var previous=default(Point);
                for(int round=0;round<3;round++)
                {
                    Require(FinalePlacement.Find(map,config,layout,players,round,previous,out var site),"A legal boundary bank could not host the finale.");
                    var pos=site.Position;var state=new FinaleState(FinalePhase.Surfaced,x:pos.X,z:pos.Z,direction:site.Direction);var anchor=FinaleRules.Anchor(config,state);
                    Require(Math.Abs(pos.X)<=48800 && Math.Abs(pos.Z)<=48800,"Target body crosses world bounds.");
                    Require(PlatformRules.CanStand(layout,anchor.X,anchor.Z) && IslandMap.DistanceSquared(anchor.X,anchor.Z,pos.X,pos.Z)<=16000000,"Target needs more than the starter rod or an unsafe stance.");
                    foreach(int x in new[]{pos.X-1200,pos.X,pos.X+1200})foreach(int z in new[]{pos.Z-1200,pos.Z,pos.Z+1200})
                        Require(!PlatformRules.OnPlatform(layout,x,z),"Part of the body spawned in a platform tile.");
                    if(round>0)Require(pos.X!=previous.X || pos.Z!=previous.Z,"Next round reused identical position.");
                    previous=pos;
                }
            }
        });
        yield return ("finale_surface_body_reserves_building_space_only_while_visible",()=>{
            var c=new FinaleConfig();var shown=new FinaleState(FinalePhase.Surfaced,x:0,z:-5100);
            Require(FinaleRules.BlocksBuild(c,shown,0,-2) && !FinaleRules.BlocksBuild(c,shown,0,0),"Building exclusion does not match giant body.");
            Require(!FinaleRules.BlocksBuild(c,new FinaleState(FinalePhase.Submerged,x:0,z:-5100),0,-2),"Submerged target permanently blocks building.");
        });
        yield return ("finale_disabled_and_low_bait_leave_progress_untouched",()=>{
            var map=FinaleRuleMap();var players=FinalePlayers(map,1);var disabled=new FinaleSystem(FinaleConfig.Disabled);
            for(ulong t=1;t<=500;t++)disabled.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),t);
            Require(disabled.Capture().Phase==FinalePhase.Dormant && disabled.Capture().SpentBait==0,"Disabled finale changed play.");
            var expensive=new FinaleSystem(new FinaleConfig(baitCost:4));expensive.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map),1);
            Require(expensive.Capture().Phase==FinalePhase.Dormant,"Insufficient bait started a summon.");
            var cheap=new FinaleSystem(new FinaleConfig(baitCost:1));cheap.Advance(map,PlatformRules.InitialTiles(),players,FinaleCommissions(map,2),1);
            Require(cheap.Capture().Phase==FinalePhase.Dormant,"Enough bait bypassed unfinished commissions.");
        });
        yield return ("finale_rejects_unreachable_hook_counts_and_invalid_parameters",()=>{
            foreach(var invalid in new Action[]{()=>new FinaleConfig(baitCost:0),()=>new FinaleConfig(summonTicks:0),()=>new FinaleConfig(holdTicks:0),
                ()=>new FinaleConfig(relocateTicks:-1),()=>new FinaleConfig(staminaMilliPerSecond:0),()=>new FinaleConfig(requiredHooks:new[]{1,3,3,4}),
                ()=>new FinaleConfig(requiredHooks:new[]{1,2,1,3}),()=>new FinaleConfig(requiredHooks:new[]{1}),()=>new FinaleConfig(edgeOffset:2000),
                ()=>new FinaleConfig(bodyRadius:int.MaxValue),()=>new FinaleConfig(phaseShift:0)})
            {bool rejected=false;try{invalid();}catch(InvalidDataException){rejected=true;}Require(rejected,"Invalid finale setup accepted.");}
        });
    }
}

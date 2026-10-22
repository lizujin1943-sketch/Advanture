using System;
using System.Collections.Generic;
using System.Linq;
using FrameSync.Abstractions;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.EnemyTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> EvolutionCases()
    {
        foreach(int count in new[]{1,4})yield return ($"evolution_normal_input_acceptance_{count}",()=>{
            var m=Model(count);bool patrol=false,submerged=false;var first=m.Capture().Enemies.Piranhas[0];
            for(int i=0;i<1800 && !EnemySmokePilot.Complete(m.Capture());i++)
            {
                var s=m.Capture();patrol|=s.Enemies.Piranhas[0].X!=first.X;submerged|=s.Enemies.Monster.Phase==MonsterPhase.Cooldown && s.Enemies.Monster.DiveTicks==0;
                Step(m,Enumerable.Range(0,count).Select(slot=>EnemySmokePilot.Sample(s,slot)).ToArray());
            }
            var e=m.Capture().Enemies;
            Require(patrol && submerged && EnemySmokePilot.Complete(m.Capture()),$"Acceptance incomplete: capture={e.Captures} interrupts={e.Interruptions} hits={e.WaterHits} bites={e.Bites} shots={e.Shots}");
        });
        yield return ("evolution_two_normal_hooks_share_one_reward",()=>{
            var c=new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30);var m=Model(2,c);var s=m.Capture();var ps=s.Players.ToArray();
            ps[0].X=ps[1].X=-2400;ps[0].Z=-700;ps[1].Z=700;m.Restore(Replace(s,ps));
            Step(m,new HookInput(0,0,-4800,0,true),new HookInput(0,0,-4800,0,true));
            int lead=-1;
            for(int i=0;i<100;i++)
            {
                var state=m.Capture();if(state.Players.All(p=>p.HookedPiranha==1))lead=state.Enemies.Piranhas[0].Owner;
                m.Restore(state);Step(m);
            }
            var end=m.Capture();
            Require(lead>=0 && end.Enemies.Captures==1 && end.Players.All(p=>p.Catches==1) && end.Coins-s.Coins==15 &&
                end.Players.All(p=>p.Fishbones==3) && end.Players[lead].Coins-s.Players[lead].Coins==8 &&
                end.Players[1-lead].Coins-s.Players[1-lead].Coins==7,"Shared hooks duplicated, lost or misallocated rewards");
        });
        yield return ("evolution_building_over_monster_cancels_windup",()=>{
            var e=new EnemySystem(new EnemyConfig(initialDelayTicks:0));var ps=new[]{Player(0,0,0)};e.Advance(ps,_=>{},(x,z)=>true);
            Require(e.Capture().Monster.Phase==MonsterPhase.Windup,"No windup");e.Advance(ps,_=>{},(x,z)=>false);
            Require(e.Capture().Monster.Phase==MonsterPhase.Cooldown && e.Capture().Shots==0,"Monster fired through new floor");
        });
        yield return ("evolution_checkpoint_keeps_projectiles_from_different_surface_positions",()=>{
            var c=new EnemyConfig(piranhaCount:0,initialDelayTicks:0,windupTicks:1,attackIntervalTicks:15,waterballSpeed:1000,monsterLevel:10);
            var a=Model(config:c);var b=Model(config:c);var codec=new HookGame();
            for(int i=0;i<40;i++){b.Restore(a.Capture());Step(a);Step(b);Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Relocation/flight restore diverged");}
            var s=a.Capture();Require(s.Enemies.Waterballs.Select(ball=>(ball.OriginX,ball.OriginZ)).Distinct().Count()>1,"Fixture lacks concurrent origins");
            Require(s.Enemies.Monster.Level==10 && s.Enemies.Waterballs.All(ball=>ball.KnockbackDistance==22000),"Highest level was not saved");
        });
        yield return ("evolution_checkpoint_rejects_orphan_or_respawning_hook_owner",()=>{
            var m=Model(config:new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30));Step(m,new HookInput(0,0,-4800,0,true));
            for(int i=0;i<60 && m.Capture().Players[0].HookedPiranha==0;i++)Step(m);
            var s=m.Capture();var fs=s.Enemies.Piranhas.ToArray();fs[0].Owner=-1;
            Throws<System.IO.InvalidDataException>(()=>m.Restore(Replace(s,enemies:new EnemyState(fs,s.Enemies.Monster,s.Enemies.Waterballs))));
            fs=s.Enemies.Piranhas.ToArray();fs[0].RespawnTicks=1;
            Throws<System.IO.InvalidDataException>(()=>m.Restore(Replace(s,enemies:new EnemyState(fs,s.Enemies.Monster,s.Enemies.Waterballs))));
        });
        yield return ("evolution_game_normal_hook_catches_and_restores_reward_once",()=>{
            var c=new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30);var m=Model(config:c);var start=m.Capture();
            Step(m,new HookInput(0,0,-4800,0,true));bool attached=false;
            for(int i=0;i<100 && m.Capture().Enemies.Captures==0;i++)
            {
                var s=m.Capture();attached|=s.Players[0].HookedPiranha==1;
                m.Restore(new HookGame().Decode(new HookGame().Encode(s)));Step(m);
            }
            var end=m.Capture();var p=end.Players[0];
            Require(attached && end.Enemies.Captures==1,"Normal hook did not capture a piranha");
            Require(p.Catches==1 && p.Coins-start.Players[0].Coins==15 && p.Fishbones==6 && p.Stamina<start.Players[0].Stamina,"Capture did not reuse fish economy/stamina");
            Require(end.Enemies.Piranhas[0].Owner==-1 && end.Enemies.Piranhas[0].RespawnTicks>0,"Captured fish still available");
            m.Restore(end);for(int i=0;i<30;i++)Step(m);Require(m.Capture().Coins==end.Coins && m.Capture().Players[0].Fishbones==6,"Restoring duplicated rewards");
        });
        foreach(var reason in new[]{"cancel","exhausted","offline","downed"})
            yield return ($"evolution_game_hook_releases_on_{reason}",()=>{
                var m=Model(config:new EnemyConfig(piranhaCount:1,initialDelayTicks:18000,patrolSpeed:30));Step(m,new HookInput(0,0,-4800,0,true));
                for(int i=0;i<60 && m.Capture().Players[0].HookedPiranha==0;i++)Step(m);
                var s=m.Capture();Require(s.Players[0].HookedPiranha==1,"Fixture never hooked piranha");
                if(reason=="exhausted"){var ps=s.Players.ToArray();ps[0].Stamina=0;m.Restore(Replace(s,ps));Step(m);}
                else if(reason=="downed")
                {
                    var ps=s.Players.ToArray();ps[0].Vitals.Health=1;
                    var ball=new Waterball{Id=1,OriginX=ps[0].X,OriginZ=ps[0].Z-4600,X=ps[0].X,Z=ps[0].Z-67,DirectionZ=1000000,Distance=4533,TravelRemainder=10,KnockbackDistance=4000};
                    m.Restore(Replace(s,ps,new EnemyState(s.Enemies.Piranhas,s.Enemies.Monster,new[]{ball},1,shots:1)));Step(m);
                    Require(m.Capture().Players[0].Vitals.Downed,"Fixture was not downed");
                }
                else if(reason=="offline")m.Step(new FrameContext(s.Steps+1,s.Steps+1,30,new[]{new MemberEvent(s.Steps+1,MemberEventKind.Deactivate,0,1)}),Array.Empty<SimulationInput>());
                else Step(m,new HookInput(0,0,cancel:true));
                var end=m.Capture();Require(end.Players[0].HookedPiranha==0 && end.Enemies.Piranhas[0].Owner==-1 && end.Enemies.Captures==0 && end.Coins==s.Coins,"Release retained ownership/reward");
                if(reason=="exhausted")Require(end.Players[0].Escapes==1,"Exhaustion did not report escape");
                m.Restore(end);
            });
        yield return ("evolution_piranha_patrols_then_prioritizes_swimmer",()=>{
            var e=new EnemySystem(new EnemyConfig(piranhaCount:1,monsterEnabled:false));var first=e.Capture().Piranhas[0];
            var ps=new[]{Player(0,0,0)};for(int i=0;i<60;i++)e.Advance(ps,_=>{});
            var patrolled=e.Capture().Piranhas[0];Require(patrolled.X!=first.X || patrolled.Z!=first.Z,"Idle fish stood still");
            ps[0]=Player(0,patrolled.X+500,patrolled.Z,true);var hits=new List<EnemyHit>();e.Advance(ps,hits.Add);
            Require(hits.Count==1 && hits[0].Damage==8,"Reduced 2.5 kg damage must be ceil(7.5)=8");
        });
        yield return ("evolution_hook_claim_excludes_competitors_and_stops_biting_until_respawn",()=>{
            var c=new EnemyConfig(piranhaCount:1,monsterEnabled:false,piranhaRespawnTicks:30);var e=new EnemySystem(c);
            var f=e.Capture().Piranhas[0];var ps=new[]{Player(0,f.X,f.Z,true),Player(1,f.X,f.Z,true)};
            Require(e.TryHook(0,0) && !e.TryHook(0,1),"Duplicate hook ownership");
            for(int i=0;i<10;i++)e.Advance(ps,_=>throw new Exception("Hooked fish bit player"));
            Require(e.CompleteCapture(0,0) && !e.CompleteCapture(0,0),"Capture was not exactly once");
            for(int i=0;i<29;i++)e.Advance(ps,_=>throw new Exception("Captured fish attacked"));
            Require(e.Capture().Piranhas[0].RespawnTicks==1,"Wrong refresh timer");
            e.Advance(ps,_=>{});Require(e.Capture().Piranhas[0].RespawnTicks==0 && e.Capture().Captures==1,"No respawn");
        });
        yield return ("evolution_monster_dives_relocates_in_range_and_only_surfaces_for_attacks",()=>{
            var c=new EnemyConfig(piranhaCount:0,initialDelayTicks:0,windupTicks:1,attackIntervalTicks:15);
            var e=new EnemySystem(c);var ps=new[]{Player(0,0,-2000)};
            e.Advance(ps,_=>{});var first=e.Capture().Monster;Require(first.Phase==MonsterPhase.Windup,"No first rise");
            e.Advance(ps,_=>{});Require(e.Capture().Monster.Phase==MonsterPhase.Cooldown && e.Capture().Monster.DiveTicks>0,"Did not dive after firing");
            for(int i=0;i<15;i++)e.Advance(ps,_=>{},(x,z)=>z< -3000);
            var next=e.Capture().Monster;
            Require(next.Phase==MonsterPhase.Windup && (next.X!=first.X || next.Z!=first.Z) && next.Z< -3000,"Did not choose new open water");
            Require(IslandMap.DistanceSquared(next.X,next.Z,ps[0].X,ps[0].Z)<=(long)c.WaterballRange*c.WaterballRange,"Out of attack range");
            var blocked=new EnemySystem(c);for(int i=0;i<50;i++)blocked.Advance(ps,_=>throw new Exception("Attacked from land"),(x,z)=>false);
            Require(blocked.Capture().Shots==0,"No-position fallback fired");
        });
        foreach(var row in new[]{(level:1,distance:4000),(level:2,distance:6000),(level:5,distance:12000),(level:10,distance:22000)})
            yield return ($"evolution_level_{row.level}_waterball_carries_knockback_distance",()=>{
                var c=new EnemyConfig(piranhaCount:0,initialDelayTicks:0,windupTicks:1,monsterLevel:row.level);
                var e=new EnemySystem(c);var ps=new[]{Player(0,0,-4000)};var hits=new List<EnemyHit>();
                for(int i=0;i<20;i++)e.Advance(ps,hits.Add);
                Require(hits.Count==1 && hits[0].KnockbackDistance==row.distance,"Wrong level impulse");
                var p=ps[0];KnockbackSystem.Start(ref p,0,1000,hits[0].KnockbackDistance);
                for(int i=0;i<row.distance*HookGame.Rate/KnockbackSystem.Speed+2;i++)KnockbackSystem.Advance(IslandMap.CreatePlatform(),PlatformRules.InitialTiles().ToArray(),new[]{p},ref p);
                Require(p.Z==-4000+row.distance,"Level distance was not applied");
            });
    }
}

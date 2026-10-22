using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.EnemyTests;
internal static partial class Program
{
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void Throws<T>(Action action) where T:Exception
    {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static int Main(string[] args)
    {
        int passed=0,total=0;
        foreach(var (name,test) in DomainCases().Concat(EvolutionCases()).Concat(PlacementCases()).Concat(GameCases()).Concat(NetworkCases()))
        {
            if(args.Length>0 && !name.Contains(args[0],StringComparison.Ordinal))continue;
            total++;try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){Console.WriteLine("FAIL "+name+": "+e);}
        }
        Console.WriteLine($"{passed}/{total} passed");return total>0 && passed==total?0:1;
    }
    private static Fisher Player(ushort slot,int x,int z,bool water=false)=>new Fisher
    {Slot=slot,X=x,Z=z,Vitals=new CharacterVitals{Health=100,Hunger=100},Swimming=new SwimmingState{InWater=water},PulledBy=-1,HookedPlayer=-1,FishId=-1,DriftwoodId=-1};
    private static IEnumerable<(string,Action)> DomainCases()
    {
        yield return ("piranha_minimum_speed_accumulates_diagonal_movement",()=>{
            var c=new EnemyConfig(piranhaCount:1,monsterEnabled:false,piranhaSpeed:30);var e=new EnemySystem(c);
            var start=e.Capture().Piranhas[0];var ps=new[]{Player(0,start.X+1000,start.Z+1000,true)};
            for(int i=0;i<30;i++)e.Advance(ps,_=>{});var end=e.Capture().Piranhas[0];
            Require(end.X-start.X>=20 && end.Z-start.Z>=20,"Fractional movement was lost");
        });
        yield return ("interrupt_waits_full_configured_delay_after_hook_frame",()=>{
            var c=new EnemyConfig(piranhaCount:0,initialDelayTicks:0,interruptDelayTicks:1);var e=new EnemySystem(c);
            var ps=new[]{Player(0,0,-2000)};e.Advance(ps,_=>{});Require(e.Interrupt(),"No windup");
            e.Advance(ps,_=>{});Require(e.Capture().Monster.Phase==MonsterPhase.Cooldown,"Restarted windup in interruption frame");
            e.Advance(ps,_=>{});Require(e.Capture().Monster.Phase==MonsterPhase.Windup,"Did not resume after delay");
        });
        yield return ("weight_damage_uses_latest_persistent_armor",()=>{
            var c=new EnemyConfig();Require(c.BiteDamage(1500)==5 && c.BiteDamage(3500)==11,"Weight scaling");
            var p=Player(0,0,0);CharacterSystem.Damage(ref p.Vitals,c.BiteDamage(3500),DamageKind.Combat,100);
            Require(p.Vitals.Health==94,"Armor did not reduce enemy damage");
            CharacterSystem.Damage(ref p.Vitals,c.BiteDamage(3500),DamageKind.Combat,100);
            Require(p.Vitals.Health==88,"Defense was consumed");
        });
        yield return ("piranha_selects_water_only_and_stable_slot_then_bite_interval",()=>{
            var c=new EnemyConfig(piranhaCount:1,monsterEnabled:false);var e=new EnemySystem(c);
            var f=e.Capture().Piranhas[0];var players=new[]{Player(0,f.X,f.Z),Player(1,f.X+100,f.Z,true),Player(2,f.X-100,f.Z,true)};
            var hits=new List<EnemyHit>();e.Advance(players,hits.Add);
            Require(hits.Count==1 && hits[0].Slot==1,"Wrong water target/tie break");
            for(int i=0;i<c.BiteIntervalTicks-1;i++)e.Advance(players,hits.Add);
            Require(hits.Count==1,"Bite cooldown ignored");e.Advance(players,hits.Add);Require(hits.Count==2,"Bite did not resume");
            players[1].Vitals.Health=players[2].Vitals.Health=0;
            for(int i=0;i<100;i++)e.Advance(players,hits.Add);Require(hits.Count==2,"Attacked land/downed players");
        });
        yield return ("monster_windup_can_be_interrupted_on_last_tick",()=>{
            var c=new EnemyConfig(piranhaCount:0,initialDelayTicks:0,windupTicks:3);var e=new EnemySystem(c);var players=new[]{Player(0,0,-2000)};
            e.Advance(players,_=>{});Require(e.Capture().Monster.Phase==MonsterPhase.Windup,"Missing windup");
            e.Advance(players,_=>{});e.Advance(players,_=>{});
            Require(e.Interrupt(),"Last windup tick not interruptible");e.Advance(players,_=>{});
            Require(e.Capture().Shots==0 && e.Capture().Interruptions==1,"Interrupted attack still fired");
            Require(!e.Interrupt(),"Cooldown interrupts counted");
        });
        yield return ("waterball_sweep_hits_nearest_once_and_skips_reserved_seats",()=>{
            var c=new EnemyConfig(piranhaCount:0,initialDelayTicks:0,windupTicks:1,waterballSpeed:30000);
            var e=new EnemySystem(c);var ps=new[]{Player(0,0,-4000),Player(1,0,-4500),Player(2,0,-5000)};ps[2].Unavailable=true;ps[2].WaitingToJoin=true;
            var hits=new List<EnemyHit>();for(int i=0;i<8;i++)e.Advance(ps,hits.Add);
            Require(hits.Count==1 && hits[0].Slot==1 && hits[0].Knockback,"Swept nearest hit or reserved seat exclusion failed");
            Require(e.Capture().WaterHits==1,"Projectile hit more than once");
        });
        yield return ("knockback_ten_metres_and_water_entry_ignore_armor",()=>{
            var map=IslandMap.CreatePlatform();var tiles=PlatformRules.InitialTiles().ToArray();var p=Player(0,0,-2000);var ps=new[]{p};
            KnockbackSystem.Start(ref p,0,1000,10000);
            for(int i=0;i<30;i++){KnockbackSystem.Advance(map,tiles,ps,ref p);ps[0]=p;}
            Require(p.X==0 && p.Z==8000 && !p.Knockback.Active && p.Swimming.InWater,"Knockback distance/fall failed");
        });
        yield return ("knockback_stops_at_world_obstacle_without_teleport",()=>{
            var map=IslandMap.CreatePlatform();var p=Player(0,0,49000,true);KnockbackSystem.Start(ref p,0,1000);
            var ps=new[]{p};for(int i=0;i<30;i++){KnockbackSystem.Advance(map,PlatformRules.InitialTiles().ToArray(),ps,ref p);ps[0]=p;}
            Require(p.Z<=50000 && p.Z>=49900 && !p.Knockback.Active,"Obstacle did not stop swept knockback");
        });
    }
}

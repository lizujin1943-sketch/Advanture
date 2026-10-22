using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.EnemyTests;
internal static partial class Program
{
    private static IslandMap Map(EnemyConfig? enemies=null)
    {var m=IslandMap.CreatePlatform();return new IslandMap(m.Shore,m.Pier,m.Spawns,m.FishSpots,m.Platform,new FishingConfig(enemies:enemies??new EnemyConfig()));}
    private static IDeterministicSimulation<HookState> Model(int count=1,EnemyConfig? config=null)
    {
        var m=new HookGame().Create();m.Initialize(new SimulationInitialization(new SessionDescriptor(new Guid("8b2f855d-4cfa-42f2-a934-913985c7302c"),1,30,
            Enumerable.Range(0,count).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray()),new ByteString(Map(config).Encode())));return m;
    }
    private static void Step(IDeterministicSimulation<HookState> model,params HookInput[] commands)
    {
        var s=model.Capture();model.Step(new FrameContext(s.Steps+1,s.Steps+1,30),s.Players.Select(p=>new SimulationInput(
            new Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new ByteString((p.Slot<commands.Length?commands[p.Slot]:default).Encode()))).ToArray());
    }
    private static HookState Replace(HookState s,Fisher[]? players=null,EnemyState? enemies=null)=>
        new HookState(s.Steps,players??s.Players.ToArray(),s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,enemies??s.Enemies);
    private static IEnumerable<(string,Action)> GameCases()
    {
        yield return ("checkpoint_rejects_disabled_enemy_receipts",()=>{
            var m=Model(config:EnemyConfig.Disabled);var s=m.Capture();
            Throws<InvalidDataException>(()=>m.Restore(Replace(s,enemies:new EnemyState(Array.Empty<Piranha>(),default,Array.Empty<Waterball>(),bites:1))));
            Throws<InvalidDataException>(()=>m.Restore(Replace(s,enemies:new EnemyState(Array.Empty<Piranha>(),default,Array.Empty<Waterball>(),serial:1))));
        });
        yield return ("checkpoint_preserves_wrapped_projectile_ids_in_spawn_order",()=>{
            var m=Model(config:new EnemyConfig(piranhaCount:0,initialDelayTicks:0,windupTicks:1,attackIntervalTicks:15,waterballSpeed:1000));
            for(int i=0;i<3;i++)Step(m);var s=m.Capture();var b=s.Enemies.Waterballs[0];b.Id=uint.MaxValue;
            b.Distance=566;b.TravelRemainder=20;
            b.X=b.OriginX+(int)((long)b.DirectionX*b.Distance/1000000);b.Z=b.OriginZ+(int)((long)b.DirectionZ*b.Distance/1000000);
            var monster=s.Enemies.Monster;monster.Phase=MonsterPhase.Windup;monster.Timer=1;monster.Target=0;monster.DirectionZ=1000000;monster.DirectionX=0;monster.DiveTicks=0;
            m.Restore(Replace(s,enemies:new EnemyState(s.Enemies.Piranhas,monster,new[]{b},uint.MaxValue,shots:int.MaxValue)));
            Step(m);var end=m.Capture();Require(end.Enemies.Serial==0 && end.Enemies.Waterballs.Count==2,"Serial did not wrap");
            m.Restore(end);
            var wrong=end.Enemies.Waterballs.Reverse().ToArray();
            Throws<InvalidDataException>(()=>m.Restore(Replace(end,enemies:new EnemyState(end.Enemies.Piranhas,end.Enemies.Monster,wrong,0,shots:int.MaxValue))));
        });
        yield return ("checkpoint_rejects_projectile_age_inconsistent_with_flight_progress",()=>{
            var m=Model(config:new EnemyConfig(piranhaCount:0,initialDelayTicks:0,windupTicks:1,attackIntervalTicks:15,waterballSpeed:1000));
            for(int i=0;i<3;i++)Step(m);var s=m.Capture();var e=s.Enemies;
            Throws<InvalidDataException>(()=>m.Restore(Replace(s,enemies:new EnemyState(e.Piranhas,e.Monster,e.Waterballs,128,shots:128))));
            var b=e.Waterballs[0];b.Id=uint.MaxValue;
            Throws<InvalidDataException>(()=>m.Restore(Replace(s,enemies:new EnemyState(e.Piranhas,e.Monster,new[]{b},1,shots:1))));
        });
        yield return ("checkpoint_rejects_rewound_waterball_serial_before_it_can_duplicate_hits",()=>{
            var m=Model(config:new EnemyConfig(piranhaCount:0,initialDelayTicks:0,windupTicks:1));
            for(int i=0;i<3;i++)Step(m);var s=m.Capture();Require(s.Enemies.Waterballs.Count==1,"Fixture has no flying projectile");
            var e=s.Enemies;var rewind=new EnemyState(e.Piranhas,e.Monster,e.Waterballs,0,e.Bites,e.Shots,e.Interruptions,e.WaterHits);
            Throws<InvalidDataException>(()=>m.Restore(Replace(s,enemies:rewind)));
        });
        yield return ("game_normal_hook_interrupts_monster_without_capture_reward",()=>{
            var m=Model(config:new EnemyConfig(piranhaCount:0,initialDelayTicks:45));
            for(int i=0;i<100;i++)
            {
                var s=m.Capture();var p=s.Players[0];
                Step(m,p.X< -100?new HookInput(1000,0):p.Z> -2400?new HookInput(0,-1000):s.Enemies.Monster.Phase==MonsterPhase.Windup?new HookInput(0,0,0,-6000,true):default);
            }
            var end=m.Capture();Require(end.Enemies.Interruptions>0 && end.Enemies.Shots==0,$"Hook failed: interruptions={end.Enemies.Interruptions} shots={end.Enemies.Shots} pos={end.Enemies.Monster.X},{end.Enemies.Monster.Z} phase={end.Players[0].Phase} casts={end.Players[0].Casts}");
            Require(end.Players[0].Catches==0 && end.Coins==0,"Monster awarded undefined loot");
        });
        yield return ("game_waterball_damage_knockback_blocks_walking_and_causes_fall",()=>{
            var m=Model(config:new EnemyConfig(piranhaCount:0,initialDelayTicks:0,windupTicks:1));
            var start=m.Capture();var ps=start.Players.ToArray();ps[0].X=0;ps[0].Z=1800;m.Restore(Replace(start,ps));
            for(int i=0;i<60 && m.Capture().Enemies.WaterHits==0;i++)Step(m);
            var before=m.Capture().Players[0];Require(before.Vitals.Health==80 && before.Knockback.Active,"No combat damage/knockback");
            while(m.Capture().Players[0].Knockback.Active)Step(m,new HookInput(1000,0));var p=m.Capture().Players[0];
            int distance=HookGame.Sqrt(IslandMap.DistanceSquared(before.X,before.Z,p.X,p.Z));
            Require(Math.Abs(distance-4000)<=2 && p.Swimming.InWater,"Movement changed the 4 m trajectory: "+distance);
        });
        yield return ("game_piranha_chases_fallen_player",()=>{
            var m=Model(config:new EnemyConfig(monsterEnabled:false));
            for(int i=0;i<100;i++)Step(m,i<30?new HookInput(-1000,0):default);
            var s=m.Capture();Require(s.Enemies.Bites>0 && s.Players[0].Vitals.Health<85,"Piranhas ignored falling player");
        });
        yield return ("checkpoint_roundtrip_mid_windup_projectile_and_knockback",()=>{
            var config=new EnemyConfig(initialDelayTicks:0,windupTicks:20);var a=Model(config:config);var b=Model(config:config);var codec=new HookGame();
            var seen=new HashSet<string>();
            for(int i=0;i<230;i++)
            {
                Step(a);var s=a.Capture();
                if(s.Enemies.Monster.Phase==MonsterPhase.Windup)seen.Add("windup");
                if(s.Enemies.Waterballs.Count>0)seen.Add("projectile");
                if(s.Players[0].Knockback.Active)seen.Add("knockback");
                b.Restore(codec.Decode(codec.Encode(s)));Step(a);Step(b);
                Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Restored frame diverged");
            }
            Require(seen.Count==3,"Missing restore phases");
        });
        yield return ("checkpoint_rejects_invalid_enemy_or_knockback_progress",()=>{
            var m=Model();var s=m.Capture();var p=s.Players.ToArray();p[0].Knockback=new KnockbackState{Active=true,Distance=10001,DirectionZ=1000000};
            Throws<InvalidDataException>(()=>m.Restore(Replace(s,p)));
            var fs=s.Enemies.Piranhas.ToArray();fs[0].WeightGrams=-1;
            Throws<InvalidDataException>(()=>m.Restore(Replace(s,enemies:new EnemyState(fs,s.Enemies.Monster,s.Enemies.Waterballs))));
            var different=Model(config:new EnemyConfig(waterballDamage:21));Throws<InvalidDataException>(()=>different.Restore(s));
        });
        yield return ("deactivated_body_remains_attackable_after_restore",()=>{
            var m=Model(config:new EnemyConfig(piranhaCount:0,initialDelayTicks:0));
            m.Step(new FrameContext(1,1,30,new[]{new MemberEvent(1,MemberEventKind.Deactivate,0,1)}),Array.Empty<SimulationInput>());
            var restored=Model(config:new EnemyConfig(piranhaCount:0,initialDelayTicks:0));restored.Restore(m.Capture());
            for(int i=0;i<400;i++)Step(restored);
            Require(restored.Capture().Players[0].Unavailable && restored.Capture().Enemies.Shots>0,"Disconnect granted damage immunity after restore");
        });
        yield return ("waterball_releases_rescue_connections",()=>{
            var m=Model(2);var s=m.Capture();var ps=s.Players.ToArray();
            ps[0].HookedPlayer=1;ps[0].Phase=HookPhase.Returning;ps[0].HookX=ps[1].X;ps[0].HookZ=ps[1].Z;ps[1].PulledBy=0;
            var ball=new Waterball {Id=1,OriginX=0,OriginZ=-6000,X=0,Z=-1467,DirectionX=0,DirectionZ=1000000,Distance=4533,TravelRemainder=10,KnockbackDistance=4000};
            // A well-formed projectile following the monster's original axis reaches slot 0 on this frame.
            ps[0].X=0;ps[0].Z=-1400;ball.X=0;
            var enemies=new EnemyState(s.Enemies.Piranhas,s.Enemies.Monster,new[]{ball},1,shots:1);
            m.Restore(Replace(s,ps,enemies));Step(m);var end=m.Capture();
            Require(end.Players[0].Knockback.Active && end.Players[0].HookedPlayer==-1 && end.Players[1].PulledBy==-1,"Hit retained rescue connection");
        });
    }
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Advanture.Tactical.Network;
using Advanture.HookIsland.Simulation;
using Advanture.Tactical.Simulation;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using static Advanture.Tactical.Tests.SimulationTests;

namespace Advanture.Tactical.Tests;

internal static class CompoundEncounterTests
{
    internal static TacticalMap WithPlayerSpawns(params PlayerSpawn[] spawns)
    {
        var data=File.ReadAllBytes(FortifiedCompoundTests.Asset);
        using var stream=new MemoryStream();
        using var writer=new BinaryWriter(stream);
        writer.Write(data,0,28);
        writer.Write((byte)spawns.Length);
        foreach(var p in spawns){writer.Write(p.X);writer.Write(p.Z);writer.Write(p.Yaw);}
        int tail=29+data[28]*10;
        writer.Write(data,tail,data.Length-tail);
        return TacticalMap.Decode(stream.ToArray());
    }

    internal static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return ("compound.town_monsters_emerge_only_at_breaches_and_reach_plaza",()=>
        {
            var map=TacticalMap.Decode(File.ReadAllBytes(FortifiedCompoundTests.Asset));
            var expected=new[]{(-21250,54000),(35500,2250),(-26500,-56000)};
            Check.True(expected.SequenceEqual(map.MonsterEntrances.Select(p=>(p.X,p.Z))));
            var runner=Runner(map);
            var mirror=Runner(map);
            var generations=new uint[map.SpawnSchedule.Population];
            var origins=new Dictionary<int,(int x,int z)>();
            var observed=new HashSet<(int,int)>();
            var approached=new HashSet<(int,int)>();
            bool attacked=false;
            for(int tick=1;tick<=3400;tick++)
            {
                Step(runner,default);
                var state=runner.InspectState();
                Check.True(state.Monsters.Director.ElapsedTicks>0 && state.Monsters.Director.ElapsedTicks<=(ulong)tick);
                Check.True(state.Monsters.Creatures.Count(c=>c.Alive && !c.IsMinion)<=32);
                foreach(var c in state.Monsters.Creatures)
                {
                    if(c.Generation!=generations[c.Id])
                    {
                        Check.True(tick>=map.SpawnSchedule.InitialWait);
                        Check.Equal(EldritchPhase.Emerging,c.Phase);
                        Check.Equal(EldritchCatalog.EmergenceTicks,c.Timer);
                        Check.True(c.Kind!=EldritchKind.DrownedBell && expected.Contains((c.X,c.Z)));
                        generations[c.Id]=c.Generation;
                        origins[c.Id]=(c.X,c.Z);
                        observed.Add((c.X,c.Z));
                    }
                    if(c.Alive && origins.TryGetValue(c.Id,out var start) &&
                        state.Players.Any(p=>IslandMap.DistanceSquared(p.X,p.Z,c.X,c.Z)<11000L*11000))approached.Add(start);
                }
                attacked |= state.Players.Any(p=>p.Combat.Health<100);
                if(tick==310)mirror.Restore(CheckpointCodec.Decode(CheckpointCodec.Encode(runner.CaptureCheckpoint())));
                if(tick>310 && tick<=510)
                {
                    Step(mirror,default);
                    Check.True(mirror.CaptureCheckpoint().ApplicationState.Span.SequenceEqual(runner.CaptureCheckpoint().ApplicationState.Span));
                }
            }
            Check.True(observed.SetEquals(expected),"Observed breaches: "+string.Join(", ",observed));
            Check.True(approached.SetEquals(expected),"Breach origins reaching the plaza: "+string.Join(", ",approached)+"; "+string.Join(" / ",runner.InspectState().Monsters.Creatures.Where(c=>c.Alive).Select(c=>$"{c.Kind} ({c.X},{c.Z}) towards ({c.GoalX},{c.GoalZ}) target {c.Target}, phase {c.Phase}")));
            Check.True(attacked,"Monsters should reach and attack the plaza players.");
        });
        yield return ("compound.two_udp_clients_receive_the_same_breach_spawns",()=>
        {
            var map=TacticalMap.Decode(File.ReadAllBytes(FortifiedCompoundTests.Asset));
            using var host=new TacticalHost(map,0);
            using var first=new TacticalClient(map,IPAddress.Loopback,host.Port,0);
            using var second=new TacticalClient(map,IPAddress.Loopback,host.Port,1);
            var ports=new HashSet<(int,int)>();
            void Poll(int ms)
            {
                var now=TimeSpan.FromMilliseconds(ms);
                host.Poll(now);
                first.Poll(now,_=>default);
                second.Poll(now,_=>default);
                Check.True(host.Failure==null && first.Failure==null && second.Failure==null);
            }
            for(int ms=0;ms<=20000;ms+=5)
            {
                Poll(ms);
                if(ms%150!=0 || second.Session?.LocalParticipant==null)continue;
                foreach(var c in second.Session.InspectState().Monsters.Creatures)
                    if(c.Phase==EldritchPhase.Emerging)ports.Add((c.X,c.Z));
            }
            for(int i=0;i<100;i++)Poll(20000);
            Check.True(ports.SetEquals(map.MonsterEntrances.Select(p=>(p.X,p.Z))));
            string hash=CheckpointCodec.Hash(host.Session!.CaptureCheckpoint());
            Check.Equal(hash,CheckpointCodec.Hash(first.Session!.CaptureCheckpoint()));
            Check.Equal(hash,CheckpointCodec.Hash(second.Session!.CaptureCheckpoint()));
        });
        yield return ("compound.confirmed_hits_kill_a_monster_once_and_recycle_its_slot",()=>
        {
            var map=TacticalMap.Decode(File.ReadAllBytes(FortifiedCompoundTests.Asset));
            var checkpoint=Runner(map).CaptureCheckpoint();
            var game=new TacticalGame();
            var model=game.Create();
            model.Initialize(new SimulationInitialization(checkpoint.Session,checkpoint.InitializationData));
            var initial=model.Capture();
            var roster=initial.Monsters.Creatures.ToArray();
            // Place a town creature on the unobstructed line between the two plaza spawns.
            var creature=roster[0];
            creature.Generation=1;
            creature.Phase=EldritchPhase.Emerging;
            creature.Timer=EldritchCatalog.EmergenceTicks;
            creature.DirectionZ=1000000;
            creature.X=-5250;creature.Z=7400;
            creature.Health=EldritchCatalog.MaxHealth(creature);
            roster[0]=creature;
            model.Restore(new TacticalState(0,initial.Players,new EldritchState(roster)));
            ulong tick=0;
            void Advance(TacticalInput first)
            {
                tick++;
                model.Step(new FrameContext(tick,tick,30),new[]{
                    new SimulationInput(checkpoint.Session.Participants[0],new ByteString(first.Encode())),
                    new SimulationInput(checkpoint.Session.Participants[1],new ByteString(default(TacticalInput).Encode()))});
            }
            for(int i=0;i<7;i++)Advance(new TacticalInput(0,0,9000,0,true,fire:true));
            var result=game.Decode(game.Encode(model.Capture()));
            Check.Equal(0,result.Players[0].Combat.LastHitCreature);
            Check.Equal(-1,result.Players[0].Combat.LastHitSlot);
            Check.Equal(3u,result.Players[0].Combat.Hits);
            Check.Equal(1u,result.Players[0].Combat.Kills);
            Check.Equal(100,result.Players[1].Combat.Health);
            Check.Equal(EldritchPhase.Dead,result.Monsters.Creatures[0].Phase);
            Check.True(result.Monsters.Creatures[0].Rewarded);
            model.Restore(result);
            for(int i=0;i<TownDirectorRules.CorpseTicks;i++)Advance(default);
            var recycled=model.Capture().Monsters.Creatures[0];
            Check.True(recycled.Phase==EldritchPhase.Dormant || recycled.Generation>1);
            Check.Equal(1u,model.Capture().Players[0].Combat.Kills);
        });
        yield return ("compound.guarded_breach_is_skipped_and_visible_ports_defer_without_spending",()=>
        {
            var map=WithPlayerSpawns(new PlayerSpawn(-21250,54000,0),new PlayerSpawn(-2250,7400,0));
            var runner=Runner(map);
            for(int i=0;i<310;i++)
            {
                Step(runner,default);
                Check.True(runner.InspectState().Monsters.Creatures.All(c=>c.Phase!=EldritchPhase.Emerging || c.X!=-21250 || c.Z!=54000));
            }
            Check.True(runner.InspectState().Monsters.Creatures.Any(c=>c.Generation>0));
            // Use one real breach so both a nearby observer and an unobstructed observer individually block every port.
            var data=File.ReadAllBytes(FortifiedCompoundTests.Asset);
            int table=31+data[28]*10;
            Check.Equal(3,(int)data[table]);
            data[table]=1;
            data=data.Take(table+9).Concat(data.Skip(table+25)).ToArray();
            foreach(var observer in new[]{(x:-21250,z:54000),(x:-21000,z:44875)})
            {
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(29),observer.x);
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(33),observer.z);
                var blocked=Runner(TacticalMap.Decode(data));
                for(int i=0;i<360;i++)Step(blocked,default);
                var roster=blocked.InspectState().Monsters;
                Check.True(roster.Creatures.All(c=>c.Generation==0),"Unexpected spawn near "+observer);
                Check.Equal(Math.Min(map.SpawnSchedule.MaxCredits*HookGame.Rate,map.SpawnSchedule.InitialCredits*HookGame.Rate+360*map.SpawnSchedule.Income*8/5),roster.Director.CreditUnits);
            }
        });
    }
}

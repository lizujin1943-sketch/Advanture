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
        yield return ("compound.four_udp_clients_share_breaches_and_reject_fifth_player",()=>
        {
            var map=TacticalMap.Decode(File.ReadAllBytes(FortifiedCompoundTests.Asset));
            using var host=new TacticalHost(map,0);
            using var owner=new TacticalClient(map,IPAddress.Loopback,host.Port,0);
            using var guestA=new TacticalClient(map,IPAddress.Loopback,host.Port);
            using var guestB=new TacticalClient(map,IPAddress.Loopback,host.Port);
            using var guestC=new TacticalClient(map,IPAddress.Loopback,host.Port);
            var clients=new[]{owner,guestA,guestB,guestC};
            var ports=new HashSet<(int,int)>();
            Check.Equal(4,host.Capacity);
            void Poll(int ms)
            {
                var now=TimeSpan.FromMilliseconds(ms);
                host.Poll(now);
                foreach (var client in clients)
                {
                    client.Poll(now, tick => new TacticalInput(0,0,jump:tick==7,fire:tick==21));
                    Check.True(client.Failure==null,client.Failure ?? "");
                }
                Check.True(host.Failure==null,host.Failure ?? "");
            }
            for(int ms=0;ms<=20000;ms+=5)
            {
                Poll(ms);
                if(ms%150!=0 || guestC.Session?.LocalParticipant==null)continue;
                foreach(var c in guestC.Session.InspectState().Monsters.Creatures)
                    if(c.Phase==EldritchPhase.Emerging)ports.Add((c.X,c.Z));
            }
            using var fifth=new TacticalClient(map,IPAddress.Loopback,host.Port);
            for(int ms=20000;ms<=21000;ms+=5)
            {
                Poll(ms);
                fifth.Poll(TimeSpan.FromMilliseconds(ms),_=>default);
            }
            Check.True(fifth.Session==null && fifth.Failure?.Contains("房间已满")==true);
            for(int i=0;i<100;i++)Poll(21000);
            Check.True(ports.SetEquals(map.MonsterEntrances.Select(p=>(p.X,p.Z))));
            Check.True(clients.Select(c=>(int)c.Session!.LocalParticipant!.Slot).OrderBy(slot=>slot).SequenceEqual(new[]{0,1,2,3}));
            Check.Equal((ushort)0,owner.Session!.LocalParticipant!.Slot);
            Check.Equal(4,host.JoinedCount);
            var state=host.Session!.InspectState();
            Check.True(state.Players.Count==4 && state.Players.All(p=>p.Combat.ShotSequence==1 && p.Combat.Ammo==29));
            string hash=CheckpointCodec.Hash(host.Session!.CaptureCheckpoint());
            foreach(var client in clients)Check.Equal(hash,CheckpointCodec.Hash(client.Session!.CaptureCheckpoint()));
        });
        foreach (int partySize in new[]{2,3})
            yield return ($"compound.owner_starts_with_{partySize}_after_lobby_departure", () =>
            {
                var map=TacticalMap.Decode(File.ReadAllBytes(FortifiedCompoundTests.Asset));
                using var host=new TacticalHost(map,0);
                using var departing=new TacticalClient(map,IPAddress.Loopback,host.Port);
                var clients=new List<TacticalClient>{departing};
                int ms=0;
                void Advance(int duration)
                {
                    int end=ms+duration;
                    while(ms<end)
                    {
                        ms+=5;
                        var now=TimeSpan.FromMilliseconds(ms);
                        host.Poll(now);
                        foreach(var client in clients)
                        {
                            client.Poll(now,_=>default);
                            Check.True(client.Failure==null,client.Failure ?? "");
                        }
                    }
                }
                Advance(200);
                using var friend=new TacticalClient(map,IPAddress.Loopback,host.Port);
                clients.Add(friend);
                Advance(200);
                Check.Equal(2,host.JoinedCount);
                Check.True(!host.TryStart(),"Only a room with its owner may start");
                clients.Remove(departing);
                departing.Dispose();
                Advance(12000);
                Check.Equal(1,host.JoinedCount);
                using var owner=new TacticalClient(map,IPAddress.Loopback,host.Port,0);
                clients.Add(owner);
                using var replacement=partySize==3 ? new TacticalClient(map,IPAddress.Loopback,host.Port) : null;
                if(replacement!=null)clients.Add(replacement);
                Advance(250);
                Check.True(host.Session==null && host.CanStart && host.TryStart());
                Check.True(!host.TryStart());
                Advance(2500);
                var players=host.Session!.InspectState().Players;
                Check.True(players.Select(p=>(int)p.Slot).SequenceEqual(partySize==2 ? new[]{0,2} : new[]{0,1,2}));
                Check.Equal((ushort)2,friend.Session!.LocalParticipant!.Slot);
                if(replacement!=null)Check.Equal((ushort)1,replacement.Session!.LocalParticipant!.Slot);
                using var late=new TacticalClient(map,IPAddress.Loopback,host.Port);
                for(int i=0;i<100;i++){Advance(5);late.Poll(TimeSpan.FromMilliseconds(ms),_=>default);}
                Check.True(late.Session==null && late.Failure?.Contains("已经开局")==true);
                for(int i=0;i<50;i++)
                {
                    var now=TimeSpan.FromMilliseconds(ms);
                    host.Poll(now);
                    foreach(var client in clients)client.Poll(now,_=>default);
                }
                var hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach(var client in clients)Check.Equal(hash,CheckpointCodec.Hash(client.Session!.CaptureCheckpoint()));
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

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using Advanture.Tactical.Simulation;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using static Advanture.Tactical.Tests.SimulationTests;

namespace Advanture.Tactical.Tests;

internal static class CompoundDefenseTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("compound.solo_and_coop_waves_fill_the_arena", () =>
        {
            var map = TacticalMap.Decode(File.ReadAllBytes(FortifiedCompoundTests.Asset));
            var setup = Runner(map).CaptureCheckpoint();
            foreach (int players in new[] { 1, 2 })
            {
                var roster = setup.Session.Participants.Take(players).ToArray();
                var session = new SessionDescriptor(Id,1,30,roster);
                var model = new TacticalGame().Create();
                model.Initialize(new SimulationInitialization(session,setup.InitializationData));
                var neutral = new ByteString(default(TacticalInput).Encode());
                var inputs = roster.Select(p => new SimulationInput(p,neutral)).ToArray();
                var births = new List<int>();
                int peak = 0, first = players==1 ? 12 : 16, limit = players==1 ? 24 : 32;
                for (int tick=1;tick<=2100;tick++)
                {
                    model.Step(new FrameContext((ulong)tick,(ulong)tick,30),inputs);
                    var state = model.Capture().Monsters;
                    int adults = state.Creatures.Count(c=>c.Damageable && !c.IsMinion);
                    int generated = state.Creatures.Where(c=>!c.IsMinion).Sum(c=>(int)c.Generation);
                    if (generated>births.Count) births.Add(tick);
                    Check.Equal(generated,births.Count);
                    peak = Math.Max(peak,adults);
                    Check.True(adults<=limit);
                }
                Check.True(births.Count>first);
                Check.Equal(45,births[0]);
                Check.True(births[first-1]<=30*(players==1?20:25),"First wave last arrival: "+births[first-1]);
                Check.True(births[first]-births[first-1]>=180,"Missing inter-wave rest.");
                Check.Equal(limit,peak);
                Console.WriteLine($"Compound {players}P first wave {first} by tick {births[first-1]}, next at {births[first]}, peak {peak} adults.");
            }
        });
        yield return ("compound.both_players_walk_from_plaza_and_resupply_at_different_stores", () =>
        {
            var map = TacticalMap.Decode(File.ReadAllBytes(FortifiedCompoundTests.Asset));
            Check.True(map.AmmoDepots.Select(p=>(p.X,p.Z)).SequenceEqual(new[]{(-29500,-6250),(-16250,30750)}));
            var routes = new[] {
                new[]{(-9625,-14625),(-10125,-14625),(-29375,-7375),(-29500,-6250)},
                new[]{(9875,18125),(11125,17875),(15125,4625),(31375,4625),(31875,6125),(31875,11375),
                      (27625,18125),(8875,25875),(-16125,26125),(-16250,30750)}
            };
            var runner = Runner(map);
            var next = new int[2];
            var supplied = new bool[2];
            for(int slot=0;slot<2;slot++)Check.Equal(360,runner.InspectState().Players[slot].Combat.Reserve);
            // Spend ammunition and reload on the plaza, then fire once so the two counters differ.
            var shot = new TacticalInput(0,0,0,-8500,true,fire:true);
            Step(runner,shot,shot);
            var reload = new TacticalInput(0,0,reload:true);
            Step(runner,reload,reload);
            for(int i=0;i<72;i++)Step(runner,default);
            Step(runner,shot,shot);
            var use = new TacticalInput(0,0,resupply:true);
            Step(runner,use,use);
            Check.True(runner.InspectState().Players.All(p=>p.Combat.Reserve==359 && p.Combat.Ammo==29));
            for(int tick=0;tick<1000 && !supplied.All(x=>x);tick++)
            {
                var inputs = new TacticalInput[2];
                foreach(var p in runner.InspectState().Players)
                {
                    int slot=p.Slot;
                    if(supplied[slot])continue;
                    if(map.NearbyAmmoDepot(p)==slot)
                    {
                        inputs[slot]=use;
                        supplied[slot]=true;
                        continue;
                    }
                    var route=routes[slot];
                    while (next[slot]<route.Length && IslandMap.DistanceSquared(p.X,p.Z,route[next[slot]].Item1,route[next[slot]].Item2)<150L*150)
                        next[slot]++;
                    Check.True(next[slot]<route.Length,"Reached store but could not interact");
                    var (x,z)=route[next[slot]];
                    int dx=x-p.X,dz=z-p.Z;
                    double length=Math.Sqrt((double)dx*dx+(double)dz*dz);
                    inputs[slot]=new TacticalInput((short)(1000*dx/length),(short)(1000*dz/length),sprint:length>450);
                }
                Step(runner,inputs[0],inputs[1]);
                foreach(var p in runner.InspectState().Players)
                {
                    Check.Equal(supplied[p.Slot]?360:359,p.Combat.Reserve);
                    Check.Equal(29,p.Combat.Ammo);
                }
            }
            Check.True(supplied.All(x=>x),"Arrival waypoints "+string.Join(",",next)+" at "+string.Join(" / ",runner.InspectState().Players.Select(p=>$"({p.X},{p.Z})")));
            Step(runner,use,use);
            Check.True(runner.InspectState().Players.All(p=>p.Combat.Reserve==360 && p.Combat.Ammo==29));
        });
        yield return ("compound.airborne_use_does_not_queue_and_resupply_replays_once", () =>
        {
            var map = CompoundEncounterTests.WithPlayerSpawns(new PlayerSpawn(-29500,-6250,0),new PlayerSpawn(-2250,7400,0));
            var runner=Runner(map);
            Step(runner,new TacticalInput(0,0,fire:true));
            Step(runner,new TacticalInput(0,0,reload:true));
            for(int i=0;i<72;i++)Step(runner,default);
            Step(runner,new TacticalInput(0,0,jump:true,resupply:true));
            for(int i=0;i<30;i++)Step(runner,default);
            Check.Equal(359,Player(runner).Combat.Reserve);
            var checkpoint=runner.CaptureCheckpoint();
            var copy=Runner(map);
            copy.Restore(CheckpointCodec.Decode(CheckpointCodec.Encode(checkpoint)));
            for(int i=0;i<3;i++)
            {
                var input=new TacticalInput(0,0,resupply:true);
                Step(runner,input);Step(copy,input);
                Check.Equal(360,Player(runner).Combat.Reserve);
                Check.Equal(CheckpointCodec.Hash(runner.CaptureCheckpoint()),CheckpointCodec.Hash(copy.CaptureCheckpoint()));
            }
        });
        yield return ("compound.solid_cover_blocks_a_nearby_ammo_depot", () =>
        {
            var map=CompoundEncounterTests.WithPlayerSpawns(new PlayerSpawn(-29500,-7250,0),new PlayerSpawn(-2250,7400,0));
            Check.Equal(0,map.NearbyAmmoDepot(Player(Runner(map))));
            // Insert a thin wall between the player and the case, keeping both endpoints walkable.
            var bytes=map.Encode();
            int countOffset=29+bytes[28]*10;
            var wall=new byte[24];
            var bounds=new[]{-31000,-6850,-28000,-6750,0,3000};
            for(int i=0;i<bounds.Length;i++)BinaryPrimitives.WriteInt32LittleEndian(wall.AsSpan(i*4),bounds[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(countOffset),1);
            var blocked=TacticalMap.Decode(bytes.Take(countOffset+2).Concat(wall).Concat(bytes.Skip(countOffset+2)).ToArray());
            Check.Equal(-1,blocked.NearbyAmmoDepot(Player(Runner(blocked))));
        });
        yield return ("compound.legacy_breach_map_and_roster_still_roundtrip", () =>
        {
            var bytes = File.ReadAllBytes(FortifiedCompoundTests.Asset);
            int ports = 31 + bytes[28]*10;
            int depots = ports+1+bytes[ports]*8;
            var noStores = bytes.Take(depots).Append((byte)0).Concat(bytes.Skip(depots+1+bytes[depots]*8)).ToArray();
            var stillDefense = TacticalMap.Decode(noStores);
            Check.True(stillDefense.HasDefenseWaves && stillDefense.AmmoDepots.Count==0 && stillDefense.Encode().SequenceEqual(noStores));
            var old = bytes.Take(depots).Concat(bytes.Skip(depots+1+bytes[depots]*8)).ToArray();
            old[3]=(byte)'5';
            var map=TacticalMap.Decode(old);
            Check.True(map.Encode().SequenceEqual(old) && map.AmmoDepots.Count==0);
            var runner=Runner(map);
            for(int i=0;i<200;i++)Step(runner,default);
            var codec=new TacticalGame();
            var state=codec.Encode(runner.InspectState());
            Check.Equal((byte)'4',state[3]);
            Check.Equal(TownEncounters.Population,codec.Decode(state).Monsters.Creatures.Count);
            Check.True(codec.Encode(codec.Decode(state)).SequenceEqual(state));
            var duplicate=(byte[])bytes.Clone();
            BinaryPrimitives.WriteInt64LittleEndian(duplicate.AsSpan(depots+9),BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(depots+1)));
            Check.Throws(()=>TacticalMap.Decode(duplicate));
        });
    }
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.Tactical.Simulation;

namespace Advanture.Tactical.Tests;

internal static class FortifiedCompoundTests
{
    private const string Asset = "samples/TacticalGame.Unity/Assets/TacticalGame/Art/FortifiedCompound/FortifiedCompound.bytes";

    internal static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return ("compound.both_plaza_spawns_roundtrip_and_reject_truncation", () =>
        {
            var bytes = File.ReadAllBytes(Asset);
            var map = TacticalMap.Decode(bytes);
            Check.True(map.HasElevation && map.Encode().SequenceEqual(bytes));
            Check.Equal(2, map.Spawns.Count);
            Check.Equal(6000, map.Spawns[1].X-map.Spawns[0].X);
            foreach (var s in map.Spawns) Check.Equal(2050, map.GroundHeight(s.X,s.Z));
            Check.Throws(()=>TacticalMap.Decode(bytes.AsSpan(0,bytes.Length-2)));
        });
        yield return ("compound.walk_down_plaza_stairs_and_respawn_on_paving", () =>
        {
            var map = TacticalMap.Decode(File.ReadAllBytes(Asset));
            var runner = SimulationTests.Runner(map);
            var start = map.Spawns[0];
            int last = 2050;
            for (int i=0;i<165;i++)
            {
                SimulationTests.Step(runner,new TacticalInput(0,-1000));
                var p = SimulationTests.Player(runner);
                int feet = map.GroundHeight(p.X,p.Z);
                Check.True(Math.Abs(feet-last)<=220);
                last = feet;
            }
            var yard = SimulationTests.Player(runner);
            Check.Equal(start.Z-165*150,yard.Z);
            Check.Equal(0,last);
            SimulationTests.Step(runner,new TacticalInput(0,0,respawn:true));
            var back = SimulationTests.Player(runner);
            Check.Equal(start.X,back.X); Check.Equal(start.Z,back.Z);
            Check.Equal(2050,map.GroundHeight(back.X,back.Z));
        });
        yield return ("compound.all_three_stairs_connect_to_roof_level", () =>
        {
            var map = TacticalMap.Decode(File.ReadAllBytes(Asset));
            void CheckRoute(params (double x, double z)[] corners)
            {
                int previous = map.GroundHeight((int)(1000*corners[0].x),(int)(1000*corners[0].z));
                for (int i=1;i<corners.Length;i++)
                {
                    var a=corners[i-1]; var b=corners[i];
                    double dx=b.x-a.x,dz=b.z-a.z;
                    int steps=(int)Math.Ceiling(Math.Sqrt(dx*dx+dz*dz)/.1);
                    for(int j=0;j<=steps;j++)
                    {
                        int x=(int)Math.Round(1000*(a.x+dx*j/steps)),z=(int)Math.Round(1000*(a.z+dz*j/steps));
                        Check.True(map.CanStand(x,z),"Blocked stair at "+x+","+z);
                        int height=map.GroundHeight(x,z);
                        Check.True(Math.Abs(height-previous)<=420,"Disconnected stair at "+x+","+z);
                        previous=height;
                    }
                }
                Check.True(previous>5000);
            }
            CheckRoute((.50,41.30),(8.1,41.30),(8.1,44.3));
            CheckRoute((19.6,6.2),(30.45,6.2),(30.45,10.6),(26.5,10.6));
            CheckRoute((16.85,-49.65),(12.35,-40.75),(2.125,-40.75),(2.125,-44.0));
        });
        yield return ("compound.raised_players_and_ground_use_world_height", () =>
        {
            var map = TacticalMap.Decode(File.ReadAllBytes(Asset));
            var runner = SimulationTests.Runner(map);
            SimulationTests.Step(runner,new TacticalInput(0,0,9000,0,true,fire:true));
            var shot=SimulationTests.Player(runner).Combat;
            Check.Equal(3700,shot.ShotY);
            Check.Equal(75,runner.InspectState().Players[1].Combat.Health);
            for(int i=0;i<4;i++)SimulationTests.Step(runner,default);
            SimulationTests.Step(runner,new TacticalInput(0,0,0,7000,true,fire:true));
            Check.True(Math.Abs(SimulationTests.Player(runner).Combat.EndY-2050)<=1);
        });
        yield return ("compound.low_tick_rate_cannot_cross_plaza_drop", () =>
        {
            var map = TacticalMap.Decode(File.ReadAllBytes(Asset));
            var runner = SimulationTests.Runner(map,1);
            for(int i=0;i<5;i++)SimulationTests.Step(runner,new TacticalInput(1000,0,sprint:true));
            var p=SimulationTests.Player(runner);
            Check.True(p.X<10500 && p.X>8000);
            Check.Equal(2050,map.GroundHeight(p.X,p.Z));
        });
        yield return ("compound.jump_over_plaza_wall_and_fall_to_yard", () =>
        {
            var map = TacticalMap.Decode(File.ReadAllBytes(Asset));
            var runner = SimulationTests.Runner(map);
            for (int i=0;i<90;i++) SimulationTests.Step(runner,new TacticalInput(1000,0,sprint:true));
            var atWall = SimulationTests.Player(runner);
            Check.Equal(9749, atWall.X);
            Check.Equal(2050, map.FeetHeight(atWall));
            int peak=0;
            bool aboveLedge=false, fellPastPlaza=false;
            for (int i=0;i<36;i++)
            {
                SimulationTests.Step(runner,new TacticalInput(1000,0,sprint:true,jump:i==0));
                var p = SimulationTests.Player(runner);
                int ground = map.GroundHeight(p.X,p.Z), feet = map.FeetHeight(p);
                peak = Math.Max(peak,feet);
                aboveLedge |= ground==2700 && feet>ground;
                fellPastPlaza |= p.X>10750 && p.IsAirborne && feet<2050;
            }
            var yard = SimulationTests.Player(runner);
            Check.True(aboveLedge && fellPastPlaza);
            Check.Equal(3050,peak);
            Check.True(yard.X>13000 && !yard.IsAirborne && map.FeetHeight(yard)<2050);
        });
        yield return ("compound.low_ceiling_stops_jump_then_player_lands", () =>
        {
            // Relocate the spawn beneath the existing overhead geometry in the baked map.
            var bytes = File.ReadAllBytes(Asset);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(29),18375);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(33),7125);
            var runner = SimulationTests.Runner(TacticalMap.Decode(bytes));
            int peak=0;
            for (int i=0;i<20;i++)
            {
                SimulationTests.Step(runner,new TacticalInput(0,0,jump:i==0));
                peak=Math.Max(peak,SimulationTests.Player(runner).HeightAboveGround);
            }
            Check.Equal(180,peak);
            Check.True(!SimulationTests.Player(runner).IsAirborne);
        });
    }
}

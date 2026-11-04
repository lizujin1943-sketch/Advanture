using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.CombatSpacingTests;

internal static partial class Program
{
    private static IEnumerable<(string,Action)> NetworkCases()
    {
        yield return ("four_udp_clients_crowd_with_crabs_keep_identical_checkpoints",()=>{
            var map=Map(new CrabConfig(initialDelayTicks:30,count:3,spawnStaggerTicks:30,damage:1,knockback:1));
            using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan now=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(35))
                {
                    now=clock.Elapsed;host.Poll(now);
                    foreach(var client in clients)
                    {
                        client.Poll(now,_=>{
                            if(client.Session?.LocalParticipant==null)return default;
                            var p=client.Session.InspectState().Players[client.Slot];
                            int dx=-1400-p.X,dz=-1400-p.Z,distance=HookGame.Sqrt((long)dx*dx+(long)dz*dz);
                            return distance==0?default:new HookInput((short)(dx*1000/(distance+1)),(short)(dz*1000/(distance+1)));
                        });
                        Require(client.Failure==null,client.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session?.InspectState().Steps>=240)break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session!=null && host.Session.InspectState().Steps>=240,"Four-client spacing run timed out");
                for(int i=0;i<300;i++){host.Poll(now);foreach(var client in clients)client.Poll(now,_=>default);}
                var state=host.Session!.InspectState();
                Separate(state.Players.Select(p=>new Point(p.X,p.Z)).ToArray(),EnemyGeometry.PlayerRadius*2);
                Require(state.Crabs.All(c=>c.Generation==1 && c.Health==100),"Crab crowd did not use the real population");
                Separate(state.Crabs.Select(c=>new Point(c.X,c.Z)).ToArray(),CrabConfig.BodyRadius*2);
                foreach(var crab in state.Crabs)foreach(var p in state.Players)
                    Require(IslandMap.DistanceSquared(crab.X,crab.Z,p.X,p.Z)>=728L*728,"Crab and player overlap on a confirmed network frame");
                var checkpoint=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());
                foreach(var client in clients)Require(client.Session!=null && checkpoint.SequenceEqual(CheckpointCodec.Encode(client.Session.CaptureCheckpoint())),"Confirmed crowd checkpoint differs");
                var restored=Model(map);restored.Restore(Codec.Decode(Codec.Encode(state)));
                Require(Codec.Encode(restored.Capture()).SequenceEqual(Codec.Encode(state)),"Crowd checkpoint failed state restore");
                Console.WriteLine($"SPACING_UDP clients=4 tick={state.Steps} crabs={state.Crabs.Count} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var client in clients)client.Dispose();}
        });
    }
}

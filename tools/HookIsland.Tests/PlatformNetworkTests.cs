using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> PlatformNetworkCases()
    {
        yield return ("platform_four_udp_clients_share_tiles_but_spend_only_personal_materials",()=>{
            var map=LegacyPlatformMap();using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(100))
                {
                    var now=last=clock.Elapsed;host.Poll(now);
                    foreach(var client in clients)
                    {
                        client.Poll(now,t=>PlatformSmokePilot.Sample(map,client.Session!.InspectState(),client.Slot,t));
                        Require(client.Failure==null,client.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && PlatformSmokePilot.Complete(host.Session.InspectState()))break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<300;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                var s=host.Session!.InspectState();Require(PlatformSmokePilot.Complete(s),"UDP platform collection/build/traversal did not finish: "+
                    string.Join("; ",s.Players.Select(p=>$"P{p.Slot} wood={p.WoodEarned} bones={p.FishbonesEarned} ({p.X},{p.Z}) build={p.LastBuildResult}")));
                Require(s.Tiles.Count==17 && s.Players.Sum(p=>p.WoodEarned-p.Wood)==16 && s.Players.Sum(p=>p.FishbonesEarned-p.Fishbones)==16,"Network build duplicated or lost materials.");
                string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash,"Platform authority/client states differ.");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        yield return ("platform_clients_with_different_recipes_cannot_join_same_room",()=>{
            using var host=new IslandHost(LegacyPlatformMap(4,4),0,2);
            using var client=new IslandClient(LegacyPlatformMap(5,4),IPAddress.Loopback,host.Port,ushort.MaxValue);
            for(int i=0;i<1000 && client.Failure==null;i++)
            {var now=TimeSpan.FromMilliseconds(i*5);host.Poll(now);client.Poll(now,_=>default);}
            Require(client.Failure!=null && host.JoinedCount==0,"Mismatched construction costs were accepted.");
        });
    }
}

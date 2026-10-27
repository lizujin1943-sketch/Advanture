using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> ReelingMovementNetworkCases()
    {
        foreach(int count in new[]{1,4})yield return ($"reeling_move_real_udp_{count}_clients",()=>{
            var map=CooperativeFishingSmokePilot.CreateMap();using var host=new IslandHost(map,0,count);
            var clients=Enumerable.Range(0,count).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=clients.Select(_=>new CooperativeFishingSmokePilot()).ToArray();
            var sent=new int[count,2];var moved=new bool[Math.Min(count,2),2];HookState? previous=null;
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan now=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(120))
                {
                    now=clock.Elapsed;host.Poll(now);
                    for(int i=0;i<count;i++)
                    {
                        var client=clients[i];var pilot=pilots[i];int index=i;
                        client.Poll(now,t=>{
                            if(client.Session?.LocalParticipant==null)return default;
                            var s=client.Session.InspectState();var p=s.Players[client.Slot];
                            var input=pilot.Sample(map,s,client.Slot,t);int kind=p.HookedPiranha>0?1:0;
                            // A short sideways walk after the group joins leaves the pilot's normal
                            // casting/stealing scenario intact. Only real input commands are submitted.
                            if(CooperativeFishing.Attached(p) && CooperativeFishing.Count(s.Players,p)>=Math.Min(count,2) && sent[index,kind]<3)
                            {sent[index,kind]++;return new HookInput(0,1000);}
                            return input;
                        });
                        Require(client.Failure==null,client.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");if(host.Session==null)continue;
                    var current=host.Session.InspectState();
                    if(previous!=null && current.Steps==previous.Steps+1)
                        for(int i=0;i<Math.Min(count,2);i++)
                        {
                            var a=previous.Players[i];var b=current.Players[i];
                            if(CooperativeFishing.Attached(a) && b.Z-a.Z==30 && b.X==a.X)moved[i,a.HookedPiranha>0?1:0]=true;
                        }
                    previous=current;
                    if(pilots.All(p=>p.Complete(current)))break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session!=null && pilots.All(p=>p.Complete(host.Session.InspectState())),"Normal catch/steal/piranha flow did not complete while moving.");
                for(int i=0;i<Math.Min(count,2);i++)for(int kind=0;kind<2;kind++)Require(moved[i,kind],$"Player {i} never moved at 0.9 m/s with target kind {kind} attached.");
                for(int i=0;i<200;i++){host.Poll(now);foreach(var client in clients)client.Poll(now,_=>default);}
                string hash=CheckpointCodec.Hash(host.Session!.CaptureCheckpoint());
                foreach(var client in clients)Require(client.Session!=null && CheckpointCodec.Hash(client.Session.CaptureCheckpoint())==hash,"Moving while reeling diverged between clients.");
                Console.WriteLine($"REELING_MOVE_UDP players={count} tick={host.Session.ConfirmedTick} hash={hash}");
            }
            finally{foreach(var client in clients)client.Dispose();}
        });
    }
}

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
    private static IEnumerable<(string,Action)> RodNetworkCases()
    {
        yield return ("rod_four_udp_clients_buy_with_personal_income_and_agree",()=>{
            var map=IslandMap.CreatePlatform();using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=Enumerable.Range(0,4).Select(_=>new Advanture.HookIsland.RodSmokePilot()).ToArray();
            var complete=new Advanture.HookIsland.RodSmokePilot();var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(150))
                {
                    var now=last=clock.Elapsed;host.Poll(now);
                    for(int i=0;i<clients.Length;i++)
                    {
                        int index=i;var c=clients[i];
                        c.Poll(now,t=>c.Session?.LocalParticipant==null?default:pilots[index].Sample(map,c.Session.InspectState(),c.Slot,t));
                        Require(c.Failure==null,c.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && complete.Complete(host.Session.InspectState()))break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<200;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                var s=host.Session!.InspectState();
                Require(complete.Complete(s),"Four clients failed to earn, buy, equip and catch again: "+string.Join(";",s.Players.Select(p=>$"P{p.Slot} {p.Coins}/{p.CoinsEarned} G, {p.Catches} fish, rod {p.EquippedRod}")));
                Require(s.Coins==s.Players.Sum(p=>p.Coins),"Balances do not sum to diagnostic total.");
                string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash,"Rod/stamina/inventory checkpoints differ.");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        yield return ("rod_lobby_rejects_different_stamina_configuration",()=>{
            var map=IslandMap.CreatePlatform();using var host=new IslandHost(map,0,2);
            var different=new IslandMap(map.Shore,map.Pier,map.Spawns,map.FishSpots,map.Platform,new FishingConfig(regenDelayTicks:90));
            using var c=new IslandClient(different,IPAddress.Loopback,host.Port,ushort.MaxValue);
            for(int ms=0;ms<1500 && c.Failure==null;ms+=2){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);c.Poll(now,_=>default);}
            Require(c.Failure!=null && host.Session==null,"Different stamina rules entered the same room.");
        });
    }
}

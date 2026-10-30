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
    private static IEnumerable<(string,Action)> ComboNetworkCases()
    {
        yield return ("combo_four_udp_clients_confirm_same_reward_count_and_checkpoint",()=>{
            var basis=CooperativeFishingSmokePilot.CreateMap();
            // Short scenario with a configurable first-catch bonus; default 3/10 thresholds have simulation coverage.
            var map=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,
                new FishingConfig(reelSpeed:600,enemies:basis.Fishing!.Enemies,combo:new ComboConfig(goldBonusPercent:new[]{50})));
            using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=clients.Select(_=>new CooperativeFishingSmokePilot()).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan now=default;int baseValue=0;
            var woodEarned=new int[4];var woodCatches=new int[4];
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(60))
                {
                    now=clock.Elapsed;host.Poll(now);
                    for(int i=0;i<clients.Length;i++)
                    {
                        var client=clients[i];var pilot=pilots[i];
                        client.Poll(now,t=>client.Session?.LocalParticipant==null?default:
                            client.Session.InspectState().Players.Any(p=>p.Catches>0)?default:pilot.Sample(map,client.Session.InspectState(),client.Slot,t));
                        Require(client.Failure==null,client.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null)
                    {
                        var s=host.Session.InspectState();
                        for(int i=0;i<4;i++)if(s.Players[i].WoodEarned>woodEarned[i])
                        {woodCatches[i]++;woodEarned[i]=s.Players[i].WoodEarned;}
                        if(s.Fish[0].Owner>=0)baseValue=FishCatalog.Coins(s.Fish[0]);
                        if(s.Players[0].Catches==1 && s.Players[1].Catches==1)break;
                    }
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session!=null && baseValue>0,"Network scenario never caught a fish.");
                for(int i=0;i<200;i++){host.Poll(now);foreach(var client in clients)client.Poll(now,_=>default);}
                var state=host.Session!.InspectState();
                Require(state.Coins==baseValue+baseValue/2 && state.Players.Take(2).All(p=>p.Combo.Count==1+woodCatches[p.Slot]) && state.Players.Skip(2).All(p=>p.Combo.Count==0),"Shared bonus/count differs: base="+baseValue+" coins="+state.Coins+" tick="+state.Steps+" players="+string.Join(";",state.Players.Select(p=>$"{p.Slot}:catches={p.Catches},wood={woodCatches[p.Slot]},combo={p.Combo.Count},casts={p.Casts},phase={p.Phase}")));
                var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());
                foreach(var client in clients)Require(client.Session!=null && bytes.SequenceEqual(CheckpointCodec.Encode(client.Session.CaptureCheckpoint())),"Combo confirmed checkpoint diverged.");
                Console.WriteLine($"COMBO_UDP players=4 tick={state.Steps} coins={state.Coins} base={baseValue} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var client in clients)client.Dispose();}
        });
    }
}

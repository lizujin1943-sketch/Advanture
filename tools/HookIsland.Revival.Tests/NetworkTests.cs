using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.RevivalTests;
internal static partial class Program
{
    private static IslandMap NetworkMap(RevivalConfig? revival=null)
    {
        var map=IslandMap.CreatePlatform();
        return new IslandMap(map.Shore,map.Pier,map.Spawns,map.FishSpots,map.Platform,new FishingConfig(enemies:new EnemyConfig(),revival:revival));
    }
    private static IEnumerable<(string,Action)> NetworkCases()
    {
        yield return ("network_four_clients_fail_only_after_final_death_with_empty_wallets",()=>{
            var seed=IslandMap.CreatePlatform();
            var map=new IslandMap(seed.Shore,seed.Pier,seed.Spawns,seed.FishSpots,seed.Platform,
                new FishingConfig(swimming:new SwimmingConfig(healthLossMilliPerSecond:1000000),revival:new RevivalConfig(downedTicks:30)));
            using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;bool downedWindow=false;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(12))
                {
                    last=clock.Elapsed;host.Poll(last);
                    foreach(var c in clients)
                    {c.Poll(last,_=>new HookInput(c.Slot%2==0?(short)1000:(short)-1000,0));Require(c.Failure==null,c.Failure??"");}
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session==null){System.Threading.Thread.Sleep(1);continue;}
                    var s=host.Session.InspectState();
                    if(s.Players.All(p=>p.Vitals.Downed) && s.Players.Any(p=>!p.Revival.Dead))
                    {downedWindow=true;Require(!s.GameOver,"Downed team failed before the final-death deadline.");}
                    if(s.GameOver)break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<300;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                Require(downedWindow && host.Session!=null && host.Session.InspectState().GameOver,"No synchronized final team failure.");
                string hash=CheckpointCodec.Hash(host.Session!.CaptureCheckpoint());
                foreach(var c in clients)Require(c.Session!.InspectState().GameOver && CheckpointCodec.Hash(c.Session.CaptureCheckpoint())==hash,"Team failure differs across peers.");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        yield return ("payment_buffer_retries_same_death_until_success_or_failure_receipt",()=>{
            var buffer=new RevivalRequestBuffer();var life=new RevivalState{Dead=true,Deaths=3,LastRequest=7,LastResult=RespawnResult.NoLanding};
            Require(buffer.TryQueue(life) && buffer.Command.RespawnRequest==8 && buffer.Command.DeathId==3,"Wrong payment intent.");
            Require(!buffer.TryQueue(life) && !buffer.Observe(life,out _),"Unconfirmed request was replaced/cleared.");
            life.LastRequest=8;life.LastResult=RespawnResult.InsufficientCoins;
            Require(buffer.Observe(life,out var result) && result==RespawnResult.InsufficientCoins && !buffer.Pending,"Failure receipt did not clear retry.");
            Require(buffer.TryQueue(life) && buffer.Command.RespawnRequest==9,"New click reused failed receipt.");
            buffer.Reset();Require(!buffer.Pending && !buffer.TryQueue(default),"Session reset leaked payment.");
        });
        yield return ("network_rejects_different_revival_rules",()=>{
            using var host=new IslandHost(NetworkMap(),0,2);
            using var client=new IslandClient(NetworkMap(new RevivalConfig(downedTicks:1801)),IPAddress.Loopback,host.Port,ushort.MaxValue);
            for(int ms=0;ms<1500 && client.Failure==null;ms+=2)
            {var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);client.Poll(now,_=>default);}
            Require(client.Failure!=null && host.Session==null,"Different revival rules joined room.");
        });
        yield return ("network_four_clients_free_rescue_and_personal_paid_respawn",()=>{
            var map=NetworkMap();using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=clients.Select(_=>new RevivalSmokePilot()).ToArray();var observer=new RevivalSmokePilot();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(160))
                {
                    var now=last=clock.Elapsed;host.Poll(now);
                    for(int i=0;i<clients.Length;i++)
                    {var c=clients[i];var pilot=pilots[i];c.Poll(now,t=>c.Session?.LocalParticipant==null?default:pilot.Sample(map,c.Session.InspectState(),c.Slot,t));Require(c.Failure==null,c.Failure??"");}
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && observer.Complete(host.Session.InspectState()))break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<300;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                Require(host.Session!=null,"Room did not start.");var s=host.Session!.InspectState();
                Require(observer.Complete(s),$"Revival loop incomplete at {s.Steps}.");
                Require(s.Players[1].Revival.CoinsSpent==0 && s.Players[2].Revival.CoinsSpent>0 && s.Players[2].CoinsEarned-s.Players[2].Coins==s.Players[2].Revival.CoinsSpent,"Personal/free settlement differs.");
                var cp=host.Session.CaptureCheckpoint();string hash=CheckpointCodec.Hash(cp);
                foreach(var c in clients)Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash,"Revival checkpoint differs across peers.");
                var copy=Model(4,enemies:new EnemyConfig());copy.Restore(new HookGame().Decode(cp.ApplicationState.Span));
                Require(copy.Capture().Players[2].Revival.CoinsSpent==s.Players[2].Revival.CoinsSpent,"Death/payment receipt lost on restore.");
                Console.WriteLine($"REVIVAL_NETWORK players=4 tick={s.Steps} fee={s.Players[2].Revival.CoinsSpent} hash={hash}");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using Advanture.HookIsland;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.RevivalTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> ImmediateNetworkCases()
    {
        foreach(bool earn in new[]{true,false})yield return ($"network_solo_immediate_{(earn?"earned_auto_respawn":"broke_defeat")}",()=>{
            var basis=CooperativeFishingSmokePilot.CreateMap();
            var map=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,
                new FishingConfig(reelSpeed:600,enemies:basis.Fishing!.Enemies,swimming:new SwimmingConfig(healthLossBasisPointsPerSecond:10000)));
            using var host=new IslandHost(map,0,1);using var client=new IslandClient(map,IPAddress.Loopback,host.Port,0);
            var pilot=new CooperativeFishingSmokePilot();var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan now=default;
            bool Complete()=>host.Session!=null && (earn?host.Session.InspectState().Players[0].Revival.CoinsSpent>0:host.Session.InspectState().GameOver);
            while(clock.Elapsed<TimeSpan.FromSeconds(45) && !Complete())
            {
                now=clock.Elapsed;host.Poll(now);
                client.Poll(now,t=>{
                    if(client.Session?.LocalParticipant==null)return default;
                    var s=client.Session.InspectState();var p=s.Players[0];
                    if(p.Revival.CoinsSpent>0 || s.GameOver)return default;
                    if(!earn || p.Coins>=s.Tackle!.Config.Revival.Cost(s.Steps,p.Coins))
                        return p.Phase==HookPhase.Ready?new HookInput(-1000,0):default;
                    return pilot.Sample(map,s,0,t);
                });
                Require(host.Failure==null && client.Failure==null,host.Failure??client.Failure??"");
                if(host.Session!=null)
                {var p=host.Session.InspectState().Players[0];Require(!p.Vitals.Downed || p.Revival.Dead,"Solo authoritative state exposed a rescue countdown.");}
                System.Threading.Thread.Sleep(1);
            }
            Require(Complete(),"Solo normal-input death scenario did not complete.");
            for(int i=0;i<300;i++){host.Poll(now);client.Poll(now,_=>default);}
            var state=host.Session!.InspectState();var player=state.Players[0];
            if(earn)Require(player.CoinsEarned>0 && player.Revival.Deaths==1 && !player.Vitals.Downed && player.CoinsEarned-player.Coins==player.Revival.CoinsSpent,"Automatic respawn did not use real personal earnings exactly once.");
            else Require(state.GameOver && player.Coins==0 && player.Revival.CoinsSpent==0,"Unfunded death charged money or kept the round alive.");
            string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
            Require(client.Session!=null && CheckpointCodec.Hash(client.Session.CaptureCheckpoint())==hash,"Solo server/client lifecycle diverged.");
            Console.WriteLine($"IMMEDIATE_REVIVAL_UDP players=1 earned={earn} tick={state.Steps} fee={player.Revival.CoinsSpent} hash={hash}");
        });
    }
}

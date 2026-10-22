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
    private static IEnumerable<(string,Action)> EscapeNetworkCases()
    {
        yield return ("fish_escape_four_udp_clients_agree_during_struggle_flight_and_splash",()=>{
            var basis=IslandMap.CreatePlatform();
            var map=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,
                new FishingConfig(staminaBase:1,regenMilliPerSecond:1,reelSpeed:300));
            using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var checkedPhases=new bool[3];var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            void Agree(TimeSpan now)
            {
                for(int i=0;i<200;i++){host.Poll(now);foreach(var c in clients)c.Poll(now,_=>default);}
                string hash=CheckpointCodec.Hash(host.Session!.CaptureCheckpoint());
                foreach(var c in clients)Require(c.Session!=null && CheckpointCodec.Hash(c.Session.CaptureCheckpoint())==hash,"Peers disagree on escape pose, timer or reward state.");
            }
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(100))
                {
                    last=clock.Elapsed;host.Poll(last);
                    foreach(var c in clients)
                    {c.Poll(last,t=>c.Session?.LocalParticipant==null?default:EscapeInput(map,c.Session.InspectState(),c.Slot,t));Require(c.Failure==null,c.Failure??"");}
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session==null)continue;
                    var state=host.Session.InspectState();
                    foreach(var f in state.Fish.Where(f=>f.Escape.Active))
                    {
                        int phase=f.Escape.Tick<=CatchEscapeMotion.StruggleTicks?0:f.Escape.Tick<CatchEscapeMotion.SplashTick?1:2;
                        if(checkedPhases[phase])continue;
                        Agree(last);checkedPhases[phase]=true;Console.WriteLine($"ESCAPE_PHASE {phase} TICK {state.Steps}");
                    }
                    if(state.Players.All(p=>p.Escapes>0) && checkedPhases.All(v=>v))break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session!=null,"Room never started.");Agree(last);
                var end=host.Session!.InspectState();
                Require(checkedPhases.All(v=>v) && end.Players.All(p=>p.Escapes>0),"Four-player escape acceptance incomplete: "+string.Join(";",end.Players.Select(p=>$"P{p.Slot} fish={p.Catches} escapes={p.Escapes} stamina={p.Stamina}")));
                Require(end.Players.All(p=>p.FishId<0),"Exhausted fish kept ownership.");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
    }
    private static HookInput EscapeInput(IslandMap map,HookState state,int slot,ulong requested)
    {
        var p=state.Players[slot];if(p.Escapes>0 || p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
        var stands=new[]{new Point(-1400,-2600),new Point(2600,-1400),new Point(1400,2600),new Point(-2600,1400)};
        int dx=stands[slot].X-p.X,dz=stands[slot].Z-p.Z;
        if(Math.Abs(dx)>100)return new HookInput((short)(Math.Sign(dx)*1000),0);
        if(Math.Abs(dz)>100)return new HookInput(0,(short)(Math.Sign(dz)*1000));
        int lead=(int)Math.Min(30UL,requested>state.Steps?requested-state.Steps-1:0);
        int speed=FishingRules.Speed(state,p),range=FishingRules.Range(map,state,p);
        foreach(var fish in state.Fish)
        {
            if(fish.Owner>=0 || fish.Escape.Active)continue;
            var future=FishMotion.Predict(fish,map.FishSpots[fish.Id],lead);
            for(int ticks=1;ticks<=Math.Min(120,range*30/speed+1);ticks++)
            {
                FishMotion.Advance(ref future,map.FishSpots[fish.Id]);
                if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                int distance=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                if(distance<=range && Math.Abs(distance-ticks*speed/30)<speed/60+40)
                    return new HookInput(0,0,future.X,future.Z,true);
            }
        }
        return default;
    }
}

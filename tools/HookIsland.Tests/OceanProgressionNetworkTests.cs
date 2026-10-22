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
    private static IEnumerable<(string,Action)> OceanInputCases()
    {
        foreach(int count in new[]{1,2,4})foreach(int delay in new[]{0,3,6})
            yield return ($"ocean_normal_input_{count}_players_delay_{delay}",()=>OceanInputScenario(count,delay,false));
        yield return ("ocean_default_school_and_recovery_reachable_by_starter_rod",()=>OceanInputScenario(1,3,true));
    }
    private static void OceanInputScenario(int count,int delay,bool defaults)
    {
        var map=OceanSmokePilot.CreateMap(defaults);var runner=Runner(count,map);var pilot=new OceanSmokePilot();
        var pending=new Queue<HookInput[]>();for(int i=0;i<delay;i++)pending.Enqueue(new HookInput[count]);int stages=0;
        for(int i=0;i<(defaults?90000:18000) && !pilot.Complete(runner.InspectState());i++)
        {
            var s=runner.InspectState();stages|=1<<OceanRules.Stage(s.Commissions.Completed);
            pending.Enqueue(s.Players.Select(p=>pilot.Sample(map,s,p.Slot,runner.Tick+(ulong)delay+1)).ToArray());Step(runner,pending.Dequeue());
        }
        var state=runner.InspectState();
        Console.WriteLine($"OCEAN_INPUT players={count} delay={delay} defaults={defaults} tick={runner.Tick} stage={state.Ocean.RewardedStage} event={state.Ocean.LastEvent} catches={string.Join(",",state.Players.Select(p=>p.Catches))}");
        Require(pilot.Complete(state) && stages==7,"Normal inputs did not unlock both bands and capture a recovery visit.");
        Require(state.Players.All(p=>p.Catches>0 && p.Coins==p.CoinsEarned),"A player did not contribute or rewards were consumed.");
        _=new HookGame().Decode(new HookGame().Encode(state));
    }
    private static IEnumerable<(string,Action)> OceanNetworkCases()
    {
        foreach(int count in new[]{1,2,4})yield return ($"ocean_real_udp_{count}_clients",()=>{
            var map=OceanSmokePilot.CreateMap();using var host=new IslandHost(map,0,count);
            var clients=Enumerable.Range(0,count).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=clients.Select(_=>new OceanSmokePilot()).ToArray();var seen=new int[count];
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(180))
                {
                    last=clock.Elapsed;host.Poll(last);
                    for(int i=0;i<count;i++)
                    {
                        var c=clients[i];var pilot=pilots[i];c.Poll(last,t=>c.Session?.LocalParticipant==null?default:pilot.Sample(map,c.Session.InspectState(),c.Slot,t));
                        Require(c.Failure==null,c.Failure??"");
                        if(c.Session?.LocalParticipant!=null)seen[i]|=1<<OceanRules.Stage(c.Session.InspectState().Commissions.Completed);
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && clients.Select((c,i)=>c.Session?.LocalParticipant!=null && pilots[i].Complete(c.Session.InspectState())).All(v=>v))break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<200;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                Require(host.Session!=null,"Ocean room did not start.");var state=host.Session!.InspectState();
                Require(clients.Select((c,i)=>pilots[i].Complete(c.Session!.InspectState()) && seen[i]==7).All(v=>v),"A peer missed stages or a captured recovery visit.");
                var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint())),"Ocean checkpoints differ byte-for-byte.");
                Console.WriteLine($"OCEAN_UDP players={count} tick={host.Session.ConfirmedTick} sequence={state.Ocean.Sequence} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
    }
}

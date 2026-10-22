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
    private static IEnumerable<(string,Action)> CommissionInputCases()
    {
        foreach(int count in new[]{1,2,4})foreach(int delay in new[]{0,3,6})
            yield return ($"commission_normal_input_{count}_players_delay_{delay}",()=>CommissionInputScenario(count,delay,false));
        yield return ("commission_default_three_objectives_finish_using_ordinary_input",()=>CommissionInputScenario(1,3,true));
    }
    private static void CommissionInputScenario(int count,int delay,bool defaults)
    {
        var map=CommissionSmokePilot.CreateMap(defaults);var runner=Runner(count,map);var pilot=new CommissionSmokePilot();
        var pending=new Queue<HookInput[]>();for(int i=0;i<delay;i++)pending.Enqueue(new HookInput[count]);
        int maximum=defaults?90000:18000;
        for(int i=0;i<maximum && !pilot.Complete(runner.InspectState());i++)
        {
            var s=runner.InspectState();pending.Enqueue(s.Players.Select(p=>pilot.Sample(map,s,p.Slot,runner.Tick+(ulong)delay+1)).ToArray());
            Step(runner,pending.Dequeue());
        }
        var state=runner.InspectState();
        Console.WriteLine($"COMMISSION_INPUT players={count} delay={delay} defaults={defaults} tick={runner.Tick} completed={state.Commissions.Completed} bait={state.Commissions.Bait} catches={string.Join(",",state.Players.Select(p=>p.Catches))}");
        Require(pilot.Complete(state) && state.Commissions.Bait==3,"Ordinary inputs did not finish all three commissions.");
        Require(state.Players.All(p=>p.Catches>0) && state.Players.All(p=>p.Coins==p.CoinsEarned),"A player did not contribute or bait consumed personal money.");
        Require(state.Commissions.Entries.Sum(e=>e.Fish)<=state.Players.Sum(p=>p.Catches),"Cooperation duplicated commission progress.");
    }
    private static IEnumerable<(string,Action)> CommissionNetworkCases()
    {
        foreach(int count in new[]{1,2,4})yield return ($"commission_real_udp_{count}_clients",()=>{
            var map=CommissionSmokePilot.CreateMap();using var host=new IslandHost(map,0,count);
            var clients=Enumerable.Range(0,count).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=clients.Select(_=>new CommissionSmokePilot()).ToArray();var seen=new int[count];
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(180))
                {
                    last=clock.Elapsed;host.Poll(last);
                    for(int i=0;i<count;i++)
                    {
                        var c=clients[i];var pilot=pilots[i];
                        c.Poll(last,t=>c.Session?.LocalParticipant==null?default:pilot.Sample(map,c.Session.InspectState(),c.Slot,t));
                        Require(c.Failure==null,c.Failure??"");
                        if(c.Session?.LocalParticipant!=null)seen[i]|=1<<c.Session.InspectState().Commissions.Completed;
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && pilots[0].Complete(host.Session.InspectState()) && seen.All(v=>v==15))break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<200;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                Require(host.Session!=null,"Commission room did not start.");var state=host.Session!.InspectState();
                Require(pilots[0].Complete(state) && seen.All(v=>v==15) && state.Commissions.Bait==3,"Peers did not observe all shared commission stages.");
                var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint())),"Commission checkpoints differ byte-for-byte.");
                Require(state.Players.All(p=>p.Catches>0 && p.Coins==p.CoinsEarned),"A player's capture contribution or income was lost.");
                Console.WriteLine($"COMMISSION_UDP players={count} tick={host.Session.ConfirmedTick} bait={state.Commissions.Bait} fish={state.Commissions.Entries.Sum(e=>e.Fish)} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
    }
}

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
    private static IEnumerable<(string,Action)> FinaleInputCases()
    {
        foreach(int count in new[]{1,2,3,4})foreach(int delay in new[]{0,3,6})
            yield return ($"finale_input_{count}_players_delay_{delay}",()=>FinaleInputScenario(count,delay,false));
        yield return ("finale_input_defaults_starter_rod_to_victory",()=>FinaleInputScenario(1,3,true));
    }
    private static void FinaleInputScenario(int count,int delay,bool defaults)
    {
        var map=FinaleSmokePilot.CreateMap(defaults);var runner=Runner(count,map);var pilot=new FinaleSmokePilot();
        var pending=new Queue<HookInput[]>();for(int i=0;i<delay;i++)pending.Enqueue(new HookInput[count]);
        var banks=new HashSet<(int,int)>();int maxHooks=0,participants=0;var codec=new HookGame();
        for(int i=0;i<(defaults?90000:12000) && !pilot.Complete(runner.InspectState());i++)
        {
            var s=runner.InspectState();
            if(s.Finale.Phase==FinalePhase.Surfaced)banks.Add((s.Finale.X,s.Finale.Z));
            maxHooks=Math.Max(maxHooks,FinaleRules.Count(s.Players));foreach(var p in s.Players)if(p.HookedFinale)participants|=1<<p.Slot;
            pending.Enqueue(s.Players.Select(p=>pilot.Sample(map,s,p.Slot,runner.Tick+(ulong)delay+1)).ToArray());Step(runner,pending.Dequeue());
            if(i%30==0)codec.Decode(codec.Encode(runner.InspectState()));
        }
        var state=runner.InspectState();
        Console.WriteLine($"FINALE_INPUT players={count} delay={delay} defaults={defaults} tick={runner.Tick} completed={state.Finale.Completed} phase={state.Finale.Phase} hooks={maxHooks} participants={participants} catches={string.Join(",",state.Players.Select(p=>p.Catches))}");
        Require(state.Won && state.Finale.Completed==3 && banks.Count==3,"Normal inputs did not finish all three distinct shores.");
        Require(maxHooks==count && participants==(1<<count)-1 && state.Players.All(p=>p.Catches>0),"Not every player joined the commission and giant hooks.");
        Require(state.Commissions.Bait==3 && state.Finale.SpentBait==3,"Finale spent an incorrect amount of bait.");
        var frozen=codec.Encode(state);for(int i=0;i<180;i++)Step(runner,new HookInput(1000,0,0,-5000,true));
        Require(frozen.SequenceEqual(codec.Encode(runner.InspectState())) && runner.Tick>state.Steps+100,"Victory failed to freeze gameplay while confirming more frames.");
        codec.Decode(frozen);
    }
    private static IEnumerable<(string,Action)> FinaleNetworkCases()
    {
        foreach(int count in new[]{1,2,4})yield return ($"finale_real_udp_{count}_clients",()=>{
            var map=FinaleSmokePilot.CreateMap();using var host=new IslandHost(map,0,count);
            var clients=Enumerable.Range(0,count).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=clients.Select(_=>new FinaleSmokePilot()).ToArray();var shores=new int[count];var joined=new int[count];
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;ulong end=0;byte[]? frozen=null;var codec=new HookGame();
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(180))
                {
                    last=clock.Elapsed;host.Poll(last);
                    for(int i=0;i<count;i++)
                    {
                        var c=clients[i];c.Poll(last,t=>c.Session?.LocalParticipant==null?default:pilots[i].Sample(map,c.Session.InspectState(),c.Slot,t));
                        Require(c.Failure==null,c.Failure??"");
                        if(c.Session?.LocalParticipant==null)continue;
                        var s=c.Session.InspectState();if(s.Finale.Phase==FinalePhase.Surfaced)shores[i]|=1<<s.Finale.Completed;
                        foreach(var p in s.Players)if(p.HookedFinale)joined[i]|=1<<p.Slot;
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session?.InspectState().Won==true)
                    {
                        var state=host.Session.InspectState();
                        if(frozen==null){frozen=codec.Encode(state);end=host.Session.ConfirmedTick+120;}
                        Require(frozen.SequenceEqual(codec.Encode(state)),"Completed gameplay mutated while the room kept confirming frames.");
                        if(host.Session.ConfirmedTick>=end && clients.All(c=>c.Session?.InspectState().Won==true))break;
                    }
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<200;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                Require(host.Session!=null && frozen!=null && host.Session.ConfirmedTick>=end && host.CanRestart,"Winning room stopped confirming or could not restart.");
                var bytes=CheckpointCodec.Encode(host.Session!.CaptureCheckpoint());
                for(int i=0;i<count;i++)
                {
                    Require(shores[i]==7 && joined[i]==(1<<count)-1,"A peer missed a shore or a player contribution.");
                    Require(bytes.SequenceEqual(CheckpointCodec.Encode(clients[i].Session!.CaptureCheckpoint())),"Finale checkpoints differ byte-for-byte.");
                }
                Console.WriteLine($"FINALE_UDP players={count} tick={host.Session.ConfirmedTick} victoryTick={host.Session.InspectState().Steps} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
    }
}

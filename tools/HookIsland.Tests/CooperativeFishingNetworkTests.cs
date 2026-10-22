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
    private static IEnumerable<(string,Action)> CooperativeInputCases()
    {
        foreach(int count in new[]{1,2,4})foreach(int delay in new[]{0,3,6})yield return ($"coop_normal_input_scenario_{count}_players_delay_{delay}",()=>{
            var map=CooperativeFishingSmokePilot.CreateMap();var runner=Runner(count,map);var pilot=new CooperativeFishingSmokePilot();
            var pending=new Queue<HookInput[]>();for(int i=0;i<delay;i++)pending.Enqueue(new HookInput[count]);
            for(int i=0;i<4500 && !pilot.Complete(runner.InspectState());i++)
            {
                var s=runner.InspectState();pending.Enqueue(s.Players.Select(p=>pilot.Sample(map,s,p.Slot,runner.Tick+(ulong)delay+1)).ToArray());Step(runner,pending.Dequeue());
                if(i%500==0)Console.WriteLine($"COOP_INPUT {count} tick={runner.Tick} stage={pilot.Stage} "+string.Join(";",runner.InspectState().Players.Select(p=>$"P{p.Slot}={p.X},{p.Z} fish={p.FishId} catch={p.Catches} stamina={p.Stamina}")));
            }
            Require(pilot.Complete(runner.InspectState()),"Normal input scenario incomplete at stage "+pilot.Stage);
            Console.WriteLine($"COOP_INPUT_DONE players={count} tick={runner.Tick} shared={pilot.SharedFish} stolen={pilot.StolenFish} piranha={pilot.SharedPiranha}");
        });
    }
    private static IEnumerable<(string,Action)> CooperativeNetworkCases()
    {
        foreach(int count in new[]{1,2,4})yield return ($"coop_real_udp_{count}_clients",()=>{
            var map=CooperativeFishingSmokePilot.CreateMap();using var host=new IslandHost(map,0,count);
            var clients=Enumerable.Range(0,count).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=clients.Select(_=>new CooperativeFishingSmokePilot()).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;var observed=new bool[3];ulong nextProgress=0;
            void Agree(TimeSpan now)
            {
                for(int i=0;i<200;i++){host.Poll(now);foreach(var client in clients)client.Poll(now,_=>default);}
                string hash=CheckpointCodec.Hash(host.Session!.CaptureCheckpoint());
                foreach(var client in clients)Require(client.Session!=null && CheckpointCodec.Hash(client.Session.CaptureCheckpoint())==hash,"Cooperative checkpoint differs between peers.");
            }
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(120))
                {
                    last=clock.Elapsed;host.Poll(last);
                    for(int i=0;i<count;i++)
                    {
                        var client=clients[i];var pilot=pilots[i];
                        client.Poll(last,t=>client.Session?.LocalParticipant==null?default:pilot.Sample(map,client.Session.InspectState(),client.Slot,t));
                        Require(client.Failure==null,client.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");if(host.Session==null)continue;
                    if(host.Session.ConfirmedTick>=nextProgress)
                    {
                        nextProgress=host.Session.ConfirmedTick+300;var s=host.Session.InspectState();
                        Console.WriteLine($"COOP_UDP_PROGRESS players={count} tick={host.Session.ConfirmedTick} stages={string.Join(",",pilots.Select(p=>p.Stage))} stolen={string.Join(",",pilots.Select(p=>p.StolenFish))} fish={s.Fish[0].X},{s.Fish[0].Z},owner{s.Fish[0].Owner} "+string.Join(";",s.Players.Select(p=>$"P{p.Slot}={p.X},{p.Z} phase={p.Phase} catches={p.Catches} casts={p.Casts} stamina={p.Stamina}")));
                    }
                    for(int i=0;i<count;i++)if(clients[i].Session?.LocalParticipant!=null)pilots[i].Observe(clients[i].Session!.InspectState());
                    var flags=new[]{pilots.All(p=>p.SharedFish),pilots.All(p=>p.StolenFish),pilots.All(p=>p.SharedPiranha)};
                    for(int i=0;i<3;i++)if(flags[i] && !observed[i]){Agree(last);observed[i]=true;Console.WriteLine($"COOP_UDP_EVENT players={count} phase={i} tick={host.Session.ConfirmedTick}");}
                    if(pilots.All(p=>p.Complete(host.Session.InspectState())))break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session!=null,"UDP room did not start.");Agree(last);
                Require(pilots.All(p=>p.Complete(host.Session!.InspectState())),"UDP scenario incomplete: "+string.Join(",",pilots.Select(p=>p.Stage)));
                Require(count==1 || observed.All(v=>v),"Peers did not all observe shared fish, stealing and shared piranha.");
                Console.WriteLine($"COOP_UDP_DONE players={count} tick={host.Session!.ConfirmedTick} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var client in clients)client.Dispose();}
        });
    }
}

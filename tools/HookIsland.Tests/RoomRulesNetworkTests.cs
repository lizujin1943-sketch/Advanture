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
    private static IEnumerable<(string,Action)> RoomRulesInputCases()
    {
        foreach(int count in new[]{1,2,4})foreach(int delay in new[]{0,3,6})yield return ($"room_rules_normal_input_{count}_players_delay_{delay}",()=>{
            var runner=Runner(count,RoomRulesSmokePilot.CreateMap());var pilot=new RoomRulesSmokePilot();
            var pending=new Queue<HookInput[]>();for(int i=0;i<delay;i++)pending.Enqueue(new HookInput[count]);
            for(int i=0;i<1200 && !pilot.Complete(runner.InspectState());i++)
            {
                var state=runner.InspectState();pending.Enqueue(state.Players.Select(p=>pilot.Sample(state,p.Slot)).ToArray());Step(runner,pending.Dequeue());
            }
            Require(pilot.Complete(runner.InspectState()),"Room-rules input scenario stuck at stage "+pilot.Stage);
        });
    }
    private static IEnumerable<(string,Action)> RoomRulesNetworkCases()
    {
        foreach(int count in new[]{1,2,4})yield return ($"room_rules_real_udp_{count}_clients",()=>{
            var map=RoomRulesSmokePilot.CreateMap();using var host=new IslandHost(map,0,count);
            var clients=Enumerable.Range(0,count).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=clients.Select(_=>new RoomRulesSmokePilot()).ToArray();var buffers=clients.Select(_=>new RoomRulesRequestBuffer()).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan now=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(45))
                {
                    now=clock.Elapsed;host.Poll(now);
                    for(int i=0;i<count;i++)
                    {
                        int index=i;var client=clients[i];
                        client.Poll(now,_=>{
                            if(client.Session?.LocalParticipant==null)return default;
                            var s=client.Session.InspectState();var input=pilots[index].Sample(s,client.Slot);var buffer=buffers[index];
                            buffer.Observe(s.RoomRules);
                            if(client.Slot==0 && !input.RoomRules.IsEmpty)buffer.TryQueue(s.RoomRules,input.RoomRules.Enabled);
                            // A guest's forged policy command shares the same real transport but must be ignored.
                            var command=client.Slot==0?default:new RoomRulesCommand(true,uint.MaxValue);
                            return buffer.Compose(new HookInput(input.MoveX,input.MoveZ,input.AimX,input.AimZ,input.Fire,input.Cancel,roomRules:command));
                        });
                        Require(client.Failure==null,client.Failure??"");
                        if(client.Session?.LocalParticipant!=null)pilots[i].Observe(client.Session.InspectState());
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && pilots.All(p=>p.Complete(host.Session.InspectState())))break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session!=null && pilots.All(p=>p.Complete(host.Session.InspectState())),"UDP rules scenario incomplete: "+string.Join(",",pilots.Select(p=>p.Stage)));
                for(int i=0;i<250;i++){host.Poll(now);foreach(var client in clients)client.Poll(now,_=>default);}
                string hash=CheckpointCodec.Hash(host.Session!.CaptureCheckpoint());
                Require(clients.All(c=>c.Session!=null && CheckpointCodec.Hash(c.Session.CaptureCheckpoint())==hash),"Room-rules checkpoints differ.");
                Require(!host.Session.InspectState().RoomRules.FriendlyPranksEnabled && host.Session.InspectState().RoomRules.LastRequest==3,"A guest changed the policy or host receipt.");
                Console.WriteLine($"ROOM_RULES_UDP players={count} tick={host.Session.ConfirmedTick} hash={hash}");
            }
            finally{foreach(var client in clients)client.Dispose();}
        });
    }
}

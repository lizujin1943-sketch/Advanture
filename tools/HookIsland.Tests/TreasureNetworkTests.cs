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
    private static IEnumerable<(string,Action)> TreasureInputCases()
    {
        foreach(int count in new[]{1,2,4})foreach(int delay in new[]{0,3,6})yield return ($"treasure_normal_input_{count}_players_delay_{delay}",()=>{
            var map=TreasureSmokePilot.CreateMap();var runner=Runner(count,map);var pilot=new TreasureSmokePilot();var queue=new Queue<HookInput[]>();
            for(int i=0;i<delay;i++)queue.Enqueue(new HookInput[count]);
            for(int i=0;i<3000 && !pilot.Complete(runner.InspectState());i++)
            {
                var s=runner.InspectState();queue.Enqueue(s.Players.Select(p=>pilot.Sample(map,s,p.Slot,runner.Tick+(ulong)delay+1)).ToArray());Step(runner,queue.Dequeue());
                if(i%600==0)Console.WriteLine($"TREASURE_INPUT {count} tick={runner.Tick} stage={pilot.Stage} "+string.Join(";",runner.InspectState().Players.Select(p=>$"P{p.Slot}:{p.X},{p.Z},hook={p.HookedChest},phase={p.Phase},collected={p.TreasuresCollected}")));
            }
            Require(pilot.Complete(runner.InspectState()),"Normal treasure input incomplete at stage "+pilot.Stage+" / "+string.Join(";",runner.InspectState().Treasure.Chests.Select(c=>$"C{c.Id}:{c.X},{c.Z},phase={c.Phase},timer={c.Timer},owner={c.Owner}"))+" / "+string.Join(";",runner.InspectState().Players.Select(p=>$"P{p.Slot}cast={p.Casts},wood={p.DriftwoodId},stamina={p.Stamina}")));
            Console.WriteLine($"TREASURE_INPUT_DONE players={count} tick={runner.Tick} shared={pilot.Shared} landed={pilot.Landed} opened={pilot.Opened}");
        });
    }
    private static IEnumerable<(string,Action)> TreasureNetworkCases()
    {
        foreach(int count in new[]{1,2,4})yield return ($"treasure_real_udp_{count}_clients",()=>{
            var map=TreasureSmokePilot.CreateMap();using var host=new IslandHost(map,0,count);
            var clients=Enumerable.Range(0,count).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var pilots=clients.Select(_=>new TreasureSmokePilot()).ToArray();var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(90))
                {
                    last=clock.Elapsed;host.Poll(last);
                    for(int i=0;i<count;i++)
                    {
                        var client=clients[i];var pilot=pilots[i];
                        client.Poll(last,t=>client.Session?.LocalParticipant==null?default:pilot.Sample(map,client.Session.InspectState(),client.Slot,t));
                        Require(client.Failure==null,client.Failure??"");
                        if(client.Session?.LocalParticipant!=null)pilot.Observe(client.Session.InspectState());
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && pilots.All(p=>p.Complete(host.Session.InspectState())))break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session!=null && pilots.All(p=>p.Complete(host.Session.InspectState())),
                    "UDP treasure flow incomplete. tick="+host.Session?.ConfirmedTick+
                    " pilots="+string.Join(";",pilots.Select(p=>$"stage={p.Stage},shared={p.Shared},landed={p.Landed},opened={p.Opened},max={p.MaxParticipants}"))+
                    " players="+(host.Session==null?"none":string.Join(";",host.Session.InspectState().Players.Select(p=>$"P{p.Slot}:{p.X},{p.Z},phase={p.Phase},chest={p.HookedChest},collected={p.TreasuresCollected},open={p.LastTreasureResult}")))+
                    " chests="+(host.Session==null?"none":string.Join(";",host.Session.InspectState().Treasure.Chests.Select(c=>$"C{c.Id}:{c.X},{c.Z},phase={c.Phase},owner={c.Owner}"))));
                for(int i=0;i<200;i++){host.Poll(last);foreach(var client in clients)client.Poll(last,_=>default);}
                var bytes=CheckpointCodec.Encode(host.Session!.CaptureCheckpoint());
                foreach(var client in clients)Require(client.Session!=null && bytes.SequenceEqual(CheckpointCodec.Encode(client.Session.CaptureCheckpoint())),"Treasure checkpoint differs between peers.");
                Console.WriteLine($"TREASURE_UDP_DONE players={count} tick={host.Session.ConfirmedTick} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var client in clients)client.Dispose();}
        });
    }
}

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
    private static IEnumerable<(string,Action)> PlatformNetworkCases()
    {
        yield return ("platform_drag_four_udp_clients_submit_batches_with_personal_costs_and_shared_tiles",()=>{
            var map=LegacyPlatformMap(4,2);using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var batches=Enumerable.Range(0,4).Select(_=>new PlatformBuildBatch()).ToArray();var started=new bool[4];var finished=new bool[4];
            var paths=new[]{new[]{new Point(-2,0),new Point(-2,-1)},new[]{new Point(0,-2),new Point(1,-2)},
                new[]{new Point(2,0),new Point(2,1)},new[]{new Point(0,2),new Point(-1,2)}};
            HookInput Input(IslandClient client,ulong tick)
            {
                var state=client.Session!.InspectState();int slot=client.Slot;var player=state.Players[slot];
                if(state.Players.Any(p=>p.WoodEarned<4 || p.FishbonesEarned<4 || p.Catches<2))
                {
                    var input=PlatformSmokePilot.Sample(map,state,slot,tick);
                    return input.Build==TileKind.None?input:default;
                }
                var batch=batches[slot];
                if(!started[slot])
                {
                    var draft=new PlatformBuildDraft();draft.Begin(TileKind.Fishbone,paths[slot][0].X,paths[slot][0].Z);
                    draft.Visit(paths[slot][1].X,paths[slot][1].Z);draft.Evaluate(map,state,player);
                    if(draft.ValidCount!=2)return default;
                    started[slot]=batch.TryStart(draft.Cells);
                }
                if(batch.Advance(map,state,player))finished[slot]=true;
                return batch.Compose(default,true);
            }
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(100) && !finished.All(v=>v))
                {
                    var now=last=clock.Elapsed;host.Poll(now);
                    foreach(var client in clients){client.Poll(now,t=>Input(client,t));Require(client.Failure==null,client.Failure??"");}
                    Require(host.Failure==null,host.Failure??"");System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<300;i++){host.Poll(last);foreach(var client in clients)client.Poll(last,_=>default);}
                Require(finished.All(v=>v) && batches.All(b=>b.Succeeded==2 && b.Rejected==0),"Four-client drag batches did not finish: "+string.Join("; ",batches.Select(b=>$"{b.Succeeded}/{b.Rejected}/{b.Remaining}")));
                var state=host.Session!.InspectState();
                Require(state.Tiles.Count==17 && state.Players.All(p=>p.FishbonesEarned-p.Fishbones==4 && p.WoodEarned==p.Wood && p.LastBuildId==2),"Batch debits were duplicated, omitted or taken from another material.");
                string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach(var client in clients)Require(CheckpointCodec.Hash(client.Session!.CaptureCheckpoint())==hash,"Batch construction states differ between authority and client.");
                Console.WriteLine("PLATFORM_DRAG_UDP tiles="+state.Tiles.Count+" hash="+hash);
            }
            finally{foreach(var client in clients)client.Dispose();}
        });
        yield return ("platform_four_udp_clients_share_tiles_but_spend_only_personal_materials",()=>{
            var map=LegacyPlatformMap();using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(100))
                {
                    var now=last=clock.Elapsed;host.Poll(now);
                    foreach(var client in clients)
                    {
                        client.Poll(now,t=>PlatformSmokePilot.Sample(map,client.Session!.InspectState(),client.Slot,t));
                        Require(client.Failure==null,client.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && PlatformSmokePilot.Complete(host.Session.InspectState()))break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<300;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                var s=host.Session!.InspectState();Require(PlatformSmokePilot.Complete(s),"UDP platform collection/build/traversal did not finish: "+
                    string.Join("; ",s.Players.Select(p=>$"P{p.Slot} wood={p.WoodEarned} bones={p.FishbonesEarned} ({p.X},{p.Z}) build={p.LastBuildResult}")));
                Require(s.Tiles.Count==17 && s.Players.Sum(p=>p.WoodEarned-p.Wood)==16 && s.Players.Sum(p=>p.FishbonesEarned-p.Fishbones)==16,"Network build duplicated or lost materials.");
                string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash,"Platform authority/client states differ.");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        yield return ("platform_clients_with_different_recipes_cannot_join_same_room",()=>{
            using var host=new IslandHost(LegacyPlatformMap(4,4),0,2);
            using var client=new IslandClient(LegacyPlatformMap(5,4),IPAddress.Loopback,host.Port,ushort.MaxValue);
            for(int i=0;i<1000 && client.Failure==null;i++)
            {var now=TimeSpan.FromMilliseconds(i*5);host.Poll(now);client.Poll(now,_=>default);}
            Require(client.Failure!=null && host.JoinedCount==0,"Mismatched construction costs were accepted.");
        });
    }
}

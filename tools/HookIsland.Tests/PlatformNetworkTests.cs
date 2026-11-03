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
            var submitted=Enumerable.Range(0,4).Select(_=>new Dictionary<uint,Point>()).ToArray();
            var receipts=Enumerable.Range(0,4).Select(_=>new Dictionary<uint,BuildResult>()).ToArray();
            var paths=new[]{new[]{new Point(-2,0),new Point(-2,-1)},new[]{new Point(0,-2),new Point(1,-2)},
                new[]{new Point(2,0),new Point(2,1)},new[]{new Point(0,2),new Point(-1,2)}};
            HookInput Input(IslandClient client,ulong tick)
            {
                var state=client.Session!.InspectState();int slot=client.Slot;var player=state.Players[slot];
                if(player.LastBuildId>0)
                {
                    Require(submitted[slot].ContainsKey(player.LastBuildId),"Authority acknowledged an unknown build request.");
                    if(receipts[slot].TryGetValue(player.LastBuildId,out var previous))Require(previous==player.LastBuildResult,"A settled build receipt changed.");
                    else receipts[slot].Add(player.LastBuildId,player.LastBuildResult);
                }
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
                var command=batch.Compose(default,true);
                if(command.BuildId>0)
                {
                    var cell=new Point(command.BuildX,command.BuildZ);
                    if(submitted[slot].TryGetValue(command.BuildId,out var previous))Require(previous.X==cell.X && previous.Z==cell.Z,"A pending build ID changed its cell.");
                    else submitted[slot].Add(command.BuildId,cell);
                }
                return command;
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
                // 真实漂流物和鱼会在预览与交付之间进入地格；明确占位拒绝是已完成的事务，不自动重试。
                Require(finished.All(v=>v) && batches.All(b=>!b.Pending && b.Succeeded+b.Rejected==2 && b.Cancelled==0 &&
                    (b.Rejected==0 || b.LastFailure==BuildResult.Occupied)),"Four-client drag batches did not settle: "+string.Join("; ",batches.Select(b=>$"{b.Succeeded}/{b.Rejected}/{b.Remaining}/{b.LastFailure}")));
                var state=host.Session!.InspectState();
                int succeeded=batches.Sum(b=>b.Succeeded);
                Require(succeeded>0 && state.Tiles.Count==9+succeeded,"Shared tiles do not match settled successful requests.");
                for(int slot=0;slot<4;slot++)
                {
                    var player=state.Players[slot];var batch=batches[slot];
                    Require(receipts[slot].Count==submitted[slot].Count && player.LastBuildId==submitted[slot].Count &&
                        receipts[slot].Count(r=>r.Value==BuildResult.Success)==batch.Succeeded,"A build request lost its receipt or settled twice.");
                    Require(player.FishbonesEarned-player.Fishbones==batch.Succeeded*map.Platform!.Cost(TileKind.Fishbone) && player.WoodEarned==player.Wood &&
                        state.Tiles.Count(t=>t.Builder==slot && t.Kind==TileKind.Fishbone)==batch.Succeeded,"Batch debits were duplicated, omitted or taken from another material.");
                    foreach(var request in submitted[slot])
                    {
                        var result=receipts[slot][request.Key];bool built=state.Tiles.Any(t=>t.X==request.Value.X && t.Z==request.Value.Z && t.Builder==slot && t.Kind==TileKind.Fishbone);
                        Require((result==BuildResult.Success || result==BuildResult.Occupied) && built==(result==BuildResult.Success),"A build receipt disagrees with its exact shared tile.");
                    }
                }
                string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach(var client in clients)Require(CheckpointCodec.Hash(client.Session!.CaptureCheckpoint())==hash,"Batch construction states differ between authority and client.");
                Console.WriteLine("PLATFORM_DRAG_UDP tiles="+state.Tiles.Count+" batches="+string.Join(";",batches.Select(b=>$"{b.Succeeded}/{b.Rejected}/{b.LastFailure}"))+" hash="+hash);
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

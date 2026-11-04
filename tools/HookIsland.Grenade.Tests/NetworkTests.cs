using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Persistence;
using FrameSync.Runtime;

namespace Advanture.HookIsland.GrenadeTests;
internal static partial class Program
{
    static partial void RunNetworkTests()
    {
        Test("request_buffer_freezes_target_and_waits_for_final_receipt",()=>{
            var b=new GrenadeRequestBuffer();Require(b.TryQueue(0,7,3000,-3000),"Queue failed");
            Require(!b.TryQueue(0,7,4000,-4000) && b.Command.TargetX==3000,"Overwrote target");
            Require(!b.Observe(new GrenadeAction{LastRequest=1,Result=GrenadeResult.Pending},out _),"Pending cleared request");
            var input=b.Compose(new HookInput(1000,0,fire:true));Require(input.Grenade.TargetX==3000 && !input.Fire,"Did not suppress fire");
            var rules=new RoomRulesRequestBuffer();rules.TryQueue(new RoomRulesState(true),false);
            Require(HookInput.Decode(rules.Compose(input).Encode()).Grenade.ItemId==7,"Room rule swallowed throw");
            Require(new PlatformBuildRequestBuffer().Compose(input,false).Grenade.ItemId==7,"Idle construction swallowed throw");
            var boss=new BossRequestBuffer();boss.TryQueue(0);Require(boss.Compose(input).Grenade.ItemId==7,"Boss stole throw");
            Require(b.Observe(new GrenadeAction{LastRequest=1,Result=GrenadeResult.Blocked},out var result) && result==GrenadeResult.Blocked && !b.Pending,"Failed receipt lost");
        });
        Test("four_real_udp_clients_throw_retry_and_save_flights",()=>{
            var map=Map();using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var buffers=clients.Select(_=>new GrenadeRequestBuffer()).ToArray();
            var events=clients.Select(_=>new List<HookGameplayEvent>()).ToArray();
            var clock=Stopwatch.StartNew();TimeSpan now=default;IslandSave? flightSave=null,landedSave=null;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(20))
                {
                    now=clock.Elapsed;host.Poll(now);if(host.JoinedCount==4 && host.CanStart)host.TryStart();
                    for(int i=0;i<4;i++)
                    {
                        int local=i;var client=clients[i];client.Poll(now,_=>Input(local));
                        events[i].AddRange(client.ConfirmedEvents.Select(e=>e.Event));Require(client.Failure==null,client.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null)
                    {
                        var s=host.Session.InspectState();
                        if(host.CanSave && s.Grenades.Count==4 && s.Grenades.All(g=>g.Phase==GrenadePhase.Flying) && flightSave==null)flightSave=host.CaptureSave();
                        if(host.CanSave && s.Grenades.Count==4 && s.Grenades.All(g=>g.Phase==GrenadePhase.Landed) && landedSave==null)landedSave=host.CaptureSave();
                        if(s.Steps>100 && s.Players.All(p=>p.Grenade.Sequence==1) && s.Grenades.Count==0)break;
                    }
                    Thread.Sleep(1);
                }
                for(int n=0;n<300;n++){host.Poll(now);for(int i=0;i<4;i++){clients[i].Poll(now,_=>default);events[i].AddRange(clients[i].ConfirmedEvents.Select(e=>e.Event));}}
                Require(host.Session!=null && host.Session.InspectState().Steps>100,"Room timed out");
                var bytes=CheckpointCodec.Encode(host.Session!.CaptureCheckpoint());
                foreach(var c in clients)Require(bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint())),"Client checkpoint differs");
                Require(flightSave!=null && landedSave!=null,$"Did not capture flight/landed save: flight={flightSave!=null} landed={landedSave!=null}, receipts="+string.Join(";",host.Session.InspectState().Players.Select(p=>$"{p.Slot}:{p.Grenade.LastRequest}/{p.Grenade.Result}/{p.Grenade.Sequence}")));
                Require(host.Session.InspectState().Players.All(p=>p.Grenade.Sequence==1) && !host.Session.InspectState().Tackle!.Inventory.Items.Any(i=>i.Kind==ItemKind.Throwable),"Repeated intent minted or regranted grenade");
                var expected=events[0].Where(e=>e.Kind>=HookGameplayEventKind.GrenadeThrown).Select(e=>Convert.ToBase64String(HookGameplayEventCodec.Encode(e))).ToArray();
                Require(expected.Length==12,"Missing/doubled throw landing blast events");
                foreach(var list in events)Require(expected.SequenceEqual(list.Where(e=>e.Kind>=HookGameplayEventKind.GrenadeThrown).Select(e=>Convert.ToBase64String(HookGameplayEventCodec.Encode(e)))),"Peer events differ");
                foreach(var saved in new[]{flightSave!,landedSave!})
                {
                    string path=Path.GetFullPath("artifacts/grenades/"+(saved==flightSave?"flight":"landed")+".hisv");Directory.CreateDirectory(Path.GetDirectoryName(path)!);var store=new IslandSaveStore(path);store.Save(saved);var loaded=store.Load();
                    Require(CheckpointCodec.Encode(saved.Document.Checkpoint).SequenceEqual(CheckpointCodec.Encode(loaded.Document.Checkpoint)),"Disk restore changed snapshot");
                    var resumed=Model();resumed.Restore(Codec.Decode(loaded.Document.Checkpoint.ApplicationState.Span));Wait(resumed,100);
                    Require(resumed.Capture().Grenades.Count==0 && resumed.Capture().Players.All(p=>p.Grenade.Sequence==1),"Loaded flight duplicated/lost grenade");
                }
                Console.WriteLine($"GRENADE_UDP clients=4 tick={host.Session.InspectState().Steps} events={expected.Length} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
                HookInput Input(int slot)
                {
                    var session=clients[slot].Session;if(session==null)return default;var s=session.InspectState();var p=s.Players[clients[slot].Slot];
                    if(s.Steps<20 || s.Players.Any(p=>p.Unavailable))return default;
                    if(p.LastInventoryRequest==0)return Select();
                    buffers[slot].Observe(p.Grenade,out _);
                    if(p.Grenade.LastRequest==0 && !buffers[slot].Pending)
                    {
                        var item=s.Tackle!.Inventory.Equipped(p.Slot,EquipmentSlot.Throwable);
                        if(item.HasValue)buffers[slot].TryQueue(0,item.Value.Id,p.X+(slot%2==0?-4000:4000),p.Z+(slot<2?-5000:5000));
                    }
                    return buffers[slot].Compose(default);
                }
            }
            finally{foreach(var client in clients)client.Dispose();}
        });
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;
using FrameSync.Persistence;
using FrameSync.Runtime;

namespace Advanture.HookIsland.FirearmTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> LongGunNetworkCases()
    {
        yield return ("long_guns_four_udp_clients_buy_fire_reload_save_and_rejoin",()=>{
            var map=Map(startingCoins:3000);var keys=Enumerable.Range(0,4).Select(i=>Enumerable.Repeat((byte)(i+10),32).ToArray()).ToArray();
            // Credit comes from production configuration; this fixture only separates firing lanes.
            var roster=Enumerable.Range(0,4).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray();
            var runner=new SimulationRunner<HookState>(HookGame.CreateModule(false),new SessionDescriptor(Guid.NewGuid(),1,30,roster),
                new RuntimeLimits(maxParticipants:4,maxInputBytes:HookInput.ByteCount,maxCheckpointBytes:HookGame.MaximumStateBytes+65536),new ByteString(map.Encode()));
            var initial=runner.InspectState();var players=initial.Players.ToArray();
            Require(initial.Coins==12000 && players.All(p=>p.Coins==3000 && p.CoinsEarned==3000),"Starting credit was not initialized per player");
            for(int i=0;i<4;i++){players[i].X=players[i].HookX=-1200+i*800;players[i].Z=players[i].HookZ=-1400;}
            var funded=new HookState(initial.Steps,players,initial.Fish,initial.Coins,initial.Tiles,initial.Driftwood,initial.Tackle,initial.Enemies,initial.RoomRules,initial.Treasure,initial.Commissions,initial.Ocean,initial.Finale);
            var checkpoint=CheckpointMigration.WithApplicationState(runner.CaptureCheckpoint(),HookGame.CreateModule().Manifest,new ByteString(new HookGame().Encode(funded)));
            // IslandSave's constructor is intentionally internal; keep fixture construction in this test assembly.
            var saved=(IslandSave)typeof(IslandSave).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single().Invoke(
                new object[]{new SaveDocument(Guid.NewGuid(),checkpoint),DateTime.UtcNow,keys,new[]{true,true,true,true}});
            for(int pass=0;pass<2;pass++)
            {
                using var host=new IslandHost(saved,0,keys[0]);
                var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue,playerKey:keys[i])).ToArray();
                var events=clients.Select(_=>new List<HookGameplayEvent>()).ToArray();var clock=Stopwatch.StartNew();TimeSpan now=default;
                try
                {
                    while(clock.Elapsed<TimeSpan.FromSeconds(45))
                    {
                        now=clock.Elapsed;host.Poll(now);
                        for(int i=0;i<clients.Length;i++)
                        {
                            var client=clients[i];
                            client.Poll(now,_=>{
                                if(pass>0 || client.Session?.LocalParticipant==null)return default;
                                var s=client.Session.InspectState();var p=s.Players[client.Slot];var gun=FirearmCatalog.Get(p.Slot);
                                if(p.LastInventoryRequest==0)return Buy(FirearmCatalog.OfferOffset+p.Slot,1,true);
                                if(p.Firearm.Busy || p.Firearm.ShotSequence>=gun.Capacity+1)return default;
                                var held=s.Tackle!.Inventory.Equipped(p.Slot,EquipmentSlot.RightHand);
                                if(held?.Kind!=ItemKind.Firearm || held.Value.Type!=p.Slot)return default;
                                return new HookInput(0,0,p.X,-80000,fire:true,reload:held.Value.LoadedAmmo==0);
                            });
                            events[i].AddRange(client.ConfirmedEvents.Select(e=>e.Event));Require(client.Failure==null,client.Failure??"");
                        }
                        Require(host.Failure==null,host.Failure??"");
                        if(host.Session?.ConfirmedTick>30 && host.Session.InspectState().Players.All(p=>p.Firearm.ShotSequence>=FirearmCatalog.Get(p.Slot).Capacity+1 && !p.Firearm.Busy))break;
                        Thread.Sleep(1);
                    }
                    for(int n=0;n<250;n++)
                    {host.Poll(now);for(int i=0;i<4;i++){clients[i].Poll(now,_=>default);events[i].AddRange(clients[i].ConfirmedEvents.Select(e=>e.Event));}}
                    Require(host.Session!=null,"Long-gun room failed to start");var state=host.Session!.InspectState();
                    Require(state.Players.All(p=>p.Firearm.ShotSequence==FirearmCatalog.Get(p.Slot).Capacity+1),"Long-gun shooting did not finish: "+string.Join(";",state.Players.Select(p=>$"{p.Slot}: {p.Firearm.ShotSequence}, {p.LastInventoryResult}")));
                    var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());
                    foreach(var client in clients)Require(bytes.SequenceEqual(CheckpointCodec.Encode(client.Session!.CaptureCheckpoint())),"Long-gun checkpoints diverged");
                    var expected=events[0].Select(e=>Convert.ToBase64String(HookGameplayEventCodec.Encode(e))).ToArray();
                    foreach(var list in events)Require(expected.SequenceEqual(list.Select(e=>Convert.ToBase64String(HookGameplayEventCodec.Encode(e)))),"Confirmed gun events differ");
                    Require(state.Tackle!.Inventory.Items.Count(i=>i.Kind==ItemKind.Firearm)==8,"Save/restore duplicated guns");
                    foreach(var p in state.Players)
                    {
                        var gun=FirearmCatalog.Get(p.Slot);Require(p.Coins==3000-gun.Price && p.CoinsEarned==3000,"Wrong purchase price or starting credit repeated");
                        Require(state.Tackle.Inventory.ReplenishedAmmunition[gun.AmmoType]==gun.Capacity,"Wrong replenishment ledger");
                    }
                    Require(pass==0?events[0].Count(e=>e.Kind==HookGameplayEventKind.FirearmReloaded)==4:!events[0].Any(e=>e.Kind==HookGameplayEventKind.FirearmShot || e.Kind==HookGameplayEventKind.FirearmReloaded),"Reload duplicated or restored shot replayed");
                    Require(host.CanSave,"No safe save boundary");string path=Path.GetFullPath("artifacts/firearms/long-guns-network.hisv");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var store=new IslandSaveStore(path);store.Save(host.CaptureSave());saved=store.Load();
                    Require(bytes.SequenceEqual(CheckpointCodec.Encode(saved.Document.Checkpoint)),"Saved gun state changed");
                    Console.WriteLine($"LONG_GUN_UDP pass={pass} tick={state.Steps} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
                }
                finally{foreach(var client in clients)client.Dispose();}
            }
        });
    }
}

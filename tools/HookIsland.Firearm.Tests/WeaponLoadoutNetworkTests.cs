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
    private static IEnumerable<(string,Action)> WeaponNetworkCases()
    {
        yield return ("loadout_four_udp_clients_full_bags_switch_fire_cancel_reload_save_and_rejoin",()=>{
            var map=WeaponMap();var keys=Enumerable.Range(0,4).Select(i=>Enumerable.Repeat((byte)(30+i),32).ToArray()).ToArray();
            var roster=Enumerable.Range(0,4).Select(i=>new Participant((ushort)i,new Guid(i+1,0,0,new byte[8]))).ToArray();
            var runner=new SimulationRunner<HookState>(HookGame.CreateModule(false),new SessionDescriptor(Guid.NewGuid(),1,30,roster),
                new RuntimeLimits(maxParticipants:4,maxInputBytes:HookInput.ByteCount,maxCheckpointBytes:HookGame.MaximumStateBytes+65536),new ByteString(map.Encode()));
            var filled=FillWeaponBackpacks(FreshGame(4,map));var players=filled.Players.ToArray();
            for(int i=0;i<4;i++){players[i].X=players[i].HookX=-1200+i*800;players[i].Z=players[i].HookZ=-1400;}
            var checkpoint=CheckpointMigration.WithApplicationState(runner.CaptureCheckpoint(),HookGame.CreateModule().Manifest,
                new ByteString(new HookGame().Encode(Replace(filled,players))));
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
                        for(int i=0;i<4;i++)
                        {
                            var client=clients[i];
                            client.Poll(now,_=>client.Session?.LocalParticipant==null?default:
                                pass==0?WeaponNetworkInput(client.Session.InspectState(),client.Slot):SelectWeapon(EquipmentSlot.PrimaryWeapon,7));
                            events[i].AddRange(client.ConfirmedEvents.Select(e=>e.Event));Require(client.Failure==null,client.Failure??"");
                        }
                        Require(host.Failure==null,host.Failure??"");
                        if(host.Session?.ConfirmedTick>30 && host.Session.InspectState().Players.All(p=>p.LastInventoryRequest==7 && !p.Firearm.Busy && !p.Melee.Active))break;
                        Thread.Sleep(1);
                    }
                    for(int n=0;n<250;n++)
                    {host.Poll(now);for(int i=0;i<4;i++){clients[i].Poll(now,_=>default);events[i].AddRange(clients[i].ConfirmedEvents.Select(e=>e.Event));}}
                    Require(host.Session!=null,"Weapon room failed to start.");var state=host.Session!.InspectState();var inv=state.Tackle!.Inventory;
                    Require(state.Players.All(p=>p.LastInventoryRequest==7 && p.LastInventoryResult==InventoryResult.Success && p.Firearm.ShotSequence==2 && p.Melee.Serial==1),
                        "Switch sequence incomplete: "+string.Join(";",state.Players.Select(p=>$"{p.Slot}: request {p.LastInventoryRequest}/{p.LastInventoryResult}, shots {p.Firearm.ShotSequence}, melee {p.Melee.Serial}")));
                    foreach(var p in state.Players)
                    {
                        int type=p.Slot==3?1:p.Slot+1;
                        Require(inv.ActiveItem(p.Slot)?.Id==inv.Equipped(p.Slot,EquipmentSlot.PrimaryWeapon)?.Id && inv.ActiveItem(p.Slot)?.Type==type,"Peers lost active category.");
                        Require(inv.Equipped(p.Slot,EquipmentSlot.Sidearm)?.LoadedAmmo==6 && inv.ActiveItem(p.Slot)?.LoadedAmmo==FirearmCatalog.Get(type).Capacity-1,"Switch or interrupted reload refilled ammunition.");
                        Require(p.Coins==3000-FirearmCatalog.Get(type).Price && inv.Items.Where(v=>v.Owner==p.Slot && v.Location==ItemLocation.Backpack).All(v=>v.Kind==ItemKind.Wood),"Switch displaced gear into backpack or spent money.");
                    }
                    Require(inv.Items.Count(i=>i.Kind==ItemKind.Firearm)==8 && !inv.Items.Any(i=>i.Kind==ItemKind.Rod) && inv.ReplenishedAmmunition.All(n=>n==0),"Unexpected weapon or ammunition grant.");
                    var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());
                    foreach(var client in clients)Require(bytes.SequenceEqual(CheckpointCodec.Encode(client.Session!.CaptureCheckpoint())),"Weapon client checkpoints diverged.");
                    var expected=events[0].Select(e=>Convert.ToBase64String(HookGameplayEventCodec.Encode(e))).ToArray();
                    foreach(var list in events)Require(expected.SequenceEqual(list.Select(e=>Convert.ToBase64String(HookGameplayEventCodec.Encode(e)))),"Weapon confirmed feedback differs.");
                    Require(pass==0?events[0].Count(e=>e.Kind==HookGameplayEventKind.FirearmShot)==8:!events[0].Any(e=>e.Kind==HookGameplayEventKind.FirearmShot),"Shots duplicated after save/retry.");
                    Require(host.CanSave,"Weapon switching did not reach a save boundary.");
                    string path=Path.GetFullPath("artifacts/weapon-loadout/four-client-save.hisv");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var store=new IslandSaveStore(path);store.Save(host.CaptureSave());saved=store.Load();
                    Require(bytes.SequenceEqual(CheckpointCodec.Encode(saved.Document.Checkpoint)),"Saved active loadout changed.");
                    Console.WriteLine($"WEAPON_LOADOUT_UDP pass={pass} clients=4 tick={state.Steps} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
                }
                finally{foreach(var client in clients)client.Dispose();}
            }
        });
    }
    private static HookInput WeaponNetworkInput(HookState state,int slot)
    {
        var p=state.Players[slot];
        if(p.Firearm.DrawTicks>0 || p.Firearm.RecoveryTicks>0 || p.Melee.Active)return default;
        switch(p.LastInventoryRequest)
        {
            case 0:return Buy(FirearmCatalog.OfferOffset+(slot==3?1:slot+1),1,true);
            case 1:return p.Firearm.ShotSequence==0?new HookInput(0,0,p.X,-50000,fire:true):SelectWeapon(EquipmentSlot.MeleeWeapon,2);
            case 2:return p.Melee.Serial==0?new HookInput(0,0,p.X,-50000,fire:true):SelectWeapon(EquipmentSlot.Sidearm,3);
            case 3:
                if(p.Firearm.ShotSequence<2)return new HookInput(0,0,p.X,-50000,fire:true);
                return p.Firearm.Reloading?SelectWeapon(EquipmentSlot.MeleeWeapon,4):new HookInput(0,0,reload:true);
            case 4:return SelectWeapon(EquipmentSlot.PrimaryWeapon,5);
            case 5:return SelectWeapon(EquipmentSlot.Sidearm,6);
            case 6:return SelectWeapon(EquipmentSlot.PrimaryWeapon,7);
            default:return default;
        }
    }
}

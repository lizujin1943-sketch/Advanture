using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.FirearmTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> NetworkCases()
    {
        yield return ("four_udp_clients_buy_shoot_reload_confirm_events_save_and_rejoin",()=>{
            var map=Map();var keys=Enumerable.Range(0,4).Select(i=>Enumerable.Repeat((byte)(i+1),32).ToArray()).ToArray();
            IslandSave saved;uint[] sequences;
            using(var host=new IslandHost(map,0,4))
            {
                var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue,playerKey:keys[i])).ToArray();
                var events=clients.Select(_=>new List<HookGameplayEvent>()).ToArray();var clock=Stopwatch.StartNew();TimeSpan now=default;bool starterVerified=false;
                try
                {
                    while(clock.Elapsed<TimeSpan.FromSeconds(100))
                    {
                        now=clock.Elapsed;host.Poll(now);
                        if(!starterVerified && host.Session!=null)
                        {
                            var initial=host.Session.InspectState();var inventory=initial.Tackle!.Inventory;
                            Require(inventory.Items.Count(item=>item.Kind==ItemKind.Firearm)==4,"Network room did not start with four pistols");
                            foreach(var player in initial.Players)
                            {
                                var gun=QuickbarRules.Resolve(inventory,player.Slot,5);
                                Require(gun?.Kind==ItemKind.Firearm && gun.Value.Owner==player.Slot && gun.Value.LoadedAmmo==7 && player.Coins==0,"Network starter pistol ownership, ammunition or binding is incorrect");
                            }
                            starterVerified=true;
                        }
                        for(int i=0;i<4;i++){var c=clients[i];c.Poll(now,t=>c.Session?.LocalParticipant==null?default:NetworkInput(map,c.Session.InspectState(),c.Slot,t));events[i].AddRange(c.ConfirmedEvents.Select(e=>e.Event));Require(c.Failure==null,c.Failure??"");}
                        Require(host.Failure==null,host.Failure??"");
                        if(host.Session?.InspectState().Players.Take(2).All(p=>p.Firearm.ShotSequence>=8 && !p.Firearm.Busy)==true)break;
                        Thread.Sleep(1);
                    }
                    for(int n=0;n<250;n++){host.Poll(now);for(int i=0;i<4;i++){clients[i].Poll(now,_=>default);events[i].AddRange(clients[i].ConfirmedEvents.Select(e=>e.Event));}}
                    Require(host.Session!=null,"No network session");var state=host.Session!.InspectState();
                    Require(starterVerified && state.Tackle!.Inventory.Items.Count(item=>item.Kind==ItemKind.Firearm)==6,"Starter and purchased gun instances did not coexist");
                    Require(state.Players.Take(2).All(p=>p.Firearm.ShotSequence>=8),"Network gun flow incomplete: "+string.Join(";",state.Players.Select(p=>$"{p.Slot}:coins{p.Coins} shots{p.Firearm.ShotSequence} inv{p.LastInventoryRequest}/{p.LastInventoryResult} pos{p.X},{p.Z}")));
                    var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());foreach(var c in clients)Require(bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint())),"Gun confirmed checkpoints diverged");
                    var expected=events[0].Select(e=>Convert.ToBase64String(HookGameplayEventCodec.Encode(e))).ToArray();foreach(var list in events)Require(expected.SequenceEqual(list.Select(e=>Convert.ToBase64String(HookGameplayEventCodec.Encode(e)))),"Gun event content/order differs among peers");
                    Require(events[0].Count(e=>e.Kind==HookGameplayEventKind.FirearmShot)==state.Players.Sum(p=>(long)p.Firearm.ShotSequence) && events[0].Count(e=>e.Kind==HookGameplayEventKind.FirearmReloaded)>=2,"Confirmed gun feedback missing or duplicated");
                    Require(state.Players[0].Vitals.Health<100 && state.Players[1].Vitals.Health<100,"Network friendly damage missing");
                    Require(state.Tackle!.Inventory.ReplenishedAmmunition[0]==14 && state.Tackle.Inventory.PurchasedAmmunition[0]==0 && !state.Tackle.Inventory.Items.Any(i=>i.Kind==ItemKind.Ammunition),"Network reload used finite reserve or duplicated replenishment");
                    Require(host.CanSave,"Action clocks never reached save boundary");saved=host.CaptureSave();sequences=state.Players.Select(p=>p.Firearm.ShotSequence).ToArray();
                    string path=Path.GetFullPath("artifacts/firearms/network-save.hisv");Directory.CreateDirectory(Path.GetDirectoryName(path)!);var store=new IslandSaveStore(path);store.Save(saved);saved=store.Load();
                    Require(bytes.SequenceEqual(CheckpointCodec.Encode(saved.Document.Checkpoint)),"Disk save changed gun state");
                    Console.WriteLine($"FIREARM_UDP clients=4 shots={string.Join(",",sequences)} tick={state.Steps} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
                }
                finally{foreach(var c in clients)c.Dispose();}
            }
            using(var host=new IslandHost(saved,0,keys[0]))
            {
                var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue,playerKey:keys[i])).ToArray();var clock=Stopwatch.StartNew();TimeSpan now=default;
                try
                {
                    while(clock.Elapsed<TimeSpan.FromSeconds(20))
                    {
                        now=clock.Elapsed;host.Poll(now);foreach(var c in clients){c.Poll(now,_=>default);Require(c.Failure==null,c.Failure??"");}Require(host.Failure==null,host.Failure??"");
                        if(host.Session?.ConfirmedTick>=20)break;Thread.Sleep(1);
                    }
                    for(int n=0;n<250;n++){host.Poll(now);foreach(var c in clients)c.Poll(now,_=>default);}
                    Require(host.Session!=null && host.Session.ConfirmedTick>=20,"Saved gun room did not restart");var state=host.Session!.InspectState();Require(state.Players.Select(p=>p.Firearm.ShotSequence).SequenceEqual(sequences),"Save rejoin replayed gun shot");
                    Require(state.Tackle!.Inventory.Items.Count(item=>item.Kind==ItemKind.Firearm)==6,"Save rejoin duplicated starter pistols");
                    var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());foreach(var c in clients)Require(bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint())),"Restored gun clients diverged");
                    Console.WriteLine($"FIREARM_SAVE_REJOIN clients=4 tick={state.Steps} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
                }
                finally{foreach(var c in clients)c.Dispose();}
            }
        });
    }
    private static HookInput NetworkInput(IslandMap map,HookState s,int slot,ulong requestedTick)
    {
        var p=s.Players[slot];if(slot==0 && s.RoomRules.FriendlyPranksEnabled)return new HookInput(0,0,roomRules:new RoomRulesCommand(false,s.RoomRules.LastRequest+1));
        if(slot>1)return default;int lead=(int)Math.Min(30,requestedTick>s.Steps?requestedTick-s.Steps-1:0);
        if(p.CoinsEarned<300)
        {
            var walk=Walk(p,new Point(slot==0?-1400:2600,slot==0?-2600:-1400),lead);if(walk.MoveX!=0 || walk.MoveZ!=0)return walk;
            if(p.Phase!=HookPhase.Ready || p.Cooldown>0)return default;
            int speed=FishingRules.Speed(s,p),range=FishingRules.Range(map,s,p);if(speed<=0)return default;
            foreach(var fish in s.Fish)
            {
                if(fish.Owner>=0)continue;var future=FishMotion.Predict(fish,map.FishSpots[fish.Id],lead);
                for(int ticks=1;ticks<=Math.Min(120,range*HookGame.Rate/speed+1);ticks++)
                {
                    FishMotion.Advance(ref future,map.FishSpots[fish.Id]);if(future.Phase!=FishPhase.Jumping || future.Y<450)continue;
                    int d=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
                    if(d<=range && Math.Abs(d-ticks*speed/HookGame.Rate)<speed/HookGame.Rate/2+40 && (long)FishCatalog.DrainPerTick(fish)*(d*HookGame.Rate/s.Tackle!.Config.ReelSpeed+4)<p.Stamina)return new HookInput(0,0,future.X,future.Z,true);
                }
            }
            return default;
        }
        if(p.Phase!=HookPhase.Ready)return default;
        var move=Walk(p,new Point(slot==0?-1400:1400,-1400),lead);if(move.MoveX!=0 || move.MoveZ!=0)return move;
        if(p.LastInventoryRequest==0)return Buy(FirearmCatalog.OfferOffset,1,true);
        if(s.Players.Take(2).Any(v=>v.LastInventoryRequest<1) || p.Firearm.Busy || p.Firearm.ShotSequence>=8)return default;
        var gun=s.Tackle!.Inventory.Equipped(slot,EquipmentSlot.RightHand);if(gun?.Kind!=ItemKind.Firearm)return default;
        if(gun.Value.LoadedAmmo==0)return new HookInput(0,0,reload:true);
        var target=s.Players[slot^1];return new HookInput(0,0,p.Firearm.ShotSequence==0?target.X:p.X,p.Firearm.ShotSequence==0?target.Z:-14000,fire:p.Firearm.CooldownTicks==0);
    }
    private static HookInput Walk(Fisher p,Point target,int lead)
    {
        short Speed(int d)=>(short)Math.Max(-1000,Math.Min(1000,d*10/(lead+3)));
        if(Math.Abs(target.X-p.X)>60)return new HookInput(Speed(target.X-p.X),0);if(Math.Abs(target.Z-p.Z)>60)return new HookInput(0,Speed(target.Z-p.Z));return default;
    }
}

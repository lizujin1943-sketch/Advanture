using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.CrabTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> NetworkCases()
    {
        yield return ("four_udp_clients_multi_crabs_piranha_kill_rewards_and_new_save_round_trip",()=>{
            var map=Map(new CrabConfig(initialDelayTicks:30,damage:1,knockback:1,windupTicks:300,count:3,spawnStaggerTicks:240),firearms:true,
                enemies:new EnemyConfig(piranhaCount:1,monsterEnabled:false,patrolSpeed:30,piranhaRespawnTicks:18000));using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan now=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(45))
                {
                    now=clock.Elapsed;host.Poll(now);
                    foreach(var client in clients)
                    {
                        client.Poll(now,t=>{
                            if(client.Session?.LocalParticipant==null)return default;
                            var s=client.Session.InspectState();var p=s.Players[client.Slot];var crab=s.Crabs[0];
                            if(p.Firearm.ShotSequence==0)
                            {
                                if(FirearmRules.Equipped(s,p)==null)
                                {var gun=s.Tackle!.Inventory.Items.Single(i=>i.Owner==p.Slot && i.Kind==ItemKind.Firearm);return new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,gun.Id,requestId:1));}
                                return new HookInput(0,0,p.X*10,p.Z*10,fire:true);
                            }
                            if(p.Firearm.Busy)return default;
                            if(crab.Rewarded)
                            {
                                // The first kill exercises the pan; then slot zero uses the gun on a piranha.
                                if(p.Slot!=0 || p.Melee.Active || s.Enemies.Piranhas[0].RespawnTicks>0)return default;
                                // Clear teammates' bodies before shooting; a ray from inside a friend hits that friend.
                                int walkX=-2500-p.X,walkZ=-p.Z,walk=HookGame.Sqrt((long)walkX*walkX+(long)walkZ*walkZ);
                                if(walk>100)return new HookInput((short)(walkX*1000/(walk+1)),(short)(walkZ*1000/(walk+1)),-2500,0);
                                if(FirearmRules.Equipped(s,p)==null)
                                {var gun=s.Tackle!.Inventory.Items.Single(i=>i.Owner==p.Slot && i.Kind==ItemKind.Firearm);return new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,gun.Id,requestId:3));}
                                if(s.Tackle!.Inventory.Equipped(p.Slot,EquipmentSlot.RightHand)!.Value.LoadedAmmo==0)return new HookInput(0,0,reload:true);
                                var fish=s.Enemies.Piranhas[0];return new HookInput(0,0,fish.X,fish.Z,fire:true);
                            }
                            if(MeleeRules.Equipped(s,p)==null)
                            {var pan=s.Tackle!.Inventory.Items.First(i=>i.Owner==p.Slot && i.Kind==ItemKind.Weapon);return new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,pan.Id,requestId:2));}
                            if(!crab.Damageable || crab.Phase==CrabPhase.Climbing || (crab.Attackers&(1<<p.Slot))!=0)return default;
                            int dx=crab.X-p.X,dz=crab.Z-p.Z,length=HookGame.Sqrt((long)dx*dx+(long)dz*dz);
                            bool attack=length<=1300;
                            // Ceil the integer length so quantization cannot exceed the canonical unit input circle.
                            return new HookInput(attack?(short)0:(short)(dx*1000/(length+1)),attack?(short)0:(short)(dz*1000/(length+1)),crab.X,crab.Z,attack);
                        });
                        Require(client.Failure==null,client.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session?.InspectState().Steps>=600)break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session!=null && host.Session.InspectState().Steps>=600,"Network round incomplete");
                for(int i=0;i<300;i++){host.Poll(now);foreach(var c in clients)c.Poll(now,_=>default);}
                var state=host.Session!.InspectState();Require(state.Crabs[0].Rewarded && state.Crabs[0].Attackers==15,
                    $"Not all four attacked and killed: crab={state.Crabs[0].Phase}/{state.Crabs[0].Health}, attackers={state.Crabs[0].Attackers}; "+
                    string.Join(";",state.Players.Select(p=>$"slot={p.Slot} shots={p.Firearm.ShotSequence} inventory={p.LastInventoryRequest}/{p.LastInventoryResult} health={p.Vitals.Health} water={p.Swimming.InWater}")));
                int fishGold=PiranhaFishing.Coins(map.Fishing!.Enemies,state.Enemies.Piranhas[0]);
                Require(state.Crabs.Count==3 && state.Crabs.All(c=>c.Generation==1),"Four-client population did not spawn three crabs");
                Require(state.Enemies.Piranhas[0].DamageTaken==map.Fishing.Melee.PiranhaHealth && state.Enemies.Piranhas[0].RewardOwner==0,
                    $"Network piranha kill did not settle: damage={state.Enemies.Piranhas[0].DamageTaken}, respawn={state.Enemies.Piranhas[0].RespawnTicks}, pos={state.Enemies.Piranhas[0].X},{state.Enemies.Piranhas[0].Z}; "+
                    string.Join(";",state.Players.Select(p=>$"slot={p.Slot} shots={p.Firearm.ShotSequence} inventory={p.LastInventoryRequest}/{p.LastInventoryResult} health={p.Vitals.Health} water={p.Swimming.InWater} pos={p.X},{p.Z}")));
                Require(state.Players.All(p=>p.Coins==44+(p.Slot==0?fishGold:0) && p.FishbonesEarned==2),"Wrong four-player combat rewards");
                Require(state.Players.All(p=>p.Firearm.ShotSequence>=1),"Merged simulation did not advance firearms");
                var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(c.Session!=null && bytes.SequenceEqual(CheckpointCodec.Encode(c.Session.CaptureCheckpoint())),"Confirmed crab checkpoint differs");
                Require(host.CanSave,"Merged room never reached a safe save boundary");
                string path=Path.GetFullPath("artifacts/crabs/firearms-crab-save.hisv");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var store=new IslandSaveStore(path);store.Save(host.CaptureSave());
                var saved=store.Load();Require(bytes.SequenceEqual(CheckpointCodec.Encode(saved.Document.Checkpoint)),"New save lost firearm or crab state");
                var restored=new SimulationRunner<HookState>(HookGame.CreateModule(),saved.Document.Checkpoint.Session,saved.Document.Checkpoint.Limits,saved.Document.Checkpoint.InitializationData);
                restored.Restore(saved.Document.Checkpoint);Same(state,restored.InspectState());
                var model=new HookGame().Create();model.Initialize(new FrameSync.Abstractions.SimulationInitialization(host.Session.CaptureCheckpoint().Session,new FrameSync.Abstractions.ByteString(map.Encode())));
                model.Restore(new HookGame().Decode(new HookGame().Encode(state)));Same(state,model.Capture());
                Console.WriteLine($"CRAB_UDP clients=4 tick={state.Steps} attackers={state.Crabs[0].Attackers} gold={state.Coins} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
    }
}

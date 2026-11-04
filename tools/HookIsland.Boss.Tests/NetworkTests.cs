using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.BossTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> NetworkCases()
    {
        yield return ("four_udp_clients_boss_fight_checkpoints_and_save",()=>{
            var map=Map(new BossConfig(bodyHealth:350),arenaSpawns:true);using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan now=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(45))
                {
                    now=clock.Elapsed;host.Poll(now);
                    foreach(var client in clients)
                    {
                        client.Poll(now,_=>{
                            if(client.Session?.LocalParticipant==null)return default;
                            var s=client.Session.InspectState();var p=s.Players[client.Slot];
                            if(s.Boss.Phase==BossPhase.Dormant)return p.Slot==0?new HookInput(0,0,bossRequest:1):default;
                            if(FirearmRules.Equipped(s,p)==null)
                            {var gun=s.Tackle!.Inventory.Items.Single(i=>i.Owner==p.Slot && i.Kind==ItemKind.Firearm);return new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,gun.Id,requestId:1));}
                            if(p.Slot==0)
                            {int gx=-3000-p.X,gz=16000-p.Z,walk=HookGame.Sqrt((long)gx*gx+(long)gz*gz);if(walk>100)return new HookInput((short)(gx*1000/(walk+1)),(short)(gz*1000/(walk+1)),aimZ:28000);}
                            if(!s.Boss.Damageable)return default;
                            var eye=BossGeometry.Proxies(s.Boss).Single(v=>v.Eye);int x=eye.X-p.X,y=eye.Y-BossGeometry.MuzzleHeight(p),z=eye.Z-p.Z;
                            int distance=HookGame.Sqrt((long)x*x+(long)y*y+(long)z*z);short vertical=(short)(y*1000/Math.Max(1,distance));
                            // 弹药属于当前持枪实例；右手槽保留给鱼竿，查错槽会导致每发后都请求换弹。
                            return new HookInput(0,0,eye.X,eye.Z,fire:true,reload:s.Tackle!.Inventory.ActiveItem(p.Slot)!.Value.LoadedAmmo==0,aimVertical:vertical);
                        });
                        Require(client.Failure==null,client.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");if(host.Session?.InspectState().Steps>=660)break;System.Threading.Thread.Sleep(1);
                }
                Require(host.Session!=null && host.Session.InspectState().Steps>=660,"Network fight incomplete");
                for(int i=0;i<300;i++){host.Poll(now);foreach(var client in clients)client.Poll(now,_=>default);}
                var state=host.Session!.InspectState();Require(state.Boss.Phase==BossPhase.Defeated && state.Boss.Rewarded && state.Boss.Attackers==15,
                    $"No four-player kill: {state.Boss.Phase}/{state.Boss.Health}/mask{state.Boss.Attackers}; "+string.Join(";",state.Players.Select(p=>$"{p.Slot} hp{p.Vitals.Health} pos{p.X},{p.Z} shots{p.Firearm.ShotSequence}")));
                var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());foreach(var client in clients)Require(client.Session!=null && bytes.SequenceEqual(CheckpointCodec.Encode(client.Session.CaptureCheckpoint())),"Confirmed checkpoints differ");
                Require(host.CanSave,"No safe save boundary");string path=Path.GetFullPath("artifacts/boss/boss-four-client.hisv");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var store=new IslandSaveStore(path);store.Save(host.CaptureSave());var save=store.Load();Require(bytes.SequenceEqual(CheckpointCodec.Encode(save.Document.Checkpoint)),"Boss save differs");
                var restored=new SimulationRunner<HookState>(HookGame.CreateModule(),save.Document.Checkpoint.Session,save.Document.Checkpoint.Limits,save.Document.Checkpoint.InitializationData);restored.Restore(save.Document.Checkpoint);Same(state,restored.InspectState());
                Require(state.Players.All(p=>p.Coins==600) && !state.Won,"Reward or optional challenge wrong");
                Console.WriteLine($"BOSS_UDP clients=4 tick={state.Steps} mask={state.Boss.Attackers} coins={state.Coins} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var client in clients)client.Dispose();}
        });
    }
}

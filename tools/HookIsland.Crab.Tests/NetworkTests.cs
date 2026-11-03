using System;
using System.Collections.Generic;
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
        yield return ("four_udp_clients_kill_and_confirm_identical_rewards",()=>{
            var map=Map(new CrabConfig(initialDelayTicks:30,damage:1,windupTicks:60));using var host=new IslandHost(map,0,4);
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
                            var s=client.Session.InspectState();var p=s.Players[client.Slot];var crab=s.Crab;
                            if(MeleeRules.Equipped(s,p)==null)
                            {var pan=s.Tackle!.Inventory.Items.First(i=>i.Owner==p.Slot && i.Kind==ItemKind.Weapon);return new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,pan.Id,requestId:1));}
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
                var state=host.Session!.InspectState();Require(state.Crab.Rewarded && state.Crab.Attackers==15,"Not all four attacked and killed");
                Require(state.Players.All(p=>p.Coins==44 && p.FishbonesEarned==2),"Wrong four-player rewards");
                var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(c.Session!=null && bytes.SequenceEqual(CheckpointCodec.Encode(c.Session.CaptureCheckpoint())),"Confirmed crab checkpoint differs");
                var model=new HookGame().Create();model.Initialize(new FrameSync.Abstractions.SimulationInitialization(host.Session.CaptureCheckpoint().Session,new FrameSync.Abstractions.ByteString(map.Encode())));
                model.Restore(new HookGame().Decode(new HookGame().Encode(state)));Same(state,model.Capture());
                Console.WriteLine($"CRAB_UDP clients=4 tick={state.Steps} attackers={state.Crab.Attackers} gold={state.Coins} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
    }
}

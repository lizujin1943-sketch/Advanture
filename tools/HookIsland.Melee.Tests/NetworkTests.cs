using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.MeleeTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> NetworkCases()
    {
        yield return ("four_udp_clients_confirm_same_pan_swings_hits_and_checkpoint",()=>{
            var map=Map();using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan now=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(30))
                {
                    now=clock.Elapsed;host.Poll(now);
                    foreach(var client in clients)
                    {
                        client.Poll(now,t=>{
                            if(client.Session?.LocalParticipant==null)return default;
                            var state=client.Session.InspectState();var p=state.Players[client.Slot];
                            if(MeleeRules.Equipped(state,p)==null)
                            {var pan=state.Tackle!.Inventory.Items.First(i=>i.Owner==p.Slot && i.Kind==ItemKind.Weapon);return new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,pan.Id,requestId:1));}
                            short move=state.Steps<30 && Math.Abs(p.X)>500?(short)(p.X>0?-1000:1000):(short)0;
                            var target=state.Players[p.Slot^1];
                            return new HookInput(move,0,target.X,target.Z,state.Steps>=35 && state.Steps<230 && p.Melee.CooldownTicks==0);
                        });
                        Require(client.Failure==null,client.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session?.InspectState().Steps>=300)break;
                    System.Threading.Thread.Sleep(1);
                }
                Require(host.Session!=null && host.Session.InspectState().Steps>=300,"Network round never completed");
                for(int i=0;i<300;i++){host.Poll(now);foreach(var client in clients)client.Poll(now,_=>default);}
                var state=host.Session!.InspectState();
                Require(state.Players.All(p=>p.Melee.Serial>=2) && state.Players.Sum(p=>p.Melee.HitCount)>=2,"Not all players attacked or forward impacts failed");
                Require(state.Players.All(p=>p.Vitals.Health==100 && p.Casts==0),"Friendly melee damaged health or cast a hook");
                var bytes=CheckpointCodec.Encode(host.Session.CaptureCheckpoint());
                foreach(var client in clients)Require(client.Session!=null && bytes.SequenceEqual(CheckpointCodec.Encode(client.Session.CaptureCheckpoint())),"Confirmed melee checkpoint diverged");
                new HookGame().Decode(new HookGame().Encode(state));
                Console.WriteLine($"MELEE_UDP clients=4 tick={state.Steps} swings={string.Join(",",state.Players.Select(p=>p.Melee.Serial))} hits={state.Players.Sum(p=>p.Melee.HitCount)} hash={CheckpointCodec.Hash(host.Session.CaptureCheckpoint())}");
            }
            finally{foreach(var client in clients)client.Dispose();}
        });
    }
}

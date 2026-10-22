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
    private static IEnumerable<(string,Action)> NetworkCases()
    {
        yield return ("four_udp_clients_pull_each_other_and_agree_on_release",()=>{
            var map=PullMap(new Point(-6000,-5000),new Point(-1000,-5000),new Point(-6000,6000),new Point(-1000,6000));
            using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var observedPull=new bool[4];
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                // Real sockets need real time: an accelerated clock can expire inputs before the OS delivers them.
                while(clock.Elapsed<TimeSpan.FromSeconds(20))
                {
                    var now=last=clock.Elapsed;host.Poll(now);
                    foreach(var client in clients)
                    {
                        client.Poll(now,t=>{
                            int slot=client.Slot;bool first=slot%2==0;
                            var state=client.Session!.InspectState();var p=state.Players[slot];var partner=state.Players[slot^1];
                            // Retry normal actions until confirmed; a single fixed-tick click may legitimately expire under UDP load.
                            bool reposition=!first && p.TimesHooked>0 && p.PulledBy<0 && p.X<-2400;
                            bool ready=first || p.TimesHooked>0 && p.ProtectionTicks==0 && !reposition;
                            return new HookInput(reposition?(short)1000:(short)0,0,partner.X,partner.Z,
                                ready && p.PlayerHooks==0 && p.Phase==HookPhase.Ready && t%12==0);
                        });
                        Require(client.Failure==null,client.Failure??"");
                        if(client.Session?.LocalParticipant!=null && client.Session.InspectState().Players.Any(p=>p.PulledBy>=0))
                            observedPull[client.Slot]=true;
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && host.Session.ConfirmedTick>180 &&
                        host.Session.InspectState().Players.All(p=>p.PlayerHooks==1 && p.TimesHooked==1 && p.PulledBy<0 && p.HookedPlayer<0))break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<150;i++)
                {
                    var now=last;host.Poll(now);
                    foreach(var c in clients)c.Poll(now,_=>default);
                }
                var state=host.Session!.InspectState();
                Require(observedPull.All(v=>v),"A client never observed a synchronized active pull.");
                Require(state.Players.All(p=>p.PlayerHooks==1 && p.TimesHooked==1 && p.PulledBy<0 && p.HookedPlayer<0),
                    "Four players did not each hook and get hooked exactly once, or a victim stayed locked: "+
                    string.Join("; ",state.Players.Select(p=>$"P{p.Slot} ({p.X},{p.Z}) casts={p.Casts} hooks={p.PlayerHooks} received={p.TimesHooked} target={p.HookedPlayer} owner={p.PulledBy} protection={p.ProtectionTicks}")));
                Require(state.Players[0].X>-6000 && state.Players[1].X<-1000 && state.Coins==0,
                    "Player movement or reward isolation failed.");
                string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash,
                    "Server/client player pull or protection state differs.");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        yield return ("four_udp_clients_spend_fishing_income_and_sync_personal_upgrades",()=>{
            var map=ShopMap();using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var requests=new uint[4];TimeSpan last=default;var clock=System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(140))
                {
                    // A synthetic fast clock can expire real UDP frames before the OS schedules their delivery.
                    var now=last=clock.Elapsed;host.Poll(now);
                    for(int i=0;i<clients.Length;i++)
                    {
                        int index=i;var c=clients[i];
                        c.Poll(now,t=>{
                            if(c.Session?.LocalParticipant==null)return default;
                            var state=c.Session.InspectState();var p=state.Players[c.Slot];
                            var upgrade=c.Slot%2==0?HookUpgrade.Range:HookUpgrade.Speed;
                            int level=upgrade==HookUpgrade.Range?p.RangeLevel:p.SpeedLevel;
                            if(level==0 && state.Coins>=50 && p.Phase==HookPhase.Ready)
                            {
                                if(requests[index]==0 || p.LastPurchaseId==requests[index])requests[index]=p.LastPurchaseId+1;
                                return new HookInput(0,0,upgrade:upgrade,purchaseId:requests[index]);
                            }
                            // Keep resending the last purchase id while fishing to exercise receipt deduplication over UDP.
                            var shot=Intercept(p,state.Fish[0],map.FishSpots[0],Math.Max(0,(int)t-(int)state.Steps-1));
                            return new HookInput(0,0,shot.AimX,shot.AimZ,shot.Fire,upgrade:requests[index]>0?upgrade:HookUpgrade.None,purchaseId:requests[index]);
                        });
                        Require(c.Failure==null,c.Failure??"");
                    }
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && host.Session.InspectState().Players.All(p=>p.RangeLevel+p.SpeedLevel==1))break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<200;i++)
                {
                    var now=last;host.Poll(now);
                    foreach(var c in clients)c.Poll(now,_=>default);
                }
                var state=host.Session!.InspectState();
                Require(state.Players.All(p=>p.RangeLevel+p.SpeedLevel==1),"Not all four players bought a personal upgrade with earned coins: "+
                    string.Join("; ",state.Players.Select(p=>$"P{p.Slot} catches={p.Catches} casts={p.Casts} coins={p.Coins} range={p.RangeLevel} speed={p.SpeedLevel} receipt={p.LastPurchaseId}:{p.LastPurchaseResult}")));
                Require(state.Players.All(p=>p.Slot%2==0?p.RangeLevel==1:p.SpeedLevel==1),"Upgrade applied to the wrong player or item.");
                Require(state.Players.Sum(p=>p.Coins)>=200 && state.Coins==state.Players.Sum(p=>p.Coins)-200,
                    "Network spending duplicated a charge or lost fishing income.");
                string expected=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach(var c in clients)Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==expected,
                    "Server/client upgraded state differs.");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        yield return ("four_real_udp_clients_catch_move_and_agree",()=>{
            using var host=new IslandHost(TestMap(),0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(TestMap(),IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            try
            {
                for(int ms=0;ms<8500;ms++)
                {
                    var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);
                    foreach(var client in clients)client.Poll(now,t=>{
                        var state=client.Session!.InspectState();var p=state.Players[client.Slot];
                        var shot=client.Slot==0 && state.Coins==0?
                            Intercept(p,state.Fish[0],TestMap().FishSpots[0],Math.Max(0,(int)t-(int)state.Steps-1)):default;
                        return new HookInput(t>160?(short)600:(short)0,0,shot.AimX,shot.AimZ,shot.Fire);
                    });
                    Require(host.Failure==null,host.Failure??"");
                    foreach(var c in clients)Require(c.Failure==null,c.Failure??"");
                }
                for(int i=0;i<150;i++)
                {
                    var now=TimeSpan.FromMilliseconds(8499);host.Poll(now);
                    foreach(var c in clients)c.Poll(now,_=>default);
                }
                var s=host.Session!.InspectState();
                Require(s.Players.Count==4 && s.Coins==10 && s.Players.Sum(p=>p.Catches)==1,"Four-player shared reward failed.");
                Require(clients.Select(c=>c.Slot).Distinct().Count()==4,"Auto-assigned duplicate slots.");
                string expected=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());
                foreach(var c in clients)
                {
                    Require(c.Session!.ConfirmedTick>180,"Network stalled.");
                    Require(CheckpointCodec.Hash(c.Session.CaptureCheckpoint())==expected,"Confirmed state differs.");
                }
                Require(s.Players[0].X>-4000,"Input movement not synchronized.");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        yield return ("waiting_lobby_rejects_mismatched_map_and_extra_player",()=>{
            var map=TestMap();using var host=new IslandHost(map,0,2);
            using var a=new IslandClient(map,IPAddress.Loopback,host.Port,0);
            for(int ms=0;ms<12000;ms+=5){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);}
            Require(host.Session==null && a.Failure==null,"Lobby consumed session timeout.");
            var other=new IslandMap(map.Shore,map.Pier,map.Spawns,new[]{new Point(-6000,-13000)});
            using var wrong=new IslandClient(other,IPAddress.Loopback,host.Port,ushort.MaxValue);
            using var b=new IslandClient(map,IPAddress.Loopback,host.Port,ushort.MaxValue);
            for(int ms=12000;ms<13500;ms+=2)
            {var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);wrong.Poll(now,_=>default);b.Poll(now,_=>default);}
            Require(wrong.Failure!=null && b.Session!=null && host.Session!=null,"Lobby validation failed.");
            using var extra=new IslandClient(map,IPAddress.Loopback,host.Port,ushort.MaxValue);
            // The transport may exhaust its bounded handshake budget while active peers exchange data.
            // A full room must reject or time out the newcomer within its 10-second handshake bound.
            for(int ms=13500;ms<26000 && extra.Failure==null;ms+=2)
            {var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);b.Poll(now,_=>default);extra.Poll(now,_=>default);}
            Require(extra.Failure!=null && a.Failure==null && b.Failure==null,
                $"Full room rejection: extra={extra.Failure??"none"}, A={a.Failure??"none"}, B={b.Failure??"none"}, host={host.Failure??"none"}.");
        });
        yield return ("solo_uses_same_session_and_disconnect_releases_port",()=>{
            var map=TestMap();var host=new IslandHost(map,0,1);int port=host.Port;
            using var a=new IslandClient(map,IPAddress.Loopback,port,0);
            try
            {
                for(int ms=0;ms<1500;ms++){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);}
                Require(a.Session?.ConfirmedTick>20,"Solo room did not run.");
            }
            finally{host.Dispose();}
            using var replacement=new IslandHost(map,port,1);
            for(int ms=1500;ms<40000 && a.Failure==null;ms+=10)a.Poll(TimeSpan.FromMilliseconds(ms),_=>default);
            Require(a.Failure!=null,"Disconnected client still showed a live room.");
        });
    }
}

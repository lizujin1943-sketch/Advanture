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
        yield return ("four_real_udp_clients_catch_move_and_agree",()=>{
            using var host=new IslandHost(TestMap(),0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(TestMap(),IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            try
            {
                for(int ms=0;ms<8500;ms++)
                {
                    var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);
                    foreach(var client in clients)client.Poll(now,t=>new HookInput(t>160?(short)600:(short)0,0,-5000,-12000,t==30));
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

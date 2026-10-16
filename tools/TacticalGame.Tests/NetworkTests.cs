using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using Advanture.Tactical.Network;
using Advanture.Tactical.Simulation;
using FrameSync.Runtime;
using FrameSync.Sessions;
namespace Advanture.Tactical.Tests;
internal static class NetworkTests
{
    internal static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return ("network.two_sockets_share_movement_look_and_hash",()=>{
            var map=SimulationTests.OpenMap(); using var host=new TacticalHost(map,0);
            using var a=new TacticalClient(map,IPAddress.Loopback,host.Port,0);
            using var b=new TacticalClient(map,IPAddress.Loopback,host.Port,1);
            var sampledA=new HashSet<ulong>();var sampledB=new HashSet<ulong>();
            for(int ms=0;ms<4000;ms++)
            {
                var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);
                a.Poll(now,t=>{Check.True(sampledA.Add(t),"本地输入被重复采样");return new TacticalInput(0,1000,9000,-1000,true);});
                b.Poll(now,t=>{Check.True(sampledB.Add(t),"本地输入被重复采样");return new TacticalInput(-1000,0,27000,2000,true);});
                Check.True(host.Failure==null,host.Failure??"");Check.True(a.Failure==null,a.Failure??"");Check.True(b.Failure==null,b.Failure??"");
            }
            Check.True(a.Session!.ConfirmedTick>60);Check.True(b.Session!.ConfirmedTick>60);
            // 停住服务器时钟，继续排空在途包，使三端比较同一个最终确认帧。
            for(int i=0;i<100;i++){var now=TimeSpan.FromMilliseconds(3999);host.Poll(now);a.Poll(now,_=>default);b.Poll(now,_=>default);}
            string expected=CheckpointCodec.Hash(host.Session!.CaptureCheckpoint());
            Check.Equal(expected,CheckpointCodec.Hash(a.Session.CaptureCheckpoint()));Check.Equal(expected,CheckpointCodec.Hash(b.Session.CaptureCheckpoint()));
            var state=b.Session.InspectState();Check.True(state.Players[0].Z>8000);Check.True(state.Players[1].X<0);
            Check.Equal((ushort)9000,state.Players[0].Yaw);Check.Equal((ushort)27000,state.Players[1].Yaw);
            Check.Equal((short)-1000,state.Players[0].Pitch);Check.True(a.Session.PredictedTick<=a.LastSubmittedTick);
        });
        yield return ("network.waits_for_second_player_then_rejects_third",()=>{
            var map=SimulationTests.OpenMap();using var host=new TacticalHost(map,0);using var a=new TacticalClient(map,IPAddress.Loopback,host.Port,0);
            for(int ms=0;ms<16000;ms+=5){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);}
            Check.True(host.Session==null,"等待好友时不能提前开始会话准备超时");Check.Equal(1,host.JoinedCount);Check.True(a.Failure==null);
            using var b=new TacticalClient(map,IPAddress.Loopback,host.Port,1);
            for(int ms=16000;ms<17500;ms+=2){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);b.Poll(now,_=>default);}
            Check.Equal(SessionStatus.Running,a.Session!.Status);using var third=new TacticalClient(map,IPAddress.Loopback,host.Port,1);
            for(int ms=17500;ms<18500;ms+=2){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);b.Poll(now,_=>default);third.Poll(now,_=>default);}
            Check.True(third.Failure!=null);Check.True(a.Failure==null);Check.True(b.Failure==null);
        });
        yield return ("network.map_mismatch_is_rejected_before_simulation",()=>{
            var map=SimulationTests.OpenMap();using var host=new TacticalHost(map,0);
            var other=new TacticalMap(map.Area,new[]{new PlayerSpawn(1000,0,0),map.Spawns[1]},map.Obstacles);
            using var a=new TacticalClient(other,IPAddress.Loopback,host.Port,0);
            for(int ms=0;ms<1000;ms+=2){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);}
            Check.True(a.Failure!=null);Check.True(a.Session==null);Check.Equal(0,host.JoinedCount);
        });
        yield return ("network.natural_disconnect_releases_both_ports_without_dispose",()=>{
            var map=SimulationTests.OpenMap();using var host=new TacticalHost(map,0);int hostPort=host.Port;
            using var a=new TacticalClient(map,IPAddress.Loopback,hostPort,0);
            using var b=new TacticalClient(map,IPAddress.Loopback,hostPort,1);int clientPort=a.LocalPort;
            for(int ms=0;ms<500;ms++){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);b.Poll(now,_=>default);}
            // 模拟 B 不再驱动网络，等服务器自然判定超时；A 仍正常收发。
            for(int ms=500;ms<40000 && (host.Failure==null || a.Failure==null);ms+=10)
            {var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);}
            Check.True(host.Failure!=null);Check.True(a.Failure!=null);
            Check.True(host.Session!=null && a.Session!=null,"失败后仍应保留诊断状态");
            // 此处刻意不先 Dispose host/a：进入失败状态就必须立即归还系统端口。
            using var hostAgain=new TacticalHost(map,hostPort);
            using var clientPortAgain=new TacticalHost(map,clientPort);
            host.Poll(TimeSpan.FromSeconds(40));a.Poll(TimeSpan.FromSeconds(40),_=>default);
        });
        yield return ("network.disconnect_releases_socket_and_stops_remote",()=>{
            var map=SimulationTests.OpenMap();var host=new TacticalHost(map,0);int port=host.Port;
            using var a=new TacticalClient(map,IPAddress.Loopback,port,0);using var b=new TacticalClient(map,IPAddress.Loopback,port,1);
            for(int ms=0;ms<500;ms++){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);a.Poll(now,_=>default);b.Poll(now,_=>default);}
            host.Dispose();host.Dispose();using var replacement=new TacticalHost(map,port);
            for(int ms=500;ms<40000 && b.Failure==null;ms+=10)b.Poll(TimeSpan.FromMilliseconds(ms),_=>default);
            Check.True(b.Failure!=null,"断开房主后不能继续假装联网");
        });
    }
}

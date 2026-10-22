using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.EnemyTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> NetworkCases()
    {
        foreach(int count in new[]{1,4})yield return ($"network_{count}_players_interrupt_hit_fall_and_piranhas",()=>{
            var map=Map();using var host=new IslandHost(map,0,count);
            var clients=Enumerable.Range(0,count).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=System.Diagnostics.Stopwatch.StartNew();TimeSpan last=default;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(36))
                {
                    var now=last=clock.Elapsed;host.Poll(now);
                    foreach(var c in clients)
                    {c.Poll(now,_=>c.Session?.LocalParticipant==null?default:EnemySmokePilot.Sample(c.Session.InspectState(),c.Slot));Require(c.Failure==null,c.Failure??"");}
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null && host.Session.ConfirmedTick>=600 && EnemySmokePilot.Complete(host.Session.InspectState()))break;
                    System.Threading.Thread.Sleep(1);
                }
                for(int i=0;i<300;i++){host.Poll(last);foreach(var c in clients)c.Poll(last,_=>default);}
                Require(host.Session!=null,"Room did not start");var s=host.Session!.InspectState();
                Require(EnemySmokePilot.Complete(s),$"Normal-input enemy loop incomplete at {s.Steps}: interrupts={s.Enemies.Interruptions} shots={s.Enemies.Shots} hits={s.Enemies.WaterHits} bites={s.Enemies.Bites} captures={s.Enemies.Captures}");
                string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());foreach(var c in clients)
                    Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash,"Complete enemy checkpoint differs across peers");
                Console.WriteLine($"ENEMY_NETWORK players={count} tick={s.Steps} interrupts={s.Enemies.Interruptions} shots={s.Enemies.Shots} hits={s.Enemies.WaterHits} bites={s.Enemies.Bites} captures={s.Enemies.Captures} hash={hash}");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        yield return ("network_lobby_rejects_enemy_configuration_mismatch",()=>{
            using var host=new IslandHost(Map(),0,2);using var client=new IslandClient(Map(new EnemyConfig(windupTicks:61)),IPAddress.Loopback,host.Port,ushort.MaxValue);
            for(int ms=0;ms<1500 && client.Failure==null;ms+=2){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);client.Poll(now,_=>default);}
            Require(client.Failure!=null && host.Session==null,"Different enemy rules joined room");
        });
    }
}

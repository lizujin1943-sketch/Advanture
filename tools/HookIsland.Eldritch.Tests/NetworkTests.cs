using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.EldritchTests;
internal static partial class Program
{
    static partial void RunNetworkTests()
    {
        Test("four_real_udp_clients_agree_and_restore_active_encounter",()=>{
            var map=Map(new EldritchConfig(initialDelayTicks:1,staggerTicks:1,maxAlive:3,speciesMask:(1<<0)|(1<<5)|(1<<7)));using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=Stopwatch.StartNew();TimeSpan now=default;IslandSave? save=null;bool damaged=false,summoned=false;
            try
            {
                while(clock.Elapsed<TimeSpan.FromSeconds(24))
                {
                    now=clock.Elapsed;host.Poll(now);if(host.JoinedCount==4 && host.CanStart)host.TryStart();
                    foreach(var client in clients){client.Poll(now,_=>default);Require(client.Failure==null,client.Failure??"");}
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null)
                    {
                        var s=host.Session.InspectState();damaged|=s.Players.Any(p=>p.Vitals.Health<10000);summoned|=s.Eldritch.Creatures.Any(c=>c.IsMinion && c.Damageable);
                        if(save==null && host.CanSave && s.Eldritch.Creatures.Any(c=>c.Phase==EldritchPhase.Active))save=host.CaptureSave();
                        if(s.Steps>=210 && damaged && summoned && save!=null)break;
                    }
                    Thread.Sleep(1);
                }
                for(int i=0;i<300;i++){host.Poll(now);foreach(var c in clients)c.Poll(now,_=>default);}
                Require(host.Session!=null && host.Session.InspectState().Steps>=210 && damaged && summoned && save!=null,"Encounter network scenario timed out");
                string hash=CheckpointCodec.Hash(host.Session!.CaptureCheckpoint());
                foreach(var c in clients)Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash,"Peer encounter checkpoint mismatch");
                Directory.CreateDirectory("artifacts/eldritch");var store=new IslandSaveStore(Path.GetFullPath("artifacts/eldritch/active-encounter.hisv"));store.Save(save!);var loaded=store.Load();
                var a=Model(map);var b=Model(map);a.Restore(Codec.Decode(save!.Document.Checkpoint.ApplicationState.Span));b.Restore(Codec.Decode(loaded.Document.Checkpoint.ApplicationState.Span));
                for(int i=0;i<120;i++){Step(a);Step(b);}
                Require(Codec.Encode(a.Capture()).SequenceEqual(Codec.Encode(b.Capture())),"File recovery changed attack outcome");
                Console.WriteLine($"ELDRITCH_UDP clients=4 tick={host.Session.InspectState().Steps} damage={damaged} minions={summoned} hash={hash}");
            }
            finally{foreach(var c in clients)c.Dispose();}
        });
        Test("lobby_rejects_different_eldritch_roster",()=>{
            using var host=new IslandHost(Map(),0,2);using var c=new IslandClient(Map(new EldritchConfig(speciesMask:1)),IPAddress.Loopback,host.Port,ushort.MaxValue);
            for(int ms=0;ms<1500 && c.Failure==null;ms+=2){var now=TimeSpan.FromMilliseconds(ms);host.Poll(now);c.Poll(now,_=>default);}
            Require(c.Failure!=null && host.Session==null,"Mismatched roster joined room");
        });
    }
}

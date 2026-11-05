using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;
using FrameSync.Runtime;

internal static class TownNetworkTests
{
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public static int Run()
    {
        try
        {
            var map=IslandMap.CreateTown();using var host=new IslandHost(map,0,4);
            var clients=Enumerable.Range(0,4).Select(i=>new IslandClient(map,IPAddress.Loopback,host.Port,i==0?(ushort)0:ushort.MaxValue)).ToArray();
            var clock=Stopwatch.StartNew();TimeSpan now=default;IslandSave? save=null;
            try
            {
                HookInput Input(ulong frame)=>frame==30 || frame==250?new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)):new HookInput(0,frame<180?(short)1000:(short)0);
                while(clock.Elapsed<TimeSpan.FromSeconds(26))
                {
                    now=clock.Elapsed;host.Poll(now);if(host.JoinedCount==4 && host.CanStart)host.TryStart();
                    foreach(var c in clients){c.Poll(now,Input);Require(c.Failure==null,c.Failure??"");}
                    Require(host.Failure==null,host.Failure??"");
                    if(host.Session!=null)
                    {
                        var state=host.Session.InspectState();
                        if(save==null && host.CanSave && state.Town.Zones.Any(z=>z.Started) && state.Eldritch.Creatures.Any(c=>c.Damageable))save=host.CaptureSave();
                        if(state.Steps>=360 && save!=null)break;
                    }
                    Thread.Sleep(1);
                }
                for(int i=0;i<300;i++){host.Poll(now);foreach(var c in clients)c.Poll(now,Input);}
                Require(host.Session!=null && host.Session.InspectState().Steps>=360 && save!=null,"Town encounter/save network scenario timed out: tick="+host.Session?.InspectState().Steps+" save="+(save!=null)+" poses="+string.Join(";",host.Session?.InspectState().Players.Select(p=>$"{p.X},{p.Z}")??Array.Empty<string>()));
                var s=host.Session!.InspectState();Require(s.Town.Players.All(p=>p.LastRequest==1 && p.LastResult==TownResult.GoalLocked),"Failed service receipt changed on repeated input: "+string.Join(";",s.Town.Players.Select(p=>$"{p.LastRequest}:{p.LastResult}")));
                string hash=CheckpointCodec.Hash(host.Session.CaptureCheckpoint());foreach(var c in clients)Require(CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash,"Town peer checkpoint mismatch");
                Directory.CreateDirectory("artifacts/drowned-bell-town");var store=new IslandSaveStore(Path.GetFullPath("artifacts/drowned-bell-town/town-encounter.hisv"));store.Save(save!);var loaded=store.Load();
                var codec=new HookGame(false);var a=codec.Create();var b=codec.Create();
                var desc=save!.Document.Checkpoint.Session;var init=new SimulationInitialization(desc,new ByteString(map.Encode()));a.Initialize(init);b.Initialize(init);
                a.Restore(codec.Decode(save!.Document.Checkpoint.ApplicationState.Span));b.Restore(codec.Decode(loaded.Document.Checkpoint.ApplicationState.Span));
                for(int n=0;n<120;n++){ulong tick=a.Capture().Steps+1;var context=new FrameContext(tick,tick,30);a.Step(context,Array.Empty<SimulationInput>());b.Step(context,Array.Empty<SimulationInput>());}
                Require(codec.Encode(a.Capture()).SequenceEqual(codec.Encode(b.Capture())),"Town save recovery diverged");
                Console.WriteLine($"PASS town_four_udp_clients_restore_encounter_and_failed_receipts tick={s.Steps} hash={hash}");return 0;
            }
            finally{foreach(var c in clients)c.Dispose();}
        }
        catch(Exception e){Console.WriteLine("FAIL town_four_udp_clients_restore_encounter_and_failed_receipts: "+e.Message);return 1;}
    }
}

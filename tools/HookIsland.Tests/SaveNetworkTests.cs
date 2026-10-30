using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private sealed class SaveParty:IDisposable
    {
        internal readonly IslandHost Host;internal readonly IslandMap Map;internal readonly List<IslandClient> Clients=new List<IslandClient>();
        private readonly Stopwatch clock=Stopwatch.StartNew();
        internal SaveParty(IslandSave? save=null){Map=save?.Map??RecoveryMap();Host=save==null?new IslandHost(Map,0,4):new IslandHost(save,0);}
        internal IslandClient Add(int identity,bool owner=false)
        {var c=new IslandClient(Map,IPAddress.Loopback,Host.Port,owner?(ushort)0:ushort.MaxValue,playerKey:SaveKey(identity));Clients.Add(c);return c;}
        internal void Poll(Func<IslandClient,ulong,HookInput>? input=null)
        {var now=clock.Elapsed;Host.Poll(now);foreach(var c in Clients)c.Poll(now,t=>input?.Invoke(c,t)??default);Require(Host.Failure==null,Host.Failure??"");}
        internal void Until(Func<bool> done,Func<IslandClient,ulong,HookInput>? input=null)
        {var end=clock.Elapsed+TimeSpan.FromSeconds(15);while(!done() && clock.Elapsed<end){Poll(input);Thread.Sleep(1);}Require(done(),"Save party timed out.");}
        internal void Agree()
        {var now=clock.Elapsed;for(int i=0;i<200;i++){Host.Poll(now);foreach(var c in Clients)c.Poll(now,_=>default);}var bytes=CheckpointCodec.Encode(Host.Session!.CaptureCheckpoint());foreach(var c in Clients)Require(c.Failure==null && bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint())),"Restored client diverged: "+c.Failure);}
        public void Dispose(){foreach(var c in Clients)c.Dispose();Host.Dispose();}
    }
    private static IEnumerable<(string,Action)> SaveNetworkCases()
    {
        yield return ("save_four_players_wait_for_rods_then_restore_original_slots",()=>{
            IslandSave saved;
            using(var party=new SaveParty())
            {
                for(int i=1;i<=4;i++)party.Add(i,i==1);
                party.Until(()=>party.Clients.All(c=>c.ResumeTicket!=null));
                party.Until(()=>party.Host.Session!.InspectState().Players[0].Phase==HookPhase.Flying,
                    (c,t)=>c.Slot==0?new HookInput(0,0,0,-20000,true):default);
                Require(party.Host.CanRequestSave && !party.Host.CanSave,"An in-flight cast was considered safe to save.");
                bool rejected=false;try{party.Host.CaptureSave();}catch(InvalidOperationException){rejected=true;}
                Require(rejected,"Unsafe capture bypassed the save barrier.");
                party.Until(()=>party.Host.CanSave);
                saved=party.Host.CaptureSave();var before=CheckpointCodec.Hash(party.Host.Session!.CaptureCheckpoint());
                Require(before==CheckpointCodec.Hash(saved.Document.Checkpoint),"Save did not capture the authoritative frame.");
                Require(party.Host.Session.Status==FrameSync.Sessions.SessionStatus.Running,"Saving paused/stopped combat.");
            }
            saved=IslandSaveCodec.Decode(IslandSaveCodec.Encode(saved));
            using var resumed=new SaveParty(saved);resumed.Add(1,true);resumed.Until(()=>resumed.Host.CanStart);
            for(int i=0;i<30;i++)resumed.Poll();Require(resumed.Host.Session==null,"Combat advanced before saved teammates returned.");
            foreach(int identity in new[]{4,2,3})resumed.Add(identity);
            resumed.Until(()=>resumed.Host.Session!=null);
            var initial=resumed.Host.Session!.CaptureCheckpoint();
            Require(initial.ApplicationState.Span.SequenceEqual(saved.Document.Checkpoint.ApplicationState.Span) && initial.PersistentTick==saved.Document.Checkpoint.PersistentTick,"Loading reset combat state or advanced offline time.");
            Require(initial.Session.SessionId!=saved.Document.Checkpoint.Session.SessionId && initial.Tick==0,"Restore reused an old input timeline.");
            resumed.Until(()=>resumed.Clients.All(c=>c.ResumeTicket!=null) && resumed.Host.Session!.ConfirmedTick>=15);
            foreach(var c in resumed.Clients)
            {
                var participant=c.Session!.LocalParticipant!;var old=saved.Document.Checkpoint.Membership.Find(c.Slot);
                Require(participant.ParticipantId==old.ParticipantId && participant.Generation>old.Generation,"Character identity or input generation was not restored.");
            }
            Require(resumed.Clients.Select(c=>c.Slot).SequenceEqual(new ushort[]{0,3,1,2}),"Joining in a different order exchanged characters.");resumed.Agree();
        });
        yield return ("save_restore_reserves_existing_characters_and_allows_host_to_start_early",()=>{
            IslandSave saved;using(var p=new SaveParty())
            {for(int i=1;i<=4;i++)p.Add(i,i==1);p.Until(()=>p.Clients.All(c=>c.ResumeTicket!=null));saved=p.Host.CaptureSave();}
            using var resumed=new SaveParty(saved);resumed.Add(1,true);var stranger=resumed.Add(99);
            resumed.Until(()=>stranger.Failure!=null && resumed.Host.CanStart);
            Require(resumed.Host.JoinedCount==1 && resumed.Host.TryStart(),"Unknown player claimed a saved character or owner could not start early.");
            resumed.Clients.Remove(stranger);stranger.Dispose();resumed.Until(()=>resumed.Host.Session!.ConfirmedTick>=5);
            Require(resumed.Host.Session!.InspectState().Players.Skip(1).All(p=>p.Unavailable),"Absent saved characters continued acting.");
            var returning=resumed.Add(2);resumed.Until(()=>returning.ResumeTicket!=null);Require(returning.Slot==1,"Late saved teammate could not reclaim their slot.");resumed.Agree();
        });
    }
}

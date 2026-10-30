using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using Advanture.HookIsland;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private sealed class EventParty:IDisposable
    {
        internal readonly IslandHost Host;
        internal readonly IslandMap Map;
        internal readonly List<IslandClient> Clients=new List<IslandClient>();
        internal readonly Dictionary<IslandClient,List<ConfirmedGameplayEvent>> Events=new Dictionary<IslandClient,List<ConfirmedGameplayEvent>>();
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private TimeSpan offset;
        internal TimeSpan Now=>clock.Elapsed+offset;
        internal void AdvanceClock(TimeSpan amount)=>offset+=amount;
        internal EventParty(int capacity,IslandMap map,IslandSave? save=null)
        {Map=map;Host=save==null?new IslandHost(map,0,capacity):new IslandHost(save,0);}
        internal IslandClient Add(bool owner=false,RoomResumeTicket? ticket=null,byte[]? key=null)
        {
            var c=new IslandClient(Map,IPAddress.Loopback,Host.Port,owner?(ushort)0:ushort.MaxValue,ticket,key);
            Clients.Add(c);Events.Add(c,new List<ConfirmedGameplayEvent>());return c;
        }
        internal void Poll(Func<IslandClient,ulong,HookInput>? input=null,TimeSpan? fixedNow=null)
        {
            var now=fixedNow??Now;Host.Poll(now);
            foreach(var c in Clients)
            {c.Poll(now,t=>input?.Invoke(c,t)??default);Events[c].AddRange(c.ConfirmedEvents);Require(c.Failure==null,c.Failure??"");}
            Require(Host.Failure==null,Host.Failure??"");
        }
        internal void Until(Func<bool> done,Func<IslandClient,ulong,HookInput>? input=null,int seconds=30)
        {
            var end=Now+TimeSpan.FromSeconds(seconds);while(!done() && Now<end){Poll(input);Thread.Sleep(1);}
            Require(done(),"Event network scenario timed out: host tick="+Host.Session?.ConfirmedTick+" clients="+string.Join(";",Clients.Select(c=>$"{c.Slot}:e{c.RoundEpoch}:tick{c.Session?.ConfirmedTick}:events{Events[c].Count}:"+(c.Session==null?"preparing":string.Join(",",c.Session.InspectState().Players.Select(p=>$"p{p.Slot}({p.X},{p.Z})h{p.Vitals.Health}/coins{p.Coins}/dead{p.Revival.Dead}/cast{p.Casts}"))))));
        }
        internal void Drain()
        {var now=Now;for(int i=0;i<250;i++)Poll(fixedNow:now);}
        internal void Agree()
        {
            Drain();var bytes=CheckpointCodec.Encode(Host.Session!.CaptureCheckpoint());
            foreach(var c in Clients)Require(bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint())),"Event scenario confirmed checkpoint differs.");
        }
        public void Dispose(){foreach(var c in Clients)c.Dispose();Host.Dispose();}
    }
    private static string EventIdentity(ConfirmedGameplayEvent e)=>$"{e.SessionId}:{e.Epoch}:{e.Tick}:{e.Index}";
    private static string EventContent(ConfirmedGameplayEvent e)=>EventIdentity(e)+":"+Convert.ToBase64String(HookGameplayEventCodec.Encode(e.Event));
    private static IEnumerable<(string,Action)> GameplayEventNetworkCases()
    {
        yield return ("gameplay_events_four_udp_clients_confirm_identical_ordered_results_and_state",()=>{
            var basis=CooperativeFishingSmokePilot.CreateMap();var map=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,
                new FishingConfig(reelSpeed:600,enemies:basis.Fishing!.Enemies,combo:new ComboConfig(goldBonusPercent:new[]{50})));
            using var party=new EventParty(4,map);for(int i=0;i<4;i++)party.Add(i==0);
            var pilots=party.Clients.ToDictionary(c=>c,c=>new CooperativeFishingSmokePilot());
            party.Until(()=>party.Host.Session?.InspectState().Players[0].Catches==1 && party.Host.Session.InspectState().Players[1].Catches==1,
                (c,t)=>c.Session?.LocalParticipant==null || c.Session.InspectState().Players.Any(p=>p.Catches>0)?default:pilots[c].Sample(map,c.Session.InspectState(),c.Slot,t),60);
            party.Agree();var expected=party.Events[party.Clients[0]].Select(EventContent).ToArray();
            Require(expected.Length>0,"Network produced no confirmed events.");
            foreach(var c in party.Clients)
            {
                var events=party.Events[c];Require(events.Select(EventContent).SequenceEqual(expected),"Four clients received different confirmed event contents/order.");
                Require(events.Select(EventIdentity).Distinct().Count()==events.Count,"Confirmed event identity was delivered twice.");
                Require(events.Count(e=>e.Event.Kind==HookGameplayEventKind.FishCaught)==2 && events.Count(e=>e.Event.Kind==HookGameplayEventKind.CoinsReceived)==2,"Shared settlement emitted an incorrect number of outcomes.");
                var count=events.Count;c.Poll(party.Now,_=>default);Require(c.ConfirmedEvents.Count==0 && events.Count==count,"Repeated poll replayed a settled event.");
            }
            Console.WriteLine($"GAMEPLAY_EVENTS_UDP clients=4 events={expected.Length} tick={party.Host.Session!.ConfirmedTick} hash={CheckpointCodec.Hash(party.Host.Session.CaptureCheckpoint())}");
        });
        yield return ("gameplay_events_prediction_is_silent_then_confirmation_emits_once",()=>{
            using var party=new EventParty(1,RecoveryMap());var c=party.Add(true);party.Until(()=>c.ResumeTicket!=null);party.Drain();
            var tick=c.Session!.ConfirmedTick;var before=party.Events[c].Count;
            party.AdvanceClock(TimeSpan.FromMilliseconds(500));
            c.Poll(party.Now,_=>new HookInput(0,0,0,-20000,true));
            Require(c.Session.ConfirmedTick==tick && c.ConfirmedEvents.Count==0,"Speculation escaped as confirmed feedback.");
            Require(c.Session.InspectPredictedState().Players[0].Casts>c.Session.InspectState().Players[0].Casts,"Fixture did not predict a cast.");
            party.Until(()=>party.Events[c].Count>before);party.Drain();
            Require(party.Events[c].Skip(before).Count(e=>e.Event.Kind==HookGameplayEventKind.HookCast)==1,"Predicted cast was dropped or replayed at confirmation.");
        });
        yield return ("gameplay_events_reconnect_restart_and_saved_load_are_quiet_baselines_with_new_events",()=>{
            IslandSave saved;Guid originalId;
            var basis=IslandMap.CreatePlatform();
            var recoveryMap=new IslandMap(basis.Shore,basis.Pier,basis.Spawns,basis.FishSpots,basis.Platform,
                new FishingConfig(enemies:EnemyConfig.Disabled,swimming:new SwimmingConfig(healthLossBasisPointsPerSecond:10000),revival:new RevivalConfig(downedTicks:1)));
            using(var party=new EventParty(2,recoveryMap))
            {
                var owner=party.Add(true,key:SaveKey(81));var guest=party.Add(key:SaveKey(82));party.Until(()=>guest.ResumeTicket!=null);
                bool sent=false;party.Until(()=>party.Events[owner].Any(e=>e.Event.Kind==HookGameplayEventKind.HookCast),
                    (c,t)=>c==guest && !sent?(sent=true,new HookInput(0,0,0,-20000,true)).Item2:default);
                party.Until(()=>party.Host.CanSave);party.Agree();originalId=party.Events[owner].First().SessionId;
                var ticket=guest.ResumeTicket!;
                var node=RecoveryField<ReliableUdp.UdpNode>(guest,"_node");node.Close(RecoveryField<ulong>(guest,"_peer"),"Event reconnect check");
                node.Poll(party.Now-RecoveryField<TimeSpan?>(guest,"_clockOrigin")!.Value);guest.Dispose();party.Clients.Remove(guest);
                party.Until(()=>party.Host.JoinedCount==1 && party.Host.Session!.InspectState().Players[1].Unavailable);
                var returned=party.Add(ticket:ticket,key:SaveKey(82));party.Until(()=>returned.ResumeTicket!=null);party.Drain();
                Require(party.Events[returned].Count==0,"Reconnect replayed a historical cast.");
                sent=false;party.Until(()=>party.Events[returned].Any(e=>e.Event.Kind==HookGameplayEventKind.HookCast),
                    (c,t)=>c==returned && !sent?(sent=true,new HookInput(0,0,0,-20000,true)).Item2:default);
                party.Until(()=>party.Host.Session!.InspectState().GameOver,(c,t)=>new HookInput(0,-1000));
                Require(party.Host.TryRestart(),"Could not restart the finished event scenario.");
                party.Until(()=>party.Clients.All(c=>c.RoundEpoch==2 && c.Session?.LocalParticipant?.IsActive==true));party.Drain();
                foreach(var c in party.Clients)Require(c.ConfirmedEvents.Count==0 && !party.Events[c].Any(e=>e.Epoch==2),"Restart bootstrap emitted old events.");
                sent=false;party.Until(()=>party.Events[owner].Any(e=>e.Epoch==2 && e.Event.Kind==HookGameplayEventKind.HookCast),
                    (c,t)=>c==owner && !sent?(sent=true,new HookInput(0,0,0,-20000,true)).Item2:default);
                party.Until(()=>party.Host.CanSave);party.Agree();saved=party.Host.CaptureSave();
            }
            using var loaded=new EventParty(2,saved.Map,IslandSaveCodec.Decode(IslandSaveCodec.Encode(saved)));
            var a=loaded.Add(true,key:SaveKey(81));var b=loaded.Add(key:SaveKey(82));loaded.Until(()=>b.ResumeTicket!=null);
            // Saving preserves hook cooldown. Let the saved cooldown expire before the one-shot click.
            loaded.Until(()=>a.Session!.ConfirmedTick>=12);loaded.Drain();
            Require(loaded.Events.Values.All(events=>events.Count==0),"Saved load emitted historical actions.");
            bool fire=false;loaded.Until(()=>loaded.Events[a].Any(e=>e.Event.Kind==HookGameplayEventKind.HookCast),
                (c,t)=>c==a && !fire?(fire=true,new HookInput(0,0,0,-20000,true)).Item2:default);
            Require(loaded.Events[a].All(e=>e.SessionId!=originalId),"Saved load reused old event identity.");loaded.Agree();
        });
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;
using ReliableUdp;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IslandMap RecoveryMap()
    {
        var b=IslandMap.CreatePlatform();return new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,new FishingConfig(enemies:EnemyConfig.Disabled));
    }
    private sealed class RecoveryParty : IDisposable
    {
        internal readonly IslandHost Host;
        internal readonly IslandMap Map;
        internal readonly List<IslandClient> Clients=new List<IslandClient>();
        private readonly Stopwatch clock=Stopwatch.StartNew();
        internal TimeSpan Now=>clock.Elapsed;
        internal RecoveryParty(int capacity,IslandMap? map=null){Map=map??RecoveryMap();Host=new IslandHost(Map,0,capacity);}
        internal IslandClient Add(ushort slot=ushort.MaxValue,RoomResumeTicket? ticket=null)
        {var c=new IslandClient(Map,IPAddress.Loopback,Host.Port,slot,ticket);Clients.Add(c);return c;}
        internal void Drop(IslandClient c){c.Dispose();Clients.Remove(c);}
        // Exercise the real transport close path without waiting ten seconds in every boundary case.
        internal void Disconnect(IslandClient c)
        {
            var node=RecoveryField<UdpNode>(c,"_node");
            node.Close(RecoveryField<ulong>(c,"_peer"),"Acceptance network interruption");
            node.Poll(Now-RecoveryField<TimeSpan?>(c,"_clockOrigin")!.Value);Drop(c);
        }
        internal void Poll(Func<IslandClient,ulong,HookInput>? input=null)
        {
            var now=clock.Elapsed;Host.Poll(now);
            foreach(var c in Clients)c.Poll(now,t=>input?.Invoke(c,t)??default);
            Require(Host.Failure==null,Host.Failure??"");
            foreach(var c in Clients)Require(c.Failure==null,$"{c.Failure}; server tick={Host.Session?.ConfirmedTick} rejection={Host.Session?.LastRejection}; client tick={c.Session?.ConfirmedTick} status={c.Session?.Status}");
        }
        internal void Until(Func<bool> done,Func<IslandClient,ulong,HookInput>? input=null,int seconds=12)
        {
            var end=clock.Elapsed+TimeSpan.FromSeconds(seconds);
            while(!done() && clock.Elapsed<end){Poll(input);Thread.Sleep(1);}
            Require(done(),"Recovery room timed out.");
        }
        internal void Agree()
        {
            var now=clock.Elapsed;for(int i=0;i<200;i++){Host.Poll(now);foreach(var c in Clients)c.Poll(now,_=>default);}
            var bytes=CheckpointCodec.Encode(Host.Session!.CaptureCheckpoint());
            foreach(var c in Clients)Require(bytes.SequenceEqual(CheckpointCodec.Encode(c.Session!.CaptureCheckpoint())),"Recovery complete checkpoint differs.");
        }
        public void Dispose(){foreach(var c in Clients)c.Dispose();Host.Dispose();}
    }
    private static T RecoveryField<T>(object target,string field)=>(T)target.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    private static IEnumerable<(string,Action)> RecoveryNetworkCases()
    {
        yield return ("recovery_closing_peer_without_close_ack_does_not_abort_room",()=>{
            using var p=new RecoveryParty(2);var owner=p.Add(0);var guest=p.Add();p.Until(()=>guest.ResumeTicket!=null);
            var hostNode=RecoveryField<UdpNode>(p.Host,"_node");var clientNode=RecoveryField<UdpNode>(guest,"_node");
            var peer=RecoveryField<ulong>(guest,"_peer");
            var serverPeer=RecoveryField<RoomSeatRegistry>(p.Host,"_seats").Connected.Single(s=>s.Slot==1).Connection!.Value;
            // Exhaust only the transport's control allowance at a fixed instant. Close is received,
            // but its acknowledgement cannot be sent and its final Disconnected event is delayed.
            Thread.Sleep(40);var now=p.Now;hostNode.Poll(now);
            typeof(UdpNode).GetField("_controlTokens",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(hostNode,0d);
            typeof(UdpNode).GetField("_controlTokens",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(clientNode,2400d);
            clientNode.Close(peer,"Acceptance interruption");clientNode.Poll(now-RecoveryField<TimeSpan?>(guest,"_clockOrigin")!.Value);
            // Let the OS deliver the loopback datagram without replenishing the fixed-time budget.
            for(int i=0;i<100;i++){hostNode.Poll(now);Thread.Sleep(1);}
            Require(hostNode.TrySend(serverPeer,0,new byte[]{1},out _)==SendResult.NotConnected,"Close did not reach the server.");
            Require(!hostNode.TryGetEvent(out _),"CloseAck was not delayed by the exhausted allowance.");
            p.Host.Poll(now);
            Require(p.Host.Failure==null,"Pending CloseAck aborted the room: "+p.Host.Failure);
            Require(!p.Host.Session!.IsConnected(serverPeer),"Closing connection was not deactivated before the next frame.");
            p.Drop(guest);p.Until(()=>p.Host.Session.InspectState().Players[1].Unavailable && owner.Session!.ConfirmedTick>=p.Host.Session.ConfirmedTick);p.Agree();
        });
        yield return ("recovery_host_starts_early_and_late_member_receives_current_world",()=>{
            using var p=new RecoveryParty(4);var a=p.Add(0);
            p.Until(()=>p.Host.JoinedCount==1);for(int i=0;i<50;i++)p.Poll();
            Require(p.Host.CanStart && p.Host.TryStart(),"Ready host cannot start with reserved seats.");
            p.Until(()=>p.Host.Session?.ConfirmedTick>=10);
            var before=p.Host.Session!.ConfirmedTick;
            Require(p.Host.Session.InspectState().Players.Skip(1).All(x=>x.WaitingToJoin),"Unentered seats spawned.");
            var b=p.Add();p.Until(()=>b.ResumeTicket!=null);
            Require(b.Slot==1 && b.Session!.ConfirmedTick>=before && !b.Session.InspectState().Players[1].WaitingToJoin,"Late peer did not activate on the current world.");
            Require(p.Host.Session.InspectState().Players.Skip(2).All(x=>x.WaitingToJoin),"Late join activated unrelated seats.");
            p.Agree();
        });
        yield return ("recovery_same_seat_returns_without_resetting_position_or_identity",()=>{
            using var p=new RecoveryParty(1);var a=p.Add(0);
            p.Until(()=>a.Session?.ConfirmedTick>=12,(c,t)=>new HookInput(600,0));
            var ticket=a.ResumeTicket;Require(ticket!=null,"Entered client has no recovery credential.");
            var identity=a.Session!.LocalParticipant!.ParticipantId;p.Drop(a);
            p.Until(()=>p.Host.Session!.InspectState().Players[0].Unavailable);
            var saved=p.Host.Session!.InspectState().Players[0];var tick=p.Host.Session.ConfirmedTick;
            var resumed=p.Add(0,ticket);p.Until(()=>resumed.ResumeTicket!=null);
            var after=resumed.Session!.InspectState().Players[0];
            Require(resumed.Slot==0 && resumed.Session.LocalParticipant!.ParticipantId==identity && resumed.Session.LocalParticipant.Generation==2,"Recovery changed identity or failed to revoke the old input generation.");
            Require(after.X==saved.X && after.Z==saved.Z && after.Vitals.Health==saved.Vitals.Health && resumed.Session.ConfirmedTick>tick,"Recovery reset or stopped the world.");
            p.Agree();
        });
        yield return ("recovery_four_members_join_one_by_one_without_resetting_others",()=>{
            using var p=new RecoveryParty(4);var a=p.Add(0);p.Until(()=>p.Host.CanStart);Require(p.Host.TryStart(),"Could not start alone.");
            p.Until(()=>a.ResumeTicket!=null);var original=a.Session;
            for(int slot=1;slot<4;slot++)
            {
                var tick=p.Host.Session!.ConfirmedTick;p.Until(()=>p.Host.Session!.ConfirmedTick>=tick+5);
                var b=p.Add();p.Until(()=>b.ResumeTicket!=null);
                Require(b.Slot==slot && b.Session!.LocalParticipant!.Generation==2 && ReferenceEquals(a.Session,original),"Late join reset another client or took the wrong seat.");
                p.Agree();
            }
            Require(p.Host.Session!.InspectState().Players.All(x=>!x.WaitingToJoin && !x.Unavailable),"Some late members did not enter.");
        });
        yield return ("recovery_udp_rejects_online_ticket_foreign_room_bad_secret_and_map",()=>{
            using var p=new RecoveryParty(2);var a=p.Add(0);p.Until(()=>p.Host.CanStart);Require(p.Host.TryStart(),"Start failed.");p.Until(()=>a.ResumeTicket!=null);
            var ticket=a.ResumeTicket!;var badToken=ticket.Token.ToArray();badToken[0]^=1;
            foreach(var attempt in new[]{ticket,new RoomResumeTicket(Guid.NewGuid(),0,ticket.Token),new RoomResumeTicket(ticket.RoomId,0,badToken)})
            {
                using var bad=new IslandClient(p.Map,IPAddress.Loopback,p.Host.Port,0,attempt);
                p.Until(()=>{bad.Poll(p.Now,_=>throw new Exception("Rejected client sampled input."));return bad.Failure!=null;});
                Require(p.Host.JoinedCount==1 && a.Failure==null && a.Session!.LocalParticipant!.IsActive,"Rejected claim displaced active owner.");
            }
            var b=p.Map;var changed=new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,new FishingConfig(staminaBase:101));
            using(var wrong=new IslandClient(changed,IPAddress.Loopback,p.Host.Port,ushort.MaxValue))
                p.Until(()=>{wrong.Poll(p.Now,_=>default);return wrong.Failure!=null;});
            var valid=p.Add();p.Until(()=>valid.ResumeTicket!=null);p.Agree();
        });
        yield return ("recovery_duplicate_welcome_join_and_ack_preserve_live_session",()=>{
            using var p=new RecoveryParty(1);var a=p.Add(0);p.Until(()=>a.ResumeTicket!=null);var session=a.Session;var tick=session!.ConfirmedTick;
            var node=RecoveryField<UdpNode>(p.Host,"_node");var seat=RecoveryField<RoomSeatRegistry>(p.Host,"_seats").Connected.Single();
            for(int i=0;i<2;i++)
            {
                Require(RoomProtocol.Send(node,seat.Connection!.Value,RoomProtocol.WelcomeBytes(a.ResumeTicket!.RoomId,0,RoomProtocol.Hash(p.Map),1,seat.Token)),"Welcome send failed.");
                Require(RoomProtocol.Send(RecoveryField<UdpNode>(a,"_node"),RecoveryField<ulong>(a,"_peer"),RoomProtocol.JoinBytes(0,RoomProtocol.Hash(p.Map))),"Duplicate Join send failed.");
                RoomProtocol.Send(RecoveryField<UdpNode>(a,"_node"),RecoveryField<ulong>(a,"_peer"),RoomProtocol.AckBytes());
            }
            p.Until(()=>a.Session!.ConfirmedTick>=tick+10);
            Require(ReferenceEquals(session,a.Session) && a.Session.LocalParticipant!.Generation==1,"Duplicate control reset the session.");p.Agree();
        });
        foreach(int count in new[]{2,4})yield return ($"recovery_economy_receipts_and_full_state_survive_{count}_players",()=>{
            using var p=new RecoveryParty(count);for(int i=0;i<count;i++)p.Add(i==0?(ushort)0:ushort.MaxValue);
            var pilots=p.Clients.Select(_=>new Advanture.HookIsland.RodSmokePilot()).ToArray();
            HookInput Play(IslandClient c,ulong t)=>pilots[c.Slot].Sample(p.Map,c.Session!.InspectState(),c.Slot,t);
            p.Until(()=>p.Host.Session?.InspectState().Players.All(x=>x.LastRodRequest>0 && x.LastRodResult==RodResult.Success && x.Phase==HookPhase.Ready)==true,Play,150);
            var a=p.Clients.Last();int slot=a.Slot;var ticket=a.ResumeTicket!;var id=a.Session!.LocalParticipant!.ParticipantId;
            p.Disconnect(a);p.Until(()=>p.Host.Session!.InspectState().Players[slot].Unavailable);
            var before=p.Host.Session!.InspectState();var player=before.Players[slot];var items=before.Tackle!.Inventory.Items.Where(x=>x.Owner==slot).ToArray();
            var resumed=p.Add(ushort.MaxValue,ticket);p.Until(()=>resumed.ResumeTicket!=null);
            var after=resumed.Session!.InspectState();var r=after.Players[slot];
            Require(resumed.Session.LocalParticipant!.ParticipantId==id && r.Coins==player.Coins && r.CoinsEarned==player.CoinsEarned && r.Catches==player.Catches && r.EquippedRod==player.EquippedRod && r.LastRodRequest==player.LastRodRequest && r.LastRodResult==player.LastRodResult,"Recovery changed wallet/equipment/receipt.");
            Require(items.SequenceEqual(after.Tackle!.Inventory.Items.Where(x=>x.Owner==slot)),"Recovery changed owned inventory.");
            var start=p.Host.Session.ConfirmedTick;
            p.Until(()=>p.Host.Session!.ConfirmedTick>=start+15,(c,t)=>c==resumed?new HookInput(0,0,shopping:true,rodAction:RodAction.BuyAndEquip,rodTarget:0,rodBatch:before.Tackle.Batch,rodRequestId:player.LastRodRequest):default);
            r=p.Host.Session.InspectState().Players[slot];Require(r.Coins==player.Coins && r.EquippedRod==player.EquippedRod && r.LastRodRequest==player.LastRodRequest,"Replayed confirmed purchase charged again.");
            pilots[slot]=new Advanture.HookIsland.RodSmokePilot();
            p.Until(()=>p.Host.Session!.InspectState().Players[slot].Catches>player.Catches,(c,t)=>c==resumed?Play(c,t):default,80);
            p.Agree();Console.WriteLine($"RECOVERY_NETWORK players={count} slot={slot} tick={p.Host.Session.ConfirmedTick} hash={CheckpointCodec.Hash(p.Host.Session.CaptureCheckpoint())}");
        });
    }
}

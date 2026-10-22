using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.HookIsland;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Network.Protocol;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> RecoveryLifecycleCases()
    {
        foreach(bool serverActivated in new[]{false,true})yield return ($"recovery_first_entry_proof_survives_disconnect_server_active_{serverActivated}",()=>{
            using var p=new RecoveryParty(2);var owner=p.Add(0);p.Until(()=>p.Host.CanStart);Require(p.Host.TryStart(),"Start failed.");p.Until(()=>owner.ResumeTicket!=null);
            using var guest=p.Add();
            if(serverActivated)
            {
                p.Clients.Remove(guest);
                // Stop delivery precisely after the server activates this seat, before the
                // newcomer can execute the activation frame and publish its confirmed ticket.
                p.Until(()=>{
                    if(p.Host.Session!.CaptureCheckpoint().Membership.Find(1).IsActive)return true;
                    guest.Poll(p.Now,_=>throw new Exception("Inactive newcomer sampled input."));return false;
                });
            }
            else p.Until(()=>guest.Session!=null);
            Require(guest.ResumeTicket==null && guest.Session?.LocalParticipant?.IsActive!=true,"Fixture missed the first-entry confirmation window.");
            var proof=typeof(IslandClient).GetProperty("RecoveryTicket")?.GetValue(guest) as RoomResumeTicket;
            Require(proof!=null,"Application lost the pending proof before receiving its activation frame.");
            p.Disconnect(guest);p.Until(()=>p.Host.JoinedCount==1 && !p.Host.Session!.CaptureCheckpoint().Membership.Find(1).IsActive);
            var retry=p.Add(ushort.MaxValue,proof);p.Until(()=>retry.ResumeTicket!=null);
            Require(retry.Slot==1 && !retry.Session!.InspectState().Players[1].WaitingToJoin,"First-entry retry did not recover the same seat.");p.Agree();
        });
        yield return ("recovery_offline_previous_epoch_member_cannot_reverse_current_defeat",()=>{
            var b=IslandMap.CreatePlatform();var map=new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,
                new FishingConfig(enemies:EnemyConfig.Disabled,swimming:new SwimmingConfig(1000000),revival:new RevivalConfig(downedTicks:1)));
            using var p=new RecoveryParty(2,map);var owner=p.Add(0);var guest=p.Add();p.Until(()=>guest.ResumeTicket!=null);
            var ticket=guest.ResumeTicket!;
            p.Until(()=>p.Host.Session!.InspectState().GameOver,(c,t)=>new HookInput(0,-1000));
            p.Disconnect(guest);p.Until(()=>p.Host.Session!.InspectState().Players[1].Unavailable);
            Require(p.Host.TryRestart(),"Offline member blocked restart.");
            p.Until(()=>owner.RoundEpoch==2 && owner.Session?.LocalParticipant?.IsActive==true);
            p.Until(()=>p.Host.Session!.InspectState().GameOver,(c,t)=>new HookInput(0,-1000));
            var failed=p.Host.Session!.InspectState();Require(failed.Players[1].WaitingToJoin,"Offline previous-round member entered the new world.");
            var returned=p.Add(ushort.MaxValue,ticket);p.Until(()=>returned.ResumeTicket!=null);
            var after=returned.Session!.InspectState();
            Require(returned.RoundEpoch==2 && after.GameOver && after.Players[1].WaitingToJoin,"Previous-round resume reversed terminal defeat.");
            Require(new HookGame().Encode(failed).SequenceEqual(new HookGame().Encode(after)),"Terminal spectator changed world state.");p.Agree();
            Require(p.Host.CanRestart,"Terminal spectator blocked the next round.");
        });
        yield return ("recovery_first_bootstrap_failure_frees_seat_without_stalling_owner",()=>{
            using var p=new RecoveryParty(2);var owner=p.Add(0);p.Until(()=>p.Host.CanStart);Require(p.Host.TryStart(),"Start failed.");p.Until(()=>owner.ResumeTicket!=null);
            using var port=new SocketDatagramPort(new DatagramAddress(IPAddress.Loopback,0));
            var node=new UdpNode(port);var peer=node.Connect(new DatagramAddress(IPAddress.Loopback,p.Host.Port));var transport=new RoomTransport(node);
            bool connected=false,joined=false,welcomed=false,closed=false;
            void Stall()
            {
                node.Poll(p.Now);transport.Pump();
                while(node.TryGetEvent(out var e)){if(e!.Kind==ConnectionEventKind.Connected)connected=true;else closed=true;}
                if(connected && !joined)joined=RoomProtocol.Send(node,peer,RoomProtocol.JoinBytes(ushort.MaxValue,RoomProtocol.Hash(p.Map)));
                while(transport.Controls.Count>0)
                {
                    var m=transport.Controls.Dequeue();
                    if(RoomProtocol.TryDecode(m.Payload,out var packet,out _) && packet!.BodyCase==RoomPacket.BodyOneofCase.Welcome)
                    {welcomed=true;Require(RoomProtocol.Send(node,peer,RoomProtocol.AckBytes()),"Ack failed.");}
                }
                // Deliberately never consume session snapshot or send Ready; keep UDP alive.
                node.Poll(p.Now);
            }
            var start=p.Host.Session!.ConfirmedTick;
            p.Until(()=>{Stall();return closed;});
            Require(welcomed && p.Host.JoinedCount==1 && p.Host.Session.ConfirmedTick>start+128 && owner.Failure==null,"Failed bootstrap blocked owner or kept its temporary seat.");
            Require(p.Host.Session.InspectState().Players[1].WaitingToJoin,"Unconfirmed candidate appeared in the world.");
            var replacement=p.Add();p.Until(()=>replacement.ResumeTicket!=null);Require(replacement.Slot==1,"Replacement did not reuse failed candidate.");p.Agree();node.Shutdown();
        });
        yield return ("recovery_victory_allows_original_then_offline_identity_enters_new_epoch",()=>{
            var map=FinaleSmokePilot.CreateMap();using var p=new RecoveryParty(3,map);var owner=p.Add(0);var guest=p.Add();
            p.Until(()=>p.Host.CanStart && guest.Session!=null);Require(p.Host.TryStart(),"Early start failed.");p.Until(()=>owner.ResumeTicket!=null && guest.ResumeTicket!=null);
            var pilots=new[]{new FinaleSmokePilot(),new FinaleSmokePilot()};
            p.Until(()=>p.Host.Session!.InspectState().Won,(c,t)=>pilots[c.Slot].Sample(map,c.Session!.InspectState(),c.Slot,t),100);
            var victory=p.Host.Session!.InspectState();var ticket=guest.ResumeTicket!;var identity=guest.Session!.LocalParticipant!.ParticipantId;
            p.Disconnect(guest);p.Until(()=>p.Host.Session!.InspectState().Players[1].Unavailable);
            using(var newcomer=new IslandClient(map,IPAddress.Loopback,p.Host.Port,ushort.MaxValue))
                p.Until(()=>{newcomer.Poll(p.Now,_=>throw new Exception("Newcomer sampled input after victory."));return newcomer.Failure!=null;});
            var returned=p.Add(ushort.MaxValue,ticket);p.Until(()=>returned.ResumeTicket!=null);
            var resumed=returned.Session!.InspectState();
            Require(resumed.Won && resumed.Steps==victory.Steps && resumed.Finale.Completed==victory.Finale.Completed && resumed.Players[1].Coins==victory.Players[1].Coins,"Terminal resume changed frozen result.");p.Agree();
            p.Disconnect(returned);p.Until(()=>p.Host.Session!.InspectState().Players[1].Unavailable);
            Require(p.Host.CanRestart && p.Host.TryRestart(),"Offline original member blocked online restart barrier.");
            p.Until(()=>owner.RoundEpoch==2 && owner.Session?.LocalParticipant?.IsActive==true);
            var fresh=p.Host.Session!.InspectState();Require(!fresh.Won && fresh.Players[1].WaitingToJoin && fresh.Players[2].WaitingToJoin,"Offline or never-entered body appeared in new round.");
            var current=p.Add(ushort.MaxValue,ticket);p.Until(()=>current.ResumeTicket!=null);
            var s=current.Session!.InspectState();var body=s.Players[1];
            Require(current.RoundEpoch==2 && current.Session.LocalParticipant!.ParticipantId==identity && current.Session.LocalParticipant.Generation==2 && body.Coins==0 && body.Catches==0 && body.Casts==0 && body.LastRodRequest==0 && !body.WaitingToJoin && s.Finale.Completed==0,"Old identity did not resume fresh current epoch.");
            Require(s.Players[2].WaitingToJoin,"Returning identity activated an unrelated seat.");p.Agree();
            Console.WriteLine($"RECOVERY_EPOCH epoch={current.RoundEpoch} tick={current.Session.ConfirmedTick} hash={CheckpointCodec.Hash(current.Session.CaptureCheckpoint())}");
        });
    }
}

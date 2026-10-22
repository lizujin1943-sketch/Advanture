using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Advanture.HookIsland.Network;
using ReliableUdp;

namespace Advanture.HookIsland.RestartTests;
internal static partial class Program
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Notice(Party party, ulong epoch, Guid? id = null, byte[]? hash = null)
    {
        var node=Field<UdpNode>(party.Host,"_node");
        foreach(var peer in Field<RoomSeatRegistry>(party.Host,"_seats").Connected.Select(s=>s.Connection!.Value))
            Require(RoomProtocol.Send(node,peer,RoomProtocol.RestartBytes(id??party.Host.Session!.CaptureCheckpoint().Session.SessionId,epoch,hash??RoomProtocol.Hash(Map()))),"Control queue unexpectedly full.");
    }
    private static IEnumerable<(string,Action)> AdversarialCases()
    {
        yield return ("network_duplicate_notice_and_stale_ack_never_reset_a_running_round", () => {
            using var party=new Party(2);party.Until(()=>party.Running,_=>default);
            for(ulong epoch=2;epoch<=3;epoch++)
            {
                party.Until(()=>party.Host.Session!.InspectState().GameOver,LeavePlatform);
                Require(party.Host.TryRestart(),"Could not begin next round.");
                party.Until(()=>party.Host.RoundEpoch==epoch && party.Running,_=>default);
                var sessions=party.Clients.Select(c=>c.Session).ToArray();
                var ticks=party.Clients.Select(c=>c.LastSubmittedTick).ToArray();
                Notice(party,epoch);Notice(party,epoch);if(epoch>2)Notice(party,epoch-1);
                var peer=Field<ulong>(party.Clients[0],"_peer");var node=Field<UdpNode>(party.Clients[0],"_node");
                RoomProtocol.Send(node,peer,RoomProtocol.RestartAckBytes(party.Host.Session!.CaptureCheckpoint().Session.SessionId,2));
                for(int i=0;i<200;i++){party.Poll(_=>default);Thread.Sleep(1);}
                Require(party.Clients.Select((c,i)=>ReferenceEquals(c.Session,sessions[i]) && c.LastSubmittedTick>=ticks[i] && party.Changes[i]==(int)epoch-1).All(v=>v),"Duplicate/stale notice reset live state/input.");
                Require(party.Host.RoundEpoch==epoch && party.Running && !party.Host.Restarting,"Stale ack disturbed committed round.");
            }
        });
        yield return ("network_guest_cannot_order_a_restart", () => {
            using var party=new Party(2);party.Until(()=>party.Running,_=>default);
            var guest=party.Clients[1];
            RoomProtocol.Send(Field<UdpNode>(guest,"_node"),Field<ulong>(guest,"_peer"),RoomProtocol.RestartBytes(party.Host.Session!.CaptureCheckpoint().Session.SessionId,2,RoomProtocol.Hash(Map())));
            for(int i=0;i<200 && guest.Failure==null;i++)
            { party.Now+=TimeSpan.FromMilliseconds(1);guest.Poll(party.Now,_=>default);party.Host.Poll(party.Now);Thread.Sleep(1); }
            Require(guest.Failure!=null && party.Host.RoundEpoch==1 && !party.Host.Restarting,"Guest obtained host restart authority.");
        });
        yield return ("network_disconnect_during_restart_does_not_partially_start", () => {
            using var party=new Party(2);party.Until(()=>party.Running,_=>default);
            party.Until(()=>party.Host.Session!.InspectState().GameOver,LeavePlatform);
            Require(party.Host.TryRestart(),"Could not restart.");party.Clients[1].Dispose();
            for(int i=0;i<1200 && party.Host.Failure==null;i++)
            { party.Now+=TimeSpan.FromMilliseconds(10);party.Host.Poll(party.Now);party.Clients[0].Poll(party.Now,_=>default);Thread.Sleep(1); }
            Require(party.Host.Failure!=null && party.Host.RoundEpoch==1,"Disconnected teammate allowed partial new round.");
        });
        yield return ("network_control_timeout_keeps_live_but_unready_peer_from_starting", () => {
            using var party=new Party(2);party.Until(()=>party.Running,_=>default);
            party.Until(()=>party.Host.Session!.InspectState().GameOver,LeavePlatform);
            Require(party.Host.TryRestart(),"Could not restart.");
            var stalled=party.Clients[1];var node=Field<UdpNode>(stalled,"_node");
            var origin=Field<TimeSpan?>(stalled,"_clockOrigin")!.Value;
            // Keep transport ACK/heartbeat alive while the application deliberately does not consume its restart notice.
            for(int i=0;i<3200 && party.Host.Failure==null;i++)
            {
                party.Now+=TimeSpan.FromMilliseconds(10);node.Poll(party.Now-origin);
                party.Host.Poll(party.Now);party.Clients[0].Poll(party.Now,_=>default);Thread.Sleep(1);
            }
            Require(party.Host.Failure?.Contains("等待队友确认超时")==true && party.Host.RoundEpoch==1,"Application barrier did not expire independently of a healthy transport.");
            Require(party.Clients[0].Session!.Status!=FrameSync.Sessions.SessionStatus.Running,"Ready peer started without the unready member.");
        });
        yield return ("network_unexpected_future_round_is_rejected", () => {
            using var party=new Party(1);party.Until(()=>party.Running,_=>default);
            Notice(party,3);
            for(int i=0;i<200 && party.Clients[0].Failure==null;i++)
            { party.Now+=TimeSpan.FromMilliseconds(1);party.Host.Poll(party.Now);party.Clients[0].Poll(party.Now,_=>default);Thread.Sleep(1); }
            Require(party.Clients[0].Failure!=null && party.Clients[0].RoundEpoch==1,"Skipped epoch was accepted.");
        });
    }
}

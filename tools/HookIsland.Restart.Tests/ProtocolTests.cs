using System;
using System.Collections.Generic;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Network.Protocol;
using FrameSync.Protocol.Protobuf;
using Bytes = Google.Protobuf.ByteString;

namespace Advanture.HookIsland.RestartTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> ProtocolCases()
    {
        yield return ("barrier_requires_every_notified_member_and_exact_epoch", () => {
            var b = new RoomRestartBarrier(2, new ulong[]{10,20,30,40}, TimeSpan.FromSeconds(5));
            Require(!b.Acknowledge(10,2), "Unsent round accepted.");
            foreach (var p in b.PendingSends) b.MarkSent(p);
            Require(!b.Acknowledge(10,1) && !b.Acknowledge(10,3) && !b.Acknowledge(99,2), "Foreign or stale acknowledgement accepted.");
            Require(b.Acknowledge(10,2) && b.Acknowledge(10,2) && b.Acknowledge(20,2) && b.Acknowledge(30,2) && !b.Complete, "Duplicate ack counted twice.");
            Require(!b.Expired(TimeSpan.FromSeconds(34.999)) && b.Expired(TimeSpan.FromSeconds(35)), "Timeout boundary wrong.");
            Require(b.Acknowledge(40,2) && b.Complete && !b.Expired(TimeSpan.FromSeconds(40)), "Final peer did not release barrier.");
        });
        yield return ("protocol_roundtrip_and_invalid_restart_fields", () => {
            var id = Guid.NewGuid(); var hash = new byte[32];
            Require(RoomProtocol.TryDecode(RoomProtocol.RestartBytes(id,2,hash), out var notice, out _) && notice!.RestartRound.Epoch == 2, "Notice lost epoch.");
            Require(RoomProtocol.TryDecode(RoomProtocol.RestartAckBytes(id,2), out var ack, out _) && new Guid(ack!.RestartAck.SessionId.Span) == id, "Ack lost identity.");
            foreach (var bad in new[]{
                new RoomPacket{RestartRound=new RestartRound{Epoch=2,SessionId=Bytes.CopyFrom(id.ToByteArray())}},
                new RoomPacket{RestartRound=new RestartRound{Epoch=1,SessionId=Bytes.CopyFrom(id.ToByteArray()),MapHash=Bytes.CopyFrom(hash)}},
                new RoomPacket{RestartAck=new RestartAck{Epoch=2,SessionId=Bytes.CopyFrom(Guid.Empty.ToByteArray())}},
                new RoomPacket{RestartAck=new RestartAck{Epoch=0,SessionId=Bytes.CopyFrom(id.ToByteArray())}}})
            { bad.ProtocolVersion=RoomProtocol.Version; Require(!RoomProtocol.TryDecode(ProtobufCodec.Encode(bad,1024),out _,out _), "Invalid restart packet accepted."); }
            var old = notice!; old.ProtocolVersion=12;
            Require(!RoomProtocol.TryDecode(ProtobufCodec.Encode(old,1024),out _,out byte rejection) && rejection==1, "Old protocol joined new room.");
        });
    }
}

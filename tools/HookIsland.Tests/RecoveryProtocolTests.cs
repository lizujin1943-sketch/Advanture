using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Network.Protocol;
using FrameSync.Protocol.Protobuf;
using Bytes=Google.Protobuf.ByteString;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> RecoveryProtocolCases()
    {
        yield return ("recovery_pending_proof_retries_a_free_seat_but_cannot_replace_another_claim",()=>{
            var id=Guid.NewGuid();var seats=new RoomSeatRegistry(id,2);
            Require(seats.TryClaim(1,ushort.MaxValue,Guid.Empty,Array.Empty<byte>(),true,out var first),"Candidate failed.");
            var proof=first!.Token.ToArray();seats.Release(1);
            Require(!seats.TryClaim(2,1,id,proof,false,out _),"Pending proof entered a finished room.");
            Require(seats.TryClaim(3,1,id,proof,true,out var retry) && !retry!.Entered,"Pending proof cannot retry an unclaimed seat.");
            seats.Release(3);Require(seats.TryClaim(4,ushort.MaxValue,Guid.Empty,Array.Empty<byte>(),true,out _),"Pending proof reserved a seat forever.");
            seats.Release(4);Require(!seats.TryClaim(5,1,id,proof,true,out _),"Abandoned proof replaced a newer claim.");
        });
        yield return ("recovery_registry_duplicate_join_keeps_entered_seat_and_secret",()=>{
            var id=Guid.NewGuid();var seats=new RoomSeatRegistry(id,2);
            Require(seats.TryClaim(7,ushort.MaxValue,Guid.Empty,Array.Empty<byte>(),true,out var seat),"Guest claim failed.");
            var token=seat!.Token.ToArray();seat.Entered=seat.Welcomed=seat.Ready=seat.Bound=true;
            Require(seats.TryClaim(7,ushort.MaxValue,Guid.Empty,Array.Empty<byte>(),false,out var same) && ReferenceEquals(same,seat),"Repeated original Join lost its active seat.");
            Require(seat.Token.SequenceEqual(token) && seat.Bound && seat.Ready && seats.Connected.Count()==1,"Duplicate Join reset ownership/bootstrap.");
        });
        yield return ("recovery_registry_failed_candidate_released_but_entered_identity_reserved",()=>{
            var id=Guid.NewGuid();var seats=new RoomSeatRegistry(id,2);
            Require(seats.TryClaim(1,ushort.MaxValue,Guid.Empty,Array.Empty<byte>(),true,out var first),"Initial candidate failed.");
            var abandoned=first!.Token.ToArray();seats.Release(1);
            Require(seats.TryClaim(2,ushort.MaxValue,Guid.Empty,Array.Empty<byte>(),true,out var second) && second!.Slot==1,"Failed candidate consumed slot.");
            Require(!second!.Token.SequenceEqual(abandoned),"Candidate secret reused.");
            var token=second.Token.ToArray();second.Entered=true;seats.Release(2);
            Require(!seats.TryClaim(3,ushort.MaxValue,Guid.Empty,Array.Empty<byte>(),true,out _),"New guest stole offline identity.");
            Require(seats.TryClaim(4,1,id,token,false,out var returned) && ReferenceEquals(returned,second),"Original guest cannot return to ended room.");
        });
        yield return ("recovery_registry_rejects_online_hijack_wrong_room_slot_or_secret",()=>{
            var id=Guid.NewGuid();var seats=new RoomSeatRegistry(id,4);
            Require(seats.TryClaim(1,ushort.MaxValue,Guid.Empty,Array.Empty<byte>(),true,out var seat),"Initial claim failed.");
            seat!.Entered=true;var token=seat.Token.ToArray();
            Require(!seats.TryClaim(2,1,id,token,true,out _),"Valid token stole an online connection.");
            seats.Release(1);var wrong=token.ToArray();wrong[0]^=1;
            Require(!seats.TryClaim(2,1,Guid.NewGuid(),token,true,out _) && !seats.TryClaim(2,2,id,token,true,out _) && !seats.TryClaim(2,1,id,wrong,true,out _),"Foreign resume accepted.");
            Require(!seats.TryClaim(2,1,id,token.AsSpan(1),true,out _) && seats.TryClaim(3,1,id,token,true,out _),"Invalid length accepted or rejected claims consumed seat.");
        });
        yield return ("recovery_ticket_copies_private_secret_and_protocol_preserves_epoch",()=>{
            var id=Guid.NewGuid();var secret=Enumerable.Range(1,32).Select(x=>(byte)x).ToArray();var hash=new byte[32];
            var ticket=new RoomResumeTicket(id,2,secret);secret[0]=0;
            Require(ticket.Token[0]==1 && !ticket.ToString()!.Contains(Convert.ToBase64String(ticket.Token)),"Ticket exposed/mutated its secret.");
            Require(RoomProtocol.TryDecode(RoomProtocol.JoinBytes(ushort.MaxValue,hash,ticket),out var join,out _) && join!.Join.Slot==2 && join.Join.ResumeToken.Span.SequenceEqual(ticket.Token),"Resume Join changed identity.");
            Require(RoomProtocol.TryDecode(RoomProtocol.WelcomeBytes(id,2,hash,8,ticket.Token.ToArray()),out var welcome,out _) && welcome!.Welcome.Epoch==8,"Welcome lost current epoch.");
        });
        yield return ("recovery_protocol_rejects_partial_identity_and_legacy_welcome",()=>{
            var id=Guid.NewGuid();var hash=Bytes.CopyFrom(new byte[32]);var token=Bytes.CopyFrom(new byte[32]);
            foreach(var p in new[]{
                new RoomPacket{Join=new JoinRoom{Slot=1,MapHash=hash}},
                new RoomPacket{Join=new JoinRoom{Slot=1,MapHash=hash,ResumeToken=token}},
                new RoomPacket{Join=new JoinRoom{Slot=1,MapHash=hash,RoomId=Bytes.CopyFrom(id.ToByteArray())}},
                new RoomPacket{Join=new JoinRoom{Slot=4,MapHash=hash,RoomId=Bytes.CopyFrom(id.ToByteArray()),ResumeToken=token}},
                new RoomPacket{Welcome=new WelcomeRoom{Slot=0,MapHash=hash,SessionId=Bytes.CopyFrom(id.ToByteArray())}},
                new RoomPacket{Welcome=new WelcomeRoom{Slot=0,MapHash=hash,SessionId=Bytes.CopyFrom(id.ToByteArray()),Epoch=2,ResumeToken=Bytes.CopyFrom(new byte[31])}}
            })
            {p.ProtocolVersion=RoomProtocol.Version;Require(!RoomProtocol.TryDecode(ProtobufCodec.Encode(p,1024),out _,out _),"Invalid identity packet accepted.");}
            var old=RoomPacket.Parser.ParseFrom(RoomProtocol.JoinBytes(0,new byte[32]));old.ProtocolVersion=21;
            Require(!RoomProtocol.TryDecode(ProtobufCodec.Encode(old,1024),out _,out byte rejection) && rejection==1,"Legacy protocol accepted.");
        });
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Advanture.HookIsland.Network.Protocol;
using FrameSync.Protocol.Protobuf;
using ProtoBytes = Google.Protobuf.ByteString;
using System.Security.Cryptography;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Transport;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace Advanture.HookIsland.Network;

/// <summary>试玩自己的开房协议，占用通道 2；不修改框架的输入、确认帧和快照协议。</summary>
internal static class RoomProtocol
{
    internal const uint Version = 22;
    internal const int MaxMessageBytes = 1024;
    internal const uint Rate = 30;
    internal static readonly RuntimeLimits Limits = new RuntimeLimits(maxParticipants: 4, maxInputBytes: HookInput.ByteCount, maxCheckpointBytes: HookGame.MaximumStateBytes + 65536);
    internal static readonly ByteString Neutral = new ByteString(default(HookInput).Encode());
    internal static byte[] Hash(IslandMap map) { using var sha = SHA256.Create(); return sha.ComputeHash(map.Encode()); }
    /// <summary>地图摘要由游戏定义，房间消息只负责携带；核心协议不会引用地图类型。</summary>
    internal static byte[] JoinBytes(ushort slot, byte[] hash,RoomResumeTicket? ticket=null) => Encode(new RoomPacket
    { Join = new JoinRoom { Slot = ticket?.Slot??slot, MapHash = ProtoBytes.CopyFrom(hash),
        RoomId=ticket==null?ProtoBytes.Empty:ProtoBytes.CopyFrom(ticket.RoomId.ToByteArray()),
        ResumeToken=ticket==null?ProtoBytes.Empty:ProtoBytes.CopyFrom(ticket.Token) } });
    internal static byte[] WelcomeBytes(Guid id, ushort slot, byte[] hash,ulong epoch,byte[] token) => Encode(new RoomPacket
    { Welcome = new WelcomeRoom { Slot = slot, SessionId = ProtoBytes.CopyFrom(id.ToByteArray()), MapHash = ProtoBytes.CopyFrom(hash),
        Epoch=epoch,ResumeToken=ProtoBytes.CopyFrom(token) } });
    internal static byte[] AckBytes() => Encode(new RoomPacket { Ack = new RoomAck() });
    internal static byte[] RestartBytes(Guid id, ulong epoch, byte[] hash) => Encode(new RoomPacket
    { RestartRound = new RestartRound { SessionId = ProtoBytes.CopyFrom(id.ToByteArray()), Epoch = epoch, MapHash = ProtoBytes.CopyFrom(hash) } });
    internal static byte[] RestartAckBytes(Guid id, ulong epoch) => Encode(new RoomPacket
    { RestartAck = new RestartAck { SessionId = ProtoBytes.CopyFrom(id.ToByteArray()), Epoch = epoch } });
    internal static byte[] RejectBytes(byte reason) => Encode(new RoomPacket { Reject = new RoomReject { Reason = (RejectReason)reason } });
    private static byte[] Encode(RoomPacket packet)
    { packet.ProtocolVersion = Version; return ProtobufCodec.Encode(packet, MaxMessageBytes); }

    /// <summary>先限制包长，再验证版本和各消息必需字段。畸形请求只拒绝该连接，不使整个房间抛异常。</summary>
    internal static bool TryDecode(ReadOnlySpan<byte> bytes, out RoomPacket? packet, out byte rejection)
    {
        packet = null; rejection = 3;
        try
        {
            var p = ProtobufCodec.Decode(RoomPacket.Parser, bytes, MaxMessageBytes);
            if (p.ProtocolVersion != Version) { rejection = 1; return false; }
            bool valid = p.BodyCase switch
            {
                RoomPacket.BodyOneofCase.Join => p.Join.Slot <= ushort.MaxValue && p.Join.MapHash.Length == 32 &&
                    (p.Join.ResumeToken.IsEmpty?p.Join.RoomId.IsEmpty && (p.Join.Slot==0 || p.Join.Slot==ushort.MaxValue):
                        p.Join.Slot<4 && p.Join.ResumeToken.Length==32 && ValidId(p.Join.RoomId)),
                RoomPacket.BodyOneofCase.Welcome => p.Welcome.Slot <4 && p.Welcome.MapHash.Length == 32 &&
                    ValidId(p.Welcome.SessionId) && p.Welcome.Epoch>0 && p.Welcome.ResumeToken.Length==32,
                RoomPacket.BodyOneofCase.Ack => true,
                RoomPacket.BodyOneofCase.RestartRound => p.RestartRound.MapHash.Length == 32 && p.RestartRound.Epoch > 1 && ValidId(p.RestartRound.SessionId),
                RoomPacket.BodyOneofCase.RestartAck => p.RestartAck.Epoch > 1 && ValidId(p.RestartAck.SessionId),
                RoomPacket.BodyOneofCase.Reject => (int)p.Reject.Reason >= 1 && (int)p.Reject.Reason <= 3,
                _ => false
            };
            if (!valid) return false;
            packet = p; return true;
        }
        catch (InvalidDataException) { return false; }
    }
    private static bool ValidId(ProtoBytes id) => id.Length == 16 && new Guid(id.Span) != Guid.Empty;
    internal static bool Send(UdpNode node, ulong connection, byte[] bytes) => node.TrySend(connection, 2, bytes, out _) == SendResult.Accepted;
}

/// <summary>将房间控制消息与框架会话消息分开消费；两个队列都有上限，满时停止从 UDP 取数据。</summary>
internal sealed class RoomTransport : ITransport
{
    private readonly UdpTransport _inner;
    private readonly Queue<TransportMessage> _session = new Queue<TransportMessage>();
    internal readonly Queue<TransportMessage> Controls = new Queue<TransportMessage>();
    internal RoomTransport(UdpNode node) { _inner = new UdpTransport(node); }
    // A retired session must not fill the bounded queue and starve room-control acknowledgements.
    internal void DiscardSessionMessages() => _session.Clear();
    internal void Pump()
    {
        for (int i = 0; i < 64 && _session.Count < 128 && Controls.Count < 128 && _inner.TryReceive(out var message); i++)
            (message!.Channel == 2 ? Controls : _session).Enqueue(message);
    }
    public int MaxMessageBytes => _inner.MaxMessageBytes;
    public int ChannelCount => _inner.ChannelCount;
    public bool Supports(DeliveryMode delivery) => _inner.Supports(delivery);
    public bool TrySend(ulong connectionId, byte channel, DeliveryMode delivery, ReadOnlySpan<byte> payload) => _inner.TrySend(connectionId, channel, delivery, payload);
    public bool TryReceive(out TransportMessage? message)
    { message = _session.Count == 0 ? null : _session.Dequeue(); return message != null; }
}

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Advanture.Tactical.Simulation;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Transport;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace Advanture.Tactical.Network;

/// <summary>试玩自己的开房协议，占用通道 2；不修改框架的输入、确认帧和快照协议。</summary>
internal static class RoomProtocol
{
    internal const byte Join = 1, Welcome = 2, Ack = 3, Reject = 4, Version = 2;
    internal const uint Rate = 30;
    internal static readonly RuntimeLimits Limits = new RuntimeLimits(maxParticipants: 2, maxInputBytes: TacticalInput.ByteCount, maxCheckpointBytes: 65536);
    internal static readonly ByteString Neutral = new ByteString(default(TacticalInput).Encode());
    internal static byte[] Hash(TacticalMap map) { using var sha = SHA256.Create(); return sha.ComputeHash(map.Encode()); }
    internal static byte[] JoinBytes(ushort slot, byte[] hash)
    { var b = new byte[35]; b[0] = Join; b[1] = Version; b[2] = (byte)slot; hash.CopyTo(b, 3); return b; }
    internal static byte[] WelcomeBytes(Guid id, ushort slot, byte[] hash)
    { var b = new byte[51]; b[0] = Welcome; b[1] = Version; b[2] = (byte)slot; id.ToByteArray().CopyTo(b, 3); hash.CopyTo(b, 19); return b; }
    internal static bool Send(UdpNode node, ulong connection, byte[] bytes) => node.TrySend(connection, 2, bytes, out _) == SendResult.Accepted;
}

/// <summary>将房间控制消息与框架会话消息分开消费；两个队列都有上限，满时停止从 UDP 取数据。</summary>
internal sealed class RoomTransport : ITransport
{
    private readonly UdpTransport _inner;
    private readonly Queue<TransportMessage> _session = new Queue<TransportMessage>();
    internal readonly Queue<TransportMessage> Controls = new Queue<TransportMessage>();
    internal RoomTransport(UdpNode node) { _inner = new UdpTransport(node); }
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

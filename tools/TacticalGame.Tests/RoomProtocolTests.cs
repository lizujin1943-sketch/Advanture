using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Advanture.Tactical.Network;
using FrameSync.Transport.Udp;
using ReliableUdp;
using Advanture.Tactical.Network.Protocol;
using Google.Protobuf;

namespace Advanture.Tactical.Tests;
internal static class RoomProtocolTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("protobuf.room.unknown_join_metadata_is_accepted", () => {
            var map = SimulationTests.OpenMap();
            Probe(RoomProtocol.JoinBytes(0, RoomProtocol.Hash(map)).Concat(new byte[] { 0xf8, 0x07, 1 }).ToArray(), 1);
        });
        yield return ("protobuf.room.legacy_fixed_layout_is_rejected", () => {
            var bytes = new byte[35]; bytes[0] = 1; bytes[1] = 2; bytes[2] = 0;
            RoomProtocol.Hash(SimulationTests.OpenMap()).CopyTo(bytes, 3);
            Probe(bytes, 0);
        });
        foreach (var invalid in new (string Name, Action<RoomPacket> Change)[] {
            ("version", p => p.ProtocolVersion = 2),
            ("hash", p => p.Join.MapHash = ByteString.CopyFrom(new byte[31])),
            ("slot_overflow", p => p.Join.Slot = 65536),
            ("slot_occupied_range", p => p.Join.Slot = 2),
            ("missing_body", p => p.ClearBody()) })
            yield return ("protobuf.room.rejects_" + invalid.Name, () => {
                var p = RoomPacket.Parser.ParseFrom(RoomProtocol.JoinBytes(0, RoomProtocol.Hash(SimulationTests.OpenMap())));
                invalid.Change(p); Probe(p.ToByteArray(), 0);
            });
        yield return ("protobuf.room.rejects_oversized_and_truncated", () => {
            Probe(new byte[RoomProtocol.MaxMessageBytes + 1], 0);
            Probe(RoomProtocol.JoinBytes(0, RoomProtocol.Hash(SimulationTests.OpenMap())).Concat(new byte[] { 0x80 }).ToArray(), 0);
        });
        yield return ("protobuf.room.welcome_and_rejection_validate_fields", () => {
            byte[] hash = RoomProtocol.Hash(SimulationTests.OpenMap()); var id = Guid.NewGuid();
            Check.True(RoomProtocol.TryDecode(RoomProtocol.WelcomeBytes(id, 1, hash), out var packet, out _));
            Check.Equal(id, new Guid(packet!.Welcome.SessionId.Span)); Check.Equal(1U, packet.Welcome.Slot);
            packet.Welcome.SessionId = ByteString.CopyFrom(new byte[16]);
            Check.True(!RoomProtocol.TryDecode(packet.ToByteArray(), out _, out _));
            Check.True(RoomProtocol.TryDecode(RoomProtocol.RejectBytes(1), out var reject, out _));
            Check.Equal(RejectReason.Incompatible, reject!.Reject.Reason);
            reject.Reject.Reason = (RejectReason)257;
            Check.True(!RoomProtocol.TryDecode(reject.ToByteArray(), out _, out _));
            Check.True(RoomProtocol.TryDecode(RoomProtocol.AckBytes(), out var ack, out _));
            Check.Equal(RoomPacket.BodyOneofCase.Ack, ack!.BodyCase);
        });
    }
    // 使用真正的 UDP 握手和 TacticalHost，观察入房结果，而不是只测消息类自身。
    private static void Probe(byte[] request, int joined)
    {
        using var host = new TacticalHost(SimulationTests.OpenMap(), 0);
        using var port = new SocketDatagramPort(new DatagramAddress(IPAddress.Loopback, 0));
        var node = new UdpNode(port); ulong peer = node.Connect(new DatagramAddress(IPAddress.Loopback, host.Port));
        bool sent = false;
        for (int ms = 0; ms < 1000; ms++)
        {
            var now = TimeSpan.FromMilliseconds(ms); host.Poll(now); node.Poll(now);
            while (node.TryGetEvent(out var e))
                if (e!.Kind == ConnectionEventKind.Connected)
                    sent = RoomProtocol.Send(node, peer, request);
        }
        Check.True(sent, "测试连接必须真正发送加入请求");
        Check.Equal(joined, host.JoinedCount); Check.True(host.Failure == null);
        node.Shutdown();
    }
}

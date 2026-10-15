using System;
using System.Collections.Generic;
using System.Linq;

namespace ReliableUdp.Tests
{
    // 验证连接和通道都有机会向前推进；Accepted 不能意味着永久排队。
    internal static class LivenessTests
    {
        internal static IEnumerable<(string, Action)> Cases()
        {
            yield return ("Liveness.MixedChannelsWithOnePacketBudget", () => MixedChannels(1));
            yield return ("Liveness.MixedChannelsWithDifferentPacketSizes", () => MixedChannels(128));
            yield return ("Liveness.HandshakeRecoversAfterCookieExpires", ExpiredCookie);
            yield return ("Liveness.BoundedConsumerRotatesConnections", ReceiveFairness);
        }

        private static void MixedChannels(int packetBudget)
        {
            var options = new UdpOptions(new[] {
                new ChannelOptions(UdpDelivery.ReliableOrdered, 900),
                new ChannelOptions(UdpDelivery.UnreliableUnordered, 900),
                new ChannelOptions(UdpDelivery.ReliableOrdered, 900)
            }, maxDatagramsPerPoll: packetBudget);
            var p = new Pair(options);
            byte[] small = { 7 };
            byte[] realtime = Enumerable.Repeat((byte)42, 900).ToArray();
            int reliableReceived = 0;
            bool realtimeReceived = false;

            // 一直补充可靠流量，保证测试没有通过“停止生产、等待队列排空”掩盖饥饿。
            void FillReliable()
            {
                while (p.A.TrySend(p.AId, 0, small, out _) == SendResult.Accepted) { }
                while (p.A.TrySend(p.AId, 2, small, out _) == SendResult.Accepted) { }
            }
            // 先让链路进入持续发送状态，再插入尺寸明显更大的实时包。
            for (int i = 0; i < 200; i++)
            { FillReliable(); p.Step(0.001); while (p.B.TryReceive(out _)) { } }
            FillReliable();
            Check.Equal(SendResult.Accepted, p.A.TrySend(p.AId, 1, realtime, out _,
                TimeSpan.FromSeconds(p.Network.Now + 2)));
            double end = p.Network.Now + 3;
            while (p.Network.Now < end)
            {
                FillReliable();
                p.Step(0.001);
                while (p.B.TryReceive(out var message))
                {
                    if (message!.Channel != 1) reliableReceived++;
                    else { Check.Bytes(realtime, message.Payload); realtimeReceived = true; }
                }
            }
            Check.True(reliableReceived > 50, "Reliable producer did not sustain traffic");
            Check.True(realtimeReceived, "Accepted realtime message starved behind reliable traffic");
            Check.True(!p.Events.Any(e => e.Kind == ConnectionEventKind.Disconnected));
        }

        private static void ExpiredCookie()
        {
            var p = new Pair(start: false);
            int echoes = 0, challenges = 0;
            p.Network.Schedule = (a, b, packet, count) =>
            {
                if (packet?.Kind == Kind.Retry) challenges++;
                // 前两个 Echo 丢失，此后网络完全正常；第三次尝试时最初的 Cookie 已过期。
                if (packet?.Kind == Kind.Echo && ++echoes <= 2) return Array.Empty<double>();
                return new[] { 0.005 };
            };
            p.Start();
            Check.True(challenges >= 2, "Client never acquired a fresh cookie");
            Check.True(p.Network.Now < 10, "Recovery reset or exceeded the overall handshake deadline");
            Check.Equal(2, p.Events.Count(e => e.Kind == ConnectionEventKind.Connected));
            Check.Equal(SendResult.Accepted, p.A.TrySend(p.AId, 0, new byte[] { 5 }, out var receipt));
            p.Until(() => receipt!.Status == ReceiptStatus.Acknowledged);
            Check.Bytes(new byte[] { 5 }, p.ReceiveB().Single());
        }

        private static void ReceiveFairness()
        {
            var net = new VirtualNetwork();
            var serverPort = net.Create(19000);
            var options = new UdpOptions(new[] { new ChannelOptions(UdpDelivery.ReliableOrdered, 900) });
            var server = new UdpNode(serverPort, options);
            var a = new UdpNode(net.Create(19001), options);
            var b = new UdpNode(net.Create(19002), options);
            var serverConnections = new List<ulong>();
            server.Listen();
            void Step()
            {
                net.Now += 0.005;
                net.Deliver();
                var now = TimeSpan.FromSeconds(net.Now);
                a.Poll(now); b.Poll(now); server.Poll(now);
                while (a.TryGetEvent(out _)) { }
                while (b.TryGetEvent(out _)) { }
                while (server.TryGetEvent(out var e))
                    if (e!.Kind == ConnectionEventKind.Connected) serverConnections.Add(e.ConnectionId);
            }
            void Until(Func<bool> done)
            {
                double end = net.Now + 5;
                while (!done() && net.Now < end) Step();
                Check.True(done(), "Connection/message preparation did not finish");
            }
            ulong aId = a.Connect(serverPort.LocalAddress);
            Until(() => serverConnections.Count == 1);
            ulong bId = b.Connect(serverPort.LocalAddress);
            Until(() => serverConnections.Count == 2);
            var receipts = new List<SendReceipt>();
            for (int i = 0; i < 32; i++)
            {
                Check.Equal(SendResult.Accepted, a.TrySend(aId, 0, new byte[] { 1 }, out var r));
                receipts.Add(r!);
            }
            Check.Equal(SendResult.Accepted, b.TrySend(bId, 0, new byte[] { 2 }, out var second));
            Until(() => second!.Status == ReceiptStatus.Acknowledged && receipts.All(r => r.Status == ReceiptStatus.Acknowledged));

            // 宿主每轮只消费一条。A 仍有大量排队消息，B 也应在两次调用内被交付。
            bool gotB = false;
            for (int i = 0; i < 2; i++)
            {
                Step();
                Check.True(server.TryReceive(out var message));
                if (message!.ConnectionId == serverConnections[1])
                { Check.Bytes(new byte[] { 2 }, message.Payload); gotB = true; }
            }
            Check.True(gotB, "The first connection monopolized bounded application consumption");
            Check.True(server.GetStatistics(serverConnections[0]).BufferedReceiveMessages > 0);
        }
    }
}

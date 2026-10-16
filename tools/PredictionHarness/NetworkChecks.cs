using System;
using System.Collections.Generic;
using System.Linq;
using ReliableUdp;

namespace FrameSync.PredictionHarness
{
    /// <summary>验证故障网络本身的延迟、所有权、确定性和容量，避免验收场景因注入器失效而虚绿。</summary>
    internal static class NetworkChecks
    {
        internal static int Run()
        {
            var clean = new SimulatedNetwork(false); var a = clean.Create(41000); var b = clean.Create(41001);
            var source = new byte[] { 7 }; Require(a.TrySend(b.LocalAddress, source), "clean send rejected"); source[0] = 99;
            clean.AdvanceTo(4); Require(!b.TryReceive(out _), "datagram arrived before its 5ms delay");
            clean.AdvanceTo(5); Require(b.TryReceive(out var packet) && packet!.Bytes[0] == 7, "datagram did not own a byte copy");
            Require(!b.TryReceive(out _), "clean datagram was duplicated");
            Console.WriteLine("PASS network.delay_and_independent_bytes");

            var first = Transcript(); var second = Transcript();
            Require(first.SequenceEqual(second), "fault schedule changed between identical runs");
            Console.WriteLine("PASS network.deterministic_loss_duplicate_and_reorder");

            var bounded = new SimulatedNetwork(false, 2); var c = bounded.Create(42000); var d = bounded.Create(42001);
            Require(c.TrySend(d.LocalAddress, new byte[] { 1 }) && c.TrySend(d.LocalAddress, new byte[] { 2 }), "capacity was too small");
            Require(!c.TrySend(d.LocalAddress, new byte[] { 3 }), "pending queue exceeded its capacity");
            bounded.AdvanceTo(5);
            Require(!c.TrySend(d.LocalAddress, new byte[] { 3 }), "delivered but unread queue escaped the capacity limit");
            Require(d.TryReceive(out _), "bounded queue lost its first packet");
            Require(c.TrySend(d.LocalAddress, new byte[] { 3 }), "consumed packet did not release capacity");
            Console.WriteLine("PASS network.capacity_includes_unread_datagrams");
            return 3;
        }

        private static List<byte> Transcript()
        {
            var network = new SimulatedNetwork(true); var a = network.Create(43000); var b = network.Create(43001);
            var output = new List<byte>();
            for (byte i = 0; i < 120; i++)
            {
                network.AdvanceTo(i); Require(a.TrySend(b.LocalAddress, new[] { i }), "fault probe send rejected");
                while (b.TryReceive(out var packet)) output.Add(packet!.Bytes[0]);
            }
            network.AdvanceTo(1000);
            while (b.TryReceive(out var packet)) output.Add(packet!.Bytes[0]);
            Require(network.DroppedDatagrams > 0 && network.DuplicateCopies > 0 && network.ReorderedDeliveries > 0,
                "fault probe did not exercise all requested disturbances");
            Require(output.Count == network.SentDatagrams - network.DroppedDatagrams + network.DuplicateCopies,
                "fault probe lost or invented a delivery outside its schedule");
            return output;
        }
        internal static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}

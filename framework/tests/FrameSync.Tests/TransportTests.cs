using System;
using System.Collections.Generic;
using FrameSync.Transport;

namespace FrameSync.Tests
{
    internal static class TransportTests
    {
        public static IEnumerable<(string Name, Action Run)> Cases()
        {
            yield return ("transport.owned_fifo_messages", OwnedFifo);
            yield return ("transport.count_and_byte_backpressure", Backpressure);
            yield return ("transport.rejects_invalid_address_and_size", Invalid);
        }

        private static void OwnedFifo()
        {
            var network = new InMemoryNetwork();
            var a = network.CreateEndpoint(10); var b = network.CreateEndpoint(20);
            byte[] input = { 5, 7 };
            Assert.True(a.TrySend(20, 1, DeliveryMode.ReliableOrdered, input));
            input[0] = 99;
            Assert.True(a.TrySend(20, 0, DeliveryMode.UnreliableUnordered, new byte[] { 3 }));
            Assert.True(b.TryReceive(out var first));
            Assert.Equal(10UL, first!.ConnectionId); Assert.Equal((byte)1, first.Channel);
            Assert.Bytes(new byte[] { 5, 7 }, first.Payload.ToArray());
            Assert.True(b.TryReceive(out var second)); Assert.Equal((byte)3, second!.Payload[0]);
            Assert.True(!a.TryReceive(out _)); Assert.True(!b.TryReceive(out _));
        }

        private static void Backpressure()
        {
            var network = new InMemoryNetwork(maxMessageBytes: 4, maxQueuedMessages: 2, maxQueuedBytes: 4);
            var a = network.CreateEndpoint(1); var b = network.CreateEndpoint(2);
            Assert.True(a.TrySend(2, 0, DeliveryMode.ReliableOrdered, new byte[] { 1, 2, 3 }));
            Assert.True(!a.TrySend(2, 0, DeliveryMode.ReliableOrdered, new byte[] { 4, 5 }));
            Assert.True(a.TrySend(2, 0, DeliveryMode.ReliableOrdered, new byte[] { 6 }));
            Assert.True(!a.TrySend(2, 0, DeliveryMode.ReliableOrdered, Array.Empty<byte>()));
            Assert.True(b.TryReceive(out _));
            Assert.True(a.TrySend(2, 0, DeliveryMode.ReliableOrdered, new byte[] { 4, 5 }));
            Assert.True(b.TryReceive(out var m)); Assert.Bytes(new byte[] { 6 }, m!.Payload.ToArray());
            Assert.True(b.TryReceive(out m)); Assert.Bytes(new byte[] { 4, 5 }, m!.Payload.ToArray());
            Assert.True(!b.TryReceive(out _));
        }

        private static void Invalid()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryNetwork(maxQueuedMessages: 0));
            var network = new InMemoryNetwork(maxMessageBytes: 4, maxEndpoints: 2);
            var a = network.CreateEndpoint(1); network.CreateEndpoint(2);
            Assert.Throws<ArgumentException>(() => network.CreateEndpoint(1));
            Assert.Throws<InvalidOperationException>(() => network.CreateEndpoint(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.TrySend(2, 0, DeliveryMode.ReliableOrdered, new byte[5]));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.TrySend(2, 4, DeliveryMode.ReliableOrdered, new byte[1]));
            Assert.Throws<ArgumentException>(() => a.TrySend(999, 0, DeliveryMode.ReliableOrdered, new byte[1]));
        }
    }
}

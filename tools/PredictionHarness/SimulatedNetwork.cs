using System;
using System.Collections.Generic;
using System.Net;
using ReliableUdp;

namespace FrameSync.PredictionHarness
{
    /// <summary>固定逻辑时钟的数据报故障层；可靠性仍由真实 UdpNode 完成。</summary>
    internal sealed class SimulatedNetwork
    {
        private readonly bool _faults;
        private readonly int _capacity;
        private readonly Dictionary<DatagramAddress, Port> _ports = new Dictionary<DatagramAddress, Port>();
        private readonly PriorityQueue<Delivery, (long Due, long Order)> _pending = new PriorityQueue<Delivery, (long, long)>();
        private readonly Dictionary<(int From, int To), long> _lastDelivered = new Dictionary<(int, int), long>();
        private long _order;
        private int _queued;

        internal SimulatedNetwork(bool faults, int capacity = 4096)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            _faults = faults; _capacity = capacity;
        }
        internal long NowMilliseconds { get; private set; }
        internal long SentDatagrams { get; private set; }
        internal long SentBytes { get; private set; }
        internal long DeliveredDatagrams { get; private set; }
        internal long DroppedDatagrams { get; private set; }
        internal long DuplicateCopies { get; private set; }
        internal long ReorderedDeliveries { get; private set; }
        internal long CapacityBackpressure { get; private set; }
        internal int PeakQueuedDatagrams { get; private set; }
        internal int LargestDatagramBytes { get; private set; }
        internal int PendingDatagrams => _queued;

        internal IDatagramPort Create(int port)
        {
            var value = new Port(this, new DatagramAddress(IPAddress.Loopback, port));
            _ports.Add(value.LocalAddress, value); return value;
        }
        internal void AdvanceTo(long milliseconds)
        {
            if (milliseconds < NowMilliseconds) throw new ArgumentOutOfRangeException(nameof(milliseconds));
            NowMilliseconds = milliseconds;
            while (_pending.TryPeek(out _, out var priority) && priority.Due <= milliseconds)
            {
                var delivery = _pending.Dequeue();
                var link = (delivery.Packet.Source.Port, delivery.Destination.LocalAddress.Port);
                _lastDelivered.TryGetValue(link, out long previous);
                // 重复副本不算乱序；这里只观察不同原始数据报的实际交付顺序。
                if (!delivery.Duplicate && delivery.Sequence < previous) ReorderedDeliveries++;
                if (!delivery.Duplicate) _lastDelivered[link] = Math.Max(previous, delivery.Sequence);
                delivery.Destination.Received.Enqueue(delivery.Packet); DeliveredDatagrams++;
            }
        }
        private bool Send(Port source, DatagramAddress destination, ReadOnlySpan<byte> bytes)
        {
            if (!_ports.TryGetValue(destination, out var target)) throw new ArgumentException("Unknown virtual destination.");
            if (bytes.Length > 1200) throw new ArgumentException("Datagram exceeds the UDP profile's 1200-byte limit.");
            long sequence = checked(SentDatagrams + 1);
            bool drop = _faults && sequence % 17 == 0;
            int copies = !drop && _faults && sequence % 11 == 0 ? 2 : 1;
            if (!drop && _queued > _capacity - copies) { CapacityBackpressure++; return false; }
            SentDatagrams = sequence; SentBytes += bytes.Length; LargestDatagramBytes = Math.Max(LargestDatagramBytes, bytes.Length);
            if (drop) { DroppedDatagrams++; return true; }
            long delay = 5 + (_faults ? sequence * 17 % 7 * 5 + (sequence % 5 == 0 ? 55 : 0) : 0);
            for (int i = 0; i < copies; i++)
            {
                _pending.Enqueue(new Delivery(target, new Datagram(source.LocalAddress, bytes), sequence, i != 0),
                    (NowMilliseconds + delay + i * 5, ++_order));
                _queued++;
            }
            if (copies == 2) DuplicateCopies++;
            PeakQueuedDatagrams = Math.Max(PeakQueuedDatagrams, _queued); return true;
        }
        private sealed class Port : IDatagramPort
        {
            private readonly SimulatedNetwork _network;
            internal readonly Queue<Datagram> Received = new Queue<Datagram>();
            internal Port(SimulatedNetwork network, DatagramAddress address) { _network = network; LocalAddress = address; }
            public DatagramAddress LocalAddress { get; }
            public bool TrySend(DatagramAddress destination, ReadOnlySpan<byte> bytes) => _network.Send(this, destination, bytes);
            public bool TryReceive(out Datagram? datagram)
            {
                if (Received.Count == 0) { datagram = null; return false; }
                datagram = Received.Dequeue(); _network._queued--; return true;
            }
        }
        private sealed class Delivery
        {
            internal Delivery(Port destination, Datagram packet, long sequence, bool duplicate)
            { Destination = destination; Packet = packet; Sequence = sequence; Duplicate = duplicate; }
            internal readonly Port Destination;
            internal readonly Datagram Packet;
            internal readonly long Sequence;
            internal readonly bool Duplicate;
        }
    }
}

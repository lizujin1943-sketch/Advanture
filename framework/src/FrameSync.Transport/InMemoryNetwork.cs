using System;
using System.Collections.Generic;

namespace FrameSync.Transport
{
    /// <summary>有容量限制的进程内网络，每个端点拥有独立收件箱。</summary>
    /// <remarks>单线程使用；所有交付模式均按可靠 FIFO 实现，不模拟丢包、线程或真实网络。</remarks>
    public sealed class InMemoryNetwork
    {
        private readonly Dictionary<ulong, Endpoint> _endpoints = new Dictionary<ulong, Endpoint>();
        private readonly int _maxMessageBytes, _maxQueuedMessages, _maxQueuedBytes, _maxEndpoints;

        /// <summary>设置每个端点的容量；队列满时返回背压，不丢消息。</summary>
        /// <param name="maxMessageBytes">一个消息的最大字节数。</param>
        /// <param name="maxQueuedMessages">每个收件箱的消息数上限，包含空消息。</param>
        /// <param name="maxQueuedBytes">每个收件箱的总负载字节上限，至少能装一个最大消息。</param>
        /// <param name="maxEndpoints">网络内最多端点数。</param>
        public InMemoryNetwork(int maxMessageBytes = 20480, int maxQueuedMessages = 128,
            int maxQueuedBytes = 2097152, int maxEndpoints = 16)
        {
            if (maxMessageBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
            if (maxQueuedMessages < 1) throw new ArgumentOutOfRangeException(nameof(maxQueuedMessages));
            if (maxQueuedBytes < maxMessageBytes) throw new ArgumentOutOfRangeException(nameof(maxQueuedBytes));
            if (maxEndpoints < 1) throw new ArgumentOutOfRangeException(nameof(maxEndpoints));
            _maxMessageBytes = maxMessageBytes; _maxQueuedMessages = maxQueuedMessages;
            _maxQueuedBytes = maxQueuedBytes; _maxEndpoints = maxEndpoints;
        }

        /// <summary>建立唯一连接编号的端点；编号由宿主分配，不能由消息内容伪造。</summary>
        /// <param name="connectionId">非零且不重复的连接编号。</param>
        /// <returns>只具备当前端点收发权限的传输对象。</returns>
        public ITransport CreateEndpoint(ulong connectionId)
        {
            if (connectionId == 0) throw new ArgumentOutOfRangeException(nameof(connectionId));
            if (_endpoints.ContainsKey(connectionId)) throw new ArgumentException("Duplicate connection id.");
            if (_endpoints.Count == _maxEndpoints) throw new InvalidOperationException("Endpoint capacity reached.");
            var endpoint = new Endpoint(this, connectionId); _endpoints.Add(connectionId, endpoint); return endpoint;
        }

        // 端点只公开自己的收件箱；发送时网络填写实际来源，调用方只能选择目的地。
        private sealed class Endpoint : ITransport
        {
            private readonly InMemoryNetwork _network;
            private readonly ulong _id;
            private readonly Queue<TransportMessage> _inbox = new Queue<TransportMessage>();
            private int _queuedBytes;
            public Endpoint(InMemoryNetwork network, ulong id) { _network = network; _id = id; }
            public int MaxMessageBytes => _network._maxMessageBytes;
            public int ChannelCount => 4;
            public bool Supports(DeliveryMode delivery) => delivery >= DeliveryMode.UnreliableUnordered && delivery <= DeliveryMode.ReliableOrdered;
            public bool TrySend(ulong connectionId, byte channel, DeliveryMode delivery, ReadOnlySpan<byte> payload)
            {
                if (payload.Length > MaxMessageBytes) throw new ArgumentOutOfRangeException(nameof(payload));
                if (channel >= ChannelCount) throw new ArgumentOutOfRangeException(nameof(channel));
                if (!Supports(delivery)) throw new ArgumentOutOfRangeException(nameof(delivery));
                if (!_network._endpoints.TryGetValue(connectionId, out var target)) throw new ArgumentException("Unknown connection.");
                if (target._inbox.Count >= _network._maxQueuedMessages ||
                    payload.Length > _network._maxQueuedBytes - target._queuedBytes) return false;
                target._inbox.Enqueue(new TransportMessage(_id, channel, delivery, payload));
                target._queuedBytes += payload.Length;
                return true;
            }
            public bool TryReceive(out TransportMessage? message)
            {
                if (_inbox.Count == 0) { message = null; return false; }
                message = _inbox.Dequeue(); _queuedBytes -= message.Payload.Length; return true;
            }
        }
    }
}

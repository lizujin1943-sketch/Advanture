using System.Collections.Generic;
using FrameSync.Transport;

namespace FrameSync.Sessions
{
    /// <summary>会话拥有的有界待发队列；背压时保留原始字节，不重新生成已确认结果。</summary>
    internal sealed class Outbox
    {
        private readonly Queue<Pending> _queue = new Queue<Pending>();
        private readonly SessionOptions _options;
        private int _bytes;
        public Outbox(SessionOptions options) { _options = options; }
        public bool CanAdd(int count, int bytes) => count <= _options.MaxOutboxMessages - _queue.Count && bytes <= _options.MaxOutboxBytes - _bytes;
        public void Add(ulong destination, MessageKind kind, byte[] bytes)
        {
            if (!CanAdd(1, bytes.Length)) throw new System.InvalidOperationException("Outbox capacity must be reserved first.");
            _queue.Enqueue(new Pending(destination, kind, bytes)); _bytes += bytes.Length;
        }
        public void Flush(ITransport transport, ref int budget)
        {
            while (budget > 0 && _queue.Count > 0)
            {
                var item = _queue.Peek(); budget--;
                if (!transport.TrySend(item.Destination, Wire.Channel(item.Kind), DeliveryMode.ReliableOrdered, item.Bytes)) break;
                _queue.Dequeue(); _bytes -= item.Bytes.Length;
            }
        }
        private sealed class Pending
        {
            public Pending(ulong destination, MessageKind kind, byte[] bytes) { Destination = destination; Kind = kind; Bytes = bytes; }
            public ulong Destination { get; }
            public MessageKind Kind { get; }
            public byte[] Bytes { get; }
        }
    }
}

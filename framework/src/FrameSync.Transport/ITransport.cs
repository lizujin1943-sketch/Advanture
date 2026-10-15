using System;

namespace FrameSync.Transport
{
    /// <summary>消息交付要求；内存实现可以提供比所要求模式更强的保证。</summary>
    public enum DeliveryMode
    {
        /// <summary>可以丢失或乱序；适用于由上层自行冗余的消息。</summary>
        UnreliableUnordered,
        /// <summary>保证到达，不要求同通道顺序。</summary>
        ReliableUnordered,
        /// <summary>保证到达并维持同通道发送顺序。</summary>
        ReliableOrdered
    }

    /// <summary>已接收的独立字节消息。连接身份由传输实现填写，不能信任负载里的身份声明。</summary>
    public sealed class TransportMessage
    {
        private readonly byte[] _payload;
        /// <summary>复制消息内容，避免调用者之后修改原始缓冲区。</summary>
        public TransportMessage(ulong connectionId, byte channel, DeliveryMode delivery, ReadOnlySpan<byte> payload)
        { ConnectionId = connectionId; Channel = channel; Delivery = delivery; _payload = payload.ToArray(); }
        /// <summary>发送方的传输连接编号，不是游戏玩家编号。</summary>
        public ulong ConnectionId { get; }
        /// <summary>逻辑通道编号。</summary>
        public byte Channel { get; }
        /// <summary>发送方要求的交付方式。</summary>
        public DeliveryMode Delivery { get; }
        /// <summary>消息的只读内容；本对象持有独立副本。</summary>
        public ReadOnlySpan<byte> Payload => _payload;
    }

    /// <summary>只收发字节的传输接口；不感知帧号、玩家、游戏状态或引擎。</summary>
    /// <remarks>由同一线程串行驱动。未来 UDP/Steam 适配器应实现同样的容量与发送语义。</remarks>
    public interface ITransport
    {
        /// <summary>一个消息允许携带的最大字节数。</summary>
        int MaxMessageBytes { get; }
        /// <summary>可用通道数量，编号从 0 开始。</summary>
        int ChannelCount { get; }
        /// <summary>确认适配器能否提供指定交付保证。</summary>
        bool Supports(DeliveryMode delivery);
        /// <summary>尝试接收发送任务；false 表示容量不足且未接收，调用方必须保留原消息再试。</summary>
        /// <remarks>true 表示适配器取得了字节副本的所有权，并不等于对端已经执行。非法参数直接抛异常。</remarks>
        bool TrySend(ulong connectionId, byte channel, DeliveryMode delivery, ReadOnlySpan<byte> payload);
        /// <summary>取出一个已到达消息；没有消息时返回 false。</summary>
        bool TryReceive(out TransportMessage? message);
    }
}

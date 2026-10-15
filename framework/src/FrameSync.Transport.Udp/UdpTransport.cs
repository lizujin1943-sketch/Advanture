using System;
using System.Linq;
using ReliableUdp;

namespace FrameSync.Transport.Udp
{
    /// <summary>把通用 UDP 字节节点接到当前帧同步会话。当前会话配置要求所有通道可靠有序。</summary>
    /// <remarks>宿主先 Poll 节点、处理连接事件，再 Poll 会话；不要由此适配器隐式读取系统时间。</remarks>
    public sealed class UdpTransport : ITransport
    {
        private readonly UdpNode _node;
        /// <summary>复用已有节点；构造时检查通道保证，不能运行到半途才发现能力不符。</summary>
        public UdpTransport(UdpNode node)
        {
            _node=node ?? throw new ArgumentNullException(nameof(node));
            if(Enumerable.Range(0,node.Options.ChannelCount).Any(i=>node.Options.Channel(i).Delivery!=UdpDelivery.ReliableOrdered))
                throw new ArgumentException("This session adapter requires an all-ReliableOrdered profile.",nameof(node));
            MaxMessageBytes=Enumerable.Range(0,node.Options.ChannelCount).Min(i=>node.Options.Channel(i).MaxMessageBytes);
        }
        /// <summary>所有通道都能接收的单消息上限。</summary>
        public int MaxMessageBytes { get; }
        /// <summary>节点的通道数量。</summary>
        public int ChannelCount => _node.Options.ChannelCount;
        /// <summary>当前适配配置只暴露可靠有序模式；其他模式可通过独立 UDP 核心使用。</summary>
        public bool Supports(DeliveryMode delivery) => delivery==DeliveryMode.ReliableOrdered;
        /// <summary>复制并排队；背压返回 false，断开与非法请求明确抛异常。</summary>
        public bool TrySend(ulong connectionId,byte channel,DeliveryMode delivery,ReadOnlySpan<byte> payload)
        {
            if(!Supports(delivery)) throw new ArgumentException("Unsupported delivery profile.",nameof(delivery));
            SendResult result=_node.TrySend(connectionId,channel,payload,out _);
            return result switch { SendResult.Accepted=>true, SendResult.Backpressure=>false,
                SendResult.TooLarge=>throw new ArgumentException("Message size is outside transport limits.",nameof(payload)),
                _=>throw new InvalidOperationException("Transport connection is not established.") };
        }
        /// <summary>转换为会话层的不可变消息；连接身份来自 UDP 节点，不能由消息载荷冒充。</summary>
        public bool TryReceive(out TransportMessage? message)
        {
            message=null;
            if(!_node.TryReceive(out var received)) return false;
            message=new TransportMessage(received!.ConnectionId,received.Channel,DeliveryMode.ReliableOrdered,received.Payload); return true;
        }
    }
}

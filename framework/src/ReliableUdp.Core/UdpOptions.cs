using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ReliableUdp
{
    /// <summary>建立连接时冻结的通道配置。可靠通道可以分片，不可靠通道限制为单个载荷。</summary>
    public sealed class ChannelOptions
    {
        /// <summary>设置模式和消息字节上限；详细资源约束由 UdpOptions 一并验证。</summary>
        public ChannelOptions(UdpDelivery delivery,int maxMessageBytes=65536)
        { if(!Enum.IsDefined(typeof(UdpDelivery),delivery)) throw new ArgumentOutOfRangeException(nameof(delivery)); Delivery=delivery; MaxMessageBytes=maxMessageBytes; }
        /// <summary>交付模式。</summary>
        public UdpDelivery Delivery { get; }
        /// <summary>单消息载荷上限。</summary>
        public int MaxMessageBytes { get; }
    }

    /// <summary>不可变资源预算。两个端点必须使用相同的线协议配置；进程级工作预算可不同。</summary>
    public sealed class UdpOptions
    {
        private readonly ChannelOptions[] _channels;
        /// <summary>构造受控网络配置；requireAuthenticatedSecurity 为 true 时明确拒绝裸 UDP。</summary>
        public UdpOptions(ChannelOptions[]? channels=null,int fragmentBytes=900,int fragmentWindow=128,
            int receiveBytesPerChannel=262144,int receiveMessagesPerChannel=64,int maxSendBytes=1048576,
            int maxConnections=8,int maxDatagramsPerPoll=128,int bytesPerSecond=4194304,
            TimeSpan? idleTimeout=null,TimeSpan? progressTimeout=null,bool requireAuthenticatedSecurity=false)
        {
            if(requireAuthenticatedSecurity) throw new NotSupportedException("Raw UDP only supports TrustedNetwork; authenticated secure datagrams are not implemented.");
            _channels=(ChannelOptions[])(channels ?? new[] { new ChannelOptions(UdpDelivery.ReliableOrdered),new ChannelOptions(UdpDelivery.ReliableOrdered),new ChannelOptions(UdpDelivery.ReliableOrdered),new ChannelOptions(UdpDelivery.ReliableOrdered) }).Clone();
            if(_channels.Length<1 || _channels.Length>8) throw new ArgumentOutOfRangeException(nameof(channels));
            if(fragmentBytes<64 || fragmentBytes>900 || fragmentWindow<1 || fragmentWindow>128) throw new ArgumentOutOfRangeException(nameof(fragmentBytes));
            if(receiveBytesPerChannel<1 || receiveBytesPerChannel>262144 || receiveMessagesPerChannel<1 || receiveMessagesPerChannel>64) throw new ArgumentOutOfRangeException(nameof(receiveBytesPerChannel));
            foreach(var channel in _channels)
                if(channel==null || channel.MaxMessageBytes<1 || channel.MaxMessageBytes>65536 ||
                    channel.MaxMessageBytes>receiveBytesPerChannel || channel.MaxMessageBytes>(channel.Delivery==UdpDelivery.UnreliableUnordered ? fragmentBytes : fragmentWindow*fragmentBytes))
                    throw new ArgumentException("Channel size cannot fit its receive/fragment window.",nameof(channels));
            if(maxSendBytes<1 || maxSendBytes>1048576 || maxConnections<1 || maxConnections>32 || maxDatagramsPerPoll<1 || maxDatagramsPerPoll>1024 || bytesPerSecond<1200 || bytesPerSecond>67108864) throw new ArgumentOutOfRangeException(nameof(maxSendBytes));
            IdleTimeout=idleTimeout ?? TimeSpan.FromSeconds(10); ProgressTimeout=progressTimeout ?? TimeSpan.FromSeconds(30);
            if(IdleTimeout<=TimeSpan.Zero || ProgressTimeout<=TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleTimeout));
            FragmentBytes=fragmentBytes; FragmentWindow=fragmentWindow; ReceiveBytesPerChannel=receiveBytesPerChannel;
            ReceiveMessagesPerChannel=receiveMessagesPerChannel; MaxSendBytes=maxSendBytes; MaxConnections=maxConnections;
            MaxDatagramsPerPoll=maxDatagramsPerPoll; BytesPerSecond=bytesPerSecond;
        }
        /// <summary>内层 UDP 报文上限，含头部和校验。</summary>
        public int MaxDatagramBytes => 1200;
        /// <summary>可靠分片的固定载荷大小。</summary>
        public int FragmentBytes { get; }
        /// <summary>每通道滑动窗口最多容纳的分片数。</summary>
        public int FragmentWindow { get; }
        /// <summary>每通道初始字节额度。</summary>
        public int ReceiveBytesPerChannel { get; }
        /// <summary>每通道初始消息额度。</summary>
        public int ReceiveMessagesPerChannel { get; }
        /// <summary>每连接已接受发送载荷总上限。</summary>
        public int MaxSendBytes { get; }
        /// <summary>连接和握手总数上限，不是玩家上限。</summary>
        public int MaxConnections { get; }
        /// <summary>一次 Poll 收和发的数据报预算，分别计数。</summary>
        public int MaxDatagramsPerPoll { get; }
        /// <summary>节点的总发送速率上限，各连接还受各自拥塞控制约束。</summary>
        public int BytesPerSecond { get; }
        /// <summary>无有效入站报文的最大时间。</summary>
        public TimeSpan IdleTimeout { get; }
        /// <summary>可靠消息或消费无进展的最大时间。</summary>
        public TimeSpan ProgressTimeout { get; }
        /// <summary>通道数量。</summary>
        public int ChannelCount => _channels.Length;
        /// <summary>获取不可变通道配置。</summary>
        public ChannelOptions Channel(int index) => _channels[index];
        /// <summary>当前实现不具备对端身份认证、加密和认证完整性。</summary>
        public bool HasAuthenticatedSecurity => false;
        internal byte[] Fingerprint()
        {
            // 指纹只包含双方必须一致的接收格式/额度；本机连接数和带宽不是协议协商参数。
            var bytes=new byte[16+ChannelCount*5];
            BinaryPrimitives.WriteInt32LittleEndian(bytes,FragmentBytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),FragmentWindow);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),ReceiveBytesPerChannel);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12),ReceiveMessagesPerChannel);
            for(int i=0;i<ChannelCount;i++) { bytes[16+i*5]=(byte)_channels[i].Delivery; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(17+i*5),_channels[i].MaxMessageBytes); }
            using(var sha=SHA256.Create()) return sha.ComputeHash(bytes);
        }
    }
}

using System;
using System.Net;

namespace ReliableUdp
{
    /// <summary>独立传输库的交付模式，不依赖帧同步接口中的类型。</summary>
    public enum UdpDelivery
    {
        /// <summary>不补发，不保证顺序；允许丢失。</summary>
        UnreliableUnordered,
        /// <summary>完整送达且去重，先完整的消息可先交付。</summary>
        ReliableUnordered,
        /// <summary>完整送达且去重，按同通道消息编号交付。</summary>
        ReliableOrdered
    }
    /// <summary>发送请求的同步结果。Accepted 只表示库接管了副本。</summary>
    public enum SendResult
    {
        /// <summary>已复制并接受任务。</summary>
        Accepted,
        /// <summary>容量或接收额度不足；未接受，可稍后重试。</summary>
        Backpressure,
        /// <summary>长度不符合通道限制，含零长度。</summary>
        TooLarge,
        /// <summary>连接不存在、未建立或正在关闭。</summary>
        NotConnected
    }
    /// <summary>可靠消息回执状态；Acknowledged 表示对端传输层收齐，不表示业务执行。</summary>
    public enum ReceiptStatus
    {
        /// <summary>仍有片未被确认。</summary>
        Pending,
        /// <summary>所有片均已被对端传输层确认。</summary>
        Acknowledged,
        /// <summary>连接结束或故障，无法保证完成。</summary>
        Failed
    }
    /// <summary>连接生命周期通知；业务身份校验仍由宿主完成。</summary>
    public enum ConnectionEventKind
    {
        /// <summary>握手成功，连接可发送应用消息。</summary>
        Connected,
        /// <summary>连接已关闭，本地资源已释放。</summary>
        Disconnected
    }

    /// <summary>不可变的 IP/端口值，隔离可修改的 IPEndPoint，不支持连接期间更换地址。</summary>
    public sealed class DatagramAddress : IEquatable<DatagramAddress>
    {
        private readonly byte[] _bytes;
        private readonly long _scope;
        /// <summary>保存地址的副本；仅接受 IPv4/IPv6 和有效端口（0 可用于本地临时绑定）。</summary>
        public DatagramAddress(IPAddress address,int port)
        {
            if(address==null) throw new ArgumentNullException(nameof(address));
            if(port<0 || port>65535) throw new ArgumentOutOfRangeException(nameof(port));
            if(address.IsIPv4MappedToIPv6) address=address.MapToIPv4();
            _bytes=address.GetAddressBytes(); _scope=_bytes.Length==16 ? address.ScopeId : 0; Port=port;
        }
        /// <summary>UDP 端口。</summary>
        public int Port { get; }
        /// <summary>返回可供 Socket 使用的新端点对象。</summary>
        public IPEndPoint ToEndPoint() => new IPEndPoint(_bytes.Length==16 ? new IPAddress(_bytes,_scope) : new IPAddress(_bytes),Port);
        /// <summary>地址、IPv6 范围和端口均相同才相等。</summary>
        public bool Equals(DatagramAddress? other) => other!=null && Port==other.Port && _scope==other._scope && _bytes.AsSpan().SequenceEqual(other._bytes);
        /// <summary>按地址值比较。</summary>
        public override bool Equals(object? obj) => Equals(obj as DatagramAddress);
        /// <summary>计算值哈希。</summary>
        public override int GetHashCode() { unchecked { int h=Port^(int)_scope; foreach(byte b in _bytes) h=h*31+b; return h; } }
        /// <summary>供诊断显示，不能用作 Cookie 的地址编码。</summary>
        public override string ToString() => ToEndPoint().ToString();
        internal byte[] Canonical()
        {
            var b=new byte[_bytes.Length+11]; b[0]=(byte)_bytes.Length; _bytes.CopyTo(b,1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1+_bytes.Length),(ushort)Port);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(3+_bytes.Length),_scope); return b;
        }
    }

    /// <summary>一份已接收数据报，拥有独立字节副本。</summary>
    public sealed class Datagram
    {
        private readonly byte[] _bytes;
        /// <summary>复制来源和内容；来源必须由数据报接入层确定。</summary>
        public Datagram(DatagramAddress source,ReadOnlySpan<byte> bytes) { Source=source ?? throw new ArgumentNullException(nameof(source)); _bytes=bytes.ToArray(); }
        /// <summary>真实来源地址。</summary>
        public DatagramAddress Source { get; }
        /// <summary>只读数据报内容。</summary>
        public ReadOnlySpan<byte> Bytes => _bytes;
    }

    /// <summary>非阻塞数据报接口；实现可以是操作系统 Socket，也可以是故障测试网络。</summary>
    public interface IDatagramPort
    {
        /// <summary>已经绑定的本地地址。</summary>
        DatagramAddress LocalAddress { get; }
        /// <summary>尝试发一份完整数据报，false 表示本地发送队列暂满且未接收。</summary>
        bool TrySend(DatagramAddress destination,ReadOnlySpan<byte> bytes);
        /// <summary>取一份数据报，无数据立即返回 false；接入层应限制最大分配长度。</summary>
        bool TryReceive(out Datagram? datagram);
    }

    /// <summary>已接受可靠消息的回执。连接结束会把所有未完成回执改为 Failed。</summary>
    public sealed class SendReceipt
    {
        /// <summary>当前状态，只由协议线程修改；不应跨线程无同步读取。</summary>
        public ReceiptStatus Status { get; internal set; }
        /// <summary>失败原因，不包含密钥或认证材料。</summary>
        public string? Failure { get; internal set; }
    }

    /// <summary>传输事件，句柄只在本节点有效，与玩家或房间无关。</summary>
    public sealed class ConnectionEvent
    {
        internal ConnectionEvent(ulong id,ConnectionEventKind kind,string reason) { ConnectionId=id; Kind=kind; Reason=reason; }
        /// <summary>本地连接句柄。</summary>
        public ulong ConnectionId { get; }
        /// <summary>建立或断开。</summary>
        public ConnectionEventKind Kind { get; }
        /// <summary>诊断原因。</summary>
        public string Reason { get; }
    }

    /// <summary>完整应用消息；TryReceive 将所有权交给宿主，同时释放传输接收额度。</summary>
    public sealed class ReceivedMessage
    {
        private readonly byte[] _bytes;
        internal ReceivedMessage(ulong connection,byte channel,UdpDelivery delivery,byte[] bytes)
        { ConnectionId=connection; Channel=channel; Delivery=delivery; _bytes=bytes; }
        /// <summary>来源连接句柄。</summary>
        public ulong ConnectionId { get; }
        /// <summary>独立交付通道。</summary>
        public byte Channel { get; }
        /// <summary>通道约定的交付方式。</summary>
        public UdpDelivery Delivery { get; }
        /// <summary>消息只读内容，由本对象拥有。</summary>
        public ReadOnlySpan<byte> Payload => _bytes;
    }

    /// <summary>某次查询得到的统计快照，字节数均有配置上限。</summary>
    public sealed class UdpStatistics
    {
        /// <summary>实际成功发送的数据报数。</summary>
        public long SentPackets { get; internal set; }
        /// <summary>判定丢失的数据报数。</summary>
        public long LostPackets { get; internal set; }
        /// <summary>可靠片重发次数。</summary>
        public long Retransmissions { get; internal set; }
        /// <summary>仍等待 ACK 的报文数（最多 64）。</summary>
        public int InFlightPackets { get; internal set; }
        /// <summary>在途报文字节。</summary>
        public int InFlightBytes { get; internal set; }
        /// <summary>已接受但尚未完成的载荷字节。</summary>
        public int PendingSendBytes { get; internal set; }
        /// <summary>重组和待消费可靠消息占用字节。</summary>
        public int BufferedReceiveBytes { get; internal set; }
        /// <summary>接收方尚未释放的消息数。</summary>
        public int BufferedReceiveMessages { get; internal set; }
        /// <summary>拥塞窗口，单位为完整数据报字节。</summary>
        public int CongestionWindowBytes { get; internal set; }
        /// <summary>平滑后的往返时间，未采样为零。</summary>
        public TimeSpan SmoothedRtt { get; internal set; }
        /// <summary>当前重传超时。</summary>
        public TimeSpan RetransmitTimeout { get; internal set; }
    }
}

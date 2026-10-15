using System;
using System.Net;
using System.Net.Sockets;
using ReliableUdp;

namespace FrameSync.Transport.Udp
{
    /// <summary>操作系统 UDP 接入。非阻塞、无后台线程；宿主负责 Dispose，核心算法不依赖此类。</summary>
    public sealed class SocketDatagramPort : IDatagramPort,IDisposable
    {
        private readonly Socket _socket;
        private readonly byte[] _buffer=new byte[1201];
        /// <summary>绑定 IP/端口；端口 0 由系统分配，实际端口可读 LocalAddress。</summary>
        public SocketDatagramPort(DatagramAddress local)
        {
            if(local==null) throw new ArgumentNullException(nameof(local));
            var endpoint=local.ToEndPoint(); _socket=new Socket(endpoint.AddressFamily,SocketType.Dgram,ProtocolType.Udp);
            try
            {
                _socket.Blocking=false;
                if(endpoint.AddressFamily==AddressFamily.InterNetwork)
                {
                    // macOS 的 .NET Socket 可能不支持此选项；明确暴露实际能力，不阻止受控网络运行。
                    try { _socket.DontFragment=true; DontFragmentEnabled=true; }
                    catch(SocketException e) when(e.SocketErrorCode==SocketError.OperationNotSupported || e.SocketErrorCode==SocketError.ProtocolOption) { }
                }
                _socket.Bind(endpoint); var bound=(IPEndPoint)_socket.LocalEndPoint!;
                LocalAddress=new DatagramAddress(bound.Address,bound.Port);
            }
            catch { _socket.Dispose(); throw; }
        }
        /// <summary>已经成功绑定的地址。</summary>
        public DatagramAddress LocalAddress { get; }
        /// <summary>是否成功设置 IPv4 禁止 IP 分片选项。false 时仅保证库内数据报不超过 1200 字节。</summary>
        public bool DontFragmentEnabled { get; }
        /// <summary>将整份数据报交给系统；发送队列暂满时返回 false。</summary>
        public bool TrySend(DatagramAddress destination,ReadOnlySpan<byte> bytes)
        {
            if(bytes.Length>1200) throw new ArgumentException("UDP payload exceeds 1200 bytes.",nameof(bytes));
            try { return _socket.SendTo(bytes.ToArray(),SocketFlags.None,destination.ToEndPoint())==bytes.Length; }
            catch(SocketException e) when(IsTemporary(e.SocketErrorCode)) { return false; }
        }
        /// <summary>至多取一个数据报；超长包只保留 1201 字节，交给解析器拒绝，避免按攻击包长度分配。</summary>
        public bool TryReceive(out Datagram? datagram)
        {
            datagram=null;
            EndPoint from=_socket.AddressFamily==AddressFamily.InterNetwork ? new IPEndPoint(IPAddress.Any,0) : new IPEndPoint(IPAddress.IPv6Any,0);
            try
            {
                int count=_socket.ReceiveFrom(_buffer,0,_buffer.Length,SocketFlags.None,ref from);
                var endpoint=(IPEndPoint)from; datagram=new Datagram(new DatagramAddress(endpoint.Address,endpoint.Port),_buffer.AsSpan(0,count)); return true;
            }
            catch(SocketException e) when(IsTemporary(e.SocketErrorCode)) { return false; }
            catch(SocketException e) when(e.SocketErrorCode==SocketError.MessageSize)
            {
                // Windows 可能对截断报 MessageSize；仍计作已消费的无效包，继续下一次有界 Poll。
                var endpoint=(IPEndPoint)from; datagram=new Datagram(new DatagramAddress(endpoint.Address,endpoint.Port),_buffer); return true;
            }
        }
        private static bool IsTemporary(SocketError error) => error==SocketError.WouldBlock || error==SocketError.IOPending || error==SocketError.NoBufferSpaceAvailable;
        /// <summary>释放系统 Socket，可重复调用。</summary>
        public void Dispose() => _socket.Dispose();
    }
}

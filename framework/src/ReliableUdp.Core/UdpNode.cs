using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Buffers.Binary;

namespace ReliableUdp
{
    /// <summary>纯 C# 可靠 UDP 节点。宿主串行调用 Poll 驱动收发，不创建线程、不读取系统时间。</summary>
    /// <remarks>只供受控网络；Cookie 证明回传路径，不证明玩家身份，也不加密。端口生命周期由宿主管理。</remarks>
    public sealed class UdpNode
    {
        private readonly IDatagramPort _port;
        private readonly byte[] _cookieKey=RandomBytes(32), _profile;
        private readonly Dictionary<ulong,Peer> _peers=new Dictionary<ulong,Peer>();
        private readonly Queue<ConnectionEvent> _events=new Queue<ConnectionEvent>();
        private ulong _nextHandle=1, _lastDeliveredConnection;
        private bool _listening, _portFailed;
        private double _now, _lastPoll;
        private int _sendBudget, _rotation;
        private double _tokens, _controlTokens;
        internal double Now => _now;

        /// <summary>创建节点并验证配置；必须不断 Poll 才能推进握手、ACK、重发和超时。</summary>
        public UdpNode(IDatagramPort port,UdpOptions? options=null)
        {
            _port=port ?? throw new ArgumentNullException(nameof(port)); Options=options ?? new UdpOptions();
            _profile=Options.Fingerprint(); _tokens=2400*Options.MaxConnections; _controlTokens=2400;
        }
        /// <summary>冻结的通道与容量配置。</summary>
        public UdpOptions Options { get; }
        /// <summary>包括握手中和关闭中的连接数量，永不超过配置上限。</summary>
        public int ConnectionCount => _peers.Count;
        /// <summary>开始接收连接请求；不会自动创建任何游戏会话。</summary>
        public void Listen() => _listening=true;

        /// <summary>向固定地址发起握手，返回本地句柄；实际建立结果通过 TryGetEvent 获取。</summary>
        public ulong Connect(DatagramAddress address)
        {
            if(address==null || address.Port==0) throw new ArgumentException("A concrete remote address is required.",nameof(address));
            if(_portFailed || !CanAllocate()) throw new InvalidOperationException("Connection/event capacity exhausted or datagram port failed.");
            if(_nextHandle==ulong.MaxValue) throw new InvalidOperationException("Connection handles exhausted.");
            var peer=new Peer(this,_nextHandle++,address,RandomBytes(16),false); _peers.Add(peer.Id,peer); return peer.Id;
        }

        /// <summary>推进网络。now 是宿主的单调经过时间；倒退会抛异常。单次收发数量及带宽均有上限。</summary>
        public void Poll(TimeSpan now)
        {
            if(now.TotalSeconds<_now || now<TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(now),"Clock must be monotonic.");
            _now=now.TotalSeconds;
            double elapsed=_now-_lastPoll; _lastPoll=_now;
            _tokens=Math.Min(2400*Options.MaxConnections,_tokens+elapsed*Options.BytesPerSecond);
            _controlTokens=Math.Min(2400,_controlTokens+elapsed*12000);
            _sendBudget=Options.MaxDatagramsPerPoll;
            if(_portFailed) return;
            try
            {
                for(int n=0;n<Options.MaxDatagramsPerPoll && _port.TryReceive(out var d);n++)
                    if(d!=null && Packet.TryDecode(d.Bytes,Options.MaxDatagramBytes,out var packet)) Receive(d.Source,packet!);
                Peer[] peers=_peers.Values.ToArray();
                // 每次从不同连接开始；单个连接的大块发送不能永久抢走全节点预算。
                for(int i=0;i<peers.Length;i++) peers[(_rotation+i)%peers.Length].Poll();
                if(peers.Length>0) _rotation=(_rotation+1)%peers.Length;
                foreach(var peer in peers) if(peer.State==PeerState.Closed) _peers.Remove(peer.Id);
            }
            catch(Exception e) when(e is System.IO.IOException || e is System.Net.Sockets.SocketException || e is ObjectDisposedException)
            {
                _portFailed=true;
                foreach(var peer in _peers.Values.ToArray()) peer.Fail("Datagram port failure: "+e.GetType().Name);
                _peers.Clear();
            }
        }

        /// <summary>请求有限时间的关闭；未完成可靠消息报告失败。先协调业务结果再关闭传输。</summary>
        public void Close(ulong id,string reason="Local close") { if(_peers.TryGetValue(id,out var peer)) peer.BeginClose(reason); }
        /// <summary>立即终止本节点全部连接（例如宿主退出）；端口仍由宿主释放。</summary>
        public void Shutdown()
        { foreach(var peer in _peers.Values.ToArray()) peer.Fail("Node shutdown"); _peers.Clear(); _listening=false; }
        /// <summary>取一次生命周期通知；宿主应持续消费，事件积压时拒绝分配新连接。</summary>
        public bool TryGetEvent(out ConnectionEvent? item) { item=_events.Count==0 ? null : _events.Dequeue(); return item!=null; }
        /// <summary>取得完整消息的所有权并释放接收额度。返回消息不再计入库内存预算。</summary>
        public bool TryReceive(out ReceivedMessage? message)
        {
            // 宿主可能每轮只取一条消息。按递增句柄轮转，避免首个活跃连接独占消费额度。
            // 只保存上次句柄，不依赖字典迭代顺序；连接删除/新建后也能继续轮转。
            Peer? first=null, next=null;
            foreach(var peer in _peers.Values)
            {
                if(!peer.HasMessage) continue;
                if(first==null || peer.Id<first.Id) first=peer;
                if(peer.Id>_lastDeliveredConnection && (next==null || peer.Id<next.Id)) next=peer;
            }
            Peer? selected=next ?? first;
            if(selected!=null && selected.TakeMessage(out message))
            { _lastDeliveredConnection=selected.Id; return true; }
            message=null; return false;
        }
        /// <summary>接受消息副本或明确返回背压。可靠消息不可过期；不可靠过期时间使用同一个 Poll 时钟。</summary>
        public SendResult TrySend(ulong id,byte channel,ReadOnlySpan<byte> bytes,out SendReceipt? receipt,TimeSpan? expiresAt=null)
        {
            receipt=null;
            if(channel>=Options.ChannelCount) throw new ArgumentOutOfRangeException(nameof(channel));
            if(bytes.Length<1 || bytes.Length>Options.Channel(channel).MaxMessageBytes) return SendResult.TooLarge;
            if(!_peers.TryGetValue(id,out var peer) || peer.State!=PeerState.Connected) return SendResult.NotConnected;
            return peer.Enqueue(channel,bytes,expiresAt,out receipt);
        }
        /// <summary>取得活动连接的统计副本；不存在的句柄会抛异常。</summary>
        public UdpStatistics GetStatistics(ulong id) => _peers.TryGetValue(id,out var peer) ? peer.Statistics() : throw new ArgumentException("Unknown connection.",nameof(id));

        private bool CanAllocate() => _peers.Count<Options.MaxConnections && _events.Count+2*(_peers.Count+1)<=4*Options.MaxConnections;
        internal void Notify(Peer peer,ConnectionEventKind kind,string reason) => _events.Enqueue(new ConnectionEvent(peer.Id,kind,reason));
        internal bool Output(DatagramAddress destination,Packet packet,bool control)
        {
            byte[] bytes=packet.Encode();
            if(_sendBudget==0 || _tokens<bytes.Length || (control && _controlTokens<bytes.Length)) return false;
            if(!_port.TrySend(destination,bytes)) return false;
            _sendBudget--; _tokens-=bytes.Length; if(control) _controlTokens-=bytes.Length; return true;
        }

        private void Receive(DatagramAddress source,Packet packet)
        {
            if(packet.Number==0)
            {
                if(packet.Ack!=0 || packet.Channel!=0) return;
                ReceiveHandshake(source,packet); return;
            }
            Peer? peer=_peers.Values.FirstOrDefault(p=>p.Remote.Equals(source) && p.WireId==packet.Connection && p.WireId!=Guid.Empty);
            if(peer==null || peer.State==PeerState.Closed || peer.State==PeerState.Connecting) return;
            peer.Receive(packet);
        }

        // 未验证地址的请求只用栈内信息计算 Cookie，不为来源分配连接/重组缓存。
        private void ReceiveHandshake(DatagramAddress source,Packet packet)
        {
            byte[] body=packet.Body;
            if(packet.Kind==Kind.Hello)
            {
                if(!_listening || packet.Connection!=Guid.Empty || body.Length!=108 || !body.AsSpan(16,32).SequenceEqual(_profile)) return;
                SendChallenge(source,body.AsSpan(0,16)); return;
            }
            if(packet.Kind==Kind.Echo)
            {
                if(!_listening || packet.Connection!=Guid.Empty || body.Length!=104 || !body.AsSpan(72,32).SequenceEqual(_profile)) return;
                long expires=BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(32));
                if(expires>TimeSpan.FromSeconds(_now+5).Ticks || !FixedEquals(Cookie(source,body.AsSpan(0,40)),body.AsSpan(40,32))) return;
                // 丢包可能耗尽 Cookie 的五秒有效期，但客户端的十秒总期限尚未结束。
                // 验证签名后重新发挑战，不分配连接；响应仍小于 Echo，不放大流量。
                // 服务端判断过期，客户端无需比较两台机器不同起点的单调时钟。
                if(expires<TimeSpan.FromSeconds(_now).Ticks) { SendChallenge(source,body.AsSpan(0,16)); return; }
                Peer? existing=_peers.Values.FirstOrDefault(p=>p.IsServer && p.Remote.Equals(source) && p.Nonce.AsSpan().SequenceEqual(body.AsSpan(0,16)));
                if(existing!=null) { existing.SendAccept(); return; }
                if(!CanAllocate() || _nextHandle==ulong.MaxValue) return;
                var peer=new Peer(this,_nextHandle++,source,body.AsSpan(0,16).ToArray(),true) { WireId=new Guid(RandomBytes(16)) };
                _peers.Add(peer.Id,peer); peer.SendAccept(); return;
            }
            if(packet.Kind==Kind.Retry)
            {
                if(packet.Connection!=Guid.Empty || body.Length!=72) return;
                Peer? peer=_peers.Values.FirstOrDefault(p=>!p.IsServer && p.State==PeerState.Connecting && p.Remote.Equals(source) && p.Nonce.AsSpan().SequenceEqual(body.AsSpan(0,16)));
                if(peer==null) return;
                peer.Challenge=body; peer.NextHandshake=0; return;
            }
            if(packet.Kind==Kind.Accept)
            {
                if(packet.Connection==Guid.Empty || body.Length!=48 || !body.AsSpan(16).SequenceEqual(_profile)) return;
                Peer? peer=_peers.Values.FirstOrDefault(p=>!p.IsServer && p.Remote.Equals(source) && p.Nonce.AsSpan().SequenceEqual(body.AsSpan(0,16)));
                if(peer==null || peer.State==PeerState.Closing || peer.State==PeerState.Closed) return;
                if(peer.State==PeerState.Connecting) { peer.WireId=packet.Connection; peer.Establish(); }
                // 重复 Accept 只能补发确认，绝不重置包号、窗口、可靠消息编号。
                if(peer.WireId==packet.Connection) Output(source,new Packet { Connection=peer.WireId,Kind=Kind.AcceptAck,Body=peer.Nonce },true);
                return;
            }
            if(packet.Kind==Kind.AcceptAck)
            {
                if(body.Length!=16) return;
                Peer? peer=_peers.Values.FirstOrDefault(p=>p.IsServer && p.WireId==packet.Connection && p.Remote.Equals(source) && p.Nonce.AsSpan().SequenceEqual(body));
                if(peer?.State==PeerState.Accepting) peer.Establish();
            }
        }
        private void SendChallenge(DatagramAddress source,ReadOnlySpan<byte> nonce)
        {
            var challenge=new byte[72]; nonce.CopyTo(challenge); RandomBytes(16).CopyTo(challenge,16);
            BinaryPrimitives.WriteInt64LittleEndian(challenge.AsSpan(32),TimeSpan.FromSeconds(_now+5).Ticks);
            Cookie(source,challenge.AsSpan(0,40)).CopyTo(challenge,40);
            Output(source,new Packet { Kind=Kind.Retry,Body=challenge },true);
        }
        internal void SendHandshake(Peer peer)
        {
            if(peer.IsServer) { peer.SendAccept(); return; }
            if(peer.Challenge==null)
            {
                var bytes=new byte[108]; peer.Nonce.CopyTo(bytes,0); _profile.CopyTo(bytes,16);
                Output(peer.Remote,new Packet { Kind=Kind.Hello,Body=bytes },true);
            }
            else
            { var bytes=new byte[104]; peer.Challenge.CopyTo(bytes,0); _profile.CopyTo(bytes,72); Output(peer.Remote,new Packet { Kind=Kind.Echo,Body=bytes },true); }
        }
        internal void Accept(Peer peer)
        { var bytes=new byte[48]; peer.Nonce.CopyTo(bytes,0); _profile.CopyTo(bytes,16); Output(peer.Remote,new Packet { Connection=peer.WireId,Kind=Kind.Accept,Body=bytes },true); }
        private byte[] Cookie(DatagramAddress address,ReadOnlySpan<byte> prefix)
        {
            byte[] source=address.Canonical(); var data=new byte[source.Length+prefix.Length+_profile.Length+2];
            source.CopyTo(data,0); prefix.CopyTo(data.AsSpan(source.Length)); _profile.CopyTo(data,source.Length+prefix.Length); data[data.Length-2]=1;
            using(var hmac=new HMACSHA256(_cookieKey)) return hmac.ComputeHash(data);
        }
        internal static byte[] RandomBytes(int count) { var bytes=new byte[count]; using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(bytes); return bytes; }
        private static bool FixedEquals(ReadOnlySpan<byte> left,ReadOnlySpan<byte> right)
        { if(left.Length!=right.Length) return false; int diff=0; for(int i=0;i<left.Length;i++) diff|=left[i]^right[i]; return diff==0; }
    }
}

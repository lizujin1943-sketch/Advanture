using System;
using System.Collections.Generic;
using System.Linq;
using System.Buffers.Binary;

namespace ReliableUdp
{
    internal enum PeerState { Connecting, Accepting, Connected, Closing, Closed }

    /// <summary>单连接状态机。只有 UdpNode 的协议线程可访问，和游戏线程/时钟分离。</summary>
    internal sealed partial class Peer
    {
        private readonly UdpNode _node;
        private UdpOptions Options => _node.Options;
        internal readonly ulong Id;
        internal readonly DatagramAddress Remote;
        internal readonly byte[] Nonce;
        internal readonly bool IsServer;
        internal Guid WireId;
        internal PeerState State;
        internal byte[]? Challenge;
        internal double NextHandshake;
        private readonly double _created;
        private double _handshakeDelay=1, _lastInbound, _lastSend, _lastPoll, _closeDeadline, _nextClose;
        private string _closeReason="Closed";
        private bool _closeAckNeeded;
        private readonly ChannelState[] _channels;
        private readonly Queue<ReceiveItem> _received=new Queue<ReceiveItem>();
        private int _pendingBytes, _pendingMessages, _unreliablePendingCount, _unreliableReceivedBytes, _unreliableReceivedCount;
        private int _channelCursor;
        private double _bulkTokens=2400;

        internal Peer(UdpNode node,ulong id,DatagramAddress remote,byte[] nonce,bool server)
        {
            _node=node; Id=id; Remote=remote; Nonce=nonce; IsServer=server;
            State=server ? PeerState.Accepting : PeerState.Connecting; _created=_lastInbound=_lastPoll=node.Now;
            _channels=Enumerable.Range(0,Options.ChannelCount).Select(_=>new ChannelState(Options)).ToArray();
        }
        internal void SendAccept() => _node.Accept(this);
        internal void Establish()
        {
            if(State!=PeerState.Connecting && State!=PeerState.Accepting) return;
            State=PeerState.Connected; _lastInbound=_node.Now;
            _node.Notify(this,ConnectionEventKind.Connected,"Established (TrustedNetwork)");
        }
        internal void Poll()
        {
            if(State==PeerState.Closed) return;
            double now=_node.Now, elapsed=now-_lastPoll; _lastPoll=now;
            _pacingTokens=Math.Min(2400,_pacingTokens+elapsed*_cwnd/Math.Max(_srtt,0.1));
            _bulkTokens=Math.Min(2400,_bulkTokens+elapsed*524288);
            if(State==PeerState.Connecting || State==PeerState.Accepting)
            {
                if(now-_created>=10) { Fail("Handshake timeout or incompatible profile"); return; }
                if(now>=NextHandshake) { _node.SendHandshake(this); NextHandshake=now+_handshakeDelay; _handshakeDelay=Math.Min(4,_handshakeDelay*2); }
                return;
            }
            if(State==PeerState.Closing)
            {
                if(now>=_closeDeadline) { Fail(_closeReason); return; }
                if(now>=_nextClose && Send(_closeAckNeeded ? Kind.CloseAck : Kind.Close,0,Array.Empty<byte>(),null,false))
                { _nextClose=now+0.2; if(_closeAckNeeded) Fail(_closeReason); }
                return;
            }
            if(now-_lastInbound>=Options.IdleTimeout.TotalSeconds) { Fail("Inbound idle timeout"); return; }
            foreach(var c in _channels)
            {
                if(c.Sending.Values.Any(m=>now-m.LastProgress>=Options.ProgressTimeout.TotalSeconds) ||
                    c.Receiving.Values.Any(m=>now-m.LastProgress>=Options.ProgressTimeout.TotalSeconds))
                { BeginClose("Reliable message/consumer progress timeout"); return; }
            }
            ExpirePackets();
            // 额度通知无需可靠队列额度，不要求 ACK；每秒重复，丢失也不会永久阻塞。
            for(byte i=0;i<_channels.Length;i++)
            {
                var c=_channels[i];
                if(Options.Channel(i).Delivery!=UdpDelivery.UnreliableUnordered && now>=c.NextCredit)
                {
                    var data=new byte[16]; BinaryPrimitives.WriteUInt64LittleEndian(data,c.MaxReceiveByte); BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8),c.MaxReceiveMessage);
                    if(Send(Kind.Credit,i,data,null,false)) c.NextCredit=now+1;
                }
            }
            SendData();
            if(_ackPending && now>=_ackDue) Send(Kind.None,0,Array.Empty<byte>(),null,false);
            if(now-_lastSend>=1) Send(Kind.Ping,0,Array.Empty<byte>(),null,true);
        }

        // 三种交付模式共享同一轮转，不能让可靠流量先耗尽预算再考虑实时消息。
        private void SendData()
        {
            foreach(var channel in _channels)
            {
                while(channel.Unreliable.Count>0)
                {
                    var item=channel.Unreliable.Peek();
                    if(!item.Expires.HasValue || _node.Now<item.Expires.Value) break;
                    RemoveUnreliable(channel);
                }
            }
            for(int count=0;count<Options.MaxDatagramsPerPoll;count++)
            {
                bool sent=false;
                for(int n=0;n<_channels.Length;n++)
                {
                    int index=(_channelCursor+n)%_channels.Length;
                    var channel=_channels[index];
                    Fragment? fragment=null;
                    byte[] data;
                    Kind kind;
                    if(Options.Channel(index).Delivery==UdpDelivery.UnreliableUnordered)
                    {
                        if(channel.Unreliable.Count==0) continue;
                        data=channel.Unreliable.Peek().Bytes;
                        kind=Kind.Unreliable;
                    }
                    else
                    {
                        // 同一可靠通道仍先补发，再按片号发新数据。
                        fragment=channel.Fragments.Values.Where(f=>!f.Acked && f.NeedsSend)
                            .OrderByDescending(f=>f.Transmissions>0).ThenBy(f=>f.Sequence).FirstOrDefault();
                        if(fragment==null) continue;
                        data=fragment.Encode(Options.FragmentBytes);
                        kind=Kind.Fragment;
                    }
                    int bytes=84+data.Length;
                    if(index==3 && _bulkTokens<bytes) continue;
                    // 保留轮到的大包的位置。余额不足时让令牌累积，不能改发小包把余额永远耗光。
                    _channelCursor=index;
                    if(!Send(kind,(byte)index,data,fragment,true)) return;
                    if(index==3) _bulkTokens-=bytes;
                    if(kind==Kind.Unreliable) RemoveUnreliable(channel);
                    _channelCursor=(index+1)%_channels.Length;
                    sent=true;
                    break;
                }
                if(!sent) break;
            }
        }
        private void RemoveUnreliable(ChannelState channel)
        {
            var item=channel.Unreliable.Dequeue();
            _pendingBytes-=item.Bytes.Length; _pendingMessages--; _unreliablePendingCount--;
        }

        internal void BeginClose(string reason)
        {
            if(State==PeerState.Closed || State==PeerState.Closing) return;
            _closeReason=reason; _closeDeadline=_node.Now+1; _nextClose=0; State=PeerState.Closing;
            FailReceipts(reason);
        }
        internal void Fail(string reason)
        {
            if(State==PeerState.Closed) return;
            FailReceipts(reason); State=PeerState.Closed; _sent.Clear(); _lostCopies.Clear(); _lostOrder.Clear(); _received.Clear();
            foreach(var c in _channels) { c.Sending.Clear(); c.Fragments.Clear(); c.Receiving.Clear(); c.Descriptions.Clear(); c.ReceivedSequences.Clear(); c.Unreliable.Clear(); }
            _pendingBytes=_pendingMessages=_inFlightBytes=_unreliablePendingCount=_unreliableReceivedBytes=_unreliableReceivedCount=0;
            _node.Notify(this,ConnectionEventKind.Disconnected,reason);
        }
        private void FailReceipts(string reason)
        { foreach(var c in _channels) foreach(var m in c.Sending.Values) if(m.Receipt.Status==ReceiptStatus.Pending) { m.Receipt.Status=ReceiptStatus.Failed; m.Receipt.Failure=reason; } }

        internal void Receive(Packet packet)
        {
            // 先校验 ACK 不能指向未发送的未来；错包不刷新心跳，也不确认其可靠数据。
            if(packet.Ack>_lastPacket || packet.Kind>=Kind.Hello && packet.Kind<=Kind.AcceptAck || packet.Channel>=_channels.Length)
            { BeginClose("Invalid connected packet"); return; }
            if(State==PeerState.Accepting) Establish();
            if(State==PeerState.Closing)
            {
                if(packet.Kind==Kind.CloseAck && packet.Body.Length==0) Fail(_closeReason);
                else if(packet.Kind==Kind.Close && packet.Body.Length==0) { _closeAckNeeded=true; _nextClose=0; }
                return;
            }
            bool eliciting=packet.Kind==Kind.Fragment || packet.Kind==Kind.Unreliable || packet.Kind==Kind.Ping;
            if(IsOldOrDuplicate(packet.Number))
            { if(eliciting) RequestAck(); ProcessAck(packet); return; }
            bool valid;
            switch(packet.Kind)
            {
                case Kind.None: valid=packet.Body.Length==0; break;
                case Kind.Fragment: valid=ReceiveFragment(packet.Channel,packet.Body); break;
                case Kind.Unreliable: valid=ReceiveUnreliable(packet.Channel,packet.Body); break;
                case Kind.Credit: valid=ReceiveCredit(packet.Channel,packet.Body); break;
                case Kind.Ping: valid=packet.Body.Length==0; break;
                case Kind.Close:
                    if(packet.Body.Length!=0) { valid=false; break; }
                    BeginClose("Remote close"); _closeAckNeeded=true; return;
                case Kind.CloseAck: valid=packet.Body.Length==0; break;
                default: valid=false; break;
            }
            if(!valid) { BeginClose("Invalid fragment, credit or record"); return; }
            // 到这里，可靠片已经完整存入受预算保护的缓冲，才允许确认数据报。
            RecordReceived(packet.Number); ProcessAck(packet); _lastInbound=_node.Now;
            if(eliciting) RequestAck();
        }
        private bool ReceiveUnreliable(byte channel,byte[] bytes)
        {
            if(Options.Channel(channel).Delivery!=UdpDelivery.UnreliableUnordered || bytes.Length<1 || bytes.Length>Options.Channel(channel).MaxMessageBytes) return false;
            if(_unreliableReceivedCount<64 && _unreliableReceivedBytes+bytes.Length<=65536)
            {
                _received.Enqueue(new ReceiveItem(channel,bytes,null)); _unreliableReceivedBytes+=bytes.Length; _unreliableReceivedCount++;
            }
            return true;
        }
        private bool ReceiveCredit(byte channel,byte[] body)
        {
            if(body.Length!=16 || Options.Channel(channel).Delivery==UdpDelivery.UnreliableUnordered) return false;
            var c=_channels[channel]; c.MaxSendByte=Math.Max(c.MaxSendByte,BinaryPrimitives.ReadUInt64LittleEndian(body));
            c.MaxSendMessage=Math.Max(c.MaxSendMessage,BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(8))); return true;
        }
        internal bool HasMessage => State==PeerState.Connected && _received.Count>0;
        internal bool TakeMessage(out ReceivedMessage? message)
        {
            message=null; if(!HasMessage) return false;
            var item=_received.Dequeue(); var c=_channels[item.Channel];
            if(item.Reliable!=null)
            {
                var m=item.Reliable; c.Receiving.Remove(m.Id); c.ReceiveBytes-=m.Bytes.Length;
                if(c.MaxReceiveByte>ulong.MaxValue-(ulong)m.Bytes.Length || c.MaxReceiveMessage==ulong.MaxValue) { BeginClose("Flow credit exhausted"); return false; }
                c.MaxReceiveByte+=(ulong)m.Bytes.Length; c.MaxReceiveMessage++; c.NextCredit=0;
            }
            else { _unreliableReceivedBytes-=item.Bytes.Length; _unreliableReceivedCount--; }
            message=new ReceivedMessage(Id,item.Channel,Options.Channel(item.Channel).Delivery,item.Bytes); return true;
        }
        internal UdpStatistics Statistics() => new UdpStatistics { SentPackets=_sentPackets, LostPackets=_lostPackets, Retransmissions=_retransmissions,
            InFlightPackets=_sent.Count,InFlightBytes=_inFlightBytes,PendingSendBytes=_pendingBytes,
            BufferedReceiveBytes=_channels.Sum(c=>c.ReceiveBytes)+_unreliableReceivedBytes,
            BufferedReceiveMessages=_channels.Sum(c=>c.Receiving.Count)+_unreliableReceivedCount,
            CongestionWindowBytes=(int)_cwnd,SmoothedRtt=TimeSpan.FromSeconds(_srtt),RetransmitTimeout=TimeSpan.FromSeconds(_rto) };

        private sealed class ReceiveItem
        { internal readonly byte Channel; internal readonly byte[] Bytes; internal readonly Incoming? Reliable;
          internal ReceiveItem(byte channel,byte[] bytes,Incoming? reliable) { Channel=channel;Bytes=bytes;Reliable=reliable; } }
        private sealed class UnreliableItem
        { internal readonly byte[] Bytes; internal readonly double? Expires;
          internal UnreliableItem(byte[] bytes,double? expires) { Bytes=bytes;Expires=expires; } }
    }
}

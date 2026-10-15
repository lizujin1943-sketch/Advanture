using System;
using System.Collections.Generic;
using System.Linq;

namespace ReliableUdp
{
    // 包账本只保留在途记录；分片副本的可靠责任由 ChannelState 持有，超时不丢消息。
    internal sealed partial class Peer
    {
        private readonly Dictionary<ulong,SentPacket> _sent=new Dictionary<ulong,SentPacket>();
        // 保留至多 256 个失包到分片的轻量映射。迟到 ACK 可完成原片，但绝不二次扣在途字节。
        private readonly Dictionary<ulong,Fragment> _lostCopies=new Dictionary<ulong,Fragment>();
        private readonly Queue<ulong> _lostOrder=new Queue<ulong>();
        private readonly HashSet<ulong> _receivedPackets=new HashSet<ulong>();
        private ulong _lastPacket, _largestReceived, _recoveryUntil;
        private int _inFlightBytes;
        private double _cwnd=4800, _threshold=38400, _srtt, _variance, _rto=1, _pacingTokens=2400;
        private double _ackDue, _lastAckSent=-1;
        private bool _ackPending;
        private long _sentPackets, _lostPackets, _retransmissions;

        private bool Send(Kind kind,byte channel,byte[] body,Fragment? fragment,bool tracked)
        {
            if(_lastPacket==ulong.MaxValue) { Fail("Packet numbers exhausted"); return false; }
            int length=80+(kind==Kind.None ? 0 : 4+body.Length);
            if(tracked && (_sent.Count>=64 || _inFlightBytes+length>_cwnd || _pacingTokens<length)) return false;
            if(kind==Kind.None && _node.Now-_lastAckSent<0.01) return false;
            var packet=new Packet {Connection=WireId,Number=_lastPacket+1,Ack=_largestReceived,Kind=kind,Channel=channel,Body=body};
            if(_largestReceived>0)
                foreach(ulong received in _receivedPackets)
                    if(received<_largestReceived && _largestReceived-received<=256)
                    { int i=(int)(_largestReceived-received-1);packet.AckBits[i/8]|=(byte)(1<<(i%8)); }
            if(!_node.Output(Remote,packet,!tracked)) return false;
            // 只有系统/测试端口确实接管数据报后才推进包号、在途账本和重传统计。
            _lastPacket++; _sentPackets++; _lastSend=_node.Now; _ackPending=false; _lastAckSent=_node.Now;
            if(tracked)
            {
                _sent.Add(_lastPacket,new SentPacket(_lastPacket,_node.Now,length,_rto,fragment)); _inFlightBytes+=length; _pacingTokens-=length;
                if(fragment!=null) { if(fragment.Transmissions++>0) _retransmissions++; fragment.NeedsSend=false; }
            }
            return true;
        }
        private bool IsOldOrDuplicate(ulong number) => _receivedPackets.Contains(number) || (_largestReceived>256 && number<_largestReceived-256);
        private void RecordReceived(ulong number)
        {
            _largestReceived=Math.Max(_largestReceived,number); _receivedPackets.Add(number);
            if(_largestReceived>256) _receivedPackets.RemoveWhere(p=>p<_largestReceived-256);
        }
        private void RequestAck()
        { if(!_ackPending) {_ackPending=true;_ackDue=_node.Now+0.01;} }
        private void ProcessAck(Packet packet)
        {
            if(packet.Ack==0) return;
            foreach(var lost in _lostCopies.Where(p=>IsAcknowledged(p.Key,packet)).ToArray())
            { Acknowledge(lost.Value); _lostCopies.Remove(lost.Key); }
            var acknowledged=_sent.Values.Where(p=>IsAcknowledged(p.Number,packet)).OrderBy(p=>p.Number).ToArray();
            if(acknowledged.Length==0) return;
            // 每个入站 ACK 最多采一次 RTT，重复确认不参与平滑或快速丢失计数。
            SentPacket sample=acknowledged[acknowledged.Length-1];
            double rtt=Math.Max(0.001,_node.Now-sample.Time);
            if(_srtt==0) { _srtt=rtt;_variance=rtt/2; }
            else { _variance=0.75*_variance+0.25*Math.Abs(_srtt-rtt);_srtt=0.875*_srtt+0.125*rtt; }
            _rto=Math.Max(1,Math.Min(60,_srtt+Math.Max(0.001,4*_variance)));
            foreach(var sent in acknowledged)
            {
                _sent.Remove(sent.Number); _inFlightBytes-=sent.Bytes;
                if(sent.Fragment!=null) Acknowledge(sent.Fragment);
                _cwnd=Math.Min(76800,_cwnd+(_cwnd<_threshold ? sent.Bytes : 1200.0*sent.Bytes/_cwnd));
                foreach(var older in _sent.Values) if(older.Number<sent.Number) older.LaterAcks++;
            }
            foreach(var sent in _sent.Values.Where(s=>s.LaterAcks>=3).ToArray()) Lose(sent,false);
        }
        private static bool IsAcknowledged(ulong number,Packet ack)
        {
            if(number==ack.Ack) return true;
            if(number>ack.Ack || ack.Ack-number>256) return false;
            int i=(int)(ack.Ack-number-1);return (ack.AckBits[i/8]&(1<<(i%8)))!=0;
        }
        private void ExpirePackets()
        {
            var expired=_sent.Values.Where(p=>_node.Now-p.Time>=p.Timeout).ToArray();
            if(expired.Length==0) return;
            foreach(var packet in expired) Lose(packet,true);
            _rto=Math.Min(60,_rto*2);
        }
        private void Lose(SentPacket packet,bool timeout)
        {
            if(!_sent.Remove(packet.Number)) return;
            _inFlightBytes-=packet.Bytes; _lostPackets++;
            if(packet.Fragment!=null && !packet.Fragment.Acked)
            {
                packet.Fragment.NeedsSend=true; _lostCopies[packet.Number]=packet.Fragment; _lostOrder.Enqueue(packet.Number);
                if(_lostOrder.Count>256) _lostCopies.Remove(_lostOrder.Dequeue());
            }
            // 一个恢复周期至多降窗一次。真实超时仍须降到一个报文，防止持续超载。
            if(packet.Number>_recoveryUntil || timeout)
            {
                _threshold=Math.Max(2400,_cwnd/2); _cwnd=timeout ? 1200 : _threshold; _recoveryUntil=_lastPacket;
                _pacingTokens=Math.Min(_pacingTokens,1200);
            }
        }
        private sealed class SentPacket
        {
            internal readonly ulong Number;
            internal readonly double Time,Timeout;
            internal readonly int Bytes;
            internal readonly Fragment? Fragment;
            internal int LaterAcks;
            internal SentPacket(ulong number,double time,int bytes,double timeout,Fragment? fragment)
            { Number=number;Time=time;Bytes=bytes;Timeout=timeout;Fragment=fragment; }
        }
    }
}

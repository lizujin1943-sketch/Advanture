using System;
using System.Collections.Generic;
using System.Linq;
using System.Buffers.Binary;

namespace ReliableUdp
{
    // 消息层与包层分开：一个可靠片可以装在不同包号中重发，但片号始终不变。
    internal sealed partial class Peer
    {
        internal SendResult Enqueue(byte channel,ReadOnlySpan<byte> bytes,TimeSpan? expires,out SendReceipt? receipt)
        {
            receipt=null; var c=_channels[channel]; var mode=Options.Channel(channel).Delivery;
            if(mode!=UdpDelivery.UnreliableUnordered && expires.HasValue) throw new ArgumentException("Reliable messages cannot silently expire.",nameof(expires));
            if(_pendingBytes+bytes.Length>Options.MaxSendBytes || _pendingMessages>=256) return SendResult.Backpressure;
            if(mode==UdpDelivery.UnreliableUnordered)
            {
                if(_unreliablePendingCount>=64) return SendResult.Backpressure;
                c.Unreliable.Enqueue(new UnreliableItem(bytes.ToArray(),expires?.TotalSeconds));
                _unreliablePendingCount++;
                _pendingBytes+=bytes.Length; _pendingMessages++; return SendResult.Accepted;
            }
            int count=(bytes.Length+Options.FragmentBytes-1)/Options.FragmentBytes;
            if(c.NextSendSequence>ulong.MaxValue-(ulong)count || c.NextSendMessage==ulong.MaxValue || c.NextSendByte>ulong.MaxValue-(ulong)bytes.Length)
            { BeginClose("Reliable sequence exhausted"); return SendResult.NotConnected; }
            // 一次性为整条消息预留片窗口及接收字节/消息额度，不能先发一半再发现放不下。
            if(c.NextSendSequence-c.SendBase+(ulong)count>(ulong)Options.FragmentWindow || c.NextSendMessage>c.MaxSendMessage ||
                c.NextSendByte+(ulong)bytes.Length>c.MaxSendByte) return SendResult.Backpressure;
            receipt=new SendReceipt();
            var message=new Outgoing(c.NextSendMessage,c.NextSendByte,c.NextSendSequence,bytes.ToArray(),count,receipt,_node.Now);
            c.Sending.Add(message.Id,message);
            for(int i=0;i<count;i++) { var f=new Fragment(channel,message,(ushort)i); c.Fragments.Add(f.Sequence,f); }
            c.NextSendSequence+=(ulong)count; c.NextSendByte+=(ulong)bytes.Length; c.NextSendMessage++;
            _pendingBytes+=bytes.Length; _pendingMessages++; return SendResult.Accepted;
        }

        private bool ReceiveFragment(byte channel,byte[] body)
        {
            if(Options.Channel(channel).Delivery==UdpDelivery.UnreliableUnordered || body.Length<33) return false;
            ulong sequence=BinaryPrimitives.ReadUInt64LittleEndian(body), id=BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(8)),
                offset=BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(16));
            uint length=BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(24));
            ushort index=BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(28)), count=BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(30));
            if(sequence==0 || id==0 || length<1 || length>Options.Channel(channel).MaxMessageBytes || count!=(length+Options.FragmentBytes-1)/Options.FragmentBytes ||
                index>=count || sequence<=(ulong)index || offset>ulong.MaxValue-length) return false;
            ulong first=sequence-index;
            if(first>ulong.MaxValue-count || id>first || body.Length-32!=Math.Min(Options.FragmentBytes,(int)length-index*Options.FragmentBytes)) return false;
            var c=_channels[channel];
            if(id>c.MaxReceiveMessage || offset+length>c.MaxReceiveByte) return false;
            c.Receiving.TryGetValue(id,out var m);
            if(m!=null)
            {
                if(m.Offset!=offset || m.First!=first || m.Bytes.Length!=length || m.Seen.Length!=count) return false;
                if(m.Seen[index]) return m.Bytes.AsSpan(index*Options.FragmentBytes,body.Length-32).SequenceEqual(body.AsSpan(32));
            }
            // 已经跨过接收窗口的片只能是旧副本；旧消息不能换一个新的片号再次进入。
            if(sequence<c.ReceiveBase) return true;
            if(sequence-c.ReceiveBase>=(ulong)Options.FragmentWindow) return false;
            if(m==null)
            {
                if(id<c.DescribeNext || first<c.ReceiveBase || first+(ulong)count-c.ReceiveBase>(ulong)Options.FragmentWindow ||
                    c.Receiving.Count>=Options.ReceiveMessagesPerChannel || c.ReceiveBytes+(int)length>Options.ReceiveBytesPerChannel) return false;
                var descriptor=new Descriptor(id,offset,first,length,count);
                if(!ValidateDescription(c,descriptor)) return false;
                m=new Incoming(id,offset,first,new byte[(int)length],count,_node.Now);
                c.Receiving.Add(id,m); c.ReceiveBytes+=(int)length; c.Descriptions.Add(id,descriptor);
                // 只保留尚有缺口的描述。连续前缀压缩成三个游标，不永久保存消息历史。
                while(c.Descriptions.TryGetValue(c.DescribeNext,out var known))
                { c.Descriptions.Remove(c.DescribeNext); c.DescribeNext++; c.DescribeByte=known.Offset+known.Length; c.DescribeSequence=known.First+known.Count; }
            }
            if(c.ReceivedSequences.Contains(sequence)) return false; // 同一片号被不同消息复用。
            body.AsSpan(32).CopyTo(m.Bytes.AsSpan(index*Options.FragmentBytes)); m.Seen[index]=true; m.Remaining--; m.LastProgress=_node.Now;
            c.ReceivedSequences.Add(sequence);
            while(c.ReceivedSequences.Remove(c.ReceiveBase)) c.ReceiveBase++;
            if(m.Remaining==0)
            {
                if(Options.Channel(channel).Delivery==UdpDelivery.ReliableUnordered) QueueCompleted(channel,m);
                else
                    while(c.Receiving.TryGetValue(c.DeliverNext,out var ready) && ready.Remaining==0)
                    { QueueCompleted(channel,ready); c.DeliverNext++; }
            }
            return true;
        }
        private static bool ValidateDescription(ChannelState c,Descriptor d)
        {
            if(d.Offset<c.DescribeByte || d.First<c.DescribeSequence) return false;
            if(d.Id==c.DescribeNext && (d.Offset!=c.DescribeByte || d.First!=c.DescribeSequence)) return false;
            foreach(var old in c.Descriptions.Values)
            {
                // 编号顺序、字节区间和片区间方向必须一致，禁止隔着缺失消息重叠占位。
                if(old.Id<d.Id && (old.Offset+old.Length>d.Offset || old.First+old.Count>d.First)) return false;
                if(old.Id>d.Id && (d.Offset+d.Length>old.Offset || d.First+d.Count>old.First)) return false;
                if(old.Id+1==d.Id && (old.Offset+old.Length!=d.Offset || old.First+old.Count!=d.First)) return false;
                if(d.Id+1==old.Id && (d.Offset+d.Length!=old.Offset || d.First+d.Count!=old.First)) return false;
            }
            return true;
        }
        private void QueueCompleted(byte channel,Incoming message)
        { _received.Enqueue(new ReceiveItem(channel,message.Bytes,message)); }
        private void Acknowledge(Fragment fragment)
        {
            if(fragment.Acked) return;
            fragment.Acked=true; fragment.NeedsSend=false; var c=_channels[fragment.Channel]; var m=fragment.Message;
            m.LastProgress=_node.Now;
            if(--m.Remaining==0)
            {
                m.Receipt.Status=ReceiptStatus.Acknowledged; _pendingBytes-=m.Bytes.Length; _pendingMessages--;
                c.Sending.Remove(m.Id); m.Bytes=Array.Empty<byte>();
            }
            // 后续片已确认并不能跨过首片缺口无限分配，发送基准只跨连续 ACK。
            while(c.Fragments.TryGetValue(c.SendBase,out var head) && head.Acked) { c.Fragments.Remove(c.SendBase); c.SendBase++; }
        }

        private sealed class ChannelState
        {
            internal ulong NextSendSequence=1, SendBase=1, NextSendMessage=1, NextSendByte;
            internal ulong MaxSendByte,MaxSendMessage,MaxReceiveByte,MaxReceiveMessage;
            internal ulong ReceiveBase=1, DescribeNext=1, DescribeByte, DescribeSequence=1, DeliverNext=1;
            internal int ReceiveBytes;
            internal double NextCredit;
            internal readonly Queue<UnreliableItem> Unreliable=new Queue<UnreliableItem>();
            internal readonly SortedDictionary<ulong,Fragment> Fragments=new SortedDictionary<ulong,Fragment>();
            internal readonly Dictionary<ulong,Outgoing> Sending=new Dictionary<ulong,Outgoing>();
            internal readonly Dictionary<ulong,Incoming> Receiving=new Dictionary<ulong,Incoming>();
            internal readonly SortedDictionary<ulong,Descriptor> Descriptions=new SortedDictionary<ulong,Descriptor>();
            internal readonly HashSet<ulong> ReceivedSequences=new HashSet<ulong>();
            internal ChannelState(UdpOptions options)
            { MaxSendByte=MaxReceiveByte=(ulong)options.ReceiveBytesPerChannel; MaxSendMessage=MaxReceiveMessage=(ulong)options.ReceiveMessagesPerChannel; }
        }
        private sealed class Outgoing
        {
            internal readonly ulong Id,Offset,First;
            internal byte[] Bytes;
            internal readonly int Count;
            internal int Remaining;
            internal readonly SendReceipt Receipt;
            internal double LastProgress;
            internal Outgoing(ulong id,ulong offset,ulong first,byte[] bytes,int count,SendReceipt receipt,double now)
            {Id=id;Offset=offset;First=first;Bytes=bytes;Count=Remaining=count;Receipt=receipt;LastProgress=now;}
        }
        private sealed class Fragment
        {
            internal readonly byte Channel;
            internal readonly Outgoing Message;
            internal readonly ushort Index;
            internal ulong Sequence => Message.First+Index;
            internal bool Acked,NeedsSend=true;
            internal int Transmissions;
            internal Fragment(byte channel,Outgoing message,ushort index) {Channel=channel;Message=message;Index=index;}
            internal byte[] Encode(int size)
            {
                int count=Math.Min(size,Message.Bytes.Length-Index*size); var bytes=new byte[32+count];
                BinaryPrimitives.WriteUInt64LittleEndian(bytes,Sequence);BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8),Message.Id);
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16),Message.Offset);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24),(uint)Message.Bytes.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28),Index);BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(30),(ushort)Message.Count);
                Message.Bytes.AsSpan(Index*size,count).CopyTo(bytes.AsSpan(32));return bytes;
            }
        }
        private sealed class Incoming
        {
            internal readonly ulong Id,Offset,First;
            internal readonly byte[] Bytes;
            internal readonly bool[] Seen;
            internal int Remaining;
            internal double LastProgress;
            internal Incoming(ulong id,ulong offset,ulong first,byte[] bytes,int count,double now)
            {Id=id;Offset=offset;First=first;Bytes=bytes;Seen=new bool[count];Remaining=count;LastProgress=now;}
        }
        private sealed class Descriptor
        {
            internal readonly ulong Id,Offset,First,Length,Count;
            internal Descriptor(ulong id,ulong offset,ulong first,uint length,ushort count) {Id=id;Offset=offset;First=first;Length=length;Count=count;}
        }
    }
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FrameSync.Transport;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace FrameSync.Tools.UdpHarness
{
    // 仅演示宿主的开局/验收协议。框架不认识演示名称、角色、结果报告或文件路径。
    internal static class HostMessages
    {
        internal const byte Join=1,Welcome=2,WelcomeAck=3,Result=4,FinishAck=5,Stop=6;
        internal static byte[] JoinMessage(ushort slot)
        {var b=new byte[3];b[0]=Join;BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1),slot);return b;}
        internal static byte[] WelcomeMessage(Guid session,int clients,int frames,int rate,bool manual)
        {
            var b=new byte[27];b[0]=Welcome;session.ToByteArray().CopyTo(b,1);b[17]=(byte)clients;
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(18),frames);BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(22),rate);b[26]=(byte)(manual?1:0);return b;
        }
        internal static byte[] ResultMessage(ulong tick,long total,string hash)
        {var b=new byte[81];b[0]=Result;BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(1),tick);BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(9),total);Encoding.ASCII.GetBytes(hash).CopyTo(b,17);return b;}
    }

    /// <summary>宿主把演示控制消息与会话消息分开消费，避免在已经关闭的会话里丢掉验收结果。</summary>
    internal sealed class HostTransport : ITransport
    {
        private readonly UdpTransport _inner;
        private readonly Queue<TransportMessage> _session=new Queue<TransportMessage>();
        internal readonly Queue<TransportMessage> Controls=new Queue<TransportMessage>();
        internal HostTransport(UdpNode node){_inner=new UdpTransport(node);}
        internal void Pump()
        {
            // 两个宿主队列都有上限。空间不足时停止消费，背压由 UDP 接收额度向对端传播。
            for(int i=0;i<64 && _session.Count<128 && Controls.Count<128 && _inner.TryReceive(out var message);i++)
                (message!.Channel==2 ? Controls : _session).Enqueue(message);
        }
        public int MaxMessageBytes=>_inner.MaxMessageBytes;
        public int ChannelCount=>_inner.ChannelCount;
        public bool Supports(DeliveryMode delivery)=>_inner.Supports(delivery);
        public bool TrySend(ulong connectionId,byte channel,DeliveryMode delivery,ReadOnlySpan<byte> payload)=>_inner.TrySend(connectionId,channel,delivery,payload);
        public bool TryReceive(out TransportMessage? message){message=_session.Count==0?null:_session.Dequeue();return message!=null;}
    }
}

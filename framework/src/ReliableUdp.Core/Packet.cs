using System;
using System.Buffers.Binary;

namespace ReliableUdp
{
    // 这些记录属于传输协议，和游戏逻辑帧无关。v1 每个数据报最多一个记录。
    internal enum Kind : byte { None, Hello, Retry, Echo, Accept, AcceptAck, Fragment, Unreliable, Credit, Ping, Close, CloseAck }

    /// <summary>固定小端序线协议。先验证完整长度和校验，再分配载荷；不反序列化任意对象。</summary>
    internal sealed class Packet
    {
        internal const int HeaderBytes = 80, RecordBytes = 4;
        internal Guid Connection;
        internal ulong Number, Ack;
        internal byte[] AckBits = new byte[32];
        internal Kind Kind;
        internal byte Channel;
        internal byte[] Body = Array.Empty<byte>();
        private static readonly uint[] CrcTable = MakeCrcTable();

        internal byte[] Encode()
        {
            bool record = Kind != Kind.None;
            var bytes = new byte[HeaderBytes + (record ? RecordBytes + Body.Length : 0)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x50445552);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)(Ack == 0 ? 0 : 1));
            Connection.ToByteArray().CopyTo(bytes,8);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24),Number);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32),Ack);
            AckBits.CopyTo(bytes,40);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(72),checked((ushort)(bytes.Length-HeaderBytes)));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(74),(ushort)(record ? 1 : 0));
            if (record)
            {
                bytes[80]=(byte)Kind; bytes[81]=Channel;
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(82),checked((ushort)Body.Length));
                Body.CopyTo(bytes,84);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(76),PacketCrc(bytes));
            return bytes;
        }

        internal static bool TryDecode(ReadOnlySpan<byte> data,int maximum,out Packet? packet)
        {
            packet=null;
            if(data.Length<HeaderBytes || data.Length>maximum || BinaryPrimitives.ReadUInt32LittleEndian(data)!=0x50445552 ||
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4))!=1) return false;
            ushort flags=BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6));
            ushort count=BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(74));
            if(flags>1 || count>1 || BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(72))!=data.Length-HeaderBytes ||
                BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(76))!=PacketCrc(data)) return false;
            ulong ack=BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(32));
            if((flags==0)!=(ack==0)) return false;
            // 位图不允许确认 0 号包，或绕到 uint64 的另一端。
            for(int i=0;i<256;i++) if((data[40+i/8] & (1<<(i%8)))!=0 && (ulong)(i+1)>=ack) return false;
            Kind kind=Kind.None; byte channel=0;
            if(count==0) { if(data.Length!=HeaderBytes) return false; }
            else
            {
                if(data.Length<84 || data[80]==0 || data[80]>(byte)Kind.CloseAck ||
                    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(82))!=data.Length-84) return false;
                kind=(Kind)data[80]; channel=data[81];
            }
            packet=new Packet { Connection=new Guid(data.Slice(8,16)), Number=BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(24)),
                Ack=ack, AckBits=data.Slice(40,32).ToArray(), Kind=kind, Channel=channel,
                Body=count==0 ? Array.Empty<byte>() : data.Slice(84).ToArray() };
            return true;
        }

        // CRC32C 的标准反射多项式。仅查损坏，不提供认证或加密。
        internal static uint Crc(ReadOnlySpan<byte> data)
        { uint crc=uint.MaxValue; foreach(byte b in data) crc=CrcTable[(crc^b)&255]^(crc>>8); return ~crc; }
        private static uint PacketCrc(ReadOnlySpan<byte> data)
        { uint crc=uint.MaxValue; for(int i=0;i<data.Length;i++) crc=CrcTable[(crc^(i>=76 && i<80 ? 0 : data[i]))&255]^(crc>>8); return ~crc; }
        private static uint[] MakeCrcTable()
        {
            var table=new uint[256];
            for(uint i=0;i<256;i++) { uint c=i; for(int b=0;b<8;b++) c=(c&1)!=0 ? (c>>1)^0x82f63b78 : c>>1; table[i]=c; }
            return table;
        }
    }
}

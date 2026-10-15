using System;
using System.Collections.Generic;
using System.Text;
namespace ReliableUdp.Tests
{
    internal static class CodecTests
    {
        internal static IEnumerable<(string, Action)> Cases()
        {
            yield return ("Codec.Crc32CStandardVector", () => Check.Equal(0xe3069283u, Packet.Crc(Encoding.ASCII.GetBytes("123456789"))));
            yield return ("Codec.HeaderAndRoundTrip", () => {
                var p = new Packet { Connection = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), Number = 0x0102030405060708,
                    Ack = 4, Kind = Kind.Unreliable, Channel = 2, Body = new byte[] { 9, 8, 7 } }; p.AckBits[0] = 5;
                byte[] b = p.Encode(); Check.Equal(87, b.Length);
                Check.Bytes(new byte[] { 82,85,68,80,1,0,1,0,51,34,17,0,85,68,119,102 }, b.AsSpan(0,16));
                Check.Bytes(new byte[] { 8,7,6,5,4,3,2,1 }, b.AsSpan(24,8));
                Check.True(Packet.TryDecode(b,1200,out var parsed)); Check.Equal(p.Connection,parsed!.Connection);
                Check.Equal(p.Number,parsed.Number); Check.Equal(4UL,parsed.Ack); Check.Bytes(p.Body,parsed.Body);
            });
            yield return ("Codec.RejectsTruncationCorruptionAndOversize", () => {
                byte[] valid = new Packet { Kind = Kind.Ping, Body = new byte[8] }.Encode();
                for (int i=0;i<valid.Length;i++) Check.True(!Packet.TryDecode(valid.AsSpan(0,i),1200,out _));
                byte[] corrupt=(byte[])valid.Clone(); corrupt[84]^=1; Check.True(!Packet.TryDecode(corrupt,1200,out _));
                Check.True(!Packet.TryDecode(valid,80,out _));
                var random=new Random(73); for(int i=0;i<5000;i++) { var bytes=new byte[random.Next(1300)]; random.NextBytes(bytes); Check.True(!Packet.TryDecode(bytes,1200,out _)); }
            });
        }
    }
}

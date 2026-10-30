using System;
using System.IO;
using System.Security.Cryptography;
using FrameSync.Persistence;

namespace Advanture.HookIsland.Network;

/// <summary>Bounded game envelope. Ownership and the framework checkpoint commit as one file.</summary>
internal static class IslandSaveCodec
{
    private const uint Magic=0x56534948; // HISV
    private const ushort Version=1;
    internal static int MaximumBytes=>checked(SaveCodec.GetMaximumEncodedBytes(RoomProtocol.Limits)+512);
    internal static byte[] Encode(IslandSave save)
    {
        byte[] checkpoint=SaveCodec.Encode(save.Document,RoomProtocol.Limits);
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(Magic);writer.Write(Version);writer.Write(save.SavedAtUtc.Ticks);writer.Write((byte)save.Capacity);
        var profiles=save.Profiles;
        for(int i=0;i<save.Capacity;i++){writer.Write(save.Entered[i]);if(save.Entered[i])writer.Write(profiles[i]);}
        writer.Write(checkpoint.Length);writer.Write(checkpoint);writer.Flush();
        using var hash=SHA256.Create();byte[] digest=hash.ComputeHash(stream.ToArray());writer.Write(digest);writer.Flush();
        byte[] bytes=stream.ToArray();if(bytes.Length>MaximumBytes)throw new InvalidDataException("Game save exceeds its byte budget.");return bytes;
    }
    internal static IslandSave Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<52 || bytes.Length>MaximumBytes)throw new InvalidDataException("Invalid game save length.");
        using var hash=SHA256.Create();byte[] body=bytes.Slice(0,bytes.Length-32).ToArray();
        if(!CryptographicOperations.FixedTimeEquals(hash.ComputeHash(body),bytes.Slice(bytes.Length-32)))throw new InvalidDataException("Game save checksum mismatch.");
        try
        {
            using var stream=new MemoryStream(body,false);using var reader=new BinaryReader(stream);
            if(reader.ReadUInt32()!=Magic || reader.ReadUInt16()!=Version)throw new InvalidDataException("Unsupported game save format.");
            long ticks=reader.ReadInt64();if(ticks<0 || ticks>DateTime.MaxValue.Ticks)throw new InvalidDataException("Invalid save time.");
            int count=reader.ReadByte();if(count<1 || count>4)throw new InvalidDataException("Invalid saved seat count.");
            var profiles=new byte[count][];var entered=new bool[count];
            for(int i=0;i<count;i++)
            {byte flag=reader.ReadByte();if(flag>1)throw new InvalidDataException("Invalid saved ownership flag.");entered[i]=flag==1;profiles[i]=entered[i]?reader.ReadBytes(32):Array.Empty<byte>();}
            int length=reader.ReadInt32();if(length<0 || length>SaveCodec.GetMaximumEncodedBytes(RoomProtocol.Limits) || length!=stream.Length-stream.Position)throw new InvalidDataException("Invalid embedded save length.");
            return new IslandSave(SaveCodec.Decode(reader.ReadBytes(length),RoomProtocol.Limits),new DateTime(ticks,DateTimeKind.Utc),profiles,entered);
        }
        catch(EndOfStreamException e){throw new InvalidDataException("Truncated game save.",e);}
    }
}

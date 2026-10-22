using System;
using System.Buffers.Binary;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Open one landed chest generation. Both successful and failed requests are final.</summary>
public readonly struct TreasureCommand
{
    public const int ByteCount=9;
    public TreasureCommand(int chestId,uint generation,uint requestId)
    {
        if(chestId<1 || chestId>16 || generation==0 || requestId==0)throw new InvalidDataException("Invalid treasure command.");
        ChestId=chestId;Generation=generation;RequestId=requestId;
    }
    public int ChestId { get; }
    public uint Generation { get; }
    public uint RequestId { get; }
    public bool IsEmpty=>RequestId==0;
    public byte[] Encode()
    {
        var b=new byte[ByteCount];b[0]=(byte)ChestId;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(1),Generation);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5),RequestId);return b;
    }
    public static TreasureCommand Decode(ReadOnlySpan<byte> b)
    {
        if(b.Length!=ByteCount)throw new InvalidDataException("Invalid treasure command length.");
        uint generation=BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(1)),request=BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(5));
        if(b[0]==0 && generation==0 && request==0)return default;
        return new TreasureCommand(b[0],generation,request);
    }
}

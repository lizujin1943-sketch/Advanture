using System;
using System.Buffers.Binary;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>一次投掷意图。目标使用世界毫米坐标，重传必须保持原编号、物品和目标。</summary>
public readonly struct GrenadeCommand
{
    public const int ByteCount=16;
    public GrenadeCommand(uint requestId,int itemId,int targetX,int targetZ)
    {RequestId=requestId;ItemId=itemId;TargetX=targetX;TargetZ=targetZ;}
    public uint RequestId {get;}
    public int ItemId {get;}
    public int TargetX {get;}
    public int TargetZ {get;}
    public bool IsEmpty=>RequestId==0;
    public byte[] Encode()
    {
        var bytes=new byte[ByteCount];BinaryPrimitives.WriteUInt32LittleEndian(bytes,RequestId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),ItemId);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),TargetX);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12),TargetZ);Decode(bytes);return bytes;
    }
    public static GrenadeCommand Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length!=ByteCount)throw new InvalidDataException("Invalid grenade command length.");
        var c=new GrenadeCommand(BinaryPrimitives.ReadUInt32LittleEndian(bytes),BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4)),BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8)),BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12)));
        if(c.IsEmpty?(c.ItemId!=0 || c.TargetX!=0 || c.TargetZ!=0):(c.ItemId<=0 || Math.Abs((long)c.TargetX)>50000 || Math.Abs((long)c.TargetZ)>50000))
            throw new InvalidDataException("Invalid grenade command.");
        return c;
    }
}

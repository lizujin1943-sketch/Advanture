using System;
using System.Buffers.Binary;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Set a desired value, never toggle on delivery. Zero bytes encode no request.</summary>
public readonly struct RoomRulesCommand
{
    public const int ByteCount=5;
    public RoomRulesCommand(bool enabled,uint requestId)
    {
        if(requestId==0)throw new InvalidDataException("Room rules require a nonzero request id.");
        Enabled=enabled;RequestId=requestId;
    }
    public bool Enabled { get; }
    public uint RequestId { get; }
    public bool IsEmpty=>RequestId==0;
    public byte[] Encode()
    {
        var bytes=new byte[ByteCount];bytes[0]=IsEmpty?(byte)0:Enabled?(byte)2:(byte)1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1),RequestId);return bytes;
    }
    public static RoomRulesCommand Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length!=ByteCount)throw new InvalidDataException("Invalid room rules command length.");
        uint request=BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(1));
        if(bytes[0]>2 || (bytes[0]==0)!=(request==0))throw new InvalidDataException("Invalid room rules command.");
        return request==0?default:new RoomRulesCommand(bytes[0]==2,request);
    }
}

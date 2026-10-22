using System;
using System.IO;
using System.Buffers.Binary;

namespace Advanture.HookIsland.Simulation;

/// <summary>Hold-to-help is continuous input; paid respawn is a receipt tied to one death generation.</summary>
public readonly struct RevivalCommand
{
    public const int ByteCount=9;
    public RevivalCommand(int rescueSlot=-1,uint respawnRequest=0,uint deathId=0)
    {RescueTarget=(byte)(rescueSlot+1);RespawnRequest=respawnRequest;DeathId=deathId;if(rescueSlot< -1 || rescueSlot>3)throw new InvalidDataException("Invalid rescue target.");Validate();}
    public byte RescueTarget { get; }
    public int RescueSlot=>RescueTarget-1;
    public uint RespawnRequest { get; }
    public uint DeathId { get; }
    public bool IsEmpty=>RescueTarget==0 && RespawnRequest==0;
    public void Validate()
    {
        if(RescueTarget>4 || (RespawnRequest==0)!=(DeathId==0) || (RescueTarget!=0 && RespawnRequest!=0))
            throw new InvalidDataException("Invalid revival command.");
    }
    public byte[] Encode()
    {Validate();var b=new byte[ByteCount];b[0]=RescueTarget;BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(1),RespawnRequest);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(5),DeathId);return b;}
    public static RevivalCommand Decode(ReadOnlySpan<byte> b)
    {if(b.Length!=ByteCount)throw new InvalidDataException("Invalid revival command length.");return new RevivalCommand(b[0]-1,BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(1)),BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(5)));}
}

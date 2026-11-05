using System;
using System.Buffers.Binary;
using System.IO;

namespace Advanture.HookIsland.Simulation;

public enum TownAction : byte { None, BuyAmmo, BuyRifle, Heal, BuyRing, BuyGrenade, Contract, Sell, RevealMap, ShortcutOne, ShortcutTwo, RegisterInn, ReadArchive, ReadClues, ReadyBoss, ReturnToHarbor, EmergencyAmmo, RepairBand, RestorePower, OpenTideGate }
public enum TownResult : byte { None, Success, Disabled, TooFar, Busy, InsufficientCoins, NoSpace, AlreadyCompleted, GoalLocked, PartyNotReady, InvalidTarget, Cooldown, Unsafe, LimitReached }

/// <summary>One immutable service intention. Failures and successes both consume the request id.</summary>
public readonly struct TownCommand
{
    public const int ByteCount=13;
    public TownCommand(TownAction action,uint requestId,int target=0,uint attempt=0)
    {Action=action;RequestId=requestId;Target=target;Attempt=attempt;}
    public TownAction Action {get;}
    public uint RequestId {get;}
    public int Target {get;}
    public uint Attempt {get;}
    public bool IsEmpty=>Action==TownAction.None;
    public byte[] Encode()
    {var b=new byte[ByteCount];b[0]=(byte)Action;BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(1),RequestId);BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(5),Target);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(9),Attempt);Decode(b);return b;}
    public static TownCommand Decode(ReadOnlySpan<byte> b)
    {
        if(b.Length!=ByteCount || b[0]>(byte)TownAction.OpenTideGate)throw new InvalidDataException("Invalid town command.");
        var c=new TownCommand((TownAction)b[0],BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(1)),BinaryPrimitives.ReadInt32LittleEndian(b.Slice(5)),BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(9)));
        if(c.IsEmpty!=(c.RequestId==0) || c.Target<0 || c.IsEmpty && (c.Target!=0 || c.Attempt!=0) || c.Action!=TownAction.RepairBand && c.Attempt!=0 || c.Action!=TownAction.RepairBand && c.Action!=TownAction.Sell && c.Action!=TownAction.BuyAmmo && c.Target!=0 || c.Action==TownAction.RepairBand && c.Target>3)
            throw new InvalidDataException("Invalid town command fields.");
        return c;
    }
}

using System;
using System.IO;

namespace Advanture.HookIsland.Simulation;

public enum HookGameplayEventKind : byte
{
    CoinsReceived = 1, MaterialReceived, EquipmentReceived, Damaged, HookCast,
    FishCaught, PlayerHooked, ComboIncreased, Upgraded, MeleeSwing, MeleeHit, FoodConsumed
}

/// <summary>One committed result, independent of engine objects and persistent game state.
/// Amount is actual income/damage (overflow on the ground is not income). Value is the
/// resulting combo/upgrade level. TargetId identifies the fish, hooked player or item;
/// melee hits use a player slot, -1 for a piranha and -2 for the monster. X/Z are world millimetres.
/// </summary>
public readonly struct HookGameplayEvent
{
    public HookGameplayEvent(HookGameplayEventKind kind, ushort slot, int amount = 0, int value = 0,
        int targetId = -1, int x = 0, int z = 0, ItemKind itemKind = default, int itemType = 0)
    { Kind=kind;Slot=slot;Amount=amount;Value=value;TargetId=targetId;X=x;Z=z;ItemKind=itemKind;ItemType=itemType; }
    public HookGameplayEventKind Kind { get; }
    public ushort Slot { get; }
    public int Amount { get; }
    public int Value { get; }
    public int TargetId { get; }
    public int X { get; }
    public int Z { get; }
    public ItemKind ItemKind { get; }
    public int ItemType { get; }
}

/// <summary>Versioned event payload only; does not participate in checkpoint/save encoding.</summary>
public static class HookGameplayEventCodec
{
    public const int ByteCount = 29;
    public static byte[] Encode(HookGameplayEvent value)
    {
        Validate(value);
        using var stream=new MemoryStream(ByteCount);using var writer=new BinaryWriter(stream);
        writer.Write((byte)1);writer.Write((byte)value.Kind);writer.Write(value.Slot);
        writer.Write(value.Amount);writer.Write(value.Value);writer.Write(value.TargetId);
        writer.Write(value.X);writer.Write(value.Z);writer.Write((byte)value.ItemKind);writer.Write(value.ItemType);
        return stream.ToArray();
    }
    public static HookGameplayEvent Decode(ReadOnlySpan<byte> payload)
    {
        if(payload.Length!=ByteCount)throw new InvalidDataException("Invalid gameplay event length.");
        using var reader=new BinaryReader(new MemoryStream(payload.ToArray(),false));
        if(reader.ReadByte()!=1)throw new InvalidDataException("Unsupported gameplay event version.");
        var value=new HookGameplayEvent((HookGameplayEventKind)reader.ReadByte(),reader.ReadUInt16(),
            reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32(),
            (ItemKind)reader.ReadByte(),reader.ReadInt32());
        Validate(value);return value;
    }
    private static void Validate(HookGameplayEvent value)
    {
        if(value.Kind<HookGameplayEventKind.CoinsReceived || value.Kind>HookGameplayEventKind.FoodConsumed ||
            value.Slot>=4 || value.Amount<0 || value.Value<0 || value.ItemKind>ItemKind.Food || value.ItemType<0)
            throw new InvalidDataException("Invalid gameplay event.");
    }
}

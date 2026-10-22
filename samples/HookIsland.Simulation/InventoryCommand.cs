using System;
using System.Buffers.Binary;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>用户的一次库存操作；不包含坐标快照、金币或物品内容，客户端不能自行生成物品。</summary>
public enum InventoryAction : byte { None, Move, Split, Drop, Pickup, PickupAndEquip, Equip, Unequip, Buy, BuyAndEquip }

/// <summary>
/// 固定 24 字节的确定性游戏输入。请求编号在本地排队时分配，直到收到确认回执前保持不变。
/// 购买的 Target 是货架索引并带 Batch；卸装的 Target 是槽位；其他操作使用唯一物品 ID。
/// </summary>
public readonly struct InventoryCommand
{
    public const int ByteCount = 24;
    public InventoryCommand(InventoryAction action, int target, int amount = 0, int x = 0, int y = 0,
        bool rotated = false, bool dropOld = false, uint batch = 0, uint requestId = 0, int merchantId = 0)
    { Action = action; Target = target; Amount = amount; X = x; Y = y; Rotated = rotated; DropOld = dropOld; Batch = batch; RequestId = requestId; MerchantId = merchantId; }
    public InventoryAction Action { get; } // 操作类型。
    public int Target { get; } // 物品 ID、槽位或货架索引。
    public int Amount { get; } // 拆分／丢弃数量。
    public int X { get; } // 目标列。
    public int Y { get; } // 目标行。
    public bool Rotated { get; } // 目标朝向，不是“切换朝向”的相对指令。
    public bool DropOld { get; } // 明确允许将被替换装备放到平台。
    public uint Batch { get; } // 购买所见的商品批次。
    public int MerchantId { get; } // 购买的指定商人；其他操作为零。
    public uint RequestId { get; } // 本玩家的操作编号。
    public InventoryCommand WithRequest(uint requestId) => new InventoryCommand(Action, Target, Amount, X, Y, Rotated, DropOld, Batch, requestId, MerchantId);
    public byte[] Encode()
    {
        Validate(); var bytes = new byte[ByteCount]; bytes[0] = (byte)Action; bytes[1] = (byte)((Rotated ? 1 : 0) | (DropOld ? 2 : 0));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2), RequestId); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6), Target);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), Amount); bytes[14] = (byte)X; bytes[15] = (byte)Y;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), Batch); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), MerchantId); return bytes;
    }
    public static InventoryCommand Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteCount || bytes[1] > 3) throw new InvalidDataException("Invalid inventory command length/flags.");
        var result = new InventoryCommand((InventoryAction)bytes[0], BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(6)),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(10)), bytes[14], bytes[15], (bytes[1] & 1) != 0, (bytes[1] & 2) != 0,
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(16)), BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(2)), BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20)));
        result.Validate(); return result;
    }
    public void Validate()
    {
        if (Action > InventoryAction.BuyAndEquip || (Action == InventoryAction.None) != (RequestId == 0)) Invalid();
        bool buying = Action == InventoryAction.Buy || Action == InventoryAction.BuyAndEquip;
        if (buying ? (MerchantId < 0 || MerchantId > 4) : MerchantId != 0) Invalid();
        bool grid = Action == InventoryAction.Move || Action == InventoryAction.Split;
        bool quantity = Action == InventoryAction.Split || Action == InventoryAction.Drop;
        bool equipping = Action == InventoryAction.Equip || Action == InventoryAction.PickupAndEquip || Action == InventoryAction.BuyAndEquip;
        if (buying ? (Target < 0 || Target >= 8 || Batch == 0) : Batch != 0) Invalid();
        if (Action == InventoryAction.None && (Target != 0 || Amount != 0 || X != 0 || Y != 0 || Rotated || DropOld || Batch != 0)) Invalid();
        if (Action != InventoryAction.None && !buying && Target <= 0) Invalid();
        if (Action == InventoryAction.Unequip && (Target < (int)EquipmentSlot.Hat || Target > (int)EquipmentSlot.SwimRing)) Invalid();
        if (grid ? (X < 0 || X > 7 || Y < 0 || Y > 8) : (X != 0 || Y != 0)) Invalid();
        if (quantity ? (Amount < 1 || Amount > InventoryCatalog.MaterialStackLimit || (Action == InventoryAction.Split && Amount == InventoryCatalog.MaterialStackLimit)) : Amount != 0) Invalid();
        if (Rotated && Action != InventoryAction.Move || DropOld && !equipping) Invalid();
    }
    private static void Invalid() => throw new InvalidDataException("Invalid inventory command fields.");
}

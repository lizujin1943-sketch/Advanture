using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>库存的二进制快照和完整性校验。网络输入格式独立，界面不能直接导入未经校验的库存。</summary>
public static class InventoryStateCodec
{
    public const int MaximumBytes = 262144;
    public static byte[] Encode(InventoryState state)
    {
        Validate(state); using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        Write(writer, state); return stream.ToArray();
    }
    public static InventoryState Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24 || bytes.Length > MaximumBytes) throw new InvalidDataException("Invalid inventory length.");
        using var reader = new BinaryReader(new MemoryStream(bytes.ToArray(), false)); var result = Read(reader);
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing inventory bytes.");
        return result;
    }
    internal static void Write(BinaryWriter w, InventoryState state)
    {
        w.Write(0x31474142); w.Write(state.PlayerCount); w.Write(state.NextId);
        foreach (int spent in state.SpentCoins) w.Write(spent);
        w.Write(state.Items.Count);
        foreach (var item in state.Items.OrderBy(i => i.Id))
        {
            w.Write(item.Id); w.Write((byte)item.Kind); w.Write(item.Type); w.Write(item.Count); w.Write(item.Owner);
            w.Write((byte)item.Location); w.Write(item.X); w.Write(item.Y); w.Write(item.Rotated); w.Write(item.WorldX); w.Write(item.WorldZ);
        }
    }
    internal static InventoryState Read(BinaryReader r)
    {
        if (r.ReadInt32() != 0x31474142) throw new InvalidDataException("Inventory version mismatch.");
        int players = r.ReadInt32(), nextId = r.ReadInt32();
        if (players < 1 || players > 4) throw new InvalidDataException("Invalid inventory participants.");
        var spent = new int[players]; for (int i = 0; i < players; i++) spent[i] = r.ReadInt32();
        int count = r.ReadInt32(); if (count < 0 || count > InventoryCatalog.MaxItems) throw new InvalidDataException("Invalid inventory count.");
        var items = new InventoryItem[count]; int previousId = 0;
        for (int i = 0; i < count; i++)
        {
            var item = new InventoryItem { Id = r.ReadInt32(), Kind = (ItemKind)r.ReadByte(), Type = r.ReadInt32(), Count = r.ReadInt32(), Owner = r.ReadInt32(),
                Location = (ItemLocation)r.ReadByte(), X = r.ReadInt32(), Y = r.ReadInt32() };
            byte rotated = r.ReadByte(); if (rotated > 1 || item.Id <= previousId) throw new InvalidDataException("Noncanonical inventory item.");
            item.Rotated = rotated != 0; item.WorldX = r.ReadInt32(); item.WorldZ = r.ReadInt32(); items[i] = item; previousId = item.Id;
        }
        var state = new InventoryState(players, items, nextId, spent); Validate(state); return state;
    }
    public static void Validate(InventoryState state)
    {
        if (state.PlayerCount < 1 || state.PlayerCount > 4 || state.SpentCoins.Count != state.PlayerCount || state.SpentCoins.Any(v => v < 0) ||
            state.NextId < 1 || state.Items.Count > InventoryCatalog.MaxItems) throw new InvalidDataException("Invalid inventory header.");
        var groundMaterials = new HashSet<(ItemKind, int, int)>();
        var ids = new HashSet<int>(); var slots = new HashSet<(int, EquipmentSlot)>(); int gearCount = 0;
        foreach (var item in state.Items)
        {
            if (item.Id < 1 || item.Id >= state.NextId || !ids.Add(item.Id) || !InventoryCatalog.IsValid(item.Kind, item.Type) ||
                item.Count < 1 || item.Location > ItemLocation.Ground || (item.Rotated && item.Definition.Width == item.Definition.Height))
                throw new InvalidDataException("Invalid inventory item.");
            if (!InventoryCatalog.IsMaterial(item.Kind)) { gearCount++; if (item.Count != 1) throw new InvalidDataException("Stacked equipment."); }
            if (item.Location == ItemLocation.Ground)
            {
                if (item.Owner != -1 || item.X != -1 || item.Y != -1 || item.Rotated || Math.Abs((long)item.WorldX) > 50000 || Math.Abs((long)item.WorldZ) > 50000)
                    throw new InvalidDataException("Invalid ground item.");
                if (InventoryCatalog.IsMaterial(item.Kind) && !groundMaterials.Add((item.Kind, PlatformRules.CellAt(item.WorldX), PlatformRules.CellAt(item.WorldZ))))
                    throw new InvalidDataException("Multiple material packages in the same platform cell.");
                continue;
            }
            if (item.Owner < 0 || item.Owner >= state.PlayerCount || item.WorldX != 0 || item.WorldZ != 0 || item.Count > item.Definition.StackLimit)
                throw new InvalidDataException("Invalid inventory ownership or stack.");
            if (item.Location == ItemLocation.Equipped && (item.Definition.Slot == EquipmentSlot.None || item.X != -1 || item.Y != -1 || item.Rotated || !slots.Add((item.Owner, item.Definition.Slot))))
                throw new InvalidDataException("Invalid equipment slot.");
        }
        if (gearCount > InventoryCatalog.MaxEquipmentItems) throw new InvalidDataException("Too much equipment.");
        foreach (var item in state.Items.Where(i => i.Location == ItemLocation.Backpack))
            if (!InventoryLayout.Fits(state.Size(item.Owner), item, item.X, item.Y, item.Rotated) || !InventoryLayout.Free(state.Items, item.Owner, item, item.X, item.Y, item.Rotated))
                throw new InvalidDataException("Invalid backpack layout.");
    }
}

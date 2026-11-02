using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>同一实例只能处于一种位置；穿戴与地面物品不占背包网格。</summary>
public enum ItemLocation : byte { Backpack, Equipped, Ground }

/// <summary>事务回执。失败也会被上层按请求编号记住，重传不能变成一次新的操作。</summary>
public enum InventoryResult : byte
{
    None, Success, Disabled, NotOwned, InvalidItem, InvalidQuantity, InvalidPlacement,
    NoSpace, TooFar, InsufficientCoins, InventoryNotEmpty, HookBusy, StaleStock, SoldOut, LimitReached, LayoutTooComplex, HealthFull, CoolingDown
}

/// <summary>
/// 物品实例。材料 Count 是堆叠数量，装备固定为 1；Type 是对应目录中的编号。
/// Backpack 使用 X/Y 格子坐标；Equipped 和 Ground 的 X/Y 为 -1。
/// Ground 的 Owner 为 -1、WorldX/WorldZ 为毫米坐标，全员可主动拾取。
/// </summary>
public struct InventoryItem
{
    public int Id; // 房间内唯一实例编号，转交与丢弃不更换装备 ID。
    public int Type; // 该物品种类下的目录编号；不是唯一实例 ID。
    public int Count; // 材料堆叠数量；装备固定为 1。
    public int Owner; // 角色槽位；地面物品为 -1。
    public int X, Y; // 背包左上角的格子坐标；穿戴／地面为 -1。
    public int WorldX, WorldZ; // 仅地面物品使用的毫米坐标。
    public ItemKind Kind;
    public ItemLocation Location;
    public bool Rotated;
    public InventoryDefinition Definition => InventoryCatalog.Get(Kind, Type);
    public int Width => Rotated ? Definition.Height : Definition.Width;
    public int Height => Rotated ? Definition.Width : Definition.Height;
}

/// <summary>
/// 可保存和回滚的独立库存快照。构造时复制集合；Unity 只能读快照，不能直接改权威库存。
/// SpentCoins 是每个玩家的累计购买支出，不随物品转交而转移，也不是共享钱包。
/// </summary>
public sealed class InventoryState
{
    public InventoryState(int players, IEnumerable<InventoryItem> items, int nextId, IEnumerable<int> spentCoins, IEnumerable<int>? consumedFoods=null, IEnumerable<QuickbarBinding>? quickbar=null)
    {
        PlayerCount = players; Items = Array.AsReadOnly(items.ToArray()); NextId = nextId;
        SpentCoins = Array.AsReadOnly(spentCoins.ToArray());
        ConsumedFoods=Array.AsReadOnly(consumedFoods?.ToArray()??new int[FoodCatalog.All.Count]);
        Quickbar=Array.AsReadOnly(quickbar?.ToArray()??QuickbarRules.Defaults(players,Items));
    }
    public IReadOnlyList<int> ConsumedFoods {get;} // Global consumed quantities retain purchase-ledger conservation across trades.
    public IReadOnlyList<QuickbarBinding> Quickbar {get;}
    public int PlayerCount { get; }
    public IReadOnlyList<InventoryItem> Items { get; }
    public int NextId { get; } // 下次分配的实例 ID，已消耗的编号不复用。
    public IReadOnlyList<int> SpentCoins { get; } // 按角色槽位保存累计支出，用于个人钱包校验。
    public InventoryItem? Equipped(int owner, EquipmentSlot slot)
    {
        foreach (var item in Items)
            if (item.Owner == owner && item.Location == ItemLocation.Equipped && item.Definition.Slot == slot) return item;
        return null;
    }
    public InventorySize Size(int owner)
    {
        var bag = Equipped(owner, EquipmentSlot.Backpack);
        return bag.HasValue ? InventoryCatalog.Backpack(bag.Value.Type).Size : default;
    }
    public int Balance(int owner, ItemKind kind) => Items.Where(i => i.Owner == owner && i.Kind == kind && i.Location == ItemLocation.Backpack).Sum(i => i.Count);
}

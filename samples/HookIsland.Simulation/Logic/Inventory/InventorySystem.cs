using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public readonly struct MaterialCredit
{
    public MaterialCredit(int owner,ItemKind kind,int amount,Point ground)
    {Owner=owner;Kind=kind;Amount=amount;Ground=ground;}
    public int Owner { get; }
    public ItemKind Kind { get; }
    public int Amount { get; }
    public Point Ground { get; }
}

/// <summary>
/// 个人库存的事务边界。先在候选集合检查数量、空间和归属，成功才替换实际状态。
/// 这里不采样输入、不查商店距离、不改耐力；调用方负责动作时机、商品批次和回执去重。
/// </summary>
public sealed class InventorySystem
{
    // A transient simulation sink; never captured, restored or exposed as mutable game state.
    internal Action<HookGameplayEvent>? Events { get; set; }
    private readonly int playerCount;
    private List<InventoryItem> items;
    private int nextId;
    private int[] spent;

    public InventorySystem(int players,bool starterPan=false)
    {
        if (players < 1 || players > 4) throw new ArgumentOutOfRangeException(nameof(players));
        playerCount = players; items = new List<InventoryItem>(); nextId = 1; spent = new int[players];
        // 先给所有角色发初始竿，便于保持原有竿 ID，再免费装备每人的 Lv1 背包。
        for (int owner = 0; owner < players; owner++) items.Add(EquippedItem(nextId++, owner, ItemKind.Rod, 0));
        for (int owner = 0; owner < players; owner++) items.Add(EquippedItem(nextId++, owner, ItemKind.Backpack, 1));
        if(starterPan)for(int owner=0;owner<players;owner++)items.Add(new InventoryItem{Id=nextId++,Owner=owner,Kind=ItemKind.Weapon,Type=0,Count=1,Location=ItemLocation.Backpack});
    }
    public InventorySystem(InventoryState state)
    {
        InventoryStateCodec.Validate(state);
        playerCount = state.PlayerCount; items = state.Items.ToList(); nextId = state.NextId; spent = state.SpentCoins.ToArray();
    }
    public InventoryState Capture() => new InventoryState(playerCount, items.OrderBy(i => i.Id), nextId, spent);
    /// <summary>按槽位读取当前穿戴项；避免玩法每帧为查询单件装备创建完整快照。</summary>
    public InventoryItem? Equipped(int owner, EquipmentSlot slot)
    {
        var item = items.FirstOrDefault(i => i.Owner == owner && i.Location == ItemLocation.Equipped && i.Definition.Slot == slot);
        return item.Id == 0 ? (InventoryItem?)null : item;
    }
    public InventoryItem? Find(int id)
    { var item = items.FirstOrDefault(i => i.Id == id); return item.Id == 0 ? (InventoryItem?)null : item; }
    public int Balance(int owner, ItemKind kind) => items.Where(i => i.Owner == owner && i.Kind == kind && i.Location == ItemLocation.Backpack).Sum(i => i.Count);

    /// <summary>收获材料：先补同类堆叠，再找空格；放不下的部分合入平台材料包。返回实际入包量；达到实例／数量上限时返回 -1，整笔收获不提交。</summary>
    public int CreditMaterial(int owner, ItemKind kind, int amount, Point ground)
    {
        if (!OwnerExists(owner) || !InventoryCatalog.IsMaterial(kind) || amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var draft = Draft(); int remaining = PutMaterial(draft, owner, kind, amount);
        if (remaining > 0 && !AddGroundMaterial(draft, kind, remaining, ground)) return -1;
        Commit(draft); Received(owner,kind,0,amount-remaining,0,ground); return amount - remaining;
    }
    /// <summary>One catch may reward several backpacks. Any failed credit discards the entire draft.</summary>
    public bool CreditMaterials(IReadOnlyList<MaterialCredit> credits)
    {
        var draft=Draft();var received=new List<HookGameplayEvent>();
        foreach(var credit in credits)
        {
            if(!OwnerExists(credit.Owner) || !InventoryCatalog.IsMaterial(credit.Kind) || credit.Amount<0)
                throw new ArgumentOutOfRangeException(nameof(credits));
            if(credit.Amount==0)continue;
            int remaining=PutMaterial(draft,credit.Owner,credit.Kind,credit.Amount);
            if(remaining>0 && !AddGroundMaterial(draft,credit.Kind,remaining,credit.Ground))return false;
            if(credit.Amount>remaining)received.Add(new HookGameplayEvent(HookGameplayEventKind.MaterialReceived,(ushort)credit.Owner,
                amount:credit.Amount-remaining,x:credit.Ground.X,z:credit.Ground.Z,itemKind:credit.Kind));
        }
        Commit(draft);foreach(var value in received)Events?.Invoke(value);return true;
    }
    /// <summary>只消费本人背包，按 ID 顺序跨堆叠扣除；不足时整个操作保持原状。</summary>
    public bool TrySpendMaterial(int owner, ItemKind kind, int amount)
    {
        if (!OwnerExists(owner) || !InventoryCatalog.IsMaterial(kind) || amount <= 0) return false;
        if (Balance(owner, kind) < amount) return false;
        var draft = Draft(); int remaining = amount;
        foreach (var original in draft.Items.Where(i => i.Owner == owner && i.Kind == kind && i.Location == ItemLocation.Backpack).OrderBy(i => i.Id).ToArray())
        {
            var item = original; int taken = Math.Min(remaining, item.Count); item.Count -= taken; remaining -= taken;
            ReplaceOrRemove(draft.Items, item); if (remaining == 0) break;
        }
        Commit(draft); return true;
    }
    /// <summary>移动／旋转整个物品；把材料拖到同类堆叠上会合并，放不下的余量留在原格。</summary>
    public InventoryResult Move(int owner, int id, int x, int y, bool rotated)
    {
        if (!TryOwned(owner, id, out var item)) return InventoryResult.NotOwned;
        if (item.Location != ItemLocation.Backpack) return InventoryResult.InvalidItem;
        if (!InventoryLayout.Fits(Size(items, owner), item, x, y, rotated)) return InventoryResult.InvalidPlacement;
        var draft = Draft();
        if (InventoryCatalog.IsMaterial(item.Kind))
        {
            int targetIndex = draft.Items.FindIndex(i => i.Owner == owner && i.Id != id && i.Location == ItemLocation.Backpack && i.X == x && i.Y == y && i.Kind == item.Kind);
            if (targetIndex >= 0)
            {
                var target = draft.Items[targetIndex]; int amount = Math.Min(item.Count, InventoryCatalog.MaterialStackLimit - target.Count);
                if (amount == 0) return InventoryResult.NoSpace;
                target.Count += amount; item.Count -= amount; draft.Items[targetIndex] = target; ReplaceOrRemove(draft.Items, item);
                Commit(draft); return InventoryResult.Success;
            }
        }
        if (!InventoryLayout.Free(draft.Items, owner, item, x, y, rotated)) return InventoryResult.NoSpace;
        item.X = x; item.Y = y; item.Rotated = rotated && item.Definition.Width != item.Definition.Height;
        ReplaceOrRemove(draft.Items, item); Commit(draft); return InventoryResult.Success;
    }
    /// <summary>将指定数量拆到空格；完整堆叠使用移动，零数量及超量不生成物品。</summary>
    public InventoryResult Split(int owner, int id, int amount, int x, int y)
    {
        if (!TryOwned(owner, id, out var item)) return InventoryResult.NotOwned;
        if (item.Location != ItemLocation.Backpack || !InventoryCatalog.IsMaterial(item.Kind)) return InventoryResult.InvalidItem;
        if (amount <= 0 || amount >= item.Count) return InventoryResult.InvalidQuantity;
        if (!InventoryLayout.Fits(Size(items, owner), item, x, y, false)) return InventoryResult.InvalidPlacement;
        var draft = Draft(); var split = item;
        if (!TryAllocate(draft, out split.Id)) return InventoryResult.LimitReached;
        split.Count = amount;
        if (!InventoryLayout.Free(draft.Items, owner, split, x, y, false)) return InventoryResult.NoSpace;
        split.X = x; split.Y = y; split.Rotated = false; item.Count -= amount;
        ReplaceOrRemove(draft.Items, item); draft.Items.Add(split); Commit(draft); return InventoryResult.Success;
    }
    /// <summary>主动放下选定数量。装备实例保留 ID；材料在同一地块按种类合包，不会定时消失。</summary>
    public InventoryResult Drop(int owner, int id, int amount, Point ground)
    {
        if (!TryOwned(owner, id, out var item)) return InventoryResult.NotOwned;
        if (item.Location != ItemLocation.Backpack) return InventoryResult.InvalidItem;
        if (amount <= 0 || amount > item.Count) return InventoryResult.InvalidQuantity;
        var draft = Draft();
        if (InventoryCatalog.IsMaterial(item.Kind))
        {
            item.Count -= amount; ReplaceOrRemove(draft.Items, item);
            if (!AddGroundMaterial(draft, item.Kind, amount, ground)) return InventoryResult.LimitReached;
        }
        else { ToGround(ref item, ground); ReplaceOrRemove(draft.Items, item); }
        Commit(draft); return InventoryResult.Success;
    }
    /// <summary>主动拾取，最多搬走可容纳部分。地面包不属于原收获者，任意附近玩家都可拿取。</summary>
    public InventoryResult Pickup(int owner, int id, Point playerPosition, out int taken)
    {
        taken = 0; var check = GroundItem(owner, id, playerPosition, out var item);
        if (check != InventoryResult.Success) return check;
        var draft = Draft();
        if (InventoryCatalog.IsMaterial(item.Kind))
        {
            int remaining = PutMaterial(draft, owner, item.Kind, item.Count); taken = item.Count - remaining;
            if (taken == 0) return CanAllocate(draft) ? InventoryResult.NoSpace : InventoryResult.LimitReached;
            item.Count = remaining; ReplaceOrRemove(draft.Items, item);
        }
        else
        {
            if (!InventoryLayout.Place(draft.Items, owner, Size(draft.Items, owner), ref item)) return InventoryResult.NoSpace;
            ReplaceOrRemove(draft.Items, item); taken = 1;
        }
        Commit(draft); Received(owner,item.Kind,item.Type,taken,id,playerPosition); return InventoryResult.Success;
    }
    /// <summary>直接拾取并穿戴，允许没有背包的角色重新捡起地上的背包；仍完整检查旧装备去处。</summary>
    public InventoryResult PickupAndEquip(int owner, int id, Point playerPosition, bool dropOld)
    {
        var check = GroundItem(owner, id, playerPosition, out var item); if (check != InventoryResult.Success) return check;
        if (InventoryCatalog.IsMaterial(item.Kind)) return InventoryResult.InvalidItem;
        var draft = Draft(); item.Owner = owner; ReplaceOrRemove(draft.Items, item);
        var result = Equip(draft, owner, item, dropOld, playerPosition);
        if (result == InventoryResult.Success) { Commit(draft); Received(owner,item.Kind,item.Type,1,id,playerPosition); } return result;
    }
    public InventoryResult Equip(int owner, int id, bool dropOld, Point ground)
    {
        if (!TryOwned(owner, id, out var item)) return InventoryResult.NotOwned;
        if (InventoryCatalog.IsMaterial(item.Kind)) return InventoryResult.InvalidItem;
        if (item.Location == ItemLocation.Equipped) return InventoryResult.Success;
        var draft = Draft(); var result = Equip(draft, owner, item, dropOld, ground);
        if (result == InventoryResult.Success) Commit(draft); return result;
    }
    /// <summary>背包只能在清空后明确放地上；卸下钓竿则先检查当前背包能否容纳。</summary>
    public InventoryResult Unequip(int owner, EquipmentSlot slot, Point ground)
    {
        if (!OwnerExists(owner)) return InventoryResult.NotOwned;
        var found = Equipped(items, owner, slot); if (!found.HasValue) return InventoryResult.Success;
        var item = found.Value; var draft = Draft();
        if (slot == EquipmentSlot.Backpack)
        {
            if (draft.Items.Any(i => i.Owner == owner && i.Location == ItemLocation.Backpack)) return InventoryResult.InventoryNotEmpty;
            ToGround(ref item, ground);
        }
        else if (!InventoryLayout.Place(draft.Items, owner, Size(draft.Items, owner), ref item)) return InventoryResult.NoSpace;
        ReplaceOrRemove(draft.Items, item); Commit(draft); return InventoryResult.Success;
    }
    /// <summary>
    /// 商品事务的库存部分。商店先核验商品及批次；此方法只扣传入的购买者个人钱包。
    /// 库存完整落位后才扣钱、累计个人支出。失败不占用 ID，也不消耗货架商品。
    /// </summary>
    public InventoryResult Purchase(int owner, ItemKind kind, int type, bool equip, bool dropOld, Point ground, ref int personalCoins)
    {
        if (!OwnerExists(owner)) return InventoryResult.NotOwned;
        if (!InventoryCatalog.IsValid(kind, type) || InventoryCatalog.IsMaterial(kind) || (kind == ItemKind.Rod && type == 0)) return InventoryResult.InvalidItem;
        int price = InventoryCatalog.Get(kind, type).Price;
        if (personalCoins < price) return InventoryResult.InsufficientCoins;
        if (items.Count(i => !InventoryCatalog.IsMaterial(i.Kind)) >= InventoryCatalog.MaxEquipmentItems || nextId == int.MaxValue || spent[owner] > int.MaxValue - price)
            return InventoryResult.LimitReached;
        var draft = Draft(); if (!TryAllocate(draft, out int id)) return InventoryResult.LimitReached;
        var incoming = new InventoryItem { Id = id, Owner = owner, Kind = kind, Type = type, Count = 1, X = -1, Y = -1 };
        InventoryResult result;
        if (equip)
        {
            draft.Items.Add(incoming); result = Equip(draft, owner, incoming, dropOld, ground);
        }
        else
        {
            result = InventoryLayout.Place(draft.Items, owner, Size(draft.Items, owner), ref incoming) ? InventoryResult.Success : InventoryResult.NoSpace;
            if (result == InventoryResult.Success) draft.Items.Add(incoming);
        }
        if (result != InventoryResult.Success) return result;
        draft.Spent[owner] += price; Commit(draft); personalCoins -= price;
        Received(owner,kind,type,1,id,ground); return InventoryResult.Success;
    }

    private void Received(int owner,ItemKind kind,int type,int amount,int id,Point point)
    {
        if(amount>0)Events?.Invoke(new HookGameplayEvent(InventoryCatalog.IsMaterial(kind)?HookGameplayEventKind.MaterialReceived:HookGameplayEventKind.EquipmentReceived,
            (ushort)owner,amount:amount,targetId:id,x:point.X,z:point.Z,itemKind:kind,itemType:type));
    }

    private static InventoryResult Equip(Candidate draft, int owner, InventoryItem incoming, bool dropOld, Point ground)
    {
        var slot = incoming.Definition.Slot; var old = Equipped(draft.Items, owner, slot);
        draft.Items.RemoveAll(i => i.Id == incoming.Id);
        if (old.HasValue)
        {
            var outgoing = old.Value; draft.Items.RemoveAll(i => i.Id == outgoing.Id);
            if (dropOld) ToGround(ref outgoing, ground);
            else { outgoing.Location = ItemLocation.Backpack; outgoing.X = outgoing.Y = -1; outgoing.Rotated = false; }
            draft.Items.Add(outgoing);
        }
        incoming.Location = ItemLocation.Equipped; incoming.Owner = owner; incoming.X = incoming.Y = -1;
        incoming.WorldX = incoming.WorldZ = 0; incoming.Rotated = false; draft.Items.Add(incoming);
        if (slot == EquipmentSlot.Backpack) return InventoryLayout.Repack(draft.Items, owner, InventoryCatalog.Backpack(incoming.Type).Size);
        if (old.HasValue && !dropOld)
        {
            int index = draft.Items.FindIndex(i => i.Id == old.Value.Id); var outgoing = draft.Items[index];
            if (!InventoryLayout.Place(draft.Items, owner, Size(draft.Items, owner), ref outgoing)) return InventoryResult.NoSpace;
            draft.Items[index] = outgoing;
        }
        return InventoryResult.Success;
    }
    private static int PutMaterial(Candidate draft, int owner, ItemKind kind, int remaining)
    {
        foreach (var original in draft.Items.Where(i => i.Owner == owner && i.Kind == kind && i.Location == ItemLocation.Backpack).OrderBy(i => i.Id).ToArray())
        {
            var item = original; int amount = Math.Min(remaining, InventoryCatalog.MaterialStackLimit - item.Count);
            item.Count += amount; remaining -= amount; ReplaceOrRemove(draft.Items, item);
            if (remaining == 0) return 0;
        }
        var size = Size(draft.Items, owner);
        while (remaining > 0)
        {
            var item = new InventoryItem { Id = draft.NextId, Owner = owner, Kind = kind, Count = Math.Min(remaining, InventoryCatalog.MaterialStackLimit) };
            if (!InventoryLayout.Place(draft.Items, owner, size, ref item)) break;
            if (!TryAllocate(draft, out item.Id)) break;
            draft.Items.Add(item); remaining -= item.Count;
        }
        return remaining;
    }
    private static bool AddGroundMaterial(Candidate draft, ItemKind kind, int amount, Point ground)
    {
        // 以平台地块作为合并单元，防止每帧在不同毫米位置收获时生成无限小材料包。
        int x = Cell(ground.X), z = Cell(ground.Z);
        int index = draft.Items.FindIndex(i => i.Location == ItemLocation.Ground && i.Kind == kind && Cell(i.WorldX) == x && Cell(i.WorldZ) == z);
        if (index >= 0)
        {
            var existing = draft.Items[index]; if (existing.Count > int.MaxValue - amount) return false;
            existing.Count += amount; draft.Items[index] = existing;
        }
        else
        {
            if (!TryAllocate(draft, out int id)) return false;
            var item = new InventoryItem { Id = id, Kind = kind, Count = amount };
            ToGround(ref item, ground); draft.Items.Add(item);
        }
        return true;
    }
    private static int Cell(int coordinate)
    {
        long shifted = (long)coordinate + PlatformRules.HalfTile;
        return (int)((shifted >= 0 ? shifted : shifted - PlatformRules.TileSize + 1) / PlatformRules.TileSize);
    }
    private InventoryResult GroundItem(int owner, int id, Point position, out InventoryItem item)
    {
        item = default; if (!OwnerExists(owner)) return InventoryResult.NotOwned;
        int index = items.FindIndex(i => i.Id == id && i.Location == ItemLocation.Ground);
        if (index < 0) return InventoryResult.InvalidItem;
        item = items[index];
        return IslandMap.DistanceSquared(position.X, position.Z, item.WorldX, item.WorldZ) <= (long)InventoryCatalog.PickupDistance * InventoryCatalog.PickupDistance
            ? InventoryResult.Success : InventoryResult.TooFar;
    }
    private bool OwnerExists(int owner) => owner >= 0 && owner < playerCount;
    private bool TryOwned(int owner, int id, out InventoryItem item)
    {
        int index = items.FindIndex(i => i.Id == id && i.Owner == owner && i.Location != ItemLocation.Ground);
        item = index >= 0 ? items[index] : default; return OwnerExists(owner) && index >= 0;
    }
    private static InventoryItem EquippedItem(int id, int owner, ItemKind kind, int type) =>
        new InventoryItem { Id = id, Owner = owner, Kind = kind, Type = type, Count = 1, Location = ItemLocation.Equipped, X = -1, Y = -1 };
    private static InventoryItem? Equipped(IReadOnlyList<InventoryItem> list, int owner, EquipmentSlot slot)
    {
        foreach (var item in list) if (item.Owner == owner && item.Location == ItemLocation.Equipped && item.Definition.Slot == slot) return item;
        return null;
    }
    private static InventorySize Size(IReadOnlyList<InventoryItem> list, int owner)
    { var bag = Equipped(list, owner, EquipmentSlot.Backpack); return bag.HasValue ? InventoryCatalog.Backpack(bag.Value.Type).Size : default; }
    private static void ToGround(ref InventoryItem item, Point where)
    { item.Owner = -1; item.Location = ItemLocation.Ground; item.X = item.Y = -1; item.WorldX = where.X; item.WorldZ = where.Z; item.Rotated = false; }
    private static void ReplaceOrRemove(List<InventoryItem> list, InventoryItem item)
    { int index = list.FindIndex(i => i.Id == item.Id); if (item.Count == 0) list.RemoveAt(index); else list[index] = item; }
    // ID 永不回绕或复用；耗尽后仍可合堆、移动、消费与换装，只拒绝需要新实例的事务。
    private static bool CanAllocate(Candidate draft) => draft.NextId < int.MaxValue && draft.Items.Count < InventoryCatalog.MaxItems;
    private static bool TryAllocate(Candidate draft, out int id)
    {
        id = 0; if (!CanAllocate(draft)) return false;
        id = draft.NextId++; return true;
    }
    private Candidate Draft() => new Candidate(items.ToList(), nextId, (int[])spent.Clone());
    private void Commit(Candidate draft)
    {
        if (draft.Items.Count > InventoryCatalog.MaxItems) throw new InvalidOperationException("Inventory item limit exceeded.");
        items = draft.Items; nextId = draft.NextId; spent = draft.Spent;
    }
    private sealed class Candidate
    {
        internal Candidate(List<InventoryItem> items, int nextId, int[] spent) { Items = items; NextId = nextId; Spent = spent; }
        internal readonly List<InventoryItem> Items;
        internal int NextId;
        internal readonly int[] Spent;
    }
}

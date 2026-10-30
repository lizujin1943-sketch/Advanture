using System;

namespace Advanture.HookIsland.Simulation;

/// <summary>
/// 捕鱼、浮木与建造使用的个人材料入口。正式平台以格子库存为准；Fisher 上的数量只是显示汇总。
/// 未启用装备系统的旧岛屿仍使用原计数规则，不能在两种存储中同时扣料。
/// </summary>
public static class PersonalMaterialInventory
{
    private static ItemKind Kind(TileKind material) => material switch {
        TileKind.Wood => ItemKind.Wood,
        TileKind.Fishbone => ItemKind.Fishbone,
        _ => throw new ArgumentOutOfRangeException(nameof(material))
    };
    public static int Balance(Fisher player, TileKind material, InventorySystem? inventory = null)
    {
        var kind = Kind(material);
        return inventory != null ? inventory.Balance(player.Slot, kind) : material == TileKind.Wood ? player.Wood : player.Fishbones;
    }
    /// <summary>库存操作后刷新界面兼容字段，不改变历史收获量。</summary>
    public static void Refresh(ref Fisher player, InventorySystem inventory)
    {
        player.Wood = inventory.Balance(player.Slot, ItemKind.Wood);
        player.Fishbones = inventory.Balance(player.Slot, ItemKind.Fishbone);
    }
    public static bool TrySpend(ref Fisher player, TileKind material, int amount, InventorySystem? inventory = null)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var kind = Kind(material);
        if (inventory != null)
        {
            if (!inventory.TrySpendMaterial(player.Slot, kind, amount)) return false;
            Refresh(ref player, inventory); return true;
        }
        if (Balance(player, material) < amount) return false;
        if (material == TileKind.Wood) player.Wood -= amount; else player.Fishbones -= amount;
        return true;
    }
    /// <summary>
    /// 收获统计只记一次；满包溢出仍是已挣得材料，拾取它时不再累计收获或金币。
    /// 若账本或实例容量达到上限则返回 false，所有材料和统计保持原状；调用方不得消耗对应鱼／浮木。
    /// </summary>
    public static bool Credit(ref Fisher player, TileKind material, int amount, InventorySystem? inventory = null)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var kind = Kind(material);
        // 所有可能溢出的累计值先算好，不能先发材料再发现收获账本无法更新。
        int previous = material == TileKind.Wood ? player.WoodEarned : player.FishbonesEarned;
        if (previous > int.MaxValue - amount) return false;
        int earned = previous + amount;
        if (inventory != null)
        {
            if (inventory.CreditMaterial(player.Slot, kind, amount, new Point(player.X, player.Z)) < 0) return false;
            Refresh(ref player, inventory);
        }
        else
        {
            int oldBalance = Balance(player, material); if (oldBalance > int.MaxValue - amount) return false;
            int balance = oldBalance + amount;
            if (material == TileKind.Wood) player.Wood = balance; else player.Fishbones = balance;
        }
        if (material == TileKind.Wood) player.WoodEarned = earned; else player.FishbonesEarned = earned;
        return true;
    }
}

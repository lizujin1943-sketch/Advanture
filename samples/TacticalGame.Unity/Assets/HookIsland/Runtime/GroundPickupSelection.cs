using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>本地拾取选择规则；只返回可见条目及操作意图，最终距离和空间由模拟重新检查。</summary>
    internal static class GroundPickupSelection
    {
        internal static InventoryItem[] Nearby(InventoryState state, Fisher player)
        {
            bool bagless = !state.Equipped(player.Slot, EquipmentSlot.Backpack).HasValue;
            // 无包时优先恢复容量，避免重叠的旧竿挡住地上的包；列表保留全部物品供翻页选择。
            return state.Items.Where(i => i.Location == ItemLocation.Ground &&
                IslandMap.DistanceSquared(player.X, player.Z, i.WorldX, i.WorldZ) <= (long)InventoryCatalog.PickupDistance * InventoryCatalog.PickupDistance)
                .OrderBy(i => bagless && i.Kind == ItemKind.Backpack ? 0 : 1)
                .ThenBy(i => IslandMap.DistanceSquared(player.X, player.Z, i.WorldX, i.WorldZ)).ThenBy(i => i.Id).ToArray();
        }
        internal static InventoryCommand Command(InventoryState state, Fisher player, InventoryItem item)
        {
            bool wear = item.Kind == ItemKind.Backpack && !state.Equipped(player.Slot, EquipmentSlot.Backpack).HasValue;
            return new InventoryCommand(wear ? InventoryAction.PickupAndEquip : InventoryAction.Pickup, item.Id);
        }
    }
}

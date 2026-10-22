using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>
/// 只负责矩形占格。换包先保留已有布局，必要时按固定顺序重排；不会修改真实库存。
/// 搜索有确定性上限，过于复杂时明确返回 LayoutTooComplex，玩家可先手动整理。
/// </summary>
internal static class InventoryLayout
{
    internal static bool Fits(InventorySize size, InventoryItem item, int x, int y, bool rotated)
    {
        var d = item.Definition; int w = rotated ? d.Height : d.Width, h = rotated ? d.Width : d.Height;
        // 使用减法检查边界，避免恶意坐标 int.MaxValue 在加法中溢出。
        return x >= 0 && y >= 0 && w <= size.Width && h <= size.Height && x <= size.Width - w && y <= size.Height - h;
    }
    internal static bool Free(IReadOnlyList<InventoryItem> items, int owner, InventoryItem item, int x, int y, bool rotated)
    {
        var d = item.Definition; var rect = new GridRect(x, y, rotated ? d.Height : d.Width, rotated ? d.Width : d.Height);
        foreach (var other in items)
            if (other.Owner == owner && other.Id != item.Id && other.Location == ItemLocation.Backpack &&
                rect.Overlaps(new GridRect(other.X, other.Y, other.Width, other.Height))) return false;
        return true;
    }
    internal static bool Place(List<InventoryItem> items, int owner, InventorySize size, ref InventoryItem item)
    {
        var d = item.Definition;
        for (int turn = 0; turn < (d.Width == d.Height ? 1 : 2); turn++)
            for (int y = 0; y < size.Height; y++) for (int x = 0; x < size.Width; x++)
                if (Fits(size, item, x, y, turn != 0) && Free(items, owner, item, x, y, turn != 0))
                {
                    item.Owner = owner; item.Location = ItemLocation.Backpack; item.X = x; item.Y = y;
                    item.Rotated = turn != 0; item.WorldX = item.WorldZ = 0; return true;
                }
        return false;
    }
    internal static InventoryResult Repack(List<InventoryItem> items, int owner, InventorySize size)
    {
        var stored = items.Where(i => i.Owner == owner && i.Location == ItemLocation.Backpack).ToArray();
        if (stored.Sum(i => i.Width * i.Height) > size.Width * size.Height) return InventoryResult.NoSpace;
        if (stored.All(i => Fits(size, i, i.X, i.Y, i.Rotated) && Free(items, owner, i, i.X, i.Y, i.Rotated))) return InventoryResult.Success;
        // 先放大件；相同尺寸按 ID 排列，1×1 材料最后填空，避免无意义的排列组合。
        var order = stored.OrderByDescending(i => i.Width * i.Height).ThenByDescending(i => Math.Max(i.Width, i.Height)).ThenBy(i => i.Id).ToArray();
        var occupied = new bool[size.Width * size.Height]; int budget = 200000;
        bool Search(int at)
        {
            if (at == order.Length) return true;
            if (--budget < 0) return false;
            var item = order[at]; var d = item.Definition;
            int start = 0;
            if (at > 0 && d.Width == order[at - 1].Definition.Width && d.Height == order[at - 1].Definition.Height)
                start = order[at - 1].Y * size.Width + order[at - 1].X;
            for (int cell = start; cell < occupied.Length; cell++)
                for (int turn = 0; turn < (d.Width == d.Height ? 1 : 2); turn++)
                {
                    int x = cell % size.Width, y = cell / size.Width, w = turn == 0 ? d.Width : d.Height, h = turn == 0 ? d.Height : d.Width;
                    if (x + w > size.Width || y + h > size.Height) continue;
                    bool free = true;
                    for (int yy = y; yy < y + h && free; yy++) for (int xx = x; xx < x + w; xx++) if (occupied[yy * size.Width + xx]) { free = false; break; }
                    if (!free) continue;
                    for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) occupied[yy * size.Width + xx] = true;
                    item.X = x; item.Y = y; item.Rotated = turn != 0; order[at] = item;
                    if (Search(at + 1)) return true;
                    for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) occupied[yy * size.Width + xx] = false;
                    if (budget < 0) return false;
                }
            return false;
        }
        if (!Search(0)) return budget < 0 ? InventoryResult.LayoutTooComplex : InventoryResult.NoSpace;
        foreach (var item in order) items[items.FindIndex(i => i.Id == item.Id)] = item;
        return InventoryResult.Success;
    }
}

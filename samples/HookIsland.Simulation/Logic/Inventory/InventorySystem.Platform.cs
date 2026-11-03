using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed partial class InventorySystem
{
    /// <summary>地板消失时，原物品移至最近的剩余格；材料合包保留总量，装备实例保持原 ID。</summary>
    internal void ReconcileGround(IReadOnlyList<PlatformTile> tiles)
    {
        var falling=items.Where(i=>i.Location==ItemLocation.Ground && !PlatformRules.CanStand(tiles,i.WorldX,i.WorldZ)).OrderBy(i=>i.Id).ToArray();
        foreach(var original in falling)
        {
            int index=items.FindIndex(i=>i.Id==original.Id);if(index<0)continue;var item=items[index];bool placed=false;
            foreach(var tile in tiles.OrderBy(t=>IslandMap.DistanceSquared(item.WorldX,item.WorldZ,t.X*2000,t.Z*2000)).ThenBy(t=>t.Z).ThenBy(t=>t.X))
            {
                int other=InventoryCatalog.IsMaterial(item.Kind)?items.FindIndex(i=>i.Id!=item.Id && i.Kind==item.Kind && i.Location==ItemLocation.Ground && PlatformRules.CellAt(i.WorldX)==tile.X && PlatformRules.CellAt(i.WorldZ)==tile.Z):-1;
                if(other>=0)
                {
                    var target=items[other];int moved=Math.Min(item.Count,int.MaxValue-target.Count);target.Count+=moved;item.Count-=moved;items[other]=target;
                    if(item.Count>0)continue;items.RemoveAt(index);placed=true;break;
                }
                item.WorldX=tile.X*2000;item.WorldZ=tile.Z*2000;items[index]=item;placed=true;break;
            }
            // 主平台的九格不会损毁；合法全局收入最多四个 int，剩余材料容积始终足够。
            if(!placed)throw new InvalidOperationException("No legal ground package destination.");
        }
    }
}

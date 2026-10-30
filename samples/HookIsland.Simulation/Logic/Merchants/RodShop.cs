using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>同一批货对所有玩家一致；尺寸统一取物品表，售出状态不影响下一批的随机结果。</summary>
internal static class RodShop
{
    internal const int Width=8, Height=6, Capacity=8;
    private static readonly int[][] Chances={new[]{100,0,0,0,0},new[]{60,40,0,0,0},
        new[]{15,55,30,0,0},new[]{5,15,50,30,0},new[]{0,5,20,45,30}};

    internal static List<RodStock> Generate(FishingConfig config,uint batch,int merchantId=0)
    {
        var stock=new List<RodStock>();var occupied=new List<GridRect>();
        int level=MerchantRules.Level(config,merchantId);
        uint random=FishCatalog.Mix(unchecked(((uint)config.Seed+batch*2654435761u)^((uint)merchantId*2246822519u)));
        // 初期先放四支 T1 竿，再保底 Lv2 背包；最宽的四支 2x4 竿仍为 2x2 包留有空间。
        for(int n=0;n<(level==1?4:1);n++)
            Add(ItemKind.Rod,ChooseRod(Tier(),ref random));
        int bagTier=level==1?1:Tier();
        Add(ItemKind.Backpack,level==1?2:bagTier*2-(int)(Next()%2));
        Add(ItemKind.SwimRing,level==1?1:Tier()*2-(int)(Next()%2));
        if(config.Melee.Enabled)Add(ItemKind.Weapon,0);
        // 保留四人初期钓具供应后轮换衣装；优先款放不下时尝试其他尺寸。
        int firstApparel=(int)((batch+1UL)%(ulong)ApparelCatalog.All.Count);
        for(int n=0;n<ApparelCatalog.All.Count;n++)
            if(Add(ItemKind.Apparel,(firstApparel+n)%ApparelCatalog.All.Count))break;
        for(int attempt=0;attempt<64 && stock.Count<Capacity;attempt++)
        {
            // 剩余货位按竿／包／圈／衣装 40:20:20:20 混排。
            int roll=(int)(Next()%5),tier=Tier();
            var kind=roll<2?ItemKind.Rod:roll==2?ItemKind.Backpack:roll==3?ItemKind.SwimRing:ItemKind.Apparel;
            Add(kind,kind==ItemKind.Rod?ChooseRod(tier,ref random):kind==ItemKind.Apparel?
                (int)(Next()%(uint)ApparelCatalog.All.Count):tier*2-(int)(Next()%2));
        }
        return stock;
        uint Next(){random=FishCatalog.Mix(random);return random;}
        int Tier()
        {
            int roll=(int)(Next()%100),tier=1;
            foreach(int chance in Chances[level-1]){if(roll<chance)break;roll-=chance;tier++;}
            return tier;
        }
        bool Add(ItemKind kind,int type)
        {
            var definition=InventoryCatalog.Get(kind,type);
            if(stock.Count>=Capacity || !GridInventory.TryPlace(Width,Height,definition.Width,definition.Height,occupied,false,out var place,out _))return false;
            occupied.Add(place);stock.Add(new RodStock{Kind=kind,Type=type,X=place.X,Y=place.Y});
            return true;
        }
    }
    private static int ChooseRod(int tier,ref uint random)
    {
        var candidates=Enumerable.Range(1,RodCatalog.All.Count-1).Where(i=>RodCatalog.Get(i).Tier==tier).ToArray();
        random=FishCatalog.Mix(random);return candidates[random%(uint)candidates.Length];
    }
}

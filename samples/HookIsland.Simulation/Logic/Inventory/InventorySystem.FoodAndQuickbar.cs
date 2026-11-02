using System;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed partial class InventorySystem
{
    private InventoryResult PurchaseFood(int owner,int type,bool equip,Point ground,ref int coins)
    {
        if(equip)return InventoryResult.InvalidItem;
        int price=FoodCatalog.Get(type).Price;
        if(spent[owner]>int.MaxValue-price)return InventoryResult.LimitReached;
        var draft=Draft();
        if(PutStack(draft,owner,ItemKind.Food,type,1)!=0)return CanAllocate(draft)?InventoryResult.NoSpace:InventoryResult.LimitReached;
        draft.Spent[owner]+=price;Commit(draft);coins-=price;
        Received(owner,ItemKind.Food,type,1,0,ground);return InventoryResult.Success;
    }
    internal InventoryResult ConsumeFood(int owner,int id)
    {
        if(!TryOwned(owner,id,out var item))return InventoryResult.NotOwned;
        if(item.Location!=ItemLocation.Backpack || item.Kind!=ItemKind.Food)return InventoryResult.InvalidItem;
        if(consumedFoods[item.Type]==int.MaxValue)return InventoryResult.LimitReached;
        var draft=Draft();item.Count--;ReplaceOrRemove(draft.Items,item);Commit(draft);
        consumedFoods[item.Type]++;return InventoryResult.Success;
    }
    public InventoryResult BindQuickSlot(int owner,int id,int slot)
    {
        if(!OwnerExists(owner) || slot<0 || slot>=QuickbarRules.SlotCount)return InventoryResult.InvalidPlacement;
        if(!TryOwned(owner,id,out var item))return InventoryResult.NotOwned;
        if(!QuickbarRules.CanBind(item))return InventoryResult.InvalidItem;
        var binding=new QuickbarBinding(item.Kind,item.Type,item.Kind==ItemKind.Food?0:item.Id);
        // Move an existing binding instead of silently duplicating it into two slots.
        for(int n=owner*QuickbarRules.SlotCount;n<(owner+1)*QuickbarRules.SlotCount;n++)
            if(quickbar[n].Equals(binding))quickbar[n]=default;
        quickbar[owner*QuickbarRules.SlotCount+slot]=binding;return InventoryResult.Success;
    }
    public InventoryResult ClearQuickSlot(int owner,int slot)
    {
        if(!OwnerExists(owner) || slot<0 || slot>=QuickbarRules.SlotCount)return InventoryResult.InvalidPlacement;
        quickbar[owner*QuickbarRules.SlotCount+slot]=default;return InventoryResult.Success;
    }
    public QuickbarBinding QuickSlot(int owner,int slot)=>quickbar[owner*QuickbarRules.SlotCount+slot];
    public InventoryItem? QuickItem(int owner,int slot)
    {
        var b=QuickSlot(owner,slot);
        var item=items.Where(i=>i.Owner==owner && i.Location!=ItemLocation.Ground &&
            (b.Kind==ItemKind.Food?i.Kind==b.Kind && i.Type==b.Type:i.Id==b.ItemId)).OrderBy(i=>i.Id).FirstOrDefault();
        return item.Id==0?(InventoryItem?)null:item;
    }
}

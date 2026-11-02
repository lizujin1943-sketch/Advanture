using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Equipment references identity; food references a type, surviving stack merges and depletion.</summary>
public readonly struct QuickbarBinding
{
    public QuickbarBinding(ItemKind kind,int type,int itemId=0){Kind=kind;Type=type;ItemId=itemId;}
    public ItemKind Kind {get;}
    public int Type {get;}
    public int ItemId {get;}
    public bool Empty=>Kind==0;
}

public static class QuickbarRules
{
    public const int SlotCount=9;
    public static bool CanBind(InventoryItem item)=>item.Kind==ItemKind.Food || item.Definition.Slot!=EquipmentSlot.None;
    public static InventoryItem? Resolve(InventoryState inventory,int owner,int slot)
    {
        if(owner<0 || owner>=inventory.PlayerCount || slot<0 || slot>=SlotCount)return null;
        var binding=inventory.Quickbar[owner*SlotCount+slot];
        var item=inventory.Items.Where(i=>i.Owner==owner && i.Location!=ItemLocation.Ground &&
            (binding.Kind==ItemKind.Food?i.Kind==ItemKind.Food && i.Type==binding.Type:i.Id==binding.ItemId)).OrderBy(i=>i.Id).FirstOrDefault();
        return item.Id==0?(InventoryItem?)null:item;
    }
    public static int Count(InventoryState inventory,int owner,int slot)
    {
        var binding=inventory.Quickbar[owner*SlotCount+slot];
        return binding.Kind==ItemKind.Food?inventory.Items.Where(i=>i.Owner==owner && i.Kind==ItemKind.Food && i.Type==binding.Type).Sum(i=>i.Count):Resolve(inventory,owner,slot).HasValue?1:0;
    }
    internal static QuickbarBinding[] Defaults(int players,IReadOnlyList<InventoryItem> items)
    {
        var result=new QuickbarBinding[players*SlotCount];
        for(int owner=0;owner<players;owner++)
        {
            foreach(var kind in new[]{ItemKind.Rod,ItemKind.Weapon})
            {
                var item=items.FirstOrDefault(i=>i.Owner==owner && i.Kind==kind && i.Location!=ItemLocation.Ground);
                if(item.Id!=0)result[owner*SlotCount+(kind==ItemKind.Rod?0:1)]=new QuickbarBinding(kind,item.Type,item.Id);
            }
            for(int type=0;type<FoodCatalog.All.Count && type<SlotCount-2;type++)result[owner*SlotCount+2+type]=new QuickbarBinding(ItemKind.Food,type);
        }
        return result;
    }
}

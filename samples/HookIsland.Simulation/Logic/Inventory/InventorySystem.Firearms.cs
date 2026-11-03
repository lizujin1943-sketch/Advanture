using System;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed partial class InventorySystem
{
    private InventoryResult PurchaseAmmunition(int owner,int type,bool equip,Point ground,ref int coins)
    {
        if(equip)return InventoryResult.InvalidItem;
        var ammo=AmmunitionCatalog.Get(type);
        if(spent[owner]>int.MaxValue-ammo.Price || purchasedAmmunition[type]==int.MaxValue)return InventoryResult.LimitReached;
        var draft=Draft();
        if(PutStack(draft,owner,ItemKind.Ammunition,type,ammo.Quantity)!=0)return CanAllocate(draft)?InventoryResult.NoSpace:InventoryResult.LimitReached;
        draft.Spent[owner]+=ammo.Price;Commit(draft);coins-=ammo.Price;purchasedAmmunition[type]++;
        Received(owner,ItemKind.Ammunition,type,ammo.Quantity,0,ground);return InventoryResult.Success;
    }
    public int AmmunitionBalance(int owner,int type)=>items.Where(i=>i.Owner==owner && i.Kind==ItemKind.Ammunition && i.Type==type && i.Location==ItemLocation.Backpack).Sum(i=>i.Count);
    /// <summary>Caller owns action timing and deduplication. A failed shot never consumes ammunition.</summary>
    public bool TryFireRound(int owner,int weaponId)
    {
        if(!TryOwned(owner,weaponId,out var gun) || gun.Kind!=ItemKind.Firearm || gun.Location!=ItemLocation.Equipped || gun.LoadedAmmo==0)return false;
        int type=FirearmCatalog.Get(gun.Type).AmmoType;if(consumedAmmunition[type]==int.MaxValue)return false;
        var draft=Draft();gun.LoadedAmmo--;ReplaceOrRemove(draft.Items,gun);Commit(draft);consumedAmmunition[type]++;return true;
    }
    /// <summary>Refill from unlimited supply at the confirmed completion tick; legacy physical ammo remains untouched.</summary>
    public int ReloadFirearm(int owner,int weaponId)
    {
        if(!TryOwned(owner,weaponId,out var gun) || gun.Kind!=ItemKind.Firearm || gun.Location!=ItemLocation.Equipped)return 0;
        var definition=FirearmCatalog.Get(gun.Type);int amount=definition.Capacity-gun.LoadedAmmo;
        if(amount==0 || replenishedAmmunition[definition.AmmoType]>int.MaxValue-amount)return 0;
        var draft=Draft();gun.LoadedAmmo+=amount;ReplaceOrRemove(draft.Items,gun);Commit(draft);
        replenishedAmmunition[definition.AmmoType]+=amount;return amount;
    }
}

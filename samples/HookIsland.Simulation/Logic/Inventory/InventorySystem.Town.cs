using System;
using System.Linq;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

public sealed partial class InventorySystem
{
    /// <summary>Finite loading: explicit external grant first, then purchased backpack rounds.</summary>
    public int ReloadFinite(int owner,int weaponId,int external,out int externalUsed)
    {
        externalUsed=0;
        if(!TryOwned(owner,weaponId,out var gun) || activeItems[owner]!=weaponId || gun.Kind!=ItemKind.Firearm || gun.Location!=ItemLocation.Equipped)return 0;
        var def=FirearmCatalog.Get(gun.Type);int capacity=def.Capacity-gun.LoadedAmmo;
        int supplied=Math.Min(capacity,Math.Max(0,external));
        if(replenishedAmmunition[def.AmmoType]>int.MaxValue-supplied)return 0;
        var draft=Draft();int taken=supplied;
        foreach(var original in draft.Items.Where(i=>i.Owner==owner && i.Kind==ItemKind.Ammunition && i.Type==def.AmmoType && i.Location==ItemLocation.Backpack).OrderBy(i=>i.Id).ToArray())
        {var item=original;int n=Math.Min(capacity-taken,item.Count);item.Count-=n;taken+=n;ReplaceOrRemove(draft.Items,item);if(taken==capacity)break;}
        if(taken==0)return 0;gun.LoadedAmmo+=taken;ReplaceOrRemove(draft.Items,gun);Commit(draft);
        replenishedAmmunition[def.AmmoType]+=supplied;externalUsed=supplied;return taken;
    }
    public void DiscardLoaded(int owner,int weaponId,int amount)
    {
        if(amount<=0)return;
        if(!TryOwned(owner,weaponId,out var gun) || gun.Kind!=ItemKind.Firearm || amount>gun.LoadedAmmo)throw new InvalidOperationException("Invalid attempt magazine receipt.");
        int type=FirearmCatalog.Get(gun.Type).AmmoType;
        if(consumedAmmunition[type]>int.MaxValue-amount)throw new InvalidOperationException("Ammunition ledger exhausted.");
        var draft=Draft();gun.LoadedAmmo-=amount;ReplaceOrRemove(draft.Items,gun);Commit(draft);consumedAmmunition[type]+=amount;
    }
    /// <summary>Only falling items move; fixed town streets are never interpreted as removable grid tiles.</summary>
    internal void ReconcileTownGround(IReadOnlyList<PlatformTile> tiles)
    {
        for(int i=0;i<items.Count;i++)
        {
            var item=items[i];if(item.Location!=ItemLocation.Ground || TownLayout.CanStand(tiles,item.WorldX,item.WorldZ))continue;
            Point best=TownLayout.Buildings[0].Entrance;long nearest=long.MaxValue;
            // Arena stone is permanent. A deterministic 500mm search preserves the original item id.
            for(int z=TownLayout.CenterZ-25000;z<=TownLayout.CenterZ+25000;z+=500)
                for(int x=TownLayout.CenterX-25000;x<=TownLayout.CenterX+25000;x+=500)
                {
                    long distance=IslandMap.DistanceSquared(item.WorldX,item.WorldZ,x,z);
                    if(distance>=nearest || !TownLayout.CanStand(tiles,x,z))continue;
                    nearest=distance;best=new Point(x,z);
                }
            item.WorldX=best.X;item.WorldZ=best.Z;items[i]=item;
        }
    }
    public bool SellTownEquipment(int owner,int id,int protectedIdLimit,out int fullValue,out int proceeds)
    {
        fullValue=proceeds=0;
        if(!TryOwned(owner,id,out var item) || id<=protectedIdLimit || item.Kind==ItemKind.Ammunition || item.Kind==ItemKind.Food || InventoryCatalog.IsMaterial(item.Kind) || item.Kind==ItemKind.Backpack || item.Definition.Price<2)return false;
        int ammo=-1,capacity=0;
        if(item.Kind==ItemKind.Firearm)
        {
            var gun=FirearmCatalog.Get(item.Type);ammo=gun.AmmoType;capacity=gun.Capacity;
            if(retiredFirearmAmmunition[ammo]>int.MaxValue-capacity || consumedAmmunition[ammo]>int.MaxValue-item.LoadedAmmo)return false;
        }
        fullValue=item.Definition.Price;proceeds=fullValue/2;
        var draft=Draft();draft.Items.RemoveAll(i=>i.Id==id);Commit(draft);
        if(ammo>=0){retiredFirearmAmmunition[ammo]+=capacity;consumedAmmunition[ammo]+=item.LoadedAmmo;}
        return true;
    }
}

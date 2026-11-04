using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.InventoryTests;

internal static partial class Program
{
    private static IEnumerable<(string,Action)> FirearmInventoryCases()
    {
        yield return ("firearm_inventory_reload_refills_only_missing_rounds_and_preserves_trade",()=>{
            var inventory=new InventorySystem(2);int coins=1000;
            Require(inventory.Purchase(0,ItemKind.Firearm,0,true,false,Ground,ref coins)==InventoryResult.Success,"Gun rejected.");
            var gun=inventory.Equipped(0,EquipmentSlot.Sidearm)!.Value;
            Require(!inventory.TryFireRound(1,gun.Id),"Another player fired owned gun.");
            Require(inventory.TryFireRound(0,gun.Id) && inventory.TryFireRound(0,gun.Id),"Loaded gun did not fire.");
            Require(inventory.ReloadFirearm(1,gun.Id)==0,"Another player reloaded owned gun.");
            Require(inventory.Purchase(0,ItemKind.Ammunition,0,false,false,Ground,ref coins)==InventoryResult.Success,"Ammo rejected.");
            Require(inventory.ReloadFirearm(0,gun.Id)==2 && inventory.AmmunitionBalance(0,0)==14 && inventory.Find(gun.Id)!.Value.LoadedAmmo==7,"Reload consumed legacy loose rounds or overfilled magazine.");
            Require(inventory.ReloadFirearm(0,gun.Id)==0,"Full gun consumed ammunition.");
            Require(inventory.TryFireRound(0,gun.Id),"Third shot rejected.");
            Require(inventory.Unequip(0,EquipmentSlot.Sidearm,Ground)==InventoryResult.Success,"Unequip failed.");
            Require(!inventory.TryFireRound(0,gun.Id),"Unequipped gun fired.");
            Require(inventory.Drop(0,gun.Id,1,Ground)==InventoryResult.Success,"Gun drop failed.");
            Require(inventory.PickupAndEquip(1,gun.Id,Ground,false)==InventoryResult.Success,"Gun transfer failed.");
            Require(inventory.Find(gun.Id)!.Value.LoadedAmmo==6,"Trade refilled magazine.");
            Require(inventory.AmmunitionBalance(1,0)==0,"Transfer shared reserve ammunition.");
            var restored=new InventorySystem(InventoryStateCodec.Decode(InventoryStateCodec.Encode(inventory.Capture())));
            Require(restored.Find(gun.Id)!.Value.LoadedAmmo==6 && restored.Capture().ConsumedAmmunition[0]==3 && restored.Capture().PurchasedAmmunition[0]==1 && restored.Capture().ReplenishedAmmunition[0]==2,"Restore lost magazine/ledger.");
            Require(restored.Capture().SpentCoins.SequenceEqual(new[]{330,0}),"Trade moved purchase debt.");
        });
        yield return ("firearm_inventory_unlimited_refills_preserve_legacy_loose_rounds",()=>{
            var inventory=new InventorySystem(1);int coins=1000;
            inventory.Purchase(0,ItemKind.Firearm,0,true,false,Ground,ref coins);inventory.Purchase(0,ItemKind.Ammunition,0,false,false,Ground,ref coins);
            int gun=inventory.Equipped(0,EquipmentSlot.Sidearm)!.Value.Id;
            foreach(int count in new[]{7,5})
            {
                for(int n=0;n<count;n++)Require(inventory.TryFireRound(0,gun),"Shot failed before expected depletion.");
                Require(inventory.ReloadFirearm(0,gun)==count,"Reload discarded partially filled magazine.");
            }
            Require(inventory.AmmunitionBalance(0,0)==14,"Reserve accounting incorrect.");
            for(int n=0;n<4;n++)Require(inventory.TryFireRound(0,gun),"Shot failed.");
            Require(inventory.ReloadFirearm(0,gun)==4 && inventory.Find(gun)!.Value.LoadedAmmo==7 && inventory.AmmunitionBalance(0,0)==14 && inventory.Capture().ReplenishedAmmunition[0]==16,"Unlimited reload changed legacy reserves or replenishment ledger.");
            InventoryStateCodec.Decode(InventoryStateCodec.Encode(inventory.Capture()));
        });
        yield return ("firearm_inventory_empty_fire_and_corrupt_magazines_are_rejected",()=>{
            var inventory=new InventorySystem(1);int coins=300;
            Require(inventory.Purchase(0,ItemKind.Firearm,0,true,false,Ground,ref coins)==InventoryResult.Success,"Gun rejected.");
            int id=inventory.Equipped(0,EquipmentSlot.Sidearm)!.Value.Id;
            for(int n=0;n<7;n++)Require(inventory.TryFireRound(0,id),"Magazine depleted early.");
            var before=InventoryStateCodec.Encode(inventory.Capture());
            Require(!inventory.TryFireRound(0,id),"Empty gun created rounds.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())),"Failed shot changed inventory.");
            var snapshot=inventory.Capture();var items=snapshot.Items.ToArray();int index=Array.FindIndex(items,i=>i.Id==id);items[index].LoadedAmmo=1;
            bool rejected=false;try{InventoryStateCodec.Encode(new InventoryState(1,items,snapshot.NextId,snapshot.SpentCoins,snapshot.ConsumedFoods,snapshot.Quickbar,snapshot.PurchasedAmmunition,snapshot.ConsumedAmmunition,snapshot.ReplenishedAmmunition));}catch(System.IO.InvalidDataException){rejected=true;}
            Require(rejected,"Unaccounted magazine round accepted.");
        });
        yield return ("firearm_inventory_ammunition_stacks_99_and_partial_drop_transfers_rounds",()=>{
            var inventory=new InventorySystem(2);int coins=1000;
            for(int n=0;n<8;n++)Require(inventory.Purchase(0,ItemKind.Ammunition,0,false,false,Ground,ref coins)==InventoryResult.Success,"Pack rejected.");
            var stacks=inventory.Capture().Items.Where(i=>i.Kind==ItemKind.Ammunition).OrderBy(i=>i.Id).ToArray();
            Require(stacks.Select(i=>i.Count).SequenceEqual(new[]{99,13}),"Ammunition did not split at 99.");
            Require(inventory.Drop(0,stacks[0].Id,8,Ground)==InventoryResult.Success,"Partial ammunition drop failed.");
            var drop=inventory.Capture().Items.Single(i=>i.Location==ItemLocation.Ground);
            Require(drop.Count==8 && inventory.AmmunitionBalance(0,0)==104,"Drop moved entire stack.");
            Require(inventory.Pickup(1,drop.Id,Ground,out int taken)==InventoryResult.Success && taken==8 && inventory.AmmunitionBalance(1,0)==8,"Pickup lost reserve rounds.");
            var remaining=inventory.Capture().Items.Single(i=>i.Id==stacks[1].Id);
            Require(inventory.Split(0,remaining.Id,3,3,3)==InventoryResult.Success,"Ammunition split failed.");
            InventoryStateCodec.Decode(InventoryStateCodec.Encode(inventory.Capture()));
        });

        yield return ("firearm_inventory_new_purchase_has_ammo_and_preserves_quickbar",()=>{
            var inventory=new InventorySystem(2);int coins=1000;var before=inventory.Capture().Quickbar.ToArray();
            Require(inventory.Purchase(0,(ItemKind)9,0,true,false,Ground,ref coins)==InventoryResult.Success,"New firearm purchase rejected.");
            var gun=inventory.Equipped(0,EquipmentSlot.Sidearm)!.Value;
            Require(gun.Kind==(ItemKind)9 && gun.Count==1 && gun.LoadedAmmo==7 && coins==700,"Wrong gun/wallet.");
            Require(before.SequenceEqual(inventory.Capture().Quickbar),"Gun overwrote quickbar.");
        });
        yield return ("firearm_inventory_ammunition_full_fit_is_atomic",()=>{
            var inventory=new InventorySystem(1);int coins=1000;
            Require(inventory.Purchase(0,(ItemKind)10,0,false,false,Ground,ref coins)==InventoryResult.Success,"Ammo purchase rejected.");
            inventory.CreditMaterial(0,ItemKind.Wood,(StarterCells-1)*InventoryCatalog.MaterialStackLimit,Ground);
            for(int n=0;n<6;n++)Require(inventory.Purchase(0,(ItemKind)10,0,false,false,Ground,ref coins)==InventoryResult.Success,"Existing ammo stack could not grow.");
            var before=InventoryStateCodec.Encode(inventory.Capture());
            Require(inventory.Purchase(0,(ItemKind)10,0,false,false,Ground,ref coins)==InventoryResult.NoSpace && coins==790,"Partial pack purchase committed.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())),"Failed pack changed state.");
        });
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.InventoryTests;

internal static partial class Program
{
    private static IEnumerable<(string,Action)> WeaponLoadoutCases()
    {
        yield return ("weapon_slots_switch_with_full_backpack_without_moving_items_or_refilling_ammo",()=>{
            // 枚举全部四类武器时，显式准备第四槽的初始手雷。
            var inventory=new InventorySystem(1,true,true,false,starterGrenade:true);int coins=5000;
            var pistol=inventory.ActiveItem(0)!.Value;
            Require(pistol.Kind==ItemKind.Firearm && pistol.Type==0 && !inventory.Capture().Items.Any(i=>i.Kind==ItemKind.Rod || i.Location==ItemLocation.Backpack),"Starting weapons are not independently equipped.");
            Require(inventory.TryFireRound(0,pistol.Id),"Starter pistol cannot fire.");
            inventory.CreditMaterial(0,ItemKind.Wood,StarterCells*InventoryCatalog.MaterialStackLimit,Ground);
            Require(inventory.Purchase(0,ItemKind.Firearm,1,true,false,Ground,ref coins)==InventoryResult.Success,"Empty primary slot incorrectly requires backpack space.");
            var before=inventory.Capture();int primary=inventory.ActiveItem(0)!.Value.Id;
            for(int n=0;n<5;n++)foreach(var slot in WeaponLoadout.Slots)
            {
                Require(inventory.SelectWeapon(0,slot)==InventoryResult.Success,"Full backpack blocked switching.");
                Require(inventory.ActiveItem(0)?.Definition.Slot==slot,"Wrong active category.");
                Require(before.Items.SequenceEqual(inventory.Capture().Items),"Switch moved gear or changed a magazine.");
                if(slot!=EquipmentSlot.PrimaryWeapon)Require(!inventory.TryFireRound(0,primary) && inventory.ReloadFirearm(0,primary)==0,"Stowed weapon could fire or reload.");
            }
            Require(inventory.Find(pistol.Id)!.Value.LoadedAmmo==6 && coins==4100,"Switch refilled ammunition or charged coins.");
            var saved=InventoryStateCodec.Encode(inventory.Capture());
            var restored=new InventorySystem(InventoryStateCodec.Decode(saved));
            Require(saved.SequenceEqual(InventoryStateCodec.Encode(restored.Capture())) && restored.ActiveItem(0)?.Kind==ItemKind.Throwable,"Active selection did not round-trip.");
        });
        yield return ("weapon_same_category_replacement_is_atomic_and_preserves_other_slots",()=>{
            var inventory=new InventorySystem(1,true,true,false);int coins=5000;
            inventory.Purchase(0,ItemKind.Firearm,1,true,false,Ground,ref coins);
            int original=inventory.ActiveItem(0)!.Value.Id;inventory.TryFireRound(0,original);
            inventory.CreditMaterial(0,ItemKind.Wood,StarterCells*InventoryCatalog.MaterialStackLimit,Ground);
            var before=InventoryStateCodec.Encode(inventory.Capture());
            Require(inventory.Purchase(0,ItemKind.Firearm,2,true,false,Ground,ref coins)==InventoryResult.NoSpace && coins==4100,"Failed swap spent coins.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())),"Failed swap changed active weapon, items or ledger.");
            Require(inventory.TrySpendMaterial(0,ItemKind.Wood,InventoryCatalog.MaterialStackLimit),"Could not free one backpack cell.");
            Require(inventory.Purchase(0,ItemKind.Firearm,2,true,false,Ground,ref coins)==InventoryResult.Success,"Replacement failed after space was freed.");
            var state=inventory.Capture();
            Require(state.Items.Single(i=>i.Id==original).Location==ItemLocation.Backpack && state.Items.Single(i=>i.Id==original).LoadedAmmo==29,"Outgoing magazine was lost.");
            Require(state.Equipped(0,EquipmentSlot.PrimaryWeapon)?.Type==2 && state.Equipped(0,EquipmentSlot.Sidearm)?.Type==0 && state.Equipped(0,EquipmentSlot.MeleeWeapon)?.Type==0,"Replacing primary altered other categories.");
            Require(state.Items.Count(i=>i.Location==ItemLocation.Backpack)==StarterCells,"Replacement did not use the single free cell.");
            Require(inventory.Equip(0,original,false,Ground)==InventoryResult.Success,"Full bag could not reuse the incoming weapon's freed cell.");
            state=inventory.Capture();
            Require(state.ActiveItem(0)?.Id==original && state.Items.Single(i=>i.Kind==ItemKind.Firearm && i.Type==2).Location==ItemLocation.Backpack &&
                state.Items.Count(i=>i.Location==ItemLocation.Backpack)==StarterCells,"Full bag swap lost or dropped a weapon.");
            InventoryStateCodec.Decode(InventoryStateCodec.Encode(state));
        });
        yield return ("weapon_empty_slot_and_invalid_owner_do_not_change_active_selection",()=>{
            var inventory=new InventorySystem(1,true,true,false);var before=InventoryStateCodec.Encode(inventory.Capture());
            Require(inventory.SelectWeapon(0,EquipmentSlot.PrimaryWeapon)==InventoryResult.InvalidItem,"Empty slot succeeded.");
            Require(inventory.SelectWeapon(1,EquipmentSlot.Sidearm)==InventoryResult.NotOwned,"Foreign owner succeeded.");
            Require(inventory.SelectWeapon(0,EquipmentSlot.Backpack)==InventoryResult.InvalidItem,"Backpack became a weapon.");
            Require(before.SequenceEqual(InventoryStateCodec.Encode(inventory.Capture())),"Failed selection mutated inventory.");
            inventory.Unequip(0,EquipmentSlot.Sidearm,Ground);
            Require(inventory.ActiveItem(0)?.Kind==ItemKind.Weapon,"Removing held sidearm did not choose remaining melee.");
            inventory.Unequip(0,EquipmentSlot.MeleeWeapon,Ground);
            Require(!inventory.ActiveItem(0).HasValue,"Removing all weapons left an invalid active ID.");
            InventoryStateCodec.Decode(InventoryStateCodec.Encode(inventory.Capture()));
        });
        yield return ("weapon_active_identity_rejects_foreign_stored_and_non_weapon_items",()=>{
            var inventory=new InventorySystem(2,true,true,false);int coins=1000;
            inventory.Purchase(0,ItemKind.Firearm,0,false,false,Ground,ref coins);
            var state=inventory.Capture();
            foreach(int id in new[]{0,-1,state.NextId,state.ActiveItems[1],state.Equipped(0,EquipmentSlot.Backpack)!.Value.Id,state.Items.Single(i=>i.Location==ItemLocation.Backpack).Id})
            {
                bool rejected=false;
                try{InventoryStateCodec.Encode(new InventoryState(2,state.Items,state.NextId,state.SpentCoins,activeItems:new[]{id,state.ActiveItems[1]}));}
                catch(InvalidDataException){rejected=true;}
                Require(rejected,"Invalid active ID was accepted: "+id);
            }
            var active=state.ActiveItems.ToArray();var copy=new InventoryState(2,state.Items,state.NextId,state.SpentCoins,activeItems:active);
            active[0]=0;Require(copy.ActiveItems[0]==state.ActiveItems[0],"Snapshot retained mutable active-ID array.");
        });
        yield return ("weapon_select_command_accepts_only_canonical_category_targets",()=>{
            foreach(var slot in WeaponLoadout.Slots)
            {
                var command=new InventoryCommand(InventoryAction.SelectWeapon,(int)slot,requestId:1);
                Require(InventoryCommand.Decode(command.Encode()).Target==(int)slot,"Weapon slot changed in transit.");
            }
            foreach(int target in new[]{0,1,3,8,(int)EquipmentSlot.Throwable+1,265,int.MaxValue})
            {
                bool rejected=false;try{new InventoryCommand(InventoryAction.SelectWeapon,target,requestId:1).Encode();}catch(InvalidDataException){rejected=true;}
                Require(rejected,"Noncanonical or wrapped weapon slot accepted.");
            }
        });
    }
}

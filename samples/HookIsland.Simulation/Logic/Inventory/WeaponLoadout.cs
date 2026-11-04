using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Weapon categories and active selection; equipped stowed weapons never occupy backpack cells.</summary>
public static class WeaponLoadout
{
    public static IReadOnlyList<EquipmentSlot> Slots {get;}=Array.AsReadOnly(new[]{
        EquipmentSlot.PrimaryWeapon,EquipmentSlot.Sidearm,EquipmentSlot.MeleeWeapon});
    public static bool IsWeaponSlot(EquipmentSlot slot)=>slot==EquipmentSlot.PrimaryWeapon || slot==EquipmentSlot.Sidearm || slot==EquipmentSlot.MeleeWeapon;
    public static bool IsHandSlot(EquipmentSlot slot)=>IsWeaponSlot(slot) || slot==EquipmentSlot.RightHand;
    internal static int Fallback(IReadOnlyList<InventoryItem> items,int owner)
    {
        foreach(var slot in new[]{EquipmentSlot.Sidearm,EquipmentSlot.PrimaryWeapon,EquipmentSlot.MeleeWeapon,EquipmentSlot.RightHand})
            foreach(var item in items)
                if(item.Owner==owner && item.Location==ItemLocation.Equipped && item.Definition.Slot==slot)return item.Id;
        return 0;
    }
    // Shared by the command adapter and authority. Failed requests never cancel an existing reload.
    public static bool CanCancelReload(InventoryState inventory,int owner,InventoryCommand command)
    {
        if(command.DropOld)return false;
        var current=inventory.ActiveItem(owner);
        if(command.Action==InventoryAction.Unequip)return current.HasValue && command.Target==(int)current.Value.Definition.Slot;
        InventoryItem? incoming=null;
        if(command.Action==InventoryAction.SelectWeapon && command.Target>=(int)EquipmentSlot.PrimaryWeapon && command.Target<=(int)EquipmentSlot.MeleeWeapon)
            incoming=inventory.Equipped(owner,(EquipmentSlot)command.Target);
        else if(command.Action==InventoryAction.SelectQuickSlot)incoming=QuickbarRules.Resolve(inventory,owner,command.Target-1);
        else if(command.Action==InventoryAction.Equip)incoming=inventory.Items.Where(i=>i.Id==command.Target && i.Owner==owner && i.Location!=ItemLocation.Ground).Select(i=>(InventoryItem?)i).FirstOrDefault();
        return incoming.HasValue && IsHandSlot(incoming.Value.Definition.Slot) && incoming.Value.Id!=current?.Id;
    }
}

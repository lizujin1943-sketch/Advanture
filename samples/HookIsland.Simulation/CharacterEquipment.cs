using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Read-only bridge between inventory definitions and character attributes. Transactions stay in InventorySystem.</summary>
public static class CharacterEquipment
{
    public static IReadOnlyList<EquipmentSlot> Slots { get; } = Array.AsReadOnly(new[] {
        EquipmentSlot.Hat, EquipmentSlot.LeftHand, EquipmentSlot.RightHand, EquipmentSlot.Clothes,
        EquipmentSlot.Shoes, EquipmentSlot.Backpack, EquipmentSlot.Accessory, EquipmentSlot.SwimRing });

    public static CharacterStats Calculate(CharacterConfig config, int staminaBase, InventoryState inventory, int owner)
    {
        if (owner < 0 || owner >= inventory.PlayerCount) throw new ArgumentOutOfRangeException(nameof(owner));
        return CharacterStats.Calculate(config, staminaBase, inventory.Items
            .Where(i => i.Owner == owner && i.Location == ItemLocation.Equipped).Select(i => i.Definition.Modifiers));
    }
    public static bool CanEquipTo(InventoryItem item, EquipmentSlot slot) =>
        slot != EquipmentSlot.None && item.Definition.Slot == slot;

    /// <summary>Run the same atomic equip rules on an independent candidate, never on live inventory.</summary>
    public static InventoryResult Preview(CharacterConfig config, int staminaBase, InventoryState inventory, int owner,
        InventoryCommand intent, bool hookBusy, bool beingPulled, out CharacterStats stats)
    {
        stats = Calculate(config, staminaBase, inventory, owner);
        bool unequip = intent.Action == InventoryAction.Unequip;
        if (!unequip && intent.Action != InventoryAction.Equip) return InventoryResult.InvalidItem;
        var item = unequip ? inventory.Equipped(owner, (EquipmentSlot)intent.Target) :
            inventory.Items.Where(i => i.Id == intent.Target && i.Owner == owner && i.Location != ItemLocation.Ground)
                .Select(i => (InventoryItem?)i).FirstOrDefault();
        if (!item.HasValue) return InventoryResult.NotOwned;
        if (beingPulled || (hookBusy && item.Value.Definition.Slot == EquipmentSlot.RightHand)) return InventoryResult.HookBusy;
        var draft = new InventorySystem(inventory);
        var result = unequip ? draft.Unequip(owner, (EquipmentSlot)intent.Target, new Point(0, 0)) :
            draft.Equip(owner, intent.Target, intent.DropOld, new Point(0, 0));
        if (result == InventoryResult.Success) stats = Calculate(config, staminaBase, draft.Capture(), owner);
        return result;
    }
}
